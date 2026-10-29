using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Common;

/// <summary>
/// Decides when to remind that the timer is stopped while the user is actively
/// working inside configured work hours. The policy only produces decisions; the
/// UI is responsible for showing a non-modal toast.
/// </summary>
public sealed class ForgotTimerReminderPolicy
{
    private static readonly TimeSpan ActiveInputWindow = TimeSpan.FromSeconds(60);

    private readonly IWallClock _wallClock;
    private DateTimeOffset? _eligibleSinceUtc;
    private DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

    public ForgotTimerReminderPolicy(IWallClock wallClock)
    {
        _wallClock = wallClock;
    }

    public bool ShouldShow(AppSettings settings, TimerStatus status, TimeSpan idleTime)
    {
        var now = _wallClock.UtcNow;
        if (!IsEligible(settings, status, idleTime, now))
        {
            _eligibleSinceUtc = null;
            return false;
        }

        _eligibleSinceUtc ??= now;
        if (now - _eligibleSinceUtc.Value < TimeSpan.FromMinutes(settings.ForgotTimerAfterMinutes))
        {
            return false;
        }

        if (now < _nextAllowedUtc)
        {
            return false;
        }

        _nextAllowedUtc = now.AddMinutes(settings.ReminderRepeatMinutes);
        return true;
    }

    public void Reset()
    {
        _eligibleSinceUtc = null;
        _nextAllowedUtc = DateTimeOffset.MinValue;
    }

    private bool IsEligible(AppSettings settings, TimerStatus status, TimeSpan idleTime, DateTimeOffset nowUtc)
    {
        if (!settings.RemindersEnabled ||
            !settings.ForgotTimerReminderEnabled ||
            status != TimerStatus.Stopped ||
            idleTime > ActiveInputWindow)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, _wallClock.LocalTimeZone);
        var mondayFirstIndex = ((int)local.DayOfWeek + 6) % 7;
        if (!settings.WorkDays[mondayFirstIndex])
        {
            return false;
        }

        if (!SettingsValidator.TryParseTime(settings.WorkStart, out var workStart) ||
            !SettingsValidator.TryParseTime(settings.WorkEnd, out var workEnd))
        {
            return false;
        }

        var localTime = TimeOnly.FromDateTime(local.DateTime);
        return localTime >= workStart && localTime < workEnd;
    }
}
