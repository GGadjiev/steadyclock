using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Common;

public sealed record SessionDaySlice(
    long SessionId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    TimeSpan Duration,
    bool IsActive,
    bool IsRunning,
    SessionSource Source,
    string Comment)
{
    public bool IsManual => Source == SessionSource.Manual;
}

public sealed record LocalDaySummary(
    DateOnly Date,
    TimeSpan TotalDuration,
    IReadOnlyList<SessionDaySlice> Sessions)
{
    public int SessionCount => Sessions.Select(session => session.SessionId).Distinct().Count();
    public bool HasSessions => Sessions.Count > 0;
}

/// <summary>
/// Builds per-local-day slices. Automatic segments that cross midnight are split
/// proportionally between days; total duration always comes from monotonic duration
/// stored on the segment.
/// </summary>
public static class SessionTimeline
{
    public static LocalDaySummary BuildDay(
        IEnumerable<TimeSession> sessions,
        DateOnly localDate,
        TimeZoneInfo timeZone,
        DateTimeOffset nowUtc)
    {
        var (dayStart, dayEnd) = TimeText.GetLocalDayRangeUtc(localDate, timeZone);
        var result = new List<SessionDaySlice>();
        long totalMs = 0;

        foreach (var session in sessions)
        {
            var effectiveSessionEnd = session.EndUtc ?? nowUtc;
            if (session.StartUtc >= dayEnd || effectiveSessionEnd < dayStart)
            {
                continue;
            }

            DateTimeOffset? firstStart = null;
            DateTimeOffset? lastEnd = null;
            long sessionMs = 0;

            foreach (var segment in session.Segments)
            {
                var segmentEnd = segment.EndUtc ?? (session.State == TimerStatus.Running
                    ? nowUtc
                    : session.LastHeartbeatUtc ?? nowUtc);
                if (segmentEnd < segment.StartUtc)
                {
                    segmentEnd = segment.StartUtc;
                }

                var overlapStart = segment.StartUtc > dayStart ? segment.StartUtc : dayStart;
                var overlapEnd = segmentEnd < dayEnd ? segmentEnd : dayEnd;
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                var wallTicks = Math.Max(0, (segmentEnd - segment.StartUtc).Ticks);
                long contributionMs;
                if (wallTicks == 0)
                {
                    contributionMs = segment.DurationMs;
                }
                else if (overlapStart == segment.StartUtc && overlapEnd == segmentEnd)
                {
                    contributionMs = segment.DurationMs;
                }
                else
                {
                    var overlapTicks = (overlapEnd - overlapStart).Ticks;
                    contributionMs = (long)Math.Round(
                        segment.DurationMs * (double)overlapTicks / wallTicks,
                        MidpointRounding.AwayFromZero);
                    contributionMs = Math.Clamp(contributionMs, 0, Math.Max(0, segment.DurationMs));
                }

                sessionMs += contributionMs;
                firstStart = firstStart is null || overlapStart < firstStart ? overlapStart : firstStart;
                lastEnd = lastEnd is null || overlapEnd > lastEnd ? overlapEnd : lastEnd;
            }

            if (firstStart is null || lastEnd is null || lastEnd < firstStart)
            {
                continue;
            }

            var isActive = session.IsActive;
            result.Add(new SessionDaySlice(
                session.Id,
                firstStart.Value,
                lastEnd.Value,
                TimeSpan.FromMilliseconds(Math.Max(0, sessionMs)),
                isActive,
                isActive && session.State == TimerStatus.Running,
                session.Source,
                session.Comment));
            totalMs += sessionMs;
        }

        result.Sort((left, right) => right.StartUtc.CompareTo(left.StartUtc));
        return new LocalDaySummary(localDate, TimeSpan.FromMilliseconds(Math.Max(0, totalMs)), result);
    }

    public static IReadOnlyList<LocalDaySummary> BuildRange(
        IEnumerable<TimeSession> sessions,
        DateOnly firstDate,
        DateOnly lastDate,
        TimeZoneInfo timeZone,
        DateTimeOffset nowUtc)
    {
        var list = sessions.ToList();
        var result = new List<LocalDaySummary>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var summary = BuildDay(list, date, timeZone, nowUtc);
            if (summary.HasSessions)
            {
                result.Add(summary);
            }
        }

        return result;
    }
}
