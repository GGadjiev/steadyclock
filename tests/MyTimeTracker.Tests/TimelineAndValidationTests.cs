using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Tests;

public sealed class TimelineAndValidationTests
{
    private static TimeSession CompletedSession(
        DateTimeOffset start,
        DateTimeOffset end,
        long id = 1,
        SessionSource source = SessionSource.Automatic)
    {
        var session = new TimeSession
        {
            Id = id,
            StartUtc = start,
            EndUtc = end,
            Source = source,
            State = TimerStatus.Stopped,
            CreatedUtc = start,
            UpdatedUtc = end
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start,
            EndUtc = end,
            DurationMs = (long)(end - start).TotalMilliseconds,
            Ordinal = 0,
            CreatedUtc = start
        });
        return session;
    }

    [Fact]
    public void Timeline_splits_cross_midnight_segment_by_local_day()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var startUtc = new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero); // 23:00 local
        var endUtc = startUtc.AddHours(3); // 02:00 next local day
        var session = CompletedSession(startUtc, endUtc);

        var dayOne = SessionTimeline.BuildDay(new[] { session }, new DateOnly(2026, 5, 1), timeZone, endUtc);
        var dayTwo = SessionTimeline.BuildDay(new[] { session }, new DateOnly(2026, 5, 2), timeZone, endUtc);

        Assert.Equal(TimeSpan.FromHours(1), dayOne.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(2), dayTwo.TotalDuration);
        Assert.Single(dayOne.Sessions);
        Assert.Single(dayTwo.Sessions);
    }

    [Fact]
    public void Timeline_groups_the_same_utc_session_in_the_current_local_time_zone()
    {
        var startUtc = new DateTimeOffset(2026, 5, 1, 23, 30, 0, TimeSpan.Zero);
        var endUtc = startUtc.AddHours(1);
        var session = CompletedSession(startUtc, endUtc);
        var utcPlus3 = TimeZoneInfo.CreateCustomTimeZone(
            "UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var utcMinus5 = TimeZoneInfo.CreateCustomTimeZone(
            "UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

        var plus3Day = SessionTimeline.BuildDay(
            new[] { session }, new DateOnly(2026, 5, 2), utcPlus3, endUtc);
        var minus5Day = SessionTimeline.BuildDay(
            new[] { session }, new DateOnly(2026, 5, 1), utcMinus5, endUtc);

        Assert.Equal(TimeSpan.FromHours(1), plus3Day.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(1), minus5Day.TotalDuration);
        Assert.Empty(SessionTimeline.BuildDay(
            new[] { session }, new DateOnly(2026, 5, 1), utcPlus3, endUtc).Sessions);
    }

    [Fact]
    public void Timeline_uses_active_segments_and_ignores_idle_gaps()
    {
        var timeZone = TimeZoneInfo.Utc;
        var day = new DateOnly(2026, 3, 10);
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var session = new TimeSession
        {
            Id = 3,
            StartUtc = start,
            EndUtc = start.AddHours(2),
            Source = SessionSource.Automatic,
            State = TimerStatus.Stopped,
            CreatedUtc = start,
            UpdatedUtc = start.AddHours(2)
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start,
            EndUtc = start.AddMinutes(30),
            DurationMs = 30 * 60_000,
            Ordinal = 0
        });
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start.AddMinutes(90),
            EndUtc = start.AddMinutes(120),
            DurationMs = 30 * 60_000,
            Ordinal = 1
        });

        var summary = SessionTimeline.BuildDay(new[] { session }, day, timeZone, start.AddHours(3));

        Assert.Equal(TimeSpan.FromHours(1), summary.TotalDuration);
        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(start.AddMinutes(120), summary.Sessions.Single().EndUtc);
    }

    [Fact]
    public void Manual_record_validation_rejects_future_and_inverted_ranges()
    {
        var now = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var baseInput = new ManualSessionInput(
            new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
            "");

        var inverted = baseInput with { EndUtc = baseInput.StartUtc.AddMinutes(-1) };
        var future = baseInput with { StartUtc = now.AddMinutes(5), EndUtc = now.AddMinutes(20) };

        Assert.Equal(
            ManualValidationError.EndNotAfterStart,
            ManualSessionEditor.Validate(inverted, now, Array.Empty<TimeSession>()).Error);
        Assert.Equal(
            ManualValidationError.StartInFuture,
            ManualSessionEditor.Validate(future, now, Array.Empty<TimeSession>()).Error);
    }

    [Fact]
    public void Manual_record_validation_rejects_overlap()
    {
        var now = new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);
        var existing = CompletedSession(now.AddHours(-4), now.AddHours(-2), id: 5);
        var input = new ManualSessionInput(now.AddHours(-3), now.AddHours(-1), "x");

        var result = ManualSessionEditor.Validate(input, now, new[] { existing });

        Assert.False(result.IsValid);
        Assert.Equal(ManualValidationError.OverlapsExisting, result.Error);
    }

    [Fact]
    public void Manual_record_may_use_a_pause_gap_inside_automatic_session()
    {
        var now = new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);
        var session = new TimeSession
        {
            Id = 6,
            StartUtc = now.AddHours(-4),
            EndUtc = now,
            Source = SessionSource.Automatic,
            State = TimerStatus.Stopped,
            CreatedUtc = now.AddHours(-4),
            UpdatedUtc = now
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = now.AddHours(-4),
            EndUtc = now.AddHours(-3),
            DurationMs = 60 * 60_000,
            Ordinal = 0
        });
        session.Segments.Add(new SessionSegment
        {
            StartUtc = now.AddHours(-1),
            EndUtc = now,
            DurationMs = 60 * 60_000,
            Ordinal = 1
        });

        var input = new ManualSessionInput(now.AddHours(-2), now.AddMinutes(-90), "в паузе");
        var result = ManualSessionEditor.Validate(input, now, new[] { session });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Manual_record_edit_keeps_source_when_only_comment_changes()
    {
        var session = CompletedSession(
            new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
            id: 9);
        var originalStart = session.StartUtc;
        var originalEnd = session.EndUtc;
        var now = session.EndUtc!.Value.AddHours(1);

        ManualSessionEditor.ApplyEdit(
            session,
            new ManualSessionInput(session.StartUtc, session.EndUtc.Value, "новый комментарий"),
            now);

        Assert.Equal(SessionSource.Automatic, session.Source);
        Assert.Equal("новый комментарий", session.Comment);
        Assert.Equal(originalStart, session.StartUtc);
        Assert.Equal(originalEnd, session.EndUtc);
    }

    [Fact]
    public void Comment_only_edit_preserves_automatic_idle_segments()
    {
        var start = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var session = new TimeSession
        {
            Id = 12,
            StartUtc = start,
            EndUtc = start.AddHours(2),
            Source = SessionSource.Automatic,
            State = TimerStatus.Stopped,
            CreatedUtc = start,
            UpdatedUtc = start.AddHours(2)
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start,
            EndUtc = start.AddHours(1),
            DurationMs = 60 * 60_000,
            Ordinal = 0
        });
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start.AddMinutes(90),
            EndUtc = start.AddMinutes(120),
            DurationMs = 30 * 60_000,
            Ordinal = 1
        });

        ManualSessionEditor.ApplyEdit(
            session,
            new ManualSessionInput(session.StartUtc, session.EndUtc!.Value, "только комментарий"),
            start.AddHours(3),
            timesChanged: false);

        Assert.Equal(SessionSource.Automatic, session.Source);
        Assert.Equal("только комментарий", session.Comment);
        Assert.Equal(2, session.Segments.Count);
        Assert.Equal(TimeSpan.FromMinutes(90), session.Duration);
    }

    [Fact]
    public void Editing_automatic_times_converts_record_to_manual_and_recalculates_total()
    {
        var start = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var session = CompletedSession(start, start.AddHours(2), id: 11);
        var now = start.AddHours(5);
        var newStart = start.AddMinutes(15);
        var newEnd = start.AddMinutes(75);

        ManualSessionEditor.ApplyEdit(
            session,
            new ManualSessionInput(newStart, newEnd, "исправлено"),
            now);

        Assert.Equal(SessionSource.Manual, session.Source);
        Assert.Equal(TimeSpan.FromMinutes(60), session.Duration);
        Assert.Single(session.Segments);
        Assert.Equal(newStart, session.StartUtc);
        Assert.Equal(newEnd, session.EndUtc);

        var day = SessionTimeline.BuildDay(
            new[] { session },
            DateOnly.FromDateTime(start.UtcDateTime),
            TimeZoneInfo.Utc,
            now);
        Assert.Equal(TimeSpan.FromMinutes(60), day.TotalDuration);
    }

    [Fact]
    public void Settings_validation_rejects_duplicate_hotkeys_and_bad_idle_range()
    {
        var settings = AppSettings.CreateDefault();
        settings.IdleThresholdSeconds = 5;
        settings.ShowHideHotkey = settings.ToggleTimerHotkey.Clone();

        var errors = SettingsValidator.Validate(settings);

        Assert.Contains(errors, error => error.Contains("простоя", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("нескольким", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reminder_policy_obeys_work_hours_and_repeat_interval()
    {
        // Monday 5 January 2026, 10:00 UTC (time zone is UTC in tests).
        var clocks = new TestClockSet(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero));
        var policy = new ForgotTimerReminderPolicy(clocks.Wall);
        var settings = AppSettings.CreateDefault();
        settings.WorkDays = new[] { true, true, true, true, true, true, true };
        settings.ForgotTimerAfterMinutes = 15;
        settings.ReminderRepeatMinutes = 30;

        Assert.False(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(10)));
        clocks.AdvanceActive(TimeSpan.FromMinutes(14));
        Assert.False(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(10)));

        clocks.AdvanceActive(TimeSpan.FromMinutes(1));
        Assert.True(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(10)));

        clocks.AdvanceActive(TimeSpan.FromMinutes(29));
        Assert.False(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(10)));
        clocks.AdvanceActive(TimeSpan.FromMinutes(1));
        Assert.True(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Reminder_policy_does_not_fire_on_manual_pause_or_outside_work_hours()
    {
        var clocks = new TestClockSet(new DateTimeOffset(2026, 1, 5, 18, 30, 0, TimeSpan.Zero));
        var policy = new ForgotTimerReminderPolicy(clocks.Wall);
        var settings = AppSettings.CreateDefault();
        settings.WorkDays = new[] { true, true, true, true, true, true, true };

        clocks.AdvanceActive(TimeSpan.FromHours(1));
        Assert.False(policy.ShouldShow(settings, TimerStatus.Stopped, TimeSpan.FromSeconds(5)));

        clocks.Wall.Set(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero));
        for (var index = 0; index < 4; index++)
        {
            Assert.False(policy.ShouldShow(settings, TimerStatus.PausedManual, TimeSpan.Zero));
            clocks.AdvanceActive(TimeSpan.FromMinutes(10));
        }

        Assert.False(policy.ShouldShow(settings, TimerStatus.PausedIdle, TimeSpan.Zero));
    }
}
