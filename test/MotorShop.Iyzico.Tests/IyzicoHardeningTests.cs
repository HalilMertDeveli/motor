using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMigrator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Providers;
using MotorShop.Iyzico.Services;
using NUnit.Framework;
using Smartstore;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Data.Migrations;
using Smartstore.Engine;
using Smartstore.Engine.Modularity;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// Review findings of PR #21: refund in-flight guard, currency allow-list, strict paidPrice, config-only signature
/// checks, return page throttle, void error, Payments.Manual block, unique migration versions.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IyzicoHardeningTests : IyzicoTestBase
{
    #region Refund in-flight guard

    private async Task<(Smartstore.Core.Checkout.Orders.Order Order, IyzicoTransaction Tx)> CreatePaidOrderAsync(decimal total, params (string Id, decimal Paid)[] items)
    {
        var order = await CreateOrderAsync(total);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, items: items);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        await using var db = NewDbContext();
        return (await db.Orders.AsNoTracking().FirstAsync(x => x.Id == order.Id), await db.Set<IyzicoTransaction>().AsNoTracking().FirstAsync(x => x.Id == tx.Id));
    }

    private static List<IyzicoItemTransaction> Items(IyzicoTransaction tx)
        => JsonSerializer.Deserialize<List<IyzicoItemTransaction>>(tx.ItemTransactionsJson);

    private async Task<(IyzicoTransaction Tx, string ConversationId)> CreateUnknownRefundAsync(Smartstore.Core.Checkout.Orders.Order order, IyzicoTransaction tx)
    {
        Api.OnRefund = r => r.PaymentTransactionId == "T1" ? throw new IyzicoApiException("timeout") : Api.RefundSuccess(r);

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 120m, isPartial: true));
        Api.OnRefund = null;

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        return (t, Api.RefundRequests.First(x => x.PaymentTransactionId == "T1").ConversationId);
    }

    [Test]
    public async Task Refund_timeout_marks_item_unknown_and_blocks_further_refunds()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);

        var (t, conversationId) = await CreateUnknownRefundAsync(order, tx);

        var items = Items(t);
        Assert.That(items[0].PendingRefundConversationId, Is.EqualTo(conversationId));
        Assert.That(items[0].PendingRefundAmount, Is.EqualTo(100m));
        Assert.That(items[0].PendingRefundIsCancel, Is.False);
        Assert.That(items[0].RefundedAmount, Is.Zero);
        Assert.That(items[1].HasPendingRefund, Is.False);
        Assert.That(t.RefundedAmount, Is.Zero);
        Assert.That(t.LastErrorCode, Is.EqualTo("REFUND_UNKNOWN"));
        var (_, _, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(notes.Any(x => x.Note.Contains("unknown outcome")), Is.True);

        // Not even a small partial refund of another item is sent while the outcome is unknown.
        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 10m, isPartial: true));
        Assert.That(Api.RefundRequests, Has.Count.EqualTo(1));
        Assert.That(Api.PaymentDetailsCalls, Is.EqualTo(1), "the transaction report is asked before refusing");
    }

    [TestCase("SIGNATURE")]
    [TestCase("AMOUNT")]
    [TestCase("TRANSACTION")]
    [TestCase("PAYMENTID")]
    [TestCase("CONVERSATION")]
    public async Task Unverifiable_refund_answer_is_treated_as_unknown(string tamper)
    {
        var (order, tx) = await CreatePaidOrderAsync(150m);
        Now = Now.AddDays(2);
        Api.OnRefund = r =>
        {
            var response = Api.RefundSuccess(r);
            switch (tamper)
            {
                case "SIGNATURE": response.Signature = new string('0', 64); return response;
                case "AMOUNT": response.Price = 99m; break;
                case "TRANSACTION": response.PaymentTransactionId = "other"; break;
                case "PAYMENTID": response.PaymentId = "other"; break;
                case "CONVERSATION": response.ConversationId = "other"; break;
            }
            // Genuinely signed, but not the answer to this request.
            return Api.SignRefund(response);
        };

        var ex = Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 50m, isPartial: true));
        Assert.That(ex.Message, Does.Contain(tamper));

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.LastErrorCode, Is.EqualTo("REFUND_UNVERIFIED"));
        Assert.That(t.RefundedAmount, Is.Zero);
        Assert.That(Items(t).Single().PendingRefundAmount, Is.EqualTo(50m));
    }

    [Test]
    public async Task Rejected_refund_is_not_unknown_and_can_be_retried()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m);
        Now = Now.AddDays(2);
        Api.OnRefund = _ => new RefundResponse { Status = "failure", ErrorCode = "5085", ErrorMessage = "refund not allowed" };

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 50m, isPartial: true));
        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(Items(t).Single().HasPendingRefund, Is.False);

        Api.OnRefund = null;
        Assert.That(await NewService().RefundAsync(order, 50m, isPartial: true), Is.False);
    }

    [Test]
    public async Task Transaction_report_confirms_unknown_refund_and_unblocks()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);
        var (_, conversationId) = await CreateUnknownRefundAsync(order, tx);

        Api.OnPaymentDetails = paymentId => Report(paymentId, "T1", conversationId, 100m, status: 1);

        // Next refund: the report confirms the first one, then only the rest of the request is sent.
        var full = await NewService().RefundAsync(order, 20m, isPartial: true);

        var (_, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(full, Is.False);
        Assert.That(Api.RefundRequests.Select(x => x.PaymentTransactionId), Is.EqualTo(new[] { "T1", "T2" }));
        Assert.That(Api.RefundRequests.Last().Price, Is.EqualTo("20.0"));
        Assert.That(t.RefundedAmount, Is.EqualTo(120m));
        Assert.That(Items(t).Select(x => x.RefundedAmount), Is.EqualTo(new[] { 100m, 20m }));
        Assert.That(Items(t).Any(x => x.HasPendingRefund), Is.False);
        Assert.That(t.LastErrorCode, Is.Null);
        Assert.That(notes.Any(x => x.Note.Contains("was processed by iyzico")), Is.True);
    }

    [TestCase(2, 100)]
    [TestCase(1, 90)]
    [TestCase(null, 100)]
    public async Task Report_without_clear_success_keeps_the_block(int? status, decimal price)
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);
        var (_, conversationId) = await CreateUnknownRefundAsync(order, tx);
        Api.OnPaymentDetails = paymentId => Report(paymentId, "T1", conversationId, price, status);

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 20m, isPartial: true));

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(Api.RefundRequests, Has.Count.EqualTo(1));
        Assert.That(Items(t)[0].HasPendingRefund, Is.True);
        Assert.That(t.RefundedAmount, Is.Zero);
    }

    [Test]
    public async Task Admin_resolves_unknown_refund_as_not_processed()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);
        var (_, conversationId) = await CreateUnknownRefundAsync(order, tx);

        Assert.That(await NewService().GetPendingRefundsAsync(), Has.Count.EqualTo(1));
        Assert.That(await NewService().ResolvePendingRefundAsync(tx.Id, conversationId, processed: false, "test"), Is.True);
        Assert.That(await NewService().GetPendingRefundsAsync(), Is.Empty);

        // Refundable again: the same 120 is sent with new conversation ids.
        Assert.That(await NewService().RefundAsync(order, 120m, isPartial: true), Is.False);
        var requests = Api.RefundRequests.ToArray();
        Assert.That(requests.Select(x => x.PaymentTransactionId), Is.EqualTo(new[] { "T1", "T1", "T2" }));
        Assert.That(requests[1].ConversationId, Is.Not.EqualTo(conversationId));

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.RefundedAmount, Is.EqualTo(120m));
    }

    [Test]
    public async Task Admin_resolves_unknown_refund_as_processed()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);
        var (_, conversationId) = await CreateUnknownRefundAsync(order, tx);

        Assert.That(await NewService().ResolvePendingRefundAsync(tx.Id, conversationId, processed: true, "test"), Is.True);
        Assert.That(await NewService().ResolvePendingRefundAsync(tx.Id, conversationId, processed: false, "test"), Is.False, "already resolved");

        var (_, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.RefundedAmount, Is.EqualTo(100m));
        Assert.That(notes.Any(x => x.Note.Contains("resolved by test")), Is.True);

        // Only the remaining 50 can still be refunded.
        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 50.01m, isPartial: true));
        Assert.That(await NewService().RefundAsync(order, 0m, isPartial: false), Is.True);
        Assert.That(Api.RefundRequests.Last().Price, Is.EqualTo("50.0"));
    }

    [Test]
    public async Task Cancel_without_answer_does_not_fall_back_to_refund()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Api.OnCancel = _ => throw new IyzicoApiException("timeout");

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 150m, isPartial: false));

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(Api.RefundRequests, Is.Empty);
        Assert.That(t.LastErrorCode, Is.EqualTo("CANCEL_UNKNOWN"));
        Assert.That(Items(t).All(x => x.PendingRefundIsCancel && x.HasPendingRefund), Is.True);
        Assert.That(Items(t).Select(x => x.PendingRefundAmount), Is.EqualTo(new decimal?[] { 100m, 50m }));

        var pending = (await NewService().GetPendingRefundsAsync()).Single();
        Assert.That(pending.IsCancel, Is.True);
        Assert.That(pending.Amount, Is.EqualTo(150m));

        Assert.That(await NewService().ResolvePendingRefundAsync(tx.Id, pending.ConversationId, processed: true, "test"), Is.True);
        (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Cancelled));
        Assert.That(t.RefundedAmount, Is.EqualTo(150m));
    }

    [Test]
    public async Task Reconciliation_task_confirms_unknown_cancel_from_report()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m);
        Api.OnCancel = _ => throw new IyzicoApiException("timeout");
        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 150m, isPartial: false));
        var conversationId = Api.CancelRequests.Single().ConversationId;

        Api.OnPaymentDetails = paymentId => new PaymentDetailsReportResponse
        {
            Status = "success",
            Payments = [new ReportedPayment
            {
                PaymentId = paymentId,
                Cancels = [new ReportedCancel { CancelConversationId = conversationId, RefundPrice = 150m, RefundStatus = 1, CurrencyCode = "TRY" }]
            }]
        };
        Now = Now.AddMinutes(5);

        await NewService().ReconcileAsync();

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Cancelled));
        Assert.That(t.RefundedAmount, Is.EqualTo(150m));
        Assert.That(Items(t).Any(x => x.HasPendingRefund), Is.False);
    }

    [Test]
    public void Refund_signature_follows_documented_field_order()
    {
        var response = new RefundResponse { PaymentId = "24681357", Price = 10.50m, Currency = "TRY", ConversationId = "conv" };

        Assert.That(
            IyzicoSignature.ComputeRefundSignature(SecretKey, response),
            Is.EqualTo(IyzicoSignature.ComputeResponseSignature(SecretKey, "24681357", "10.5", "TRY", "conv")));
    }

    [Test]
    public async Task Failed_void_saves_the_error_code()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: 0);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);
        Api.OnCancel = _ => new CancelResponse { Status = "failure", ErrorCode = "5093", ErrorMessage = "cancel not allowed" };

        Assert.ThrowsAsync<PaymentException>(() => NewService().VoidAsync(order));

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.LastErrorCode, Is.EqualTo("5093"));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.FraudReview));
    }

    private static PaymentDetailsReportResponse Report(string paymentId, string transactionId, string conversationId, decimal price, int? status) => new()
    {
        Status = "success",
        Payments = [new ReportedPayment
        {
            PaymentId = paymentId,
            ItemTransactions = [new ReportedItemTransaction
            {
                PaymentTransactionId = transactionId,
                Refunds = [new ReportedRefund { RefundConversationId = conversationId, RefundPrice = price, RefundStatus = status, CurrencyCode = "TRY" }]
            }]
        }]
    };

    #endregion

    #region Currency allow-list

    [Test]
    public void Allowed_currencies_default_to_try_and_are_limited_to_iyzico_currencies()
    {
        var settings = new IyzicoSettings();
        Assert.That(IyzicoRequestBuilder.GetAllowedCurrencies(settings), Is.EquivalentTo(new[] { "TRY" }));
        Assert.That(IyzicoRequestBuilder.IsCurrencyAllowed(settings, "try"), Is.True);
        Assert.That(IyzicoRequestBuilder.IsCurrencyAllowed(settings, "EUR"), Is.False);
        Assert.That(IyzicoRequestBuilder.IsCurrencyAllowed(settings, null), Is.False);

        settings.AllowedCurrencies = " try, eur ;JPY,,usd";
        Assert.That(IyzicoRequestBuilder.GetAllowedCurrencies(settings), Is.EquivalentTo(new[] { "TRY", "EUR", "USD" }));

        settings.AllowedCurrencies = "";
        Assert.That(IyzicoRequestBuilder.GetAllowedCurrencies(settings), Is.Empty);
    }

    [Test]
    public async Task Start_refuses_eur_unless_configured()
    {
        var order = await CreateOrderAsync(150m, "EUR", o =>
        {
            o.BillingAddress = new Address { FirstName = "Ali", LastName = "Demir", Email = "ali@example.com", Address1 = "Cad. 5", City = "Ankara", CreatedOnUtc = Now };
            o.OrderItems.Add(new Smartstore.Core.Checkout.Orders.OrderItem { ProductId = 1, Quantity = 1, PriceInclTax = 150m, OrderItemGuid = Guid.NewGuid() });
        });

        Assert.That((await NewService().StartPaymentAsync(order, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.UnsupportedCurrency));
        Assert.That(Api.InitializeCalls, Is.Zero);

        Settings.AllowedCurrencies = "TRY,EUR";
        Assert.That((await NewService().StartPaymentAsync(order, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.Redirect));
        Assert.That(Api.LastInitializeRequest.Currency, Is.EqualTo("EUR"));
    }

    [TestCase("TRY", "TRY", false)]
    [TestCase("EUR", "TRY", true)]
    [TestCase("EUR", "TRY,EUR", false)]
    [TestCase("USD", "", true)]
    public async Task Payment_filter_hides_iyzico_for_currencies_not_allowed(string workingCurrency, string allowed, bool excluded)
    {
        Settings.AllowedCurrencies = allowed;
        var filter = CreateIyzicoFilter(workingCurrency);

        Assert.That(await filter.IsExcludedAsync(FilterRequest(IyzicoProvider.SystemName)), Is.EqualTo(excluded));
        Assert.That(await filter.IsExcludedAsync(FilterRequest("Payments.CashOnDelivery")), Is.False, "other methods are not affected");
    }

    private IyzicoPaymentFilter CreateIyzicoFilter(string workingCurrency)
    {
        var workContext = new Mock<IWorkContext>();
        workContext.SetupGet(x => x.WorkingCurrency).Returns(new Currency { CurrencyCode = workingCurrency });

        var settingFactory = new Mock<ISettingFactory>();
        settingFactory.Setup(x => x.LoadSettingsAsync<IyzicoSettings>(It.IsAny<int>())).ReturnsAsync(() => Settings);

        var credentials = new Mock<IIyzicoCredentialProvider>();
        credentials.Setup(x => x.GetCredentialsAsync(It.IsAny<int>())).ReturnsAsync(new IyzicoCredentials(ApiKey, SecretKey, IyzicoCredentialProvider.SandboxBaseUrl));

        // FilterRequest has no cart, so the order minimum check needs neither total calculation nor database.
        return new IyzicoPaymentFilter(
            workContext.Object,
            settingFactory.Object,
            new Lazy<IIyzicoCredentialProvider>(() => credentials.Object),
            new Lazy<IOrderCalculationService>(() => throw new InvalidOperationException("not expected")),
            new Lazy<SmartDbContext>(() => throw new InvalidOperationException("not expected")));
    }

    private static PaymentFilterRequest FilterRequest(string systemName) => new()
    {
        StoreId = 1,
        PaymentProvider = new Provider<IPaymentMethod>(new Lazy<IPaymentMethod, ProviderMetadata>(() => null, new ProviderMetadata { SystemName = systemName }))
    };

    #endregion

    #region Strict paidPrice

    [TestCase(null, 100.00, null)]
    [TestCase(1, 100.00, null)]
    [TestCase(1, 100.004, null)]
    [TestCase(1, 100.01, "PAIDPRICE")]
    [TestCase(null, 100.01, "PAIDPRICE")]
    [TestCase(0, 100.01, "PAIDPRICE")]
    [TestCase(1, 99.99, "PAIDPRICE")]
    [TestCase(3, 100.00, null)]
    [TestCase(3, 115.50, null)]
    [TestCase(3, 130.00, null)]
    [TestCase(3, 130.01, "SURCHARGE")]
    [TestCase(9, 99.99, "PAIDPRICE")]
    public void Paid_price_boundaries(int? installment, double paidPrice, string expectedError)
    {
        var verdict = IyzicoVerifier.CheckPaidPrice(100m, (decimal)paidPrice, installment, 30m);

        Assert.That(verdict?.ErrorCode, Is.EqualTo(expectedError));
        if (verdict != null)
        {
            Assert.That(verdict.Status, Is.EqualTo(IyzicoTransactionStatus.VerificationFailed));
        }
    }

    [Test]
    public void Paid_price_missing_or_zero_surcharge_allowance()
    {
        Assert.That(IyzicoVerifier.CheckPaidPrice(100m, null, 1, 30m)?.ErrorCode, Is.EqualTo("PAIDPRICE"));
        Assert.That(IyzicoVerifier.CheckPaidPrice(100m, 100m, 6, 0m), Is.Null);
        Assert.That(IyzicoVerifier.CheckPaidPrice(100m, 100.01m, 6, 0m)?.ErrorCode, Is.EqualTo("SURCHARGE"));
        Assert.That(IyzicoVerifier.CheckPaidPrice(100m, 100.01m, 6, -5m)?.ErrorCode, Is.EqualTo("SURCHARGE"), "negative allowance counts as 0");
    }

    [Test]
    public async Task Single_payment_with_paid_price_above_amount_is_not_marked_paid()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, installment: 1, paidPrice: 150.01m);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("PAIDPRICE"));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(MarkPaidCalls, Is.Zero);
    }

    [Test]
    public async Task Installment_surcharge_above_configured_maximum_is_not_marked_paid()
    {
        var order = await CreateOrderAsync(1000m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, installment: 6, paidPrice: 1400m);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That((await LoadAsync(order.Id, tx.Token)).Tx.LastErrorCode, Is.EqualTo("SURCHARGE"));

        // A higher configured maximum accepts the same answer for a fresh attempt.
        Settings.MaxInstallmentSurchargePercent = 40m;
        var tx2 = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx2, installment: 6, paidPrice: 1400m);

        Assert.That((await NewService().ProcessTokenAsync(tx2.Token, IyzicoEventSource.Callback)).Outcome, Is.EqualTo(IyzicoOutcome.Paid));
    }

    #endregion

    #region Configuration-only signature checks, return throttle

    [Test]
    public void Signature_checks_can_only_be_relaxed_through_configuration()
    {
        var secure = CreateCredentialProvider(new Dictionary<string, string>());
        Assert.That(secure.SkipResponseSignatureVerification, Is.False);
        Assert.That(secure.AllowUnsignedWebhooks, Is.False);

        var invalid = CreateCredentialProvider(new Dictionary<string, string>
        {
            ["MotorShop:Iyzico:SkipResponseSignatureVerification"] = "yes",
            ["MotorShop:Iyzico:AllowUnsignedWebhooks"] = "1"
        });
        Assert.That(invalid.SkipResponseSignatureVerification, Is.False, "only an explicit true relaxes a check");
        Assert.That(invalid.AllowUnsignedWebhooks, Is.False);

        var relaxed = CreateCredentialProvider(new Dictionary<string, string>
        {
            ["MotorShop:Iyzico:SkipResponseSignatureVerification"] = "true",
            ["MotorShop:Iyzico:AllowUnsignedWebhooks"] = "True"
        });
        Assert.That(relaxed.SkipResponseSignatureVerification, Is.True);
        Assert.That(relaxed.AllowUnsignedWebhooks, Is.True);

        Assert.That(typeof(IyzicoSettings).GetProperty("VerifyResponseSignature"), Is.Null, "not an admin setting");
        Assert.That(typeof(IyzicoSettings).GetProperty("RequireWebhookSignature"), Is.Null, "not an admin setting");
    }

    private static IyzicoCredentialProvider CreateCredentialProvider(Dictionary<string, string> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var appContext = new Mock<IApplicationContext>();
        appContext.SetupGet(x => x.Configuration).Returns(configuration);

        return new IyzicoCredentialProvider(new Mock<ISettingFactory>().Object, appContext.Object, new EphemeralDataProtectionProvider());
    }

    [Test]
    public void Return_page_retrieves_are_throttled_per_order()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var throttle = new IyzicoReturnThrottle(cache);
        var t = Now;

        Assert.That(throttle.TryAcquire(1, t), Is.True);
        Assert.That(throttle.TryAcquire(1, t.AddSeconds(2)), Is.False, "minimum interval");
        Assert.That(throttle.TryAcquire(2, t.AddSeconds(2)), Is.True, "other orders are independent");

        var allowed = 1;
        for (var i = 1; i <= 50; i++)
        {
            if (throttle.TryAcquire(1, t.AddSeconds(i * 6)))
            {
                allowed++;
            }
        }

        Assert.That(allowed, Is.EqualTo(IyzicoReturnThrottle.MaxAttempts), "attempts per order are capped");
    }

    #endregion

    #region Payments.Manual, migrations

    [TestCase("Payments.Manual", true)]
    [TestCase("payments.manual", true)]
    [TestCase("Payments.Iyzico", false)]
    [TestCase("Payments.CashOnDelivery", false)]
    [TestCase("Payments.Prepayment", false)]
    public async Task Manual_card_payment_is_always_hidden(string systemName, bool excluded)
    {
        var filter = new BlockedPaymentMethodFilter();

        Assert.That(await filter.IsExcludedAsync(FilterRequest(systemName)), Is.EqualTo(excluded));
    }

    [Test]
    public async Task Blocked_filter_never_throws()
    {
        var filter = new BlockedPaymentMethodFilter();

        Assert.That(await filter.IsExcludedAsync(null), Is.False);
        Assert.That(await filter.IsExcludedAsync(new PaymentFilterRequest()), Is.False);
    }

    [Test]
    public async Task Manual_card_payment_is_removed_from_active_methods_of_all_stores()
    {
        const string name = "PaymentSettings.ActivePaymentMethodSystemNames";
        await using (var db = NewDbContext())
        {
            db.Settings.RemoveRange(db.Settings.Where(x => x.Name == name));
            db.Settings.Add(new Setting { Name = name, StoreId = 0, Value = new List<string> { "Payments.CashOnDelivery", "Payments.Manual", "Payments.PayInStore", "Payments.Prepayment" }.Convert<string>() });
            db.Settings.Add(new Setting { Name = name, StoreId = 2, Value = new List<string> { "Payments.Manual", "Payments.Iyzico" }.Convert<string>() });
            db.Settings.Add(new Setting { Name = name, StoreId = 3, Value = new List<string> { "Payments.Iyzico" }.Convert<string>() });
            await db.SaveChangesAsync();
        }

        try
        {
            await using (var db = NewDbContext())
            {
                Assert.That(await BlockedPaymentMethods.DeactivateAsync(db), Is.EqualTo(2));
            }

            await using (var db = NewDbContext())
            {
                var rows = await db.Settings.AsNoTracking().Where(x => x.Name == name).OrderBy(x => x.StoreId).ToListAsync();
                Assert.That(rows[0].Value.Convert<List<string>>(), Is.EqualTo(new[] { "Payments.CashOnDelivery", "Payments.PayInStore", "Payments.Prepayment" }));
                Assert.That(rows[1].Value.Convert<List<string>>(), Is.EqualTo(new[] { "Payments.Iyzico" }));
                Assert.That(rows[2].Value.Convert<List<string>>(), Is.EqualTo(new[] { "Payments.Iyzico" }));

                // Idempotent.
                Assert.That(await BlockedPaymentMethods.DeactivateAsync(db), Is.Zero);
            }
        }
        finally
        {
            await using var db = NewDbContext();
            db.Settings.RemoveRange(db.Settings.Where(x => x.Name == name));
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public void Migration_versions_are_unique_across_core_and_module()
    {
        var migrations = new[] { typeof(Smartstore.Core.Data.SmartDbContext).Assembly, typeof(IyzicoTransaction).Assembly }
            .SelectMany(x => x.GetTypes())
            .Select(x => (Type: x, Attribute: (MigrationVersionAttribute)Attribute.GetCustomAttribute(x, typeof(MigrationVersionAttribute))))
            .Where(x => x.Attribute != null)
            .ToList();

        var duplicates = migrations
            .GroupBy(x => x.Attribute.Version)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" / ", g.Select(x => x.Type.FullName)))
            .ToList();

        Assert.That(migrations.Count(x => x.Type.Assembly == typeof(IyzicoTransaction).Assembly), Is.EqualTo(3));
        Assert.That(duplicates, Is.Empty);
    }

    #endregion
}
