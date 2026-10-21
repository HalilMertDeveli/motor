global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Smartstore;
global using Smartstore.Core.Localization;
global using Smartstore.Web.Modelling;
using System.Runtime.CompilerServices;
using MotorShop.PaymentTerms.Configuration;
using MotorShop.PaymentTerms.Services;
using Smartstore.Core.Data;
using Smartstore.Engine.Modularity;

[assembly: InternalsVisibleTo("MotorShop.PaymentTerms.Tests")]

namespace MotorShop.PaymentTerms;

internal class Module : ModuleBase
{
    private readonly SmartDbContext _db;

    public Module(SmartDbContext db)
    {
        _db = db;
    }

    public override async Task InstallAsync(ModuleInstallationContext context)
    {
        await ImportLanguageResourcesAsync();
        await TrySaveSettingsAsync<PaymentTermsSettings>();

        // Runs on fresh installs (settings are seeded before modules) and when an admin installs the module later.
        await PaymentTermsSetup.EnsureDealerRoleAsync(_db);
        await PaymentTermsSetup.EnsureDescriptionsAsync(_db);
        await PaymentTermsSetup.ConfigureActiveMethodsAsync(_db);

        await base.InstallAsync(context);
    }

    public override async Task UninstallAsync()
    {
        // The dealer role and its customers, payment descriptions and order notes are store data: kept.
        await DeleteSettingsAsync<PaymentTermsSettings>();
        await DeleteLanguageResourcesAsync();
        await base.UninstallAsync();
    }
}
