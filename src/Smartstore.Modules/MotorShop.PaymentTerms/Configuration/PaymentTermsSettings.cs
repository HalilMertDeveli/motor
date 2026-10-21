using Smartstore.Core.Configuration;

namespace MotorShop.PaymentTerms.Configuration;

/// <summary>
/// Dealer terms of the Prosev 2026 catalog (page 1 and 128):
/// "30-60-90 day terms from the list date, cash payment term 10 days, 8% monthly late fee on overdue invoices".
/// </summary>
public class PaymentTermsSettings : ISettings
{
    /// <summary>
    /// System name of the customer role that may pay on terms. Created on install as "Bayi".
    /// </summary>
    public string DealerRoleSystemName { get; set; } = PaymentTermsDefaults.DealerRoleSystemName;

    /// <summary>
    /// Payment term in days for "cash" (peşin) dealer orders.
    /// </summary>
    public int CashTermDays { get; set; } = 10;

    /// <summary>
    /// Comma separated deferred payment terms in days the dealer can choose from.
    /// </summary>
    public string TermDays { get; set; } = "30,60,90";

    /// <summary>
    /// Monthly late fee on overdue invoices in percent. Shown to the dealer and written to the order note; not charged automatically.
    /// </summary>
    public decimal LateFeeMonthlyPercent { get; set; } = 8m;

    /// <summary>
    /// Shows the "3 installments on all credit cards" box in checkout while card payment (iyzico) is offered.
    /// </summary>
    public bool ShowInstallmentInfo { get; set; } = true;
}
