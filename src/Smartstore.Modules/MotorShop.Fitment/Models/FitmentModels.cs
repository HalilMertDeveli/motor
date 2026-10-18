using MotorShop.Fitment.Domain;
using MotorShop.Fitment.Services;
using Smartstore.Core.Catalog.Search;
using Smartstore.Web.Models.Catalog;
using Smartstore.Web.Models.Search;

namespace MotorShop.Fitment.Models;

public class VehiclePickerModel
{
    public List<string> Makes { get; set; } = [];
    public List<string> Models { get; set; } = [];
    public List<int> Years { get; set; } = [];
    public VehicleSelection Selection { get; set; }
}

public class FitmentResultsModel : ISearchResultModel
{
    public VehicleSelection Selection { get; set; }
    public CatalogSearchResult SearchResult { get; set; }
    public ProductSummaryModel Products { get; set; }
}

public class ProductFitmentModel
{
    public List<MotorVehicle> Vehicles { get; set; } = [];
    public VehicleSelection Selection { get; set; }

    public bool Fits(MotorVehicle vehicle)
    {
        if (Selection == null || Selection.IsEmpty)
        {
            return false;
        }

        return vehicle.Make.Equals(Selection.Make, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(Selection.Model) || vehicle.Model.Equals(Selection.Model, StringComparison.OrdinalIgnoreCase))
            && (!Selection.Year.HasValue || (vehicle.YearFrom <= Selection.Year && vehicle.YearTo >= Selection.Year));
    }
}

[LocalizedDisplay("Plugins.MotorShop.Fitment.")]
public class MotorVehicleModel : EntityModelBase
{
    [LocalizedDisplay("*Make")]
    [Required, StringLength(100)]
    public string Make { get; set; }

    [LocalizedDisplay("*Model")]
    [Required, StringLength(200)]
    public string Model { get; set; }

    [LocalizedDisplay("*YearFrom")]
    [Range(1950, 2100)]
    public int YearFrom { get; set; } = DateTime.Now.Year;

    [LocalizedDisplay("*YearTo")]
    [Range(1950, 2100)]
    public int YearTo { get; set; } = DateTime.Now.Year;

    [LocalizedDisplay("*EngineCc")]
    public int? EngineCc { get; set; }
}

public class FitmentConfigurationModel
{
    public List<MotorVehicle> Vehicles { get; set; } = [];
    public Dictionary<int, int> ProductCounts { get; set; } = [];
    public MotorVehicleModel NewVehicle { get; set; } = new();

    [LocalizedDisplay("Plugins.MotorShop.Fitment.BulkImport")]
    public string BulkImport { get; set; }
}

[CustomModelPart]
public class ProductFitmentEditModel : ModelBase
{
    public int ProductId { get; set; }
    public int[] VehicleIds { get; set; } = [];
}
