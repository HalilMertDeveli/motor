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

    public MotorPanelController(PanelQueryService queries)
    {
        _queries = queries;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(ProductFilter filter)
    {
        NoIndex();

        var model = new DashboardModel
        {
            CanReadProducts = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Read),
            CanReadOrders = await Services.Permissions.AuthorizeAsync(Permissions.Order.Read)
        };

        if (model.CanReadProducts)
        {
            model.Summary = await _queries.GetCatalogSummaryAsync();
            model.Products = await _queries.GetProductsAsync(filter ?? new());
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
        NoIndex();

        var model = await _queries.GetProductsAsync(filter ?? new());

        return PartialView("_ProductGrid", model);
    }

    private void NoIndex()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
    }
}
