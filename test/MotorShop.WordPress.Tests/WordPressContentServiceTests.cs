using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Services;
using NUnit.Framework;

namespace MotorShop.WordPress.Tests;

[TestFixture]
public class WordPressContentServiceTests
{
    private FakeApiClient _client = null!;
    private FakeCache _cache = null!;
    private WordPressStatus _status = null!;
    private WordPressThrottle _throttle = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new FakeApiClient();
        _cache = new FakeCache();
        _status = new WordPressStatus();
        _throttle = new WordPressThrottle();
    }

    [TearDown]
    public void TearDown() => _throttle.Dispose();

    private WordPressContentService Create(WordPressOptions? options = null)
        => new(_client, _cache, TestOptions.Monitor(options ?? TestOptions.Create()), new BlogPostMapper(new WordPressHtmlSanitizer()),
            _throttle, _status, NullLogger<WordPressContentService>.Instance);

    private static WpCollection<WpPost> Collection(params WpPost[] posts)
        => new() { Items = posts, Total = posts.Length, TotalPages = 1 };

    [Test]
    public async Task Post_is_served_from_cache_after_the_first_request()
    {
        _client.Posts = q => Collection(WpSamples.Post(1, q.Slug!));
        var service = Create();

        var first = await service.GetPostAsync("zincir", BlogSection.Blog, "1.2.3.4");
        var second = await service.GetPostAsync("zincir", BlogSection.Blog, "1.2.3.4");

        Assert.That(first.Post!.Slug, Is.EqualTo("zincir"));
        Assert.That(second.Post!.Id, Is.EqualTo(1));
        Assert.That(_client.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Upper_case_slug_finds_the_post_with_its_lower_case_slug()
    {
        _client.Posts = q => Collection(WpSamples.Post(1, q.Slug!));

        var result = await Create().GetPostAsync("Zincir-Bakimi", BlogSection.Blog, null);

        Assert.That(_client.PostQueries[0].Slug, Is.EqualTo("zincir-bakimi"));
        Assert.That(result.Post!.Slug, Is.EqualTo("zincir-bakimi"));
    }

    [Test]
    public async Task New_content_shows_after_cache_refresh()
    {
        var title = "Eski başlık";
        _client.Posts = q => Collection(WpSamples.Post(1, "zincir", title: title));
        var service = Create();

        await service.GetPostAsync("zincir", BlogSection.Blog, null);
        title = "Yeni başlık";
        var cached = await service.GetPostAsync("zincir", BlogSection.Blog, null);
        await service.ClearCacheAsync();
        var refreshed = await service.GetPostAsync("zincir", BlogSection.Blog, null);

        Assert.That(cached.Post!.Title, Is.EqualTo("Eski başlık"));
        Assert.That(refreshed.Post!.Title, Is.EqualTo("Yeni başlık"));
        Assert.That(_status.LastCacheClearUtc, Is.Not.Null);
    }

    [Test]
    public async Task Fallback_copy_is_served_when_wordpress_is_down()
    {
        _client.Posts = _ => Collection(WpSamples.Post(1, "zincir"));
        var service = Create();
        await service.GetPostAsync("zincir", BlogSection.Blog, null);

        await service.ClearCacheAsync();
        _client.Posts = _ => throw FakeApiClient.Down();

        var result = await service.GetPostAsync("zincir", BlogSection.Blog, null);

        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Post!.Slug, Is.EqualTo("zincir"));
    }

    [Test]
    public async Task Unavailable_without_fallback_copy()
    {
        _client.Posts = _ => throw FakeApiClient.Down();
        var service = Create();

        var post = await service.GetPostAsync("zincir", BlogSection.Blog, null);
        var list = await service.GetPostsAsync(new BlogListRequest(), null);

        Assert.That(post.IsAvailable, Is.False);
        Assert.That(post.Post, Is.Null);
        Assert.That(list.IsAvailable, Is.False);
    }

    [Test]
    public async Task Deleted_or_unpublished_post_disappears_including_its_fallback_copy()
    {
        _client.Posts = _ => Collection(WpSamples.Post(1, "zincir"));
        var service = Create();
        await service.GetPostAsync("zincir", BlogSection.Blog, null);

        // Unpublished in WordPress: the public API no longer returns it.
        _client.Posts = _ => Collection();
        await service.ClearCacheAsync();
        var gone = await service.GetPostAsync("zincir", BlogSection.Blog, null);

        // Then WordPress goes down: the old copy must not come back.
        _client.Posts = _ => throw FakeApiClient.Down();
        await service.ClearCacheAsync();
        var whileDown = await service.GetPostAsync("zincir", BlogSection.Blog, null);

        Assert.That(gone.IsAvailable, Is.True);
        Assert.That(gone.Post, Is.Null);
        Assert.That(whileDown.Post, Is.Null);
    }

    [Test]
    public async Task Draft_returned_by_wordpress_is_not_shown()
    {
        _client.Posts = _ => Collection(WpSamples.Post(1, "taslak", status: "draft"));

        var result = await Create().GetPostAsync("taslak", BlogSection.Blog, null);

        Assert.That(result.Post, Is.Null);
    }

    [Test]
    public async Task Blog_list_excludes_guides_and_guides_list_includes_only_them()
    {
        _client.Categories = () => [WpSamples.Category(3, "bakim"), WpSamples.Category(7, "rehberler", "Rehberler")];
        _client.Posts = _ => Collection(WpSamples.Post(1, "a"));
        var service = Create();

        await service.GetPostsAsync(new BlogListRequest { Section = BlogSection.Blog }, null);
        await service.GetPostsAsync(new BlogListRequest { Section = BlogSection.Guides }, null);
        await service.GetPostsAsync(new BlogListRequest { Section = BlogSection.Blog, CategorySlug = "bakim", Page = 2 }, null);

        Assert.That(_client.PostQueries[0].CategoriesExclude, Is.EqualTo(new[] { 7 }));
        Assert.That(_client.PostQueries[1].Categories, Is.EqualTo(new[] { 7 }));
        Assert.That(_client.PostQueries[2].Categories, Is.EqualTo(new[] { 3 }));
        Assert.That(_client.PostQueries[2].Page, Is.EqualTo(2));
    }

    [Test]
    public async Task Unknown_category_or_missing_guide_category_gives_empty_list_without_post_request()
    {
        _client.Categories = () => [WpSamples.Category(3, "bakim")];
        var service = Create();

        var unknown = await service.GetPostsAsync(new BlogListRequest { CategorySlug = "yok" }, null);
        var guides = await service.GetPostsAsync(new BlogListRequest { Section = BlogSection.Guides }, null);

        Assert.That(unknown.Posts, Is.Empty);
        Assert.That(unknown.IsAvailable, Is.True);
        Assert.That(guides.Posts, Is.Empty);
        Assert.That(_client.PostQueries, Is.Empty);
    }

    [Test]
    public async Task Search_is_trimmed_limited_and_not_kept_as_fallback()
    {
        _client.Posts = _ => Collection(WpSamples.Post(1, "a"));
        var service = Create();

        await service.GetPostsAsync(new BlogListRequest { Search = "  " + new string('z', 300) + "  " }, "1.2.3.4");

        Assert.That(_client.PostQueries[0].Search, Has.Length.EqualTo(100));
        Assert.That(_cache.Entries.Keys.Where(k => k.Contains(":stale:posts:")), Is.Empty);
    }

    [Test]
    public async Task Visitor_is_rate_limited_on_cache_misses_only()
    {
        _client.Posts = q => Collection(WpSamples.Post(1, q.Slug!));
        var service = Create();

        // The bucket allows a burst of 20 WordPress calls per visitor.
        for (var i = 0; i < 20; i++)
        {
            await service.GetPostAsync("yazi-" + i, BlogSection.Blog, "6.6.6.6");
        }

        Assert.ThrowsAsync<WordPressRateLimitedException>(() => service.GetPostAsync("yazi-yeni", BlogSection.Blog, "6.6.6.6"));

        // Cached content and other visitors are not affected.
        Assert.That((await service.GetPostAsync("yazi-3", BlogSection.Blog, "6.6.6.6")).Post, Is.Not.Null);
        Assert.That((await service.GetPostAsync("yazi-yeni", BlogSection.Blog, "7.7.7.7")).Post, Is.Not.Null);
    }

    [Test]
    public async Task Invalid_slug_or_disabled_integration_makes_no_request()
    {
        var service = Create();
        var invalid = await service.GetPostAsync("../../wp-admin", BlogSection.Blog, null);

        var disabled = Create(TestOptions.Create(o => o.Enabled = false));
        var off = await disabled.GetPostAsync("zincir", BlogSection.Blog, null);
        var list = await disabled.GetPostsAsync(new BlogListRequest(), null);

        Assert.That(invalid.Post, Is.Null);
        Assert.That(off.IsAvailable, Is.False);
        Assert.That(list.IsAvailable, Is.False);
        Assert.That(_client.Calls, Is.Zero);
    }

    [Test]
    public async Task Related_posts_never_throw_and_exclude_the_post()
    {
        var post = new BlogPost { Id = 1, Slug = "a", Categories = [new BlogTerm { Id = 3, Slug = "bakim" }] };
        _client.Posts = q => q.Categories != null ? Collection(WpSamples.Post(2, "b"), WpSamples.Post(3, "c")) : Collection();
        var service = Create();

        var related = await service.GetRelatedAsync(post, 3);

        Assert.That(related.Select(x => x.Id), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(_client.PostQueries[0].Exclude, Is.EqualTo(new[] { 1 }));
        Assert.That(_client.PostQueries[0].Categories, Is.EqualTo(new[] { 3 }));

        _client.Posts = _ => throw FakeApiClient.Down();
        await service.ClearCacheAsync();
        _cache.Entries.Clear();
        Assert.That(await service.GetRelatedAsync(post, 3), Is.Empty);
    }

    [Test]
    public async Task Related_posts_are_filled_up_with_latest_posts()
    {
        var post = new BlogPost { Id = 1, Slug = "a", Categories = [new BlogTerm { Id = 3, Slug = "bakim" }] };
        _client.Posts = q => q.Categories != null ? Collection(WpSamples.Post(2, "b")) : Collection(WpSamples.Post(4, "d"), WpSamples.Post(5, "e"), WpSamples.Post(6, "f"));

        var related = await Create().GetRelatedAsync(post, 3);

        Assert.That(related.Select(x => x.Id), Is.EqualTo(new[] { 2, 4, 5 }));
        Assert.That(_client.PostQueries[1].Exclude, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task Sitemap_enumerates_posts_and_pages_across_pages()
    {
        _client.Posts = q => new WpCollection<WpPost>
        {
            Items = q.Page == 1 ? [WpSamples.Post(1, "a"), WpSamples.Post(2, "b")] : [WpSamples.Post(3, "c")],
            Total = 3,
            TotalPages = 2
        };
        _client.Pages = _ =>
        {
            var page = WpSamples.Post(10, "garanti");
            page.Type = "page";
            return Collection(page);
        };

        var all = new System.Collections.Generic.List<BlogPost>();
        await foreach (var post in Create().EnumerateAllAsync())
        {
            all.Add(post);
        }

        Assert.That(all.Select(x => x.Id), Is.EqualTo(new[] { 1, 2, 3, 10 }));
        Assert.That(all.Last().Section, Is.EqualTo(BlogSection.Page));
    }
}
