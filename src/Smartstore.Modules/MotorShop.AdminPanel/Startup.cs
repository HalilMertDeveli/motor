using Autofac;
using MotorShop.AdminPanel.Services;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.AdminPanel;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<PanelQueryService>().AsSelf().InstancePerLifetimeScope();
    }
}
