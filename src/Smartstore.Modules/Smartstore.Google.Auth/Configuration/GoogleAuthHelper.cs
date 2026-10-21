using System;
using System.Text.Json;
using Smartstore.Core.Identity;

namespace Smartstore.Google.Auth;

/// <summary>
/// Thrown while creating the authentication ticket when Google does not confirm the email address.
/// The remote authentication handler reports it to <c>OnRemoteFailure</c>.
/// </summary>
public sealed class GoogleEmailNotVerifiedException : Exception
{
    public GoogleEmailNotVerifiedException()
        : base("Google did not confirm the email address of the account.")
    {
    }
}

public static class GoogleAuthHelper
{
    /// <summary>
    /// Gets a value indicating whether the Google user info response confirms the email address ("email_verified": true).
    /// </summary>
    public static bool IsEmailVerified(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object || !user.TryGetProperty("email_verified", out var verified))
        {
            return false;
        }

        return verified.ValueKind == JsonValueKind.True
            || (verified.ValueKind == JsonValueKind.String && string.Equals(verified.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps a remote authentication failure to an <see cref="ExternalAuthenticationErrorCodes"/> value.
    /// </summary>
    /// <returns>The error code or <c>null</c> for configuration and provider errors.</returns>
    public static string GetErrorCode(Exception failure)
    {
        if (failure == null)
        {
            return null;
        }

        if (failure is GoogleEmailNotVerifiedException || failure.InnerException is GoogleEmailNotVerifiedException)
        {
            return ExternalAuthenticationErrorCodes.EmailNotVerified;
        }

        // Messages of Microsoft.AspNetCore.Authentication.RemoteAuthenticationHandler and OAuthHandler.
        var message = failure.Message.EmptyNull();
        if (message.Contains("Correlation failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("state was missing or invalid", StringComparison.OrdinalIgnoreCase))
        {
            return ExternalAuthenticationErrorCodes.SessionExpired;
        }

        return null;
    }

    /// <summary>
    /// Builds the storefront URL that displays a localized error message for a failed Google sign-in.
    /// </summary>
    public static string BuildErrorUrl(string pathBase, string errorCode)
    {
        var url = pathBase.EmptyNull().TrimEnd('/') + "/identity/externalerrorcallback?provider=google";
        if (errorCode.HasValue())
        {
            url += "&errorCode=" + Uri.EscapeDataString(errorCode);
        }

        return url;
    }
}
