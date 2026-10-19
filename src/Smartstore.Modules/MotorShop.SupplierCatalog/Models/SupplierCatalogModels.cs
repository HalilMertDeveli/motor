using MotorShop.SupplierCatalog.Services;

namespace MotorShop.SupplierCatalog.Models;

[LocalizedDisplay("Plugins.MotorShop.SupplierCatalog.")]
public class SupplierCatalogImportModel : ModelBase
{
    [LocalizedDisplay("*PublishUnpricedProducts")]
    public bool PublishUnpricedProducts { get; set; }

    [LocalizedDisplay("*UpdateExistingProducts")]
    public bool UpdateExistingProducts { get; set; } = true;

    public SupplierCatalogImportResult LastResult { get; set; }
}
