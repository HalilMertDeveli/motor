using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorShop.Security.HealthChecks;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.Security;

internal class Startup : StarterBase
{
    const string ReadyTag = "ready";

    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        var section = appContext.Configuration.GetSection(MotorSecurityOptions.SectionName);
        var options = section.Get<MotorSecurityOptions>() ?? new MotorSecurityOptions();
        services.Configure<MotorSecurityOptions>(section);

        // Kestrel sends "Server: Kestrel" unless told otherwise.
        services.Configure<KestrelServerOptions>(o => o.AddServerHeader = false);

        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.ReadinessTimeoutSeconds));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, HealthStatus.Unhealthy, [ReadyTag], timeout)
            .AddCheck<TaskSchedulerHealthCheck>(TaskSchedulerHealthCheck.Name, HealthStatus.Degraded, [ReadyTag], timeout);

        services.AddHostedService<ProductionConfigurationCheck>();
    }

    public override void BuildPipeline(RequestPipelineBuilder builder)
    {
        var options = builder.ApplicationContext.Configuration
            .GetSection(MotorSecurityOptions.SectionName)
            .Get<MotorSecurityOptions>() ?? new MotorSecurityOptions();

        // Before static files, so that theme assets, media and health responses get the headers too.
        builder.Configure(StarterOrdering.BeforeStaticFilesMiddleware, app =>
        {
            app.UseMiddleware<SecurityHeadersMiddleware>();

            // Liveness: the process serves requests. No dependency is checked.
            app.UseHealthChecks(options.LivenessPath, new HealthCheckOptions
            {
                Predicate = _ => false,
                ResponseWriter = WriteStatusAsync
            });

            // Readiness: database reachable (503 otherwise), task scheduler polling (200 "Degraded" otherwise).
            app.UseHealthChecks(options.ReadinessPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains(ReadyTag),
                ResponseWriter = WriteStatusAsync
            });
        });
    }

    /// <summary>
    /// Writes only the overall status. Check names, descriptions and exceptions stay in the log.
    /// </summary>
    private static Task WriteStatusAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(report.Status.ToString());
    }
}
