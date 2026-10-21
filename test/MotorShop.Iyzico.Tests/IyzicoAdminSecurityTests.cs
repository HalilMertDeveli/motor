using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MotorShop.Iyzico.Controllers;
using NUnit.Framework;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// The admin controller stores and clears the iyzico keys and decides refunds, so every POST
/// must carry an antiforgery token and a permission, not only backend access.
/// </summary>
[TestFixture]
public class IyzicoAdminSecurityTests
{
    private static MethodInfo[] Actions => typeof(IyzicoAdminController)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

    [Test]
    public void Controller_inherits_admin_authorization_ip_check_and_antiforgery()
    {
        var type = typeof(IyzicoAdminController);
        var attributes = type.GetCustomAttributes(true);

        Assert.That(type.IsSubclassOf(typeof(AdminController)), Is.True);
        Assert.That(attributes.OfType<AuthorizeAdminAttribute>(), Is.Not.Empty);
        Assert.That(attributes.OfType<ValidateAdminIpAddressAttribute>(), Is.Not.Empty);
        Assert.That(attributes.OfType<AutoValidateAntiforgeryTokenAttribute>(), Is.Not.Empty);
        Assert.That(attributes.OfType<AreaAttribute>().Single().RouteValue, Is.EqualTo("Admin"));
    }

    [Test]
    public void No_post_opts_out_of_antiforgery()
    {
        var posts = Actions.Where(x => x.GetCustomAttributes<HttpMethodAttribute>().Any(m => m.HttpMethods.Contains("POST"))).ToList();

        Assert.That(posts.Select(x => x.Name), Is.SupersetOf(new[] { "Configure", "ClearKeys", "CheckRefunds", "ResolveRefund" }));
        foreach (var post in posts)
        {
            Assert.That(post.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>(), Is.Null, post.Name);
        }
    }

    [TestCase("ClearKeys", Permissions.Configuration.PaymentMethod.Update)]
    [TestCase("CheckRefunds", Permissions.Order.Update)]
    [TestCase("ResolveRefund", Permissions.Order.Update)]
    public void Post_requires_permission(string action, string permission)
    {
        Assert.That(PermissionOf(Actions.Single(x => x.Name == action)), Is.EqualTo(permission));
    }

    [Test]
    public void Configure_requires_read_to_view_and_update_to_save()
    {
        var configure = Actions.Where(x => x.Name == "Configure").ToList();
        var get = configure.Single(x => x.GetCustomAttribute<HttpPostAttribute>() == null);
        var post = configure.Single(x => x.GetCustomAttribute<HttpPostAttribute>() != null);

        Assert.That(PermissionOf(get), Is.EqualTo(Permissions.Configuration.PaymentMethod.Read));
        Assert.That(PermissionOf(post), Is.EqualTo(Permissions.Configuration.PaymentMethod.Update));
    }

    private static string PermissionOf(MethodInfo action)
        => action.GetCustomAttribute<PermissionAttribute>()?.Arguments?.OfType<PermissionRequirement>().SingleOrDefault()?.SystemName;
}
