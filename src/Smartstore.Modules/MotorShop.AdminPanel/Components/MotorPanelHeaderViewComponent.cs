using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Configuration;
using MotorShop.AdminPanel.Models;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Identity;
using Smartstore.Core.Security;
using Smartstore.Web.Components;

namespace MotorShop.AdminPanel.Components;

/// <summary>
/// Header and tab navigation. Brand values come from the current store (name, logo) and
/// <see cref="MotorPanelSettings"/>; nothing brand specific is hard coded.
/// </summary>
public partial class MotorPanelHeaderViewComponent : SmartViewComponent
{
    // MotorDark's accent ($motor-accent), so panel and storefront share one brand color by default.
    public const string DefaultAccent = "#ff5a1f";

    private readonly MotorPanelSettings _settings;
    private readonly CustomerSettings _customerSettings;
    private readonly IMediaService _mediaService;

    public MotorPanelHeaderViewComponent(MotorPanelSettings settings, CustomerSettings customerSettings, IMediaService mediaService)
    {
        _settings = settings;
        _customerSettings = customerSettings;
        _mediaService = mediaService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string activeTab)
    {
        var store = Services.StoreContext.CurrentStore;
        var customer = Services.WorkContext.CurrentCustomer;
        var logo = store.LogoMediaFileId > 0 ? await _mediaService.GetFileByIdAsync(store.LogoMediaFileId) : null;

        var model = new PanelHeaderModel
        {
            StoreName = store.Name,
            Title = _settings.PanelTitle.NullEmpty() ?? store.Name,
            LogoUrl = logo != null ? _mediaService.GetUrl(logo, 0) : null,
            AccentColor = NormalizeColor(_settings.AccentColor),
            UserName = customer.GetFullName().NullEmpty()
                ?? (_customerSettings.CustomerLoginType != CustomerLoginType.Email ? customer.Username : null).NullEmpty()
                ?? customer.Email,
            ActiveTab = activeTab
        };

        model.Tabs.Add(new("products", T("Plugins.MotorShop.AdminPanel.Tab.Products"), Url.Action("Index", "MotorPanel", new { area = "Admin" }), false));

        await AddClassicTabAsync(model, "orders", "Tab.Orders", Permissions.Order.Read, "List", "Order");
        await AddClassicTabAsync(model, "customers", "Tab.Customers", Permissions.Customer.Read, "List", "Customer");
        await AddClassicTabAsync(model, "reports", "Tab.Reports", Permissions.Order.Read, "BestsellersReport", "Order");
        await AddClassicTabAsync(model, "content", "Tab.Content", Permissions.Cms.Topic.Read, "List", "Topic");
        await AddClassicTabAsync(model, "settings", "Tab.Settings", Permissions.Configuration.Setting.Read, "GeneralCommon", "Setting");
        await AddClassicTabAsync(model, "coupons", "Tab.Coupons", Permissions.Promotion.Discount.Read, "List", "Discount");
        if (await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Category.Read))
        {
            model.Tabs.Add(new("categories", T("Plugins.MotorShop.AdminPanel.Tab.Categories"), Url.Action("Index", "MotorPanelCategory", new { area = "Admin" }), false));
        }
        await AddClassicTabAsync(model, "shipments", "Tab.Shipments", Permissions.Order.Read, "List", "Shipment");

        return View(model);
    }

    private async Task AddClassicTabAsync(PanelHeaderModel model, string key, string resource, string permission, string action, string controller)
    {
        if (await Services.Permissions.AuthorizeAsync(permission))
        {
            model.Tabs.Add(new(key, T("Plugins.MotorShop.AdminPanel." + resource), Url.Action(action, controller, new { area = "Admin" }), true));
        }
    }

    internal static string NormalizeColor(string value)
    {
        value = value?.Trim();
        return value.HasValue() && HexColorRegex().IsMatch(value) ? value.ToLowerInvariant() : DefaultAccent;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();
}
