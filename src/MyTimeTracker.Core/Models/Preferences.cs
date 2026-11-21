namespace MyTimeTracker.Core.Models;

/// <summary>Application colour theme.</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>How idle time is removed from the active segment.</summary>
public enum IdleDeductionMode
{
    /// <summary>Rewind the segment to the last real input event.</summary>
    FromLastActivity = 0,

    /// <summary>Keep the first minutes and stop at the moment idle was detected.</summary>
    FromDetection = 1
}
