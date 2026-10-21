using FluentMigrator;
using Smartstore.Core.Installation;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Turkish country names for EXISTING databases. Fresh installs get them from <see cref="TrTRSeedData"/>.
/// The name itself is changed, not a localized property: with only one published language Smartstore
/// does not load localized entity values at all.
/// </summary>
[MigrationVersion("2026-10-21 17:00:00", "MOTOR: Turkish country names")]
internal class TurkishCountryNamesMigration : Migration, IDataSeeder<SmartDbContext>
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
        var countries = await context.Countries.ToListAsync(cancelToken);

        foreach (var country in countries)
        {
            if (country.TwoLetterIsoCode != null && TurkishCountryNames.ByTwoLetterIsoCode.TryGetValue(country.TwoLetterIsoCode, out var name))
            {
                country.Name = name;
            }
        }

        await context.SaveChangesAsync(cancelToken);
    }
}
