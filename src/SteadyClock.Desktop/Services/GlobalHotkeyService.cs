using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

public sealed record HotkeyApplyResult(bool Success, string? ConflictDisplayName = null);

/// <summary>
/// Registers process-wide hotkeys. Windows uses RegisterHotKey, Linux X11 uses
/// XGrabKey. Unsupported sessions (for example Wayland without XWayland) get a
/// no-op implementation instead of breaking startup.
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
            if (OperatingSystem.IsWindows())
            {
                return new WindowsGlobalHotkeyService(logger);
            }

            if (OperatingSystem.IsLinux())
            {
                var x11 = new X11GlobalHotkeyService(logger);
                if (x11.IsAvailable)
                {
                    return x11;
                }

                x11.Dispose();
                logger.Warn("Глобальные горячие клавиши недоступны: X11 display не найден.");
                return new NullGlobalHotkeyService();
            }
        }
        catch (Exception exception)
        {
            logger.Warn($"Не удалось включить глобальные горячие клавиши: {exception.Message}");
        }

        return new NullGlobalHotkeyService();
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
