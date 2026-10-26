using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Catalog;

/// <summary>
/// MOTOR: imported products without a tax category showed "KDV: 0,00 ₺" in cart and orders.
/// </summary>
[TestFixture]
public class DefaultProductTaxCategoryTests : ServiceTestBase
{
    [Test]
    public async Task Products_without_category_get_the_general_KDV_category_others_are_kept()
    {
        var general = new TaxCategory { Name = TurkishStoreDefaults.TaxCategories[0].Name };
        var reduced = new TaxCategory { Name = TurkishStoreDefaults.TaxCategories[1].Name };
        DbContext.TaxCategories.AddRange(general, reduced);
        await DbContext.SaveChangesAsync();

        var none = new Product { Name = "Kask", TaxCategoryId = 0 };
        var chosen = new Product { Name = "Mont", TaxCategoryId = reduced.Id };
        DbContext.Products.AddRange(none, chosen);
        await DbContext.SaveChangesAsync();

        await new DefaultProductTaxCategory().SeedAsync(DbContext);

        Assert.That(DbContext.Products.Single(x => x.Id == none.Id).TaxCategoryId, Is.EqualTo(general.Id));
        Assert.That(DbContext.Products.Single(x => x.Id == chosen.Id).TaxCategoryId, Is.EqualTo(reduced.Id));
    }
}
