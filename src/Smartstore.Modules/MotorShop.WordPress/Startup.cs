using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Services;
using Smartstore.Core.Seo;
using Smartstore.Engine;
using Smartstore.Engine.Builders;

namespace MotorShop.WordPress;

internal class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.Configure<WordPressOptions>(appContext.Configuration.GetSection(WordPressOptions.SectionName));

        services.AddHttpClient<IWordPressApiClient, WordPressApiClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<WordPressOptions>>().Value;
                // The resilience pipeline enforces the real time limits; this is only a last resort.
                client.Timeout = TimeSpan.FromSeconds(60);
                // No platform or version information for the WordPress logs.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MOTOR-WordPress/1.0");
                client.MaxResponseContentBufferSize = Math.Clamp(options.MaxResponseKilobytes, 64, 32 * 1024) * 1024L;
            })
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptionsMonitor<WordPressOptions>>()));
    }

    public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
    {
        builder.RegisterType<WordPressResilience>().AsSelf().SingleInstance();
        builder.RegisterType<WordPressStatus>().AsSelf().SingleInstance();
        builder.RegisterType<WordPressThrottle>().AsSelf().SingleInstance();
        builder.RegisterType<WordPressHtmlSanitizer>().AsSelf().SingleInstance();
        builder.RegisterType<BlogPostMapper>().AsSelf().SingleInstance();
        builder.RegisterType<WordPressContentService>().AsSelf().InstancePerLifetimeScope();

        if (appContext.IsInstalled)
        {
            builder.RegisterType<WordPressXmlSitemapPublisher>().As<IXmlSitemapPublisher>().InstancePerLifetimeScope();
        }
    }

    public override void MapRoutes(EndpointRoutingBuilder builder)
    {
        builder.MapRoutes(StarterOrdering.EarlyRoute, routes => routes.MapPost(WordPressWebhook.Path, WordPressWebhook.HandleAsync));
    }

    /// <summary>
    /// No redirects, and every connection is checked against the resolved IP address, so that a DNS answer
    /// pointing to an internal address (DNS rebinding) cannot turn the client into a proxy for internal services.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler(IOptionsMonitor<WordPressOptions> options)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.TimeoutSeconds, 1, 30)),
            ConnectCallback = async (context, cancelToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancelToken);
                var allowPrivate = options.CurrentValue.AllowPrivateNetwork;
                var allowed = addresses.Where(a => allowPrivate || WordPressEndpoint.IsPublicAddress(a)).ToList();
                if (allowed.Count == 0)
                {
                    throw new HttpRequestException("The WordPress host resolves only to non public addresses.");
                }

                // Every checked address in turn (e.g. IPv6 first, then IPv4), never an unchecked one.
                Exception? last = null;
                foreach (var address in allowed)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancelToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException ex)
                    {
                        socket.Dispose();
                        last = ex;
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                throw new HttpRequestException("WordPress could not be reached.", last);
            }
        };
    }
}
