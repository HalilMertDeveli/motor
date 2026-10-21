using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Models;
using MotorShop.WordPress.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Web.Controllers;

namespace MotorShop.WordPress.Controllers;

/// <summary>
/// Connection status and maintenance. The configuration itself is read only here (environment variables),
/// and the WordPress dashboard is only linked: there is no shared login.
/// </summary>
public class MotorWordPressAdminController : AdminController
{
    private readonly WordPressContentService _content;
    private readonly IWordPressApiClient _client;
    private readonly WordPressStatus _status;
    private readonly BlogPostMapper _mapper;
    private readonly SmartDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MotorWordPressAdminController(
        WordPressContentService content,
        IWordPressApiClient client,
        WordPressStatus status,
        BlogPostMapper mapper,
        SmartDbContext db,
        IWebHostEnvironment env)
    {
        _content = content;
        _client = client;
        _status = status;
        _mapper = mapper;
        _db = db;
        _env = env;
    }

    [Permission(WordPressPermissions.Read)]
    public async Task<IActionResult> Index()
    {
        var options = _content.Options;
        var baseUri = WordPressEndpoint.TryGetBaseUri(options, out var configError);

        var model = new WordPressAdminModel
        {
            Enabled = options.Enabled,
            Host = baseUri?.GetLeftPart(UriPartial.Authority),
            ConfigurationError = options.BaseUrl.HasValue() || options.Enabled ? configError : null,
            UsesApplicationPassword = options.UserName.HasValue() && options.ApplicationPassword.HasValue(),
            WebhookEnabled = options.WebhookSecret?.Length >= WordPressWebhook.MinSecretLength,
            AdminUrl = GetAdminUrl(options, baseUri),
            CacheMinutes = options.CacheMinutes,
            GuideCategorySlug = options.GuideCategorySlug,
            ProductTagPrefix = options.ProductTagPrefix,
            CanUpdate = await Services.Permissions.AuthorizeAsync(WordPressPermissions.Update)
        };

        if (options.Enabled && baseUri != null)
        {
            await CheckConnectionAsync(model, options, baseUri);
        }

        model.LastSuccessUtc = _status.LastSuccessUtc;
        model.LastFailureUtc = _status.LastFailureUtc;
        model.LastCacheClearUtc = _status.LastCacheClearUtc;
        model.ConsecutiveFailures = _status.ConsecutiveFailures;
        model.RecentErrors = _status.RecentErrors;

        await AddWarningsAsync(model, options);

        return View(model);
    }

    [HttpPost]
    [Permission(WordPressPermissions.Update)]
    public async Task<IActionResult> ClearCache()
    {
        var removed = await _content.ClearCacheAsync();
        NotifySuccess(T("Plugins.MotorShop.WordPress.Admin.CacheCleared", removed));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Permission(WordPressPermissions.Update)]
    public IActionResult RebuildSitemap([FromServices] IXmlSitemapGenerator sitemapGenerator)
    {
        // The next request to /sitemap.xml rebuilds it, with the current blog posts.
        sitemapGenerator.InvalidateAll();
        NotifySuccess(T("Plugins.MotorShop.WordPress.Admin.SitemapInvalidated"));

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Uncached requests, so that the page shows the real state of WordPress.
    /// </summary>
    private async Task CheckConnectionAsync(WordPressAdminModel model, WordPressOptions options, Uri baseUri)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var posts = await _client.GetPostsAsync(new WpPostQuery { PerPage = 5 }, HttpContext.RequestAborted);
            model.ResponseMilliseconds = watch.ElapsedMilliseconds;
            model.PublishedPosts = posts.Total;
            model.RecentPosts = posts.Items
                .Select(x => _mapper.Map(x, options, baseUri, includeContent: false))
                .Where(x => x != null)
                .ToList()!;

            var pages = await _client.GetPagesAsync(new WpPostQuery { PerPage = 1 }, HttpContext.RequestAborted);
            model.PublishedPages = pages.Total;

            var categories = await _client.GetCategoriesAsync(HttpContext.RequestAborted);
            model.Categories = categories.Count;
            model.GuideCategoryExists = categories.Any(c => BlogPostMapper.DecodeSlug(c.Slug).EqualsNoCase(options.GuideCategorySlug));
            model.Connected = true;
        }
        catch (WordPressApiException ex)
        {
            model.Connected = false;
            model.ConnectionError = ex.Message;
            model.ResponseMilliseconds = watch.ElapsedMilliseconds;
        }
    }

    private async Task AddWarningsAsync(WordPressAdminModel model, WordPressOptions options)
    {
        if (_env.IsProduction() && (options.AllowInsecureHttp || options.AllowPrivateNetwork))
        {
            model.Warnings.Add(T("Plugins.MotorShop.WordPress.Admin.Warning.DevFlags"));
        }

        if (options.WebhookSecret.HasValue() && options.WebhookSecret.Length < WordPressWebhook.MinSecretLength)
        {
            model.Warnings.Add(T("Plugins.MotorShop.WordPress.Admin.Warning.ShortSecret", WordPressWebhook.MinSecretLength));
        }

        if (model.Connected == true && options.GuideCategorySlug.HasValue() && !model.GuideCategoryExists)
        {
            model.Warnings.Add(T("Plugins.MotorShop.WordPress.Admin.Warning.NoGuideCategory", options.GuideCategorySlug));
        }

        // Shop entities whose URL is /blog or /rehberler would be hidden by the blog routes.
        var collisions = await _db.UrlRecords
            .AsNoTracking()
            .Where(x => x.IsActive && BlogPaths.ReservedSlugs.Contains(x.Slug))
            .Select(x => new { x.EntityName, x.EntityId, x.Slug })
            .ToListAsync();

        foreach (var c in collisions)
        {
            model.Warnings.Add(T("Plugins.MotorShop.WordPress.Admin.Warning.SlugCollision", c.Slug, c.EntityName, c.EntityId));
        }
    }

    private static string? GetAdminUrl(WordPressOptions options, Uri? baseUri)
    {
        if (options.AdminUrl.HasValue())
        {
            return Uri.TryCreate(options.AdminUrl, UriKind.Absolute, out var admin) && (admin.Scheme == Uri.UriSchemeHttps || (options.AllowInsecureHttp && admin.Scheme == Uri.UriSchemeHttp))
                ? admin.ToString()
                : null;
        }

        return baseUri == null ? null : new Uri(baseUri, "wp-admin/").ToString();
    }
}
