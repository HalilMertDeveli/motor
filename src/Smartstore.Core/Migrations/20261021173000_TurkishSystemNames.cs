using FluentMigrator;
using Smartstore.Core.Installation;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Turkish names of system entities for EXISTING databases, so the admin shows no English names
/// (activity log types, scheduled tasks, templates, currencies, measures, customer roles, quantity units).
/// Fresh Turkish installs get them from <see cref="TrTRSeedData"/>.
/// Only names that still have their English default value are renamed; names changed by an admin are kept.
/// </summary>
[MigrationVersion("2026-10-21 17:30:00", "MOTOR: Turkish system names")]
internal class TurkishSystemNamesMigration : Migration, IDataSeeder<SmartDbContext>
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        foreach (var x in await context.ActivityLogTypes.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.ActivityLogTypes, x.SystemKeyword, x.Name);
        }

        foreach (var x in await context.TaskDescriptors.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.ScheduleTasks, x.Type, x.Name);
        }

        foreach (var x in await context.ProductTemplates.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Templates, x.ViewPath, x.Name);
        }

        foreach (var x in await context.CategoryTemplates.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Templates, x.ViewPath, x.Name);
        }

        foreach (var x in await context.ManufacturerTemplates.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Templates, x.ViewPath, x.Name);
        }

        foreach (var x in await context.Currencies.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Currencies, x.CurrencyCode, x.Name);
        }

        foreach (var x in await context.MeasureDimensions.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Measures, x.SystemKeyword, x.Name);
        }

        foreach (var x in await context.MeasureWeights.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.Measures, x.SystemKeyword, x.Name);
        }

        foreach (var x in await context.CustomerRoles.ToListAsync(cancelToken))
        {
            x.Name = Rename(TurkishSystemNames.CustomerRoles, x.SystemName, x.Name);
        }

        foreach (var x in await context.QuantityUnits.ToListAsync(cancelToken))
        {
            if (x.Name != null && TurkishSystemNames.QuantityUnits.TryGetValue(x.Name, out var name))
            {
                x.Name = name;
                x.NamePlural = name;
                x.Description = name;
            }
        }

        await context.SaveChangesAsync(cancelToken);
    }

    private static string Rename(IReadOnlyDictionary<string, (string En, string Tr)> names, string key, string current)
    {
        if (key != null && names.TryGetValue(key, out var name) && string.Equals(current, name.En, StringComparison.Ordinal))
        {
            return name.Tr;
        }

        return current;
    }
}
