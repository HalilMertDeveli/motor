using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartstore.Core.Catalog.Products;
using Smartstore.Domain;

namespace MotorShop.Fitment.Domain;

/// <remarks>
/// The relationships must be part of the EF model: on app installation the schema is created from the model
/// (RelationalDatabaseCreator.CreateTables) and all migrations, including this module's, are only marked as applied.
/// Constraint names match the migrations, so the existence checks in later migrations find them.
/// </remarks>
internal class ProductMotorVehicleMap : IEntityTypeConfiguration<ProductMotorVehicle>
{
    public const string ProductForeignKey = "FK_ProductMotorVehicle_Product";
    public const string VehicleForeignKey = "FK_ProductMotorVehicle_MotorVehicle";

    public void Configure(EntityTypeBuilder<ProductMotorVehicle> builder)
    {
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName(ProductForeignKey)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MotorVehicle>()
            .WithMany()
            .HasForeignKey(x => x.MotorVehicleId)
            .HasConstraintName(VehicleForeignKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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
