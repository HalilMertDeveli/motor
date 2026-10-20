using MotorShop.Tracking.Components;
using Smartstore.Core.Widgets;

namespace MotorShop.Tracking.Services;

/// <summary>
/// Places the tracking card into the existing order details zone, no view override needed.
/// </summary>
internal class TrackingWidgetSource : IWidgetSource
{
    // Views/Order/Details.cshtml, right before the core shipments table.
    internal const string OrderDetailsZone = "orderdetails_page_afterproducts";

    public int Order => 10;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea && zone.Name.EqualsNoCase(OrderDetailsZone))
        {
            return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<OrderShipmentTrackingViewComponent>()]);
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
