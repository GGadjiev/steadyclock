namespace MyTimeTracker.Core.Models;

/// <summary>
/// Identity of the application that drives process tracking. Path is preferred;
/// Name is a fallback for launchers whose executable path changes.
/// </summary>
public sealed class ProcessTriggerSettings
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool StopWhenClosed { get; set; } = true;

    public bool HasTarget =>
        !string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(Name);

    public ProcessTriggerSettings Clone() => new()
    {
        Name = Name,
        Path = Path,
        DisplayName = DisplayName,
        StopWhenClosed = StopWhenClosed
    };
}
