using MotorShop.Legal.Components;
using Smartstore.Core.Widgets;

namespace MotorShop.Legal.Services;

/// <summary>
/// Places the legal documents into existing core zones, no view override needed.
/// </summary>
internal class LegalWidgetSource : IWidgetSource
{
    // Confirm page (Views/Checkout/Partials/ConfirmDetails.cshtml): inside the confirm form, before the order review.
    internal const string ConfirmZone = "checkout_confirm_before_summary";

    // Order details page (Views/Order/Details.cshtml).
    internal const string OrderDetailsZone = "orderdetails_page_afterproducts";

    public int Order => 0;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea)
        {
            if (zone.Name.EqualsNoCase(ConfirmZone))
            {
                return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<LegalCheckoutViewComponent>()]);
            }

            if (zone.Name.EqualsNoCase(OrderDetailsZone))
            {
                return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<OrderLegalDocumentsViewComponent>()]);
            }
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
