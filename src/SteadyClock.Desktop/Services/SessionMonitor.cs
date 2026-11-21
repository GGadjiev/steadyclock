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
        new WindowsSessionMonitor(logger);
}
