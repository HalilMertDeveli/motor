using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MotorShop.AdminPanel.Components;
using MotorShop.AdminPanel.Controllers;
using MotorShop.AdminPanel.Models;
using NUnit.Framework;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class PanelContractTests
{
    [Test]
    public void Controller_inherits_admin_authorization_antiforgery_and_ip_check()
    {
        var type = typeof(MotorPanelController);

        Assert.That(type.IsSubclassOf(typeof(AdminController)), Is.True);

        var inherited = type.GetCustomAttributes(true);
        Assert.That(inherited.OfType<AuthorizeAdminAttribute>(), Is.Not.Empty, "backend access is required for every action");
        Assert.That(inherited.OfType<AutoValidateAntiforgeryTokenAttribute>(), Is.Not.Empty, "unsafe methods must carry a token");
        Assert.That(inherited.OfType<AreaAttribute>().Single().RouteValue, Is.EqualTo("Admin"));
        Assert.That(type.GetCustomAttribute<RouteAttribute>()!.Template, Is.EqualTo("motor-panel"));
    }

    [Test]
    public void Every_panel_controller_is_an_admin_controller_with_a_panel_route()
    {
        foreach (var type in PanelControllers)
        {
            Assert.That(type.IsSubclassOf(typeof(AdminController)), Is.True, type.Name);
            Assert.That(type.GetCustomAttribute<RouteAttribute>()?.Template, Does.StartWith("motor-panel"), type.Name);
        }
    }

    // Save checks Create or Update itself, depending on whether the category is new.
    static readonly string[] PermissionCheckedInBody = ["MotorPanelCategoryController.Save", "MotorPanelCouponController.Save"];

    [Test]
    public void Every_write_requires_a_permission_and_keeps_antiforgery()
    {
        var posts = PanelControllers
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => !a.HttpMethods.Contains("GET")))
            .ToList();

        Assert.That(posts, Has.Count.GreaterThanOrEqualTo(9));
        foreach (var post in posts)
        {
            var name = $"{post.DeclaringType!.Name}.{post.Name}";
            Assert.That(post.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>(), Is.Null, name);
            Assert.That(post.GetCustomAttribute<PermissionAttribute>() != null || PermissionCheckedInBody.Contains(name), Is.True, name);
        }
    }

    [Test]
    public void Every_read_action_requires_backend_access_and_a_permission_or_checks_blocks()
    {
        var gets = PanelControllers
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() != null)
            .ToList();

        foreach (var get in gets)
        {
            var name = $"{get.DeclaringType!.Name}.{get.Name}";
            // The dashboard checks a permission per block instead.
            Assert.That(get.GetCustomAttribute<PermissionAttribute>() != null || name == "MotorPanelController.Index", Is.True, name);
        }
    }

    private static Type[] PanelControllers => typeof(MotorPanelController).Assembly
        .GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    [Test]
    public void Product_grid_requires_product_read_permission()
    {
        var action = typeof(MotorPanelController).GetMethod(nameof(MotorPanelController.Products))!;

        var requirement = action.GetCustomAttribute<PermissionAttribute>()?.Arguments?.OfType<PermissionRequirement>().SingleOrDefault();

        Assert.That(requirement?.SystemName, Is.EqualTo(Permissions.Catalog.Product.Read));
    }

[TestCase(nameof(MotorPanelProductController.New), Permissions.Catalog.Product.Create)]
    [TestCase(nameof(MotorPanelProductController.Create), Permissions.Catalog.Product.Create)]
    [TestCase(nameof(MotorPanelProductController.Simple), Permissions.Catalog.Product.Read)]
    [TestCase(nameof(MotorPanelProductController.SaveSimple), Permissions.Catalog.Product.Update)]
    [TestCase(nameof(MotorPanelProductController.AddPhotos), Permissions.Catalog.Product.EditPicture)]
    [TestCase(nameof(MotorPanelProductController.RemovePhoto), Permissions.Catalog.Product.EditPicture)]
    [TestCase(nameof(MotorPanelProductController.OrderPhotos), Permissions.Catalog.Product.EditPicture)]
    public void Simple_product_actions_require_the_classic_permission(string action, string permission)
    {
        var method = typeof(MotorPanelProductController).GetMethod(action)!;
        var requirement = method.GetCustomAttribute<PermissionAttribute>()?.Arguments?.OfType<PermissionRequirement>().SingleOrDefault();

        Assert.That(requirement?.SystemName, Is.EqualTo(permission), action);
    }

    [Test]
    public void Editor_version_is_sent_as_text_so_javascript_keeps_every_digit()
    {
        // 639286465365955133 is above 2^53: as a JSON number the browser would round it and every save would conflict.
        var json = System.Text.Json.JsonSerializer.Serialize(new SimpleProductResult { Version = 639286465365955133 }, Smartstore.Json.SmartJsonOptions.CamelCased);

        Assert.That(json, Does.Contain("\"version\":\"639286465365955133\""));
    }

    [Test]
    public void Quick_category_requires_category_create()
    {
        var method = typeof(MotorPanelCategoryController).GetMethod(nameof(MotorPanelCategoryController.QuickCreate))!;
        var requirement = method.GetCustomAttribute<PermissionAttribute>()?.Arguments?.OfType<PermissionRequirement>().SingleOrDefault();

        Assert.That(requirement?.SystemName, Is.EqualTo(Permissions.Catalog.Category.Create));
        Assert.That(method.GetCustomAttribute<HttpPostAttribute>(), Is.Not.Null);
    }

    [Test]
    public void Photo_uploads_are_size_limited()
    {
        foreach (var action in new[] { nameof(MotorPanelProductController.Create), nameof(MotorPanelProductController.AddPhotos) })
        {
            var limit = typeof(MotorPanelProductController).GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>();
            Assert.That(limit, Is.Not.Null, action);
        }
    }

    [Test]
    public void Navigation_puts_daily_work_first_and_keeps_every_page()
    {
        Assert.That(MotorPanelHeaderViewComponent.MainTabs.Select(x => x.Key),
            Is.EqualTo(new[] { "dashboard", "products", "categories", "orders", "shipments" }));

        // Every page the panel had before is still in the navigation, with its read permission.
        var all = MotorPanelHeaderViewComponent.MainTabs.Concat(MotorPanelHeaderViewComponent.MoreTabs).ToDictionary(x => x.Key);
        Assert.That(all["customers"].Permission, Is.EqualTo(Permissions.Customer.Read));
        Assert.That(all["reports"].Permission, Is.EqualTo(Permissions.Order.Read));
        Assert.That(all["coupons"].Permission, Is.EqualTo(Permissions.Promotion.Discount.Read));
        Assert.That(all["content"].Permission, Is.EqualTo(Permissions.Cms.Topic.Read));
        Assert.That(all["invoices"].Permission, Is.EqualTo(Permissions.Order.Read));
        Assert.That(all["settings"].Permission, Is.EqualTo(Permissions.Configuration.Setting.Read));
        Assert.That(all["products"].Permission, Is.EqualTo(Permissions.Catalog.Product.Read));
        Assert.That(all["categories"].Permission, Is.EqualTo(Permissions.Catalog.Category.Read));
        Assert.That(all["orders"].Permission, Is.EqualTo(Permissions.Order.Read));
        Assert.That(all["shipments"].Permission, Is.EqualTo(Permissions.Order.Read));
        Assert.That(all["classic"].Classic, Is.True);

        // Each entry points to an existing panel action.
        foreach (var tab in all.Values.Where(x => !x.Classic))
        {
            var controller = typeof(MotorPanelController).Assembly.GetTypes().Single(x => x.Name == tab.Controller + "Controller");
            Assert.That(controller.GetMethods().Any(m => m.Name == tab.Action && m.GetCustomAttribute<HttpGetAttribute>() != null), Is.True, tab.Key);
        }
    }

    [TestCase(null, ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    [TestCase("", ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    [TestCase("#12AB9f", ExpectedResult = "#12ab9f")]
    [TestCase(" #ff0000 ", ExpectedResult = "#ff0000")]
    [TestCase("red", ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    [TestCase("#fff", ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    [TestCase("#000000; } body { display:none", ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    [TestCase("</style><script>alert(1)</script>", ExpectedResult = MotorPanelHeaderViewComponent.DefaultAccent)]
    public string Accent_color_accepts_only_six_digit_hex(string value)
        => MotorPanelHeaderViewComponent.NormalizeColor(value);

    [Test]
    public void Filter_normalize_clamps_paging_and_trims_search()
    {
        var filter = new ProductFilter { Q = "  kask  ", Page = -4, Size = 5000, CategoryId = -1 };
        filter.Normalize();

        Assert.That(filter.Q, Is.EqualTo("kask"));
        Assert.That(filter.Page, Is.EqualTo(1));
        Assert.That(filter.Size, Is.EqualTo(ProductFilter.DefaultPageSize));
        Assert.That(filter.CategoryId, Is.Zero);

        var longQuery = new ProductFilter { Q = new string('a', 500) };
        longQuery.Normalize();
        Assert.That(longQuery.Q, Has.Length.EqualTo(200));
    }

    [Test]
    public void Route_values_omit_defaults_for_short_deep_links()
    {
        var plain = new ProductFilter().ToRouteValues();
        Assert.That(plain.Keys, Is.EquivalentTo(new[] { "area" }));

        var filtered = new ProductFilter { Q = "kask", Stock = StockFilter.OutOfStock, Sort = ProductSort.PriceDesc, Page = 3, Size = 50 }
            .ToRouteValues();

        Assert.That(filtered["q"], Is.EqualTo("kask"));
        Assert.That(filtered["stock"], Is.EqualTo("OutOfStock"));
        Assert.That(filtered["sort"], Is.EqualTo("PriceDesc"));
        Assert.That(filtered["page"], Is.EqualTo(3));
        Assert.That(filtered["size"], Is.EqualTo(50));
        Assert.That(filtered.ContainsKey("price"), Is.False);

        // Changing the sort starts again on page 1.
        Assert.That(new ProductFilter { Page = 4 }.ToRouteValues(1, ProductSort.NameAsc).ContainsKey("page"), Is.False);
    }
}
