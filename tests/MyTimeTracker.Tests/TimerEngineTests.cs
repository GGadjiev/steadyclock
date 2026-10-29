using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Timer;

namespace MyTimeTracker.Tests;

public sealed class TimerEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(120);

    private static (TimerEngine Engine, TestClockSet Clocks) CreateEngine(
        DateTimeOffset? start = null)
    {
        var clocks = new TestClockSet(start ?? Start);
        return (new TimerEngine(clocks.Wall, clocks.Monotonic, clocks.Idle), clocks);
    }

    /// <summary>Simulates the 1-second dispatcher poll while the user is active.</summary>
    private static void RunActive(TimerEngine engine, TestClockSet clocks, TimeSpan duration)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            var step = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            clocks.AdvanceActive(step);
            engine.Tick(IdleThreshold);
            remaining -= step;
        }
    }

    private static void RunIdle(TimerEngine engine, TestClockSet clocks, TimeSpan duration)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            var step = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            clocks.AdvanceIdle(step);
            engine.Tick(IdleThreshold);
            remaining -= step;
        }
    }

    private static void ReturnAndTick(TimerEngine engine, TestClockSet clocks, TimeSpan delta)
    {
        clocks.ReturnFromIdle(delta);
        engine.Tick(IdleThreshold);
    }

    [Fact]
    public void Idle_below_threshold_is_not_subtracted()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();

        RunActive(engine, clocks, TimeSpan.FromSeconds(30));
        RunIdle(engine, clocks, TimeSpan.FromSeconds(30));

        Assert.Equal(TimerStatus.Running, engine.Status);
        Assert.InRange(engine.CurrentDuration.TotalSeconds, 59.9, 60.1);
        Assert.Single(engine.ActiveSession!.Segments);
    }

    [Fact]
    public void Idle_at_threshold_pauses_and_removes_entire_idle_period()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();

        RunActive(engine, clocks, TimeSpan.FromMinutes(5));
        RunIdle(engine, clocks, TimeSpan.FromMinutes(2));

        Assert.Equal(TimerStatus.PausedIdle, engine.Status);
        Assert.Equal(IdlePauseCause.Idle, engine.LastPauseCause);
        Assert.InRange(engine.CurrentDuration.TotalMinutes, 4.99, 5.01);
        Assert.True(engine.ActiveSession!.Segments[^1].EndUtc.HasValue);
    }

    [Fact]
    public void Long_idle_before_detection_deducts_from_last_activity_not_threshold_only()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();

        RunActive(engine, clocks, TimeSpan.FromMinutes(5));

        // No poll happened for 30 minutes (e.g. the desktop session was suspended).
        clocks.AdvanceIdle(TimeSpan.FromMinutes(30));
        engine.Tick(IdleThreshold);

        Assert.Equal(TimerStatus.PausedIdle, engine.Status);
        Assert.InRange(engine.CurrentDuration.TotalMinutes, 4.99, 5.01);

        var segment = engine.ActiveSession!.Segments.Single();
        Assert.Equal(Start.AddMinutes(5), segment.EndUtc);
    }

    [Fact]
    public void Auto_pause_resumes_only_after_actual_input()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        RunActive(engine, clocks, TimeSpan.FromMinutes(5));
        RunIdle(engine, clocks, TimeSpan.FromMinutes(2));
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        // No input: idle continues to grow and the tracker remains paused.
        RunIdle(engine, clocks, TimeSpan.FromMinutes(10));
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        // Mouse/keyboard input resets idle and resumes automatically.
        ReturnAndTick(engine, clocks, TimeSpan.FromSeconds(1));
        Assert.Equal(TimerStatus.Running, engine.Status);
        Assert.Equal(2, engine.ActiveSession!.Segments.Count);
    }

    [Fact]
    public void Wall_clock_jump_cannot_auto_resume_an_idle_pause()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        RunActive(engine, clocks, TimeSpan.FromMinutes(1));
        RunIdle(engine, clocks, TimeSpan.FromMinutes(2));
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        // A system clock/time-zone adjustment must not look like mouse input.
        clocks.Wall.Advance(TimeSpan.FromHours(2));
        engine.Tick(IdleThreshold);

        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        ReturnAndTick(engine, clocks, TimeSpan.Zero);
        Assert.Equal(TimerStatus.Running, engine.Status);
    }

    [Fact]
    public void Manual_pause_never_resumes_automatically()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        RunActive(engine, clocks, TimeSpan.FromMinutes(3));

        engine.PauseManually();
        Assert.Equal(TimerStatus.PausedManual, engine.Status);
        var pausedDuration = engine.CurrentDuration;

        clocks.ReturnFromIdle(TimeSpan.FromMinutes(20));
        engine.Tick(IdleThreshold);
        Assert.Equal(TimerStatus.PausedManual, engine.Status);
        Assert.Equal(IdlePauseCause.None, engine.LastPauseCause);
        Assert.Equal(pausedDuration, engine.CurrentDuration);

        engine.ResumeManually();
        Assert.Equal(TimerStatus.Running, engine.Status);
    }

    [Fact]
    public void Several_idle_periods_are_summed_as_separate_segments()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();

        RunActive(engine, clocks, TimeSpan.FromMinutes(2));
        RunIdle(engine, clocks, TimeSpan.FromMinutes(2));
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        ReturnAndTick(engine, clocks, TimeSpan.FromSeconds(1));
        Assert.Equal(TimerStatus.Running, engine.Status);

        RunActive(engine, clocks, TimeSpan.FromMinutes(3));
        RunIdle(engine, clocks, TimeSpan.FromMinutes(5));
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);

        ReturnAndTick(engine, clocks, TimeSpan.FromSeconds(1));
        Assert.Equal(TimerStatus.Running, engine.Status);

        RunActive(engine, clocks, TimeSpan.FromMinutes(4));
        engine.Stop();

        Assert.Equal(TimerStatus.Stopped, engine.Status);
        Assert.Equal(3, engine.ActiveSession!.Segments.Count);
        Assert.InRange(engine.ActiveSession.Duration.TotalMinutes, 8.98, 9.05);
    }

    [Fact]
    public void Sleep_or_freeze_gap_is_excluded_and_pauses_the_tracker()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();

        clocks.AdvanceActive(TimeSpan.FromSeconds(1));
        engine.Tick(IdleThreshold);
        var durationBeforeSleep = engine.CurrentDuration;

        // The dispatcher timer was frozen for 30 minutes (sleep/hibernation).
        clocks.AdvanceIdle(TimeSpan.FromMinutes(30));
        engine.Tick(IdleThreshold);

        Assert.Equal(TimerStatus.PausedIdle, engine.Status);
        Assert.Equal(IdlePauseCause.SystemGap, engine.LastPauseCause);
        Assert.InRange(
            engine.CurrentDuration.TotalSeconds,
            durationBeforeSleep.TotalSeconds - 0.2,
            durationBeforeSleep.TotalSeconds + 0.2);

        ReturnAndTick(engine, clocks, TimeSpan.FromSeconds(2));
        Assert.Equal(TimerStatus.Running, engine.Status);
        Assert.Equal(2, engine.ActiveSession!.Segments.Count);
    }

    [Fact]
    public void Suspend_and_screen_lock_do_not_count_locked_time()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        RunActive(engine, clocks, TimeSpan.FromMinutes(1));

        engine.HandleSessionLock();
        Assert.Equal(TimerStatus.PausedIdle, engine.Status);
        Assert.Equal(IdlePauseCause.ScreenLock, engine.LastPauseCause);

        clocks.AdvanceIdle(TimeSpan.FromMinutes(10));
        engine.HandleSessionUnlock();
        Assert.Equal(TimerStatus.Running, engine.Status);
        Assert.InRange(engine.CurrentDuration.TotalMinutes, 0.99, 1.01);

        RunActive(engine, clocks, TimeSpan.FromMinutes(2));
        engine.HandleSystemSuspend();
        Assert.Equal(IdlePauseCause.Sleep, engine.LastPauseCause);
        Assert.InRange(engine.CurrentDuration.TotalMinutes, 2.99, 3.01);
    }

    [Fact]
    public void Unlock_does_not_resume_manual_pause()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        clocks.AdvanceActive(TimeSpan.FromSeconds(20));
        engine.PauseManually();

        engine.HandleSessionUnlock();
        Assert.Equal(TimerStatus.PausedManual, engine.Status);
    }

    [Fact]
    public void Crash_recovery_closes_segment_at_heartbeat_and_excludes_downtime()
    {
        var heartbeat = Start.AddSeconds(42);
        var session = new TimeSession
        {
            Id = 7,
            StartUtc = Start,
            EndUtc = null,
            Source = SessionSource.Automatic,
            State = TimerStatus.Running,
            LastHeartbeatUtc = heartbeat,
            CreatedUtc = Start,
            UpdatedUtc = heartbeat
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = Start,
            EndUtc = null,
            DurationMs = 42_000,
            Ordinal = 0,
            CreatedUtc = Start
        });

        var restartAt = Start.AddHours(2);
        var clocks = new TestClockSet(restartAt);
        var engine = new TimerEngine(clocks.Wall, clocks.Monotonic, clocks.Idle);

        var recovery = engine.Restore(session);
        Assert.True(recovery.HadActiveSession);
        Assert.Equal(TimerStatus.Running, engine.Status);
        Assert.Equal(TimeSpan.FromSeconds(42), engine.CurrentDuration);
        Assert.Equal(heartbeat, session.Segments[0].EndUtc);
        Assert.Equal(2, session.Segments.Count);

        RunActive(engine, clocks, TimeSpan.FromSeconds(8));
        Assert.InRange(engine.CurrentDuration.TotalSeconds, 49.9, 50.1);
    }

    [Fact]
    public void Recovered_manual_pause_stays_manual()
    {
        var session = new TimeSession
        {
            Id = 8,
            StartUtc = Start,
            EndUtc = null,
            Source = SessionSource.Automatic,
            State = TimerStatus.PausedManual,
            LastHeartbeatUtc = Start.AddMinutes(1),
            CreatedUtc = Start,
            UpdatedUtc = Start.AddMinutes(1)
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = Start,
            EndUtc = Start.AddMinutes(1),
            DurationMs = 60_000,
            Ordinal = 0,
            CreatedUtc = Start
        });

        var clocks = new TestClockSet(Start.AddHours(1));
        var engine = new TimerEngine(clocks.Wall, clocks.Monotonic, clocks.Idle);
        engine.Restore(session);

        clocks.ReturnFromIdle(TimeSpan.FromMinutes(5));
        engine.Tick(IdleThreshold);
        Assert.Equal(TimerStatus.PausedManual, engine.Status);
    }

    [Fact]
    public void Wall_clock_change_does_not_affect_monotonic_duration()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        RunActive(engine, clocks, TimeSpan.FromMinutes(2));

        // The wall clock is moved back one hour, while the user keeps working.
        clocks.Wall.Advance(TimeSpan.FromHours(-1));
        for (var second = 0; second < 60; second++)
        {
            clocks.Wall.Advance(TimeSpan.FromSeconds(1));
            clocks.Monotonic.Advance(TimeSpan.FromSeconds(1));
            clocks.Idle.Input();
            engine.Tick(IdleThreshold);
        }

        Assert.InRange(engine.CurrentDuration.TotalMinutes, 2.99, 3.01);
        engine.Stop();
        Assert.InRange(engine.ActiveSession!.Duration.TotalMinutes, 2.99, 3.01);
    }

    [Fact]
    public void Snapshot_while_running_keeps_segment_open_and_heartbeat_current()
    {
        var (engine, clocks) = CreateEngine();
        engine.Start();
        clocks.AdvanceActive(TimeSpan.FromSeconds(7));
        var snapshot = engine.CreateSnapshot();

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.EndUtc);
        Assert.Equal(TimerStatus.Running, snapshot.State);
        Assert.Equal(clocks.Wall.UtcNow, snapshot.LastHeartbeatUtc);
        Assert.InRange(snapshot.Segments.Single().DurationMs, 6990, 7010);
    }
}
