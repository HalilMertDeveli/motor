using Microsoft.Extensions.Caching.Memory;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Limits the iyzico retrieve calls triggered by reloading the return page: at most one per order every
/// <see cref="MinInterval"/> and <see cref="MaxAttempts"/> per order within <see cref="Window"/>.
/// Callback, webhook and the reconciliation task are not limited; they settle the payment anyway.
/// </summary>
/// <remarks>
/// Per application instance. The return page additionally skips the call while the stored attempt was updated
/// less than <see cref="MinInterval"/> ago, which also holds across instances.
/// </remarks>
public class IyzicoReturnThrottle
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    public const int MaxAttempts = 20;

    private readonly IMemoryCache _cache;

    public IyzicoReturnThrottle(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <returns><c>true</c> if a retrieve may be made for <paramref name="orderId"/> now. Counts the attempt.</returns>
    public bool TryAcquire(int orderId, DateTime nowUtc)
    {
        var entry = _cache.GetOrCreate($"motorshop.iyzico.return:{orderId}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = Window;
            e.Size = 1;
            return new Counter();
        });

        lock (entry)
        {
            if (entry.Count >= MaxAttempts || (entry.LastUtc.HasValue && nowUtc - entry.LastUtc.Value < MinInterval))
            {
                return false;
            }

            entry.Count++;
            entry.LastUtc = nowUtc;
            return true;
        }
    }

    private sealed class Counter
    {
        public int Count;
        public DateTime? LastUtc;
    }
}
