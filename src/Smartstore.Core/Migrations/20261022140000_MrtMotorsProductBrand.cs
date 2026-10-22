using FluentMigrator;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Installation;
using Smartstore.Core.Seo;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: the supplier brand "Prosev" becomes the store brand "MRT Motors" in EXISTING databases (owner decision
/// 2026-10-22: "hiç bir prosev olmasın sitemde"). Renames the manufacturer, replaces the word in product names and
/// descriptions and moves the active URL slugs ("prosev-..." → "mrt-motors-..."). The old slugs stay as inactive
/// URL records, so old links still resolve. SKUs, MPNs and pictures are not touched.
///
/// Idempotent: values without "Prosev" are left alone. The supplier importer applies the same replacement to new
/// imports (MotorShop.SupplierCatalog).
/// </summary>
[MigrationVersion("2026-10-22 14:00:00", "MOTOR: MRT Motors product brand")]
internal class MrtMotorsProductBrand : Migration, IDataSeeder<SmartDbContext>
{
    private static readonly string[] _brandSpellings = ["Prosev", "PROSEV", "prosev"];
    private static readonly string[] _textKeys = [nameof(Product.Name), nameof(Product.ShortDescription), nameof(Product.FullDescription)];

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
        await RenameManufacturersAsync(context, cancelToken);
        await RenameProductsAsync(context, cancelToken);
        await RenameLocalizedValuesAsync(context, cancelToken);
        await MoveSlugsAsync(context, cancelToken);
    }

    private static async Task RenameManufacturersAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var manufacturers = await context.Manufacturers.ToListAsync(cancelToken);
        if (manufacturers.Any(x => x.Name.EqualsNoCase(ProsevStoreDefaults.StoreName)))
        {
            // A store brand already exists: merging two brands is an admin decision.
            return;
        }

        manufacturers
            .Where(x => x.Name?.Trim().EqualsNoCase(ProsevStoreDefaults.SupplierBrandName) == true)
            .Each(x => x.Name = ProsevStoreDefaults.StoreName);

        foreach (var manufacturer in manufacturers.Where(x => ProsevStoreDefaults.ContainsSupplierBrand(x.Description)))
        {
            manufacturer.Description = ProsevStoreDefaults.ReplaceSupplierBrand(manufacturer.Description);
        }

        await context.SaveChangesAsync(cancelToken);
    }

    private static async Task RenameProductsAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        // Contains() is case-sensitive on some providers, hence the spellings.
        var query = context.Products.IgnoreQueryFilters().Where(x => !x.Deleted);
        var products = await query
            .Where(x =>
                x.Name.Contains("Prosev") || x.Name.Contains("PROSEV") || x.Name.Contains("prosev") ||
                x.ShortDescription.Contains("Prosev") || x.ShortDescription.Contains("PROSEV") || x.ShortDescription.Contains("prosev") ||
                x.FullDescription.Contains("Prosev") || x.FullDescription.Contains("PROSEV") || x.FullDescription.Contains("prosev"))
            .ToListAsync(cancelToken);

        foreach (var product in products)
        {
            product.Name = ProsevStoreDefaults.ReplaceSupplierBrand(product.Name).Truncate(400);
            product.ShortDescription = ProsevStoreDefaults.ReplaceSupplierBrand(product.ShortDescription);
            product.FullDescription = ProsevStoreDefaults.ReplaceSupplierBrand(product.FullDescription);
        }

        await context.SaveChangesAsync(cancelToken);
    }

    private static async Task RenameLocalizedValuesAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var groups = new[] { nameof(Product), nameof(Manufacturer) };
        var values = await context.LocalizedProperties
            .Where(x => groups.Contains(x.LocaleKeyGroup) && _textKeys.Contains(x.LocaleKey))
            .Where(x => x.LocaleValue.Contains("Prosev") || x.LocaleValue.Contains("PROSEV") || x.LocaleValue.Contains("prosev"))
            .ToListAsync(cancelToken);

        values.Each(x => x.LocaleValue = ProsevStoreDefaults.ReplaceSupplierBrand(x.LocaleValue));

        await context.SaveChangesAsync(cancelToken);
    }

    private static async Task MoveSlugsAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var entityNames = new[] { nameof(Product), nameof(Manufacturer) };
        var oldRecords = await context.UrlRecords
            .Where(x => x.IsActive && entityNames.Contains(x.EntityName))
            .Where(x => x.Slug.Contains("prosev") || x.Slug.Contains("Prosev") || x.Slug.Contains("PROSEV"))
            .ToListAsync(cancelToken);

        oldRecords = oldRecords
            .Where(x => ProsevStoreDefaults.ReplaceSupplierBrandInSlug(x.Slug) != x.Slug)
            .ToList();

        if (oldRecords.Count == 0)
        {
            return;
        }

        // Existing records that a new slug could collide with, to keep slugs unique.
        var candidates = oldRecords.Select(x => ProsevStoreDefaults.ReplaceSupplierBrandInSlug(x.Slug).ToLowerInvariant()).Distinct().ToArray();
        var existing = await context.UrlRecords
            .Where(x => candidates.Contains(x.Slug) || x.Slug.StartsWith(ProsevStoreDefaults.StoreBrandSlug))
            .ToListAsync(cancelToken);

        foreach (var record in oldRecords)
        {
            var newSlug = ProsevStoreDefaults.ReplaceSupplierBrandInSlug(record.Slug).ToLowerInvariant();

            // The same entity may already own the new slug (e.g. as an inactive record): reactivate it.
            var own = existing.FirstOrDefault(x =>
                x.EntityName == record.EntityName && x.EntityId == record.EntityId && x.LanguageId == record.LanguageId && x.Slug.EqualsNoCase(newSlug));

            if (own != null)
            {
                own.IsActive = true;
            }
            else
            {
                var slug = newSlug;
                for (var i = 2; existing.Any(x => x.Slug.EqualsNoCase(slug)); i++)
                {
                    slug = $"{newSlug}-{i}";
                }

                var added = new UrlRecord
                {
                    EntityId = record.EntityId,
                    EntityName = record.EntityName,
                    LanguageId = record.LanguageId,
                    Slug = slug,
                    IsActive = true
                };

                context.UrlRecords.Add(added);
                existing.Add(added);
            }

            record.IsActive = false;
        }

        await context.SaveChangesAsync(cancelToken);
    }
}
