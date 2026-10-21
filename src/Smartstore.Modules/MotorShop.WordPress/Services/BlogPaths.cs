namespace MotorShop.WordPress.Services;

/// <summary>
/// Storefront paths of the blog. The route templates in <see cref="Startup"/> use the same segments.
/// </summary>
public static class BlogPaths
{
    public const string Blog = "blog";
    public const string Guides = "rehberler";
    public const string Category = "kategori";
    public const string Tag = "etiket";
    public const string Page = "sayfa";

    /// <summary>
    /// First URL segments owned by this module. Entity slugs (products, categories, ...) with these values would be hidden.
    /// </summary>
    public static readonly string[] ReservedSlugs = [Blog, Guides];

    public static string ForPost(BlogPost post) => post.Section switch
    {
        BlogSection.Guides => ForGuide(post.Slug),
        BlogSection.Page => ForPage(post.Slug),
        _ => ForBlogPost(post.Slug)
    };

    public static string ForBlogPost(string slug) => $"/{Blog}/{Escape(slug)}";
    public static string ForGuide(string slug) => $"/{Guides}/{Escape(slug)}";
    public static string ForPage(string slug) => $"/{Blog}/{Page}/{Escape(slug)}";
    public static string ForCategory(string slug) => $"/{Blog}/{Category}/{Escape(slug)}";
    public static string ForTag(string slug) => $"/{Blog}/{Tag}/{Escape(slug)}";

    /// <summary>
    /// Maps a link to the WordPress site (permalink) to the MOTOR path. Media files and admin URLs are kept.
    /// </summary>
    /// <returns>A root relative path, or <c>null</c> to keep the link.</returns>
    public static string? RewriteWordPressLink(Uri link, string? guideCategorySlug)
    {
        Guard.NotNull(link);

        var path = link.AbsolutePath;
        if (path.StartsWith("/wp-", StringComparison.OrdinalIgnoreCase) || link.Query.HasValue())
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(BlogPostMapper.DecodeSlug).ToArray();
        string? result = segments switch
        {
            [] => "/" + Blog,
            ["category", .., var c] when guideCategorySlug.HasValue() && c.EqualsNoCase(guideCategorySlug) => "/" + Guides,
            ["category", .., var c] => ForCategory(c),
            ["tag", var t] => ForTag(t),
            [.., var last] when BlogPostMapper.IsValidSlug(last) => ForBlogPost(last),
            _ => null
        };

        return result == null ? null : result + link.Fragment;
    }

    private static string Escape(string slug) => Uri.EscapeDataString(slug);
}
