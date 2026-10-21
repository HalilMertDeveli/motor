global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotorShop.Fitment.Services;
using Smartstore.Core.Data;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

namespace MotorShop.Fitment;

internal class Module : ModuleBase, IConfigurable
{
    public RouteInfo GetConfigurationRoute()
        => new("Configure", "MotorFitmentAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await SeedSampleVehiclesAsync(context);
        await base.InstallAsync(context);
    }

    /// <summary>
    /// On app installation the schema comes from the EF model and the Initial migration (with its seeder) is
    /// only marked as applied, so the starter vehicles are seeded here. They are sample data with approximate
    /// years and are only added when the installer was asked for sample data.
    /// </summary>
    private static async Task SeedSampleVehiclesAsync(ModuleInstallationContext context)
    {
        if (context.Stage != ModuleInstallationStage.AppInstallation || context.SeedSampleData != true)
        {
            return;
        }

        var db = context.Scope.Resolve<SmartDbContext>();
        if (!await db.MotorVehicles().AnyAsync())
        {
            db.MotorVehicles().AddRange(VehicleSeedData.GetVehicles());
            await db.SaveChangesAsync();
            context.Logger.Info("MotorShop.Fitment: seeded sample motorcycles.");
        }
    }

    public override async Task UninstallAsync()
    {
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
