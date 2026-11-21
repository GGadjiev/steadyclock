using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

public sealed record HotkeyApplyResult(bool Success, string? ConflictDisplayName = null);

/// <summary>
/// Registers process-wide hotkeys through the Windows RegisterHotKey API.
/// A no-op implementation is used when registration is unavailable.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    bool IsSupported { get; }

    event EventHandler? ToggleRequested;
    event EventHandler? StopRequested;
    event EventHandler? ShowHideRequested;

    HotkeyApplyResult Apply(AppSettings settings);
}

public static class GlobalHotkeyServiceFactory
{
    public static IGlobalHotkeyService Create(IAppLogger logger)
    {
        try
        {
            return new WindowsGlobalHotkeyService(logger);
        }
        catch (Exception exception)
        {
            logger.Warn($"Не удалось включить глобальные горячие клавиши: {exception.Message}");
            return new NullGlobalHotkeyService();
        }
    }
}

internal sealed class NullGlobalHotkeyService : IGlobalHotkeyService
{
    public bool IsSupported => false;

    public event EventHandler? ToggleRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? StopRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? ShowHideRequested
    {
        add { }
        remove { }
    }

    public HotkeyApplyResult Apply(AppSettings settings) => new(true);

    public void Dispose()
    {
    }
}
