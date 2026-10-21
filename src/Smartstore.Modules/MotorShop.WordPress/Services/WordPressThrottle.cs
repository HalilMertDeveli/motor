using System.Threading.RateLimiting;

namespace MotorShop.WordPress.Services;

/// <summary>
/// Limits how often one visitor can make MOTOR call WordPress (cache misses: unknown slugs, searches, deep pages)
/// and how many calls run at the same time. Cached pages are not limited.
/// </summary>
/// <remarks>Per application instance.</remarks>
public sealed class WordPressThrottle : IDisposable
{
    public const int MaxConcurrentRequests = 8;

    private readonly PartitionedRateLimiter<string> _perVisitor = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            // A burst of 20, then 1 call every 2 seconds.
            TokenLimit = 20,
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentRequests, MaxConcurrentRequests);

    /// <returns><c>true</c> if a WordPress call may be made for <paramref name="visitorKey"/> now. Counts the call.</returns>
    public bool TryAcquire(string visitorKey)
    {
        using var lease = _perVisitor.AttemptAcquire(visitorKey.NullEmpty() ?? "unknown");
        return lease.IsAcquired;
    }

    /// <summary>
    /// Waits briefly for a free slot. <c>false</c> means WordPress is already busy with requests from MOTOR.
    /// </summary>
    public Task<bool> EnterAsync(CancellationToken cancelToken)
        => _concurrency.WaitAsync(TimeSpan.FromSeconds(2), cancelToken);

    public void Exit()
        => _concurrency.Release();

    public void Dispose()
    {
        _perVisitor.Dispose();
        _concurrency.Dispose();
    }
}

/// <summary>
/// A visitor made too many requests that needed WordPress. The storefront answers with 429.
/// </summary>
public sealed class WordPressRateLimitedException : Exception
{
    public WordPressRateLimitedException()
        : base("Too many blog requests.")
    {
    }
}
