using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Smartstore.Engine.Modularity;

namespace Smartstore.Core.Identity;

public static class ExternalAuthenticationExtensions
{
    public static bool IsMethodActive(this Provider<IExternalAuthenticationMethod> method, ExternalAuthenticationSettings settings)
    {
        Guard.NotNull(method, nameof(method));

        return method.ToLazy().IsMethodActive(settings);
    }

    public static bool IsMethodActive(this Lazy<IExternalAuthenticationMethod, ProviderMetadata> method, ExternalAuthenticationSettings settings)
    {
        Guard.NotNull(method, nameof(method));
        Guard.NotNull(settings, nameof(settings));

        if (settings.ActiveAuthenticationMethodSystemNames == null)
        {
            return false;
        }

        return settings.ActiveAuthenticationMethodSystemNames.Contains(method.Metadata.SystemName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a value indicating whether the module of an external authentication scheme is active.
    /// Authentication modules use the system name "Smartstore.&lt;Scheme&gt;.Auth".
    /// </summary>
    public static bool IsSchemeActive(this IProviderManager providerManager, string scheme, ExternalAuthenticationSettings settings, int storeId = 0)
    {
        Guard.NotNull(providerManager);

        if (scheme.IsEmpty())
        {
            return false;
        }

        var systemName = $"Smartstore.{scheme}.Auth";
        return providerManager.GetAllProviders<IExternalAuthenticationMethod>(storeId)
            .Any(x => x.Metadata.SystemName.EqualsNoCase(systemName) && x.IsMethodActive(settings));
    }

    /// <summary>
    /// Gets a value indicating whether the options of an external authentication scheme are valid (e.g. client ID and secret are set).
    /// Modules register their handlers without the options validation of <c>AuthenticationBuilder</c>,
    /// so a challenge with incomplete options would otherwise end on an error page of the provider.
    /// </summary>
    public static async Task<bool> IsSchemeConfiguredAsync(this IAuthenticationHandlerProvider handlerProvider, HttpContext httpContext, string scheme)
    {
        Guard.NotNull(handlerProvider);
        Guard.NotNull(httpContext);

        try
        {
            var handler = await handlerProvider.GetHandlerAsync(httpContext, scheme);
            if (handler == null)
            {
                return false;
            }

            if (handler.GetType().GetProperty(nameof(AuthenticationHandler<AuthenticationSchemeOptions>.Options))?.GetValue(handler) is AuthenticationSchemeOptions options)
            {
                options.Validate(scheme);
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
