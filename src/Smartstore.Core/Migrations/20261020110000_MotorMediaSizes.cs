using FluentMigrator;
using Smartstore.Core.Configuration;
using Smartstore.Core.Installation;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: larger, sharper product, category and brand pictures for EXISTING databases
/// (fresh installs get the same values from <see cref="InvariantSeedData"/>).
/// A setting is only changed while it still has Smartstore's default value, so sizes an admin chose are kept.
/// Thumbnails in the new sizes are generated on first request.
/// </summary>
[MigrationVersion("2026-10-20 11:00:00", "MOTOR: media sizes")]
internal class MotorMediaSizes : Migration, IDataSeeder<SmartDbContext>
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
        var names = MotorMediaDefaults.Settings.Select(x => x.Name).ToArray();
        var existing = await context.Settings
            .Where(x => names.Contains(x.Name))
            .ToListAsync(cancelToken);

        foreach (var (name, smartstoreDefault, value) in MotorMediaDefaults.Settings)
        {
            var rows = existing.Where(x => x.Name == name).ToList();
            if (rows.Count == 0)
            {
                // Never saved: the class default applies to all stores.
                context.Settings.Add(new Setting { Name = name, Value = value.ToStringInvariant(), StoreId = 0 });
                continue;
            }

            foreach (var row in rows.Where(x => x.Value.IsEmpty() || x.Value == smartstoreDefault.ToStringInvariant()))
            {
                row.Value = value.ToStringInvariant();
            }
        }

        await context.SaveChangesAsync(cancelToken);
    }
}
