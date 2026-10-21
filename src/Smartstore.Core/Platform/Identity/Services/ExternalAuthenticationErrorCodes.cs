namespace Smartstore.Core.Identity;

/// <summary>
/// Error codes an external authentication module passes to <c>IdentityController.ExternalErrorCallback</c>
/// (query parameter "errorCode"). The storefront maps them to localized messages and never displays provider error texts.
/// </summary>
public static class ExternalAuthenticationErrorCodes
{
    /// <summary>
    /// The customer cancelled the sign-in or declined the consent screen.
    /// </summary>
    public const string AccessDenied = "access_denied";

    /// <summary>
    /// The OAuth state or correlation cookie is missing, invalid or expired (timeout, replayed callback, cookies blocked).
    /// </summary>
    public const string SessionExpired = "session_expired";

    /// <summary>
    /// The provider did not confirm that the customer owns the email address.
    /// </summary>
    public const string EmailNotVerified = "email_not_verified";
}
