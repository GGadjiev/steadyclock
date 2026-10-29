using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Common;

public enum ManualValidationError
{
    None = 0,
    StartInFuture,
    EndInFuture,
    EndNotAfterStart,
    OverlapsExisting,
    ActiveSessionCannotBeEdited
}

public sealed record ManualValidationResult(bool IsValid, ManualValidationError Error)
{
    public static readonly ManualValidationResult Valid = new(true, ManualValidationError.None);
    public static ManualValidationResult Invalid(ManualValidationError error) => new(false, error);
}

public sealed record ManualSessionInput(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Comment);

public static class ManualSessionEditor
{
    public static ManualValidationResult Validate(
        ManualSessionInput input,
        DateTimeOffset nowUtc,
        IEnumerable<TimeSession> sessionsOverlappingRange,
        long? editingSessionId = null,
        bool editingActiveSession = false)
    {
        if (editingActiveSession)
        {
            return ManualValidationResult.Invalid(ManualValidationError.ActiveSessionCannotBeEdited);
        }

        if (input.StartUtc > nowUtc)
        {
            return ManualValidationResult.Invalid(ManualValidationError.StartInFuture);
        }

        if (input.EndUtc > nowUtc)
        {
            return ManualValidationResult.Invalid(ManualValidationError.EndInFuture);
        }

        if (input.EndUtc <= input.StartUtc)
        {
            return ManualValidationResult.Invalid(ManualValidationError.EndNotAfterStart);
        }

        foreach (var session in sessionsOverlappingRange)
        {
            if (editingSessionId.HasValue && session.Id == editingSessionId.Value)
            {
                continue;
            }

            if (OverlapsTrackedIntervals(session, input.StartUtc, input.EndUtc, nowUtc))
            {
                return ManualValidationResult.Invalid(ManualValidationError.OverlapsExisting);
            }
        }

        return ManualValidationResult.Valid;
    }

    /// <summary>
    /// A record occupies its tracked intervals, not the idle gaps inside an
    /// automatic session. This lets the user add a manual record during a pause
    /// while still preventing double-counted time.
    /// </summary>
    private static bool OverlapsTrackedIntervals(
        TimeSession session,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset nowUtc)
    {
        if (session.Segments.Count == 0)
        {
            var fallbackEnd = session.EndUtc ?? nowUtc;
            return session.StartUtc < endUtc && fallbackEnd > startUtc;
        }

        foreach (var segment in session.Segments)
        {
            var segmentEnd = segment.EndUtc ?? (session.State == TimerStatus.Running
                ? nowUtc
                : segment.StartUtc.Add(segment.Duration));
            if (segmentEnd <= segment.StartUtc)
            {
                continue;
            }

            if (segment.StartUtc < endUtc && segmentEnd > startUtc)
            {
                return true;
            }
        }

        return false;
    }

    public static TimeSession Create(
        ManualSessionInput input,
        DateTimeOffset nowUtc)
    {
        var session = new TimeSession
        {
            StartUtc = input.StartUtc,
            EndUtc = input.EndUtc,
            Comment = input.Comment?.Trim() ?? string.Empty,
            Source = SessionSource.Manual,
            State = TimerStatus.Stopped,
            LastHeartbeatUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = input.StartUtc,
            EndUtc = input.EndUtc,
            DurationMs = Math.Max(0, (long)Math.Round((input.EndUtc - input.StartUtc).TotalMilliseconds)),
            Ordinal = 0,
            CreatedUtc = nowUtc
        });
        return session;
    }

    /// <summary>
    /// Applies an edit. Changing the time range of an automatic session converts it
    /// to a manual record because the original idle gaps can no longer be represented.
    /// A comment-only edit preserves the tracked segments.
    /// </summary>
    public static void ApplyEdit(
        TimeSession session,
        ManualSessionInput input,
        DateTimeOffset nowUtc,
        bool? timesChanged = null)
    {
        var effectiveTimesChanged = timesChanged ??
            (session.StartUtc != input.StartUtc || session.EndUtc != input.EndUtc);

        session.Comment = input.Comment?.Trim() ?? string.Empty;
        session.UpdatedUtc = nowUtc;

        if (!effectiveTimesChanged)
        {
            return;
        }

        session.StartUtc = input.StartUtc;
        session.EndUtc = input.EndUtc;
        session.Source = SessionSource.Manual;
        session.State = TimerStatus.Stopped;
        session.LastHeartbeatUtc = null;
        session.Segments.Clear();
        session.Segments.Add(new SessionSegment
        {
            StartUtc = input.StartUtc,
            EndUtc = input.EndUtc,
            DurationMs = Math.Max(0, (long)Math.Round((input.EndUtc - input.StartUtc).TotalMilliseconds)),
            Ordinal = 0,
            CreatedUtc = nowUtc
        });
    }
}
