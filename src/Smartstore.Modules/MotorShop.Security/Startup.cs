using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.Security;

internal class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        // Kestrel sends "Server: Kestrel" unless told otherwise.
        services.Configure<KestrelServerOptions>(o => o.AddServerHeader = false);
    }

    public override void BuildPipeline(RequestPipelineBuilder builder)
    {
        // Before static files, so that theme assets and media get the headers too.
        builder.Configure(StarterOrdering.BeforeStaticFilesMiddleware, app =>
        {
            app.UseMiddleware<SecurityHeadersMiddleware>();
        });
    }
}
