using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MotorShop.WordPress.Models;
using MotorShop.WordPress.Services;
using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.Data;
using Smartstore.Core.Localization.Routing;
using Smartstore.Core.Stores;
using Smartstore.Web.Controllers;
using Smartstore.Web.Models.Catalog;
using Smartstore.Web.Models.Common;

namespace MotorShop.WordPress.Controllers;

/// <summary>
/// Server side rendered blog. WordPress is only asked on cache misses; when it fails, cached copies are shown
/// or a 503 page for the blog alone. Nothing here touches catalog, cart or checkout state.
/// </summary>
public class MotorWordPressController : PublicController
{
    const int RelatedCount = 3;
    const int MaxProducts = 8;

    private readonly WordPressContentService _content;
    private readonly IBreadcrumb _breadcrumb;
    private readonly SmartDbContext _db;
    private readonly ICatalogSearchService _catalogSearchService;
    private readonly CatalogHelper _catalogHelper;
    private readonly SocialSettings _socialSettings;

    public MotorWordPressController(
        WordPressContentService content,
        IBreadcrumb breadcrumb,
        SmartDbContext db,
        ICatalogSearchService catalogSearchService,
        CatalogHelper catalogHelper,
        SocialSettings socialSettings)
    {
        _content = content;
        _breadcrumb = breadcrumb;
        _db = db;
        _catalogSearchService = catalogSearchService;
        _catalogHelper = catalogHelper;
        _socialSettings = socialSettings;
    }

    [LocalizedRoute("/blog", Name = RouteNames.Blog)]
    public Task<IActionResult> Index(int page = 1, string? q = null)
        => ListAsync(new BlogListRequest { Section = BlogSection.Blog, Page = page, Search = q }, RouteNames.Blog, null);

    [LocalizedRoute("/blog/kategori/{slug}", Name = RouteNames.Category)]
    public async Task<IActionResult> Category(string slug, int page = 1)
    {
        if (!_content.IsEnabled)
        {
            return NotFound();
        }

        var category = await _content.GetCategoryAsync(slug, HttpContext.RequestAborted);
        if (category == null)
        {
            return NotFound();
        }

        if (category.Slug.EqualsNoCase(_content.Options.GuideCategorySlug))
        {
            return RedirectToRoutePermanent(RouteNames.Guides);
        }

        return await ListAsync(new BlogListRequest { Section = BlogSection.Blog, Page = page, CategorySlug = category.Slug }, RouteNames.Category, category);
    }

    [LocalizedRoute("/blog/etiket/{slug}", Name = RouteNames.Tag)]
    public async Task<IActionResult> Tag(string slug, int page = 1)
    {
        if (!_content.IsEnabled)
        {
            return NotFound();
        }

        BlogTerm? tag;
        try
        {
            tag = await _content.GetTagAsync(slug, VisitorKey, HttpContext.RequestAborted);
        }
        catch (WordPressRateLimitedException)
        {
            return TooManyRequests();
        }

        if (tag == null)
        {
            return NotFound();
        }

        return await ListAsync(new BlogListRequest { Section = BlogSection.Blog, Page = page, TagSlug = tag.Slug }, RouteNames.Tag, tag);
    }

    [LocalizedRoute("/rehberler", Name = RouteNames.Guides)]
    public Task<IActionResult> Guides(int page = 1)
        => ListAsync(new BlogListRequest { Section = BlogSection.Guides, Page = page }, RouteNames.Guides, null);

    [LocalizedRoute("/blog/{slug}", Name = RouteNames.Post)]
    public Task<IActionResult> Post(string slug)
        => DetailAsync(slug, BlogSection.Blog);

    [LocalizedRoute("/rehberler/{slug}", Name = RouteNames.Guide)]
    public Task<IActionResult> Guide(string slug)
        => DetailAsync(slug, BlogSection.Guides);

    [LocalizedRoute("/blog/sayfa/{slug}", Name = RouteNames.Page)]
    public Task<IActionResult> Page(string slug)
        => DetailAsync(slug, BlogSection.Page);

    private string VisitorKey
        => Services.WebHelper.ClientInfo?.IpAddress?.ToString() ?? "unknown";

    private async Task<IActionResult> ListAsync(BlogListRequest request, string routeName, BlogTerm? term)
    {
        if (!_content.IsEnabled)
        {
            return NotFound();
        }

        if (request.Page < 1)
        {
            return RedirectToRoutePermanent(routeName, term != null ? new { slug = term.Slug } : null);
        }

        BlogPostList result;
        try
        {
            // Rate limited like detail pages: random page numbers or searches would all miss the cache.
            result = await _content.GetPostsAsync(request, VisitorKey, HttpContext.RequestAborted);
        }
        catch (WordPressRateLimitedException)
        {
            return TooManyRequests();
        }

        if (!result.IsAvailable)
        {
            return Unavailable();
        }

        // A page behind the last one.
        if (request.Page > 1 && result.Posts.Count == 0)
        {
            return NotFound();
        }

        var isGuides = request.Section == BlogSection.Guides;
        var sectionTitle = T(isGuides ? "Plugins.MotorShop.WordPress.Guides" : "Plugins.MotorShop.WordPress.Blog").ToString();

        var routeValues = new Dictionary<string, object?>();
        if (term != null)
        {
            routeValues["slug"] = term.Slug;
        }

        var model = new BlogListModel
        {
            Section = request.Section,
            Title = term?.Name ?? sectionTitle,
            Intro = term != null
                ? term.Description.NullEmpty()
                : T(isGuides ? "Plugins.MotorShop.WordPress.Guides.Intro" : "Plugins.MotorShop.WordPress.Blog.Intro").ToString(),
            Result = result,
            Categories = isGuides ? [] : (await _content.GetCategoriesAsync(HttpContext.RequestAborted))
                .Where(c => c.Count > 0 && !c.Slug.EqualsNoCase(_content.Options.GuideCategorySlug))
                .ToList(),
            ActiveCategorySlug = request.CategorySlug,
            Search = request.Search?.Trim().NullEmpty(),
            RouteName = routeName,
            RouteValues = routeValues
        };

        var canonicalValues = new RouteValueDictionary(routeValues);
        if (request.Page > 1)
        {
            canonicalValues["page"] = request.Page;
        }
        model.CanonicalUrl = AbsoluteUrl(routeName, canonicalValues);

        _breadcrumb.Track(new MenuItem { Text = sectionTitle, Url = Url.RouteUrl(isGuides ? RouteNames.Guides : RouteNames.Blog) });
        if (term != null)
        {
            _breadcrumb.Track(new MenuItem { Text = term.Name.HtmlEncode() });
        }

        return View("List", model);
    }

    private async Task<IActionResult> DetailAsync(string slug, BlogSection section)
    {
        if (!_content.IsEnabled)
        {
            return NotFound();
        }

        BlogPostResult result;
        try
        {
            result = await _content.GetPostAsync(slug, section, VisitorKey, HttpContext.RequestAborted);

            // Old WordPress links and links in content point to /blog/{slug}; the slug may belong to a page.
            if (result.IsAvailable && result.Post == null && section == BlogSection.Blog)
            {
                var page = await _content.GetPostAsync(slug, BlogSection.Page, VisitorKey, HttpContext.RequestAborted);
                if (page.Post != null)
                {
                    return RedirectPermanent(Url.BlogPostUrl(page.Post));
                }
            }
        }
        catch (WordPressRateLimitedException)
        {
            return TooManyRequests();
        }

        if (!result.IsAvailable)
        {
            return Unavailable();
        }

        var post = result.Post;
        if (post == null)
        {
            return NotFound();
        }

        // One URL per post: a guide under /blog (or a post under /rehberler) moves to its own section.
        var canonicalPath = Url.BlogPostUrl(post);
        if (post.Section != section || !post.Slug.Equals(slug, StringComparison.Ordinal))
        {
            return RedirectPermanent(canonicalPath);
        }

        var isGuide = post.Section == BlogSection.Guides;
        var sectionTitle = T(isGuide ? "Plugins.MotorShop.WordPress.Guides" : "Plugins.MotorShop.WordPress.Blog").ToString();
        var sectionUrl = Url.RouteUrl(isGuide ? RouteNames.Guides : RouteNames.Blog)!;
        var canonicalUrl = Services.StoreContext.CurrentStore.GetAbsoluteUrl(canonicalPath);

        var model = new BlogPostModel
        {
            Post = post,
            CanonicalUrl = canonicalUrl,
            SectionTitle = sectionTitle,
            SectionUrl = sectionUrl,
            Related = await _content.GetRelatedAsync(post, RelatedCount, HttpContext.RequestAborted),
            Products = await GetProductsAsync(post.ProductSkus),
            MetaProperties = CreateMetaProperties(post, canonicalUrl, sectionTitle)
        };

        _breadcrumb.Track(new MenuItem { Text = sectionTitle, Url = sectionUrl });
        if (!isGuide && post.Categories.Count > 0)
        {
            var category = post.Categories[0];
            _breadcrumb.Track(new MenuItem { Text = category.Name, Url = Url.RouteUrl(RouteNames.Category, new { slug = category.Slug }) });
        }
        // The breadcrumb renders the last item as raw HTML.
        _breadcrumb.Track(new MenuItem { Text = post.Title.HtmlEncode() });

        return View("Post", model);
    }

    /// <summary>
    /// Products linked by SKU tags, filtered like any other product list (published, visible, store, ACL).
    /// </summary>
    private async Task<ProductSummaryModel?> GetProductsAsync(IReadOnlyList<string> skus)
    {
        if (skus.Count == 0)
        {
            return null;
        }

        var lowerSkus = skus.Take(MaxProducts).Select(x => x.ToLowerInvariant()).ToArray();
        var productIds = await _db.Products
            .AsNoTracking()
            .Where(x => !x.Deleted && x.Published && x.Sku != null && lowerSkus.Contains(x.Sku.ToLower()))
            .Select(x => x.Id)
            .Take(MaxProducts)
            .ToArrayAsync(HttpContext.RequestAborted);

        if (productIds.Length == 0)
        {
            return null;
        }

        var query = new CatalogSearchQuery()
            .VisibleOnly(Services.WorkContext.CurrentCustomer)
            .WithVisibility(ProductVisibility.Full)
            .HasStoreId(Services.StoreContext.CurrentStore.Id)
            .WithLanguage(Services.WorkContext.WorkingLanguage)
            .WithCurrency(Services.WorkContext.WorkingCurrency)
            .WithProductIds(productIds)
            .Slice(0, MaxProducts);

        var searchResult = await _catalogSearchService.SearchAsync(query);
        if (searchResult.TotalHitsCount == 0)
        {
            return null;
        }

        var settings = _catalogHelper.GetBestFitProductSummaryMappingSettings(ProductSummaryViewMode.Grid);
        return await _catalogHelper.MapProductSummaryModelAsync(searchResult, settings);
    }

    private MetaPropertiesModel CreateMetaProperties(BlogPost post, string canonicalUrl, string sectionTitle)
    {
        return new MetaPropertiesModel
        {
            SiteName = Services.StoreContext.CurrentStore.Name,
            Site = Services.StoreContext.CurrentStore.GetBaseUrl(),
            Type = post.Section == BlogSection.Page ? "website" : "article",
            Url = canonicalUrl,
            Title = post.MetaTitle ?? post.Title,
            Description = post.MetaDescription ?? post.Excerpt,
            ImageUrl = post.Image?.Url,
            ImageAlt = post.Image?.Alt.NullEmpty() ?? post.Title,
            ImageType = ImageMimeType(post.Image?.Url),
            ImageWidth = post.Image?.Width ?? 0,
            ImageHeight = post.Image?.Height ?? 0,
            TwitterSite = _socialSettings.TwitterSite,
            FacebookAppId = _socialSettings.FacebookAppId,
            Article = post.Section == BlogSection.Page ? null : new MetaPropertiesModel.OpenGraphArticle
            {
                PublishedTime = post.PublishedUtc,
                Author = post.AuthorName,
                Section = post.Categories.FirstOrDefault()?.Name ?? sectionTitle,
                Tags = [.. post.Tags.Select(x => x.Name)]
            }
        };
    }

    private static string? ImageMimeType(string? url)
    {
        var extension = System.IO.Path.GetExtension(url?.Split('?')[0])?.ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => null
        };
    }

    private string AbsoluteUrl(string routeName, RouteValueDictionary values)
        => Services.StoreContext.CurrentStore.GetAbsoluteUrl(Url.RouteUrl(routeName, values));

    /// <summary>
    /// WordPress is down and nothing is cached. Only the blog is affected; crawlers are told to come back.
    /// </summary>
    private IActionResult Unavailable()
    {
        Response.Headers.RetryAfter = "300";
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View("Unavailable");
    }

    private IActionResult TooManyRequests()
    {
        Response.Headers.RetryAfter = "30";
        return StatusCode(StatusCodes.Status429TooManyRequests);
    }

    public static class RouteNames
    {
        public const string Blog = "MotorWordPress.Blog";
        public const string Category = "MotorWordPress.Category";
        public const string Tag = "MotorWordPress.Tag";
        public const string Post = "MotorWordPress.Post";
        public const string Guides = "MotorWordPress.Guides";
        public const string Guide = "MotorWordPress.Guide";
        public const string Page = "MotorWordPress.Page";
    }
}
