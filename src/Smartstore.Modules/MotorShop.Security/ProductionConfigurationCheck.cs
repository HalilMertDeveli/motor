using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Smartstore;
using Smartstore.Engine;
using Smartstore.Utilities;

namespace MotorShop.Security;

/// <summary>
/// Logs a warning at start-up for every setting that is unsafe on a public production host.
/// It only reports and never changes or blocks anything, so a misconfigured store still starts.
/// </summary>
internal sealed class ProductionConfigurationCheck : IHostedService
{
    private readonly IApplicationContext _appContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProductionConfigurationCheck> _logger;

    public ProductionConfigurationCheck(
        IApplicationContext appContext,
        IConfiguration configuration,
        ILogger<ProductionConfigurationCheck> logger)
    {
        _appContext = appContext;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancelToken)
    {
        if (_appContext.HostEnvironment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        foreach (var warning in GetWarnings())
        {
            _logger.LogWarning("Production configuration: {Warning:l}", warning);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancelToken)
        => Task.CompletedTask;

    internal IEnumerable<string> GetWarnings()
    {
        var config = _appContext.AppConfiguration;

        var allowedHosts = _configuration["AllowedHosts"];
        if (allowedHosts.IsEmpty() || allowedHosts.Split(';').Any(x => x.Trim() == "*"))
        {
            yield return "\"AllowedHosts\" accepts any host. Set it to the store domains, e.g. \"motor.example.com;www.motor.example.com\".";
        }

        var proxy = config.ReverseProxy;
        if (proxy?.Enabled == true)
        {
            if ((proxy.KnownProxies?.Length ?? 0) == 0 && (proxy.KnownNetworks?.Length ?? 0) == 0)
            {
                yield return "Reverse proxy is enabled without \"KnownProxies\"/\"KnownNetworks\". Only loopback proxies are trusted, "
                    + "so behind a remote proxy the client IP and the HTTPS scheme are wrong.";
            }

            if (proxy.ForwardHostHeader && (proxy.AllowedHosts?.Length ?? 0) == 0)
            {
                yield return "Reverse proxy forwards X-Forwarded-Host without \"Smartstore:ReverseProxy:AllowedHosts\". Any host value sent by a trusted proxy is accepted.";
            }
        }

        if (config.UseDeveloperExceptionPage == true)
        {
            yield return "\"UseDeveloperExceptionPage\" is true. Stack traces and source code are shown to visitors.";
        }

        if (config.ShowCustomErrors)
        {
            yield return "\"ShowCustomErrors\" is true. Exception messages are shown on the error page.";
        }

        if (CommonHelper.IsDevEnvironment)
        {
            yield return "The application detects a development environment (debugger attached or Smartstore.sln above the content root). "
                + "HTTPS redirection and canonical host redirection are suppressed. Deploy outside the source tree.";
        }

        if (_appContext.ModuleCatalog?.GetModuleByName("Smartstore.DevTools") != null)
        {
            yield return "Module \"Smartstore.DevTools\" is installed. Uninstall it on production.";
        }
    }
}
