namespace MotorShop.WordPress.Services;

public enum BlogSection
{
    /// <summary>/blog</summary>
    Blog,
    /// <summary>/rehberler</summary>
    Guides,
    /// <summary>/blog/sayfa/{slug} (WordPress pages)</summary>
    Page
}

/// <summary>
/// A published WordPress post or page, already sanitized and ready to render.
/// Kept in the cache, so it holds plain data only.
/// </summary>
public sealed class BlogPost
{
    public int Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public BlogSection Section { get; init; }
    public string Title { get; init; } = string.Empty;
    /// <summary>Plain text.</summary>
    public string Excerpt { get; init; } = string.Empty;
    /// <summary>Sanitized HTML. Empty in list results.</summary>
    public string ContentHtml { get; init; } = string.Empty;
    public DateTime PublishedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string? AuthorName { get; init; }
    public BlogImage? Image { get; init; }
    public IReadOnlyList<BlogTerm> Categories { get; init; } = [];
    public IReadOnlyList<BlogTerm> Tags { get; init; } = [];
    /// <summary>SKUs from tags with <see cref="WordPressOptions.ProductTagPrefix"/>.</summary>
    public IReadOnlyList<string> ProductSkus { get; init; } = [];
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public int ReadingMinutes { get; init; }
}

public sealed class BlogImage
{
    public string Url { get; init; } = string.Empty;
    public string Alt { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>"url 300w, url 768w, ..." or empty.</summary>
    public string SrcSet { get; init; } = string.Empty;
    /// <summary>A smaller version for cards, or <see cref="Url"/>.</summary>
    public string CardUrl { get; init; } = string.Empty;
}

public sealed class BlogTerm
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class BlogPostList
{
    public static BlogPostList Empty(int page, int pageSize, bool available) => new() { PageIndex = page, PageSize = pageSize, IsAvailable = available };

    public IReadOnlyList<BlogPost> Posts { get; init; } = [];
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    /// <summary><c>false</c> when WordPress could not be reached and nothing was cached.</summary>
    public bool IsAvailable { get; init; } = true;
    /// <summary><c>true</c> when the result is an older copy served because WordPress failed.</summary>
    public bool IsStale { get; init; }
}

/// <summary>
/// Result of a single item lookup. <see cref="Post"/> is <c>null</c> when it does not exist or is not published.
/// </summary>
public sealed class BlogPostResult
{
    public BlogPost? Post { get; init; }
    public bool IsAvailable { get; init; } = true;
}
