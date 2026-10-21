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
}
