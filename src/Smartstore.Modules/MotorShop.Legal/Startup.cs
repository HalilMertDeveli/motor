using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MotorShop.Legal.Filters;
using MotorShop.Legal.Services;
using Smartstore.Core.Widgets;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.Web.Controllers;

namespace MotorShop.Legal;

internal class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        if (!appContext.IsInstalled)
        {
            return;
        }

        services.Configure<MvcOptions>(o =>
        {
            // POST Checkout/Confirm and the AJAX payment confirmation of redirect payment methods.
            o.Filters.AddEndpointFilter<LegalConfirmFilter, CheckoutController>()
                .ForAction(x => x.ConfirmOrder())
                .ForAction(x => x.ConfirmPayment());

            // Confirm view (GET, and POST when it re-renders with warnings).
            o.Filters.AddEndpointFilter<LegalTermsTextFilter, CheckoutController>()
                .ForAction(x => x.Confirm())
                .ForAction(x => x.ConfirmOrder());
        });
    }

    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<LegalDocumentService>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            builder.RegisterType<LegalWidgetSource>().As<IWidgetSource>().InstancePerLifetimeScope();
        }
    }
}
