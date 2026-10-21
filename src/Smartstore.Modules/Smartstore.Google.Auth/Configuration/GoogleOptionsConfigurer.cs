using System.Diagnostics;
using Autofac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Smartstore.Core.Identity;
using Smartstore.Engine;
using Smartstore.Google.Auth;

namespace Smartstore.Google;

internal sealed class GoogleOptionsConfigurer : IConfigureOptions<AuthenticationOptions>, IConfigureNamedOptions<GoogleOptions>
{
    private readonly IApplicationContext _appContext;

    public GoogleOptionsConfigurer(IApplicationContext appContext)
    {
        _appContext = appContext;
    }

    public void Configure(AuthenticationOptions options)
    {
        // Register the OpenID Connect client handler in the authentication handlers collection.
        options.AddScheme(GoogleDefaults.AuthenticationScheme, builder =>
        {
            builder.DisplayName = "Google";
            builder.HandlerType = typeof(GoogleHandler);
        });
    }

    public void Configure(string name, GoogleOptions options)
    {
        // Ignore OpenID Connect client handler instances that don't correspond to the instance managed by the OpenID module.
        if (name.HasValue() && !string.Equals(name, GoogleDefaults.AuthenticationScheme))
        {
            return;
        }

        var settings = _appContext.Services.Resolve<GoogleExternalAuthSettings>();
        options.ClientId = settings.ConsumerKey?.Trim();
        options.ClientSecret = settings.ConsumerSecret?.Trim();

        // GoogleOptions requests the minimal scopes "openid", "profile" and "email" by default. Tokens are not needed after sign-in.
        options.SaveTokens = false;

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context =>
            {
                // Only a verified address proves that the Google account owns it.
                if (!GoogleAuthHelper.IsEmailVerified(context.User))
                {
                    throw new GoogleEmailNotVerifiedException();
                }

                return Task.CompletedTask;
            },
            OnAccessDenied = context =>
            {
                // The customer cancelled on the Google account chooser or consent screen.
                context.Response.Redirect(GoogleAuthHelper.BuildErrorUrl(context.Request.PathBase.Value, ExternalAuthenticationErrorCodes.AccessDenied));
                context.HandleResponse();
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                var errorCode = GoogleAuthHelper.GetErrorCode(context.Failure);

                // Log the failure on the server only. Its message never contains tokens or the client secret,
                // and it is not passed to the browser.
                _appContext.Logger.Warn("Google sign-in failed ({ErrorCode}): {Message}",
                    errorCode ?? "provider_error",
                    context.Failure?.Message.Truncate(500));

                context.Response.Redirect(GoogleAuthHelper.BuildErrorUrl(context.Request.PathBase.Value, errorCode));
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    }

    public void Configure(GoogleOptions options)
        => Debug.Fail("This infrastructure method shouldn't be called.");
}