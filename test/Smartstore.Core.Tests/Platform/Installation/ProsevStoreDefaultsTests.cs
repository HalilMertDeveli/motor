using System;
using System.Linq;
using NUnit.Framework;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Platform.Installation;

/// <summary>
/// MOTOR: Prosev store identity and the category tree of the 2026 catalog.
/// </summary>
[TestFixture]
public class ProsevStoreDefaultsTests
{
    [TestCase("Çene Açılır Kask", "cene-acilir-kask")]
    [TestCase("İnterkom", "interkom")]
    [TestCase("Arka Çanta (Top Case)", "arka-canta-top-case")]
    [TestCase("Bagaj Filesi ve Lastiği", "bagaj-filesi-ve-lastigi")]
    [TestCase("Gözlük", "gozluk")]
    [TestCase("Şarj Cihazı", "sarj-cihazi")]
    public void Slugs_are_turkish_aware(string name, string slug)
    {
        Assert.That(ProsevStoreDefaults.BuildSlug(name), Is.EqualTo(slug));
    }

    [Test]
    public void Category_tree_covers_the_catalog_sections()
    {
        var topLevel = ProsevStoreDefaults.Categories.Select(x => x.Name).ToArray();

        // The 30 catalog sections, grouped like the supplier package (MotorShop.SupplierCatalog).
        Assert.That(topLevel, Is.EquivalentTo(new[]
        {
            "Kampanyalı Ürünler", "Kask", "İnterkom", "Çanta", "Sissybar ve Sırt Dayama", "Giyim", "Bandana ve Maske",
            "Eldiven", "Koruma Ekipmanları", "Bot", "Kilit", "Gözlük", "Branda", "Muhtelif Aksesuar", "Ön Siperlik",
            "Sprey ve Bakım", "Kask Aksesuarları"
        }));

        var children = ProsevStoreDefaults.Categories.SelectMany(x => x.Children).Count();
        Assert.That(children, Is.EqualTo(40), "40 subcategories: with 11 leaf top-level categories the 51 package paths, plus the campaign category");
    }

    [Test]
    public void Category_names_and_slugs_are_unique()
    {
        var paths = ProsevStoreDefaults.Categories
            .SelectMany(x => x.Children.Length == 0 ? [x.Name] : x.Children.Select(c => x.Name + " > " + c).Prepend(x.Name))
            .ToList();

        Assert.That(paths, Is.Unique);

        var slugs = ProsevStoreDefaults.Categories
            .SelectMany(x => x.Children.Prepend(x.Name))
            .Select(ProsevStoreDefaults.BuildSlug)
            .ToList();

        Assert.That(slugs, Is.Unique, "unique slugs avoid numbered fallbacks on a fresh install");
    }

    [Test]
    public void Payment_info_states_only_catalog_terms_and_no_invented_bank_data()
    {
        var body = ProsevStoreDefaults.PaymentInfoBody;

        Assert.That(body, Does.Contain("3 taksit"));
        Assert.That(body, Does.Contain("30-60-90 gün"));
        Assert.That(body, Does.Contain("%8 vade farkı"));
        Assert.That(body, Does.Contain("10.000 TL").And.Contain("5.000 TL"));
        Assert.That(body, Does.Not.Match(@"TR\d{2}"), "no IBAN may be invented");
    }

    [Test]
    public void Placeholder_store_names_include_fresh_install_names_but_not_prosev()
    {
        Assert.That(ProsevStoreDefaults.PlaceholderStoreNames, Does.Contain("Your store name").And.Contain("Mağaza adınız"));
        Assert.That(ProsevStoreDefaults.PlaceholderStoreNames.Any(x => x.Equals(ProsevStoreDefaults.StoreName, StringComparison.OrdinalIgnoreCase)), Is.False);
    }
}
