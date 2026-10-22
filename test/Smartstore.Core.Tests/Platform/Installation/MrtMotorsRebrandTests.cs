using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Platform.Installation;

/// <summary>
/// MOTOR: the store is called MRT Motors (renamed from Prosev on 2026-10-22).
/// "Prosev" stays only as the product manufacturer of the Motosev catalog.
/// </summary>
[TestFixture]
public class MrtMotorsRebrandTests
{
    private static string _repoRoot;

    private static string RepoRoot
    {
        get
        {
            if (_repoRoot == null)
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Smartstore.sln")))
                {
                    dir = dir.Parent;
                }

                _repoRoot = dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (Smartstore.sln) not found.");
            }

            return _repoRoot;
        }
    }

    [Test]
    public void Store_name_is_mrt_motors()
    {
        Assert.That(ProsevStoreDefaults.StoreName, Is.EqualTo("MRT Motors"));
    }

    [Test]
    public void Rebrand_resources_use_the_new_name_only()
    {
        var builder = new LocaleResourcesBuilder();
        new MrtMotorsRebrand().MigrateLocaleResources(builder);
        var entries = builder.Build().ToList();

        Assert.That(entries.Where(x => x.Value?.Contains("Prosev", StringComparison.OrdinalIgnoreCase) == true).Select(x => x.Key), Is.Empty);

        foreach (var key in new[] { "Admin.PageTitle", "Themes.MotorDark.Footer.Note", "Themes.MotorDark.Story.Eyebrow", "Plugins.FriendlyName.MotorShop.PaymentTerms" })
        {
            Assert.That(entries.Any(x => x.Key == key && x.Lang == "tr" && x.Value.Contains("MRT Motors")), Is.True, key);
        }
    }

    [Test]
    public void Contact_pages_show_address_phone_and_instagram()
    {
        foreach (var body in new[] { ProsevStoreDefaults.ContactUsBody, ProsevStoreDefaults.AboutUsBody, ProsevStoreDefaults.ImprintBody })
        {
            Assert.That(body, Does.Contain("Yıldırım Beyazıt Caddesi No: 10/B").And.Contain("Avcılar / İstanbul"));
            Assert.That(body, Does.Contain("0537 490 68 32").And.Contain("tel:+905374906832"));
            Assert.That(body, Does.Contain("https://www.instagram.com/mrtmotorservis/").And.Contain("@mrtmotorservis"));
            Assert.That(ProsevStoreDefaults.IsPlaceholderTopicBody(body), Is.False);
        }

        // Legal identifiers are not known yet.
        Assert.That(ProsevStoreDefaults.ImprintBody, Does.Contain("[mağaza sahibi tarafından girilecek]"));
        Assert.That(ProsevStoreDefaults.MapsUrl, Does.StartWith("https://www.google.com/maps/search/?api=1&query=MRT%20Motors%2C%20"));
    }

    [TestCase("<p>İletişim bilgilerinizi buraya ekleyin. Bu metni yönetim panelinden düzenleyebilirsiniz.</p>", true)]
    [TestCase("<p>Put your contact information here. You can edit this in the admin site.</p>", true)]
    [TestCase("", true)]
    [TestCase("<p>Our own text</p>", false)]
    public void Only_install_texts_count_as_topic_placeholders(string body, bool expected)
    {
        Assert.That(ProsevStoreDefaults.IsPlaceholderTopicBody(body), Is.EqualTo(expected));
    }

    [TestCase("src/Smartstore.Web/Themes/MotorDark/Views/Shared/Components/ShopLogo/Default.cshtml")]
    [TestCase("src/Smartstore.Web/Areas/Admin/Views/Shared/Components/AdminNavbar/Default.cshtml")]
    [TestCase("src/Smartstore.Web/Themes/MotorDark/theme.config")]
    [TestCase("src/Smartstore.Modules/MotorShop.PaymentTerms/module.json")]
    [TestCase("src/Smartstore.Modules/MotorShop.PaymentTerms/Localization/resources.tr-tr.xml")]
    [TestCase("src/Smartstore.Modules/MotorShop.PaymentTerms/Localization/resources.en-us.xml")]
    public void Visible_brand_texts_say_mrt_motors(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        // CSS class names (prosev-*) are code identifiers, not visible text.
        var visible = text.Replace("prosev-", string.Empty, StringComparison.Ordinal);

        Assert.That(visible, Does.Not.Contain("Prosev").IgnoreCase);
        Assert.That(text, Does.Contain("MRT").And.Contain("MOTORS").IgnoreCase);
    }
}
