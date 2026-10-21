using Autofac;
using MotorShop.PaymentTerms.Services;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.PaymentTerms;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        if (appContext.IsInstalled)
        {
            builder.RegisterType<PaymentTermsWidgetSource>().As<IWidgetSource>().InstancePerLifetimeScope();
        }
    }
}
