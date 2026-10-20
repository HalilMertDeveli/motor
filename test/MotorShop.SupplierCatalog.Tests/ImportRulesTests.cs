using MotorShop.SupplierCatalog.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.SupplierCatalog.Tests;

/// <summary>
/// MOTOR rules for supplier imports: nothing is published automatically, the published state of existing products is
/// never changed, a missing price never overwrites a price, and package HTML is sanitized.
/// </summary>
[TestFixture]
public class ImportRulesTests
{
    [Test]
    public void New_product_with_price_is_created_unpublished()
    {
        var product = new Product { Published = true };

        SupplierCatalogImporter.ApplyValues(new CatalogPackageProduct { Sku = "A", Name = " Kask ", Price = 1499.90m }, product, isNew: true);

        Assert.That(product.Published, Is.False);
        Assert.That(product.Price, Is.EqualTo(1499.90m));
        Assert.That(product.Name, Is.EqualTo("Kask"));
    }

    [Test]
    public void New_product_without_price_is_created_unpublished_with_zero_price()
    {
        var product = new Product();

        SupplierCatalogImporter.ApplyValues(new CatalogPackageProduct { Sku = "A", Name = "Kask" }, product, isNew: true);

        Assert.That(product.Published, Is.False);
        Assert.That(product.Price, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Update_keeps_published_state(bool published)
    {
        var product = new Product { Published = published, Price = 100m };

        SupplierCatalogImporter.ApplyValues(new CatalogPackageProduct { Sku = "A", Name = "Kask", Price = 120m }, product, isNew: false);

        Assert.That(product.Published, Is.EqualTo(published));
        Assert.That(product.Price, Is.EqualTo(120m));
    }

    [Test]
    public void Update_without_price_keeps_existing_price()
    {
        var product = new Product { Published = true, Price = 899m };

        SupplierCatalogImporter.ApplyValues(new CatalogPackageProduct { Sku = "A", Name = "Kask" }, product, isNew: false);

        Assert.That(product.Price, Is.EqualTo(899m));
    }

    [Test]
    public void Description_html_is_sanitized()
    {
        var html = """<p onclick="steal()">Güvenli <b>metin</b></p><script>alert(1)</script><img src="x" onerror="alert(2)">""";

        var result = SupplierCatalogImporter.SanitizeDescription(html);

        Assert.That(result, Does.Contain("<b>metin</b>"));
        Assert.That(result, Does.Not.Contain("<script"));
        Assert.That(result, Does.Not.Contain("onclick"));
        Assert.That(result, Does.Not.Contain("onerror"));
    }

    [Test]
    public void Empty_description_stays_null()
        => Assert.That(SupplierCatalogImporter.SanitizeDescription("  "), Is.Null);

    [TestCase("Ekipman>Kask", "Ekipman > Kask")]
    [TestCase(" Ekipman  >  Kask > ", "Ekipman > Kask")]
    [TestCase("Yedek Parça", "Yedek Parça")]
    public void Category_paths_are_normalized(string input, string expected)
        => Assert.That(SupplierCatalogImporter.NormalizeCategoryPath(input), Is.EqualTo(expected));
}
