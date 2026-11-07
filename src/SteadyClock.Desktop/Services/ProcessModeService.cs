using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Automatically starts/stops a process-triggered session. An explicit Stop while
/// the target application is still running suppresses restarts until the next
/// application launch.
/// </summary>
public sealed class ProcessModeService : IDisposable
{
    private readonly TimeTrackingController _controller;
    private readonly IProcessCatalog _processCatalog;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _wasRunning;
    private bool _suppressUntilClosed;
    private bool _stoppingFromProcess;
    private bool _targetInitialized;
    private string _targetKey = string.Empty;
    private bool _disposed;

    public ProcessModeService(
        TimeTrackingController controller,
        IProcessCatalog processCatalog,
        IAppLogger logger)
    {
        _controller = controller;
        _processCatalog = processCatalog;
        _logger = logger;
        _controller.TimerStateChanged += OnTimerStateChanged;
    }

    public event EventHandler<string>? ProcessSessionStarted;
    public event EventHandler<string>? ProcessSessionStopped;

    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var trigger = _controller.CurrentSettings.ProcessTrigger;
            if (_controller.CurrentSettings.TrackerMode != TrackingMode.Process ||
                trigger is null ||
                !trigger.HasTarget)
            {
                _wasRunning = false;
                return;
            }

            var key = $"{trigger.Path}|{trigger.Name}";
            if (!string.Equals(key, _targetKey, StringComparison.Ordinal))
            {
                if (_targetInitialized &&
                    _controller.HasActiveSession &&
                    _controller.Engine.ActiveSession?.Source == SessionSource.ProcessAutomatic)
                {
                    _stoppingFromProcess = true;
                    try
                    {
                        await _controller.StopAsync(cancellationToken).ConfigureAwait(true);
                        _logger.Info(
                            "Режим «По приложению»: цель изменена, предыдущая сессия остановлена.");
                    }
                    finally
                    {
                        _stoppingFromProcess = false;
                    }
                }

                _targetKey = key;
                _targetInitialized = true;
                _wasRunning = false;
                _suppressUntilClosed = false;
            }

            var running = await _processCatalog.IsRunningAsync(trigger, cancellationToken)
                .ConfigureAwait(true);

            if (!running)
            {
                _suppressUntilClosed = false;
                if (_wasRunning &&
                    trigger.StopWhenClosed &&
                    _controller.HasActiveSession &&
                    _controller.Engine.ActiveSession?.Source == SessionSource.ProcessAutomatic)
                {
                    _stoppingFromProcess = true;
                    try
                    {
                        await _controller.StopAsync(cancellationToken).ConfigureAwait(true);
                        var displayName = string.IsNullOrWhiteSpace(trigger.DisplayName)
                            ? trigger.Name
                            : trigger.DisplayName;
                        _logger.Info($"Режим «По приложению»: сессия для {displayName} остановлена.");
                        ProcessSessionStopped?.Invoke(this, displayName);
                    }
                    finally
                    {
                        _stoppingFromProcess = false;
                    }
                }

                _wasRunning = false;
                return;
            }

            if (!_wasRunning)
            {
                // The process was already running when the app started, or it has
                // just appeared. Start only if the user has no active session and
                // hasn't explicitly stopped tracking while this process is open.
                if (_controller.Status == TimerStatus.Stopped && !_suppressUntilClosed)
                {
                    await _controller.StartAsync(SessionSource.ProcessAutomatic, cancellationToken)
                        .ConfigureAwait(true);
                    if (_controller.Engine.ActiveSession?.Source == SessionSource.ProcessAutomatic)
                    {
                        var displayName = string.IsNullOrWhiteSpace(trigger.DisplayName)
                            ? trigger.Name
                            : trigger.DisplayName;
                        _logger.Info($"Режим «По приложению»: сессия для {displayName} запущена.");
                        ProcessSessionStarted?.Invoke(this, displayName);
                    }
                }

                _wasRunning = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception exception)
        {
            _logger.Error("Ошибка режима «По приложению».", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e)
    {
        if (e.Status != TimerStatus.Stopped ||
            _stoppingFromProcess ||
            _controller.CurrentSettings.TrackerMode != TrackingMode.Process)
        {
            return;
        }

        if (_wasRunning)
        {
            _suppressUntilClosed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.TimerStateChanged -= OnTimerStateChanged;
        _gate.Dispose();
    }
}
