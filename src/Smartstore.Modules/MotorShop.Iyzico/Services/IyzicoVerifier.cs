using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Domain;

namespace MotorShop.Iyzico.Services;

public sealed record IyzicoVerdict(IyzicoTransactionStatus Status, string ErrorCode = null, string Message = null);

/// <summary>
/// Decides what a checkout form retrieve result means for a stored transaction.
/// Pure function without side effects so that every rule is unit tested.
/// </summary>
public static class IyzicoVerifier
{
    public const int FraudApproved = 1;
    public const int FraudInReview = 0;
    public const int FraudRejected = -1;

    public static IyzicoVerdict Evaluate(IyzicoTransaction tx, CheckoutFormResult result, string secretKey, bool verifySignature)
    {
        ArgumentNullException.ThrowIfNull(tx);

        if (result == null)
        {
            return new(IyzicoTransactionStatus.Failed, "EMPTY", "Empty retrieve response.");
        }

        if (!result.IsSuccess)
        {
            // The retrieve call itself failed, e.g. the customer never paid or the token expired. Not terminal.
            return new(IyzicoTransactionStatus.Failed, result.ErrorCode.NullEmpty() ?? "RETRIEVE_FAILURE", result.ErrorMessage);
        }

        var paymentStatus = result.PaymentStatus?.ToUpperInvariant();
        var claimsSuccess = paymentStatus == "SUCCESS";
        // A success claim that cannot be verified needs manual review. Anything else stays retryable.
        var mismatchStatus = claimsSuccess ? IyzicoTransactionStatus.VerificationFailed : IyzicoTransactionStatus.Failed;

        if (verifySignature)
        {
            var expected = IyzicoSignature.ComputeRetrieveSignature(secretKey, result);
            if (!IyzicoSignature.SignatureEquals(expected, result.Signature))
            {
                return new(mismatchStatus, "SIGNATURE", "Response signature is missing or invalid.");
            }
        }

        if (!string.Equals(result.Token, tx.Token, StringComparison.Ordinal))
        {
            return new(mismatchStatus, "TOKEN", "Token does not match the stored transaction.");
        }

        if (!string.Equals(result.ConversationId, tx.ConversationId, StringComparison.Ordinal))
        {
            return new(mismatchStatus, "CONVERSATION", "conversationId does not match the stored transaction.");
        }

        if (!string.Equals(result.BasketId, tx.BasketId, StringComparison.OrdinalIgnoreCase))
        {
            return new(mismatchStatus, "BASKET", "basketId does not match the order.");
        }

        switch (paymentStatus)
        {
            case "SUCCESS":
                if (result.PaymentId.IsEmpty())
                {
                    return new(IyzicoTransactionStatus.VerificationFailed, "PAYMENTID", "Successful payment without paymentId.");
                }
                if (!string.Equals(result.Currency, tx.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    return new(IyzicoTransactionStatus.VerificationFailed, "CURRENCY", $"Currency {result.Currency} does not match {tx.Currency}.");
                }
                if (result.Price == null || IyzicoRequestBuilder.Round(result.Price.Value) != IyzicoRequestBuilder.Round(tx.Amount))
                {
                    return new(IyzicoTransactionStatus.VerificationFailed, "AMOUNT", $"Price {result.Price} does not match {tx.Amount}.");
                }
                // paidPrice may exceed price by the installment commission passed on to the customer, never fall below it.
                if (result.PaidPrice == null || IyzicoRequestBuilder.Round(result.PaidPrice.Value) < IyzicoRequestBuilder.Round(tx.Amount))
                {
                    return new(IyzicoTransactionStatus.VerificationFailed, "PAIDPRICE", $"Paid price {result.PaidPrice} is below {tx.Amount}.");
                }

                return result.FraudStatus switch
                {
                    FraudApproved => new(IyzicoTransactionStatus.Paid),
                    FraudRejected => new(IyzicoTransactionStatus.Failed, "FRAUD_REJECTED", "Payment rejected by the iyzico fraud check."),
                    // 0 = in review. A missing value is treated the same way: do not ship until approved.
                    _ => new(IyzicoTransactionStatus.FraudReview, "FRAUD_REVIEW", "Payment is held by the iyzico fraud check.")
                };

            case "FAILURE":
                return new(IyzicoTransactionStatus.Failed, result.ErrorCode.NullEmpty() ?? "FAILURE", result.ErrorMessage);

            default:
                // INIT_THREEDS, CALLBACK_THREEDS, INIT_BANK_TRANSFER... payment not completed yet.
                return new(IyzicoTransactionStatus.Pending, paymentStatus, "Payment not completed yet.");
        }
    }
}
