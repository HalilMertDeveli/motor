using System.Data;
using System.Threading;
using FluentMigrator;
using Microsoft.EntityFrameworkCore;
using MotorShop.Fitment.Domain;
using MotorShop.Fitment.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;
using Smartstore.Domain;

namespace MotorShop.Fitment.Migrations;

[MigrationVersion("2026-10-18 12:00:00", "MotorShop.Fitment: Initial")]
internal class Initial : Migration, IDataSeeder<SmartDbContext>
{
    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        if (!await context.MotorVehicles().AnyAsync(cancelToken))
        {
            context.MotorVehicles().AddRange(VehicleSeedData.GetVehicles());
            await context.SaveChangesAsync(cancelToken);
        }
    }

    const string VehicleTable = "MotorVehicle";
    const string MappingTable = "ProductMotorVehicle";

    public override void Up()
    {
        if (!Schema.Table(VehicleTable).Exists())
        {
            Create.Table(VehicleTable)
                .WithIdColumn()
                .WithColumn(nameof(MotorVehicle.Make)).AsString(100).NotNullable()
                .WithColumn(nameof(MotorVehicle.Model)).AsString(200).NotNullable()
                .WithColumn(nameof(MotorVehicle.YearFrom)).AsInt32().NotNullable()
                .WithColumn(nameof(MotorVehicle.YearTo)).AsInt32().NotNullable()
                .WithColumn(nameof(MotorVehicle.EngineCc)).AsInt32().Nullable();

            Create.Index("IX_MotorVehicle_Make_Model")
                .OnTable(VehicleTable)
                .OnColumn(nameof(MotorVehicle.Make)).Ascending()
                .OnColumn(nameof(MotorVehicle.Model)).Ascending();
        }

        if (!Schema.Table(MappingTable).Exists())
        {
            Create.Table(MappingTable)
                .WithIdColumn()
                .WithColumn(nameof(ProductMotorVehicle.ProductId)).AsInt32().NotNullable()
                    .ForeignKey("FK_ProductMotorVehicle_Product", "Product", nameof(BaseEntity.Id))
                    .OnDelete(Rule.Cascade)
                .WithColumn(nameof(ProductMotorVehicle.MotorVehicleId)).AsInt32().NotNullable()
                    .ForeignKey("FK_ProductMotorVehicle_MotorVehicle", VehicleTable, nameof(BaseEntity.Id))
                    .OnDelete(Rule.Cascade);

            Create.Index("IX_ProductMotorVehicle_Product_Vehicle")
                .OnTable(MappingTable)
                .OnColumn(nameof(ProductMotorVehicle.ProductId)).Ascending()
                .OnColumn(nameof(ProductMotorVehicle.MotorVehicleId)).Ascending()
                .WithOptions().Unique();

            Create.Index("IX_ProductMotorVehicle_Vehicle")
                .OnTable(MappingTable)
                .OnColumn(nameof(ProductMotorVehicle.MotorVehicleId)).Ascending();
        }
    }

    public override void Down()
    {
        // INFO: no down initial migration. Leave schema as it is or ask merchant to delete it.
    }
}
