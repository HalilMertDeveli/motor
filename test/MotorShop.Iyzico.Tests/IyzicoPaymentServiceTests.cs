using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Services;
using NUnit.Framework;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;

namespace MotorShop.Iyzico.Tests;

[TestFixture]
[NonParallelizable]
public class IyzicoPaymentServiceTests : IyzicoTestBase
{
    #region Payment results

    [Test]
    public async Task Success_marks_order_paid_after_server_side_retrieve()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(result.OrderGuid, Is.EqualTo(order.OrderGuid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(o.AuthorizationTransactionId, Is.EqualTo("24681357"));
        Assert.That(o.CaptureTransactionId, Is.EqualTo("24681357"));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
        Assert.That(t.PaidOnUtc, Is.EqualTo(Now));
        Assert.That(MarkPaidCalls, Is.EqualTo(1));
        Assert.That(notes.Single().Note, Does.Contain("verified server-side").And.Contain("24681357"));
    }

    [Test]
    public async Task Failure_keeps_order_pending()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, paymentStatus: "FAILURE");

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Failed));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Failed));
        Assert.That(t.LastErrorCode, Is.EqualTo("10051"));
        Assert.That(MarkPaidCalls, Is.Zero);
        Assert.That(notes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Customer_cancel_on_hosted_form_keeps_order_pending_and_retryable()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        // Customer left the form: retrieve reports no payment for the token.
        Api.OnRetrieve = _ => FakeIyzicoApi.RetrieveFailure();

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Failed));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(IyzicoPaymentService.IsPayable(o), Is.True, "The customer can start the payment again.");
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Failed));
    }

    [Test]
    public async Task ThreeDS_pending_then_success()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);

        Api.OnRetrieve = _ => Api.Result(tx, paymentStatus: "INIT_THREEDS");
        var first = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        Api.OnRetrieve = _ => Api.Result(tx);
        var second = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(first.Outcome, Is.EqualTo(IyzicoOutcome.Pending));
        Assert.That(second.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
    }

    [Test]
    public async Task ThreeDS_failure_does_not_pay()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx, paymentStatus: "FAILURE");
            r.MdStatus = 0;
            r.ErrorCode = "10012";
            return Api.Sign(r);
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Failed));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(t.LastErrorCode, Is.EqualTo("10012"));
    }

    [Test]
    public async Task Installment_with_commission_is_accepted_and_recorded()
    {
        var order = await CreateOrderAsync(1000m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, installment: 6, paidPrice: 1089.90m);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(t.Installment, Is.EqualTo(6));
        Assert.That(t.PaidPrice, Is.EqualTo(1089.90m));
        Assert.That(notes.Single().Note, Does.Contain("installments: 6"));
    }

    [Test]
    public async Task Fraud_review_marks_order_authorized_not_paid()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: 0);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.FraudReview));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Authorized));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.FraudReview));
        Assert.That(MarkPaidCalls, Is.Zero);

        // Review approved later (webhook or reconciliation).
        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: 1);
        var approved = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Reconciliation);

        (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(approved.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Fraud_rejection_after_review_voids_the_authorization()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: 0);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: -1);
        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Failed));
        Assert.That(t.LastErrorCode, Is.EqualTo("FRAUD_REJECTED"));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Voided));
        Assert.That(VoidOfflineCalls, Is.EqualTo(1));
    }

    #endregion

    #region Idempotency and races

    [Test]
    public async Task Duplicate_callback_is_idempotent()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);

        var first = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);
        var second = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);
        var third = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        var (o, _, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(new[] { first.Outcome, second.Outcome, third.Outcome }, Is.All.EqualTo(IyzicoOutcome.Paid));
        Assert.That(Api.RetrieveCalls, Is.EqualTo(1), "A settled transaction is not retrieved again.");
        Assert.That(MarkPaidCalls, Is.EqualTo(1));
        Assert.That(notes, Has.Count.EqualTo(1));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Simultaneous_webhook_and_callback_mark_paid_once()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);
        Api.RetrieveDelay = TimeSpan.FromMilliseconds(150);

        var callback = NewService();
        var webhook = NewService();

        var results = await Task.WhenAll(
            Task.Run(() => callback.ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback)),
            Task.Run(() => webhook.ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook)));

        var (o, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(results.Select(x => x.Outcome), Is.All.EqualTo(IyzicoOutcome.Paid));
        Assert.That(MarkPaidCalls, Is.EqualTo(1));
        Assert.That(Api.RetrieveCalls, Is.EqualTo(1));
        Assert.That(notes, Has.Count.EqualTo(1));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
    }

    [Test]
    public async Task Late_failure_event_never_downgrades_a_paid_transaction()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        // A delayed/out-of-order webhook arrives; even a (hypothetical) FAILURE answer must not change anything.
        Api.OnRetrieve = _ => Api.Result(tx, paymentStatus: "FAILURE");
        var late = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(late.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Callback_without_session_cookie_is_correlated_by_stored_token()
    {
        // No HttpContext, no customer, no checkout state: the service only knows the token iyzico posted.
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = request =>
        {
            Assert.That(request.Token, Is.EqualTo(tx.Token));
            Assert.That(request.ConversationId, Is.EqualTo(tx.ConversationId), "Retrieve uses the stored conversationId, not session state.");
            return Api.Result(tx);
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(result.OrderGuid, Is.EqualTo(order.OrderGuid), "The controller redirects to the order's return page with this.");
    }

    [Test]
    public async Task Unknown_token_does_not_call_iyzico()
    {
        var result = await NewService().ProcessTokenAsync("tok-does-not-exist", IyzicoEventSource.Webhook);
        var empty = await NewService().ProcessTokenAsync(null, IyzicoEventSource.Callback);
        var tooLong = await NewService().ProcessTokenAsync(new string('a', 500), IyzicoEventSource.Callback);

        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.UnknownToken));
        Assert.That(empty.Outcome, Is.EqualTo(IyzicoOutcome.UnknownToken));
        Assert.That(tooLong.Outcome, Is.EqualTo(IyzicoOutcome.UnknownToken));
        Assert.That(Api.RetrieveCalls, Is.Zero);
    }

    [Test]
    public async Task Database_failure_is_retried_on_next_event()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);
        FailNextMarkPaid = 1;

        var failed = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(failed.Outcome, Is.EqualTo(IyzicoOutcome.Error), "Webhook returns 500 so iyzico retries.");
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid), "Verified payment state was committed before the failing order update.");
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));

        var retried = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        (o, _, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(retried.Outcome, Is.EqualTo(IyzicoOutcome.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(Api.RetrieveCalls, Is.EqualTo(1));
        Assert.That(MarkPaidCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Api_outage_returns_error_and_changes_nothing()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => throw new IyzicoApiException("timeout");

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Error));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Initialized));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
    }

    [Test]
    public async Task Second_successful_payment_for_same_order_is_flagged_not_applied()
    {
        var order = await CreateOrderAsync();
        var first = await CreateTransactionAsync(order);
        var second = await CreateTransactionAsync(order);

        Api.OnRetrieve = r => r.Token == first.Token ? Api.Result(first) : Api.Result(second, paymentId: "99999");
        await NewService().ProcessTokenAsync(first.Token, IyzicoEventSource.Callback);
        var result = await NewService().ProcessTokenAsync(second.Token, IyzicoEventSource.Webhook);

        var (o, t, notes) = await LoadAsync(order.Id, second.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.Duplicate));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Duplicate));
        Assert.That(o.AuthorizationTransactionId, Is.EqualTo("24681357"), "The first payment stays the order's payment.");
        Assert.That(MarkPaidCalls, Is.EqualTo(1));
        Assert.That(notes.Any(x => x.Note.Contains("99999") && x.Note.Contains("Refund it manually")), Is.True);
    }

    #endregion

    #region Verification failures

    [Test]
    public async Task Invalid_response_signature_is_never_paid()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx);
            r.Signature = "0000" + r.Signature[4..];
            return r;
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("SIGNATURE"));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(MarkPaidCalls, Is.Zero);
        Assert.That(notes.Single().Note, Does.Contain("NOT marked as paid"));
    }

    [Test]
    public async Task Tampered_amount_with_stale_signature_is_rejected()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx);
            r.Price = 1m; // signature not recomputed
            return r;
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(MarkPaidCalls, Is.Zero);
    }

    [Test]
    public async Task Mismatched_amount_is_rejected_even_when_signed()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx);
            r.Price = 15m;
            r.PaidPrice = 15m;
            return Api.Sign(r);
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("AMOUNT"));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
    }

    [Test]
    public async Task Paid_price_below_order_amount_is_rejected()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, paidPrice: 149.99m);

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("PAIDPRICE"));
    }

    [Test]
    public async Task Mismatched_currency_is_rejected()
    {
        var order = await CreateOrderAsync(150m, "TRY");
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx);
            r.Currency = "USD";
            return Api.Sign(r);
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("CURRENCY"));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
    }

    [TestCase("conversation")]
    [TestCase("basket")]
    [TestCase("token")]
    public async Task Mismatched_correlation_is_rejected(string field)
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ =>
        {
            var r = Api.Result(tx);
            switch (field)
            {
                case "conversation": r.ConversationId = "other"; break;
                case "basket": r.BasketId = Guid.NewGuid().ToString(); break;
                case "token": r.Token = "tok-other"; break;
            }
            return Api.Sign(r);
        };

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(MarkPaidCalls, Is.Zero);
    }

    [Test]
    public async Task Verification_failure_is_terminal_until_reviewed()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => { var r = Api.Result(tx); r.Signature = "bad"; return r; };
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        // A later, valid-looking answer does not flip it automatically.
        Api.OnRetrieve = _ => Api.Result(tx);
        var again = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Webhook);

        Assert.That(again.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(Api.RetrieveCalls, Is.EqualTo(1));
        Assert.That(MarkPaidCalls, Is.Zero);
    }

    #endregion

    #region Start payment

    [Test]
    public async Task Start_initializes_form_and_stores_transaction()
    {
        var order = await CreateOrderWithAddressAsync();

        var result = await NewService().StartPaymentAsync(order, "https://shop.test/iyzico/callback", "tr");

        Assert.That(result.Status, Is.EqualTo(IyzicoStartStatus.Redirect));
        Assert.That(result.RedirectUrl, Does.StartWith("https://sandbox-cpp.iyzipay.com"));

        await using var db = NewDbContext();
        var tx = await db.Set<IyzicoTransaction>().SingleAsync(x => x.OrderId == order.Id);
        Assert.That(tx.Status, Is.EqualTo(IyzicoTransactionStatus.Initialized));
        Assert.That(tx.ConversationId, Is.EqualTo(Api.LastInitializeRequest.ConversationId));
        Assert.That(tx.BasketId, Is.EqualTo(order.OrderGuid.ToString()));
        Assert.That(tx.Amount, Is.EqualTo(150m));
        Assert.That(Api.LastInitializeRequest.CallbackUrl, Is.EqualTo("https://shop.test/iyzico/callback"));
    }

    [Test]
    public async Task Start_rejects_initialize_response_with_bad_signature()
    {
        var order = await CreateOrderWithAddressAsync();
        Api.OnInitialize = request =>
        {
            var response = Api.InitializeSuccess(request);
            response.Signature = "forged";
            return response;
        };

        var result = await NewService().StartPaymentAsync(order, "https://shop.test/cb", "tr");

        await using var db = NewDbContext();
        Assert.That(result.Status, Is.EqualTo(IyzicoStartStatus.Failed));
        Assert.That(result.ErrorCode, Is.EqualTo("SIGNATURE"));
        Assert.That(await db.Set<IyzicoTransaction>().AnyAsync(x => x.OrderId == order.Id), Is.False);
    }

    [Test]
    public async Task Start_rejects_non_https_payment_page()
    {
        var order = await CreateOrderWithAddressAsync();
        Api.OnInitialize = request =>
        {
            var response = Api.InitializeSuccess(request);
            response.PaymentPageUrl = "http://evil.example/pay";
            return response;
        };

        var result = await NewService().StartPaymentAsync(order, "https://shop.test/cb", "tr");

        Assert.That(result.Status, Is.EqualTo(IyzicoStartStatus.Failed));
    }

    [Test]
    public async Task Start_detects_payment_made_in_another_tab_and_does_not_charge_twice()
    {
        var order = await CreateOrderWithAddressAsync();
        var earlier = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(earlier);

        var result = await NewService().StartPaymentAsync(order, "https://shop.test/cb", "tr");

        Assert.That(result.Status, Is.EqualTo(IyzicoStartStatus.AlreadyPaid));
        Assert.That(Api.InitializeCalls, Is.Zero);
    }

    [Test]
    public async Task Start_refuses_paid_cancelled_and_foreign_currency_orders()
    {
        var paid = await CreateOrderWithAddressAsync(o => o.PaymentStatus = PaymentStatus.Paid);
        var cancelled = await CreateOrderWithAddressAsync(o => o.OrderStatus = OrderStatus.Cancelled);
        var yen = await CreateOrderWithAddressAsync(o => o.CustomerCurrencyCode = "JPY");
        var otherMethod = await CreateOrderWithAddressAsync(o => o.PaymentMethodSystemName = "Payments.CashOnDelivery");

        Assert.That((await NewService().StartPaymentAsync(paid, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.AlreadyPaid));
        Assert.That((await NewService().StartPaymentAsync(cancelled, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.NotPayable));
        Assert.That((await NewService().StartPaymentAsync(yen, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.UnsupportedCurrency));
        Assert.That((await NewService().StartPaymentAsync(otherMethod, "https://x/cb", "tr")).Status, Is.EqualTo(IyzicoStartStatus.NotPayable));
        Assert.That(Api.InitializeCalls, Is.Zero);
    }

    [Test]
    public async Task Start_without_credentials_reports_not_configured()
    {
        var order = await CreateOrderWithAddressAsync();

        var result = await NewService(new IyzicoCredentials(null, null, IyzicoCredentialProvider.SandboxBaseUrl)).StartPaymentAsync(order, "https://x/cb", "tr");

        Assert.That(result.Status, Is.EqualTo(IyzicoStartStatus.NotConfigured));
        Assert.That(Api.InitializeCalls, Is.Zero);
    }

    private Task<Order> CreateOrderWithAddressAsync(Action<Order> configure = null)
        => CreateOrderAsync(150m, "TRY", o =>
        {
            o.BillingAddress = new Smartstore.Core.Common.Address
            {
                FirstName = "Ali",
                LastName = "Demir",
                Email = "ali@example.com",
                Address1 = "Atatürk Cad. 5",
                City = "Ankara",
                CreatedOnUtc = Now
            };
            o.OrderItems.Add(new OrderItem { ProductId = 1, Quantity = 1, PriceInclTax = 150m, OrderItemGuid = Guid.NewGuid() });
            configure?.Invoke(o);
        });

    #endregion

    #region Refund, cancel, reconciliation

    private async Task<(Order Order, IyzicoTransaction Tx)> CreatePaidOrderAsync(decimal total, params (string Id, decimal Paid)[] items)
    {
        var order = await CreateOrderAsync(total);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, items: items);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        await using var db = NewDbContext();
        return (await db.Orders.AsNoTracking().FirstAsync(x => x.Id == order.Id), await db.Set<IyzicoTransaction>().AsNoTracking().FirstAsync(x => x.Id == tx.Id));
    }

    [Test]
    public async Task Partial_refund_is_spread_over_item_transactions()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);

        var full = await NewService().RefundAsync(order, 120m, isPartial: true);

        var requests = Api.RefundRequests.ToArray();
        Assert.That(full, Is.False);
        Assert.That(requests.Select(x => (x.PaymentTransactionId, x.Price)), Is.EqualTo(new[] { ("T1", "100.0"), ("T2", "20.0") }));
        Assert.That(requests.All(x => x.Currency == "TRY"), Is.True);
        Assert.That(Api.CancelRequests, Is.Empty);

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.RefundedAmount, Is.EqualTo(120m));
        var items = JsonSerializer.Deserialize<IyzicoItemTransaction[]>(t.ItemTransactionsJson);
        Assert.That(items.Select(x => x.RefundedAmount), Is.EqualTo(new[] { 100m, 20m }));

        // The rest, as a second partial refund, completes it.
        var rest = await NewService().RefundAsync(order, 30m, isPartial: true);
        Assert.That(rest, Is.True);
        Assert.That(Api.RefundRequests.Last().PaymentTransactionId, Is.EqualTo("T2"));
    }

    [Test]
    public async Task Refund_above_refundable_amount_is_refused()
    {
        var (order, _) = await CreatePaidOrderAsync(150m);
        Now = Now.AddDays(2);

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 150.01m, isPartial: true));
        Assert.That(Api.RefundRequests, Is.Empty);
    }

    [Test]
    public async Task Same_day_full_refund_uses_cancel()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m);

        var full = await NewService().RefundAsync(order, 150m, isPartial: false);

        var (_, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(full, Is.True);
        Assert.That(Api.CancelRequests.Single().PaymentId, Is.EqualTo("24681357"));
        Assert.That(Api.RefundRequests, Is.Empty);
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Cancelled));
        Assert.That(t.RefundedAmount, Is.EqualTo(150m));
        Assert.That(notes.Any(x => x.Note.Contains("cancelled")), Is.True);
    }

    [Test]
    public async Task Full_refund_falls_back_to_refund_when_cancel_is_rejected()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m);
        Api.OnCancel = _ => new CancelResponse { Status = "failure", ErrorCode = "5093" };

        var full = await NewService().RefundAsync(order, 150m, isPartial: false);

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(full, Is.True);
        Assert.That(Api.CancelRequests, Has.Count.EqualTo(1));
        Assert.That(Api.RefundRequests.Single().Price, Is.EqualTo("150.0"));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
        Assert.That(t.RefundedAmount, Is.EqualTo(150m));
        Assert.That(t.LastErrorCode, Is.Null);
    }

    [Test]
    public async Task Full_refund_on_later_day_refunds_paid_price_including_installment_commission()
    {
        var order = await CreateOrderAsync(1000m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, installment: 3, paidPrice: 1045m);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);
        Now = Now.AddDays(1);

        await NewService().RefundAsync(order, 1000m, isPartial: false);

        Assert.That(Api.CancelRequests, Is.Empty);
        Assert.That(Api.RefundRequests.Single().Price, Is.EqualTo("1045.0"));
    }

    [Test]
    public async Task Rejected_refund_throws_and_keeps_completed_parts()
    {
        var (order, tx) = await CreatePaidOrderAsync(150m, ("T1", 100m), ("T2", 50m));
        Now = Now.AddDays(2);
        Api.OnRefund = r => r.PaymentTransactionId == "T2"
            ? new RefundResponse { Status = "failure", ErrorCode = "5085", ErrorMessage = "refund not allowed" }
            : Api.RefundSuccess(r);

        Assert.ThrowsAsync<PaymentException>(() => NewService().RefundAsync(order, 150m, isPartial: true));

        var (_, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(t.RefundedAmount, Is.EqualTo(100m));
        Assert.That(notes.Any(x => x.Note.Contains("refund partly processed")), Is.True);
    }

    [Test]
    public async Task Void_cancels_payment_held_by_fraud_check()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx, fraudStatus: 0);
        await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        await NewService().VoidAsync(order);

        var (_, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(Api.CancelRequests.Single().PaymentId, Is.EqualTo("24681357"));
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Cancelled));
    }

    [Test]
    public async Task Void_without_payment_throws()
    {
        var order = await CreateOrderAsync();
        await CreateTransactionAsync(order);

        Assert.ThrowsAsync<PaymentException>(() => NewService().VoidAsync(order));
        Assert.That(Api.CancelRequests, Is.Empty);
    }

    [Test]
    public async Task Reconciliation_settles_missed_payments_and_expires_abandoned_ones()
    {
        var paidOrder = await CreateOrderAsync();
        var missed = await CreateTransactionAsync(paidOrder);
        var abandonedOrder = await CreateOrderAsync();
        var abandoned = await CreateTransactionAsync(abandonedOrder);

        Api.OnRetrieve = r => r.Token == missed.Token ? Api.Result(missed) : FakeIyzicoApi.RetrieveFailure();
        Now = Now.AddHours(4);

        var count = await NewService().ReconcileAsync();

        var (o1, t1, _) = await LoadAsync(paidOrder.Id, missed.Token);
        var (o2, t2, _) = await LoadAsync(abandonedOrder.Id, abandoned.Token);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(o1.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
        Assert.That(t1.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
        Assert.That(o2.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(t2.Status, Is.EqualTo(IyzicoTransactionStatus.Expired));
    }

    #endregion
}
