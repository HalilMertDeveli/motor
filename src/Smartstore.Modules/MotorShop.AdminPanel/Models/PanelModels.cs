using Microsoft.AspNetCore.Routing;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Common;

namespace MotorShop.AdminPanel.Models;

public class PanelHeaderModel
{
    public string StoreName { get; set; }
    public string Title { get; set; }
    public string LogoUrl { get; set; }
    public string AccentColor { get; set; }
    public string UserName { get; set; }
    public string ActiveTab { get; set; }
    public List<PanelTab> Tabs { get; set; } = [];
}

/// <param name="Classic">Opens a page of the classic Smartstore admin.</param>
public record PanelTab(string Key, string Title, string Url, bool Classic);

public class DashboardModel
{
    public bool CanReadProducts { get; set; }
    public bool CanReadOrders { get; set; }

    public CatalogSummary Summary { get; set; }
    public PriceDisplayState PriceDisplay { get; set; }
    public List<RecentOrderModel> RecentOrders { get; set; } = [];
    public CurrencyStripModel Currency { get; set; }
    public ProductListModel Products { get; set; }
}

/// <summary>
/// Counting rules (see docs/motor/admin-panel.md):
/// all counts exclude deleted products and system products; unpublished products are included.
/// </summary>
public class CatalogSummary
{
    public int TotalProducts { get; set; }
    public int PublishedProducts { get; set; }

    /// <summary>
    /// Stock managed per product with quantity &lt;= 0, or managed per attribute combination
    /// without any active combination in stock.
    /// </summary>
    public int OutOfStock { get; set; }

    /// <summary>
    /// Products with <c>CallForPrice</c> set. Independent of the published state.
    /// </summary>
    public int CallForPrice { get; set; }
}

/// <summary>
/// Read-only view of Smartstore's native "Display prices" permission for the guest and registered roles.
/// </summary>
public class PriceDisplayState
{
    public bool GuestsSeePrices { get; set; }
    public bool RegisteredSeePrices { get; set; }
}

public class RecentOrderModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; }
    public string Customer { get; set; }
    public DateTime CreatedOn { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public string OrderStatusText { get; set; }
    public string PaymentMethod { get; set; }
    public Money Total { get; set; }
}

public class CurrencyStripModel
{
    public string PrimaryCode { get; set; }
    public string ExchangeCode { get; set; }
    public List<CurrencyRateModel> Rates { get; set; } = [];
}

public class CurrencyRateModel
{
    public string Code { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// Amount of the primary exchange currency for one unit of <see cref="Code"/>.
    /// </summary>
    public decimal PrimaryPerUnit { get; set; }
    public DateTime UpdatedOn { get; set; }
}

public enum StockFilter
{
    All,
    InStock,
    OutOfStock,
    NotTracked
}

public enum PriceVisibilityFilter
{
    All,
    Visible,
    CallForPrice,
    ZeroPrice
}

public enum PublishFilter
{
    All,
    Published,
    Unpublished
}

public enum ProductSort
{
    Newest,
    NameAsc,
    NameDesc,
    PriceAsc,
    PriceDesc,
    StockAsc,
    StockDesc
}

public class ProductFilter
{
    public const int DefaultPageSize = 25;
    public static readonly int[] PageSizes = [25, 50, 100];

    public string Q { get; set; }
    public int CategoryId { get; set; }
    public StockFilter Stock { get; set; }
    public PriceVisibilityFilter Price { get; set; }
    public PublishFilter Status { get; set; }
    public ProductSort Sort { get; set; }
    public int Page { get; set; } = 1;
    public int Size { get; set; } = DefaultPageSize;

    /// <summary>
    /// Query values for links, without defaults so deep links stay short.
    /// </summary>
    public RouteValueDictionary ToRouteValues(int? page = null, ProductSort? sort = null)
    {
        var values = new RouteValueDictionary { ["area"] = "Admin" };
        var p = page ?? Page;
        var s = sort ?? Sort;

        if (Q.HasValue()) values["q"] = Q;
        if (CategoryId > 0) values["categoryId"] = CategoryId;
        if (Stock != StockFilter.All) values["stock"] = Stock.ToString();
        if (Price != PriceVisibilityFilter.All) values["price"] = Price.ToString();
        if (Status != PublishFilter.All) values["status"] = Status.ToString();
        if (s != ProductSort.Newest) values["sort"] = s.ToString();
        if (Size != DefaultPageSize) values["size"] = Size;
        if (p > 1) values["page"] = p;

        return values;
    }

    public void Normalize()
    {
        Q = Q?.Trim();
        if (Q?.Length > 200)
        {
            Q = Q[..200];
        }

        Page = Math.Max(Page, 1);
        Size = PageSizes.Contains(Size) ? Size : DefaultPageSize;
        CategoryId = Math.Max(CategoryId, 0);
    }
}

public class ProductListModel
{
    public ProductFilter Filter { get; set; }
    public List<ProductRowModel> Rows { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages => Filter.Size > 0 ? (int)Math.Ceiling((double)TotalCount / Filter.Size) : 0;
    public string CurrencyCode { get; set; }
    public List<CategoryOption> Categories { get; set; } = [];
}

public class CategoryOption
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Depth { get; set; }
}

public enum StockState
{
    InStock,
    OutOfStock,
    NotTracked,
    ByAttributes
}

public class ProductRowModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string ThumbnailUrl { get; set; }
    public List<string> Categories { get; set; } = [];
    public Money Price { get; set; }
    public Money? SpecialPrice { get; set; }
    public bool SpecialPriceActive { get; set; }
    public int StockQuantity { get; set; }
    public StockState Stock { get; set; }
    public bool CallForPrice { get; set; }
    public bool Published { get; set; }
    public string Slug { get; set; }
}
