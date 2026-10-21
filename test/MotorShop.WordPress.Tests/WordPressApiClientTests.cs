using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorShop.WordPress.Client;
using MotorShop.WordPress.Services;
using NUnit.Framework;

namespace MotorShop.WordPress.Tests;

[TestFixture]
public class WordPressApiClientTests
{
    const string PostsJson = """
        [{
          "id": 7, "slug": "zincir-bakimi", "status": "publish", "type": "post",
          "date_gmt": "2026-10-01T08:00:00", "modified_gmt": "2026-10-02T09:30:00",
          "title": { "rendered": "Zincir bak&#305;m&#305; &#8211; rehber" },
          "content": { "rendered": "<p>Metin</p>", "protected": false },
          "excerpt": { "rendered": "<p>&Ouml;zet [&hellip;]</p>" },
          "featured_media": 11, "categories": [3], "tags": [5],
          "_embedded": {
            "author": [{ "id": 1, "name": "Editör" }],
            "wp:featuredmedia": [{ "id": 11, "source_url": "https://cms.example.com/wp-content/uploads/zincir.jpg", "alt_text": "Zincir", "media_type": "image",
              "media_details": { "width": 1600, "height": "900", "sizes": { "medium_large": { "source_url": "https://cms.example.com/wp-content/uploads/zincir-768x432.jpg", "width": 768, "height": 432 } } } }],
            "wp:term": [[{ "id": 3, "name": "Bak&#305;m", "slug": "bakim", "taxonomy": "category" }], [{ "id": 5, "name": "SKU", "slug": "sku-pr-1001", "taxonomy": "post_tag" }]]
          },
          "unknown_field": { "nested": true }
        }]
        """;

    private static (WordPressApiClient Client, StubHandler Handler, WordPressStatus Status) Create(StubHandler handler, WordPressOptions? options = null)
    {
        options ??= TestOptions.Create();
        var status = new WordPressStatus();
        var client = new WordPressApiClient(
            new HttpClient(handler),
            TestOptions.Monitor(options),
            new WordPressResilience(Options.Create(options)),
            status,
            new WordPressThrottle(),
            NullLogger<WordPressApiClient>.Instance);

        return (client, handler, status);
    }

    [Test]
    public async Task Parses_posts_and_totals_and_requests_only_published_content()
    {
        var (client, handler, status) = Create(new StubHandler(_ => StubHandler.Json(PostsJson, total: 31, totalPages: 3)));

        var result = await client.GetPostsAsync(new WpPostQuery { Page = 2, PerPage = 12, Search = "zincir & yağ", CategoriesExclude = [9] });

        Assert.That(result.Total, Is.EqualTo(31));
        Assert.That(result.TotalPages, Is.EqualTo(3));
        Assert.That(result.Items, Has.Count.EqualTo(1));

        var post = result.Items[0];
        Assert.That(post.Slug, Is.EqualTo("zincir-bakimi"));
        Assert.That(post.Embedded!.FeaturedMedia![0].MediaDetails!.Height, Is.EqualTo(900), "numeric string");
        Assert.That(post.Embedded.Terms!.Length, Is.EqualTo(2));

        var uri = handler.Requests.Single().RequestUri!;
        Assert.That(uri.AbsolutePath, Is.EqualTo("/wp-json/wp/v2/posts"));
        Assert.That(uri.Query, Does.Contain("status=publish"));
        Assert.That(uri.Query, Does.Contain("page=2"));
        Assert.That(uri.Query, Does.Contain("search=zincir%20%26%20ya%C4%9F"));
        Assert.That(uri.Query, Does.Contain("categories_exclude=9"));
        Assert.That(handler.Requests[0].Headers.Authorization, Is.Null, "public API without credentials");
        Assert.That(status.LastSuccessUtc, Is.Not.Null);
    }

    [Test]
    public async Task Sends_application_password_only_when_configured()
    {
        var options = TestOptions.Create(o => { o.UserName = "motor-reader"; o.ApplicationPassword = "abcd efgh ijkl"; });
        var (client, handler, _) = Create(new StubHandler(_ => StubHandler.Json("[]")), options);

        await client.GetPostsAsync(new WpPostQuery());

        var auth = handler.Requests.Single().Headers.Authorization!;
        Assert.That(auth.Scheme, Is.EqualTo("Basic"));
        Assert.That(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!)), Is.EqualTo("motor-reader:abcdefghijkl"));
    }

    [Test]
    public void Disabled_or_invalid_configuration_sends_nothing()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));

        var (disabled, _, _) = Create(handler, TestOptions.Create(o => o.Enabled = false));
        var ex = Assert.ThrowsAsync<WordPressApiException>(() => disabled.GetPostsAsync(new WpPostQuery()));
        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.NotConfigured));

        var (http, _, _) = Create(handler, TestOptions.Create(o => o.BaseUrl = "http://cms.example.com"));
        ex = Assert.ThrowsAsync<WordPressApiException>(() => http.GetPostsAsync(new WpPostQuery()));
        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.NotConfigured));

        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task Page_beyond_last_returns_empty_collection()
    {
        var (client, _, _) = Create(new StubHandler(_ =>
            StubHandler.Json("""{"code":"rest_post_invalid_page_number","message":"x","data":{"status":400}}""", HttpStatusCode.BadRequest, total: 4, totalPages: 1)));

        var result = await client.GetPostsAsync(new WpPostQuery { Page = 9 });

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.Total, Is.EqualTo(4));
    }

    [TestCase(HttpStatusCode.NotFound, WordPressFailure.InvalidResponse)]
    [TestCase(HttpStatusCode.Unauthorized, WordPressFailure.InvalidResponse)]
    [TestCase(HttpStatusCode.MovedPermanently, WordPressFailure.InvalidResponse)]
    [TestCase(HttpStatusCode.ServiceUnavailable, WordPressFailure.Unavailable)]
    [TestCase(HttpStatusCode.TooManyRequests, WordPressFailure.Unavailable)]
    public void Http_errors_are_classified(HttpStatusCode code, WordPressFailure expected)
    {
        var (client, _, status) = Create(new StubHandler(_ =>
        {
            var response = StubHandler.Json("{}", code);
            if (code == HttpStatusCode.MovedPermanently)
            {
                response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            }
            return response;
        }));

        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));

        Assert.That(ex!.Failure, Is.EqualTo(expected));
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.RecentErrors[0].Message, Does.Not.Contain("169.254"), "no response details in the status");
    }

    [Test]
    public void Transient_errors_are_retried_once()
    {
        var (client, handler, _) = Create(new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.BadGateway)));

        Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));

        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void Non_json_answer_is_invalid_response()
    {
        var (client, _, _) = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Security check</html>", System.Text.Encoding.UTF8, "text/html")
        }));

        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));
        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.InvalidResponse));
    }

    [Test]
    public void Broken_json_is_invalid_response()
    {
        var (client, _, _) = Create(new StubHandler(_ => StubHandler.Json("[{\"id\": 1, \"slug\": ")));

        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));
        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.InvalidResponse));
    }

    [Test]
    public void Timeout_is_unavailable_and_bounded()
    {
        var (client, handler, _) = Create(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHandler.Json("[]");
        }));

        var watch = Stopwatch.StartNew();
        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));
        watch.Stop();

        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.Unavailable));
        // TimeoutSeconds = 1: one attempt, one retry, small backoff, total limit 3 s.
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void Network_error_is_unavailable()
    {
        var (client, _, status) = Create(new StubHandler((_, _) => throw new HttpRequestException("No such host is known.")));

        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));

        Assert.That(ex!.Failure, Is.EqualTo(WordPressFailure.Unavailable));
        Assert.That(status.RecentErrors[0].Message, Is.EqualTo("WordPress could not be reached."));
    }

    [Test]
    public void Circuit_opens_after_repeated_failures()
    {
        var (client, handler, _) = Create(new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable)));

        for (var i = 0; i < 5; i++)
        {
            Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));
        }

        var before = handler.Requests.Count;
        var ex = Assert.ThrowsAsync<WordPressApiException>(() => client.GetPostsAsync(new WpPostQuery()));

        Assert.That(ex!.Message, Does.Contain("paused"));
        Assert.That(handler.Requests.Count, Is.EqualTo(before), "open circuit: no request");
    }

    [Test]
    public void Caller_cancellation_is_not_recorded_as_failure()
    {
        var (client, _, status) = Create(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHandler.Json("[]");
        }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.CatchAsync<OperationCanceledException>(() => client.GetPostsAsync(new WpPostQuery(), cts.Token));
        Assert.That(status.ConsecutiveFailures, Is.Zero);
    }
}
