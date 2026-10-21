global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using System.Runtime.CompilerServices;
using MotorShop.Tracking.Configuration;
using MotorShop.Tracking.Services;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

[assembly: InternalsVisibleTo("MotorShop.Tracking.Tests")]

namespace MotorShop.Tracking;

internal class Module : ModuleBase, IConfigurable
{
    private readonly ShipmentTemplateLocalizer _templateLocalizer;

    public Module(ShipmentTemplateLocalizer templateLocalizer)
    {
        _templateLocalizer = templateLocalizer;
    }

    public RouteInfo GetConfigurationRoute()
        => new("Configure", "MotorTrackingAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await TrySaveSettingsAsync<ShipmentTrackingSettings>();

        // Only if a Turkish language exists and the templates have no Turkish text yet.
        await _templateLocalizer.ApplyAsync();

        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        // Tracking URLs already stored on shipments and the localized e-mail texts are kept.
        await DeleteSettingsAsync<ShipmentTrackingSettings>();
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
