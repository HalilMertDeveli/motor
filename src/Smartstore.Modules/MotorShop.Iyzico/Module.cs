global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Tasks;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using Smartstore.Scheduling;

namespace MotorShop.Iyzico;

internal class Module : ModuleBase, IConfigurable
{
    private readonly ITaskStore _taskStore;

    public Module(ITaskStore taskStore)
    {
        _taskStore = taskStore;
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
