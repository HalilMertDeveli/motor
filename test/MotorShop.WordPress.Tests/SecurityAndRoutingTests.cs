using System;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Controllers;
using MotorShop.WordPress.Services;
using NUnit.Framework;
using Smartstore.Core.Localization.Routing;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.WordPress.Tests;

[TestFixture]
public class SecurityAndRoutingTests
{
    [TestCase("https://cms.example.com", "https://cms.example.com/")]
    [TestCase("https://cms.example.com/wp", "https://cms.example.com/wp/")]
    [TestCase("https://cms.example.com:8443/", "https://cms.example.com:8443/")]
    public void Accepts_https_base_urls(string url, string expected)
    {
        var uri = WordPressEndpoint.TryGetBaseUri(TestOptions.Create(o => o.BaseUrl = url), out var error);

        Assert.That(error, Is.Null);
        Assert.That(uri!.ToString(), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("cms.example.com")]
    [TestCase("http://cms.example.com")]
    [TestCase("ftp://cms.example.com")]
    [TestCase("file:///etc/passwd")]
    [TestCase("https://admin:secret@cms.example.com")]
    [TestCase("https://cms.example.com/?rest_route=/")]
    [TestCase("https://localhost")]
    [TestCase("https://wp.localhost")]
    [TestCase("https://127.0.0.1")]
    [TestCase("https://10.0.0.5")]
    [TestCase("https://192.168.1.10")]
    [TestCase("https://172.16.0.1")]
    [TestCase("https://169.254.169.254")]
    [TestCase("https://[::1]")]
    [TestCase("https://[fd00::1]")]
    public void Rejects_unsafe_base_urls(string? url)
    {
        var uri = WordPressEndpoint.TryGetBaseUri(TestOptions.Create(o => o.BaseUrl = url), out var error);

        Assert.That(uri, Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Development_flags_allow_a_local_container()
    {
        var options = TestOptions.Create(o => { o.BaseUrl = "http://localhost:8097"; o.AllowInsecureHttp = true; o.AllowPrivateNetwork = true; });

        Assert.That(WordPressEndpoint.TryGetBaseUri(options, out _)!.ToString(), Is.EqualTo("http://localhost:8097/"));
    }

    [TestCase("8.8.8.8", true)]
    [TestCase("2a00:1450:4001:80b::200e", true)]
    [TestCase("127.0.0.1", false)]
    [TestCase("10.1.2.3", false)]
    [TestCase("100.64.0.1", false)]
    [TestCase("169.254.169.254", false)]
    [TestCase("172.31.255.255", false)]
    [TestCase("192.168.0.1", false)]
    [TestCase("0.0.0.0", false)]
    [TestCase("224.0.0.1", false)]
    [TestCase("::1", false)]
    [TestCase("fe80::1", false)]
    [TestCase("fd12:3456::1", false)]
    [TestCase("::ffff:10.0.0.1", false)]
    [TestCase("2001:db8::1", false)]
    public void Classifies_public_addresses(string ip, bool isPublic)
    {
        Assert.That(WordPressEndpoint.IsPublicAddress(IPAddress.Parse(ip)), Is.EqualTo(isPublic));
    }

    [Test]
    public void Webhook_secret_is_compared_exactly()
    {
        const string secret = "0123456789abcdef0123456789abcdef";

        Assert.That(WordPressWebhook.IsValidSecret(secret, secret), Is.True);
        Assert.That(WordPressWebhook.IsValidSecret(secret + "x", secret), Is.False);
        Assert.That(WordPressWebhook.IsValidSecret(secret.ToUpperInvariant(), secret), Is.False);
        Assert.That(WordPressWebhook.IsValidSecret("", secret), Is.False);
        Assert.That(WordPressWebhook.IsValidSecret(null, secret), Is.False);
    }

    [Test]
    public void Storefront_routes_stay_below_blog_and_rehberler()
    {
        var templates = typeof(MotorWordPressController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<LocalizedRouteAttribute>())
            .Select(a => a.Template!)
            .ToList();

        Assert.That(templates, Has.Count.EqualTo(7));
        Assert.That(templates.All(t => t == "/blog" || t == "/rehberler" || t.StartsWith("/blog/") || t.StartsWith("/rehberler/")), Is.True, string.Join(", ", templates));
        // Literal segments take precedence over "/blog/{slug}" in endpoint routing.
        Assert.That(templates, Does.Contain("/blog/kategori/{slug}"));
        Assert.That(templates, Does.Contain("/blog/etiket/{slug}"));
        Assert.That(templates, Does.Contain("/blog/sayfa/{slug}"));
        Assert.That(BlogPaths.ReservedSlugs, Is.EquivalentTo(new[] { "blog", "rehberler" }));
    }

    [Test]
    public void Storefront_controller_is_public_and_admin_controller_is_protected()
    {
        Assert.That(typeof(PublicController).IsAssignableFrom(typeof(MotorWordPressController)));
        Assert.That(typeof(AdminController).IsAssignableFrom(typeof(MotorWordPressAdminController)));

        var actions = typeof(MotorWordPressAdminController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.That(actions, Is.Not.Empty);

        foreach (var action in actions)
        {
            var permission = action.GetCustomAttribute<PermissionAttribute>();
            Assert.That(permission, Is.Not.Null, action.Name + " needs a permission");

            var changesState = action.Name is "ClearCache" or "RebuildSitemap";
            Assert.That(((PermissionRequirement)permission!.Arguments![0]).SystemName, Is.EqualTo(changesState ? WordPressPermissions.Update : WordPressPermissions.Read), action.Name);
            if (changesState)
            {
                Assert.That(action.GetCustomAttribute<HttpPostAttribute>(), Is.Not.Null, action.Name + " must be POST (antiforgery)");
            }
        }
    }

    [Test]
    public void Permission_provider_lists_read_and_update()
    {
        var provider = new WordPressPermissionProvider();

        Assert.That(provider.GetPermissions().Select(x => x.SystemName), Is.EquivalentTo(new[] { "wordpress", "wordpress.read", "wordpress.update" }));
        Assert.That(provider.GetDefaultPermissions().Single().CustomerRoleSystemName, Is.EqualTo("Administrators"));
    }

    [TestCase("https://cms.example.com/zincir-bakimi/", "/blog/zincir-bakimi")]
    [TestCase("https://cms.example.com/2026/10/zincir-bakimi/", "/blog/zincir-bakimi")]
    [TestCase("https://cms.example.com/bak%C4%B1m/", "/blog/bak%C4%B1m")]
    [TestCase("https://cms.example.com/", "/blog")]
    [TestCase("https://cms.example.com/category/bakim/", "/blog/kategori/bakim")]
    [TestCase("https://cms.example.com/category/ust/rehberler/", "/rehberler")]
    [TestCase("https://cms.example.com/tag/zincir/", "/blog/etiket/zincir")]
    [TestCase("https://cms.example.com/x/#bolum", "/blog/x#bolum")]
    [TestCase("https://cms.example.com/wp-content/uploads/a.pdf", null)]
    [TestCase("https://cms.example.com/wp-admin/", null)]
    [TestCase("https://cms.example.com/?p=12", null)]
    [TestCase("https://cms.example.com/Upper-Case/", "/blog/upper-case")]
    public void Rewrites_wordpress_permalinks(string link, string? expected)
    {
        Assert.That(BlogPaths.RewriteWordPressLink(new Uri(link), "rehberler"), Is.EqualTo(expected));
    }

    [Test]
    public void Canonical_paths_per_section()
    {
        Assert.That(BlogPaths.ForPost(new BlogPost { Slug = "a", Section = BlogSection.Blog }), Is.EqualTo("/blog/a"));
        Assert.That(BlogPaths.ForPost(new BlogPost { Slug = "a", Section = BlogSection.Guides }), Is.EqualTo("/rehberler/a"));
        Assert.That(BlogPaths.ForPost(new BlogPost { Slug = "a", Section = BlogSection.Page }), Is.EqualTo("/blog/sayfa/a"));
        Assert.That(BlogPaths.ForPost(new BlogPost { Slug = "bakım", Section = BlogSection.Blog }), Is.EqualTo("/blog/bak%C4%B1m"));
    }
}
