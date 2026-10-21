namespace MotorShop.WordPress;

/// <summary>
/// Settings bound from the "MotorShop:WordPress" configuration section
/// (appsettings.json, Config/usersettings.json or environment variables like "MotorShop__WordPress__BaseUrl").
/// </summary>
/// <remarks>
/// Deliberately not editable in the admin UI: the URL decides which host the server calls (SSRF),
/// and the application password must stay out of the database.
/// </remarks>
public class WordPressOptions
{
    public const string SectionName = "MotorShop:WordPress";

    /// <summary>
    /// Turns the integration on. When off, blog routes return 404 and no request is sent to WordPress.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Root of the WordPress site, e.g. <c>https://cms.example.com</c>. The REST API is expected at <c>{BaseUrl}/wp-json/</c>.
    /// Must be https unless <see cref="AllowInsecureHttp"/> is set.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Link to the WordPress dashboard shown in the MOTOR admin. Defaults to <c>{BaseUrl}/wp-admin/</c>.
    /// The link is only a link: there is no single sign-on.
    /// </summary>
    public string? AdminUrl { get; set; }

    /// <summary>
    /// Optional WordPress user for sites that require authentication for the REST API.
    /// Use a low privileged account with an application password, never an administrator password.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// WordPress application password of <see cref="UserName"/>. Set it as a secret (environment variable), never in a file in the repository.
    /// </summary>
    public string? ApplicationPassword { get; set; }

    /// <summary>
    /// Time limit of one request attempt, in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// How long fetched content is served from the cache before WordPress is asked again, in minutes.
    /// </summary>
    public int CacheMinutes { get; set; } = 10;

    /// <summary>
    /// How long the last successful response is kept as a fallback for when WordPress is down, in hours.
    /// </summary>
    public int StaleCacheHours { get; set; } = 48;

    /// <summary>
    /// Posts per list page.
    /// </summary>
    public int PageSize { get; set; } = 12;

    /// <summary>
    /// Slug of the WordPress category whose posts are shown under /rehberler instead of /blog.
    /// </summary>
    public string GuideCategorySlug { get; set; } = "rehberler";

    /// <summary>
    /// WordPress tags starting with this prefix link products: the rest of the tag slug is a product SKU,
    /// e.g. the tag "sku-pr-1001" links the product with SKU "PR-1001".
    /// </summary>
    public string ProductTagPrefix { get; set; } = "sku-";

    /// <summary>
    /// Hosts allowed as iframe sources in post content (video embeds). Every other iframe is removed.
    /// </summary>
    public string[] AllowedEmbedHosts { get; set; } = ["www.youtube.com", "www.youtube-nocookie.com", "player.vimeo.com"];

    /// <summary>
    /// Shared secret for the cache purge webhook (<c>POST /wordpress/webhook/purge</c>, header <c>X-Motor-WordPress-Secret</c>).
    /// Empty disables the webhook. At least 32 characters.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Largest accepted response body, in kilobytes.
    /// </summary>
    public int MaxResponseKilobytes { get; set; } = 4096;

    /// <summary>
    /// Development only: allows http:// base URLs.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// Development only: allows WordPress on loopback or private network addresses (e.g. a local Docker container).
    /// Off in production, so that the server cannot be pointed at internal services.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }
}
