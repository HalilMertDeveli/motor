using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MotorShop.CatalogImport.Domain;
using MotorShop.Fitment;
using MotorShop.Fitment.Domain;
using Smartstore;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;

namespace MotorShop.CatalogImport.Services;

/// <summary>
/// What is still missing before a part may move to state B (verified reference) or C (ready for sale).
/// Each entry is a resource key suffix under Plugins.MotorShop.CatalogImport.Requirement.
/// </summary>
public record PartReadiness(IReadOnlyList<string> MissingForReference, IReadOnlyList<string> MissingForSale)
{
    public bool CanBeReference => MissingForReference.Count == 0;
    public bool CanBeSold => MissingForReference.Count == 0 && MissingForSale.Count == 0;
}

public class PublicationException(string message) : Exception(message);

public record ProductConflict(int ProductId, string Name, string Sku, string Mpn, string Gtin);

/// <summary>
/// Turns reviewed parts into Smartstore products and keeps the product flags in line with the part's state:
/// A (candidate): no product. B (reference): published, buy button disabled, price hidden, stock not managed.
/// C (sale): purchasable at the approved price with stock from authorized supplier offers.
/// Rejected: product unpublished, never deleted.
/// </summary>
public class CatalogPublicationService
{
    /// <summary>
    /// Supplier stock older than this no longer supports a sale.
    /// </summary>
    public const int MaxOfferAgeDays = 30;

    private readonly SmartDbContext _db;
    private readonly IUrlService _urlService;

    public CatalogPublicationService(SmartDbContext db, IUrlService urlService)
    {
        _db = db;
        _urlService = urlService;
    }

    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public ILogger Logger { get; set; } = NullLogger.Instance;

    #region Readiness

    public async Task<PartReadiness> GetReadinessAsync(CatalogPart part, CancellationToken cancelToken = default)
    {
        Guard.NotNull(part);

        var reference = new List<string>();
        var sale = new List<string>();

        var source = await _db.CatalogSources().AsNoTracking().FirstOrDefaultAsync(x => x.Id == part.SourceId, cancelToken);

        if (part.State == PartPublicationState.Rejected) reference.Add("NotRejected");
        if (part.VerificationStatus != RecordVerificationStatus.Verified) reference.Add("VerifiedData");
        if (source == null || source.PermissionStatus < SourcePermissionStatus.PublicFactualReferenceOnly) reference.Add("SourcePermission");
        if (part.HasPendingChanges) reference.Add("NoPendingChanges");
        if (part.ProductId == null && (await FindConflictingProductsAsync(part, cancelToken)).Count > 0) reference.Add("NoProductConflict");

        var now = UtcNow();
        var hasOffer = await _db.CatalogSupplierOffers()
            .Where(x => x.PartId == part.Id && x.IsActive && x.VerifiedOnUtc >= now.AddDays(-MaxOfferAgeDays))
            .Join(_db.CatalogSources().Where(s => s.PermissionStatus == SourcePermissionStatus.Authorized), o => o.SourceId, s => s.Id, (o, s) => o)
            .AnyAsync(x => x.StockQuantity > 0 || x.LeadTimeDays != null, cancelToken);

        if (!hasOffer) sale.Add("AuthorizedSupply");
        if (!(part.ApprovedPrice > 0)) sale.Add("ApprovedPrice");
        if (!(part.TaxCategoryId > 0) || !await _db.TaxCategories.AnyAsync(x => x.Id == part.TaxCategoryId, cancelToken)) sale.Add("TaxCategory");
        if (!part.ImageRightsApproved) sale.Add("ImageRights");
        if (part.ProductId == null || !await _db.ProductMediaFiles.AnyAsync(x => x.ProductId == part.ProductId, cancelToken)) sale.Add("ProductImage");

        var openMatches = await _db.CatalogFitmentMatches()
            .AnyAsync(x => x.PartId == part.Id && x.Status == FitmentMatchStatus.Proposed, cancelToken);
        if (openMatches) sale.Add("FitmentReviewed");

        return new PartReadiness(reference, sale);
    }

    #endregion

    #region State transitions

    /// <summary>
    /// A → B. Creates (or updates) the product as non-purchasable reference content.
    /// </summary>
    public async Task<CatalogPart> ApproveReferenceAsync(int partId, string approvedBy, CancellationToken cancelToken = default)
    {
        var part = await GetPartAsync(partId, cancelToken);
        var readiness = await GetReadinessAsync(part, cancelToken);
        if (!readiness.CanBeReference)
        {
            throw new PublicationException(string.Join(", ", readiness.MissingForReference));
        }

        part.State = PartPublicationState.VerifiedReference;
        part.ReferenceApprovedOnUtc = UtcNow();
        part.ReferenceApprovedBy = approvedBy.Truncate(200);
        part.RejectionReason = null;

        await SyncProductAsync(part, cancelToken);
        return part;
    }

    /// <summary>
    /// B → C. Every sale requirement must hold at the moment of approval.
    /// </summary>
    public async Task<CatalogPart> ApproveSaleAsync(int partId, string approvedBy, CancellationToken cancelToken = default)
    {
        var part = await GetPartAsync(partId, cancelToken);
        if (part.State != PartPublicationState.VerifiedReference && part.State != PartPublicationState.ReadyForSale)
        {
            throw new PublicationException("ReferenceFirst");
        }

        var readiness = await GetReadinessAsync(part, cancelToken);
        if (!readiness.CanBeSold)
        {
            throw new PublicationException(string.Join(", ", readiness.MissingForReference.Concat(readiness.MissingForSale)));
        }

        part.State = PartPublicationState.ReadyForSale;
        part.SaleApprovedOnUtc = UtcNow();
        part.SaleApprovedBy = approvedBy.Truncate(200);

        await SyncProductAsync(part, cancelToken);
        return part;
    }

    /// <summary>
    /// B/C → A. The product is unpublished; research data stays.
    /// </summary>
    public async Task<CatalogPart> WithdrawAsync(int partId, CancellationToken cancelToken = default)
    {
        var part = await GetPartAsync(partId, cancelToken);
        part.State = PartPublicationState.ResearchCandidate;
        part.SaleApprovedOnUtc = null;
        part.SaleApprovedBy = null;
        await SyncProductAsync(part, cancelToken);
        return part;
    }

    public async Task<CatalogPart> RejectAsync(int partId, string reason, CancellationToken cancelToken = default)
    {
        var part = await GetPartAsync(partId, cancelToken);
        part.State = PartPublicationState.Rejected;
        part.RejectionReason = reason.Truncate(1000);
        part.SaleApprovedOnUtc = null;
        part.SaleApprovedBy = null;
        await SyncProductAsync(part, cancelToken);
        return part;
    }

    /// <summary>
    /// Saves the commercial data an admin entered. A part for sale that no longer meets the sale requirements
    /// goes back to B.
    /// </summary>
    public async Task<CatalogPart> UpdateCommerceAsync(
        int partId,
        decimal? approvedPrice,
        int? taxCategoryId,
        int? categoryId,
        bool imageRightsApproved,
        string imageRightsNote,
        CancellationToken cancelToken = default)
    {
        if (approvedPrice is decimal price && price <= 0)
        {
            throw new PublicationException("ApprovedPrice");
        }

        if (imageRightsApproved && string.IsNullOrWhiteSpace(imageRightsNote))
        {
            // Approval without a record of who granted the rights is not an approval.
            throw new PublicationException("ImageRightsNote");
        }

        var part = await GetPartAsync(partId, cancelToken);
        part.ApprovedPrice = approvedPrice;
        part.TaxCategoryId = taxCategoryId > 0 ? taxCategoryId : null;
        part.CategoryId = categoryId > 0 ? categoryId : null;
        part.ImageRightsApproved = imageRightsApproved;
        part.ImageRightsNote = imageRightsNote?.Trim().Truncate(1000);
        part.UpdatedOnUtc = UtcNow();

        await _db.SaveChangesAsync(cancelToken);
        await RefreshAsync([part.Id], cancelToken);
        return part;
    }

    /// <summary>
    /// Re-checks parts after their data, offers or matches changed: parts for sale that lost a requirement
    /// go back to B, stock follows the offers, fitment mappings follow approved matches.
    /// </summary>
    public async Task RefreshAsync(IEnumerable<int> partIds, CancellationToken cancelToken = default)
    {
        var ids = partIds.Distinct().ToList();
        var parts = await _db.CatalogParts().Where(x => ids.Contains(x.Id)).ToListAsync(cancelToken);

        foreach (var part in parts)
        {
            if (part.State == PartPublicationState.ReadyForSale && !(await GetReadinessAsync(part, cancelToken)).CanBeSold)
            {
                part.State = PartPublicationState.VerifiedReference;
                part.SaleApprovedOnUtc = null;
                part.SaleApprovedBy = null;
            }

            if (part.ProductId.HasValue || part.State >= PartPublicationState.VerifiedReference)
            {
                try
                {
                    await SyncProductAsync(part, cancelToken);
                }
                catch (PublicationException ex)
                {
                    // E.g. its product was deleted and another product now has the same SKU/MPN/GTIN: the admin decides.
                    Logger.LogWarning("Catalog part {PartId} was not synchronized: {Reason}", part.Id, ex.Message);
                }
            }
        }

        await _db.SaveChangesAsync(cancelToken);
    }

    #endregion

    #region Fitment review

    public async Task ApproveMatchAsync(int partId, int matchId, string reviewedBy, CancellationToken cancelToken = default)
    {
        var match = await GetMatchAsync(partId, matchId, cancelToken);

        if (match.Kind != FitmentMatchKind.Exact)
        {
            throw new PublicationException("PartialMatch");
        }

        if (!await _db.MotorVehicles().AnyAsync(x => x.Id == match.MotorVehicleId, cancelToken))
        {
            throw new PublicationException("UnknownVehicle");
        }

        match.Status = FitmentMatchStatus.Approved;
        match.ReviewedOnUtc = UtcNow();
        match.ReviewedBy = reviewedBy.Truncate(200);
        await _db.SaveChangesAsync(cancelToken);

        await RefreshAsync([match.PartId], cancelToken);
    }

    public async Task RejectMatchAsync(int partId, int matchId, string reviewedBy, CancellationToken cancelToken = default)
    {
        var match = await GetMatchAsync(partId, matchId, cancelToken);

        match.Status = FitmentMatchStatus.Rejected;
        match.ReviewedOnUtc = UtcNow();
        match.ReviewedBy = reviewedBy.Truncate(200);
        await _db.SaveChangesAsync(cancelToken);

        await RefreshAsync([match.PartId], cancelToken);
    }

    /// <summary>
    /// A match is only reviewed in the context of its own part: a forged or stale match id from another part's page
    /// must not approve fitment for a different product.
    /// </summary>
    private async Task<CatalogFitmentMatch> GetMatchAsync(int partId, int matchId, CancellationToken cancelToken)
        => await _db.CatalogFitmentMatches().FirstOrDefaultAsync(x => x.Id == matchId && x.PartId == partId, cancelToken)
            ?? throw new PublicationException("UnknownMatch");

    /// <summary>
    /// Makes ProductMotorVehicle follow the approved matches. Mappings someone created by hand are never touched;
    /// only mappings a match created are removed again.
    /// </summary>
    private async Task SyncMappingsAsync(CatalogPart part, CancellationToken cancelToken)
    {
        var matches = await _db.CatalogFitmentMatches().Where(x => x.PartId == part.Id).ToListAsync(cancelToken);
        var productId = part.ProductId;

        // Only a product a customer can see gets compatibility claims.
        var approvedVehicleIds = productId.HasValue && part.State >= PartPublicationState.VerifiedReference
            ? matches.Where(x => x.Status == FitmentMatchStatus.Approved && x.Kind == FitmentMatchKind.Exact).Select(x => x.MotorVehicleId).ToHashSet()
            : [];

        var mapped = productId.HasValue
            ? (await _db.ProductMotorVehicles().Where(x => x.ProductId == productId).ToListAsync(cancelToken)).ToDictionary(x => x.MotorVehicleId)
            : [];

        foreach (var match in matches)
        {
            var wanted = approvedVehicleIds.Contains(match.MotorVehicleId);

            if (wanted && !mapped.ContainsKey(match.MotorVehicleId))
            {
                var mapping = new ProductMotorVehicle { ProductId = productId.Value, MotorVehicleId = match.MotorVehicleId };
                _db.ProductMotorVehicles().Add(mapping);
                mapped[match.MotorVehicleId] = mapping;
                match.CreatedMapping = true;
            }
            else if (!wanted && match.CreatedMapping)
            {
                if (mapped.Remove(match.MotorVehicleId, out var mapping))
                {
                    _db.ProductMotorVehicles().Remove(mapping);
                }

                match.CreatedMapping = false;
            }
        }
    }

    #endregion

    #region Product

    /// <summary>
    /// Products that already carry the SKU, MPN or GTIN a new product for this part would get. Creating another one
    /// would put a twin in the shop; an admin has to decide (nothing is merged automatically).
    /// Empty when the part already has its product.
    /// </summary>
    public async Task<List<ProductConflict>> FindConflictingProductsAsync(CatalogPart part, CancellationToken cancelToken = default)
    {
        Guard.NotNull(part);

        if (part.ProductId.HasValue)
        {
            return [];
        }

        var sku = ProductSku(part);
        var mpn = part.Mpn.NullEmpty();
        var gtin = part.Gtin.NullEmpty();

        return await _db.Products.AsNoTracking()
            .Where(x => x.Sku == sku || (mpn != null && x.ManufacturerPartNumber == mpn) || (gtin != null && x.Gtin == gtin))
            .OrderBy(x => x.Id)
            .Take(20)
            .Select(x => new ProductConflict(x.Id, x.Name, x.Sku, x.ManufacturerPartNumber, x.Gtin))
            .ToListAsync(cancelToken);
    }

    public static string ProductSku(CatalogPart part)
        => $"{part.BrandKey}-{part.MpnKey}".ToUpperInvariant().Truncate(400);

    /// <summary>
    /// Creates or updates the Smartstore product of a part according to its state, in one database transaction:
    /// a failure leaves neither a half-written product nor a product the part does not know about.
    /// </summary>
    /// <exception cref="PublicationException">NoProductConflict: another product has the same SKU, MPN or GTIN.</exception>
    public async Task SyncProductAsync(CatalogPart part, CancellationToken cancelToken = default)
    {
        Guard.NotNull(part);

        if (part.ProductId == null && part.State >= PartPublicationState.VerifiedReference
            && (await FindConflictingProductsAsync(part, cancelToken)).Count > 0)
        {
            throw new PublicationException("NoProductConflict");
        }

        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction != null)
        {
            await SyncProductCoreAsync(part, cancelToken);
            return;
        }

        var tx = await _db.Database.BeginTransactionAsync(cancelToken);
        try
        {
            await SyncProductCoreAsync(part, cancelToken);
            await tx.CommitAsync(cancelToken);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);

            // Tracked entities still hold the rolled back values and keys (e.g. a product id that no longer exists).
            _db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            await tx.DisposeAsync();
        }
    }

    private async Task SyncProductCoreAsync(CatalogPart part, CancellationToken cancelToken)
    {
        Product product = null;
        if (part.ProductId.HasValue)
        {
            product = await _db.Products.FirstOrDefaultAsync(x => x.Id == part.ProductId, cancelToken);
        }

        if (product == null && part.State < PartPublicationState.VerifiedReference)
        {
            // A research candidate or a rejected record never becomes a product.
            part.ProductId = null;
            await _db.SaveChangesAsync(cancelToken);
            await SyncMappingsAsync(part, cancelToken);
            await _db.SaveChangesAsync(cancelToken);
            return;
        }

        var now = UtcNow();

        if (product == null)
        {
            product = new Product
            {
                ProductType = ProductType.SimpleProduct,
                Visibility = ProductVisibility.Full,
                ProductTemplateId = await _db.ProductTemplates.OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstOrDefaultAsync(cancelToken),
                IsShippingEnabled = true,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 100,
                CreatedOnUtc = now,
                Published = false
            };

            ApplyIdentity(product, part, now);
            SetNotPurchasable(product);
            _db.Products.Add(product);
            await _db.SaveChangesAsync(cancelToken);

            // Link before anything is published: the part owns its product from the first moment, so a later
            // failure can never leave a product the part does not know about (and a retry would not create a twin).
            part.ProductId = product.Id;
            await _db.SaveChangesAsync(cancelToken);
            await SaveSlugAsync(product, product.Name);
        }

        ApplyIdentity(product, part, now);

        switch (part.State)
        {
            case PartPublicationState.ReadyForSale:
                var stock = await _db.CatalogSupplierOffers()
                    .Where(x => x.PartId == part.Id && x.IsActive && x.StockQuantity > 0)
                    .Join(_db.CatalogSources().Where(s => s.PermissionStatus == SourcePermissionStatus.Authorized), o => o.SourceId, s => s.Id, (o, s) => o.StockQuantity.Value)
                    .SumAsync(cancelToken);

                product.Published = true;
                product.Price = part.ApprovedPrice.Value;
                product.TaxCategoryId = part.TaxCategoryId.Value;
                product.CallForPrice = false;
                product.DisableBuyButton = false;
                product.DisableWishlistButton = false;
                product.ManageInventoryMethod = ManageInventoryMethod.ManageStock;
                product.StockQuantity = stock;
                product.BackorderMode = BackorderMode.NoBackorders;
                product.DisplayStockAvailability = true;
                break;

            case PartPublicationState.VerifiedReference:
                product.Published = true;
                SetNotPurchasable(product);
                break;

            default:
                product.Published = false;
                SetNotPurchasable(product);
                break;
        }

        await _db.SaveChangesAsync(cancelToken);

        await SyncManufacturerAsync(part, product, cancelToken);
        await SyncCategoryAsync(part, product, cancelToken);
        await SyncMappingsAsync(part, cancelToken);
        await _db.SaveChangesAsync(cancelToken);
    }

    /// <summary>
    /// Identity and technical facts.
    /// </summary>
    private static void ApplyIdentity(Product product, CatalogPart part, DateTime now)
    {
        product.Name = part.ProductName;
        product.ManufacturerPartNumber = part.Mpn;
        product.Gtin = part.Gtin;
        product.Sku = ProductSku(part);
        product.Condition = ProductCondition.New;
        product.UpdatedOnUtc = now;
    }

    /// <summary>
    /// No price, no cart, no stock claim. The price field is 0 but hidden by CallForPrice.
    /// </summary>
    public static void SetNotPurchasable(Product product)
    {
        product.Price = 0;
        product.CallForPrice = true;
        product.DisableBuyButton = true;
        product.DisableWishlistButton = true;
        product.ManageInventoryMethod = ManageInventoryMethod.DontManageStock;
        product.DisplayStockAvailability = false;
        product.DisplayStockQuantity = false;
    }

    private async Task SyncManufacturerAsync(CatalogPart part, Product product, CancellationToken cancelToken)
    {
        var manufacturerId = await _db.Manufacturers
            .Where(x => x.Name == part.Brand && !x.Deleted)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancelToken);

        if (manufacturerId == null)
        {
            // Unpublished until an admin publishes the brand page.
            var manufacturer = new Manufacturer
            {
                Name = part.Brand,
                Published = false,
                CreatedOnUtc = UtcNow(),
                UpdatedOnUtc = UtcNow()
            };
            _db.Manufacturers.Add(manufacturer);
            await _db.SaveChangesAsync(cancelToken);
            await SaveSlugAsync(manufacturer, manufacturer.Name);
            manufacturerId = manufacturer.Id;
        }

        if (!await _db.ProductManufacturers.AnyAsync(x => x.ProductId == product.Id && x.ManufacturerId == manufacturerId, cancelToken))
        {
            _db.ProductManufacturers.Add(new ProductManufacturer { ProductId = product.Id, ManufacturerId = manufacturerId.Value });
        }
    }

    private async Task SyncCategoryAsync(CatalogPart part, Product product, CancellationToken cancelToken)
    {
        if (part.CategoryId is not int categoryId
            || !await _db.Categories.AnyAsync(x => x.Id == categoryId, cancelToken)
            || await _db.ProductCategories.AnyAsync(x => x.ProductId == product.Id && x.CategoryId == categoryId, cancelToken))
        {
            return;
        }

        _db.ProductCategories.Add(new ProductCategory { ProductId = product.Id, CategoryId = categoryId });
    }

    #endregion

    protected virtual async Task SaveSlugAsync<T>(T entity, string name) where T : ISlugSupported
        => await _urlService.SaveSlugAsync(entity, null, name, true);

    private async Task<CatalogPart> GetPartAsync(int partId, CancellationToken cancelToken)
        => await _db.CatalogParts().FirstOrDefaultAsync(x => x.Id == partId, cancelToken)
            ?? throw new PublicationException("UnknownPart");
}
