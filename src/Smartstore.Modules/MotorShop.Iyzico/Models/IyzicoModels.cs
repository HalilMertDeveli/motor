using MotorShop.Iyzico.Domain;

namespace MotorShop.Iyzico.Models;

public enum IyzicoReturnState
{
    Paid,
    InReview,
    Processing,
    Failed
}

public class IyzicoReturnModel : ModelBase
{
    public IyzicoReturnState State { get; set; }
    public bool IsOwner { get; set; }
    public int? OrderId { get; set; }
    public string OrderNumber { get; set; }
    public Guid OrderGuid { get; set; }
    public bool CanRetry { get; set; }
}

[LocalizedDisplay("Plugins.MotorShop.Iyzico.")]
public class ConfigurationModel : ModelBase
{
    [LocalizedDisplay("*UseSandbox")]
    public bool UseSandbox { get; set; }

    /// <summary>
    /// Write-only. Never rendered back.
    /// </summary>
    [LocalizedDisplay("*ApiKey")]
    public string ApiKey { get; set; }

    /// <summary>
    /// Write-only. Never rendered back.
    /// </summary>
    [LocalizedDisplay("*SecretKey")]
    public string SecretKey { get; set; }

    public bool HasApiKey { get; set; }
    public bool HasSecretKey { get; set; }
    public bool HasConfigurationOverride { get; set; }
    public string EffectiveEndpoint { get; set; }

    [LocalizedDisplay("*EnableInstallments")]
    public bool EnableInstallments { get; set; }

    [LocalizedDisplay("*EnabledInstallments")]
    public string EnabledInstallments { get; set; }

    [LocalizedDisplay("*MinOrderTotal")]
    [Range(0, 100_000_000)]
    public decimal MinOrderTotal { get; set; }

    [LocalizedDisplay("*MinInstallmentOrderTotal")]
    [Range(0, 100_000_000)]
    public decimal MinInstallmentOrderTotal { get; set; }

    [LocalizedDisplay("*OrderMinimumsCustomerRoles")]
    public string OrderMinimumsCustomerRoles { get; set; }

    /// <summary>
    /// Read-only. Signature checks can only be relaxed through configuration.
    /// </summary>
    public bool SkipResponseSignatureVerification { get; set; }

    /// <summary>
    /// Read-only. Signature checks can only be relaxed through configuration.
    /// </summary>
    public bool AllowUnsignedWebhooks { get; set; }

    [LocalizedDisplay("*AllowedCurrencies")]
    public string AllowedCurrencies { get; set; }

    [LocalizedDisplay("*MaxInstallmentSurchargePercent")]
    [Range(0, 100)]
    public decimal MaxInstallmentSurchargePercent { get; set; }

    [LocalizedDisplay("*PlaceholderIdentityNumber")]
    public string PlaceholderIdentityNumber { get; set; }

    [LocalizedDisplay("*ItemizeBasket")]
    public bool ItemizeBasket { get; set; }

    [LocalizedDisplay("*BasketItemCategory")]
    public string BasketItemCategory { get; set; }

    [LocalizedDisplay("*TryCancelOnSameDayFullRefund")]
    public bool TryCancelOnSameDayFullRefund { get; set; }

    [LocalizedDisplay("*CancelUnpaidOrdersAfterHours")]
    public int CancelUnpaidOrdersAfterHours { get; set; }

    [LocalizedDisplay("*AdditionalFee")]
    public decimal AdditionalFee { get; set; }

    [LocalizedDisplay("*AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }

    [LocalizedDisplay("*CallbackUrl")]
    public string CallbackUrl { get; set; }

    [LocalizedDisplay("*WebhookUrl")]
    public string WebhookUrl { get; set; }

    public List<IyzicoTransactionRow> RecentTransactions { get; set; } = [];

    public List<MotorShop.Iyzico.Services.IyzicoPendingRefund> PendingRefunds { get; set; } = [];
}

public class IyzicoTransactionRow
{
    public int OrderId { get; set; }
    public IyzicoTransactionStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? PaidPrice { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; }
    public int? Installment { get; set; }
    public string PaymentId { get; set; }
    public string LastErrorCode { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
