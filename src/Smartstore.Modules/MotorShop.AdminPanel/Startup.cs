using Autofac;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Security;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.AdminPanel;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<PanelQueryService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ProductEditService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<CategoryEditService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<PriceLockService>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            // "Hide all prices" (MotorPanelSettings.HideAllPrices). Inactive while the setting is off.
            builder.RegisterDecorator<PriceLockPermissionService, IPermissionService>();
            builder.RegisterDecorator<PriceLockCartValidator, IShoppingCartValidator>();
        }
    }
}
