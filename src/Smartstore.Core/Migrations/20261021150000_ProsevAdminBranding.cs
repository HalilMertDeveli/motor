using FluentMigrator;
using Smartstore.Core.Localization;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Prosev name in the admin area (browser title of every admin page). Resources only.
/// "About Smartstore" stays: that page holds the AGPL license and copyright notices of the software.
/// </summary>
[MigrationVersion("2026-10-21 15:00:00", "MOTOR: Prosev admin branding")]
internal class ProsevAdminBranding : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
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
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        builder.AddOrUpdate("Admin.PageTitle")
            .Value("Prosev administration")
            .Value("de", "Prosev Administration")
            .Value("tr", "Prosev yönetimi");

        builder.AddOrUpdate("Admin.Common.About")
            .Value("tr", "Smartstore hakkında");
    }
}
