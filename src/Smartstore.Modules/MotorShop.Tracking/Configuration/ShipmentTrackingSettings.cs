using Smartstore.Core.Configuration;

namespace MotorShop.Tracking.Configuration;

/// <summary>
/// Carrier neutral shipment tracking. No courier API is called: the tracking URL is built from a template.
/// </summary>
public class ShipmentTrackingSettings : ISettings
{
    /// <summary>
    /// One carrier per line: <c>Carrier name | https://carrier.example/track?no={0}</c>.
    /// <c>{0}</c> is replaced by the URL encoded tracking number. Lines starting with # are ignored.
    /// Empty by default: the carrier has not been chosen yet.
    /// </summary>
    public string Carriers { get; set; } = string.Empty;

    /// <summary>
    /// Carrier used when a shipment has no carrier name. Must match a name in <see cref="Carriers"/>.
    /// </summary>
    public string DefaultCarrier { get; set; } = string.Empty;

    /// <summary>
    /// Shows carrier, tracking number, tracking link and status on the customer order details page.
    /// </summary>
    public bool ShowOnOrderDetails { get; set; } = true;
}
