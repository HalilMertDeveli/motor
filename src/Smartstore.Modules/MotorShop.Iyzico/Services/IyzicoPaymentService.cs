using System.Globalization;
using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Providers;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Threading;

namespace MotorShop.Iyzico.Services;

public enum IyzicoEventSource
{
    Callback,
    Webhook,
    Reconciliation,
    PaymentPage
}

public enum IyzicoOutcome
{
    Paid,
    FraudReview,
    Pending,
    Failed,
    VerificationFailed,
    Duplicate,
    Cancelled,
    UnknownToken,
    Error
}

public sealed record IyzicoProcessResult(IyzicoOutcome Outcome, int? OrderId = null, Guid? OrderGuid = null);

public enum IyzicoStartStatus
{
    Redirect,
    AlreadyPaid,
    NotPayable,
    NotConfigured,
    UnsupportedCurrency,
    Failed
}

public sealed record IyzicoStartResult(IyzicoStartStatus Status, string RedirectUrl = null, string ErrorCode = null);

/// <summary>
/// A refund or cancel sent to iyzico whose outcome is unknown. Blocks further refunds of the payment until resolved.
/// </summary>
public sealed class IyzicoPendingRefund
{
    public int TransactionId { get; init; }
    public int OrderId { get; init; }
    public string PaymentId { get; init; }
    public string ConversationId { get; init; }
    public bool IsCancel { get; init; }
    public string PaymentTransactionIds { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; }
    public DateTime? SinceUtc { get; init; }
}

/// <summary>
/// Starts checkout form payments, verifies results server-side and applies them to orders.
/// </summary>
/// <remarks>
/// Rules:
/// - An order is only marked as paid after a server-side retrieve whose signature, token, conversationId,
///   basketId, currency and amounts match the stored transaction. Browser redirects and webhooks only trigger that retrieve.
/// - All processing of a token runs under a distributed lock and re-reads the stored state, so duplicate,
///   delayed, out-of-order and simultaneous callback/webhook calls are idempotent.
/// - Correlation uses the stored transaction, never the customer session.
/// </remarks>
public class IyzicoPaymentService
{
    static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    private readonly SmartDbContext _db;
    private readonly IIyzicoApiClient _api;
    private readonly IIyzicoCredentialProvider _credentialProvider;
    private readonly ISettingFactory _settingFactory;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IDistributedLockProvider _lockProvider;

    public IyzicoPaymentService(
        SmartDbContext db,
        IIyzicoApiClient api,
        IIyzicoCredentialProvider credentialProvider,
        ISettingFactory settingFactory,
        IOrderProcessingService orderProcessingService,
        IDistributedLockProvider lockProvider)
    {
        _db = db;
        _api = api;
        _credentialProvider = credentialProvider;
        _settingFactory = settingFactory;
        _orderProcessingService = orderProcessingService;
        _lockProvider = lockProvider;
    }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Used for tests to make time deterministic.
    /// </summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    #region Start payment

    public async Task<IyzicoStartResult> StartPaymentAsync(Order order, string callbackUrl, string locale, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!IsPayable(order))
        {
            return order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Authorized
                ? new(IyzicoStartStatus.AlreadyPaid)
                : new(IyzicoStartStatus.NotPayable);
        }

        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(order.StoreId);
        if (!IyzicoRequestBuilder.IsCurrencyAllowed(settings, order.CustomerCurrencyCode))
        {
            return new(IyzicoStartStatus.UnsupportedCurrency);
        }

        // Serializes payment starts of one order. Taken before token and order locks (lock order: start, token, order).
        await using var startLock = await _lockProvider.GetLock($"motorshop.iyzico.start:{order.Id}").AcquireAsync(LockTimeout, cancelToken);

        // Settle earlier attempts first: the customer may have paid in another tab. Prevents double charges.
        var earlier = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancelToken);

        if (earlier.Any(x => x.Status is IyzicoTransactionStatus.Paid or IyzicoTransactionStatus.FraudReview or IyzicoTransactionStatus.VerificationFailed or IyzicoTransactionStatus.Duplicate))
        {
            return new(IyzicoStartStatus.AlreadyPaid);
        }

        foreach (var tx in earlier.Where(x => x.Status is IyzicoTransactionStatus.Initialized or IyzicoTransactionStatus.Pending).Take(5))
        {
            var settled = await ProcessTokenAsync(tx.Token, IyzicoEventSource.PaymentPage, cancelToken);
            if (settled.Outcome is IyzicoOutcome.Paid or IyzicoOutcome.FraudReview or IyzicoOutcome.VerificationFailed or IyzicoOutcome.Duplicate)
            {
                return new(IyzicoStartStatus.AlreadyPaid);
            }
            if (settled.Outcome == IyzicoOutcome.Error)
            {
                // Earlier attempt state unknown. Do not risk a second charge.
                return new(IyzicoStartStatus.Failed, ErrorCode: "PREVIOUS_ATTEMPT_UNKNOWN");
            }
        }

        var credentials = await _credentialProvider.GetCredentialsAsync(order.StoreId);
        if (!credentials.IsComplete)
        {
            return new(IyzicoStartStatus.NotConfigured);
        }

        var conversationId = Guid.NewGuid().ToString("N");
        var request = IyzicoRequestBuilder.Build(order, settings, conversationId, callbackUrl, locale);

        CheckoutFormInitializeResponse response;
        try
        {
            response = await _api.InitializeCheckoutFormAsync(credentials, request, cancelToken);
        }
        catch (IyzicoApiException ex)
        {
            Logger.LogWarning(ex, "iyzico initialize failed for order {OrderId}.", order.Id);
            return new(IyzicoStartStatus.Failed, ErrorCode: "UNREACHABLE");
        }

        if (!response.IsSuccess)
        {
            Logger.LogWarning("iyzico initialize rejected for order {OrderId}: {ErrorCode} {ErrorGroup}.", order.Id, response.ErrorCode, response.ErrorGroup);
            return new(IyzicoStartStatus.Failed, ErrorCode: response.ErrorCode);
        }

        if (!_credentialProvider.SkipResponseSignatureVerification)
        {
            var expected = IyzicoSignature.ComputeInitializeSignature(credentials.SecretKey, response.ConversationId, response.Token);
            if (!IyzicoSignature.SignatureEquals(expected, response.Signature))
            {
                Logger.LogWarning("iyzico initialize response for order {OrderId} has an invalid signature.", order.Id);
                return new(IyzicoStartStatus.Failed, ErrorCode: "SIGNATURE");
            }
        }

        if (!string.Equals(response.ConversationId, conversationId, StringComparison.Ordinal)
            || response.Token.IsEmpty()
            || response.Token.Length > 200
            || !IsAllowedPaymentPageUrl(response.PaymentPageUrl, credentials))
        {
            Logger.LogWarning("iyzico initialize response for order {OrderId} failed correlation checks.", order.Id);
            return new(IyzicoStartStatus.Failed, ErrorCode: "CORRELATION");
        }

        var now = UtcNow();
        _db.IyzicoTransactions().Add(new IyzicoTransaction
        {
            OrderId = order.Id,
            StoreId = order.StoreId,
            Token = response.Token,
            ConversationId = conversationId,
            BasketId = request.BasketId,
            Status = IyzicoTransactionStatus.Initialized,
            Amount = IyzicoRequestBuilder.GetOrderAmount(order),
            Currency = request.Currency,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        });

        await _db.SaveChangesAsync(cancelToken);

        return new(IyzicoStartStatus.Redirect, response.PaymentPageUrl);
    }

    public static bool IsPayable(Order order)
        => order != null
        && !order.Deleted
        && order.OrderStatus != OrderStatus.Cancelled
        && order.PaymentStatus == PaymentStatus.Pending
        && order.PaymentMethodSystemName == IyzicoProvider.SystemName
        && order.OrderTotal > decimal.Zero;

    private static bool IsAllowedPaymentPageUrl(string url, IyzicoCredentials credentials)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Plain HTTP only for a local mock configured through MotorShop:Iyzico:BaseUrl.
        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && credentials.BaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
    }

    #endregion

    #region Process result

    public async Task<IyzicoProcessResult> ProcessTokenAsync(string token, IyzicoEventSource source, CancellationToken cancelToken = default)
    {
        if (token.IsEmpty() || token.Length > 200)
        {
            return new(IyzicoOutcome.UnknownToken);
        }

        var exists = await _db.IyzicoTransactions().AsNoTracking().AnyAsync(x => x.Token == token, cancelToken);
        if (!exists)
        {
            Logger.LogWarning("iyzico {Source} for an unknown token.", source);
            return new(IyzicoOutcome.UnknownToken);
        }

        await using var tokenLock = await _lockProvider.GetLock($"motorshop.iyzico.token:{token}").AcquireAsync(LockTimeout, cancelToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ProcessLockedAsync(token, source, cancelToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Another node updated the transaction. Re-read and evaluate again.
                DetachAll();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "iyzico {Source} processing failed. The result will be processed again by the next callback, webhook or reconciliation run.", source);
                DetachAll();
                return new(IyzicoOutcome.Error);
            }
        }
    }

    private async Task<IyzicoProcessResult> ProcessLockedAsync(string token, IyzicoEventSource source, CancellationToken cancelToken)
    {
        var tx = await _db.IyzicoTransactions().FirstAsync(x => x.Token == token, cancelToken);

        // Per-order lock: Order has no concurrency token, so two tokens of the same order (two tabs) must not
        // pass CanMarkOrderAsPaid in parallel. Also serializes against the stale order cancellation.
        await using var orderLock = await AcquireOrderLockAsync(tx.OrderId, cancelToken);

        await _db.Entry(tx).ReloadAsync(cancelToken);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == tx.OrderId, cancelToken);
        if (order == null)
        {
            Logger.LogError("iyzico transaction {TransactionId} points to missing order {OrderId}.", tx.Id, tx.OrderId);
            return new(IyzicoOutcome.Error);
        }

        await _db.Entry(order).ReloadAsync(cancelToken);

        switch (tx.Status)
        {
            case IyzicoTransactionStatus.Paid:
                // Idempotent: completes the order transition if an earlier run stored the payment but failed afterwards.
                await EnsureOrderPaidAsync(order);
                return Result(IyzicoOutcome.Paid, order);
            case IyzicoTransactionStatus.Cancelled:
                return Result(IyzicoOutcome.Cancelled, order);
            case IyzicoTransactionStatus.Duplicate:
                return Result(IyzicoOutcome.Duplicate, order);
            case IyzicoTransactionStatus.VerificationFailed:
                // Never re-evaluated automatically: needs a human decision.
                return Result(IyzicoOutcome.VerificationFailed, order);
        }

        var credentials = await _credentialProvider.GetCredentialsAsync(tx.StoreId);
        if (!credentials.IsComplete)
        {
            Logger.LogError("iyzico credentials missing for store {StoreId}.", tx.StoreId);
            return Result(IyzicoOutcome.Error, order);
        }

        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(tx.StoreId);

        CheckoutFormResult result;
        try
        {
            result = await _api.RetrieveCheckoutFormAsync(credentials, new RetrieveCheckoutFormRequest
            {
                Locale = "en",
                ConversationId = tx.ConversationId,
                Token = tx.Token
            }, cancelToken);
        }
        catch (IyzicoApiException ex)
        {
            Logger.LogWarning(ex, "iyzico retrieve failed for transaction {TransactionId} ({Source}).", tx.Id, source);
            return Result(IyzicoOutcome.Error, order);
        }

        var verdict = IyzicoVerifier.Evaluate(tx, result, credentials.SecretKey, !_credentialProvider.SkipResponseSignatureVerification, settings.MaxInstallmentSurchargePercent);

        if (verdict.Status is IyzicoTransactionStatus.Paid or IyzicoTransactionStatus.FraudReview &&
            (IyzicoRequestBuilder.GetOrderAmount(order) != IyzicoRequestBuilder.Round(tx.Amount) ||
             !string.Equals(order.CustomerCurrencyCode, tx.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            // The order total or currency was edited after the payment page was opened.
            verdict = new(IyzicoTransactionStatus.VerificationFailed, "ORDER_CHANGED", "Order total or currency changed after the payment was started.");
        }
        var previous = tx.Status;

        switch (verdict.Status)
        {
            case IyzicoTransactionStatus.Paid:
            case IyzicoTransactionStatus.FraudReview:
                return await ApplySuccessAsync(tx, order, result, verdict, previous, source, cancelToken);

            case IyzicoTransactionStatus.VerificationFailed:
                Touch(tx, verdict);
                tx.Status = IyzicoTransactionStatus.VerificationFailed;
                tx.PaymentId = result?.PaymentId?.Truncate(64);
                _db.OrderNotes.Add(order, $"iyzico: payment result failed verification ({verdict.ErrorCode}). Order NOT marked as paid. Check the payment in the iyzico merchant panel. paymentId: {tx.PaymentId ?? "-"}, source: {source}.");
                await _db.SaveChangesAsync(cancelToken);
                Logger.LogWarning("iyzico transaction {TransactionId} failed verification: {ErrorCode}.", tx.Id, verdict.ErrorCode);
                return Result(IyzicoOutcome.VerificationFailed, order);

            case IyzicoTransactionStatus.Failed:
                Touch(tx, verdict);
                tx.Status = IyzicoTransactionStatus.Failed;
                if (previous != IyzicoTransactionStatus.Failed)
                {
                    _db.OrderNotes.Add(order, $"iyzico: payment not completed ({verdict.ErrorCode}). Source: {source}.");
                }
                await _db.SaveChangesAsync(cancelToken);

                if (previous == IyzicoTransactionStatus.FraudReview && order.CanVoidOffline())
                {
                    // Held payment was rejected after review.
                    await _orderProcessingService.VoidOfflineAsync(order);
                }

                return Result(IyzicoOutcome.Failed, order);

            default:
                Touch(tx, verdict);
                tx.Status = previous == IyzicoTransactionStatus.FraudReview ? previous : IyzicoTransactionStatus.Pending;
                await _db.SaveChangesAsync(cancelToken);
                return Result(previous == IyzicoTransactionStatus.FraudReview ? IyzicoOutcome.FraudReview : IyzicoOutcome.Pending, order);
        }
    }

    private async Task<IyzicoProcessResult> ApplySuccessAsync(
        IyzicoTransaction tx,
        Order order,
        CheckoutFormResult result,
        IyzicoVerdict verdict,
        IyzicoTransactionStatus previous,
        IyzicoEventSource source,
        CancellationToken cancelToken)
    {
        Touch(tx, verdict);
        tx.PaymentId = result.PaymentId.Truncate(64);
        tx.PaidPrice = result.PaidPrice;
        tx.Installment = result.Installment;
        tx.FraudStatus = result.FraudStatus;
        tx.ItemTransactionsJson = JsonSerializer.Serialize(
            (result.ItemTransactions ?? []).Select(x => new IyzicoItemTransaction
            {
                PaymentTransactionId = x.PaymentTransactionId,
                ItemId = x.ItemId,
                PaidPrice = x.PaidPrice ?? 0m
            }).ToList());

        var anotherPayment = await _db.IyzicoTransactions()
            .AsNoTracking()
            .AnyAsync(x => x.OrderId == tx.OrderId && x.Id != tx.Id &&
                (x.Status == IyzicoTransactionStatus.Paid || x.Status == IyzicoTransactionStatus.FraudReview), cancelToken);

        if (anotherPayment || order.Deleted || order.OrderStatus == OrderStatus.Cancelled ||
            order.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.Voided or PaymentStatus.PartiallyRefunded)
        {
            // Money was taken although the order cannot accept it. Never auto-refund: flag for a human.
            tx.Status = IyzicoTransactionStatus.Duplicate;
            tx.LastErrorCode = "UNEXPECTED_PAYMENT";
            _db.OrderNotes.Add(order, $"iyzico: an additional payment was received for this order (paymentId {tx.PaymentId}, {FormatAmount(tx.PaidPrice, tx.Currency)}). Refund it manually in the iyzico merchant panel.");
            await _db.SaveChangesAsync(cancelToken);
            Logger.LogError("iyzico: unexpected additional payment on order {OrderId}, transaction {TransactionId}. Manual refund required.", order.Id, tx.Id);
            return Result(IyzicoOutcome.Duplicate, order);
        }

        tx.Status = verdict.Status;
        order.AuthorizationTransactionId = tx.PaymentId;

        if (verdict.Status == IyzicoTransactionStatus.Paid)
        {
            tx.PaidOnUtc ??= UtcNow();
            order.CaptureTransactionId = tx.PaymentId;
            order.CaptureTransactionResult = "SUCCESS";
        }
        else
        {
            order.AuthorizationTransactionResult = "FRAUD_REVIEW";
        }

        if (previous != verdict.Status)
        {
            var installment = tx.Installment > 1 ? $", installments: {tx.Installment}" : string.Empty;
            var note = verdict.Status == IyzicoTransactionStatus.Paid
                ? $"iyzico: payment verified server-side (paymentId {tx.PaymentId}, paid {FormatAmount(tx.PaidPrice, tx.Currency)}{installment}, source: {source})."
                : $"iyzico: payment received but held by the iyzico fraud check (paymentId {tx.PaymentId}). Do not ship until it is approved.";
            _db.OrderNotes.Add(order, note);
        }

        // One commit for transaction state and order fields.
        await _db.SaveChangesAsync(cancelToken);

        if (verdict.Status == IyzicoTransactionStatus.Paid)
        {
            await EnsureOrderPaidAsync(order);
            return Result(IyzicoOutcome.Paid, order);
        }

        if (order.CanMarkOrderAsAuthorized())
        {
            await _orderProcessingService.MarkAsAuthorizedAsync(order);
        }

        return Result(IyzicoOutcome.FraudReview, order);
    }

    private async Task EnsureOrderPaidAsync(Order order)
    {
        if (order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Authorized && order.CanMarkOrderAsPaid())
        {
            await _orderProcessingService.MarkOrderAsPaidAsync(order);
        }
    }

    #endregion

    #region Refund and cancel

    /// <summary>
    /// Marker of an unconfirmed refund in <see cref="IyzicoTransaction.ItemTransactionsJson"/>, used to find such transactions.
    /// </summary>
    internal const string PendingRefundJsonMarker = "\"PendingRefundConversationId\":\"";

    /// <summary>
    /// iyzico refundStatus treated as "processed" in the transaction report. Not documented by iyzico (checked 2026-10-20);
    /// assumed to follow paymentStatus (1 = success). Any other value leaves the refund unconfirmed for an admin to resolve.
    /// </summary>
    internal const int ReportedRefundSuccess = 1;

    private enum CancelOutcome
    {
        Cancelled,
        Rejected,
        Unknown
    }

    /// <summary>
    /// Refunds a paid order. Same-day full refunds first try the cancel service.
    /// Partial refunds are spread over the item transactions (refund v1 by paymentTransactionId),
    /// because iyzico advises against amount-based refund v2 for baskets with more than one item.
    /// </summary>
    /// <remarks>
    /// A refund or cancel without a verifiable answer (timeout, unreadable or unverifiable response) may still have been
    /// processed by iyzico. It is stored as unconfirmed on the item and blocks every further refund of the payment until
    /// the transaction report confirms it or an admin resolves it (<see cref="ResolvePendingRefundAsync"/>).
    /// </remarks>
    /// <returns><c>true</c> if the whole paid amount is refunded.</returns>
    public async Task<bool> RefundAsync(Order order, decimal amountInPrimaryCurrency, bool isPartial, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var refundLock = await AcquireRefundLockAsync(order.Id, cancelToken);

        var tx = await _db.IyzicoTransactions()
            .Where(x => x.OrderId == order.Id && x.Status == IyzicoTransactionStatus.Paid)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancelToken)
            ?? throw new PaymentException("No verified iyzico payment found for this order.");

        await _db.Entry(tx).ReloadAsync(cancelToken);

        var credentials = await _credentialProvider.GetCredentialsAsync(tx.StoreId);
        if (!credentials.IsComplete)
        {
            throw new PaymentException("iyzico is not configured.");
        }

        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(tx.StoreId);
        var items = DeserializeItems(tx);

        if (items.Any(x => x.HasPendingRefund))
        {
            // An earlier refund may have been processed by iyzico. Never send another one before it is settled.
            await ConfirmFromReportAsync(tx, order, items, credentials, cancelToken);
            if (items.Any(x => x.HasPendingRefund))
            {
                throw new PaymentException("An earlier iyzico refund of this order has an unknown outcome. Check it in the iyzico merchant panel and resolve it on the iyzico configuration page (unconfirmed refunds) before refunding again.");
            }
            if (tx.Status != IyzicoTransactionStatus.Paid)
            {
                throw new PaymentException("The iyzico payment was cancelled by an earlier, now confirmed operation.");
            }
        }

        var paid = tx.PaidPrice ?? tx.Amount;
        var remaining = paid - tx.RefundedAmount;
        var amount = isPartial
            ? IyzicoRequestBuilder.Round(amountInPrimaryCurrency * order.CurrencyRate)
            : remaining;

        if (amount <= 0m || amount > remaining)
        {
            throw new PaymentException($"Refund amount {FormatAmount(amount, tx.Currency)} exceeds the refundable iyzico amount {FormatAmount(remaining, tx.Currency)}.");
        }

        if (!isPartial && tx.RefundedAmount == 0m && settings.TryCancelOnSameDayFullRefund && IsSameTurkeyDay(tx.PaidOnUtc, UtcNow()))
        {
            var cancelConversationId = Guid.NewGuid().ToString("N");
            var cancel = await TryCancelAsync(tx, order, credentials, cancelConversationId, cancelToken);
            if (cancel == CancelOutcome.Cancelled)
            {
                items.Each(x => x.RefundedAmount = x.PaidPrice);
                tx.ItemTransactionsJson = JsonSerializer.Serialize(items);
                tx.RefundedAmount = paid;
                await _db.SaveChangesAsync(cancelToken);
                return true;
            }

            if (cancel == CancelOutcome.Unknown)
            {
                // iyzico may have cancelled the payment. Refunding the items now could pay the customer twice.
                var now = UtcNow();
                foreach (var item in items.Where(x => x.PaidPrice - x.RefundedAmount > 0m))
                {
                    item.PendingRefundConversationId = cancelConversationId;
                    item.PendingRefundAmount = item.PaidPrice - item.RefundedAmount;
                    item.PendingRefundIsCancel = true;
                    item.PendingRefundSinceUtc = now;
                }

                tx.ItemTransactionsJson = JsonSerializer.Serialize(items);
                tx.LastErrorCode = "CANCEL_UNKNOWN";
                Touch(tx, null);
                _db.OrderNotes.Add(order, $"iyzico: cancel of payment {tx.PaymentId} ({FormatAmount(paid, tx.Currency)}) has an unknown outcome. iyzico may have processed it. Further refunds are blocked until it is confirmed (transaction report) or resolved on the iyzico configuration page.");
                await _db.SaveChangesAsync(cancelToken);

                throw new PaymentException("The iyzico cancel request has an unknown outcome. It may have been processed, so further refunds of this order are blocked until it is confirmed.");
            }
        }

        if (items.Count == 0)
        {
            throw new PaymentException("The iyzico payment has no item transactions to refund.");
        }

        var left = amount;
        foreach (var item in items)
        {
            var itemRemaining = item.PaidPrice - item.RefundedAmount;
            if (left <= 0m || itemRemaining <= 0m)
            {
                continue;
            }

            var part = Math.Min(left, itemRemaining);
            var conversationId = Guid.NewGuid().ToString("N");
            RefundResponse response;
            try
            {
                response = await _api.RefundAsync(credentials, new RefundRequest
                {
                    Locale = "en",
                    ConversationId = conversationId,
                    PaymentTransactionId = item.PaymentTransactionId,
                    Price = IyzicoSignature.FormatPriceForRequest(part),
                    Currency = tx.Currency,
                    Ip = order.CustomerIp
                }, cancelToken);
            }
            catch (IyzicoApiException ex)
            {
                // Timeout or unreadable answer: iyzico may have processed the refund.
                Logger.LogWarning(ex, "iyzico refund for transaction {TransactionId} has an unknown outcome.", tx.Id);
                await SaveUnknownRefundAsync(tx, order, items, item, conversationId, part, amount, left, "REFUND_UNKNOWN", cancelToken);
                throw new PaymentException("iyzico did not answer the refund request. It may have been processed, so further refunds of this order are blocked until it is confirmed.");
            }

            if (!response.IsSuccess)
            {
                // iyzico answered with an error: nothing was refunded for this item.
                await SaveRefundProgressAsync(tx, order, items, amount, left, response.ErrorCode, cancelToken);
                throw new PaymentException($"iyzico refund rejected ({response.ErrorCode}): {response.ErrorMessage}");
            }

            var mismatch = VerifyRefundResponse(response, tx, item, conversationId, part, credentials.SecretKey);
            if (mismatch != null)
            {
                // A success claim that cannot be verified: the refund may or may not have happened.
                Logger.LogWarning("iyzico refund answer for transaction {TransactionId} failed verification: {Mismatch}.", tx.Id, mismatch);
                await SaveUnknownRefundAsync(tx, order, items, item, conversationId, part, amount, left, "REFUND_UNVERIFIED", cancelToken);
                throw new PaymentException($"The iyzico refund answer could not be verified ({mismatch}). Further refunds of this order are blocked until it is confirmed.");
            }

            item.RefundedAmount += part;
            tx.RefundedAmount += part;
            left -= part;

            // Persist after every successful call, so a later failure never loses a completed refund.
            tx.ItemTransactionsJson = JsonSerializer.Serialize(items);
            Touch(tx, null);
            await _db.SaveChangesAsync(cancelToken);
        }

        if (left > 0m)
        {
            await SaveRefundProgressAsync(tx, order, items, amount, left, "NOT_ALLOCATED", cancelToken);
            throw new PaymentException("The refund amount could not be allocated to the iyzico item transactions.");
        }

        // A rejected same-day cancel before the refund is not an error of the completed operation.
        tx.LastErrorCode = null;
        tx.LastErrorMessage = null;
        _db.OrderNotes.Add(order, $"iyzico: refunded {FormatAmount(amount, tx.Currency)} (total refunded {FormatAmount(tx.RefundedAmount, tx.Currency)} of {FormatAmount(paid, tx.Currency)}).");
        await _db.SaveChangesAsync(cancelToken);

        return tx.RefundedAmount >= paid;
    }

    /// <summary>
    /// Cancels a payment held by the fraud check (order payment status Authorized).
    /// </summary>
    public async Task VoidAsync(Order order, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var refundLock = await AcquireRefundLockAsync(order.Id, cancelToken);

        var tx = await _db.IyzicoTransactions()
            .Where(x => x.OrderId == order.Id && (x.Status == IyzicoTransactionStatus.FraudReview || x.Status == IyzicoTransactionStatus.Paid))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancelToken)
            ?? throw new PaymentException("No iyzico payment to cancel for this order.");

        var credentials = await _credentialProvider.GetCredentialsAsync(tx.StoreId);
        if (!credentials.IsComplete)
        {
            throw new PaymentException("iyzico is not configured.");
        }

        if (await TryCancelAsync(tx, order, credentials, Guid.NewGuid().ToString("N"), cancelToken) != CancelOutcome.Cancelled)
        {
            // Keep the error for the admin. Retrying a void is harmless: iyzico rejects a second cancel.
            Touch(tx, null);
            await _db.SaveChangesAsync(cancelToken);
            throw new PaymentException($"iyzico cancel rejected ({tx.LastErrorCode}).");
        }
    }

    private async Task<CancelOutcome> TryCancelAsync(IyzicoTransaction tx, Order order, IyzicoCredentials credentials, string conversationId, CancellationToken cancelToken)
    {
        CancelResponse response;
        try
        {
            response = await _api.CancelAsync(credentials, new CancelRequest
            {
                Locale = "en",
                ConversationId = conversationId,
                PaymentId = tx.PaymentId,
                Ip = order.CustomerIp
            }, cancelToken);
        }
        catch (IyzicoApiException ex)
        {
            Logger.LogWarning(ex, "iyzico cancel failed for transaction {TransactionId}.", tx.Id);
            tx.LastErrorCode = "UNREACHABLE";
            tx.LastErrorMessage = null;
            return CancelOutcome.Unknown;
        }

        if (!response.IsSuccess)
        {
            Logger.LogWarning("iyzico cancel rejected for transaction {TransactionId}: {ErrorCode}.", tx.Id, response.ErrorCode);
            tx.LastErrorCode = response.ErrorCode.Truncate(50);
            tx.LastErrorMessage = response.ErrorMessage.Truncate(500);
            return CancelOutcome.Rejected;
        }

        if (!string.Equals(response.PaymentId, tx.PaymentId, StringComparison.Ordinal))
        {
            // A success answer for another payment cannot be correlated. The cancel response has no documented signature.
            Logger.LogWarning("iyzico cancel answer for transaction {TransactionId} does not match the payment.", tx.Id);
            tx.LastErrorCode = "CANCEL_UNVERIFIED";
            tx.LastErrorMessage = null;
            return CancelOutcome.Unknown;
        }

        tx.Status = IyzicoTransactionStatus.Cancelled;
        tx.RefundedAmount = tx.PaidPrice ?? tx.Amount;
        Touch(tx, null);
        _db.OrderNotes.Add(order, $"iyzico: payment {tx.PaymentId} cancelled ({FormatAmount(tx.RefundedAmount, tx.Currency)}).");
        await _db.SaveChangesAsync(cancelToken);

        return CancelOutcome.Cancelled;
    }

    /// <returns><c>null</c> if the refund answer is genuine and matches the request, otherwise the failed check.</returns>
    private string VerifyRefundResponse(RefundResponse response, IyzicoTransaction tx, IyzicoItemTransaction item, string conversationId, decimal part, string secretKey)
    {
        if (!_credentialProvider.SkipResponseSignatureVerification &&
            !IyzicoSignature.SignatureEquals(IyzicoSignature.ComputeRefundSignature(secretKey, response), response.Signature))
        {
            return "SIGNATURE";
        }
        if (!string.Equals(response.ConversationId, conversationId, StringComparison.Ordinal))
        {
            return "CONVERSATION";
        }
        if (!string.Equals(response.PaymentTransactionId, item.PaymentTransactionId, StringComparison.Ordinal))
        {
            return "TRANSACTION";
        }
        if (!string.Equals(response.PaymentId, tx.PaymentId, StringComparison.Ordinal))
        {
            return "PAYMENTID";
        }
        if (response.Price == null || IyzicoRequestBuilder.Round(response.Price.Value) != IyzicoRequestBuilder.Round(part))
        {
            return "AMOUNT";
        }
        if (!string.Equals(response.Currency, tx.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return "CURRENCY";
        }

        return null;
    }

    private async Task SaveUnknownRefundAsync(
        IyzicoTransaction tx,
        Order order,
        List<IyzicoItemTransaction> items,
        IyzicoItemTransaction item,
        string conversationId,
        decimal part,
        decimal requested,
        decimal left,
        string errorCode,
        CancellationToken cancelToken)
    {
        item.PendingRefundConversationId = conversationId;
        item.PendingRefundAmount = part;
        item.PendingRefundIsCancel = false;
        item.PendingRefundSinceUtc = UtcNow();

        _db.OrderNotes.Add(order, $"iyzico: refund of {FormatAmount(part, tx.Currency)} (item transaction {item.PaymentTransactionId}) has an unknown outcome ({errorCode}). iyzico may have processed it. Further refunds are blocked until it is confirmed (transaction report) or resolved on the iyzico configuration page.");
        await SaveRefundProgressAsync(tx, order, items, requested, left, errorCode, cancelToken);
    }

    private async Task SaveRefundProgressAsync(IyzicoTransaction tx, Order order, List<IyzicoItemTransaction> items, decimal requested, decimal left, string errorCode, CancellationToken cancelToken)
    {
        tx.ItemTransactionsJson = JsonSerializer.Serialize(items);
        tx.LastErrorCode = errorCode.Truncate(50);
        Touch(tx, null);

        var done = requested - left;
        if (done > 0m)
        {
            _db.OrderNotes.Add(order, $"iyzico: refund partly processed. {FormatAmount(done, tx.Currency)} of {FormatAmount(requested, tx.Currency)} was refunded at iyzico; record it as an offline refund before retrying the rest.");
        }

        await _db.SaveChangesAsync(cancelToken);
    }

    private static List<IyzicoItemTransaction> DeserializeItems(IyzicoTransaction tx)
    {
        if (tx.ItemTransactionsJson.IsEmpty())
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<IyzicoItemTransaction>>(tx.ItemTransactionsJson) ?? [];
    }

    private static bool IsSameTurkeyDay(DateTime? paidOnUtc, DateTime nowUtc)
        => paidOnUtc.HasValue && (paidOnUtc.Value + TurkeyOffset).Date == (nowUtc + TurkeyOffset).Date;

    #endregion

    #region Unconfirmed refunds

    /// <summary>
    /// Lists refunds and cancels with an unknown outcome, for the admin page.
    /// </summary>
    public async Task<List<IyzicoPendingRefund>> GetPendingRefundsAsync(int max = 50, CancellationToken cancelToken = default)
    {
        var txs = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x => x.ItemTransactionsJson.Contains(PendingRefundJsonMarker))
            .OrderBy(x => x.Id)
            .Take(max)
            .ToListAsync(cancelToken);

        return txs
            .SelectMany(tx => DeserializeItems(tx)
                .Where(x => x.HasPendingRefund)
                .GroupBy(x => x.PendingRefundConversationId)
                .Select(g => new IyzicoPendingRefund
                {
                    TransactionId = tx.Id,
                    OrderId = tx.OrderId,
                    PaymentId = tx.PaymentId,
                    ConversationId = g.Key,
                    IsCancel = g.First().PendingRefundIsCancel,
                    PaymentTransactionIds = string.Join(", ", g.Select(x => x.PaymentTransactionId)),
                    Amount = g.Sum(x => x.PendingRefundAmount ?? 0m),
                    Currency = tx.Currency,
                    SinceUtc = g.Min(x => x.PendingRefundSinceUtc)
                }))
            .ToList();
    }

    /// <summary>
    /// Asks the iyzico transaction report whether the unconfirmed refunds of a transaction were processed.
    /// Only confirms processed refunds; "not processed" always needs an admin decision.
    /// </summary>
    /// <returns>Number of operations confirmed as processed.</returns>
    public async Task<int> CheckPendingRefundsAsync(int transactionId, CancellationToken cancelToken = default)
    {
        var orderId = await _db.IyzicoTransactions().Where(x => x.Id == transactionId).Select(x => (int?)x.OrderId).FirstOrDefaultAsync(cancelToken);
        if (orderId == null)
        {
            return 0;
        }

        await using var refundLock = await AcquireRefundLockAsync(orderId.Value, cancelToken);

        var tx = await _db.IyzicoTransactions().FirstAsync(x => x.Id == transactionId, cancelToken);
        await _db.Entry(tx).ReloadAsync(cancelToken);

        var items = DeserializeItems(tx);
        if (!items.Any(x => x.HasPendingRefund))
        {
            return 0;
        }

        var credentials = await _credentialProvider.GetCredentialsAsync(tx.StoreId);
        if (!credentials.IsComplete)
        {
            return 0;
        }

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == tx.OrderId, cancelToken);
        return await ConfirmFromReportAsync(tx, order, items, credentials, cancelToken);
    }

    /// <summary>
    /// Admin decision on a refund or cancel with an unknown outcome, after checking the iyzico merchant panel.
    /// </summary>
    /// <param name="processed"><c>true</c>: iyzico processed it (counted as refunded). <c>false</c>: it was not processed (refundable again).</param>
    /// <returns><c>false</c> if there is no such unconfirmed operation (already resolved).</returns>
    public async Task<bool> ResolvePendingRefundAsync(int transactionId, string conversationId, bool processed, string resolvedBy, CancellationToken cancelToken = default)
    {
        if (conversationId.IsEmpty())
        {
            return false;
        }

        var orderId = await _db.IyzicoTransactions().Where(x => x.Id == transactionId).Select(x => (int?)x.OrderId).FirstOrDefaultAsync(cancelToken);
        if (orderId == null)
        {
            return false;
        }

        await using var refundLock = await AcquireRefundLockAsync(orderId.Value, cancelToken);

        var tx = await _db.IyzicoTransactions().FirstAsync(x => x.Id == transactionId, cancelToken);
        await _db.Entry(tx).ReloadAsync(cancelToken);

        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == tx.OrderId, cancelToken);
        var items = DeserializeItems(tx);
        if (!ApplyResolvedRefund(tx, order, items, conversationId, processed, $"resolved by {resolvedBy}"))
        {
            return false;
        }

        await _db.SaveChangesAsync(cancelToken);
        Logger.LogWarning("iyzico unconfirmed refund {ConversationId} of transaction {TransactionId} resolved as {Resolution} by {ResolvedBy}.",
            conversationId, tx.Id, processed ? "processed" : "not processed", resolvedBy);

        return true;
    }

    /// <summary>
    /// Re-checks unconfirmed refunds older than a few minutes. Called by the reconciliation task.
    /// </summary>
    /// <returns>Number of operations confirmed as processed.</returns>
    public async Task<int> ReconcilePendingRefundsAsync(int maxTransactions = 20, CancellationToken cancelToken = default)
    {
        var minAge = UtcNow().AddMinutes(-2);
        var ids = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x => x.ItemTransactionsJson.Contains(PendingRefundJsonMarker) && x.UpdatedOnUtc < minAge)
            .OrderBy(x => x.UpdatedOnUtc)
            .Take(maxTransactions)
            .Select(x => x.Id)
            .ToListAsync(cancelToken);

        var confirmed = 0;
        foreach (var id in ids)
        {
            cancelToken.ThrowIfCancellationRequested();

            try
            {
                confirmed += await CheckPendingRefundsAsync(id, cancelToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "iyzico: checking unconfirmed refunds of transaction {TransactionId} failed.", id);
                DetachAll();
            }
        }

        return confirmed;
    }

    private async Task<int> ConfirmFromReportAsync(IyzicoTransaction tx, Order order, List<IyzicoItemTransaction> items, IyzicoCredentials credentials, CancellationToken cancelToken)
    {
        var pending = items.Where(x => x.HasPendingRefund).ToList();
        if (pending.Count == 0 || tx.PaymentId.IsEmpty())
        {
            return 0;
        }

        PaymentDetailsReportResponse report;
        try
        {
            report = await _api.RetrievePaymentDetailsAsync(credentials, tx.PaymentId, cancelToken);
        }
        catch (IyzicoApiException ex)
        {
            Logger.LogWarning(ex, "iyzico transaction report failed for transaction {TransactionId}.", tx.Id);
            return 0;
        }

        if (!report.IsSuccess)
        {
            Logger.LogWarning("iyzico transaction report rejected for transaction {TransactionId}: {ErrorCode}.", tx.Id, report.ErrorCode);
            return 0;
        }

        var payment = report.Payments?.FirstOrDefault(x => string.Equals(x.PaymentId, tx.PaymentId, StringComparison.Ordinal));
        if (payment == null)
        {
            return 0;
        }

        var confirmed = 0;
        foreach (var group in pending.GroupBy(x => x.PendingRefundConversationId).ToList())
        {
            var first = group.First();
            var total = group.Sum(x => x.PendingRefundAmount ?? 0m);

            var processed = first.PendingRefundIsCancel
                ? (payment.Cancels ?? []).Any(c =>
                    string.Equals(c.CancelConversationId, group.Key, StringComparison.Ordinal) &&
                    IsReportedAsProcessed(c.RefundStatus, c.RefundPrice, c.CurrencyCode, total, tx.Currency))
                : (payment.ItemTransactions ?? [])
                    .Where(x => string.Equals(x.PaymentTransactionId, first.PaymentTransactionId, StringComparison.Ordinal))
                    .SelectMany(x => x.Refunds ?? [])
                    .Any(r =>
                        string.Equals(r.RefundConversationId, group.Key, StringComparison.Ordinal) &&
                        IsReportedAsProcessed(r.RefundStatus, r.RefundPrice, r.CurrencyCode, total, tx.Currency));

            if (processed && ApplyResolvedRefund(tx, order, items, group.Key, true, "confirmed by the iyzico transaction report"))
            {
                confirmed++;
            }
        }

        if (confirmed > 0)
        {
            await _db.SaveChangesAsync(cancelToken);
        }

        return confirmed;
    }

    private static bool IsReportedAsProcessed(int? status, decimal? price, string currency, decimal expectedAmount, string expectedCurrency)
        => status == ReportedRefundSuccess
        && price.HasValue
        && IyzicoRequestBuilder.Round(price.Value) == IyzicoRequestBuilder.Round(expectedAmount)
        && (currency.IsEmpty() || string.Equals(currency, expectedCurrency, StringComparison.OrdinalIgnoreCase));

    private bool ApplyResolvedRefund(IyzicoTransaction tx, Order order, List<IyzicoItemTransaction> items, string conversationId, bool processed, string reason)
    {
        var group = items.Where(x => string.Equals(x.PendingRefundConversationId, conversationId, StringComparison.Ordinal)).ToList();
        if (group.Count == 0)
        {
            return false;
        }

        var isCancel = group[0].PendingRefundIsCancel;
        var amount = group.Sum(x => x.PendingRefundAmount ?? 0m);
        var paid = tx.PaidPrice ?? tx.Amount;
        var kind = isCancel ? "cancel" : "refund";

        if (processed)
        {
            foreach (var item in group)
            {
                item.RefundedAmount = Math.Min(item.PaidPrice, item.RefundedAmount + (item.PendingRefundAmount ?? 0m));
            }

            tx.RefundedAmount = Math.Min(paid, tx.RefundedAmount + amount);
            if (isCancel)
            {
                tx.Status = IyzicoTransactionStatus.Cancelled;
            }
        }

        group.Each(x => x.ClearPendingRefund());
        tx.ItemTransactionsJson = JsonSerializer.Serialize(items);
        if (!items.Any(x => x.HasPendingRefund))
        {
            tx.LastErrorCode = null;
            tx.LastErrorMessage = null;
        }
        Touch(tx, null);

        _db.OrderNotes.Add(order, processed
            ? $"iyzico: the unconfirmed {kind} of {FormatAmount(amount, tx.Currency)} was processed by iyzico ({reason}). It is counted as refunded in the iyzico module; record it in Smartstore as an offline refund."
            : $"iyzico: the unconfirmed {kind} of {FormatAmount(amount, tx.Currency)} was not processed by iyzico ({reason}). The amount can be refunded again.");

        return true;
    }

    #endregion

    #region Reconciliation

    /// <summary>
    /// Re-checks open transactions, for when neither the callback nor a webhook arrived
    /// (customer closed the browser, webhooks disabled). Expires stale unpaid attempts, cancels stale unpaid orders
    /// and asks the transaction report about refunds with an unknown outcome.
    /// </summary>
    public async Task<int> ReconcileAsync(int maxTransactions = 50, CancellationToken cancelToken = default)
    {
        var now = UtcNow();
        var minAge = now.AddMinutes(-2);
        var maxAge = now.AddDays(-30);

        var open = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x =>
                (x.Status == IyzicoTransactionStatus.Initialized || x.Status == IyzicoTransactionStatus.Pending || x.Status == IyzicoTransactionStatus.FraudReview) &&
                x.UpdatedOnUtc < minAge &&
                x.CreatedOnUtc > maxAge)
            .OrderBy(x => x.UpdatedOnUtc)
            .Take(maxTransactions)
            .Select(x => new { x.Id, x.Token, x.CreatedOnUtc })
            .ToListAsync(cancelToken);

        foreach (var item in open)
        {
            cancelToken.ThrowIfCancellationRequested();

            var result = await ProcessTokenAsync(item.Token, IyzicoEventSource.Reconciliation, cancelToken);

            if (result.Outcome is IyzicoOutcome.Pending or IyzicoOutcome.Failed && item.CreatedOnUtc < now.AddHours(-3))
            {
                var tx = await _db.IyzicoTransactions().FirstOrDefaultAsync(x => x.Id == item.Id, cancelToken);
                if (tx != null && tx.Status is IyzicoTransactionStatus.Pending or IyzicoTransactionStatus.Initialized or IyzicoTransactionStatus.Failed)
                {
                    tx.Status = IyzicoTransactionStatus.Expired;
                    Touch(tx, null);
                    await _db.SaveChangesAsync(cancelToken);
                }
            }
        }

        await CancelStaleUnpaidOrdersAsync(maxTransactions, cancelToken);
        await ReconcilePendingRefundsAsync(20, cancelToken);

        return open.Count;
    }

    /// <summary>
    /// Cancels iyzico orders that stayed unpaid longer than <see cref="IyzicoSettings.CancelUnpaidOrdersAfterHours"/>
    /// and have no open, paid or disputed attempt. Cancelling restocks the items (Smartstore CancelOrderAsync).
    /// A failed attempt alone never cancels an order: the customer can retry the payment until then.
    /// </summary>
    public async Task<int> CancelStaleUnpaidOrdersAsync(int maxOrders = 50, CancellationToken cancelToken = default)
    {
        var now = UtcNow();
        var minAge = now.AddHours(-1);

        var candidates = await _db.Orders
            .AsNoTracking()
            .Where(x =>
                !x.Deleted &&
                x.PaymentMethodSystemName == IyzicoProvider.SystemName &&
                x.PaymentStatusId == (int)PaymentStatus.Pending &&
                x.OrderStatusId == (int)OrderStatus.Pending &&
                x.CreatedOnUtc < minAge)
            .OrderBy(x => x.CreatedOnUtc)
            .Take(maxOrders)
            .Select(x => new { x.Id, x.StoreId, x.CreatedOnUtc })
            .ToListAsync(cancelToken);

        var cancelled = 0;

        foreach (var candidate in candidates)
        {
            cancelToken.ThrowIfCancellationRequested();

            var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(candidate.StoreId);
            if (settings.CancelUnpaidOrdersAfterHours <= 0 || candidate.CreatedOnUtc > now.AddHours(-settings.CancelUnpaidOrdersAfterHours))
            {
                continue;
            }

            // Settle open attempts first, outside the order lock (processing takes token, then order lock).
            var openTokens = await _db.IyzicoTransactions()
                .AsNoTracking()
                .Where(x => x.OrderId == candidate.Id && (x.Status == IyzicoTransactionStatus.Initialized || x.Status == IyzicoTransactionStatus.Pending))
                .Select(x => x.Token)
                .ToListAsync(cancelToken);

            foreach (var token in openTokens)
            {
                await ProcessTokenAsync(token, IyzicoEventSource.Reconciliation, cancelToken);
            }

            await using var orderLock = await AcquireOrderLockAsync(candidate.Id, cancelToken);

            var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == candidate.Id, cancelToken);
            if (order == null)
            {
                continue;
            }

            await _db.Entry(order).ReloadAsync(cancelToken);

            var statuses = await _db.IyzicoTransactions()
                .AsNoTracking()
                .Where(x => x.OrderId == order.Id)
                .Select(x => x.Status)
                .ToListAsync(cancelToken);

            var onlyDeadAttempts = statuses.All(x => x is IyzicoTransactionStatus.Failed or IyzicoTransactionStatus.Expired);
            if (!onlyDeadAttempts || order.PaymentStatus != PaymentStatus.Pending || !order.CanCancelOrder() || order.OrderStatus != OrderStatus.Pending)
            {
                continue;
            }

            try
            {
                await _orderProcessingService.CancelOrderAsync(order, true);
                _db.OrderNotes.Add(order, $"iyzico: order cancelled automatically because it was not paid within {settings.CancelUnpaidOrdersAfterHours} hours. Reserved stock was released.");
                await _db.SaveChangesAsync(cancelToken);
                cancelled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "iyzico: could not cancel unpaid order {OrderId}.", order.Id);
                DetachAll();
            }
        }

        return cancelled;
    }

    #endregion

    #region Utilities

    private Task<ILockHandle> AcquireOrderLockAsync(int orderId, CancellationToken cancelToken)
        => _lockProvider.GetLock($"motorshop.iyzico.order:{orderId}").AcquireAsync(LockTimeout, cancelToken);

    /// <summary>
    /// Serializes refunds, voids and the resolution of unconfirmed refunds of one order.
    /// </summary>
    private Task<ILockHandle> AcquireRefundLockAsync(int orderId, CancellationToken cancelToken)
        => _lockProvider.GetLock($"motorshop.iyzico.refund:{orderId}").AcquireAsync(LockTimeout, cancelToken);

    private void Touch(IyzicoTransaction tx, IyzicoVerdict verdict)
    {
        if (verdict != null)
        {
            tx.LastErrorCode = verdict.ErrorCode.Truncate(50);
            tx.LastErrorMessage = verdict.Message.Truncate(500);
        }

        tx.UpdatedOnUtc = UtcNow();
        tx.Version++;
    }

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(x => x.Entity is IyzicoTransaction || x.Entity is Order || x.Entity is OrderNote).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static IyzicoProcessResult Result(IyzicoOutcome outcome, Order order)
        => new(outcome, order?.Id, order?.OrderGuid);

    private static string FormatAmount(decimal? amount, string currency)
        => $"{(amount ?? 0m).ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    #endregion
}
