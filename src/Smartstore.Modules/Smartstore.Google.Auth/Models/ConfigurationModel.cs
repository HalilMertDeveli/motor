namespace Smartstore.Google.Auth.Models;

[LocalizedDisplay("Plugins.Smartstore.Google.Auth.")]
public class ConfigurationModel : ModelBase
{
    [LocalizedDisplay("*ConsumerKey")]
    public string ConsumerKey { get; set; }

    [LocalizedDisplay("*ConsumerSecret")]
    public string ConsumerSecret { get; set; }

    [LocalizedDisplay("*RedirectUri")]
    public string RedirectUrl { get; set; }

    /// <summary>
    /// Redirect URI built from the current request, if it differs from <see cref="RedirectUrl"/> (e.g. another host name).
    /// </summary>
    public string RequestRedirectUrl { get; set; }

    public bool IsConfigured { get; set; }
}