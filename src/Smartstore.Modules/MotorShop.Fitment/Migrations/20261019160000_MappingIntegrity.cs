using FluentMigrator;
using MotorShop.Fitment.Domain;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data;
using Smartstore.Domain;

namespace MotorShop.Fitment.Migrations;

/// <summary>
/// Adds the foreign keys and indexes of ProductMotorVehicle where they are missing.
/// </summary>
/// <remarks>
/// Root cause: on app installation Smartstore creates the schema from the EF model and marks every migration,
/// including <see cref="Initial"/>, as applied without running it. The model had no relationships for
/// ProductMotorVehicle, so installations made before <see cref="ProductMotorVehicleMap"/> existed have no
/// foreign keys, and mappings to missing products or vehicles were accepted.
/// <para>
/// Before a constraint is added, rows it would reject are deleted:
/// mappings whose product or vehicle no longer exists (exactly what ON DELETE CASCADE would have removed,
/// they can never be shown), and exact duplicates of (ProductId, MotorVehicleId), keeping the lowest Id.
/// Nothing else is changed or deleted. Every step checks the current schema first, so the migration is safe
/// to run on databases that already have some or all of these objects.
/// </para>
/// <para>
/// SQLite cannot add a foreign key to an existing table, so there only the cleanup and indexes are applied.
/// New SQLite installations get the foreign keys from the EF model.
/// </para>
/// </remarks>
[MigrationVersion("2026-10-19 16:00:00", "MotorShop.Fitment: Mapping integrity")]
internal class MappingIntegrity : Migration
{
    const string VehicleTable = "MotorVehicle";
    const string MappingTable = "ProductMotorVehicle";
    const string ProductTable = "Product";
    const string UniqueIndex = "IX_ProductMotorVehicle_Product_Vehicle";
    const string VehicleIndex = "IX_ProductMotorVehicle_Vehicle";

    public override void Up()
    {
        if (!Schema.Table(MappingTable).Exists())
        {
            // Nothing to repair. Initial creates the table with all constraints.
            return;
        }

        var mappings = Schema.Table(MappingTable);
        var isSqlite = DataSettings.Instance.DbFactory.DbSystem == DbSystemType.SQLite;
        var addProductFk = !isSqlite && !mappings.Constraint(ProductMotorVehicleMap.ProductForeignKey).Exists();
        var addVehicleFk = !isSqlite && !mappings.Constraint(ProductMotorVehicleMap.VehicleForeignKey).Exists();
        var addUniqueIndex = !mappings.Index(UniqueIndex).Exists();

        if (addProductFk || addVehicleFk || isSqlite)
        {
            ExecuteSql(@"
DELETE FROM {mapping}
WHERE NOT EXISTS (SELECT 1 FROM {product} WHERE {product}.{id} = {mapping}.{productId})
   OR NOT EXISTS (SELECT 1 FROM {vehicle} WHERE {vehicle}.{id} = {mapping}.{vehicleId})");
        }

        if (addUniqueIndex)
        {
            // The derived table keeps MySQL from rejecting a subquery on the table being deleted from.
            ExecuteSql(@"
DELETE FROM {mapping}
WHERE {id} NOT IN (
    SELECT k.{id} FROM (
        SELECT MIN({id}) AS {id} FROM {mapping} GROUP BY {productId}, {vehicleId}
    ) k
)");

            Create.Index(UniqueIndex)
                .OnTable(MappingTable)
                .OnColumn(nameof(ProductMotorVehicle.ProductId)).Ascending()
                .OnColumn(nameof(ProductMotorVehicle.MotorVehicleId)).Ascending()
                .WithOptions().Unique();
        }

        if (!mappings.Index(VehicleIndex).Exists())
        {
            Create.Index(VehicleIndex)
                .OnTable(MappingTable)
                .OnColumn(nameof(ProductMotorVehicle.MotorVehicleId)).Ascending();
        }

        if (addProductFk)
        {
            Create.ForeignKey(ProductMotorVehicleMap.ProductForeignKey)
                .FromTable(MappingTable).ForeignColumn(nameof(ProductMotorVehicle.ProductId))
                .ToTable(ProductTable).PrimaryColumn(nameof(BaseEntity.Id))
                .OnDelete(System.Data.Rule.Cascade);
        }

        if (addVehicleFk)
        {
            Create.ForeignKey(ProductMotorVehicleMap.VehicleForeignKey)
                .FromTable(MappingTable).ForeignColumn(nameof(ProductMotorVehicle.MotorVehicleId))
                .ToTable(VehicleTable).PrimaryColumn(nameof(BaseEntity.Id))
                .OnDelete(System.Data.Rule.Cascade);
        }
    }

    public override void Down()
    {
        // INFO: the constraints are part of the schema the module expects. Nothing to roll back.
    }

    /// <summary>
    /// Runs <paramref name="template"/> with identifiers quoted for the current database
    /// (backticks for MySQL, ANSI double quotes for SQL Server, PostgreSQL and SQLite).
    /// </summary>
    private void ExecuteSql(string template)
    {
        string Build(Func<string, string> quote)
            => template
                .Replace("{mapping}", quote(MappingTable))
                .Replace("{product}", quote(ProductTable))
                .Replace("{vehicle}", quote(VehicleTable))
                .Replace("{id}", quote(nameof(BaseEntity.Id)))
                .Replace("{productId}", quote(nameof(ProductMotorVehicle.ProductId)))
                .Replace("{vehicleId}", quote(nameof(ProductMotorVehicle.MotorVehicleId)));

        var isMySql = DataSettings.Instance.DbFactory.DbSystem == DbSystemType.MySql;
        Execute.Sql(Build(x => isMySql ? $"`{x}`" : $"\"{x}\""));
    }
}
