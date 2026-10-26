using FluentMigrator;
using Smartstore.Core.Installation;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: products imported without a tax category had no KDV at all: prices (which include tax) were right, but cart,
/// order and invoice showed "KDV: 0,00 ₺". Products without a category get the general KDV category
/// (<see cref="TurkishStoreDefaults.TaxCategories"/>, 20 %). Products with a category chosen by an admin are kept;
/// reduced rates (e.g. clothing) are an accountant's decision and are set per product in the admin.
///
/// Only runs when the general KDV category exists (Turkish installs).
/// </summary>
[MigrationVersion("2026-10-26 19:00:00", "MOTOR: default product tax category")]
internal class DefaultProductTaxCategory : Migration, IDataSeeder<SmartDbContext>
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        var generalName = TurkishStoreDefaults.TaxCategories[0].Name;
        var general = await context.TaxCategories
            .Where(x => x.Name == generalName)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancelToken);

        if (general == 0)
        {
            return;
        }

        var products = await context.Products
            .IgnoreQueryFilters()
            .Where(x => x.TaxCategoryId == 0)
            .ToListAsync(cancelToken);

        products.Each(x => x.TaxCategoryId = general);
        await context.SaveChangesAsync(cancelToken);
    }
}
