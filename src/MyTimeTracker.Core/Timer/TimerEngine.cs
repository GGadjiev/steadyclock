using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Timer;

/// <summary>
/// State machine for the single tracker. All durations are measured with the
/// monotonic clock. Wall-clock timestamps are recorded only for display/history.
/// The engine is deterministic and persistence-free; tests drive it with fake clocks.
/// </summary>
public sealed class TimerEngine
{
    public static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(10);

    private readonly IWallClock _wallClock;
    private readonly IMonotonicClock _monotonicClock;
    private readonly IIdleTimeProvider _idleTimeProvider;

    private DateTimeOffset? _lastTickWallUtc;
    private TimeSpan? _lastTickMonotonic;
    private TimeSpan? _openSegmentStartMonotonic;
    private TimeSpan _idleAtAutoPause;

    public TimerEngine(
        IWallClock wallClock,
        IMonotonicClock monotonicClock,
        IIdleTimeProvider idleTimeProvider)
    {
        _wallClock = wallClock;
        _monotonicClock = monotonicClock;
        _idleTimeProvider = idleTimeProvider;
    }

    public TimerStatus Status { get; private set; } = TimerStatus.Stopped;
    public TimeSession? ActiveSession { get; private set; }
    public IdlePauseCause LastPauseCause { get; private set; } = IdlePauseCause.None;
    public bool HasActiveSession => ActiveSession is not null && Status != TimerStatus.Stopped;
    public TimeSpan MonotonicNow => _monotonicClock.Elapsed;
    public DateTimeOffset UtcNow => _wallClock.UtcNow;
    public TimeZoneInfo LocalTimeZone => _wallClock.LocalTimeZone;

    public TimeSpan CurrentDuration
    {
        get
        {
            if (ActiveSession is null || Status == TimerStatus.Stopped)
            {
                return TimeSpan.Zero;
            }

            var closedMs = ActiveSession.Segments
                .Where(segment => segment.EndUtc.HasValue)
                .Sum(segment => Math.Max(0L, segment.DurationMs));

            if (Status == TimerStatus.Running && _openSegmentStartMonotonic.HasValue)
            {
                var openMs = (_monotonicClock.Elapsed - _openSegmentStartMonotonic.Value).TotalMilliseconds;
                closedMs += Math.Max(0, (long)openMs);
            }

            return TimeSpan.FromMilliseconds(Math.Max(0, closedMs));
        }
    }

    public event EventHandler<TimerStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AutoPausedEventArgs>? AutoPaused;
    public event EventHandler? AutoResumed;
    public event EventHandler<RecoveredEventArgs>? Recovered;

    /// <summary>
    /// Restores an unfinished session. Any segment that was left open is closed at
    /// the last persisted heartbeat; the application downtime is never counted.
    /// A previously running session continues with a new segment starting now.
    /// </summary>
    public RecoveredEventArgs Restore(TimeSession? session)
    {
        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var idle = GetIdleTimeSafe();
        ActiveSession = session;
        LastPauseCause = IdlePauseCause.None;
        _openSegmentStartMonotonic = null;

        if (session is null || session.EndUtc is not null || session.State == TimerStatus.Stopped)
        {
            Status = TimerStatus.Stopped;
            if (session is not null)
            {
                session.EndUtc ??= nowWall;
                session.State = TimerStatus.Stopped;
            }

            SetLastTick(nowWall, nowMono);
            return new RecoveredEventArgs
            {
                HadActiveSession = false,
                Status = Status,
                ExcludedGap = TimeSpan.Zero
            };
        }

        var heartbeat = session.LastHeartbeatUtc ?? nowWall;
        if (heartbeat > nowWall)
        {
            heartbeat = nowWall;
        }

        var excludedGap = TimeSpan.Zero;
        foreach (var segment in session.Segments.Where(segment => segment.EndUtc is null))
        {
            var segmentEnd = heartbeat < segment.StartUtc ? segment.StartUtc : heartbeat;
            segment.EndUtc = segmentEnd;
            excludedGap = nowWall - segmentEnd;
        }

        if (session.State == TimerStatus.PausedIdle)
        {
            Status = TimerStatus.PausedIdle;
            LastPauseCause = IdlePauseCause.Recovered;
            _idleAtAutoPause = idle < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : idle;
        }
        else if (session.State == TimerStatus.PausedManual)
        {
            Status = TimerStatus.PausedManual;
            LastPauseCause = IdlePauseCause.None;
        }
        else
        {
            // The tracker was running when the process stopped. Continue with a fresh
            // active segment at the current moment; the shutdown gap is excluded.
            Status = TimerStatus.Running;
            session.State = TimerStatus.Running;
            AddOpenSegment(nowWall, nowMono);
        }

        session.UpdatedUtc = nowWall;
        SetLastTick(nowWall, nowMono);
        Recovered?.Invoke(this, new RecoveredEventArgs
        {
            HadActiveSession = true,
            Status = Status,
            ExcludedGap = excludedGap < TimeSpan.Zero ? TimeSpan.Zero : excludedGap
        });
        return new RecoveredEventArgs
        {
            HadActiveSession = true,
            Status = Status,
            ExcludedGap = excludedGap < TimeSpan.Zero ? TimeSpan.Zero : excludedGap
        };
    }

    public void Start(string? comment = null)
    {
        if (Status != TimerStatus.Stopped)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        ActiveSession = new TimeSession
        {
            StartUtc = nowWall,
            EndUtc = null,
            Comment = comment?.Trim() ?? string.Empty,
            Source = SessionSource.Automatic,
            State = TimerStatus.Running,
            LastHeartbeatUtc = nowWall,
            CreatedUtc = nowWall,
            UpdatedUtc = nowWall
        };
        AddOpenSegment(nowWall, nowMono);
        Status = TimerStatus.Running;
        LastPauseCause = IdlePauseCause.None;
        SetLastTick(nowWall, nowMono);
        RaiseStatusChanged(previous, TimerStatus.Running, IdlePauseCause.None, nowWall);
    }

    public void PauseManually()
    {
        if (Status != TimerStatus.Running || ActiveSession is null)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        CloseOpenSegment(nowWall, nowMono);
        ActiveSession.State = TimerStatus.PausedManual;
        ActiveSession.UpdatedUtc = nowWall;
        Status = TimerStatus.PausedManual;
        LastPauseCause = IdlePauseCause.None;
        SetLastTick(nowWall, nowMono);
        RaiseStatusChanged(previous, TimerStatus.PausedManual, IdlePauseCause.None, nowWall);
    }

    public void ResumeManually()
    {
        if (Status is not (TimerStatus.PausedManual or TimerStatus.PausedIdle) || ActiveSession is null)
        {
            return;
        }

        ResumeCore(autoResumed: false);
    }

    public void Stop()
    {
        if (Status == TimerStatus.Stopped || ActiveSession is null)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        if (Status == TimerStatus.Running)
        {
            CloseOpenSegment(nowWall, nowMono);
        }

        ActiveSession.EndUtc = nowWall < ActiveSession.StartUtc ? ActiveSession.StartUtc : nowWall;
        ActiveSession.State = TimerStatus.Stopped;
        ActiveSession.UpdatedUtc = nowWall;
        Status = TimerStatus.Stopped;
        LastPauseCause = IdlePauseCause.None;
        _openSegmentStartMonotonic = null;
        SetLastTick(nowWall, nowMono);
        RaiseStatusChanged(previous, TimerStatus.Stopped, IdlePauseCause.None, nowWall);
    }

    /// <summary>Polled roughly once per second by the UI, with a cheap idle check.</summary>
    public void Tick(TimeSpan idleThreshold)
    {
        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;

        if (_lastTickMonotonic is null || _lastTickWallUtc is null)
        {
            SetLastTick(nowWall, nowMono);
            return;
        }

        var monoDelta = nowMono - _lastTickMonotonic.Value;
        if (monoDelta < TimeSpan.Zero)
        {
            SetLastTick(nowWall, nowMono);
            return;
        }

        if (monoDelta > MaxTickGap)
        {
            if (Status == TimerStatus.Running && ActiveSession is not null)
            {
                // The last observed tick is the end of the counted interval. The whole
                // sleep/freeze gap is excluded. DurationMs was updated on that tick.
                var gapEnd = _lastTickWallUtc.Value;
                var previous = Status;
                var open = GetOpenSegment();
                if (open is not null)
                {
                    open.EndUtc = gapEnd < open.StartUtc ? open.StartUtc : gapEnd;
                }

                MarkAutoPaused(
                    IdlePauseCause.SystemGap,
                    nowWall,
                    GetIdleTimeSafe(),
                    previous);
            }

            SetLastTick(nowWall, nowMono);
            return;
        }

        if (Status == TimerStatus.Running && ActiveSession is not null)
        {
            UpdateOpenSegmentDuration(nowMono);

            if (idleThreshold < TimeSpan.Zero)
            {
                idleThreshold = TimeSpan.Zero;
            }

            var idle = GetIdleTimeSafe();
            if (idle >= idleThreshold)
            {
                AutoPauseForIdle(nowWall, nowMono, idle, idleThreshold);
                SetLastTick(nowWall, nowMono);
                return;
            }
        }
        else if (Status == TimerStatus.PausedIdle && ActiveSession is not null)
        {
            // A decrease in system idle time can only be caused by new user input.
            // This check is based on GetLastInputInfo, not on the wall clock, so a
            // time-zone or system-clock change cannot resume the timer by itself.
            var idle = GetIdleTimeSafe();
            if (idle + TimeSpan.FromMilliseconds(100) < _idleAtAutoPause)
            {
                ResumeCore(autoResumed: true);
            }
        }

        SetLastTick(nowWall, nowMono);
    }

    /// <summary>Windows is going to suspend. Stop counting immediately.</summary>
    public void HandleSystemSuspend()
    {
        if (Status != TimerStatus.Running || ActiveSession is null)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        CloseOpenSegment(nowWall, nowMono);
        MarkAutoPaused(IdlePauseCause.Sleep, nowWall, GetIdleTimeSafe(), previous);
        SetLastTick(nowWall, nowMono);
    }

    public void HandleSessionLock()
    {
        if (Status != TimerStatus.Running || ActiveSession is null)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        CloseOpenSegment(nowWall, nowMono);
        MarkAutoPaused(IdlePauseCause.ScreenLock, nowWall, GetIdleTimeSafe(), previous);
        SetLastTick(nowWall, nowMono);
    }

    /// <summary>
    /// Unlock is an explicit input event. Only an automatic pause is resumed;
    /// a manual pause is deliberately left untouched.
    /// </summary>
    public void HandleSessionUnlock()
    {
        if (Status == TimerStatus.PausedIdle && ActiveSession is not null)
        {
            ResumeCore(autoResumed: true);
        }
    }

    public void SetComment(string? comment)
    {
        if (ActiveSession is not null)
        {
            ActiveSession.Comment = comment ?? string.Empty;
            ActiveSession.UpdatedUtc = _wallClock.UtcNow;
        }
    }

    public void AssignActiveSessionId(long id)
    {
        if (ActiveSession is not null && ActiveSession.Id == 0)
        {
            ActiveSession.Id = id;
        }
    }

    /// <summary>
    /// Creates a detached transaction payload. While running, it also advances the
    /// heartbeat timestamp and refreshes the open segment duration.
    /// </summary>
    public TimeSession? CreateSnapshot()
    {
        if (ActiveSession is null)
        {
            return null;
        }

        var nowWall = _wallClock.UtcNow;
        if (Status == TimerStatus.Running)
        {
            UpdateOpenSegmentDuration(_monotonicClock.Elapsed);
            ActiveSession.LastHeartbeatUtc = nowWall;
        }

        ActiveSession.State = Status;
        ActiveSession.UpdatedUtc = nowWall;
        return ActiveSession.DeepClone();
    }

    private void ResumeCore(bool autoResumed)
    {
        if (ActiveSession is null)
        {
            return;
        }

        var nowWall = _wallClock.UtcNow;
        var nowMono = _monotonicClock.Elapsed;
        var previous = Status;
        AddOpenSegment(nowWall, nowMono);
        ActiveSession.State = TimerStatus.Running;
        ActiveSession.UpdatedUtc = nowWall;
        Status = TimerStatus.Running;
        LastPauseCause = IdlePauseCause.None;
        SetLastTick(nowWall, nowMono);
        RaiseStatusChanged(previous, TimerStatus.Running, IdlePauseCause.None, nowWall);
        if (autoResumed)
        {
            AutoResumed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AutoPauseForIdle(
        DateTimeOffset nowWall,
        TimeSpan nowMono,
        TimeSpan idle,
        TimeSpan idleThreshold)
    {
        if (ActiveSession is null)
        {
            return;
        }

        var open = GetOpenSegment();
        if (open is null)
        {
            return;
        }

        var elapsed = nowMono - (_openSegmentStartMonotonic ?? nowMono);
        var counted = elapsed - idle;
        if (counted < TimeSpan.Zero)
        {
            counted = TimeSpan.Zero;
        }

        var effectiveEnd = nowWall - idle;
        if (effectiveEnd < open.StartUtc)
        {
            effectiveEnd = open.StartUtc;
        }

        if (effectiveEnd > nowWall)
        {
            effectiveEnd = nowWall;
        }

        open.EndUtc = effectiveEnd;
        open.DurationMs = Math.Max(0, (long)Math.Round(counted.TotalMilliseconds));
        _openSegmentStartMonotonic = null;
        var previous = Status;
        MarkAutoPaused(IdlePauseCause.Idle, nowWall, idle, previous);
        _ = idleThreshold;
    }

    private void MarkAutoPaused(
        IdlePauseCause cause,
        DateTimeOffset detectedAtUtc,
        TimeSpan idleAtPause,
        TimerStatus previous)
    {
        if (ActiveSession is null)
        {
            return;
        }

        ActiveSession.State = TimerStatus.PausedIdle;
        ActiveSession.EndUtc = null;
        ActiveSession.UpdatedUtc = detectedAtUtc;
        Status = TimerStatus.PausedIdle;
        LastPauseCause = cause;
        _idleAtAutoPause = idleAtPause < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : idleAtPause;
        _openSegmentStartMonotonic = null;
        RaiseStatusChanged(previous, TimerStatus.PausedIdle, cause, detectedAtUtc);
        AutoPaused?.Invoke(this, new AutoPausedEventArgs
        {
            Cause = cause,
            AtUtc = detectedAtUtc,
            IdleTime = idleAtPause < TimeSpan.Zero ? TimeSpan.Zero : idleAtPause
        });
    }

    private void AddOpenSegment(DateTimeOffset nowWall, TimeSpan nowMono)
    {
        if (ActiveSession is null)
        {
            return;
        }

        ActiveSession.Segments.Add(new SessionSegment
        {
            SessionId = ActiveSession.Id,
            StartUtc = nowWall,
            EndUtc = null,
            DurationMs = 0,
            Ordinal = ActiveSession.Segments.Count,
            CreatedUtc = nowWall
        });
        _openSegmentStartMonotonic = nowMono;
    }

    private void CloseOpenSegment(DateTimeOffset nowWall, TimeSpan nowMono)
    {
        var open = GetOpenSegment();
        if (open is null)
        {
            _openSegmentStartMonotonic = null;
            return;
        }

        var elapsed = nowMono - (_openSegmentStartMonotonic ?? nowMono);
        open.DurationMs = Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
        open.EndUtc = nowWall < open.StartUtc ? open.StartUtc : nowWall;
        _openSegmentStartMonotonic = null;
    }

    private void UpdateOpenSegmentDuration(TimeSpan nowMono)
    {
        var open = GetOpenSegment();
        if (open is null)
        {
            return;
        }

        var elapsed = nowMono - (_openSegmentStartMonotonic ?? nowMono);
        open.DurationMs = Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
    }

    private SessionSegment? GetOpenSegment() =>
        ActiveSession?.Segments.LastOrDefault(segment => segment.EndUtc is null);

    private void RaiseStatusChanged(
        TimerStatus previous,
        TimerStatus current,
        IdlePauseCause cause,
        DateTimeOffset atUtc)
    {
        StatusChanged?.Invoke(this, new TimerStatusChangedEventArgs
        {
            PreviousStatus = previous,
            Status = current,
            PauseCause = cause,
            AtUtc = atUtc
        });
    }

    private void SetLastTick(DateTimeOffset wall, TimeSpan monotonic)
    {
        _lastTickWallUtc = wall;
        _lastTickMonotonic = monotonic;
    }

    private TimeSpan GetIdleTimeSafe()
    {
        try
        {
            var idle = _idleTimeProvider.GetIdleTime();
            return idle < TimeSpan.Zero ? TimeSpan.Zero : idle;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
