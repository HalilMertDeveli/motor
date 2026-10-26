using System.Collections.Concurrent;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Remembers which "new product" forms were already submitted, so a double click, a retried request or a
/// second tab cannot create the same product twice. Kept in memory for an hour; the buttons are disabled
/// while saving as well.
/// </summary>
public class SubmissionRegistry
{
    static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    record Entry(DateTime StartedUtc, int ProductId);

    public enum State
    {
        /// <summary>First time: go ahead.</summary>
        Started,
        /// <summary>The same submission is still being saved.</summary>
        InProgress,
        /// <summary>Already saved: <c>productId</c> is the result.</summary>
        Completed
    }

    public State Begin(string key, out int productId)
    {
        Purge();
        productId = 0;

        var entry = new Entry(DateTime.UtcNow, 0);
        if (_entries.TryAdd(key, entry))
        {
            return State.Started;
        }

        if (_entries.TryGetValue(key, out var existing) && existing.ProductId > 0)
        {
            productId = existing.ProductId;
            return State.Completed;
        }

        return State.InProgress;
    }

    public void Complete(string key, int productId)
        => _entries[key] = new Entry(DateTime.UtcNow, productId);

    /// <summary>
    /// The submission failed (e.g. validation): the same form may be sent again.
    /// </summary>
    public void Abandon(string key)
        => _entries.TryRemove(key, out _);

    private void Purge()
    {
        var limit = DateTime.UtcNow - Lifetime;
        foreach (var (key, entry) in _entries)
        {
            if (entry.StartedUtc < limit)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
