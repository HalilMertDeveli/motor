using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Models;
using Smartstore.Core.Data;
using Smartstore.Web.Components;

namespace MotorShop.CatalogImport.Components;

/// <summary>
/// Part identity and the "not confirmed" notice on the product detail page.
/// </summary>
public class CatalogPartNoticeViewComponent : SmartViewComponent
{
    private readonly SmartDbContext _db;

    public CatalogPartNoticeViewComponent(SmartDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        var model = await PublicPartModel.LoadAsync(_db, productId);
        return model == null ? Empty() : View(model);
    }
}

/// <summary>
/// Technical data and source attribution, rendered in the product detail tabs.
/// </summary>
public class CatalogPartSpecsViewComponent : SmartViewComponent
{
    private readonly SmartDbContext _db;

    public CatalogPartSpecsViewComponent(SmartDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        var model = await PublicPartModel.LoadAsync(_db, productId);
        return model == null ? Empty() : View(model);
    }
}
