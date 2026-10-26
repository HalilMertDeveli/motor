using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;
using Smartstore.Events;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Writes of the product screens. Everything goes through the tracked <see cref="SmartDbContext"/>, so
/// Smartstore's hooks run as in the classic editor (audit dates, low-stock activity, back-in-stock mails,
/// soft delete, search index).
/// </summary>
public class ProductEditService
{
    const int BatchSize = 100;
    public const int MaxBulkProducts = 5000;

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly PanelQueryService _queries;
    private readonly IProductCloner _productCloner;
    private readonly IProductTagService _productTagService;
    private readonly IUrlService _urlService;
    private readonly IMediaService _mediaService;
    private readonly IEventPublisher _eventPublisher;

    public ProductEditService(
        SmartDbContext db,
        ICommonServices services,
        PanelQueryService queries,
        IProductCloner productCloner,
        IProductTagService productTagService,
        IUrlService urlService,
        IMediaService mediaService,
        IEventPublisher eventPublisher)
    {
        _db = db;
        _services = services;
        _queries = queries;
        _productCloner = productCloner;
        _productTagService = productTagService;
        _urlService = urlService;
        _mediaService = mediaService;
        _eventPublisher = eventPublisher;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    private string R(string key, params object[] args)
        => T("Plugins.MotorShop.AdminPanel." + key, args);

    #region Inline edit

    /// <param name="expected">The value the user saw (invariant culture). A different stored value means someone else changed it.</param>
    public async Task<FieldUpdateResult> UpdateFieldAsync(int id, string field, string value, string expected)
    {
        var product = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);
        if (product == null)
        {
            return new() { Error = R("NotFound") };
        }

        if (expected != null && expected != CurrentValue(product, field))
        {
            return new() { Conflict = true, Error = R("Conflict"), Value = CurrentValue(product, field), Display = Display(product, field) };
        }

        switch (field)
        {
            case ProductRules.Fields.Price:
                if (!AmountParser.TryParse(value, out var price, out var priceError))
                {
                    return new() { Error = R("Amount." + priceError) };
                }
                product.Price = price;
                break;

            case ProductRules.Fields.SpecialPrice:
                if (value.IsEmpty())
                {
                    product.SpecialPrice = null;
                    product.SpecialPriceStartDateTimeUtc = null;
                    product.SpecialPriceEndDateTimeUtc = null;
                }
                else if (AmountParser.TryParse(value, out var special, out var specialError))
                {
                    product.SpecialPrice = special;
                }
                else
                {
                    return new() { Error = R("Amount." + specialError) };
                }
                break;

            case ProductRules.Fields.Stock:
                if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock)
                {
                    return new() { Error = R("Rule.StockNotTracked") };
                }
                if (!int.TryParse(value?.Trim().Replace(".", string.Empty), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var stock))
                {
                    return new() { Error = R("Rule.Stock") };
                }
                product.StockQuantity = stock;
                break;

            case ProductRules.Fields.CallForPrice:
                product.CallForPrice = ParseBool(value);
                break;

            case ProductRules.Fields.Published:
                product.Published = ParseBool(value);

                // An incomplete product is not published from the list: customers could not find it in any category.
                if (product.Published && !await _db.ProductCategories.AnyAsync(x => x.ProductId == product.Id && !x.Category.Deleted))
                {
                    await _db.Entry(product).ReloadAsync();
                    return new() { Error = R("Publish.CategoryRequired") };
                }
                break;

            default:
                return new() { Error = R("NotFound") };
        }

        var violation = ProductRules.Validate(product, field);
        if (violation != null)
        {
            // The request-wide SaveChanges filter would otherwise persist the refused value.
            await _db.Entry(product).ReloadAsync();
            return new() { Error = R(violation) };
        }

        await _db.SaveChangesAsync();

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditProduct, R("Log.FieldChanged", product.Name, field, CurrentValue(product, field)));
        await _db.SaveChangesAsync();

        return new() { Success = true, Value = CurrentValue(product, field), Display = Display(product, field) };
    }

    internal static string CurrentValue(Product p, string field) => field switch
    {
        ProductRules.Fields.Price => p.Price.ToString("0.####", CultureInfo.InvariantCulture),
        ProductRules.Fields.SpecialPrice => p.SpecialPrice?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        ProductRules.Fields.Stock => p.StockQuantity.ToString(CultureInfo.InvariantCulture),
        ProductRules.Fields.CallForPrice => p.CallForPrice ? "true" : "false",
        ProductRules.Fields.Published => p.Published ? "true" : "false",
        _ => null
    };

    private string Display(Product p, string field) => field switch
    {
        ProductRules.Fields.Price => _services.CurrencyService.CreateMoney(p.Price, _services.CurrencyService.PrimaryCurrency).ToString(),
        ProductRules.Fields.SpecialPrice => p.SpecialPrice.HasValue ? _services.CurrencyService.CreateMoney(p.SpecialPrice.Value, _services.CurrencyService.PrimaryCurrency).ToString() : "-",
        ProductRules.Fields.Stock => p.StockQuantity.ToString("N0"),
        _ => CurrentValue(p, field)
    };

    private static bool ParseBool(string value)
        => value.EqualsNoCase("true") || value == "1" || value.EqualsNoCase("on");

    #endregion

    #region Bulk

    public async Task<BulkResult> BulkAsync(BulkRequest request)
    {
        var result = new BulkResult();

        // Validate the parameter once, before touching any product.
        decimal amount = 0;
        int stock = 0;
        switch (request.Action)
        {
            case BulkAction.PricePercent when !AmountParser.TryParsePercent(request.Value, out amount):
            case BulkAction.PriceFixed when !AmountParser.TryParseSigned(request.Value, out amount):
                result.Error = R("Bulk.InvalidValue");
                return result;
            case BulkAction.SetStock when !int.TryParse(request.Value, NumberStyles.None, CultureInfo.InvariantCulture, out stock) || stock > ProductRules.MaxStock:
                result.Error = R("Rule.Stock");
                return result;
            case BulkAction.AddCategory or BulkAction.RemoveCategory when !await _db.Categories.AnyAsync(x => x.Id == request.CategoryId && !x.Deleted):
                result.Error = R("Bulk.InvalidCategory");
                return result;
        }

        var ids = await ResolveIdsAsync(request);
        result.Requested = ids.Length;
        if (ids.Length == 0)
        {
            result.Error = R("Bulk.NothingSelected");
            return result;
        }
        if (ids.Length > MaxBulkProducts)
        {
            result.Error = R("Bulk.TooMany", MaxBulkProducts);
            return result;
        }

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var query = _db.Products.Where(x => chunk.Contains(x.Id) && !x.Deleted && !x.IsSystemProduct);
            if (request.Action is BulkAction.AddCategory or BulkAction.RemoveCategory or BulkAction.Publish)
            {
                query = query.Include(x => x.ProductCategories).ThenInclude(x => x.Category);
            }

            var products = await query.ToListAsync();
            result.Skipped += chunk.Length - products.Count;

            foreach (var product in products)
            {
                var reason = Apply(product, request, amount, stock);
                if (reason == null)
                {
                    result.Updated++;
                }
                else if (reason.Length == 0)
                {
                    result.Skipped++;
                }
                else
                {
                    result.Failures.Add(new() { Id = product.Id, Name = product.Name, Reason = reason });
                }
            }

            await _db.SaveChangesAsync();
        }

        _services.ActivityLogger.LogActivity(
            request.Action == BulkAction.Delete ? KnownActivityLogTypes.DeleteProduct : KnownActivityLogTypes.EditProduct,
            R("Log.Bulk", request.Action.ToString(), request.Value ?? request.CategoryId.ToStringInvariant(), result.Updated, result.Failures.Count));
        await _db.SaveChangesAsync();

        return result;
    }

    /// <returns><c>null</c> when applied, an empty string when nothing had to change, otherwise the failure reason.</returns>
    private string Apply(Product product, BulkRequest request, decimal amount, int stock)
    {
        // Snapshot to undo a change that breaks a rule.
        var (price, special, stockQty, method, cfp, published) =
            (product.Price, product.SpecialPrice, product.StockQuantity, product.ManageInventoryMethod, product.CallForPrice, product.Published);

        string[] changed;
        switch (request.Action)
        {
            case BulkAction.PricePercent:
                product.Price = AmountParser.RoundPrice(product.Price * (1 + amount / 100m));
                changed = [ProductRules.Fields.Price];
                break;
            case BulkAction.PriceFixed:
                product.Price = AmountParser.RoundPrice(product.Price + amount);
                changed = [ProductRules.Fields.Price];
                break;
            case BulkAction.SetStock:
                if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock)
                {
                    return R("Rule.StockNotTracked");
                }
                product.StockQuantity = stock;
                changed = [ProductRules.Fields.Stock];
                break;
            case BulkAction.TrackStock or BulkAction.UntrackStock:
                if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes)
                {
                    return R("Rule.StockByAttributes");
                }
                product.ManageInventoryMethod = request.Action == BulkAction.TrackStock ? ManageInventoryMethod.ManageStock : ManageInventoryMethod.DontManageStock;
                changed = [];
                break;
            case BulkAction.HidePrice or BulkAction.ShowPrice:
                product.CallForPrice = request.Action == BulkAction.HidePrice;
                changed = [ProductRules.Fields.CallForPrice];
                break;
            case BulkAction.Publish when !product.Published && !product.ProductCategories.Any(x => x.Category != null && !x.Category.Deleted):
                return R("Publish.CategoryRequired");
            case BulkAction.Publish or BulkAction.Unpublish:
                product.Published = request.Action == BulkAction.Publish;
                changed = [ProductRules.Fields.Published];
                break;
            case BulkAction.AddCategory:
                if (product.ProductCategories.Any(x => x.CategoryId == request.CategoryId))
                {
                    return string.Empty;
                }
                product.ProductCategories.Add(new ProductCategory { CategoryId = request.CategoryId, ProductId = product.Id });
                return null;
            case BulkAction.RemoveCategory:
                var mapping = product.ProductCategories.FirstOrDefault(x => x.CategoryId == request.CategoryId);
                if (mapping == null)
                {
                    return string.Empty;
                }
                _db.ProductCategories.Remove(mapping);
                return null;
            case BulkAction.Delete:
                // Soft delete through Smartstore's hook, like the classic list.
                _db.Products.Remove(product);
                return null;
            default:
                return R("Bulk.InvalidValue");
        }

        var violation = ProductRules.Validate(product, changed);
        if (violation != null)
        {
            (product.Price, product.SpecialPrice, product.StockQuantity, product.ManageInventoryMethod, product.CallForPrice, product.Published) =
                (price, special, stockQty, method, cfp, published);
            return R(violation);
        }

        return null;
    }

    private async Task<int[]> ResolveIdsAsync(BulkRequest request)
    {
        if (request.AllMatching)
        {
            var filter = request.Filter ?? new ProductFilter();
            filter.Normalize();

            return await _queries.BuildProductQuery(filter)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(MaxBulkProducts + 1)
                .ToArrayAsync();
        }

        return request.Ids?.Where(x => x > 0).Distinct().Take(MaxBulkProducts + 1).ToArray() ?? [];
    }

    #endregion

    #region Duplicate, delete

    public async Task<Product> DuplicateAsync(int id)
    {
        // Loaded untracked with the navigation properties the cloner needs, like the classic "copy product".
        var product = await _db.Products
            .AsSplitQuery()
            .Include(x => x.ProductCategories)
            .Include(x => x.ProductManufacturers)
            .Include(x => x.ProductSpecificationAttributes)
            .Include(x => x.ProductVariantAttributes)
            .Include(x => x.ProductVariantAttributeCombinations)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return null;
        }

        // The copy is never published, so a copied price or stock cannot go live by accident.
        var clone = await _productCloner.CloneProductAsync(product, T("Admin.Common.CopyOf", product.Name), false);
        await _eventPublisher.PublishAsync(new ProductClonedEvent(product, clone));

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.AddNewProduct, R("Log.Duplicated", product.Name, clone.Id));
        await _db.SaveChangesAsync();

        return clone;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);
        if (product == null)
        {
            return false;
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.DeleteProduct, T("ActivityLog.DeleteProduct", product.Name));
        await _db.SaveChangesAsync();

        return true;
    }

    #endregion

    #region Editor

    public async Task<ProductEditorModel> LoadEditorAsync(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ProductCategories)
            .Include(x => x.ProductTags)
            .Include(x => x.ProductMediaFiles)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return null;
        }

        var model = new ProductEditorModel
        {
            Id = product.Id,
            Version = product.UpdatedOnUtc.Ticks,
            Name = product.Name,
            Sku = product.Sku,
            ManufacturerPartNumber = product.ManufacturerPartNumber,
            Gtin = product.Gtin,
            ShortDescription = product.ShortDescription,
            FullDescription = product.FullDescription,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            MetaKeywords = product.MetaKeywords,
            Price = FormatAmount(product.Price),
            ComparePrice = product.ComparePrice > 0 ? FormatAmount(product.ComparePrice) : null,
            SpecialPrice = product.SpecialPrice.HasValue ? FormatAmount(product.SpecialPrice.Value) : null,
            SpecialPriceStart = product.SpecialPriceStartDateTimeUtc,
            SpecialPriceEnd = product.SpecialPriceEndDateTimeUtc,
            CallForPrice = product.CallForPrice,
            ManageInventoryMethod = product.ManageInventoryMethod,
            StockQuantity = product.StockQuantity,
            MinStockQuantity = product.MinStockQuantity,
            LowStockActivity = product.LowStockActivity,
            OrderMinimumQuantity = product.OrderMinimumQuantity,
            OrderMaximumQuantity = product.OrderMaximumQuantity,
            CategoryIds = [.. product.ProductCategories.Select(x => x.CategoryId)],
            Tags = string.Join(", ", product.ProductTags.Select(x => x.Name)),
            IsShippingEnabled = product.IsShippingEnabled,
            IsFreeShipping = product.IsFreeShipping,
            Weight = FormatMeasure(product.Weight),
            Length = FormatMeasure(product.Length),
            Width = FormatMeasure(product.Width),
            Height = FormatMeasure(product.Height),
            Published = product.Published,
            Visibility = product.Visibility,
            ProductType = product.ProductType,
            SeName = await product.GetActiveSlugAsync(0, true, false)
        };

        await PrepareEditorAsync(model, product);
        return model;
    }

    public async Task PrepareEditorAsync(ProductEditorModel model, Product product = null)
    {
        product ??= await _db.Products.AsNoTracking().Include(x => x.ProductMediaFiles).FirstOrDefaultAsync(x => x.Id == model.Id);

        model.CurrencyCode = _services.CurrencyService.PrimaryCurrency.CurrencyCode;
        model.AllCategories = await _queries.GetCategoryOptionsAsync();
        model.Slug = product != null && product.Published ? await _urlService.GetActiveSlugAsync(product.Id, nameof(Product), 0) : null;

        if (product != null)
        {
            model.TaxCategoryName = await _db.TaxCategories.Where(x => x.Id == product.TaxCategoryId).Select(x => x.Name).FirstOrDefaultAsync();

            var fileIds = product.ProductMediaFiles.OrderBy(x => x.DisplayOrder).Select(x => x.MediaFileId).Take(12).ToArray();
            if (fileIds.Length > 0)
            {
                var files = await _mediaService.GetFilesByIdsAsync(fileIds);
                model.PictureUrls = [.. fileIds
                    .Select(id => files.FirstOrDefault(f => f.Id == id))
                    .Where(f => f != null)
                    .Select(f => _mediaService.GetUrl(f, 128))];
            }
        }
    }

    /// <returns>Field name to error message; empty on success.</returns>
    public async Task<Dictionary<string, string>> SaveEditorAsync(ProductEditorModel model)
    {
        var errors = new Dictionary<string, string>();

        var product = await _db.Products
            .Include(x => x.ProductCategories)
            .FirstOrDefaultAsync(x => x.Id == model.Id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            errors[""] = R("NotFound");
            return errors;
        }

        if (product.UpdatedOnUtc.Ticks != model.Version)
        {
            errors[""] = R("ConflictEditor");
            return errors;
        }

        decimal? Amount(string name, string input, bool optional, int maxDecimals = AmountParser.MaxDecimals)
        {
            if (input.IsEmpty() && optional)
            {
                return null;
            }

            if (AmountParser.TryParse(input, maxDecimals, out var value, out var error))
            {
                return value;
            }

            errors[name] = R("Amount." + error);
            return null;
        }

        var price = Amount(nameof(model.Price), model.Price, false);
        var compare = Amount(nameof(model.ComparePrice), model.ComparePrice, true);
        var special = Amount(nameof(model.SpecialPrice), model.SpecialPrice, true);
        var weight = Amount(nameof(model.Weight), model.Weight, true, 4);
        var length = Amount(nameof(model.Length), model.Length, true, 4);
        var width = Amount(nameof(model.Width), model.Width, true, 4);
        var height = Amount(nameof(model.Height), model.Height, true, 4);

        if (model.SpecialPriceStart.HasValue && model.SpecialPriceEnd.HasValue && model.SpecialPriceEnd < model.SpecialPriceStart)
        {
            errors[nameof(model.SpecialPriceEnd)] = R("Rule.SpecialPriceDates");
        }

        if (model.OrderMinimumQuantity < 1 || model.OrderMaximumQuantity < model.OrderMinimumQuantity)
        {
            errors[nameof(model.OrderMaximumQuantity)] = R("Rule.OrderQuantity");
        }

        var validCategoryIds = await _db.Categories
            .Where(x => model.CategoryIds.Contains(x.Id) && !x.Deleted)
            .Select(x => x.Id)
            .ToListAsync();

        if (errors.Count > 0)
        {
            return errors;
        }

        product.Name = model.Name.Trim();
        product.Sku = model.Sku?.Trim();
        product.ManufacturerPartNumber = model.ManufacturerPartNumber?.Trim();
        product.Gtin = model.Gtin?.Trim();
        product.ShortDescription = model.ShortDescription;
        product.FullDescription = model.FullDescription;
        product.MetaTitle = model.MetaTitle;
        product.MetaDescription = model.MetaDescription;
        product.MetaKeywords = model.MetaKeywords;

        product.Price = price.Value;
        product.ComparePrice = compare ?? 0;
        product.SpecialPrice = special;
        product.SpecialPriceStartDateTimeUtc = special.HasValue ? model.SpecialPriceStart : null;
        product.SpecialPriceEndDateTimeUtc = special.HasValue ? model.SpecialPriceEnd : null;
        product.CallForPrice = model.CallForPrice;

        // Per-variant stock lives on the combinations and stays in the classic editor.
        if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStockByAttributes
            && model.ManageInventoryMethod != ManageInventoryMethod.ManageStockByAttributes)
        {
            product.ManageInventoryMethod = model.ManageInventoryMethod;
        }
        product.StockQuantity = model.StockQuantity;
        product.MinStockQuantity = model.MinStockQuantity;
        product.LowStockActivity = model.LowStockActivity;
        product.OrderMinimumQuantity = model.OrderMinimumQuantity;
        product.OrderMaximumQuantity = model.OrderMaximumQuantity;

        product.IsShippingEnabled = model.IsShippingEnabled;
        product.IsFreeShipping = model.IsFreeShipping;
        product.Weight = weight ?? 0;
        product.Length = length ?? 0;
        product.Width = width ?? 0;
        product.Height = height ?? 0;

        product.Published = model.Published;
        product.Visibility = model.Visibility;

        var violation = ProductRules.Validate(product);
        if (violation != null)
        {
            // The request-wide SaveChanges filter would otherwise persist the refused values.
            await _db.Entry(product).ReloadAsync();
            errors[""] = R(violation);
            return errors;
        }

        // Categories: add new, remove unselected, keep existing mappings (and their display order).
        foreach (var mapping in product.ProductCategories.Where(x => !validCategoryIds.Contains(x.CategoryId)).ToList())
        {
            _db.ProductCategories.Remove(mapping);
        }
        foreach (var categoryId in validCategoryIds.Where(id => !product.ProductCategories.Any(x => x.CategoryId == id)))
        {
            product.ProductCategories.Add(new ProductCategory { CategoryId = categoryId, ProductId = product.Id });
        }

        await _db.SaveChangesAsync();

        var slug = await _urlService.SaveSlugAsync(product, model.SeName, product.GetDisplayName(), true);
        model.SeName = slug.Slug;

        await _productTagService.UpdateProductTagsAsync(product, (model.Tags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditProduct, T("ActivityLog.EditProduct", product.Name));
        await _db.SaveChangesAsync();

        return errors;
    }

    internal static string FormatAmount(decimal value)
        => value.ToString("#,0.00", CultureInfo.GetCultureInfo("tr-TR"));

    private static string FormatMeasure(decimal value)
        => value == 0 ? null : value.ToString("0.####", CultureInfo.GetCultureInfo("tr-TR"));

    #endregion
}
