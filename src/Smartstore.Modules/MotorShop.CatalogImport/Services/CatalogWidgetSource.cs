using MotorShop.CatalogImport.Components;
using Smartstore.Core.Widgets;
using Smartstore.Web.Models.Catalog;

namespace MotorShop.CatalogImport.Services;

/// <summary>
/// Shows part identity (brand, part number, OEM/aftermarket) and, for reference-only parts,
/// the notice that price and availability are not confirmed.
/// </summary>
internal class CatalogWidgetSource : IWidgetSource
{
    public int Order => 0;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea
            && zone.Name.Equals("productdetails_info_top", StringComparison.OrdinalIgnoreCase)
            && model is ProductDetailsModel product)
        {
            return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<CatalogPartNoticeViewComponent>(new { productId = product.Id })]);
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
