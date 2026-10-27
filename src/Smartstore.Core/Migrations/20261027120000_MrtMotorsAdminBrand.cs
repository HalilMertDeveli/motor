using FluentMigrator;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: the admin area names the store, not the platform (owner 2026-10-27: "bütün marka isimleri mrt motors
/// olacak"). Replaces the word "Smartstore" in the Turkish admin texts of EXISTING databases, e.g. the
/// "Smartstore hakkında" menu entry and the version labels of the system information page.
///
/// Not replaced: web addresses, package file names (Smartstore.Module.*.zip), configuration keys and the copyright
/// notice of the software on the about page and in the storefront footer (AGPL legal notice).
///
/// Only rows that still contain the old fragment and that no admin edited are updated, so it is idempotent.
/// The Turkish language packs carry the same texts for new installs.
/// </summary>
[MigrationVersion("2026-10-27 12:00:00", "MOTOR: MRT Motors admin brand")]
internal class MrtMotorsAdminBrand : Migration, IDataSeeder<SmartDbContext>
{
    internal static readonly (string Key, string OldText, string NewText)[] Replacements =
    [
        ("Admin.AppNews", "Smartstore Haberleri", "Platform haberleri"),
        ("Admin.CheckUpdate.AutoUpdatePossibleInfo", "Smartstore web sunucunuza", "MRT Motors web sunucunuza"),
        ("Admin.CheckUpdate.IsUpToDate", "Smartstore güncel", "MRT Motors güncel"),
        ("Admin.Common.About", "Smartstore hakkında", "MRT Motors hakkında"),
        ("Admin.Configuration.Plugins.Description.Step1", "Smartstore dizininizdeki", "MRT Motors dizininizdeki"),
        ("Admin.Configuration.Settings.CustomerUser.Privacy.Cookies.AdminInstruction", "Smartstore'a bir eklenti", "MRT Motors'a bir eklenti"),
        ("Admin.Configuration.Settings.General", "SmartStore.NET reCAPTCHA", "MRT Motors reCAPTCHA"),
        ("Admin.Configuration.Settings.General.Common", "SmartStore.NET reCAPTCHA", "MRT Motors reCAPTCHA"),
        ("Admin.Configuration.Settings.General.Common.Captcha", "SmartStore.NET reCAPTCHA", "MRT Motors reCAPTCHA"),
        ("Admin.Configuration.Themes.Option.DefaultDesktopTheme.Hint", "Temaları www.SmartStore.com adresindeki eklentiler sayfasından indirebilirsiniz", "Yeni temaları Temalar sayfasından paket olarak yükleyebilirsiniz"),
        ("Admin.Configuration.Themes.Option.DefaultMobileTheme.Hint", "Temaları www.SmartStore.com adresindeki eklentiler sayfasından indirebilirsiniz", "Yeni temaları Temalar sayfasından paket olarak yükleyebilirsiniz"),
        ("Admin.Help.OtherWorkNote", "Smartstore, aşağıda", "MRT Motors, aşağıda"),
        ("Admin.Packaging.IsIncompatible", "Lütfen Smartstore'u güncelleyin", "Lütfen MRT Motors'u güncelleyin"),
        ("Admin.PageTitle", "Smartstore yönetim paneli", "MRT Motors yönetimi"),
        ("Admin.System.Maintenance.DbBackup.RestoreVersionWarning", "farklı bir Smartstore sürümüyle", "farklı bir MRT Motors sürümüyle"),
        ("Admin.System.SystemInfo.AppDate.Hint", "Bu Smartstore sürümünün", "Bu MRT Motors sürümünün"),
        ("Admin.System.SystemInfo.AppVersion", "Smartstore sürümü", "MRT Motors sürümü"),
        ("Admin.System.SystemInfo.AppVersion.Hint", "Smartstore sürümü", "MRT Motors sürümü"),
        ("Admin.System.Warnings.IncompatiblePlugin", "Smartstore sürümünüzle", "MRT Motors sürümünüzle"),
        ("Admin.Theme.GoogleFonts.Gdpr.Hint", "Smartstore olası sonuçlardan", "Yazılım sağlayıcısı olası sonuçlardan"),
        ("Plugins.Description.SmartStore.DevTools", "Smartstore Geliştirici Araçları", "MRT Motors geliştirici araçları"),
        ("Plugins.Description.SmartStore.WebApi", "Smartstore ile veri", "MRT Motors ile veri"),
        ("Plugins.Description.MotorShop.AdminPanel", "Mevcut Smartstore servisleri", "Mevcut mağaza servisleri"),
        ("Plugins.MotorShop.AdminPanel.Content.PagesHint", " (Smartstore Topics)", string.Empty),
        ("Plugins.MotorShop.AdminPanel.Coupon.Hint", "Smartstore indirimleri.", "Mağaza indirimleri.")
    ];

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
        // Contains() is case-sensitive on some providers: every old fragment contains "Smart".
        var resources = await context.LocaleStringResources
            .Where(x => x.ResourceValue.Contains("Smart"))
            .ToListAsync(cancelToken);

        foreach (var resource in resources.Where(x => x.IsTouched != true))
        {
            resource.ResourceValue = Apply(resource.ResourceName, resource.ResourceValue);
        }

        await context.SaveChangesAsync(cancelToken);
    }

    internal static string Apply(string key, string value)
    {
        foreach (var (k, oldText, newText) in Replacements)
        {
            if (k.EqualsNoCase(key) && value != null && value.Contains(oldText, StringComparison.Ordinal))
            {
                value = value.Replace(oldText, newText, StringComparison.Ordinal);
            }
        }

        return value;
    }
}
