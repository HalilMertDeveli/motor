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

        foreach (var tab in MainTabs)
        {
            await AddTabAsync(model.Tabs, tab);
        }
        foreach (var tab in MoreTabs)
        {
            await AddTabAsync(model.MoreTabs, tab);
        }

        return View(model);
    }

    /// <param name="Permission"><c>null</c>: shown to everyone with backend access.</param>
    internal record TabDefinition(string Key, string Resource, string Permission, string Action, string Controller, bool Classic = false);

    /// <summary>
    /// Daily work of store employees, in this order.
    /// </summary>
    internal static readonly TabDefinition[] MainTabs =
    [
        new("dashboard", "Tab.Dashboard", null, "Index", "MotorPanel"),
        new("products", "Tab.Products", Permissions.Catalog.Product.Read, "Products", "MotorPanel"),
        new("categories", "Tab.Categories", Permissions.Catalog.Category.Read, "Index", "MotorPanelCategory"),
        new("orders", "Tab.Orders", Permissions.Order.Read, "Index", "MotorPanelOrder"),
        new("shipments", "Tab.Shipments", Permissions.Order.Read, "Index", "MotorPanelShipment")
    ];

    /// <summary>
    /// Rarely used pages under "Diğer İşlemler". Still one click away, with the same permissions as before.
    /// </summary>
    internal static readonly TabDefinition[] MoreTabs =
    [
        new("customers", "Tab.Customers", Permissions.Customer.Read, "Index", "MotorPanelCustomer"),
        new("reports", "Tab.Reports", Permissions.Order.Read, "Index", "MotorPanelReport"),
        new("coupons", "Tab.Coupons", Permissions.Promotion.Discount.Read, "Index", "MotorPanelCoupon"),
        new("content", "Tab.Content", Permissions.Cms.Topic.Read, "Index", "MotorPanelContent"),
        new("invoices", "Tab.Invoices", Permissions.Order.Read, "Index", "MotorPanelInvoice"),
        new("settings", "Tab.Settings", Permissions.Configuration.Setting.Read, "Index", "MotorPanelSettings"),
        new("classic", "ClassicAdmin", null, "Index", "Home", Classic: true)
    ];

    private async Task AddTabAsync(List<PanelTab> tabs, TabDefinition tab)
    {
        if (tab.Permission == null || await Services.Permissions.AuthorizeAsync(tab.Permission))
        {
            tabs.Add(new(tab.Key, T("Plugins.MotorShop.AdminPanel." + tab.Resource), Url.Action(tab.Action, tab.Controller, new { area = "Admin" }), tab.Classic));
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
