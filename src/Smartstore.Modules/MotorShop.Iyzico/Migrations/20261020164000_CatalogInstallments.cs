using System.Threading;
using FluentMigrator;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;

namespace MotorShop.Iyzico.Migrations;

/// <summary>
/// Existing databases: switches on the catalog installments (single payment or 3 installments) while the
/// installment settings still have their earlier defaults (off, "1,2,3,6,9"). Values an admin changed are kept.
/// Fresh installs get the new defaults from <see cref="Configuration.IyzicoSettings"/>. No schema change.
/// </summary>
[MigrationVersion("2026-10-20 16:40:00", "MotorShop.Iyzico: Catalog installments")]
internal class CatalogInstallments : Migration, IDataSeeder<SmartDbContext>
{
    const string EnableKey = "IyzicoSettings.EnableInstallments";
    const string OptionsKey = "IyzicoSettings.EnabledInstallments";

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        var rows = await context.Settings
            .Where(x => x.Name == EnableKey || x.Name == OptionsKey)
            .ToListAsync(cancelToken);

        foreach (var storeRows in rows.GroupBy(x => x.StoreId))
        {
            var enable = storeRows.FirstOrDefault(x => x.Name == EnableKey);
            var options = storeRows.FirstOrDefault(x => x.Name == OptionsKey);

            var untouched = (enable == null || enable.Value.EqualsNoCase("false"))
                && (options == null || options.Value.IsEmpty() || options.Value == "1,2,3,6,9");

            if (untouched)
            {
                if (enable != null) enable.Value = "True";
                if (options != null) options.Value = "1,3";
            }
        }

        await context.SaveChangesAsync(cancelToken);
    }
}
