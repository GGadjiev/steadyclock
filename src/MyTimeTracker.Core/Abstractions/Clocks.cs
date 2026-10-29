using System.Diagnostics;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Abstractions;

public interface IWallClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo LocalTimeZone { get; }
}

public interface IMonotonicClock
{
    /// <summary>Monotonic elapsed time, unaffected by wall-clock changes.</summary>
    TimeSpan Elapsed { get; }
}

public interface IIdleTimeProvider
{
    /// <summary>Time elapsed since the last mouse or keyboard input in the Windows session.</summary>
    TimeSpan GetIdleTime();
}

public sealed class SystemWallClock : IWallClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}

public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_origin);
}

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

public sealed class TimerStatusChangedEventArgs : EventArgs
{
    public required TimerStatus PreviousStatus { get; init; }
    public required TimerStatus Status { get; init; }
    public IdlePauseCause PauseCause { get; init; }
    public DateTimeOffset AtUtc { get; init; }
}

public sealed class AutoPausedEventArgs : EventArgs
{
    public required IdlePauseCause Cause { get; init; }
    public required DateTimeOffset AtUtc { get; init; }
    public required TimeSpan IdleTime { get; init; }
}

public sealed class RecoveredEventArgs : EventArgs
{
    public required bool HadActiveSession { get; init; }
    public required TimerStatus Status { get; init; }
    public required TimeSpan ExcludedGap { get; init; }
}
