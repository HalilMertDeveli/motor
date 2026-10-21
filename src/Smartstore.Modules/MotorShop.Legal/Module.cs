global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using MotorShop.Legal.Configuration;
using MotorShop.Legal.Services;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Seo;
using Smartstore.Engine.Modularity;

[assembly: InternalsVisibleTo("MotorShop.Legal.Tests")]

namespace MotorShop.Legal;

internal class Module : ModuleBase
{
    private readonly SmartDbContext _db;
    private readonly IUrlService _urlService;
    private readonly ISettingService _settingService;

    public Module(SmartDbContext db, IUrlService urlService, ISettingService settingService)
    {
        _db = db;
        _urlService = urlService;
        _settingService = settingService;
    }

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await TrySaveSettingsAsync<LegalCheckoutSettings>();
        await new LegalTopicSeeder(_db, _urlService).SeedAsync();
        await ConfigureCookieConsentAsync();

        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        // Topics are merchant content and the order records are legal evidence: both are kept.
        await DeleteSettingsAsync<LegalCheckoutSettings>();
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }

    /// <summary>
    /// Smartstore's cookie manager is reused. The default "required in EU countries only" hides it for visitors
    /// resolved to Türkiye, so the requirement is switched to "depends on country" and Türkiye is enabled.
    /// </summary>
    private async Task ConfigureCookieConsentAsync()
    {
        var current = await _settingService.GetSettingByKeyAsync(
            $"{nameof(PrivacySettings)}.{nameof(PrivacySettings.CookieConsentRequirement)}",
            CookieConsentRequirement.RequiredInEUCountriesOnly);

        if (current == CookieConsentRequirement.RequiredInEUCountriesOnly)
        {
            await _settingService.ApplySettingAsync(
                $"{nameof(PrivacySettings)}.{nameof(PrivacySettings.CookieConsentRequirement)}",
                CookieConsentRequirement.DependsOnCountry);
        }

        var turkey = await _db.Countries.FirstOrDefaultAsync(x => x.TwoLetterIsoCode == "TR");
        if (turkey != null && !turkey.DisplayCookieManager)
        {
            turkey.DisplayCookieManager = true;
        }

        await _db.SaveChangesAsync();
    }
}
