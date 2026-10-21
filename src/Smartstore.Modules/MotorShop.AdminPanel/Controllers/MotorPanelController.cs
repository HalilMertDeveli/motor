using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Controllers;

/// <summary>
/// MOTOR premium panel at /motor-panel. Inherits authentication (backend access), admin IP validation
/// and antiforgery validation from <see cref="AdminController"/>; every data block checks its own permission.
/// </summary>
[Route("motor-panel")]
public class MotorPanelController : AdminController
{
    private readonly PanelQueryService _queries;
    private readonly PriceLockService _priceLock;

    public MotorPanelController(PanelQueryService queries, PriceLockService priceLock)
    {
        _queries = queries;
        _priceLock = priceLock;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(ProductFilter filter)
    {
        PanelHttp.NoIndex(Response);

        var model = new DashboardModel
        {
            CanReadProducts = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Read),
            CanReadOrders = await Services.Permissions.AuthorizeAsync(Permissions.Order.Read)
        };

        if (model.CanReadProducts)
        {
            model.Summary = await _queries.GetCatalogSummaryAsync();
            model.Products = await GetProductListAsync(filter);
            model.Products.Categories = await _queries.GetCategoryOptionsAsync();
        }

        if (model.CanReadOrders)
        {
            model.RecentOrders = await _queries.GetRecentOrdersAsync(5);
        }

        if (await Services.Permissions.AuthorizeAsync(Permissions.Customer.Role.Read))
        {
            model.PriceDisplay = await _queries.GetPriceDisplayStateAsync();
        }

        var lockSettings = await _priceLock.GetAsync();
        model.PriceLock = new PriceLockState
        {
            HideAllPrices = lockSettings.HideAllPrices,
            ChangedOn = lockSettings.HideAllPricesChangedOnUtc.HasValue
                ? Services.DateTimeHelper.ConvertToUserTime(lockSettings.HideAllPricesChangedOnUtc.Value, DateTimeKind.Utc)
                : null,
            ChangedBy = lockSettings.HideAllPricesChangedBy,
            CanChange = await Services.Permissions.AuthorizeAsync(Permissions.Configuration.Setting.Update)
        };

        model.Currency = await _queries.GetCurrencyStripAsync();

        return View(model);
    }

    /// <summary>
    /// Product grid only, for filtering and paging without a full page load.
    /// </summary>
    [HttpGet("products")]
    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Products(ProductFilter filter)
    {
        PanelHttp.NoIndex(Response);

        return PartialView("_ProductGrid", await GetProductListAsync(filter));
    }

    /// <summary>
    /// Store-wide "hide all prices". The client asks for confirmation; the server only needs the target state.
    /// </summary>
    [HttpPost("price-lock")]
    [Permission(Permissions.Configuration.Setting.Update)]
    public async Task<IActionResult> PriceLock(bool hideAllPrices)
    {
        var settings = await _priceLock.SetAsync(hideAllPrices);

        return Json(new
        {
            success = true,
            hideAllPrices = settings.HideAllPrices,
            message = T(settings.HideAllPrices ? "Plugins.MotorShop.AdminPanel.PriceLock.Enabled" : "Plugins.MotorShop.AdminPanel.PriceLock.Disabled").Value
        });
    }

    private async Task<ProductListModel> GetProductListAsync(ProductFilter filter)
    {
        var model = await _queries.GetProductsAsync(filter ?? new());
        model.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Update);
        model.CanCreate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Create);
        model.CanDelete = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Delete);
        model.CanEditCategories = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditCategory);

        return model;
    }
}

internal static class PanelHttp
{
    public static void NoIndex(Microsoft.AspNetCore.Http.HttpResponse response)
    {
        response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        response.Headers.CacheControl = "no-store";
    }
}
