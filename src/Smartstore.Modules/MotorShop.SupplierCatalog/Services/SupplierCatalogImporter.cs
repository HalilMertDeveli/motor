using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Content.Media.Storage;
using Smartstore.Core.Data;
using Smartstore.Core.DataExchange.Import;
using Smartstore.Core.Seo;
using Smartstore.IO;

namespace MotorShop.SupplierCatalog.Services;

public class SupplierCatalogImportOptions
{
    /// <summary>
    /// Publish products that have no price. When <c>false</c> they are imported unpublished.
    /// </summary>
    public bool PublishUnpricedProducts { get; set; }

    /// <summary>
    /// Overwrite name, descriptions and price of products that already exist (matched by SKU).
    /// </summary>
    public bool UpdateExistingProducts { get; set; } = true;
}

public class SupplierCatalogImportResult
{
    public int CategoriesCreated { get; set; }
    public int BrandsCreated { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsUnpublished { get; set; }
    public int PicturesAdded { get; set; }
    public List<string> Warnings { get; } = [];
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Imports a supplier catalog package: category tree, brands, products (matched by SKU),
/// a size attribute and product pictures. Running it again updates instead of duplicating.
/// </summary>
public class SupplierCatalogImporter
{
    const string CategorySeparator = " > ";
    const string SizeAttributeName = "Beden";
    const int BatchSize = 100;

    private readonly SmartDbContext _db;
    private readonly IUrlService _urlService;
    private readonly IMediaImporter _mediaImporter;

    public SupplierCatalogImporter(SmartDbContext db, IUrlService urlService, IMediaImporter mediaImporter)
    {
        _db = db;
        _urlService = urlService;
        _mediaImporter = mediaImporter;
    }

    public async Task<SupplierCatalogImportResult> ImportAsync(
        CatalogPackage package,
        SupplierCatalogImportOptions options,
        CancellationToken cancelToken = default)
    {
        Guard.NotNull(package);
        Guard.NotNull(options);

        var watch = Stopwatch.StartNew();
        var result = new SupplierCatalogImportResult();
        var items = package.Products
            .Where(x => x.Sku.HasValue() && x.Name.HasValue())
            .DistinctBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

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

            var pairs = new List<(CatalogPackageProduct Item, Product Product, bool IsNew)>();

            foreach (var item in batch)
            {
                var sku = item.Sku.Trim();
                var isNew = !existing.TryGetValue(sku, out var product);

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
                        CreatedOnUtc = DateTime.UtcNow
                    };

                    _db.Products.Add(product);
                }

                if (isNew || options.UpdateExistingProducts)
                {
                    ApplyValues(item, product, options);
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
                    var slug = await _urlService.ValidateSlugAsync(product, null, product.Name, true);
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

    private static void ApplyValues(CatalogPackageProduct item, Product product, SupplierCatalogImportOptions options)
    {
        product.Name = item.Name.Trim().Truncate(400);
        product.ShortDescription = item.ShortDescription.NullEmpty();
        product.FullDescription = item.DescriptionHtml.NullEmpty();
        product.ManufacturerPartNumber = item.CatalogCode.NullEmpty()?.Truncate(400);
        product.Price = item.Price ?? 0;
        product.Published = item.Price.HasValue || options.PublishUnpricedProducts;
        product.DisplayOrder = item.DisplayOrder;
        product.UpdatedOnUtc = DateTime.UtcNow;
    }

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
                var stream = await package.OpenImageAsync(fileName);
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
        catch (Exception ex)
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

        foreach (var path in paths)
        {
            var parentId = 0;
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

                var category = all.FirstOrDefault(x => x.ParentId == parentId && x.Name.EqualsNoCase(name));
                if (category == null)
                {
                    category = new Category
                    {
                        Name = name,
                        ParentId = parentId,
                        CategoryTemplateId = templateId,
                        Published = true,
                        DisplayOrder = all.Count(x => x.ParentId == parentId) + 1,
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
                    Published = true,
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

    private static string NormalizeCategoryPath(string path)
        => string.Join(CategorySeparator, path
            .Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
