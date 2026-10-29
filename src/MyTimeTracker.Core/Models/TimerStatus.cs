namespace MyTimeTracker.Core.Models;

/// <summary>Persisted and runtime state of the single tracker.</summary>
public enum TimerStatus
{
    Stopped = 0,
    Running = 1,
    PausedManual = 2,
    PausedIdle = 3
}

/// <summary>Why an automatic pause was started.</summary>
public enum IdlePauseCause
{
    None = 0,
    Idle = 1,
    ScreenLock = 2,
    Sleep = 3,
    SystemGap = 4,
    Recovered = 5
}

public enum SessionSource
{
    Automatic = 0,
    Manual = 1
}
