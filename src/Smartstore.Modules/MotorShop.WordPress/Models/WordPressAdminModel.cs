using MotorShop.WordPress.Client;
using MotorShop.WordPress.Services;

namespace MotorShop.WordPress.Models;

public class WordPressAdminModel
{
    public bool Enabled { get; set; }
    /// <summary>Scheme and host only.</summary>
    public string? Host { get; set; }
    public string? ConfigurationError { get; set; }
    public bool UsesApplicationPassword { get; set; }
    public bool WebhookEnabled { get; set; }
    public string? AdminUrl { get; set; }
    public int CacheMinutes { get; set; }
    public string GuideCategorySlug { get; set; } = string.Empty;
    public string ProductTagPrefix { get; set; } = string.Empty;

    /// <summary><c>null</c> when no check was made (not configured).</summary>
    public bool? Connected { get; set; }
    public string? ConnectionError { get; set; }
    public long ResponseMilliseconds { get; set; }
    public int PublishedPosts { get; set; }
    public int PublishedPages { get; set; }
    public int Categories { get; set; }
    public bool GuideCategoryExists { get; set; }
    public IReadOnlyList<BlogPost> RecentPosts { get; set; } = [];

    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public DateTime? LastCacheClearUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public IReadOnlyList<WordPressError> RecentErrors { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
    public bool CanUpdate { get; set; }
}
