using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MotorShop.SupplierCatalog.Services;

namespace MotorShop.SupplierCatalog.Models;

[LocalizedDisplay("Plugins.MotorShop.SupplierCatalog.")]
public class SupplierCatalogImportModel : ModelBase
{
    [LocalizedDisplay("*SupplierName")]
    [Required, StringLength(100)]
    public string SupplierName { get; set; } = "Motosev";

    [LocalizedDisplay("*TaxCategory")]
    public int? TaxCategoryId { get; set; }

    [LocalizedDisplay("*UpdateExistingProducts")]
    public bool UpdateExistingProducts { get; set; } = true;

    public List<SelectListItem> AvailableTaxCategories { get; set; } = [];

    public SupplierCatalogImportResult LastResult { get; set; }
}
