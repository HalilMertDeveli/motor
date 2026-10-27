using MotorShop.AdminPanel.Components;
using Smartstore.Core.Widgets;
using Smartstore.Web.Models.Catalog;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Puts the draft notice above an unpublished product's page. Customers never get there (Smartstore answers 404),
/// so only admins previewing a draft from the panel see it.
/// </summary>
internal class DraftNoticeWidgetSource : IWidgetSource
{
    public int Order => 0;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea
            && zone.Name.Equals("productdetails_top", StringComparison.OrdinalIgnoreCase)
            && model is ProductDetailsModel product)
        {
            return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<MotorPanelDraftNoticeViewComponent>(new { productId = product.Id })]);
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
