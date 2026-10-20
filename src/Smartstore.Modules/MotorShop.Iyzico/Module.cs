global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Services;
using MotorShop.Iyzico.Tasks;
using Smartstore.Core.Data;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using Smartstore.Scheduling;

namespace MotorShop.Iyzico;

internal class Module : ModuleBase, IConfigurable
{
    private readonly ITaskStore _taskStore;
    private readonly SmartDbContext _db;

    public Module(ITaskStore taskStore, SmartDbContext db)
    {
        _taskStore = taskStore;
        _db = db;
    }

    public RouteInfo GetConfigurationRoute()
        => new("Configure", "IyzicoAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await TrySaveSettingsAsync<IyzicoSettings>();

        await _taskStore.GetOrAddTaskAsync<IyzicoReconciliationTask>(x =>
        {
            x.Name = "iyzico payment reconciliation";
            x.CronExpression = "*/10 * * * *";
            x.Enabled = true;
        });

        // Payments.Manual stores card numbers in the shop database. On a fresh install (AppInstallation) the settings
        // are seeded before modules are installed and module migrations are only stamped, so remove it here.
        await BlockedPaymentMethods.DeactivateAsync(_db);

        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        // Settings and the transaction table are kept on purpose: they hold payment audit data and protected keys.
        await DeleteLanguageResourcesAsync();
        await _taskStore.TryDeleteTaskAsync<IyzicoReconciliationTask>();

        await base.UninstallAsync();
    }
}
