using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.ViewModels;

/// <summary>
/// One row in the process picker. A saved target may be shown even when the
/// application is not currently running so the user can keep the selection.
/// </summary>
public sealed record ProcessOptionViewModel(
    string Name,
    string Path,
    string DisplayName,
    bool IsRunning)
{
    public string Label => IsRunning
        ? DisplayName
        : $"{DisplayName} — {Strings.Get("ProcessNotRunningSuffix")}";
}
