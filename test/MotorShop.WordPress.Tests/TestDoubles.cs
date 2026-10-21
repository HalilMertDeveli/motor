using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MotorShop.WordPress;
using MotorShop.WordPress.Client;
using Smartstore.Caching;
using Smartstore.Threading;

namespace MotorShop.WordPress.Tests;

internal static class TestOptions
{
    public static WordPressOptions Create(Action<WordPressOptions>? configure = null)
    {
        var options = new WordPressOptions
        {
            Enabled = true,
            BaseUrl = "https://cms.example.com",
            TimeoutSeconds = 1,
            CacheMinutes = 10,
            StaleCacheHours = 48
        };

        configure?.Invoke(options);
        return options;
    }

    public static IOptionsMonitor<WordPressOptions> Monitor(WordPressOptions options) => new StaticMonitor(options);

    private sealed class StaticMonitor(WordPressOptions value) : IOptionsMonitor<WordPressOptions>
    {
        public WordPressOptions CurrentValue => value;
        public WordPressOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<WordPressOptions, string?> listener) => null;
    }
}

/// <summary>
/// Answers requests with a delegate and records them.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((r, _) => Task.FromResult(respond(r)))
    {
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return _respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, int? total = null, int? totalPages = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (total.HasValue)
        {
            response.Headers.Add("X-WP-Total", total.Value.ToString());
        }
        if (totalPages.HasValue)
        {
            response.Headers.Add("X-WP-TotalPages", totalPages.Value.ToString());
        }
        return response;
    }
}

/// <summary>
/// Dictionary backed <see cref="ICacheManager"/> with the members the module uses. Expiration is not simulated.
/// </summary>
internal sealed class FakeCache : ICacheManager
{
    public ConcurrentDictionary<string, object?> Entries { get; } = new();

    public event EventHandler<CacheEntryExpiredEventArgs>? Expired { add { } remove { } }

    public bool IsDistributed => false;

    public Task<AsyncOut<T>> TryGetAsync<T>(string key)
        => Task.FromResult(Entries.TryGetValue(key, out var value) && value is T t ? new AsyncOut<T>(true, t) : AsyncOut<T>.Empty);

    public Task<T?> GetAsync<T>(string key, bool independent = false)
        => Task.FromResult(Entries.TryGetValue(key, out var value) && value is T t ? t : default);

    public Task PutAsync(string key, object? value, CacheEntryOptions? options = null)
    {
        Entries[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        Entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<long> RemoveByPatternAsync(string pattern)
    {
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
        var keys = Entries.Keys.Where(k => regex.IsMatch(k)).ToList();
        foreach (var key in keys)
        {
            Entries.TryRemove(key, out _);
        }
        return Task.FromResult((long)keys.Count);
    }

    public Task<bool> ContainsAsync(string key) => Task.FromResult(Entries.ContainsKey(key));

    public T? Get<T>(string key, bool independent = false) => throw new NotSupportedException();
    public bool TryGet<T>(string key, out T? value) => throw new NotSupportedException();
    public T? Get<T>(string key, Func<CacheEntryOptions, T> acquirer, bool independent = false, bool allowRecursion = false) => throw new NotSupportedException();
    public Task<T?> GetAsync<T>(string key, Func<CacheEntryOptions, Task<T>> acquirer, bool independent = false, bool allowRecursion = false) => throw new NotSupportedException();
    public Smartstore.Caching.ISet GetHashSet(string key, Func<IEnumerable<string>>? acquirer = null, bool preserveOrder = false) => throw new NotSupportedException();
    public Task<Smartstore.Caching.ISet> GetHashSetAsync(string key, Func<Task<IEnumerable<string>>>? acquirer = null, bool preserveOrder = false) => throw new NotSupportedException();
    public void Put(string key, object? value, CacheEntryOptions? options = null) => throw new NotSupportedException();
    public bool Contains(string key) => throw new NotSupportedException();
    public void Remove(string key) => throw new NotSupportedException();
    public IEnumerable<string> Keys(string? pattern = "*") => throw new NotSupportedException();
    public IAsyncEnumerable<string> KeysAsync(string? pattern = "*") => throw new NotSupportedException();
    public long RemoveByPattern(string pattern) => throw new NotSupportedException();
    public IDistributedLock GetLock(string key) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public Task ClearAsync() => throw new NotSupportedException();
    public TimeSpan? GetTimeToLive(string key) => throw new NotSupportedException();
    public Task<TimeSpan?> GetTimeToLiveAsync(string key) => throw new NotSupportedException();
    public void SetTimeToLive(string key, TimeSpan? duration) => throw new NotSupportedException();
    public Task SetTimeToLiveAsync(string key, TimeSpan? duration) => throw new NotSupportedException();
    public void Dispose() { }
}

/// <summary>
/// <see cref="IWordPressApiClient"/> returning what the test sets, counting the calls.
/// </summary>
internal sealed class FakeApiClient : IWordPressApiClient
{
    public Func<WpPostQuery, WpCollection<WpPost>> Posts { get; set; } = _ => new WpCollection<WpPost>();
    public Func<WpPostQuery, WpCollection<WpPost>> Pages { get; set; } = _ => new WpCollection<WpPost>();
    public Func<IReadOnlyList<WpTerm>> Categories { get; set; } = () => [];
    public Func<string?, IReadOnlyList<WpTerm>> Tags { get; set; } = _ => [];

    public List<WpPostQuery> PostQueries { get; } = [];
    public int Calls { get; private set; }

    public Task<WpCollection<WpPost>> GetPostsAsync(WpPostQuery query, CancellationToken cancelToken = default)
    {
        Calls++;
        PostQueries.Add(query);
        return Task.FromResult(Posts(query));
    }

    public Task<WpCollection<WpPost>> GetPagesAsync(WpPostQuery query, CancellationToken cancelToken = default)
    {
        Calls++;
        return Task.FromResult(Pages(query));
    }

    public Task<IReadOnlyList<WpTerm>> GetCategoriesAsync(CancellationToken cancelToken = default)
    {
        Calls++;
        return Task.FromResult(Categories());
    }

    public Task<IReadOnlyList<WpTerm>> GetTagsAsync(IReadOnlyCollection<int>? include = null, string? slug = null, CancellationToken cancelToken = default)
    {
        Calls++;
        return Task.FromResult(Tags(slug));
    }

    public Task<WpMedia?> GetMediaAsync(int id, CancellationToken cancelToken = default)
        => Task.FromResult<WpMedia?>(null);

    public static WordPressApiException Down() => new(WordPressFailure.Unavailable, "WordPress could not be reached.");
}

internal static class WpSamples
{
    public static WpPost Post(int id, string slug, string status = "publish", string title = "Zincir bakımı", string content = "<p>Metin</p>", params WpTerm[] terms)
        => new()
        {
            Id = id,
            Slug = slug,
            Status = status,
            Type = "post",
            DateGmt = new DateTime(2026, 10, 1, 8, 0, 0),
            ModifiedGmt = new DateTime(2026, 10, 2, 8, 0, 0),
            Title = new WpRendered { Rendered = title },
            Excerpt = new WpRendered { Rendered = "<p>Kısa özet [&hellip;]</p>" },
            Content = new WpRendered { Rendered = content },
            Embedded = new WpEmbedded { Terms = [terms] }
        };

    public static WpTerm Category(int id, string slug, string name = "Bakım", int count = 1)
        => new() { Id = id, Slug = slug, Name = name, Taxonomy = "category", Count = count };

    public static WpTerm Tag(int id, string slug, string name = "etiket")
        => new() { Id = id, Slug = slug, Name = name, Taxonomy = "post_tag" };
}
