global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using System.Runtime.CompilerServices;
using Smartstore.Engine.Modularity;
using Smartstore.Http;

[assembly: InternalsVisibleTo("MotorShop.WordPress.Tests")]

namespace MotorShop.WordPress;

/// <summary>
/// Headless WordPress: WordPress is the editor, MOTOR renders. Configuration comes from the
/// "MotorShop:WordPress" section (environment variables), so install and uninstall touch no shop data.
/// </summary>
internal class Module : ModuleBase, IConfigurable
{
    public RouteInfo GetConfigurationRoute()
        => new("Index", "MotorWordPressAdmin", new { area = "Admin" });

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
