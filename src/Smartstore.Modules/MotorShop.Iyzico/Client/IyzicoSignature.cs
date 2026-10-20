using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MotorShop.Iyzico.Client;

/// <summary>
/// iyzico request authentication and signature checks.
/// </summary>
/// <remarks>
/// Sources (checked 2026-10-19):
/// - IYZWSv2 auth: https://docs.iyzico.com/en/getting-started/preliminaries/authentication/hmacsha256-auth
///   and iyzipay-dotnet HashGeneratorV2 / IyzipayResourceV2.
/// - Response signatures: https://docs.iyzico.com/en/advanced/response-signature-validation
///   and iyzipay-php samples/signature_verification.php (values joined with ':', HMAC-SHA256, hex).
/// - Webhook X-IYZ-SIGNATURE-V3: https://docs.iyzico.com/en/advanced/webhook
///   and iyzipay-php samples/webhook_Signature_Validation.php.
/// </remarks>
public static class IyzicoSignature
{
    public const string AuthorizationScheme = "IYZWSv2";

    /// <summary>
    /// Builds the IYZWSv2 Authorization header value:
    /// "IYZWSv2 " + base64("apiKey:{apiKey}&amp;randomKey:{rnd}&amp;signature:{hex(HMACSHA256(secretKey, rnd + uriPath + body))}").
    /// </summary>
    public static string CreateAuthorization(string apiKey, string secretKey, string randomKey, string uriPath, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(secretKey);
        ArgumentException.ThrowIfNullOrEmpty(randomKey);

        var signature = HmacSha256Hex(secretKey, randomKey + uriPath + body);
        var authorization = $"apiKey:{apiKey}&randomKey:{randomKey}&signature:{signature}";

        return AuthorizationScheme + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(authorization));
    }

    public static string CreateRandomKey()
    {
        // Same shape as the official SDKs (time based prefix) plus cryptographic randomness.
        return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
            + RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Signature of the checkout form initialize response: conversationId:token.
    /// </summary>
    public static string ComputeInitializeSignature(string secretKey, string conversationId, string token)
        => ComputeResponseSignature(secretKey, conversationId, token);

    /// <summary>
    /// Signature of the checkout form retrieve response:
    /// paymentStatus:paymentId:currency:basketId:conversationId:paidPrice:price:token (prices without trailing zeros).
    /// </summary>
    public static string ComputeRetrieveSignature(string secretKey, CheckoutFormResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ComputeResponseSignature(
            secretKey,
            result.PaymentStatus,
            result.PaymentId,
            result.Currency,
            result.BasketId,
            result.ConversationId,
            FormatPriceForSignature(result.PaidPrice),
            FormatPriceForSignature(result.Price),
            result.Token);
    }

    /// <summary>
    /// Signature of the refund response (/payment/refund): paymentId:price:currency:conversationId (price without trailing zeros).
    /// The cancel response has no documented signature.
    /// </summary>
    public static string ComputeRefundSignature(string secretKey, RefundResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return ComputeResponseSignature(
            secretKey,
            response.PaymentId,
            FormatPriceForSignature(response.Price),
            response.Currency,
            response.ConversationId);
    }

    public static string ComputeResponseSignature(string secretKey, params string[] values)
        => HmacSha256Hex(secretKey, string.Join(':', values.Select(x => x ?? string.Empty)));

    /// <summary>
    /// X-IYZ-SIGNATURE-V3 for hosted payment pages (Checkout Form):
    /// hex(HMACSHA256(secretKey, secretKey + iyziEventType + iyziPaymentId + token + paymentConversationId + status)).
    /// </summary>
    public static string ComputeWebhookSignature(string secretKey, IyzicoWebhookPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var data = secretKey
            + payload.IyziEventType
            + payload.IyziPaymentId
            + payload.Token
            + payload.PaymentConversationId
            + payload.Status;

        return HmacSha256Hex(secretKey, data);
    }

    /// <summary>
    /// Constant-time, case-insensitive comparison of two hex signatures.
    /// </summary>
    public static bool SignatureEquals(string expectedHex, string actualHex)
    {
        if (expectedHex.IsEmpty() || actualHex.IsEmpty())
        {
            return false;
        }

        var a = Encoding.ASCII.GetBytes(expectedHex.Trim().ToLowerInvariant());
        var b = Encoding.ASCII.GetBytes(actualHex.Trim().ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Price as used in response signatures: no trailing zeros ("10.50" -> "10.5", "10.0" -> "10").
    /// </summary>
    public static string FormatPriceForSignature(decimal? price)
    {
        if (price == null)
        {
            return string.Empty;
        }

        return price.Value.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Price as sent in requests, formatted like the official SDKs: invariant, at least one decimal place ("10" -> "10.0").
    /// </summary>
    public static string FormatPriceForRequest(decimal price)
    {
        var str = FormatPriceForSignature(price);
        return str.Contains('.') ? str : str + ".0";
    }

    private static string HmacSha256Hex(string key, string data)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
