namespace MotorShop.PaymentTerms;

public static class PaymentTermsDefaults
{
    public const string DealerTermSystemName = "Payments.DealerTerm";
    public const string IyzicoSystemName = "Payments.Iyzico";
    public const string PrepaymentSystemName = "Payments.Prepayment";
    public const string CashOnDeliverySystemName = "Payments.CashOnDelivery";
    public const string PayInStoreSystemName = "Payments.PayInStore";

    public const string DealerRoleSystemName = "Dealer";
    public const string DealerRoleName = "Bayi";

    /// <summary>
    /// Form field of the dealer term selection. Value: "cash" or the number of days.
    /// </summary>
    public const string TermFormKey = "DealerPaymentTerm";
    public const string CashTermValue = "cash";

    /// <summary>
    /// Order generic attribute with the chosen term in days.
    /// </summary>
    public const string OrderTermDaysAttribute = "MotorShop.PaymentTerms.TermDays";

    /// <summary>
    /// Payment methods of the catalog (card, bank transfer, cash on delivery, dealer terms).
    /// </summary>
    public static readonly string[] ActivatedMethods =
    [
        IyzicoSystemName, PrepaymentSystemName, CashOnDeliverySystemName, DealerTermSystemName
    ];

    /// <summary>
    /// Pay in store is not one of the catalog's payment options.
    /// </summary>
    public static readonly string[] DeactivatedMethods = [PayInStoreSystemName];

    /// <summary>
    /// Shown in checkout under "Bank transfer". Bank, account holder and IBAN are NOT in the catalog:
    /// the owner fills them in under Admin > Configuration > Payment methods.
    /// </summary>
    public const string PrepaymentDescription =
        "<p>Siparişinizi verdikten sonra sipariş tutarını aşağıdaki hesaba havale/EFT ile gönderin ve açıklama alanına sipariş numaranızı yazın. " +
        "Ödemeniz hesabımıza geçtiğinde siparişiniz hazırlanır.</p>" +
        "<p><strong>Banka:</strong> [banka adı – yönetim panelinden girilecek]<br />" +
        "<strong>Hesap sahibi:</strong> [firma unvanı – yönetim panelinden girilecek]<br />" +
        "<strong>IBAN:</strong> [IBAN – yönetim panelinden girilecek]</p>";

    public const string CashOnDeliveryDescription =
        "<p>Ödemeyi siparişiniz teslim edilirken kargo görevlisine yaparsınız.</p>";
}
