using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.Tracking.Configuration;
using MotorShop.Tracking.Models;
using MotorShop.Tracking.Services;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Data;
using Smartstore.Web.Components;

namespace MotorShop.Tracking.Components;

/// <summary>
/// Order details page: carrier, tracking number, tracking link and status of every shipped shipment.
/// The zone passes no model, the order id comes from the route. Access to the order has already been
/// checked by <c>OrderController.Details</c>.
/// </summary>
public class OrderShipmentTrackingViewComponent : SmartViewComponent
{
    private readonly SmartDbContext _db;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ShipmentTrackingSettings _settings;

    public OrderShipmentTrackingViewComponent(SmartDbContext db, IDateTimeHelper dateTimeHelper, ShipmentTrackingSettings settings)
    {
        _db = db;
        _dateTimeHelper = dateTimeHelper;
        _settings = settings;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_settings.ShowOnOrderDetails
            || !int.TryParse(RouteData.Values["id"]?.ToString(), out var orderId)
            || orderId <= 0)
        {
            return Empty();
        }

        var shipments = await _db.Shipments
            .AsNoTracking()
            .Where(x => x.OrderId == orderId && x.ShippedDateUtc != null)
            .OrderBy(x => x.CreatedOnUtc)
            .ToListAsync();

        if (shipments.Count == 0)
        {
            return Empty();
        }

        var ids = shipments.Select(x => x.Id).ToArray();
        var carriers = await _db.GenericAttributes
            .AsNoTracking()
            .Where(x => x.KeyGroup == nameof(Shipment) && x.Key == CarrierTrackingService.CarrierAttributeKey && ids.Contains(x.EntityId))
            .ToDictionaryAsync(x => x.EntityId, x => x.Value);

        var model = shipments.Select(x => new ShipmentTrackingModel
        {
            ShipmentId = x.Id,
            Carrier = carriers.Get(x.Id).NullEmpty() ?? _settings.DefaultCarrier.NullEmpty(),
            TrackingNumber = x.TrackingNumber,
            // Same rule as the core shipment page: only absolute http(s) links are rendered.
            TrackingUrl = IsSafeUrl(x.TrackingUrl) ? x.TrackingUrl : null,
            ShippedOn = _dateTimeHelper.ConvertToUserTime(x.ShippedDateUtc.Value, DateTimeKind.Utc),
            DeliveredOn = x.DeliveryDateUtc.HasValue ? _dateTimeHelper.ConvertToUserTime(x.DeliveryDateUtc.Value, DateTimeKind.Utc) : null
        })
        .ToList();

        return View(model);
    }

    internal static bool IsSafeUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
