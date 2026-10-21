using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorShop.WordPress.Services;
using Polly;

namespace MotorShop.WordPress.Client;

public enum WordPressFailure
{
    /// <summary>The integration is off or the URL is invalid. Nothing was sent.</summary>
    NotConfigured,
    /// <summary>Timeout, network error, 5xx, 429 or open circuit.</summary>
    Unavailable,
    /// <summary>WordPress answered, but not with what was expected (4xx, redirect, no JSON).</summary>
    InvalidResponse
}

public sealed class WordPressApiException : Exception
{
    public WordPressApiException(WordPressFailure failure, string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
    }

    public WordPressFailure Failure { get; }
    public int? StatusCode { get; }
}

/// <summary>
/// Query for the posts and pages collections. Only published content is ever requested.
/// </summary>
public sealed class WpPostQuery
{
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 12;
    public string? Search { get; init; }
    public string? Slug { get; init; }
    public IReadOnlyCollection<int>? Categories { get; init; }
    public IReadOnlyCollection<int>? CategoriesExclude { get; init; }
    public IReadOnlyCollection<int>? Tags { get; init; }
    public IReadOnlyCollection<int>? Exclude { get; init; }
}

public interface IWordPressApiClient
{
    Task<WpCollection<WpPost>> GetPostsAsync(WpPostQuery query, CancellationToken cancelToken = default);
    Task<WpCollection<WpPost>> GetPagesAsync(WpPostQuery query, CancellationToken cancelToken = default);
    Task<IReadOnlyList<WpTerm>> GetCategoriesAsync(CancellationToken cancelToken = default);
    Task<IReadOnlyList<WpTerm>> GetTagsAsync(IReadOnlyCollection<int>? include = null, string? slug = null, CancellationToken cancelToken = default);
    Task<WpMedia?> GetMediaAsync(int id, CancellationToken cancelToken = default);
}

/// <summary>
/// Typed HTTP client for the public WordPress REST API (wp/v2). Read only.
/// </summary>
public class WordPressApiClient : IWordPressApiClient
{
    // Fields MOTOR needs. Keeps responses small; "_embed" adds author, featured image and terms.
    const string PostFields = "id,slug,status,type,link,date_gmt,modified_gmt,title,content,excerpt,featured_media,categories,tags,yoast_head_json,_links,_embedded";
    const string TermFields = "id,name,slug,taxonomy,description,count,parent";
    const int MaxTermPages = 10;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<WordPressOptions> _options;
    private readonly WordPressResilience _resilience;
    private readonly WordPressStatus _status;
    private readonly WordPressThrottle _throttle;
    private readonly ILogger<WordPressApiClient> _logger;

    public WordPressApiClient(
        HttpClient http,
        IOptionsMonitor<WordPressOptions> options,
        WordPressResilience resilience,
        WordPressStatus status,
        WordPressThrottle throttle,
        ILogger<WordPressApiClient> logger)
    {
        _http = http;
        _options = options;
        _resilience = resilience;
        _status = status;
        _throttle = throttle;
        _logger = logger;
    }

    public Task<WpCollection<WpPost>> GetPostsAsync(WpPostQuery query, CancellationToken cancelToken = default)
        => GetContentAsync("posts", query, cancelToken);

    public Task<WpCollection<WpPost>> GetPagesAsync(WpPostQuery query, CancellationToken cancelToken = default)
        => GetContentAsync("pages", query, cancelToken);

    public async Task<IReadOnlyList<WpTerm>> GetCategoriesAsync(CancellationToken cancelToken = default)
    {
        var result = new List<WpTerm>();

        for (var page = 1; page <= MaxTermPages; page++)
        {
            var batch = await SendAsync<WpTerm>($"categories?per_page=100&page={page}&hide_empty=true&_fields={TermFields}", cancelToken);
            result.AddRange(batch.Items);

            if (page >= batch.TotalPages)
            {
                break;
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<WpTerm>> GetTagsAsync(IReadOnlyCollection<int>? include = null, string? slug = null, CancellationToken cancelToken = default)
    {
        var sb = new StringBuilder($"tags?per_page=100&_fields={TermFields}");
        AppendIds(sb, "include", include);
        if (slug.HasValue())
        {
            sb.Append("&slug=").Append(Uri.EscapeDataString(slug));
        }

        var result = await SendAsync<WpTerm>(sb.ToString(), cancelToken);
        return result.Items;
    }

    public async Task<WpMedia?> GetMediaAsync(int id, CancellationToken cancelToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var result = await SendAsync<WpMedia>($"media?include={id}&_fields=id,source_url,alt_text,media_type,media_details", cancelToken);
        return result.Items.FirstOrDefault();
    }

    private Task<WpCollection<WpPost>> GetContentAsync(string collection, WpPostQuery query, CancellationToken cancelToken)
    {
        Guard.NotNull(query);

        var sb = new StringBuilder(collection)
            .Append("?status=publish&_embed=author,wp:featuredmedia,wp:term")
            .Append("&page=").Append(Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture))
            .Append("&per_page=").Append(Math.Clamp(query.PerPage, 1, 100).ToString(CultureInfo.InvariantCulture))
            .Append("&_fields=").Append(PostFields);

        if (query.Slug.HasValue())
        {
            sb.Append("&slug=").Append(Uri.EscapeDataString(query.Slug));
        }

        if (query.Search.HasValue())
        {
            sb.Append("&search=").Append(Uri.EscapeDataString(query.Search));
        }

        AppendIds(sb, "categories", query.Categories);
        AppendIds(sb, "categories_exclude", query.CategoriesExclude);
        AppendIds(sb, "tags", query.Tags);
        AppendIds(sb, "exclude", query.Exclude);

        return SendAsync<WpPost>(sb.ToString(), cancelToken);
    }

    private static void AppendIds(StringBuilder sb, string name, IReadOnlyCollection<int>? ids)
    {
        if (ids?.Count > 0)
        {
            sb.Append('&').Append(name).Append('=').Append(string.Join(',', ids.Select(x => x.ToString(CultureInfo.InvariantCulture))));
        }
    }

    /// <param name="relativeUrl">Path and query below <c>/wp-json/wp/v2/</c>.</param>
    internal async Task<WpCollection<T>> SendAsync<T>(string relativeUrl, CancellationToken cancelToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            throw new WordPressApiException(WordPressFailure.NotConfigured, "The WordPress integration is disabled.");
        }

        var baseUri = WordPressEndpoint.TryGetBaseUri(options, out var error)
            ?? throw new WordPressApiException(WordPressFailure.NotConfigured, error!);

        var requestUri = new Uri(baseUri, "wp-json/wp/v2/" + relativeUrl);
        // Only the path goes to the log: the query can contain what a visitor searched for.
        var logPath = requestUri.AbsolutePath;

        if (!await _throttle.EnterAsync(cancelToken))
        {
            _status.RecordFailure("Too many WordPress requests at the same time.");
            throw new WordPressApiException(WordPressFailure.Unavailable, "Too many WordPress requests at the same time.");
        }

        try
        {
            using var response = await _resilience.Pipeline.ExecuteAsync(
                async ct => await _http.SendAsync(CreateRequest(requestUri, options), HttpCompletionOption.ResponseContentRead, ct),
                cancelToken);

            var result = await ReadAsync<T>(response, cancelToken);
            _status.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            // The visitor went away. Not a WordPress problem.
            throw;
        }
        catch (WordPressApiException ex)
        {
            _status.RecordFailure(ex.Message);
            _logger.LogWarning("WordPress request {Path} failed: {Reason}", logPath, ex.Message);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            var message = ex switch
            {
                Polly.CircuitBreaker.BrokenCircuitException => "WordPress is paused after repeated failures.",
                HttpRequestException hre when hre.StatusCode.HasValue => $"WordPress answered with HTTP {(int)hre.StatusCode}.",
                HttpRequestException => "WordPress could not be reached.",
                _ => "WordPress did not answer in time."
            };

            _status.RecordFailure(message);
            _logger.LogWarning("WordPress request {Path} failed: {Reason} ({ExceptionType})", logPath, message, ex.GetType().Name);
            throw new WordPressApiException(WordPressFailure.Unavailable, message, (int?)(ex as HttpRequestException)?.StatusCode, ex);
        }
        finally
        {
            _throttle.Exit();
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri, WordPressOptions options)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (options.UserName.HasValue() && options.ApplicationPassword.HasValue())
        {
            // WordPress application passwords are shown with spaces; WordPress ignores them.
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserName}:{options.ApplicationPassword.Replace(" ", string.Empty)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        return request;
    }

    private static async Task<WpCollection<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancelToken)
    {
        var status = (int)response.StatusCode;

        if (status is >= 300 and < 400)
        {
            // Redirects are not followed, they could lead to another host.
            throw new WordPressApiException(WordPressFailure.InvalidResponse, $"WordPress redirected (HTTP {status}). Check the configured URL.", status);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // "rest_post_invalid_page_number": a page beyond the last one.
            var body = await response.Content.ReadAsStringAsync(cancelToken);
            if (body.Contains("invalid_page_number", StringComparison.Ordinal))
            {
                return new WpCollection<T> { Total = ReadIntHeader(response, "X-WP-Total"), TotalPages = ReadIntHeader(response, "X-WP-TotalPages") };
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new WordPressApiException(
                status >= 500 || status == 429 ? WordPressFailure.Unavailable : WordPressFailure.InvalidResponse,
                $"WordPress answered with HTTP {status}.",
                status);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            throw new WordPressApiException(WordPressFailure.InvalidResponse, "WordPress did not answer with JSON. Is the REST API enabled and are permalinks set?", status);
        }

        List<T>? items;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancelToken);
            items = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancelToken);
        }
        catch (JsonException ex)
        {
            throw new WordPressApiException(WordPressFailure.InvalidResponse, "WordPress answered with JSON MOTOR cannot read.", status, ex);
        }

        items ??= [];

        return new WpCollection<T>
        {
            Items = items,
            Total = response.Headers.Contains("X-WP-Total") ? ReadIntHeader(response, "X-WP-Total") : items.Count,
            TotalPages = response.Headers.Contains("X-WP-TotalPages") ? ReadIntHeader(response, "X-WP-TotalPages") : 1
        };
    }

    private static int ReadIntHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
