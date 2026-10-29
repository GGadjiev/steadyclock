namespace MyTimeTracker.Core.Models;

/// <summary>
/// One continuous active interval. Idle time is represented by the gaps between
/// segments rather than by negative or "paused" segments.
/// </summary>
public sealed class SessionSegment
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }

    /// <summary>
    /// Duration measured by the monotonic clock. This is authoritative for totals;
    /// wall-clock timestamps are used only for display and day buckets.
    /// </summary>
    public long DurationMs { get; set; }

    public int Ordinal { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, DurationMs));

    public SessionSegment Clone() => new()
    {
        Id = Id,
        SessionId = SessionId,
        StartUtc = StartUtc,
        EndUtc = EndUtc,
        DurationMs = DurationMs,
        Ordinal = Ordinal,
        CreatedUtc = CreatedUtc
    };
}
