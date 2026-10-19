using Autofac;
using MotorShop.Tracking.Services;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.Tracking;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<CarrierTrackingService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ShipmentTemplateLocalizer>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            builder.RegisterType<TrackingWidgetSource>().As<IWidgetSource>().InstancePerLifetimeScope();
        }
    }
}
