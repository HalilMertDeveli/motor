using System.ComponentModel.DataAnnotations;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Models;

/// <summary>
/// "Yeni Ürün Ekle": the few fields a store employee fills in. Everything else gets Smartstore defaults.
/// Photos arrive as files in display order (the first one is the main photo).
/// </summary>
public class QuickProductModel
{
    /// <summary>
    /// Random id of the form instance. A second submit with the same id returns the first result instead of a twin.
    /// </summary>
    [StringLength(64)]
    public string SubmissionId { get; set; }

    public string Name { get; set; }
    public int CategoryId { get; set; }

    // Amounts arrive as text so Turkish notation can be parsed on the server.
    public string Price { get; set; }
    public string Stock { get; set; }
    public string ShortDescription { get; set; }
    public string Sku { get; set; }

    /// <summary>
    /// <c>true</c> = "Yayınla", <c>false</c> = "Taslak Kaydet".
    /// </summary>
    public bool Publish { get; set; }
}

/// <summary>
/// The simple product editor. Only these fields are written; everything else on the product stays as it is.
/// </summary>
public class SimpleProductModel
{
    public int Id { get; set; }

    /// <summary>
    /// Optimistic concurrency: <c>UpdatedOnUtc.Ticks</c> when the editor was opened (or after the last photo change).
    /// </summary>
    public long Version { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// The product's main category (first mapping). Other mappings are kept.
    /// </summary>
    public int CategoryId { get; set; }

    public string Price { get; set; }
    public string Stock { get; set; }
    public string ShortDescription { get; set; }
    public bool Published { get; set; }

    /// <summary>
    /// "Fiyat sitede görünsün" (the opposite of call for price). <c>null</c>: not sent, see <c>SimpleProductService.PriceVisibility</c>.
    /// </summary>
    public bool? ShowPrice { get; set; }

    // Display only.
    public string Sku { get; set; }
    public string ManufacturerPartNumber { get; set; }
    public ManageInventoryMethod ManageInventoryMethod { get; set; }
    public bool CallForPrice { get; set; }
    public string SpecialPrice { get; set; }
    public string TaxCategoryName { get; set; }
    public string CurrencyCode { get; set; }
    public string Slug { get; set; }
    public List<string> OtherCategories { get; set; } = [];
    public List<ProductPhotoModel> Photos { get; set; } = [];
    public List<CategoryPickerOption> CategoryOptions { get; set; } = [];
}

public class ProductPhotoModel
{
    public int FileId { get; set; }
    public string ThumbUrl { get; set; }
    public string Name { get; set; }
}

/// <summary>
/// A category for the searchable picker: full path so equal names under different parents stay apart.
/// </summary>
public class CategoryPickerOption
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public bool Published { get; set; }
}

public class SimpleProductResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }

    /// <summary>
    /// Field name to message; "" is a message for the whole form.
    /// </summary>
    public Dictionary<string, string> Errors { get; set; } = [];

    public string Message { get; set; }

    /// <summary>
    /// Saved, but something did not turn out as asked (e.g. the price stays hidden).
    /// </summary>
    public string Warning { get; set; }

    public int ProductId { get; set; }
    public string ProductName { get; set; }

    /// <summary>
    /// Ticks exceed JavaScript's safe integer range, so the version travels as a string.
    /// </summary>
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]
    public long Version { get; set; }
    public bool Published { get; set; }
    public bool CallForPrice { get; set; }

    /// <summary>
    /// Storefront path of the product (also for drafts: an admin sees a preview, customers get a 404).
    /// </summary>
    public string Slug { get; set; }

    public List<ProductPhotoModel> Photos { get; set; }

    public static SimpleProductResult Fail(string field, string message)
        => new() { Errors = { [field ?? string.Empty] = message } };
}

public class QuickCategoryResult
{
    public bool Success { get; set; }
    public string Error { get; set; }
    public bool Created { get; set; }
    public string Message { get; set; }
    public CategoryPickerOption Category { get; set; }
}
