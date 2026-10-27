using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Installation;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// "Yeni Ürün Ekle" and the simple product editor for store employees. Writes the same Smartstore
/// <see cref="Product"/>, <see cref="ProductCategory"/> and <see cref="ProductMediaFile"/> records the classic
/// editor writes, through the tracked <see cref="SmartDbContext"/>, so Smartstore's hooks (audit dates, caches,
/// main picture, supplier sale guard) run as usual. The simple editor writes only the fields it shows.
/// </summary>
public class SimpleProductService
{
    const int ThumbnailSize = 256;

    private readonly SmartDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly IUrlService _urlService;
    private readonly IMediaService _mediaService;
    private readonly SubmissionRegistry _submissions;

    public SimpleProductService(
        SmartDbContext db,
        IActivityLogger activityLogger,
        IUrlService urlService,
        IMediaService mediaService,
        SubmissionRegistry submissions)
    {
        _db = db;
        _activityLogger = activityLogger;
        _urlService = urlService;
        _mediaService = mediaService;
        _submissions = submissions;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    private string R(string key, params object[] args)
        => T("Plugins.MotorShop.AdminPanel." + key, args);

    #region Create

    public async Task<SimpleProductResult> CreateAsync(QuickProductModel model, IReadOnlyList<IFormFile> photos, int customerId)
    {
        Guard.NotNull(model);
        photos ??= [];

        var key = model.SubmissionId.HasValue() ? $"{customerId}:{model.SubmissionId}" : null;
        if (key != null)
        {
            switch (_submissions.Begin(key, out var existingId))
            {
                case SubmissionRegistry.State.Completed:
                    var earlier = await GetResultAsync(existingId);
                    if (earlier != null)
                    {
                        earlier.Message = R("Create.AlreadySaved");
                        return earlier;
                    }
                    _submissions.Abandon(key);
                    _submissions.Begin(key, out _);
                    break;
                case SubmissionRegistry.State.InProgress:
                    return SimpleProductResult.Fail(string.Empty, R("Create.InProgress"));
            }
        }

        try
        {
            var result = await CreateCoreAsync(model, photos);
            if (key != null)
            {
                if (result.Success)
                {
                    _submissions.Complete(key, result.ProductId);
                }
                else
                {
                    _submissions.Abandon(key);
                }
            }

            return result;
        }
        catch
        {
            if (key != null)
            {
                _submissions.Abandon(key);
            }
            throw;
        }
    }

    private async Task<SimpleProductResult> CreateCoreAsync(QuickProductModel model, IReadOnlyList<IFormFile> photos)
    {
        var errors = new Dictionary<string, string>();

        var name = ValidateName(model.Name, errors);
        var price = ValidatePrice(model.Price, required: model.Publish, errors);
        var stock = ValidateStock(model.Stock, required: false, errors);
        var shortDescription = ValidateShortDescription(model.ShortDescription, errors);

        if (model.CategoryId <= 0 || !await _db.Categories.AnyAsync(x => x.Id == model.CategoryId && !x.Deleted))
        {
            errors[nameof(model.CategoryId)] = R("Validation.CategoryRequired");
        }

        var sku = model.Sku?.Trim().NullEmpty();
        if (sku != null)
        {
            if (sku.Length > 400)
            {
                errors[nameof(model.Sku)] = R("Validation.TooLong");
            }
            else if (await _db.Products.AnyAsync(x => x.Sku == sku && !x.Deleted))
            {
                errors[nameof(model.Sku)] = R("Validation.SkuTaken");
            }
        }

        if (model.Publish && price <= 0 && !errors.ContainsKey(nameof(model.Price)))
        {
            errors[nameof(model.Price)] = R("Publish.PriceRequired");
        }

        var checkedPhotos = ValidatePhotos(photos, errors);

        if (errors.Count > 0)
        {
            return new() { Errors = errors };
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            ProductType = ProductType.SimpleProduct,
            Visibility = ProductVisibility.Full,
            ProductTemplateId = await _db.ProductTemplates.OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstOrDefaultAsync(),
            Name = name,
            ShortDescription = shortDescription,
            Sku = sku ?? await GenerateSkuAsync(),
            Price = price,
            TaxCategoryId = await GetDefaultTaxCategoryIdAsync(),
            ManageInventoryMethod = ManageInventoryMethod.ManageStock,
            StockQuantity = stock,
            BackorderMode = BackorderMode.NoBackorders,
            DisplayStockAvailability = true,
            // Same defaults as the classic "add new product".
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 50,
            QuantityStep = 1,
            IsShippingEnabled = true,
            AllowCustomerReviews = true,
            MaximumCustomerEnteredPrice = 1000,
            DeliveryTimeId = await _db.DeliveryTimes.Where(x => x.IsDefault == true).Select(x => (int?)x.Id).FirstOrDefaultAsync(),
            Published = model.Publish,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };

        var violation = ProductRules.Validate(product);
        if (violation != null)
        {
            return SimpleProductResult.Fail(string.Empty, R(violation));
        }

        var useTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        var tx = useTransaction ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            product.ProductCategories.Add(new ProductCategory { CategoryId = model.CategoryId, DisplayOrder = 1 });
            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            var slug = await SaveSlugAsync(product);

            var fileIds = new List<int>();
            foreach (var photo in checkedPhotos)
            {
                int fileId;
                try
                {
                    fileId = await SavePhotoFileAsync(photo.File, $"{slug.NullEmpty() ?? "urun"}-{fileIds.Count + 1}{photo.Extension}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new PhotoSaveException(photo.File.FileName, ex);
                }

                fileIds.Add(fileId);
                _db.ProductMediaFiles.Add(new ProductMediaFile { ProductId = product.Id, MediaFileId = fileId, DisplayOrder = fileIds.Count });
            }

            if (fileIds.Count > 0)
            {
                product.MainPictureId = fileIds[0];
            }

            _activityLogger.LogActivity(KnownActivityLogTypes.AddNewProduct, T("ActivityLog.AddNewProduct", product.Name));
            await _db.SaveChangesAsync();

            if (tx != null)
            {
                await tx.CommitAsync();
            }
        }
        catch (Exception ex)
        {
            if (tx != null)
            {
                await tx.RollbackAsync(System.Threading.CancellationToken.None);

                // Tracked entities still hold the rolled back keys (e.g. a product id that no longer exists).
                _db.ChangeTracker.Clear();
            }

            if (ex is PhotoSaveException photoError && tx != null)
            {
                return SimpleProductResult.Fail("Photos", R("Photo.SaveFailed", photoError.FileName));
            }

            throw;
        }
        finally
        {
            if (tx != null)
            {
                await tx.DisposeAsync();
            }
        }

        var result = await GetResultAsync(product.Id);
        result.Message = R(result.Published ? "Create.Published" : "Create.Draft", result.ProductName);
        return result;
    }

    /// <summary>
    /// A readable, unique stock code for products created without one, e.g. "MP-261026-K7Q2D".
    /// </summary>
    internal async Task<string> GenerateSkuAsync()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = string.Create(5, alphabet, (span, chars) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
                }
            });

            var sku = $"MP-{DateTime.UtcNow:yyMMdd}-{code}";
            if (!await _db.Products.IgnoreQueryFilters().AnyAsync(x => x.Sku == sku))
            {
                return sku;
            }
        }

        return "MP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    /// <summary>
    /// "Genel KDV" like the DefaultProductTaxCategory migration, else the category most products of the store use.
    /// A product without a tax category would show "KDV: 0,00" in cart and invoice.
    /// </summary>
    private async Task<int> GetDefaultTaxCategoryIdAsync()
    {
        var generalName = TurkishStoreDefaults.TaxCategories[0].Name;
        var general = await _db.TaxCategories.Where(x => x.Name == generalName).Select(x => x.Id).FirstOrDefaultAsync();
        if (general > 0)
        {
            return general;
        }

        return await _db.Products
            .Where(x => !x.Deleted && !x.IsSystemProduct && x.TaxCategoryId > 0)
            .GroupBy(x => x.TaxCategoryId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Simple editor

    public async Task<SimpleProductModel> LoadAsync(int id)
    {
        var product = await _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ProductCategories)
            .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return null;
        }

        var mappings = ActiveMappings(product);

        return new SimpleProductModel
        {
            Id = product.Id,
            Version = product.UpdatedOnUtc.Ticks,
            Name = product.Name,
            CategoryId = mappings.FirstOrDefault()?.CategoryId ?? 0,
            OtherCategories = [.. mappings.Skip(1).Select(x => x.Category.Name)],
            Price = FormatAmount(product.Price),
            Stock = product.StockQuantity.ToString(CultureInfo.InvariantCulture),
            ShortDescription = product.ShortDescription,
            Published = product.Published,
            Sku = product.Sku,
            ManufacturerPartNumber = product.ManufacturerPartNumber,
            ManageInventoryMethod = product.ManageInventoryMethod,
            CallForPrice = product.CallForPrice,
            SpecialPrice = product.SpecialPrice.HasValue ? FormatAmount(product.SpecialPrice.Value) : null,
            TaxCategoryName = await _db.TaxCategories.Where(x => x.Id == product.TaxCategoryId).Select(x => x.Name).FirstOrDefaultAsync(),
            Slug = await GetSlugAsync(product.Id),
            Photos = await GetPhotosAsync(product.Id)
        };
    }

    /// <param name="canEditCategories">Without <c>Catalog.Product.EditCategory</c> the category is not changed.</param>
    public async Task<SimpleProductResult> SaveAsync(SimpleProductModel model, bool canEditCategories)
    {
        Guard.NotNull(model);

        var product = await _db.Products
            .Include(x => x.ProductCategories)
            .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == model.Id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return SimpleProductResult.Fail(string.Empty, R("NotFound"));
        }

        if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes)
        {
            return SimpleProductResult.Fail(string.Empty, R("Simple.VariantProduct"));
        }

        if (product.UpdatedOnUtc.Ticks != model.Version)
        {
            var conflict = SimpleProductResult.Fail(string.Empty, R("Simple.Conflict"));
            conflict.Conflict = true;
            return conflict;
        }

        var errors = new Dictionary<string, string>();
        var name = ValidateName(model.Name, errors);
        var price = ValidatePrice(model.Price, required: true, errors);
        var tracksStock = product.ManageInventoryMethod == ManageInventoryMethod.ManageStock;
        var stock = tracksStock ? ValidateStock(model.Stock, required: true, errors) : product.StockQuantity;
        var shortDescription = ValidateShortDescription(model.ShortDescription, errors);

        var mappings = ActiveMappings(product);
        var changeCategory = canEditCategories && model.CategoryId > 0 && model.CategoryId != mappings.FirstOrDefault()?.CategoryId;
        if (changeCategory && !await _db.Categories.AnyAsync(x => x.Id == model.CategoryId && !x.Deleted))
        {
            errors[nameof(model.CategoryId)] = R("Validation.CategoryRequired");
        }

        var callForPrice = PriceVisibility(product.CallForPrice, product.Price, price, model.ShowPrice);
        if (!callForPrice && price <= 0 && !errors.ContainsKey(nameof(model.Price)) && (model.ShowPrice == true || model.Published))
        {
            errors[nameof(model.Price)] = R(model.ShowPrice == true && !model.Published ? "Simple.ShowPriceNeedsPrice" : "Publish.PriceRequired");
        }

        if (model.Published)
        {
            if (mappings.Count == 0 && !changeCategory && !errors.ContainsKey(nameof(model.CategoryId)))
            {
                errors[nameof(model.CategoryId)] = R("Publish.CategoryRequired");
            }
        }

        if (errors.Count > 0)
        {
            // Nothing was changed on the tracked entity yet, so the request-wide SaveChanges filter has nothing to persist.
            return new() { Errors = errors };
        }

        product.Name = name;
        product.ShortDescription = shortDescription;
        product.Price = price;
        product.CallForPrice = callForPrice;
        product.StockQuantity = stock;
        product.Published = model.Published;

        var violation = ProductRules.Validate(product, ProductRules.Fields.Price, ProductRules.Fields.CallForPrice, ProductRules.Fields.Stock, ProductRules.Fields.Published);
        if (violation != null)
        {
            // The request-wide SaveChanges filter would otherwise persist the refused values.
            await _db.Entry(product).ReloadAsync();
            return SimpleProductResult.Fail(string.Empty, R(violation));
        }

        if (changeCategory)
        {
            ReplaceMainCategory(product, mappings, model.CategoryId);
        }

        await _db.SaveChangesAsync();

        // Keep an existing slug (links and SEO); only a product without any gets one.
        if ((await GetSlugAsync(product.Id)).IsEmpty())
        {
            await SaveSlugAsync(product);
        }

        _activityLogger.LogActivity(KnownActivityLogTypes.EditProduct, T("ActivityLog.EditProduct", product.Name));
        await _db.SaveChangesAsync();

        var result = await GetResultAsync(product.Id);
        result.Message = R("Simple.Saved");

        // A save hook (e.g. the supplier sale guard of MotorShop.CatalogImport) may keep the price hidden.
        if (!callForPrice && result.CallForPrice)
        {
            result.Warning = R("Simple.PriceStillHidden");
        }

        return result;
    }

    /// <summary>
    /// Whether the price stays hidden ("Fiyat için arayın") after a save.
    /// </summary>
    /// <param name="showPrice">The form's "Fiyat sitede görünsün" choice; <c>null</c> when the form did not send it.</param>
    /// <remarks>
    /// Without an explicit choice, "call for price" that only stands in for a missing price (stored price 0) is lifted
    /// as soon as a price is entered, the same rule the supplier import uses. A price an admin hid on purpose
    /// (stored price above 0) stays hidden.
    /// </remarks>
    internal static bool PriceVisibility(bool callForPrice, decimal oldPrice, decimal newPrice, bool? showPrice)
    {
        if (showPrice.HasValue)
        {
            return !showPrice.Value;
        }

        return callForPrice && !(oldPrice <= 0 && newPrice > 0);
    }

    /// <summary>
    /// The first mapping is the category the simple form shows. It is replaced; all other mappings (and their order)
    /// stay. Choosing a category the product already has makes that one the first.
    /// </summary>
    internal void ReplaceMainCategory(Product product, List<ProductCategory> mappings, int categoryId)
    {
        var main = mappings.FirstOrDefault();
        var existing = mappings.FirstOrDefault(x => x.CategoryId == categoryId);

        // Strictly before every other mapping, so the new main category is first regardless of ids.
        var others = mappings.Where(x => x != existing && x != main).Select(x => x.DisplayOrder).ToList();
        var order = main?.DisplayOrder ?? 1;
        var first = others.Count == 0 ? order : Math.Min(order, others.Min() - 1);

        if (main != null)
        {
            _db.ProductCategories.Remove(main);
        }

        if (existing != null)
        {
            existing.DisplayOrder = first;
        }
        else
        {
            product.ProductCategories.Add(new ProductCategory { ProductId = product.Id, CategoryId = categoryId, DisplayOrder = first });
        }
    }

    /// <summary>
    /// Mappings to existing categories, in the storefront's order.
    /// </summary>
    private static List<ProductCategory> ActiveMappings(Product product)
        => [.. product.ProductCategories
            .Where(x => x.Category != null && !x.Category.Deleted)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)];

    #endregion

    #region Photos of an existing product

    /// <param name="version">
    /// The editor's version. When it is current, the new version is returned so the editor can still save;
    /// when someone else changed the product meanwhile, the old one is kept so that save reports the conflict.
    /// </param>
    public async Task<SimpleProductResult> AddPhotosAsync(int id, long version, IReadOnlyList<IFormFile> photos)
    {
        var product = await _db.Products
            .Include(x => x.ProductMediaFiles)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return SimpleProductResult.Fail(string.Empty, R("NotFound"));
        }

        var errors = new Dictionary<string, string>();
        if (photos == null || photos.Count == 0)
        {
            errors["Photos"] = R("Photo.None");
        }

        var checkedPhotos = ValidatePhotos(photos ?? [], errors);
        if (errors.Count > 0)
        {
            return new() { Errors = errors };
        }

        var wasCurrent = product.UpdatedOnUtc.Ticks == version;
        var slug = (await GetSlugAsync(product.Id)).NullEmpty() ?? "urun";
        var order = product.ProductMediaFiles.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max();
        var added = 0;

        foreach (var photo in checkedPhotos)
        {
            int fileId;
            try
            {
                fileId = await SavePhotoFileAsync(photo.File, $"{slug}-{order + 1}{photo.Extension}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Photos saved before the failure stay assigned; the message names the one that failed.
                if (added > 0)
                {
                    await _db.SaveChangesAsync();
                }
                return SimpleProductResult.Fail("Photos", R("Photo.SaveFailed", photo.File.FileName));
            }

            _db.ProductMediaFiles.Add(new ProductMediaFile { ProductId = product.Id, MediaFileId = fileId, DisplayOrder = ++order });
            product.MainPictureId ??= fileId;
            added++;
        }

        await _db.SaveChangesAsync();
        _activityLogger.LogActivity(KnownActivityLogTypes.EditProduct, T("ActivityLog.EditProduct", product.Name));
        await _db.SaveChangesAsync();

        return await PhotoResultAsync(product.Id, wasCurrent ? null : version, R("Photo.Added", added));
    }

    public async Task<SimpleProductResult> RemovePhotoAsync(int id, long version, int fileId)
    {
        var product = await _db.Products
            .Include(x => x.ProductMediaFiles)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        var mapping = product?.ProductMediaFiles.FirstOrDefault(x => x.MediaFileId == fileId);
        if (mapping == null)
        {
            return SimpleProductResult.Fail(string.Empty, R("NotFound"));
        }

        var wasCurrent = product.UpdatedOnUtc.Ticks == version;

        // Only the assignment is removed, like the classic editor; the file stays in the media manager.
        _db.ProductMediaFiles.Remove(mapping);
        product.MainPictureId = product.ProductMediaFiles
            .Where(x => x != mapping)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => (int?)x.MediaFileId)
            .FirstOrDefault();

        await _db.SaveChangesAsync();

        return await PhotoResultAsync(product.Id, wasCurrent ? null : version, R("Photo.Removed"));
    }

    /// <param name="fileIds">All photo ids of the product in the new order; the first one becomes the main photo.</param>
    public async Task<SimpleProductResult> ReorderPhotosAsync(int id, long version, int[] fileIds)
    {
        var product = await _db.Products
            .Include(x => x.ProductMediaFiles)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemProduct);

        if (product == null)
        {
            return SimpleProductResult.Fail(string.Empty, R("NotFound"));
        }

        fileIds ??= [];
        var current = product.ProductMediaFiles.Select(x => x.MediaFileId).ToHashSet();
        if (fileIds.Length != current.Count || !current.SetEquals(fileIds))
        {
            return SimpleProductResult.Fail(string.Empty, R("Photo.OrderChanged"));
        }

        var wasCurrent = product.UpdatedOnUtc.Ticks == version;
        for (var i = 0; i < fileIds.Length; i++)
        {
            product.ProductMediaFiles.First(x => x.MediaFileId == fileIds[i]).DisplayOrder = i + 1;
        }
        product.MainPictureId = fileIds.Length > 0 ? fileIds[0] : null;

        await _db.SaveChangesAsync();

        return await PhotoResultAsync(product.Id, wasCurrent ? null : version, R("Photo.Ordered"));
    }

    private async Task<SimpleProductResult> PhotoResultAsync(int productId, long? keepVersion, string message)
    {
        var result = await GetResultAsync(productId);
        if (keepVersion.HasValue)
        {
            result.Version = keepVersion.Value;
        }
        result.Message = message;
        return result;
    }

    #endregion

    #region Validation

    internal string ValidateName(string input, Dictionary<string, string> errors)
    {
        var name = CategoryEditService.NormalizeName(input);
        if (name.Length == 0)
        {
            errors["Name"] = R("Validation.NameRequired");
        }
        else if (name.Length > 400)
        {
            errors["Name"] = R("Validation.TooLong");
        }

        return name;
    }

    /// <returns>The price, 0 when empty and not required.</returns>
    internal decimal ValidatePrice(string input, bool required, Dictionary<string, string> errors)
    {
        if (input.IsEmpty() && !required)
        {
            return 0;
        }

        if (!AmountParser.TryParse(input, out var price, out var error))
        {
            errors["Price"] = error == "TooManyDecimals" ? R("Validation.PriceDecimals") : R("Validation.PriceInvalid");
            return 0;
        }

        return price;
    }

    /// <summary>
    /// Whole number 0..1.000.000; "1.000" is read as one thousand. Empty is 0 unless required.
    /// </summary>
    internal int ValidateStock(string input, bool required, Dictionary<string, string> errors)
    {
        var s = input?.Trim().Replace(".", string.Empty).Replace(" ", string.Empty);
        if (s.IsEmpty())
        {
            if (required)
            {
                errors["Stock"] = R("Validation.StockInvalid");
            }
            return 0;
        }

        if (!s.All(char.IsAsciiDigit) || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var stock) || stock > ProductRules.MaxStock)
        {
            errors["Stock"] = R("Validation.StockInvalid");
            return 0;
        }

        return stock;
    }

    internal string ValidateShortDescription(string input, Dictionary<string, string> errors)
    {
        var text = input?.Trim().NullEmpty();
        if (text?.Length > 4000)
        {
            errors["ShortDescription"] = R("Validation.TooLong");
        }

        return text;
    }

    internal record CheckedPhoto(IFormFile File, string Extension);

    internal List<CheckedPhoto> ValidatePhotos(IReadOnlyList<IFormFile> photos, Dictionary<string, string> errors)
    {
        var result = new List<CheckedPhoto>();
        if (photos.Count > ProductPhotoRules.MaxPhotos)
        {
            errors["Photos"] = R("Photo.TooMany", ProductPhotoRules.MaxPhotos);
            return result;
        }

        foreach (var photo in photos)
        {
            byte[] header;
            using (var stream = photo.OpenReadStream())
            {
                header = ProductPhotoRules.ReadHeader(stream);
            }

            var problem = ProductPhotoRules.Validate(photo.ContentType, photo.Length, header);
            if (problem != null)
            {
                errors["Photos"] = R(problem, photo.FileName, ProductPhotoRules.MaxBytes / 1024 / 1024);
                return result;
            }

            result.Add(new CheckedPhoto(photo, ProductPhotoRules.DetectExtension(header)));
        }

        return result;
    }

    #endregion

    #region Data access (virtual for tests)

    public async Task<SimpleProductResult> GetResultAsync(int productId)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new { x.Id, x.Name, x.UpdatedOnUtc, x.Published, x.CallForPrice })
            .FirstOrDefaultAsync();

        if (product == null)
        {
            return null;
        }

        return new SimpleProductResult
        {
            Success = true,
            ProductId = product.Id,
            ProductName = product.Name,
            Version = product.UpdatedOnUtc.Ticks,
            Published = product.Published,
            CallForPrice = product.CallForPrice,
            Slug = await GetSlugAsync(product.Id),
            Photos = await GetPhotosAsync(product.Id)
        };
    }

    protected virtual async Task<string> SaveSlugAsync(Product product)
    {
        var result = await _urlService.SaveSlugAsync(product, null, product.Name, true);
        return result.Slug;
    }

    protected virtual Task<string> GetSlugAsync(int productId)
        => _urlService.GetActiveSlugAsync(productId, nameof(Product), 0);

    protected virtual async Task<int> SavePhotoFileAsync(IFormFile photo, string fileName)
    {
        using var stream = photo.OpenReadStream();
        var file = await _mediaService.SaveFileAsync(SystemAlbumProvider.Catalog + "/" + fileName, stream, false, DuplicateFileHandling.Rename);
        return file.Id;
    }

    protected virtual async Task<List<ProductPhotoModel>> GetPhotosAsync(int productId)
    {
        var fileIds = await _db.ProductMediaFiles
            .AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => x.MediaFileId)
            .ToArrayAsync();

        if (fileIds.Length == 0)
        {
            return [];
        }

        var files = (await _mediaService.GetFilesByIdsAsync(fileIds)).ToDictionarySafe(x => x.Id);

        return [.. fileIds
            .Where(files.ContainsKey)
            .Select(id => new ProductPhotoModel { FileId = id, Name = files[id].Name, ThumbUrl = _mediaService.GetUrl(files[id], ThumbnailSize) })];
    }

    #endregion

    internal static string FormatAmount(decimal value)
        => value.ToString("#,0.00", CultureInfo.GetCultureInfo("tr-TR"));

    private sealed class PhotoSaveException(string fileName, Exception inner) : Exception(inner.Message, inner)
    {
        public string FileName { get; } = fileName;
    }
}
