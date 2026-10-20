using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Data;
using Smartstore.Data.Hooks;
using EState = Smartstore.Data.EntityState;

namespace MotorShop.CatalogImport.Hooks;

/// <summary>
/// Defense in depth: a product created from a catalog part that is not approved for sale cannot get its buy button
/// back by an edit in the product admin or by an import. The part's state decides, not the product form.
/// </summary>
[Important]
internal class ProductSaleGuardHook : AsyncDbSaveHook<Product>
{
    private readonly SmartDbContext _db;

    public ProductSaleGuardHook(SmartDbContext db)
    {
        _db = db;
    }

    public override async Task<HookResult> OnBeforeSaveAsync(IHookedEntity entry, CancellationToken cancelToken)
    {
        if (entry.State != EState.Modified || entry.Entity is not Product product || product.Id == 0)
        {
            return HookResult.Ok;
        }

        var buyEnabled = !product.DisableBuyButton && entry.Entry.Property(nameof(Product.DisableBuyButton)).IsModified;
        var priceShown = !product.CallForPrice && entry.Entry.Property(nameof(Product.CallForPrice)).IsModified;

        if ((buyEnabled || priceShown) && await IsBlockedAsync(product.Id, cancelToken))
        {
            product.DisableBuyButton = true;
            product.CallForPrice = true;
        }

        return HookResult.Ok;
    }

    private async Task<bool> IsBlockedAsync(int productId, CancellationToken cancelToken)
    {
        // A part tracked in this unit of work wins over the database: the publication service changes both at once.
        var tracked = _db.CatalogParts().Local.FirstOrDefault(x => x.ProductId == productId);
        if (tracked != null)
        {
            return IsBlocked(tracked.State);
        }

        var state = await _db.CatalogParts()
            .Where(x => x.ProductId == productId)
            .Select(x => (PartPublicationState?)x.State)
            .FirstOrDefaultAsync(cancelToken);

        return state.HasValue && IsBlocked(state.Value);
    }

    public static bool IsBlocked(PartPublicationState state)
        => state != PartPublicationState.ReadyForSale;
}
