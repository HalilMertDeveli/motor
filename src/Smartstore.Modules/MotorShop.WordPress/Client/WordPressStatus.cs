namespace MotorShop.WordPress.Client;

/// <summary>
/// Connection health shown in the admin. Per application instance, kept in memory.
/// Messages never contain URLs with queries, credentials or response bodies.
/// </summary>
public sealed class WordPressStatus
{
    const int MaxErrors = 20;

    private readonly object _lock = new();
    private readonly LinkedList<WordPressError> _errors = new();

    public DateTime? LastSuccessUtc { get; private set; }
    public DateTime? LastFailureUtc { get; private set; }
    public DateTime? LastCacheClearUtc { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    public IReadOnlyList<WordPressError> RecentErrors
    {
        get
        {
            lock (_lock)
            {
                return [.. _errors];
            }
        }
    }

    public void RecordSuccess(DateTime? nowUtc = null)
    {
        lock (_lock)
        {
            LastSuccessUtc = nowUtc ?? DateTime.UtcNow;
            ConsecutiveFailures = 0;
        }
    }

    public void RecordFailure(string message, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        lock (_lock)
        {
            LastFailureUtc = now;
            ConsecutiveFailures++;

            _errors.AddFirst(new WordPressError(now, message.Truncate(300)));
            while (_errors.Count > MaxErrors)
            {
                _errors.RemoveLast();
            }
        }
    }

    public void RecordCacheClear(DateTime? nowUtc = null)
    {
        lock (_lock)
        {
            LastCacheClearUtc = nowUtc ?? DateTime.UtcNow;
        }
    }
}

public sealed record WordPressError(DateTime Utc, string Message);
