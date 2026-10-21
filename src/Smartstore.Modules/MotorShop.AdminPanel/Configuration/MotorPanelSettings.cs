using Smartstore.Core.Configuration;

namespace MotorShop.AdminPanel.Configuration;

public class MotorPanelSettings : ISettings
{
    /// <summary>
    /// Panel name in the header. Empty: the store name is used.
    /// </summary>
    public string PanelTitle { get; set; }

    /// <summary>
    /// Accent color as #rrggbb. Empty or invalid: the default accent is used.
    /// </summary>
    public string AccentColor { get; set; }

    /// <summary>
    /// Hides every price in the storefront and blocks adding to the cart and checking out.
    /// Product prices in the database, orders and invoices are not touched.
    /// </summary>
    public bool HideAllPrices { get; set; }

    public DateTime? HideAllPricesChangedOnUtc { get; set; }
    public string HideAllPricesChangedBy { get; set; }
}
