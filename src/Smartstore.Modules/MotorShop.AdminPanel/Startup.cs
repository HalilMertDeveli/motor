using Autofac;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Security;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.AdminPanel;

internal class Startup : StarterBase
{
    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<PanelQueryService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ProductEditService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SimpleProductService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SubmissionRegistry>().AsSelf().SingleInstance();
        builder.RegisterType<CategoryEditService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<PriceLockService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<OrderPanelService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<PanelReportService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ContentPanelService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<CouponPanelService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SettingsPanelService>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            // Draft notice on the storefront product page (only admins can open an unpublished product).
            builder.RegisterType<DraftNoticeWidgetSource>().As<IWidgetSource>().InstancePerLifetimeScope();

            // "Hide all prices" (MotorPanelSettings.HideAllPrices). Inactive while the setting is off.
            builder.RegisterDecorator<PriceLockPermissionService, IPermissionService>();
            builder.RegisterDecorator<PriceLockCartValidator, IShoppingCartValidator>();
        }
    }
}
