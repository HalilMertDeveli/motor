using System.Globalization;
using System.Text.RegularExpressions;
using MotorShop.WordPress.Client;

namespace MotorShop.WordPress.Services;

/// <summary>
/// Turns REST API objects into <see cref="BlogPost"/>s. Rejects anything that is not published or is password protected.
/// </summary>
public partial class BlogPostMapper
{
    const int WordsPerMinute = 200;

    // Prefers a size that fits a card (about 768px wide) over the full image.
    static readonly string[] CardSizes = ["medium_large", "large", "post-thumbnail", "medium"];

    private readonly WordPressHtmlSanitizer _sanitizer;

    public BlogPostMapper(WordPressHtmlSanitizer sanitizer)
    {
        _sanitizer = sanitizer;
    }

    /// <param name="includeContent"><c>false</c> for list results: the content is not sanitized and not kept.</param>
    /// <returns><c>null</c> when the item must not be shown.</returns>
    public BlogPost? Map(WpPost? wp, WordPressOptions options, Uri? wordPressBase, bool includeContent, Func<Uri, string?>? rewriteLink = null)
    {
        Guard.NotNull(options);

        if (wp == null || wp.Id <= 0 || !wp.Slug.HasValue())
        {
            return null;
        }

        // Only "publish" is requested, but a WordPress plugin or a misconfigured auth user must not be able to leak drafts.
        if (!"publish".EqualsNoCase(wp.Status) || wp.Content?.Protected == true)
        {
            return null;
        }

        var terms = wp.Embedded?.Terms?.SelectMany(x => x ?? []).Where(x => x != null).ToList() ?? [];
        var categories = terms.Where(x => "category".EqualsNoCase(x.Taxonomy)).Select(ToTerm).ToList();
        var allTags = terms.Where(x => "post_tag".EqualsNoCase(x.Taxonomy)).ToList();

        var prefix = options.ProductTagPrefix;
        var productSkus = prefix.HasValue()
            ? allTags.Where(t => t.Slug?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true && t.Slug.Length > prefix.Length)
                .Select(t => t.Slug![prefix.Length..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        // SKU tags are links, not topics: they are not shown as tags.
        var tags = allTags.Where(t => !(prefix.HasValue() && t.Slug?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)).Select(ToTerm).ToList();

        var isGuide = options.GuideCategorySlug.HasValue() && categories.Any(c => c.Slug.EqualsNoCase(options.GuideCategorySlug));
        var section = "page".EqualsNoCase(wp.Type) ? BlogSection.Page : isGuide ? BlogSection.Guides : BlogSection.Blog;

        var title = WordPressHtmlSanitizer.ToPlainText(wp.Title?.Rendered);
        var excerpt = WordPressHtmlSanitizer.ToPlainText(wp.Excerpt?.Rendered);
        var content = includeContent ? _sanitizer.Sanitize(wp.Content?.Rendered, wordPressBase, options.AllowedEmbedHosts ?? [], rewriteLink) : string.Empty;
        var words = CountWords(WordPressHtmlSanitizer.ToPlainText(wp.Content?.Rendered));

        var published = AsUtc(wp.DateGmt);

        return new BlogPost
        {
            Id = wp.Id,
            // WordPress percent encodes non ASCII slugs ("bak%c4%b1m"); routes and URL generation work with the decoded form.
            Slug = DecodeSlug(wp.Slug),
            Section = section,
            Title = title.HasValue() ? title : wp.Slug,
            Excerpt = excerpt.Truncate(320, "…"),
            ContentHtml = content,
            PublishedUtc = published,
            ModifiedUtc = wp.ModifiedGmt.HasValue ? AsUtc(wp.ModifiedGmt) : published,
            AuthorName = wp.Embedded?.Author?.FirstOrDefault()?.Name,
            Image = MapImage(wp.Embedded?.FeaturedMedia?.FirstOrDefault()),
            Categories = categories,
            Tags = tags,
            ProductSkus = productSkus,
            MetaTitle = WordPressHtmlSanitizer.ToPlainText(wp.YoastHead?.Title).NullEmpty(),
            MetaDescription = WordPressHtmlSanitizer.ToPlainText(wp.YoastHead?.Description ?? wp.YoastHead?.OgDescription).NullEmpty(),
            ReadingMinutes = Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute))
        };
    }

    public static BlogTerm ToTerm(WpTerm term) => new()
    {
        Id = term.Id,
        Name = WordPressHtmlSanitizer.ToPlainText(term.Name),
        Slug = DecodeSlug(term.Slug),
        Description = WordPressHtmlSanitizer.ToPlainText(term.Description),
        Count = term.Count
    };

    public static BlogImage? MapImage(WpMedia? media)
    {
        if (media == null || !IsHttpUrl(media.SourceUrl) || (media.MediaType.HasValue() && !"image".EqualsNoCase(media.MediaType)))
        {
            return null;
        }

        var sizes = media.MediaDetails?.Sizes?
            .Where(x => x.Value != null && IsHttpUrl(x.Value.SourceUrl) && x.Value.Width > 0)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            ?? [];

        var srcset = string.Join(", ", sizes.Values
            .Where(x => !x.SourceUrl!.Contains(' ') && !x.SourceUrl.Contains(','))
            .GroupBy(x => x.Width)
            .Select(g => g.First())
            .OrderBy(x => x.Width)
            .Select(x => $"{x.SourceUrl} {x.Width.ToString(CultureInfo.InvariantCulture)}w"));

        var card = CardSizes.Select(n => sizes.GetValueOrDefault(n)).FirstOrDefault(x => x != null);

        return new BlogImage
        {
            Url = media.SourceUrl!,
            Alt = WordPressHtmlSanitizer.ToPlainText(media.AltText),
            Width = media.MediaDetails?.Width ?? 0,
            Height = media.MediaDetails?.Height ?? 0,
            SrcSet = srcset,
            CardUrl = card?.SourceUrl ?? media.SourceUrl!
        };
    }

    /// <summary>
    /// <c>true</c> for a valid lower case WordPress slug (letters, digits, "-", "_" and percent encoded UTF-8).
    /// </summary>
    public static bool IsValidSlug(string? slug)
        => slug.HasValue() && slug.Length <= 200 && SlugRegex().IsMatch(slug);

    public static string DecodeSlug(string? slug)
        => slug.HasValue() ? Uri.UnescapeDataString(slug).ToLowerInvariant() : string.Empty;

    private static bool IsHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    private static DateTime AsUtc(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : DateTime.MinValue;

    private static int CountWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"^[\p{Ll}\p{Lo}\p{Nd}_%-]+$")]
    private static partial Regex SlugRegex();
}
