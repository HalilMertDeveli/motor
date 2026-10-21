using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MotorShop.WordPress.Services;

/// <summary>
/// <c>POST /wordpress/webhook/purge</c>: WordPress calls it after a post is published, updated or removed,
/// so the change shows at once instead of after <see cref="WordPressOptions.CacheMinutes"/>.
/// </summary>
/// <remarks>
/// A minimal endpoint outside MVC: no session, no guest customer, no antiforgery token. Authenticated by a shared secret
/// in the <c>X-Motor-WordPress-Secret</c> header (compared in constant time). It can only clear the blog cache.
/// </remarks>
public static class WordPressWebhook
{
    public const string Path = "wordpress/webhook/purge";
    public const string SecretHeader = "X-Motor-WordPress-Secret";
    public const int MinSecretLength = 32;

    public static async Task HandleAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var content = services.GetRequiredService<WordPressContentService>();
        var throttle = services.GetRequiredService<WordPressThrottle>();
        var secret = content.Options.WebhookSecret;

        if (!content.Options.Enabled || secret == null || secret.Length < MinSecretLength)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!throttle.TryAcquire("webhook:" + ip))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        if (!IsValidSecret(context.Request.Headers[SecretHeader], secret))
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WordPressWebhook))
                .LogWarning("WordPress webhook called with a wrong secret from {RemoteIp}.", ip);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await content.ClearCacheAsync();
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public static bool IsValidSecret(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        // Hashing first gives equal lengths, so the comparison time does not reveal the secret length.
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
