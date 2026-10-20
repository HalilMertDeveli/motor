namespace MotorShop.Tracking.Models;

public class ShipmentTrackingModel
{
    public int ShipmentId { get; set; }
    public string Carrier { get; set; }
    public string TrackingNumber { get; set; }
    public string TrackingUrl { get; set; }
    public DateTime ShippedOn { get; set; }
    public DateTime? DeliveredOn { get; set; }
}

public class TrackingConfigurationModel : ModelBase
{
    [LocalizedDisplay("Plugins.MotorShop.Tracking.Carriers")]
    public string Carriers { get; set; }

    [LocalizedDisplay("Plugins.MotorShop.Tracking.DefaultCarrier")]
    public string DefaultCarrier { get; set; }

    [LocalizedDisplay("Plugins.MotorShop.Tracking.ShowOnOrderDetails")]
    public bool ShowOnOrderDetails { get; set; }

    public List<string> ValidCarriers { get; set; } = [];
    public List<string> InvalidLines { get; set; } = [];
}
