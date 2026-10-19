using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotorShop.CatalogImport.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Widgets;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.CatalogImport;

internal class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.AddTransient<IDbContextConfigurationSource<SmartDbContext>, SmartDbContextConfigurer>();
    }

    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<CatalogImportService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<FitmentMatcher>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<CatalogPublicationService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<VehicleReferenceService>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            builder.RegisterType<CatalogWidgetSource>().As<IWidgetSource>().InstancePerLifetimeScope();
        }
    }

    class SmartDbContextConfigurer : IDbContextConfigurationSource<SmartDbContext>
    {
        public void Configure(IServiceProvider services, DbContextOptionsBuilder builder)
        {
            builder.UseDbFactory(b =>
            {
                b.AddModelAssembly(GetType().Assembly);
            });
        }
    }
}
