using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Engine.Modularity;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Read-only queries of the panel. Everything is projected into small view models; nothing is written.
/// </summary>
public class PanelQueryService
{
    const int ThumbnailSize = 64;

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly ICatalogSearchService _catalogSearchService;
    private readonly ICategoryService _categoryService;
    private readonly IMediaService _mediaService;
    private readonly IUrlService _urlService;
    private readonly IPaymentService _paymentService;
    private readonly ModuleManager _moduleManager;

    public PanelQueryService(
        SmartDbContext db,
        ICommonServices services,
        ICatalogSearchService catalogSearchService,
        ICategoryService categoryService,
        IMediaService mediaService,
        IUrlService urlService,
        IPaymentService paymentService,
        ModuleManager moduleManager)
    {
        _db = db;
        _services = services;
        _catalogSearchService = catalogSearchService;
        _categoryService = categoryService;
        _mediaService = mediaService;
        _urlService = urlService;
        _paymentService = paymentService;
        _moduleManager = moduleManager;
    }

    public async Task<CatalogSummary> GetCatalogSummaryAsync()
    {
        var products = CountableProducts(_db.Products.AsNoTracking());

        return new CatalogSummary
        {
            TotalProducts = await products.CountAsync(),
            PublishedProducts = await products.CountAsync(x => x.Published),
            OutOfStock = await ApplyStockFilter(products, StockFilter.OutOfStock, _db.ProductVariantAttributeCombinations).CountAsync(),
            CallForPrice = await products.CountAsync(x => x.CallForPrice)
        };
    }

    public async Task<PriceDisplayState> GetPriceDisplayStateAsync()
    {
        var guests = await _db.CustomerRoles.AsNoTracking().FirstOrDefaultAsync(x => x.SystemName == SystemCustomerRoleNames.Guests);
        var registered = await _db.CustomerRoles.AsNoTracking().FirstOrDefaultAsync(x => x.SystemName == SystemCustomerRoleNames.Registered);

        return new PriceDisplayState
        {
            GuestsSeePrices = await IsGrantedAsync(guests, Permissions.Catalog.DisplayPrice),
            RegisteredSeePrices = await IsGrantedAsync(registered, Permissions.Catalog.DisplayPrice)
        };
    }

    private async Task<bool> IsGrantedAsync(CustomerRole role, string permission)
    {
        if (role == null || !role.Active)
        {
            return false;
        }

        var tree = await _services.Permissions.GetPermissionTreeAsync(role);
        var node = tree.Permissions.FlattenNodes(false).FirstOrDefault(x => x.Value.SystemName == permission);

        // An unset permission inherits the value of its parent.
        for (var current = node; current != null; current = current.Parent)
        {
            if (current.Value?.Allow is bool allow)
            {
                return allow;
            }
        }

        return false;
    }

    public async Task<List<RecentOrderModel>> GetRecentOrdersAsync(int take = 5)
    {
        var customer = _services.WorkContext.CurrentCustomer;
        var authorizedStoreIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);
        var primaryCurrency = _services.CurrencyService.PrimaryCurrency;

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => !x.Deleted)
            .ApplyCustomerFilter(authorizedStoreIds)
            .OrderByDescending(x => x.CreatedOnUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.OrderNumber,
                x.CreatedOnUtc,
                x.OrderStatusId,
                x.PaymentMethodSystemName,
                x.OrderTotal,
                FirstName = x.BillingAddress.FirstName,
                LastName = x.BillingAddress.LastName,
                Company = x.BillingAddress.Company,
                x.Customer.Email
            })
            .ToListAsync();

        var paymentNames = await GetPaymentMethodNamesAsync(orders.Select(x => x.PaymentMethodSystemName));

        return orders.Select(x =>
        {
            var status = (Smartstore.Core.Checkout.Orders.OrderStatus)x.OrderStatusId;
            var name = $"{x.FirstName} {x.LastName}".Trim().NullEmpty() ?? x.Company.NullEmpty() ?? x.Email.NullEmpty() ?? "-";

            return new RecentOrderModel
            {
                Id = x.Id,
                OrderNumber = x.OrderNumber.NullEmpty() ?? x.Id.ToStringInvariant(),
                Customer = name,
                CreatedOn = _services.DateTimeHelper.ConvertToUserTime(x.CreatedOnUtc, DateTimeKind.Utc),
                OrderStatus = status,
                OrderStatusText = _services.Localization.GetLocalizedEnum(status),
                PaymentMethod = x.PaymentMethodSystemName.HasValue()
                    ? paymentNames.GetValueOrDefault(x.PaymentMethodSystemName) ?? x.PaymentMethodSystemName
                    : "-",
                Total = _services.CurrencyService.CreateMoney(x.OrderTotal, primaryCurrency)
            };
        })
        .ToList();
    }

    /// <summary>
    /// Configured rates only. Returns no rates when the store has a single published currency;
    /// rates are never fetched from an external provider here.
    /// </summary>
    public async Task<CurrencyStripModel> GetCurrencyStripAsync()
    {
        var primary = _services.CurrencyService.PrimaryCurrency;
        var exchange = _services.CurrencyService.PrimaryExchangeCurrency;

        var others = await _db.Currencies
            .AsNoTracking()
            .Where(x => x.Published && x.Id != exchange.Id && x.Rate > 0)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        return new CurrencyStripModel
        {
            PrimaryCode = primary.CurrencyCode,
            ExchangeCode = exchange.CurrencyCode,
            Rates = [.. others.Select(x => new CurrencyRateModel
            {
                Code = x.CurrencyCode,
                Name = x.GetLocalized(y => y.Name),
                // Currency.Rate is "units of this currency per one exchange currency unit".
                PrimaryPerUnit = Math.Round(1m / x.Rate, 4),
                UpdatedOn = _services.DateTimeHelper.ConvertToUserTime(x.UpdatedOnUtc, DateTimeKind.Utc)
            })]
        };
    }

    public async Task<ProductListModel> GetProductsAsync(ProductFilter filter)
    {
        Guard.NotNull(filter);
        filter.Normalize();

        var query = BuildProductQuery(filter);
        var total = await query.CountAsync();

        var lastPage = Math.Max(1, (int)Math.Ceiling((double)total / filter.Size));
        filter.Page = Math.Min(filter.Page, lastPage);

        var rows = await ApplySorting(query, filter.Sort)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Sku,
                x.MainPictureId,
                x.Price,
                x.SpecialPrice,
                x.SpecialPriceStartDateTimeUtc,
                x.SpecialPriceEndDateTimeUtc,
                x.StockQuantity,
                x.ManageInventoryMethodId,
                x.CallForPrice,
                x.Published
            })
            .ToListAsync();

        var ids = rows.Select(x => x.Id).ToArray();
        var currency = _services.CurrencyService.PrimaryCurrency;
        var now = DateTime.UtcNow;

        var categories = ids.Length == 0 ? [] : await _db.ProductCategories
            .AsNoTracking()
            .Where(x => ids.Contains(x.ProductId) && !x.Category.Deleted)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new { x.ProductId, x.Category.Name })
            .ToListAsync();

        var categoryMap = categories.ToMultimap(x => x.ProductId, x => x.Name);

        var pictureIds = rows.Where(x => x.MainPictureId > 0).Select(x => x.MainPictureId.Value).Distinct().ToArray();
        var pictures = pictureIds.Length == 0
            ? []
            : (await _mediaService.GetFilesByIdsAsync(pictureIds)).ToDictionarySafe(x => x.Id);

        await _urlService.PrefetchUrlRecordsAsync(nameof(Product), [0], ids);

        var model = new ProductListModel
        {
            Filter = filter,
            TotalCount = total,
            CurrencyCode = currency.CurrencyCode
        };

        foreach (var x in rows)
        {
            var method = (ManageInventoryMethod)x.ManageInventoryMethodId;
            var slug = x.Published ? await _urlService.GetActiveSlugAsync(x.Id, nameof(Product), 0) : null;

            model.Rows.Add(new ProductRowModel
            {
                Id = x.Id,
                Name = x.Name,
                Sku = x.Sku,
                ThumbnailUrl = x.MainPictureId > 0 && pictures.TryGetValue(x.MainPictureId.Value, out var file)
                    ? _mediaService.GetUrl(file, ThumbnailSize)
                    : null,
                Categories = categoryMap.ContainsKey(x.Id) ? [.. categoryMap[x.Id]] : [],
                Price = _services.CurrencyService.CreateMoney(x.Price, currency),
                SpecialPrice = x.SpecialPrice.HasValue ? _services.CurrencyService.CreateMoney(x.SpecialPrice.Value, currency) : null,
                SpecialPriceActive = x.SpecialPrice.HasValue
                    && (x.SpecialPriceStartDateTimeUtc == null || x.SpecialPriceStartDateTimeUtc <= now)
                    && (x.SpecialPriceEndDateTimeUtc == null || x.SpecialPriceEndDateTimeUtc >= now),
                StockQuantity = x.StockQuantity,
                Stock = method switch
                {
                    ManageInventoryMethod.DontManageStock => StockState.NotTracked,
                    ManageInventoryMethod.ManageStockByAttributes => StockState.ByAttributes,
                    _ => x.StockQuantity > 0 ? StockState.InStock : StockState.OutOfStock
                },
                CallForPrice = x.CallForPrice,
                Published = x.Published,
                Slug = slug.NullEmpty()
            });
        }

        return model;
    }

    public async Task<List<CategoryOption>> GetCategoryOptionsAsync()
    {
        var tree = await _categoryService.GetCategoryTreeAsync(0, true);
        var result = new List<CategoryOption>();

        foreach (var node in tree.FlattenNodes(false))
        {
            result.Add(new CategoryOption
            {
                Id = node.Value.Id,
                Name = node.Value.Name,
                Depth = node.Depth - 1
            });
        }

        return result;
    }

    internal IQueryable<Product> BuildProductQuery(ProductFilter filter)
    {
        var searchQuery = new CatalogSearchQuery(["name", "sku"], filter.Q);
        searchQuery.ParseSearchTerm = true;

        if (filter.CategoryId > 0)
        {
            searchQuery = searchQuery.WithCategoryIds(null, filter.CategoryId);
        }

        if (filter.Status != PublishFilter.All)
        {
            searchQuery = searchQuery.PublishedOnly(filter.Status == PublishFilter.Published);
        }

        var query = _catalogSearchService.PrepareQuery(searchQuery, CountableProducts(_db.Products.AsNoTracking()));

        query = ApplyPriceFilter(query, filter.Price);

        return ApplyStockFilter(query, filter.Stock, _db.ProductVariantAttributeCombinations);
    }

    internal static IQueryable<Product> ApplyPriceFilter(IQueryable<Product> query, PriceVisibilityFilter filter)
    {
        return filter switch
        {
            PriceVisibilityFilter.Visible => query.Where(x => !x.CallForPrice),
            PriceVisibilityFilter.CallForPrice => query.Where(x => x.CallForPrice),
            // Shown with a price of 0: purchasable for nothing once published.
            PriceVisibilityFilter.ZeroPrice => query.Where(x => !x.CallForPrice && x.Price <= 0),
            _ => query
        };
    }

    /// <summary>
    /// The products the panel counts and lists: not deleted, no system products.
    /// </summary>
    internal static IQueryable<Product> CountableProducts(IQueryable<Product> products)
        => products.Where(x => !x.Deleted && !x.IsSystemProduct);

    internal static IQueryable<Product> ApplyStockFilter(IQueryable<Product> query, StockFilter filter, IQueryable<ProductVariantAttributeCombination> combinations)
    {
        const int byProduct = (int)ManageInventoryMethod.ManageStock;
        const int byAttributes = (int)ManageInventoryMethod.ManageStockByAttributes;
        const int notTracked = (int)ManageInventoryMethod.DontManageStock;

        return filter switch
        {
            StockFilter.OutOfStock => query.Where(x =>
                (x.ManageInventoryMethodId == byProduct && x.StockQuantity <= 0) ||
                (x.ManageInventoryMethodId == byAttributes && !combinations.Any(c => c.ProductId == x.Id && c.IsActive && c.StockQuantity > 0))),
            StockFilter.InStock => query.Where(x =>
                (x.ManageInventoryMethodId == byProduct && x.StockQuantity > 0) ||
                (x.ManageInventoryMethodId == byAttributes && combinations.Any(c => c.ProductId == x.Id && c.IsActive && c.StockQuantity > 0))),
            StockFilter.NotTracked => query.Where(x => x.ManageInventoryMethodId == notTracked),
            _ => query
        };
    }

    internal static IQueryable<Product> ApplySorting(IQueryable<Product> query, ProductSort sort)
    {
        return sort switch
        {
            ProductSort.NameAsc => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ProductSort.NameDesc => query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id),
            ProductSort.PriceAsc => query.OrderBy(x => x.Price).ThenBy(x => x.Id),
            ProductSort.PriceDesc => query.OrderByDescending(x => x.Price).ThenByDescending(x => x.Id),
            ProductSort.StockAsc => query.OrderBy(x => x.StockQuantity).ThenBy(x => x.Id),
            ProductSort.StockDesc => query.OrderByDescending(x => x.StockQuantity).ThenByDescending(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id)
        };
    }

    private async Task<Dictionary<string, string>> GetPaymentMethodNamesAsync(IEnumerable<string> systemNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var systemName in systemNames.Where(x => x.HasValue()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provider = await _paymentService.LoadPaymentProviderBySystemNameAsync(systemName);
            if (provider != null)
            {
                result[systemName] = _moduleManager.GetLocalizedFriendlyName(provider.Metadata).NullEmpty() ?? systemName;
            }
        }

        return result;
    }
}
