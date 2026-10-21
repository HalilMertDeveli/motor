using System.ComponentModel.DataAnnotations;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Models;

public class FieldUpdateResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string Error { get; set; }

    /// <summary>
    /// Formatted new value for the cell.
    /// </summary>
    public string Display { get; set; }

    /// <summary>
    /// Raw new value for the next optimistic check (invariant culture).
    /// </summary>
    public string Value { get; set; }
}

public enum BulkAction
{
    PricePercent,
    PriceFixed,
    SetStock,
    TrackStock,
    UntrackStock,
    HidePrice,
    ShowPrice,
    Publish,
    Unpublish,
    AddCategory,
    RemoveCategory,
    Delete
}

public class BulkRequest
{
    public BulkAction Action { get; set; }
    public string Value { get; set; }
    public int CategoryId { get; set; }

    /// <summary>
    /// Selected product ids. Ignored when <see cref="AllMatching"/> is set.
    /// </summary>
    public int[] Ids { get; set; } = [];

    /// <summary>
    /// Apply to every product matching <see cref="Filter"/> instead of the selection.
    /// </summary>
    public bool AllMatching { get; set; }
    public ProductFilter Filter { get; set; }
}

public class BulkFailure
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Reason { get; set; }
}

public class BulkResult
{
    public int Requested { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<BulkFailure> Failures { get; set; } = [];
    public string Error { get; set; }
}

public class ProductEditorModel
{
    public int Id { get; set; }

    /// <summary>
    /// Optimistic concurrency: <c>UpdatedOnUtc.Ticks</c> when the editor was opened.
    /// </summary>
    public long Version { get; set; }

    [Required, StringLength(400)]
    public string Name { get; set; }

    [StringLength(400)]
    public string Sku { get; set; }

    [StringLength(400)]
    public string ManufacturerPartNumber { get; set; }

    [StringLength(400)]
    public string Gtin { get; set; }

    [StringLength(4000)]
    public string ShortDescription { get; set; }
    public string FullDescription { get; set; }

    [StringLength(400)]
    public string MetaTitle { get; set; }

    [StringLength(4000)]
    public string MetaDescription { get; set; }

    [StringLength(400)]
    public string MetaKeywords { get; set; }

    [StringLength(400)]
    public string SeName { get; set; }

    // Amounts arrive as text so Turkish notation can be parsed on the server.
    public string Price { get; set; }
    public string ComparePrice { get; set; }
    public string SpecialPrice { get; set; }
    public DateTime? SpecialPriceStart { get; set; }
    public DateTime? SpecialPriceEnd { get; set; }
    public bool CallForPrice { get; set; }
    public string TaxCategoryName { get; set; }

    public ManageInventoryMethod ManageInventoryMethod { get; set; }
    public int StockQuantity { get; set; }
    public int MinStockQuantity { get; set; }
    public LowStockActivity LowStockActivity { get; set; }
    public int OrderMinimumQuantity { get; set; } = 1;
    public int OrderMaximumQuantity { get; set; } = 10000;

    public int[] CategoryIds { get; set; } = [];
    public string Tags { get; set; }

    public bool IsShippingEnabled { get; set; }
    public bool IsFreeShipping { get; set; }
    public string Weight { get; set; }
    public string Length { get; set; }
    public string Width { get; set; }
    public string Height { get; set; }

    public bool Published { get; set; }
    public ProductVisibility Visibility { get; set; }

    // Display only.
    public ProductType ProductType { get; set; }
    public string CurrencyCode { get; set; }
    public List<string> PictureUrls { get; set; } = [];
    public List<CategoryOption> AllCategories { get; set; } = [];
    public string Slug { get; set; }
}
