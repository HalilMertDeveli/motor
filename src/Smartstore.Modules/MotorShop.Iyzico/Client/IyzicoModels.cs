using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorShop.Iyzico.Client;

// Request and response shapes of the iyzico REST API (https://docs.iyzico.com).
// Response models deliberately omit card fields (binNumber, lastFourDigits, cardToken...):
// the module never reads, stores or logs card data.

public sealed class IyzicoCredentials
{
    public IyzicoCredentials(string apiKey, string secretKey, string baseUrl)
    {
        ApiKey = apiKey;
        SecretKey = secretKey;
        BaseUrl = baseUrl?.TrimEnd('/');
    }

    public string ApiKey { get; }
    public string SecretKey { get; }
    public string BaseUrl { get; }

    public bool IsComplete
        => ApiKey.HasValue() && SecretKey.HasValue() && BaseUrl.HasValue();

    // Keeps secrets out of accidental string formatting and logging.
    public override string ToString()
        => $"IyzicoCredentials({BaseUrl})";
}

public class IyzicoResponse
{
    public string Status { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public string ErrorGroup { get; set; }
    public string Locale { get; set; }
    public long? SystemTime { get; set; }
    public string ConversationId { get; set; }
    public string Signature { get; set; }

    [JsonIgnore]
    public bool IsSuccess
        => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}

public sealed class CheckoutFormInitializeRequest
{
    public string Locale { get; set; }
    public string ConversationId { get; set; }
    public string Price { get; set; }
    public string PaidPrice { get; set; }
    public string Currency { get; set; }
    public string BasketId { get; set; }
    public string PaymentGroup { get; set; } = "PRODUCT";
    public string CallbackUrl { get; set; }
    public List<int> EnabledInstallments { get; set; }
    public IyzicoBuyer Buyer { get; set; }
    public IyzicoAddress ShippingAddress { get; set; }
    public IyzicoAddress BillingAddress { get; set; }
    public List<IyzicoBasketItem> BasketItems { get; set; }
}

public sealed class IyzicoBuyer
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Surname { get; set; }
    public string GsmNumber { get; set; }
    public string Email { get; set; }
    public string IdentityNumber { get; set; }
    public string LastLoginDate { get; set; }
    public string RegistrationDate { get; set; }
    public string RegistrationAddress { get; set; }
    public string Ip { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public string ZipCode { get; set; }
}

public sealed class IyzicoAddress
{
    public string ContactName { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public string Address { get; set; }
    public string ZipCode { get; set; }
}

public sealed class IyzicoBasketItem
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Category1 { get; set; }
    public string Category2 { get; set; }
    public string ItemType { get; set; }
    public string Price { get; set; }
}

public sealed class CheckoutFormInitializeResponse : IyzicoResponse
{
    public string Token { get; set; }
    public long? TokenExpireTime { get; set; }
    public string PaymentPageUrl { get; set; }
}

public sealed class RetrieveCheckoutFormRequest
{
    public string Locale { get; set; }
    public string ConversationId { get; set; }
    public string Token { get; set; }
}

public sealed class CheckoutFormResult : IyzicoResponse
{
    public string Token { get; set; }
    public string PaymentStatus { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentId { get; set; }

    public decimal? Price { get; set; }
    public decimal? PaidPrice { get; set; }
    public string Currency { get; set; }
    public string BasketId { get; set; }
    public int? Installment { get; set; }
    public int? FraudStatus { get; set; }
    public int? MdStatus { get; set; }
    public List<CheckoutFormItemTransaction> ItemTransactions { get; set; }
}

public sealed class CheckoutFormItemTransaction
{
    public string ItemId { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentTransactionId { get; set; }

    public decimal? Price { get; set; }
    public decimal? PaidPrice { get; set; }
    public int? TransactionStatus { get; set; }
}

public sealed class RefundRequest
{
    public string Locale { get; set; }
    public string ConversationId { get; set; }
    public string PaymentTransactionId { get; set; }
    public string Price { get; set; }
    public string Currency { get; set; }
    public string Ip { get; set; }
}

public sealed class RefundResponse : IyzicoResponse
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentId { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentTransactionId { get; set; }

    public decimal? Price { get; set; }
    public string Currency { get; set; }
}

public sealed class CancelRequest
{
    public string Locale { get; set; }
    public string ConversationId { get; set; }
    public string PaymentId { get; set; }
    public string Ip { get; set; }
}

public sealed class CancelResponse : IyzicoResponse
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentId { get; set; }

    public decimal? Price { get; set; }
    public string Currency { get; set; }
}

/// <summary>
/// Response of the transaction detail report (GET /v2/reporting/payment/details).
/// Used to find out whether a refund or cancel with an unknown outcome was processed.
/// </summary>
public sealed class PaymentDetailsReportResponse : IyzicoResponse
{
    public List<ReportedPayment> Payments { get; set; }
}

public sealed class ReportedPayment
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentId { get; set; }

    public string PaymentConversationId { get; set; }
    public string PaymentRefundStatus { get; set; }
    public string Currency { get; set; }
    public List<ReportedCancel> Cancels { get; set; }
    public List<ReportedItemTransaction> ItemTransactions { get; set; }
}

public sealed class ReportedItemTransaction
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentTransactionId { get; set; }

    public List<ReportedRefund> Refunds { get; set; }
}

public sealed class ReportedRefund
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string RefundTxId { get; set; }

    public string RefundConversationId { get; set; }
    public decimal? RefundPrice { get; set; }
    public int? RefundStatus { get; set; }
    public string CurrencyCode { get; set; }
}

public sealed class ReportedCancel
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string RefundId { get; set; }

    public string CancelConversationId { get; set; }
    public decimal? RefundPrice { get; set; }
    public int? RefundStatus { get; set; }
    public string CurrencyCode { get; set; }
}

/// <summary>
/// Webhook body for hosted payment pages (Checkout Form, Pay with iyzico).
/// See https://docs.iyzico.com/en/advanced/webhook
/// </summary>
public sealed class IyzicoWebhookPayload
{
    public string PaymentConversationId { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string MerchantId { get; set; }

    public string Token { get; set; }
    public string Status { get; set; }
    public string IyziReferenceCode { get; set; }
    public string IyziEventType { get; set; }
    public long? IyziEventTime { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string IyziPaymentId { get; set; }

    /// <summary>
    /// Only present in the direct (non hosted) format.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string PaymentId { get; set; }
}

/// <summary>
/// Reads JSON numbers and strings into a string, keeping the exact number text (iyzico sends some ids as numbers).
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan.ToArray()),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
