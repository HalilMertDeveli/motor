using Smartstore.Core.Configuration;

namespace MotorShop.Iyzico.Configuration;

public class IyzicoSettings : ISettings
{
    /// <summary>
    /// Sends requests to https://sandbox-api.iyzipay.com instead of https://api.iyzipay.com.
    /// </summary>
    public bool UseSandbox { get; set; } = true;

    /// <summary>
    /// API key, encrypted with ASP.NET Core Data Protection. Never store or log the plain value.
    /// The configuration value MotorShop:Iyzico:ApiKey (environment MotorShop__Iyzico__ApiKey) takes precedence.
    /// </summary>
    public string ApiKeyProtected { get; set; }

    /// <summary>
    /// Secret key, encrypted with ASP.NET Core Data Protection. Never store or log the plain value.
    /// The configuration value MotorShop:Iyzico:SecretKey (environment MotorShop__Iyzico__SecretKey) takes precedence.
    /// </summary>
    public string SecretKeyProtected { get; set; }

    /// <summary>
    /// Offers installments on the hosted form. If <c>false</c>, only single payment (1) is offered.
    /// </summary>
    public bool EnableInstallments { get; set; }

    /// <summary>
    /// Comma separated installment counts offered when <see cref="EnableInstallments"/> is on, e.g. "1,2,3,6,9".
    /// </summary>
    public string EnabledInstallments { get; set; } = "1,2,3,6,9";

    // Signature checks are not admin settings. They can only be relaxed through configuration
    // (MotorShop:Iyzico:AllowUnsignedWebhooks, MotorShop:Iyzico:SkipResponseSignatureVerification), see IIyzicoCredentialProvider.

    /// <summary>
    /// Comma separated ISO currency codes the method is offered for, e.g. "TRY" or "TRY,EUR".
    /// Only codes iyzico accepts (<see cref="Services.IyzicoRequestBuilder.SupportedCurrencies"/>) take effect.
    /// </summary>
    public string AllowedCurrencies { get; set; } = "TRY";

    /// <summary>
    /// Highest accepted installment commission passed on to the customer, in percent of the order amount.
    /// A paidPrice above price * (1 + value / 100) is not marked as paid. Single payments must match exactly.
    /// </summary>
    public decimal MaxInstallmentSurchargePercent { get; set; } = 30m;

    /// <summary>
    /// Value sent as buyer.identityNumber, which iyzico marks as required. The store does not collect
    /// a TCKN; whether to collect one is an owner decision (see module README).
    /// </summary>
    public string PlaceholderIdentityNumber { get; set; } = "11111111111";

    /// <summary>
    /// Sends one basket item per order line (plus shipping and fees) if the lines add up to the order total.
    /// Otherwise, or if disabled, a single basket item for the whole order is sent.
    /// </summary>
    public bool ItemizeBasket { get; set; } = true;

    /// <summary>
    /// category1 of basket items.
    /// </summary>
    public string BasketItemCategory { get; set; } = "Motorcycle parts";

    /// <summary>
    /// A full refund on the payment day first tries the iyzico cancel service, which leaves no entry on the card statement.
    /// </summary>
    public bool TryCancelOnSameDayFullRefund { get; set; } = true;

    /// <summary>
    /// Cancels iyzico orders that are still unpaid after this many hours and have no open payment attempt.
    /// Cancelling releases the reserved stock. 0 turns it off.
    /// </summary>
    public int CancelUnpaidOrdersAfterHours { get; set; } = 24;

    public decimal AdditionalFee { get; set; }

    public bool AdditionalFeePercentage { get; set; }
}
