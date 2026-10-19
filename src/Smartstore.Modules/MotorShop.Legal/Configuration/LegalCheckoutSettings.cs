using Smartstore.Core.Configuration;

namespace MotorShop.Legal.Configuration;

/// <summary>
/// Settings for the Turkish distance sales checkout (Mesafeli Sözleşmeler Yönetmeliği).
/// Topic texts are maintained by the merchant in CMS > Topics.
/// </summary>
public class LegalCheckoutSettings : ISettings
{
    /// <summary>
    /// Shows the legal documents at the confirm step and records the acknowledgment per order.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requires the customer to confirm that the pre-contractual information and the
    /// distance sales agreement have been read before the order can be placed.
    /// One combined checkbox; no other mandatory checkbox is added.
    /// </summary>
    public bool RequireAcknowledgment { get; set; } = true;

    /// <summary>
    /// System name of the topic holding the pre-contractual information form (Ön Bilgilendirme Formu).
    /// Rendered with order data (Liquid).
    /// </summary>
    public string PreInformationTopic { get; set; } = LegalTopicNames.PreInformation;

    /// <summary>
    /// System name of the topic holding the distance sales agreement (Mesafeli Satış Sözleşmesi).
    /// Rendered with order data (Liquid).
    /// </summary>
    public string DistanceSalesTopic { get; set; } = LegalTopicNames.DistanceSales;

    /// <summary>
    /// System name of the topic with the right of withdrawal information (cayma hakkı). Linked only.
    /// </summary>
    public string WithdrawalTopic { get; set; } = LegalTopicNames.Withdrawal;

    /// <summary>
    /// System name of the KVKK privacy notice topic (Aydınlatma Metni). Linked only, never a consent checkbox.
    /// Set to "PrivacyInfo" to reuse Smartstore's privacy topic instead.
    /// </summary>
    public string PrivacyNoticeTopic { get; set; } = LegalTopicNames.PrivacyNotice;
}

public static class LegalTopicNames
{
    public const string PreInformation = "MotorPreInformationForm";
    public const string DistanceSales = "MotorDistanceSalesAgreement";
    public const string Withdrawal = "MotorRightOfWithdrawal";
    public const string PrivacyNotice = "MotorKvkkNotice";
}
