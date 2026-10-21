using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorShop.WordPress.Client;
using Smartstore.Caching;

namespace MotorShop.WordPress.Services;

/// <summary>
/// What a blog list shows.
/// </summary>
public sealed class BlogListRequest
{
    public BlogSection Section { get; init; } = BlogSection.Blog;
    public int Page { get; init; } = 1;
    public string? CategorySlug { get; init; }
    public string? TagSlug { get; init; }
    public string? Search { get; init; }
}

/// <summary>
/// Published WordPress content for the storefront, cached.
/// </summary>
/// <remarks>
/// Two cache entries per item: a fresh one (<see cref="WordPressOptions.CacheMinutes"/>) and a fallback copy
/// (<see cref="WordPressOptions.StaleCacheHours"/>) that is served when WordPress fails. When WordPress answers that an item
/// does not exist (deleted, unpublished), its fallback copy is removed, so it disappears from MOTOR too.
/// Search results get no fallback copy, so random searches cannot fill the cache for long.
/// </remarks>
public class WordPressContentService
{
    public const string CacheKeyPrefix = "motorshop.wordpress:";
    const string FreshPrefix = CacheKeyPrefix + "fresh:";
    const string StalePrefix = CacheKeyPrefix + "stale:";
    const int MaxSearchLength = 100;
    const int MaxSitemapPages = 50;

    private readonly IWordPressApiClient _client;
    private readonly ICacheManager _cache;
    private readonly IOptionsMonitor<WordPressOptions> _options;
    private readonly BlogPostMapper _mapper;
    private readonly WordPressThrottle _throttle;
    private readonly WordPressStatus _status;
    private readonly ILogger<WordPressContentService> _logger;

    public WordPressContentService(
        IWordPressApiClient client,
        ICacheManager cache,
        IOptionsMonitor<WordPressOptions> options,
        BlogPostMapper mapper,
        WordPressThrottle throttle,
        WordPressStatus status,
        ILogger<WordPressContentService> logger)
    {
        _client = client;
        _cache = cache;
        _options = options;
        _mapper = mapper;
        _throttle = throttle;
        _status = status;
        _logger = logger;
    }

    public WordPressOptions Options => _options.CurrentValue;

    /// <summary>
    /// <c>true</c> when the integration is on and the URL passes validation.
    /// </summary>
    public bool IsEnabled
        => Options.Enabled && WordPressEndpoint.TryGetBaseUri(Options, out _) != null;

    public int PageSize => Math.Clamp(Options.PageSize, 1, 50);

    /// <param name="visitorKey">Identifies the visitor for rate limiting (IP address). <c>null</c> for internal calls.</param>
    /// <exception cref="WordPressRateLimitedException">The visitor made too many uncached requests.</exception>
    public async Task<BlogPostList> GetPostsAsync(BlogListRequest request, string? visitorKey, CancellationToken cancelToken = default)
    {
        Guard.NotNull(request);

        var page = Math.Max(1, request.Page);
        var search = request.Search?.Trim().Truncate(MaxSearchLength).NullEmpty();

        if (!IsEnabled)
        {
            return BlogPostList.Empty(page, PageSize, false);
        }

        var categories = await GetCategoriesAsync(cancelToken);
        var guideCategory = Options.GuideCategorySlug.HasValue() ? categories.FirstOrDefault(c => c.Slug.EqualsNoCase(Options.GuideCategorySlug)) : null;

        int[]? include = null, exclude = null, tags = null;
        if (request.Section == BlogSection.Guides)
        {
            if (guideCategory == null)
            {
                // No guide category in WordPress (yet): the guides section is empty.
                return BlogPostList.Empty(page, PageSize, true);
            }
            include = [guideCategory.Id];
        }
        else
        {
            if (request.CategorySlug.HasValue())
            {
                var category = categories.FirstOrDefault(c => c.Slug.EqualsNoCase(request.CategorySlug));
                if (category == null)
                {
                    return BlogPostList.Empty(page, PageSize, true);
                }
                include = [category.Id];
            }
            else if (guideCategory != null)
            {
                // Guides have their own section and canonical URL.
                exclude = [guideCategory.Id];
            }

            if (request.TagSlug.HasValue())
            {
                var tag = await GetTagAsync(request.TagSlug, visitorKey, cancelToken);
                if (tag == null)
                {
                    return BlogPostList.Empty(page, PageSize, true);
                }
                tags = [tag.Id];
            }
        }

        var key = $"posts:{request.Section}:{page}:{PageSize}:{request.CategorySlug}:{request.TagSlug}:{Hash(search)}";

        var result = await GetCachedAsync(key, async ct =>
        {
            var wp = await _client.GetPostsAsync(new WpPostQuery
            {
                Page = page,
                PerPage = PageSize,
                Search = search,
                Categories = include,
                CategoriesExclude = exclude,
                Tags = tags
            }, ct);

            return new BlogPostList
            {
                Posts = MapList(wp.Items),
                PageIndex = page,
                PageSize = PageSize,
                TotalCount = wp.Total,
                TotalPages = wp.TotalPages
            };
        }, visitorKey, keepStale: search == null, cancelToken);

        if (!result.Found)
        {
            return BlogPostList.Empty(page, PageSize, false);
        }

        var list = result.Value!;
        return result.IsStale
            ? new BlogPostList { Posts = list.Posts, PageIndex = list.PageIndex, PageSize = list.PageSize, TotalCount = list.TotalCount, TotalPages = list.TotalPages, IsStale = true }
            : list;
    }

    /// <summary>
    /// A post (<see cref="BlogSection.Blog"/>, <see cref="BlogSection.Guides"/>) or page (<see cref="BlogSection.Page"/>) by slug.
    /// The section of the returned post can differ from <paramref name="section"/> for posts: the caller redirects to the canonical URL.
    /// </summary>
    /// <exception cref="WordPressRateLimitedException">The visitor made too many uncached requests.</exception>
    public async Task<BlogPostResult> GetPostAsync(string slug, BlogSection section, string? visitorKey, CancellationToken cancelToken = default)
    {
        // "/blog/Zincir-Bakimi" finds the post; the controller redirects to the lower case URL.
        slug = slug?.ToLowerInvariant()!;

        if (!IsEnabled || !BlogPostMapper.IsValidSlug(slug))
        {
            return new BlogPostResult { IsAvailable = IsEnabled };
        }
        var isPage = section == BlogSection.Page;
        var key = $"{(isPage ? "page" : "post")}:{slug}";

        var result = await GetCachedAsync(key, async ct =>
        {
            var query = new WpPostQuery { Slug = slug, PerPage = 1 };
            var wp = isPage ? await _client.GetPagesAsync(query, ct) : await _client.GetPostsAsync(query, ct);
            var baseUri = WordPressEndpoint.TryGetBaseUri(Options, out _);

            return _mapper.Map(wp.Items.FirstOrDefault(), Options, baseUri, includeContent: true, link => BlogPaths.RewriteWordPressLink(link, Options.GuideCategorySlug));
        }, visitorKey, keepStale: true, cancelToken);

        return new BlogPostResult { Post = result.Value, IsAvailable = result.Found };
    }

    /// <summary>
    /// Latest posts of the same section, preferring the first category of <paramref name="post"/>.
    /// Never throws: related posts are optional.
    /// </summary>
    public async Task<IReadOnlyList<BlogPost>> GetRelatedAsync(BlogPost post, int count, CancellationToken cancelToken = default)
    {
        Guard.NotNull(post);

        if (!IsEnabled || post.Section == BlogSection.Page || count <= 0)
        {
            return [];
        }

        var categoryId = post.Categories.FirstOrDefault(c => !c.Slug.EqualsNoCase(Options.GuideCategorySlug))?.Id
            ?? post.Categories.FirstOrDefault()?.Id;

        try
        {
            var result = await GetCachedAsync($"related:{post.Id}:{categoryId}:{count}", async ct =>
            {
                var wp = await _client.GetPostsAsync(new WpPostQuery
                {
                    PerPage = count,
                    Categories = categoryId.HasValue ? [categoryId.Value] : null,
                    Exclude = [post.Id]
                }, ct);

                var related = MapList(wp.Items).ToList();

                // Too few in the same category: fill up with the latest posts.
                if (categoryId.HasValue && related.Count < count)
                {
                    var latest = await _client.GetPostsAsync(new WpPostQuery
                    {
                        PerPage = count,
                        Exclude = [post.Id, .. related.Select(x => x.Id)]
                    }, ct);

                    related.AddRange(MapList(latest.Items).Take(count - related.Count));
                }

                return (IReadOnlyList<BlogPost>)related;
            }, null, keepStale: true, cancelToken);

            return result.Value ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Related posts for {PostId} could not be loaded.", post.Id);
            return [];
        }
    }

    public async Task<IReadOnlyList<BlogTerm>> GetCategoriesAsync(CancellationToken cancelToken = default)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var result = await GetCachedAsync("categories", async ct =>
        {
            var terms = await _client.GetCategoriesAsync(ct);
            return (IReadOnlyList<BlogTerm>)terms.Where(x => x.Slug.HasValue() && !"uncategorized".EqualsNoCase(x.Slug)).Select(BlogPostMapper.ToTerm).ToList();
        }, null, keepStale: true, cancelToken);

        return result.Value ?? [];
    }

    public async Task<BlogTerm?> GetCategoryAsync(string slug, CancellationToken cancelToken = default)
    {
        if (!BlogPostMapper.IsValidSlug(slug))
        {
            return null;
        }

        var categories = await GetCategoriesAsync(cancelToken);
        return categories.FirstOrDefault(c => c.Slug.EqualsNoCase(slug));
    }

    /// <exception cref="WordPressRateLimitedException">The visitor made too many uncached requests.</exception>
    public async Task<BlogTerm?> GetTagAsync(string slug, string? visitorKey, CancellationToken cancelToken = default)
    {
        if (!IsEnabled || !BlogPostMapper.IsValidSlug(slug))
        {
            return null;
        }

        slug = slug.ToLowerInvariant();
        var result = await GetCachedAsync($"tag:{slug}", async ct =>
        {
            var tags = await _client.GetTagsAsync(slug: slug, cancelToken: ct);
            var tag = tags.FirstOrDefault();
            return tag == null ? null : BlogPostMapper.ToTerm(tag);
        }, visitorKey, keepStale: true, cancelToken);

        return result.Value;
    }

    /// <summary>
    /// All published posts and pages, for the XML sitemap. Not cached; uses the fallback copy when WordPress fails.
    /// </summary>
    public async IAsyncEnumerable<BlogPost> EnumerateAllAsync([EnumeratorCancellation] CancellationToken cancelToken = default)
    {
        if (!IsEnabled)
        {
            yield break;
        }

        foreach (var collection in new[] { "posts", "pages" })
        {
            for (var page = 1; page <= MaxSitemapPages; page++)
            {
                var result = await GetCachedAsync($"sitemap:{collection}:{page}", async ct =>
                {
                    var query = new WpPostQuery { Page = page, PerPage = 100 };
                    var wp = collection == "pages" ? await _client.GetPagesAsync(query, ct) : await _client.GetPostsAsync(query, ct);
                    return new BlogPostList { Posts = MapList(wp.Items), PageIndex = page, TotalPages = wp.TotalPages, TotalCount = wp.Total };
                }, null, keepStale: true, cancelToken);

                if (result.Value == null)
                {
                    break;
                }

                foreach (var post in result.Value.Posts)
                {
                    yield return post;
                }

                if (page >= result.Value.TotalPages)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Removes the fresh cache entries, so that the next request loads current content.
    /// The fallback copies stay, so MOTOR can still show content if WordPress is down right now.
    /// </summary>
    public async Task<long> ClearCacheAsync()
    {
        var removed = await _cache.RemoveByPatternAsync(FreshPrefix + "*");
        _status.RecordCacheClear();
        _logger.LogInformation("WordPress content cache cleared ({Count} entries).", removed);
        return removed;
    }

    private IReadOnlyList<BlogPost> MapList(IEnumerable<WpPost> items)
    {
        var baseUri = WordPressEndpoint.TryGetBaseUri(Options, out _);
        return items.Select(x => _mapper.Map(x, Options, baseUri, includeContent: false)).Where(x => x != null).ToList()!;
    }

    private async Task<CacheResult<T>> GetCachedAsync<T>(string key, Func<CancellationToken, Task<T?>> fetch, string? visitorKey, bool keepStale, CancellationToken cancelToken)
        where T : class
    {
        var freshKey = FreshPrefix + key;
        var staleKey = StalePrefix + key;

        var fresh = await _cache.TryGetAsync<CachedValue<T>>(freshKey);
        if (fresh.Success && fresh.Value != null)
        {
            return new CacheResult<T>(fresh.Value.Value, true, false);
        }

        if (visitorKey != null && !_throttle.TryAcquire(visitorKey))
        {
            var staleCopy = keepStale ? await _cache.GetAsync<CachedValue<T>>(staleKey) : null;
            if (staleCopy != null)
            {
                return new CacheResult<T>(staleCopy.Value, true, true);
            }

            throw new WordPressRateLimitedException();
        }

        var options = Options;
        try
        {
            var value = await fetch(cancelToken);
            var entry = new CachedValue<T>(value);

            await _cache.PutAsync(freshKey, entry, new CacheEntryOptions().ExpiresIn(TimeSpan.FromMinutes(Math.Max(1, options.CacheMinutes))));

            if (keepStale)
            {
                if (value != null)
                {
                    await _cache.PutAsync(staleKey, entry, new CacheEntryOptions().ExpiresIn(TimeSpan.FromHours(Math.Max(1, options.StaleCacheHours))));
                }
                else
                {
                    // Deleted or unpublished in WordPress.
                    await _cache.RemoveAsync(staleKey);
                }
            }

            return new CacheResult<T>(value, true, false);
        }
        catch (WordPressApiException)
        {
            // Already logged and recorded by the client.
            var staleCopy = keepStale ? await _cache.GetAsync<CachedValue<T>>(staleKey) : null;
            if (staleCopy != null)
            {
                // Keeps pages fast while WordPress is down; the circuit breaker decides when to try again.
                await _cache.PutAsync(freshKey, staleCopy, new CacheEntryOptions().ExpiresIn(TimeSpan.FromMinutes(1)));
                return new CacheResult<T>(staleCopy.Value, true, true);
            }

            return new CacheResult<T>(null, false, false);
        }
    }

    private static string Hash(string? value)
        => value == null ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant())))[..16];

    /// <summary>
    /// Cache box, so that "does not exist" can be cached too.
    /// </summary>
    public sealed record CachedValue<T>(T? Value) where T : class;

    private readonly record struct CacheResult<T>(T? Value, bool Found, bool IsStale) where T : class;
}
