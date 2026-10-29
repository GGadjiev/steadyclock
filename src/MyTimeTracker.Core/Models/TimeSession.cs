namespace MyTimeTracker.Core.Models;

/// <summary>A tracked time session and its active intervals.</summary>
public sealed class TimeSession
{
    public long Id { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string Comment { get; set; } = string.Empty;
    public SessionSource Source { get; set; }
    public TimerStatus State { get; set; } = TimerStatus.Stopped;
    public DateTimeOffset? LastHeartbeatUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<SessionSegment> Segments { get; set; } = new();

    public bool IsActive => EndUtc is null && State != TimerStatus.Stopped;

    public TimeSpan Duration => TimeSpan.FromMilliseconds(
        Segments.Sum(segment => Math.Max(0L, segment.DurationMs)));

    public DateTimeOffset GetEffectiveEndUtc() =>
        EndUtc ?? Segments.Where(segment => segment.EndUtc.HasValue)
            .Select(segment => segment.EndUtc!.Value)
            .DefaultIfEmpty(StartUtc)
            .Max();

    public TimeSession DeepClone() => new()
    {
        Id = Id,
        StartUtc = StartUtc,
        EndUtc = EndUtc,
        Comment = Comment,
        Source = Source,
        State = State,
        LastHeartbeatUtc = LastHeartbeatUtc,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        Segments = Segments.Select(segment => segment.Clone()).ToList()
    };
}
