using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Google.Auth.Tests;

/// <summary>
/// Registers the Google handler the way the module does (scheme without AuthenticationBuilder options validation)
/// and checks that a missing client ID or secret is detected before a challenge.
/// </summary>
[TestFixture]
public class ExternalSchemeConfiguredTests
{
    private static HttpContext CreateHttpContext(string clientId, string clientSecret)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(o =>
        {
            o.AddScheme(GoogleDefaults.AuthenticationScheme, b =>
            {
                b.DisplayName = "Google";
                b.HandlerType = typeof(GoogleHandler);
            });
        });
        services.Configure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, o =>
        {
            o.ClientId = clientId;
            o.ClientSecret = clientSecret;
        });
        services.AddSingleton<IPostConfigureOptions<GoogleOptions>, OAuthPostConfigureOptions<GoogleOptions, GoogleHandler>>();

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [TestCase(null, null)]
    [TestCase("client-id", null)]
    [TestCase(null, "client-secret")]
    [TestCase("", "")]
    public async Task Incomplete_credentials_are_not_configured(string clientId, string clientSecret)
    {
        var httpContext = CreateHttpContext(clientId, clientSecret);
        var handlerProvider = httpContext.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();

        Assert.That(await handlerProvider.IsSchemeConfiguredAsync(httpContext, GoogleDefaults.AuthenticationScheme), Is.False);
    }

    [Test]
    public async Task Complete_credentials_are_configured()
    {
        var httpContext = CreateHttpContext("client-id.apps.googleusercontent.com", "client-secret");
        var handlerProvider = httpContext.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();

        Assert.That(await handlerProvider.IsSchemeConfiguredAsync(httpContext, GoogleDefaults.AuthenticationScheme), Is.True);
    }

    [Test]
    public async Task Unknown_scheme_is_not_configured()
    {
        var httpContext = CreateHttpContext("client-id", "client-secret");
        var handlerProvider = httpContext.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();

        Assert.That(await handlerProvider.IsSchemeConfiguredAsync(httpContext, "Unknown"), Is.False);
    }
}
