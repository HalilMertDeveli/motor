using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Domain;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// Deterministic stand-in for the iyzico API. Signs responses with the test secret key like iyzico does.
/// </summary>
public class FakeIyzicoApi : IIyzicoApiClient
{
    private readonly string _secretKey;
    private int _retrieveCalls;
    private int _initializeCalls;

    public FakeIyzicoApi(string secretKey)
    {
        _secretKey = secretKey;
    }

    public int RetrieveCalls => _retrieveCalls;
    public int InitializeCalls => _initializeCalls;
    public TimeSpan RetrieveDelay { get; set; }

    public Func<RetrieveCheckoutFormRequest, CheckoutFormResult> OnRetrieve { get; set; }
    public Func<CheckoutFormInitializeRequest, CheckoutFormInitializeResponse> OnInitialize { get; set; }
    public Func<RefundRequest, RefundResponse> OnRefund { get; set; }
    public Func<CancelRequest, CancelResponse> OnCancel { get; set; }

    public CheckoutFormInitializeRequest LastInitializeRequest { get; private set; }
    public ConcurrentQueue<RefundRequest> RefundRequests { get; } = new();
    public ConcurrentQueue<CancelRequest> CancelRequests { get; } = new();

    public Task<CheckoutFormInitializeResponse> InitializeCheckoutFormAsync(IyzicoCredentials credentials, CheckoutFormInitializeRequest request, CancellationToken cancelToken = default)
    {
        Interlocked.Increment(ref _initializeCalls);
        LastInitializeRequest = request;

        var response = OnInitialize?.Invoke(request) ?? InitializeSuccess(request);
        return Task.FromResult(response);
    }

    public async Task<CheckoutFormResult> RetrieveCheckoutFormAsync(IyzicoCredentials credentials, RetrieveCheckoutFormRequest request, CancellationToken cancelToken = default)
    {
        Interlocked.Increment(ref _retrieveCalls);

        if (RetrieveDelay > TimeSpan.Zero)
        {
            await Task.Delay(RetrieveDelay, cancelToken);
        }

        return OnRetrieve?.Invoke(request) ?? throw new InvalidOperationException("No retrieve result configured.");
    }

    public Task<RefundResponse> RefundAsync(IyzicoCredentials credentials, RefundRequest request, CancellationToken cancelToken = default)
    {
        RefundRequests.Enqueue(request);

        var response = OnRefund?.Invoke(request) ?? new RefundResponse
        {
            Status = "success",
            ConversationId = request.ConversationId,
            PaymentTransactionId = request.PaymentTransactionId,
            Price = decimal.Parse(request.Price, System.Globalization.CultureInfo.InvariantCulture),
            Currency = request.Currency
        };

        return Task.FromResult(response);
    }

    public Task<CancelResponse> CancelAsync(IyzicoCredentials credentials, CancelRequest request, CancellationToken cancelToken = default)
    {
        CancelRequests.Enqueue(request);

        var response = OnCancel?.Invoke(request) ?? new CancelResponse
        {
            Status = "success",
            ConversationId = request.ConversationId,
            PaymentId = request.PaymentId
        };

        return Task.FromResult(response);
    }

    public CheckoutFormInitializeResponse InitializeSuccess(CheckoutFormInitializeRequest request, string token = null)
    {
        token ??= "tok-" + Guid.NewGuid().ToString("N");

        return new CheckoutFormInitializeResponse
        {
            Status = "success",
            ConversationId = request.ConversationId,
            Token = token,
            PaymentPageUrl = "https://sandbox-cpp.iyzipay.com?token=" + token + "&lang=tr",
            Signature = IyzicoSignature.ComputeInitializeSignature(_secretKey, request.ConversationId, token)
        };
    }

    /// <summary>
    /// A correctly signed retrieve result for <paramref name="tx"/>. Change fields afterwards and call <see cref="Sign"/>
    /// to simulate a genuine but different iyzico answer, or leave the signature stale to simulate tampering.
    /// </summary>
    public CheckoutFormResult Result(
        IyzicoTransaction tx,
        string paymentStatus = "SUCCESS",
        int? fraudStatus = 1,
        int installment = 1,
        decimal? paidPrice = null,
        string paymentId = "24681357",
        params (string TransactionId, decimal PaidPrice)[] items)
    {
        var itemList = items.Length > 0
            ? items.Select(x => new CheckoutFormItemTransaction { PaymentTransactionId = x.TransactionId, PaidPrice = x.PaidPrice, Price = x.PaidPrice, TransactionStatus = 1 }).ToList()
            : [new CheckoutFormItemTransaction { PaymentTransactionId = "990001", PaidPrice = paidPrice ?? tx.Amount, Price = tx.Amount, TransactionStatus = 1 }];

        var success = paymentStatus == "SUCCESS";
        var result = new CheckoutFormResult
        {
            Status = "success",
            Locale = "en",
            ConversationId = tx.ConversationId,
            Token = tx.Token,
            BasketId = tx.BasketId,
            PaymentStatus = paymentStatus,
            PaymentId = success ? paymentId : null,
            Price = tx.Amount,
            PaidPrice = success ? (paidPrice ?? tx.Amount) : null,
            Currency = tx.Currency,
            Installment = installment,
            FraudStatus = success ? fraudStatus : null,
            ErrorCode = success ? null : "10051",
            ErrorMessage = success ? null : "Kart limiti yetersiz",
            ItemTransactions = success ? itemList : null
        };

        return Sign(result);
    }

    public CheckoutFormResult Sign(CheckoutFormResult result)
    {
        result.Signature = IyzicoSignature.ComputeRetrieveSignature(_secretKey, result);
        return result;
    }

    public static CheckoutFormResult RetrieveFailure(string errorCode = "5131", string message = "Token not found")
        => new() { Status = "failure", ErrorCode = errorCode, ErrorMessage = message };
}
