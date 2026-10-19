using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Services;
using Smartstore.Core.Data;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.Iyzico;

internal class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.AddTransient<IDbContextConfigurationSource<SmartDbContext>, SmartDbContextConfigurer>();

        services.AddHttpClient<IIyzicoApiClient, IyzicoApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<IyzicoCredentialProvider>().As<IIyzicoCredentialProvider>().InstancePerLifetimeScope();
        builder.RegisterType<IyzicoPaymentService>().AsSelf().InstancePerLifetimeScope();
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
