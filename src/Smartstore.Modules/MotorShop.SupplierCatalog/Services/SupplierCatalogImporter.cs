using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Content.Media.Storage;
using Smartstore.Core.Data;
using Smartstore.Core.DataExchange.Import;
using Smartstore.Core.Installation;
using Smartstore.Core.Seo;
using Smartstore.IO;
using Smartstore.Utilities.Html;

namespace MotorShop.SupplierCatalog.Services;

public class SupplierCatalogImportOptions
{
    /// <summary>
    /// Name of the supplier the package comes from (e.g. "Motosev"). Products created by an import are marked
    /// with it, and later imports only update products of the same supplier.
    /// </summary>
    public string SupplierName { get; set; }

    /// <summary>
    /// Tax category assigned to new products. <c>null</c> or 0 leaves them without a tax category.
    /// </summary>
    public int? TaxCategoryId { get; set; }

    /// <summary>
    /// Overwrite name, descriptions and (when given) price of products this supplier's imports created before.
    /// The published state is never changed.
    /// </summary>
    public bool UpdateExistingProducts { get; set; } = true;
}

public class SupplierCatalogImportResult
{
    public int CategoriesCreated { get; set; }
    public int BrandsCreated { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }

    /// <summary>
    /// Products of this import that are not published (all new products, plus existing ones an admin keeps offline).
    /// </summary>
    public int ProductsUnpublished { get; set; }

    /// <summary>
    /// Records whose SKU belongs to a product this supplier's imports did not create (hand-made, another supplier,
    /// or MotorShop.CatalogImport). They are left unchanged.
    /// </summary>
    public int ProductsSkippedConflict { get; set; }

    public int PicturesAdded { get; set; }
    public List<string> Warnings { get; set; } = [];
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Imports a supplier catalog package: category tree, brands, products (matched by SKU),
/// a size attribute and product pictures. Running it again updates instead of duplicating.
/// </summary>
/// <remarks>
/// MOTOR rules: nothing is published automatically. New products, categories and brands are created unpublished
/// and an admin publishes them after review; updates never touch the published state. Only products this
/// supplier's imports created are updated (marked with a generic attribute), so hand-made products and products
/// managed by MotorShop.CatalogImport are never overwritten.
/// </remarks>
public class SupplierCatalogImporter
{
    public const string SupplierAttributeKey = "MotorShop.SupplierCatalog.Supplier";

    const string CategorySeparator = " > ";
    const string SizeAttributeName = "Beden";
    const int BatchSize = 100;

    private readonly SmartDbContext _db;
    private readonly IUrlService _urlService;
    private readonly IMediaImporter _mediaImporter;
    private readonly TaxSettings _taxSettings;

    public SupplierCatalogImporter(SmartDbContext db, IUrlService urlService, IMediaImporter mediaImporter, TaxSettings taxSettings)
    {
        _db = db;
        _urlService = urlService;
        _mediaImporter = mediaImporter;
        _taxSettings = taxSettings;
    }

    public async Task<SupplierCatalogImportResult> ImportAsync(
        CatalogPackage package,
        SupplierCatalogImportOptions options,
        CancellationToken cancelToken = default)
    {
        Guard.NotNull(package);
        Guard.NotNull(options);
        Guard.NotEmpty(options.SupplierName);

        var supplier = options.SupplierName.Trim();
        var watch = Stopwatch.StartNew();
        var result = new SupplierCatalogImportResult();
        result.Warnings.AddRange(package.Warnings);

        if (!_taxSettings.PricesIncludeTax)
        {
            result.Warnings.Add("The store enters prices net of tax, but catalog retail prices include KDV. Check the prices before publishing.");
        }

        var items = package.Products
            .Where(x => x.Sku.HasValue() && x.Name.HasValue())
            .DistinctBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        items.Each(ApplyStoreBrand);

        var skipped = package.Products.Count - items.Count;
        if (skipped > 0)
        {
            result.Warnings.Add($"{skipped} records skipped (missing SKU/name or duplicate SKU).");
        }

        var categoryIds = await EnsureCategoriesAsync(items, result, cancelToken);
        var brandIds = await EnsureBrandsAsync(items, result, cancelToken);
        var sizeAttributeId = await EnsureSizeAttributeAsync(cancelToken);
        var productTemplateId = await _db.ProductTemplates
            .Where(x => x.ViewPath == "Product")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancelToken);

        foreach (var batch in items.Chunk(BatchSize))
        {
            cancelToken.ThrowIfCancellationRequested();

            var skus = batch.Select(x => x.Sku.Trim()).ToArray();
            var existing = (await _db.Products
                .Include(x => x.ProductCategories)
                .Include(x => x.ProductManufacturers)
                .Include(x => x.ProductVariantAttributes)
                    .ThenInclude(x => x.ProductVariantAttributeValues)
                .Where(x => skus.Contains(x.Sku))
                .ToListAsync(cancelToken))
                .ToDictionarySafe(x => x.Sku, StringComparer.OrdinalIgnoreCase);

            var existingIds = existing.Values.Select(x => x.Id).ToArray();
            var ownedIds = (await _db.GenericAttributes
                .AsNoTracking()
                .Where(x => x.KeyGroup == nameof(Product) && x.Key == SupplierAttributeKey && existingIds.Contains(x.EntityId))
                .Select(x => new { x.EntityId, x.Value })
                .ToListAsync(cancelToken))
                .Where(x => x.Value.EqualsNoCase(supplier))
                .Select(x => x.EntityId)
                .ToHashSet();

            var pairs = new List<(CatalogPackageProduct Item, Product Product, bool IsNew)>();

            foreach (var item in batch)
            {
                var sku = item.Sku.Trim();
                var isNew = !existing.TryGetValue(sku, out var product);

                if (!isNew && !ownedIds.Contains(product.Id))
                {
                    result.ProductsSkippedConflict++;
                    result.Warnings.Add($"{sku}: a product with this SKU exists but was not created by the '{supplier}' import. Left unchanged.");
                    continue;
                }

                if (isNew)
                {
                    product = new Product
                    {
                        ProductType = ProductType.SimpleProduct,
                        Sku = sku,
                        ProductTemplateId = productTemplateId,
                        Visibility = ProductVisibility.Full,
                        AllowCustomerReviews = true,
                        ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
                        OrderMinimumQuantity = 1,
                        OrderMaximumQuantity = 100,
                        IsShippingEnabled = true,
                        TaxCategoryId = options.TaxCategoryId ?? 0,
                        CreatedOnUtc = DateTime.UtcNow
                    };

                    _db.Products.Add(product);
                }

                if (isNew || options.UpdateExistingProducts)
                {
                    ApplyValues(item, product, isNew);
                }

                AddMappings(item, product, categoryIds, brandIds);
                AddSizes(item, product, sizeAttributeId);

                if (!product.Published)
                {
                    result.ProductsUnpublished++;
                }

                pairs.Add((item, product, isNew));
            }

            await _db.SaveChangesAsync(cancelToken);

            foreach (var (item, product, isNew) in pairs)
            {
                if (isNew)
                {
                    result.ProductsCreated++;

                    _db.GenericAttributes.Add(new GenericAttribute
                    {
                        KeyGroup = nameof(Product),
                        Key = SupplierAttributeKey,
                        EntityId = product.Id,
                        Value = supplier,
                        StoreId = 0
                    });

                    // Catalog names contain year ranges like "2023/25"; a slash would split the URL.
                    var slug = await _urlService.ValidateSlugAsync(product, product.Name.Replace('/', ' '), product.Name, true);
                    await _urlService.ApplySlugAsync(slug);
                }
                else
                {
                    result.ProductsUpdated++;
                }
            }

            await _db.SaveChangesAsync(cancelToken);

            foreach (var (item, product, _) in pairs)
            {
                result.PicturesAdded += await ImportPicturesAsync(package, item, product, result, cancelToken);
            }

            // Keep the change tracker small over thousands of products.
            _db.ChangeTracker.Clear();
        }

        watch.Stop();
        result.Duration = watch.Elapsed;

        return result;
    }

    /// <summary>
    /// Copies catalog values to a product. New products stay unpublished; the published state of existing products
    /// is never changed. A missing price never overwrites an existing one.
    /// A new product without a catalog price becomes "call for price" instead of a purchasable 0 TL product;
    /// a later import that brings a price for such a product makes it purchasable again.
    /// </summary>
    /// <summary>
    /// The supplier's house brand "Prosev" is sold under the store brand (owner decision 2026-10-22), so brand,
    /// names and descriptions of the package say MRT Motors before they reach the store. SKUs and codes stay.
    /// </summary>
    internal static void ApplyStoreBrand(CatalogPackageProduct item)
    {
        if (item.Brand?.Trim().EqualsNoCase(ProsevStoreDefaults.SupplierBrandName) == true)
        {
            item.Brand = ProsevStoreDefaults.StoreName;
        }

        item.Name = ProsevStoreDefaults.ReplaceSupplierBrand(item.Name);
        item.ShortDescription = ProsevStoreDefaults.ReplaceSupplierBrand(item.ShortDescription);
        item.DescriptionHtml = ProsevStoreDefaults.ReplaceSupplierBrand(item.DescriptionHtml);
    }

    internal static void ApplyValues(CatalogPackageProduct item, Product product, bool isNew)
    {
        product.Name = item.Name.Trim().Truncate(400);
        product.ShortDescription = item.ShortDescription.NullEmpty()?.Trim();
        product.FullDescription = SanitizeDescription(item.DescriptionHtml);
        product.ManufacturerPartNumber = item.CatalogCode.NullEmpty()?.Trim().Truncate(400);
        product.DisplayOrder = item.DisplayOrder;
        product.UpdatedOnUtc = DateTime.UtcNow;

        if (item.Price.HasValue)
        {
            // Only lift "call for price" that stands in for a missing price (price 0), never one an admin set
            // for a priced product.
            if (product.CallForPrice && product.Price == 0 && item.Price.Value > 0)
            {
                product.CallForPrice = false;
            }

            product.Price = item.Price.Value;
        }
        else if (isNew)
        {
            product.Price = 0;
            product.CallForPrice = true;
        }

        if (isNew)
        {
            product.Published = false;
        }
    }

    /// <summary>
    /// Package HTML is shown on the storefront without encoding, so scripts, event handlers and similar are removed.
    /// </summary>
    internal static string SanitizeDescription(string html)
        => html.HasValue() ? HtmlUtility.SanitizeHtml(html, isFragment: true).NullEmpty() : null;

    private static void AddMappings(
        CatalogPackageProduct item,
        Product product,
        Dictionary<string, int> categoryIds,
        Dictionary<string, int> brandIds)
    {
        if (item.Category.HasValue()
            && categoryIds.TryGetValue(NormalizeCategoryPath(item.Category), out var categoryId)
            && !product.ProductCategories.Any(x => x.CategoryId == categoryId))
        {
            product.ProductCategories.Add(new ProductCategory { CategoryId = categoryId, DisplayOrder = item.DisplayOrder });
        }

        // Catalog campaign pages: campaign products are also listed in the campaign category.
        if (item.Campaign
            && categoryIds.TryGetValue(ProsevStoreDefaults.CampaignCategoryName, out var campaignCategoryId)
            && !product.ProductCategories.Any(x => x.CategoryId == campaignCategoryId))
        {
            product.ProductCategories.Add(new ProductCategory { CategoryId = campaignCategoryId, DisplayOrder = item.DisplayOrder });
        }

        if (item.Brand.HasValue()
            && brandIds.TryGetValue(item.Brand.Trim(), out var brandId)
            && !product.ProductManufacturers.Any(x => x.ManufacturerId == brandId))
        {
            product.ProductManufacturers.Add(new ProductManufacturer { ManufacturerId = brandId, DisplayOrder = 1 });
        }
    }

    private static void AddSizes(CatalogPackageProduct item, Product product, int sizeAttributeId)
    {
        var sizes = item.Sizes
            .Where(x => x.HasValue())
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sizes.Count == 0)
        {
            return;
        }

        var attribute = product.ProductVariantAttributes.FirstOrDefault(x => x.ProductAttributeId == sizeAttributeId);
        if (attribute == null)
        {
            attribute = new ProductVariantAttribute
            {
                ProductAttributeId = sizeAttributeId,
                AttributeControlType = AttributeControlType.Boxes,
                IsRequired = true,
                DisplayOrder = 1
            };

            product.ProductVariantAttributes.Add(attribute);
        }

        var order = attribute.ProductVariantAttributeValues.Count;
        foreach (var size in sizes)
        {
            if (!attribute.ProductVariantAttributeValues.Any(x => x.Name.EqualsNoCase(size)))
            {
                attribute.ProductVariantAttributeValues.Add(new ProductVariantAttributeValue
                {
                    Name = size,
                    DisplayOrder = ++order
                });
            }
        }
    }

    private async Task<int> ImportPicturesAsync(
        CatalogPackage package,
        CatalogPackageProduct item,
        Product product,
        SupplierCatalogImportResult result,
        CancellationToken cancelToken)
    {
        if (item.Images.Count == 0)
        {
            return 0;
        }

        // Pictures are only added once. Pictures maintained in the backend are never replaced.
        if (await _db.ProductMediaFiles.AnyAsync(x => x.ProductId == product.Id, cancelToken))
        {
            return 0;
        }

        var sources = new List<FileBatchSource>();
        try
        {
            foreach (var fileName in item.Images)
            {
                Stream stream;
                try
                {
                    stream = await package.OpenImageAsync(fileName, cancelToken);
                }
                catch (InvalidDataException ex)
                {
                    result.Warnings.Add($"{item.Sku}: {ex.Message}");
                    continue;
                }

                if (stream == null)
                {
                    result.Warnings.Add($"{item.Sku}: image '{fileName}' not found in package.");
                    continue;
                }

                sources.Add(new FileBatchSource(MediaStorageItem.FromStream(stream))
                {
                    FileName = PathUtility.SanitizeFileName(Path.GetFileName(fileName))
                });
            }

            if (sources.Count == 0)
            {
                return 0;
            }

            return await _mediaImporter.ImportProductImagesAsync(product, sources, DuplicateFileHandling.Rename, cancelToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Warnings.Add($"{item.Sku}: pictures not imported ({ex.Message}).");
            return 0;
        }
        finally
        {
            sources.Each(x => x.Dispose());
        }
    }

    private async Task<Dictionary<string, int>> EnsureCategoriesAsync(
        List<CatalogPackageProduct> items,
        SupplierCatalogImportResult result,
        CancellationToken cancelToken)
    {
        var templateId = await _db.CategoryTemplates
            .Where(x => x.ViewPath == "CategoryTemplate.ProductsInGridOrLines")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancelToken);

        var all = await _db.Categories.ToListAsync(cancelToken);
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newCategories = new List<Category>();

        // Keep catalog order: the first product of a category decides its position.
        var paths = items
            .Where(x => x.Category.HasValue())
            .Select(x => NormalizeCategoryPath(x.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Any(x => x.Campaign) && !paths.Contains(ProsevStoreDefaults.CampaignCategoryName, StringComparer.OrdinalIgnoreCase))
        {
            paths.Insert(0, ProsevStoreDefaults.CampaignCategoryName);
        }

        foreach (var path in paths)
        {
            int? parentId = null;
            var current = string.Empty;
            var levels = path.Split(CategorySeparator);

            foreach (var name in levels)
            {
                current = current.Length == 0 ? name : current + CategorySeparator + name;
                if (ids.TryGetValue(current, out var knownId))
                {
                    parentId = knownId;
                    continue;
                }

                var category = all.FirstOrDefault(x => (x.ParentId ?? 0) == (parentId ?? 0) && x.Name.EqualsNoCase(name));
                if (category == null)
                {
                    category = new Category
                    {
                        Name = name,
                        ParentId = parentId,
                        CategoryTemplateId = templateId,
                        // Published by an admin after review, like the products.
                        Published = false,
                        DisplayOrder = all.Count(x => (x.ParentId ?? 0) == (parentId ?? 0)) + 1,
                        CreatedOnUtc = DateTime.UtcNow,
                        UpdatedOnUtc = DateTime.UtcNow
                    };

                    _db.Categories.Add(category);
                    await _db.SaveChangesAsync(cancelToken);

                    all.Add(category);
                    newCategories.Add(category);
                    result.CategoriesCreated++;
                }

                ids[current] = category.Id;
                parentId = category.Id;
            }
        }

        foreach (var category in newCategories)
        {
            var slug = await _urlService.ValidateSlugAsync(category, null, category.Name, true);
            await _urlService.ApplySlugAsync(slug);
        }

        await _db.SaveChangesAsync(cancelToken);

        return ids;
    }

    private async Task<Dictionary<string, int>> EnsureBrandsAsync(
        List<CatalogPackageProduct> items,
        SupplierCatalogImportResult result,
        CancellationToken cancelToken)
    {
        var names = items
            .Where(x => x.Brand.HasValue())
            .Select(x => x.Brand.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var templateId = await _db.ManufacturerTemplates
            .Where(x => x.ViewPath == "ManufacturerTemplate.ProductsInGridOrLines")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancelToken);

        var all = await _db.Manufacturers.ToListAsync(cancelToken);
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newBrands = new List<Manufacturer>();

        foreach (var name in names)
        {
            var brand = all.FirstOrDefault(x => x.Name.EqualsNoCase(name));
            if (brand == null)
            {
                brand = new Manufacturer
                {
                    Name = name,
                    ManufacturerTemplateId = templateId,
                    // Published by an admin after review, like the products.
                    Published = false,
                    DisplayOrder = all.Count + 1,
                    CreatedOnUtc = DateTime.UtcNow,
                    UpdatedOnUtc = DateTime.UtcNow
                };

                _db.Manufacturers.Add(brand);
                all.Add(brand);
                newBrands.Add(brand);
                result.BrandsCreated++;
            }
        }

        await _db.SaveChangesAsync(cancelToken);

        foreach (var brand in newBrands)
        {
            var slug = await _urlService.ValidateSlugAsync(brand, null, brand.Name, true);
            await _urlService.ApplySlugAsync(slug);
        }

        await _db.SaveChangesAsync(cancelToken);

        foreach (var name in names)
        {
            ids[name] = all.First(x => x.Name.EqualsNoCase(name)).Id;
        }

        return ids;
    }

    private async Task<int> EnsureSizeAttributeAsync(CancellationToken cancelToken)
    {
        var attribute = await _db.ProductAttributes.FirstOrDefaultAsync(x => x.Name == SizeAttributeName, cancelToken);
        if (attribute == null)
        {
            attribute = new ProductAttribute
            {
                Name = SizeAttributeName,
                Alias = "beden",
                AllowFiltering = true,
                DisplayOrder = 1
            };

            _db.ProductAttributes.Add(attribute);
            await _db.SaveChangesAsync(cancelToken);
        }

        return attribute.Id;
    }

    internal static string NormalizeCategoryPath(string path)
        => string.Join(CategorySeparator, path
            .Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
