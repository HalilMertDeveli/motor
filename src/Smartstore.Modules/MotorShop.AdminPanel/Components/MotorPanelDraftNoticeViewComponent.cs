using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Web.Components;

namespace MotorShop.AdminPanel.Components;

/// <summary>
/// "Bu ürün taslak" banner on the storefront page of an unpublished product.
/// </summary>
public class MotorPanelDraftNoticeViewComponent : SmartViewComponent
{
    private readonly SmartDbContext _db;

    public MotorPanelDraftNoticeViewComponent(SmartDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        var published = await _db.Products
            .AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => (bool?)x.Published)
            .FirstOrDefaultAsync();

        return published == false ? View(productId) : Empty();
    }
}
