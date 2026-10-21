global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using System.Runtime.CompilerServices;
using MotorShop.AdminPanel.Configuration;
using Smartstore.Engine.Modularity;

[assembly: InternalsVisibleTo("MotorShop.AdminPanel.Tests")]

namespace MotorShop.AdminPanel;

internal class Module : ModuleBase
{
    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await TrySaveSettingsAsync<MotorPanelSettings>();
        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        await DeleteSettingsAsync<MotorPanelSettings>();
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
