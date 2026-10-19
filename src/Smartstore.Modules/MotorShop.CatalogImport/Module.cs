global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using Autofac;
using MotorShop.CatalogImport.Migrations;
using Smartstore.Core.Data;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

namespace MotorShop.CatalogImport;

internal class Module : ModuleBase, IConfigurable
{
    public RouteInfo GetConfigurationRoute()
        => new("Index", "MotorCatalogAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();

        if (context.Stage == ModuleInstallationStage.AppInstallation)
        {
            // The schema comes from the EF model here and the Initial migration with its seeder is skipped.
            await Initial.SeedManualSourceAsync(context.Scope.Resolve<SmartDbContext>());
        }

        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
