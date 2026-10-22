#nullable enable

using System.Diagnostics;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Data;
using Smartstore.Data.Hooks;

namespace Smartstore.Core.Catalog.Search;

/// <summary>
/// MOTOR: provides the process-wide <see cref="CatalogTermIndex"/>.
/// </summary>
public interface ICatalogTermIndexProvider
{
    /// <summary>
    /// Gets the current index, building it if necessary.
    /// Returns <c>null</c> if the catalog is too large for in-memory ranking.
    /// </summary>
    Task<CatalogTermIndex?> GetIndexAsync(SmartDbContext db, CancellationToken cancelToken = default);

    /// <summary>
    /// Marks the index as outdated. It is rebuilt on next use.
    /// </summary>
    void Invalidate();
}

public class CatalogTermIndexProvider : ICatalogTermIndexProvider
{
    /// <summary>
    /// Above this many products the database search is used as before.
    /// </summary>
    public const int MaxProducts = 20_000;

    /// <summary>
    /// Safety net for changes that bypass the save hook (raw SQL imports).
    /// </summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private CatalogTermIndex? _index;
    private DateTime _builtOnUtc;
    private int _version;
    private bool _tooLarge;

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public async Task<CatalogTermIndex?> GetIndexAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        Guard.NotNull(db);

        if (IsCurrent())
        {
            return _index;
        }

        await _lock.WaitAsync(cancelToken);
        try
        {
            if (IsCurrent())
            {
                return _index;
            }

            var version = _version;
            var watch = Stopwatch.StartNew();
            var index = await BuildAsync(db, cancelToken);

            _tooLarge = index == null;
            _index = index;
            _builtOnUtc = DateTime.UtcNow;

            if (version != _version)
            {
                // Invalidated while building: serve this one, rebuild next time.
                _builtOnUtc = DateTime.MinValue;
            }

            Logger.Debug("Catalog term index built with {0} products in {1} ms.", index?.Count ?? 0, watch.ElapsedMilliseconds);
            return index;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _builtOnUtc = DateTime.MinValue;
    }

    private bool IsCurrent()
        => (_index != null || _tooLarge) && DateTime.UtcNow - _builtOnUtc < MaxAge;

    protected virtual async Task<CatalogTermIndex?> BuildAsync(SmartDbContext db, CancellationToken cancelToken)
    {
        var productCount = await db.Products.CountAsync(x => !x.IsSystemProduct, cancelToken);
        if (productCount > MaxProducts)
        {
            return null;
        }

        var products = await db.Products
            .AsNoTracking()
            .Where(x => !x.IsSystemProduct)
            .Select(x => new { x.Id, x.Name, x.ShortDescription, x.Sku, x.ManufacturerPartNumber, x.Gtin })
            .ToListAsync(cancelToken);

        var categories = (await db.ProductCategories
            .AsNoTracking()
            .Select(x => new { x.ProductId, x.Category.Name })
            .ToListAsync(cancelToken))
            .ToLookup(x => x.ProductId, x => x.Name);

        var brands = (await db.ProductManufacturers
            .AsNoTracking()
            .Select(x => new { x.ProductId, x.Manufacturer.Name })
            .ToListAsync(cancelToken))
            .ToLookup(x => x.ProductId, x => x.Name);

        var combinationCodes = (await db.ProductVariantAttributeCombinations
            .AsNoTracking()
            .Where(x => x.Sku != null || x.ManufacturerPartNumber != null || x.Gtin != null)
            .Select(x => new { x.ProductId, x.Sku, x.ManufacturerPartNumber, x.Gtin })
            .ToListAsync(cancelToken))
            .ToLookup(x => x.ProductId);

        var tags = (await db.Products
            .AsNoTracking()
            .Where(x => !x.IsSystemProduct)
            .SelectMany(x => x.ProductTags, (p, t) => new { ProductId = p.Id, t.Name })
            .ToListAsync(cancelToken))
            .ToLookup(x => x.ProductId, x => x.Name);

        var documents = products.Select(p => new CatalogTermDocument
        {
            Id = p.Id,
            Name = p.Name,
            ShortDescription = p.ShortDescription,
            Codes = new[] { p.Sku, p.ManufacturerPartNumber, p.Gtin }
                .Concat(combinationCodes[p.Id].SelectMany(c => new[] { c.Sku, c.ManufacturerPartNumber, c.Gtin })),
            Brands = brands[p.Id],
            Categories = categories[p.Id],
            Tags = tags[p.Id]
        });

        return CatalogTermIndex.Build(documents);
    }
}

/// <summary>
/// MOTOR: invalidates the <see cref="ICatalogTermIndexProvider"/> when searchable catalog data changes.
/// </summary>
internal class CatalogTermIndexHook : AsyncDbSaveHook<BaseEntity>
{
    private readonly ICatalogTermIndexProvider _provider;

    public CatalogTermIndexHook(ICatalogTermIndexProvider provider)
    {
        _provider = provider;
    }

    public override Task<HookResult> OnAfterSaveAsync(IHookedEntity entry, CancellationToken cancelToken)
    {
        var entity = entry.Entity;
        if (entity is Product
            or Category
            or Manufacturer
            or ProductCategory
            or ProductManufacturer
            or ProductVariantAttributeCombination
            or ProductTag)
        {
            _provider.Invalidate();
            return Task.FromResult(HookResult.Ok);
        }

        return Task.FromResult(HookResult.Void);
    }
}
