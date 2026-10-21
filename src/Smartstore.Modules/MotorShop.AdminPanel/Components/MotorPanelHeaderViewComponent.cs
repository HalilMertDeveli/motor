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

        await AddTabAsync(model, "orders", "Tab.Orders", Permissions.Order.Read, "Index", "MotorPanelOrder");
        await AddTabAsync(model, "customers", "Tab.Customers", Permissions.Customer.Read, "Index", "MotorPanelCustomer");
        await AddTabAsync(model, "reports", "Tab.Reports", Permissions.Order.Read, "Index", "MotorPanelReport");
        await AddTabAsync(model, "content", "Tab.Content", Permissions.Cms.Topic.Read, "Index", "MotorPanelContent");
        await AddTabAsync(model, "settings", "Tab.Settings", Permissions.Configuration.Setting.Read, "Index", "MotorPanelSettings");
        await AddTabAsync(model, "coupons", "Tab.Coupons", Permissions.Promotion.Discount.Read, "Index", "MotorPanelCoupon");
        await AddTabAsync(model, "categories", "Tab.Categories", Permissions.Catalog.Category.Read, "Index", "MotorPanelCategory");
        await AddTabAsync(model, "invoices", "Tab.Invoices", Permissions.Order.Read, "Index", "MotorPanelInvoice");
        await AddTabAsync(model, "shipments", "Tab.Shipments", Permissions.Order.Read, "Index", "MotorPanelShipment");

        return View(model);
    }

    private async Task AddTabAsync(PanelHeaderModel model, string key, string resource, string permission, string action, string controller, bool classic = false)
    {
        if (await Services.Permissions.AuthorizeAsync(permission))
        {
            model.Tabs.Add(new(key, T("Plugins.MotorShop.AdminPanel." + resource), Url.Action(action, controller, new { area = "Admin" }), classic));
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
