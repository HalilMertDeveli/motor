using Autofac;
using MotorShop.SupplierCatalog.Services;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.SupplierCatalog;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<SupplierCatalogImporter>().AsSelf().InstancePerLifetimeScope();
    }
}
