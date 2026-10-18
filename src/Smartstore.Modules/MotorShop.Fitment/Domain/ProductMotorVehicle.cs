using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace MotorShop.Fitment.Domain;

/// <summary>
/// Maps a product (part) to a motorcycle it fits.
/// </summary>
[Table("ProductMotorVehicle")]
[Index(nameof(ProductId), nameof(MotorVehicleId), IsUnique = true, Name = "IX_ProductMotorVehicle_Product_Vehicle")]
[Index(nameof(MotorVehicleId), Name = "IX_ProductMotorVehicle_Vehicle")]
public class ProductMotorVehicle : BaseEntity
{
    public int ProductId { get; set; }

    public int MotorVehicleId { get; set; }
}
