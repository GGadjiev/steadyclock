using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

public interface ISessionMonitor : IDisposable
{
    event EventHandler? SessionLocked;
    event EventHandler? SessionUnlocked;
    event EventHandler? SystemSuspending;
    event EventHandler? SystemResumed;
    event EventHandler? SystemTimeChanged;
}

public static class SessionMonitorFactory
{
    public static ISessionMonitor Create(IAppLogger logger) =>
        OperatingSystem.IsWindows()
            ? new WindowsSessionMonitor(logger)
            : new NullSessionMonitor();
}

internal sealed class NullSessionMonitor : ISessionMonitor
{
    public event EventHandler? SessionLocked
    {
        add { }
        remove { }
    }

    public event EventHandler? SessionUnlocked
    {
        add { }
        remove { }
    }

    public event EventHandler? SystemSuspending
    {
        add { }
        remove { }
    }

    public event EventHandler? SystemResumed
    {
        add { }
        remove { }
    }

    public event EventHandler? SystemTimeChanged
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}
