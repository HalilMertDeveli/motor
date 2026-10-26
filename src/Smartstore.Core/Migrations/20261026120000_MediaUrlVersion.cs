using FluentMigrator;
using Smartstore.Core.Configuration;
using Smartstore.Core.Content.Media;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: media URLs carry a version hash ("?ver=", from file date and size) in EXISTING databases, so a product photo
/// that is corrected in place (same file id and name) is loaded again instead of the copy browsers and proxies keep for
/// <see cref="MediaSettings.ResponseCacheDuration"/> (7 days). Without it, customers kept seeing the old SHAD kit photos
/// after they were replaced on 2026-10-22. Fresh installs get it from <see cref="Installation.MotorMediaDefaults"/>.
///
/// Only the untouched default ("False" or missing) is changed, once.
/// </summary>
[MigrationVersion("2026-10-26 12:00:00", "MOTOR: media URL version")]
internal class MediaUrlVersion : Migration, IDataSeeder<SmartDbContext>
{
    public const string SettingName = nameof(MediaSettings) + "." + nameof(MediaSettings.AppendFileVersionToUrl);

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
        var settings = await context.Settings
            .Where(x => x.Name == SettingName)
            .ToListAsync(cancelToken);

        if (settings.Count == 0)
        {
            context.Settings.Add(new Setting { Name = SettingName, Value = "True", StoreId = 0 });
        }

        settings.Where(x => x.Value.EqualsNoCase("False")).Each(x => x.Value = "True");

        await context.SaveChangesAsync(cancelToken);
    }
}
