using System.Net;
using System.Net.Sockets;

namespace MotorShop.WordPress.Client;

/// <summary>
/// Validates the configured WordPress address and the IP addresses the client connects to (SSRF protection).
/// </summary>
public static class WordPressEndpoint
{
    /// <summary>
    /// Validates <see cref="WordPressOptions.BaseUrl"/>.
    /// </summary>
    /// <param name="error">A message without secrets when the URL is rejected.</param>
    /// <returns>The normalized base URL ending with "/", or <c>null</c>.</returns>
    public static Uri? TryGetBaseUri(WordPressOptions options, out string? error)
    {
        Guard.NotNull(options);
        error = null;

        if (!options.BaseUrl.HasValue())
        {
            error = "No WordPress URL is configured.";
            return null;
        }

        if (!Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            error = "The WordPress URL is not an absolute URL.";
            return null;
        }

        var https = uri.Scheme == Uri.UriSchemeHttps;
        if (!https && !(uri.Scheme == Uri.UriSchemeHttp && options.AllowInsecureHttp))
        {
            error = "The WordPress URL must use https.";
            return null;
        }

        if (uri.UserInfo.HasValue())
        {
            error = "The WordPress URL must not contain credentials. Use UserName and ApplicationPassword.";
            return null;
        }

        if (uri.Query.HasValue() || uri.Fragment.HasValue())
        {
            error = "The WordPress URL must not contain a query or fragment.";
            return null;
        }

        if (!options.AllowPrivateNetwork)
        {
            if (uri.IsLoopback || uri.Host.EqualsNoCase("localhost") || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                error = "The WordPress URL points to this machine.";
                return null;
            }

            if (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && !IsPublicAddress(ip))
            {
                error = "The WordPress URL points to a private network address.";
                return null;
            }
        }

        var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
        return new UriBuilder(uri.Scheme, uri.Host, uri.Port, path).Uri;
    }

    /// <summary>
    /// <c>true</c> for addresses on the public internet. Loopback, private, link local (cloud metadata),
    /// carrier grade NAT, multicast, unspecified and documentation ranges are not public.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        Guard.NotNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                     // fc00::/7 unique local
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)); // 2001:db8::/32 documentation
        }

        return false;
    }
}
