using System.Globalization;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Timer;

namespace MyTimeTracker.App.Services;

public sealed record SaveSessionResult(
    bool Success,
    ManualValidationError ValidationError = ManualValidationError.None,
    bool TechnicalFailure = false);

public sealed record OperationResult(bool Success, bool TechnicalFailure = false);

/// <summary>
/// Application-facing coordinator: owns the timer state machine, persistence,
/// heartbeat cadence, auto pause/resume notifications and reminder evaluation.
/// UI code never calls the database directly.
/// </summary>
public sealed class TimeTrackingController : IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReminderEvaluationInterval = TimeSpan.FromSeconds(1);

    private readonly TimerEngine _engine;
    private readonly ITimeSessionRepository _repository;
    private readonly IWallClock _wallClock;
    private readonly IMonotonicClock _monotonicClock;
    private readonly IIdleTimeProvider _idleTimeProvider;
    private readonly IAppLogger _logger;
    private readonly ForgotTimerReminderPolicy _reminderPolicy;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private TimeSpan _lastHeartbeatMonotonic;
    private TimeSpan _lastReminderEvaluationMonotonic = TimeSpan.MinValue;
    private bool _disposed;

    public TimeTrackingController(
        TimerEngine engine,
        ITimeSessionRepository repository,
        IWallClock wallClock,
        IMonotonicClock monotonicClock,
        IIdleTimeProvider idleTimeProvider,
        IAppLogger logger)
    {
        _engine = engine;
        _repository = repository;
        _wallClock = wallClock;
        _monotonicClock = monotonicClock;
        _idleTimeProvider = idleTimeProvider;
        _logger = logger;
        _reminderPolicy = new ForgotTimerReminderPolicy(wallClock);

        _engine.StatusChanged += EngineOnStatusChanged;
        _engine.AutoPaused += EngineOnAutoPaused;
        _engine.AutoResumed += EngineOnAutoResumed;
    }

    public AppSettings CurrentSettings { get; private set; } = AppSettings.CreateDefault();
    public TimerEngine Engine => _engine;
    public TimerStatus Status => _engine.Status;
    public bool HasActiveSession => _engine.HasActiveSession;
    public TimeSpan CurrentDuration => _engine.CurrentDuration;
    public TimeZoneInfo LocalTimeZone => _wallClock.LocalTimeZone;

    public event EventHandler<TimerStatusChangedEventArgs>? TimerStateChanged;
    public event EventHandler<AutoPausedEventArgs>? AutoPaused;
    public event EventHandler? AutoResumed;
    public event EventHandler? SessionsChanged;
    public event EventHandler? ReminderDue;
    public event EventHandler? PersistenceFailed;
    public event EventHandler? TickCompleted;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await _repository.InitializeAsync(cancellationToken).ConfigureAwait(true);
            var active = await _repository.GetActiveSessionAsync(cancellationToken).ConfigureAwait(true);
            var recovery = _engine.Restore(active);
            _logger.Info(
                $"Инициализация таймера. Восстановлена сессия: {recovery.HadActiveSession}. Состояние: {_engine.Status}.");

            if (active is not null)
            {
                await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
            }

            _lastHeartbeatMonotonic = _monotonicClock.Elapsed;
            _reminderPolicy.Reset();
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        CurrentSettings = settings.DeepClone();
        _reminderPolicy.Reset();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            if (_engine.Status != TimerStatus.Stopped)
            {
                return;
            }

            _engine.Start();
            await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task StartIfStoppedAsync(CancellationToken cancellationToken = default)
    {
        if (_engine.Status == TimerStatus.Stopped)
        {
            await StartAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            switch (_engine.Status)
            {
                case TimerStatus.Stopped:
                    _engine.Start();
                    break;
                case TimerStatus.Running:
                    _engine.PauseManually();
                    break;
                case TimerStatus.PausedManual:
                case TimerStatus.PausedIdle:
                    _engine.ResumeManually();
                    break;
            }

            await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            if (_engine.Status != TimerStatus.Running)
            {
                return;
            }

            _engine.PauseManually();
            await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            if (_engine.Status is not (TimerStatus.PausedManual or TimerStatus.PausedIdle))
            {
                return;
            }

            _engine.ResumeManually();
            await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            if (!_engine.HasActiveSession)
            {
                return;
            }

            _engine.Stop();
            await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var previousStatus = _engine.Status;
            _engine.Tick(TimeSpan.FromSeconds(CurrentSettings.IdleThresholdSeconds));
            var statusChanged = _engine.Status != previousStatus;

            if (statusChanged)
            {
                await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
            }
            else if (_engine.Status == TimerStatus.Running &&
                     _monotonicClock.Elapsed - _lastHeartbeatMonotonic >= HeartbeatInterval)
            {
                await PersistActiveLockedAsync(notify: false, cancellationToken).ConfigureAwait(true);
            }

            EvaluateReminder();
            TickCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void SetComment(string? comment)
    {
        _engine.SetComment(comment);
    }

    public async Task PersistCommentAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            await PersistActiveLockedAsync(notify: false, cancellationToken).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task HandleSystemSuspendAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            var hadActive = _engine.Status != TimerStatus.PausedIdle;
            _engine.HandleSystemSuspend();
            if (hadActive && _engine.HasActiveSession)
            {
                await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task HandleSessionLockAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            var previous = _engine.Status;
            _engine.HandleSessionLock();
            if (_engine.Status != previous)
            {
                await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task HandleSessionUnlockAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync(async () =>
        {
            var previous = _engine.Status;
            _engine.HandleSessionUnlock();
            if (_engine.Status != previous)
            {
                await PersistActiveLockedAsync(notify: true, cancellationToken).ConfigureAwait(true);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    public Task OnSystemResumedAsync(CancellationToken cancellationToken = default) =>
        TickAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeSession>> GetSessionsForLocalDayAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = TimeText.GetLocalDayRangeUtc(localDate, LocalTimeZone);
        return await _repository.GetSessionsOverlappingAsync(start, end, cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<TimeSession>> GetSessionsForLocalRangeAsync(
        DateOnly firstDate,
        DateOnly lastDate,
        CancellationToken cancellationToken = default)
    {
        var (start, _) = TimeText.GetLocalDayRangeUtc(firstDate, LocalTimeZone);
        var (_, end) = TimeText.GetLocalDayRangeUtc(lastDate.AddDays(1), LocalTimeZone);
        return await _repository.GetSessionsOverlappingAsync(start, end, cancellationToken)
            .ConfigureAwait(true);
    }

    public Task<TimeSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default) =>
        _repository.GetSessionAsync(id, cancellationToken);

    public async Task<SaveSessionResult> SaveManualSessionAsync(
        long? editingSessionId,
        ManualSessionInput input,
        bool timesEdited,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var now = _wallClock.UtcNow;

            TimeSession? existing = null;
            if (editingSessionId.HasValue)
            {
                existing = await _repository.GetSessionAsync(editingSessionId.Value, cancellationToken)
                    .ConfigureAwait(true);
                if (existing is null)
                {
                    return new SaveSessionResult(false, TechnicalFailure: true);
                }

                if (existing.IsActive)
                {
                    return new SaveSessionResult(false, ManualValidationError.ActiveSessionCannotBeEdited);
                }
            }

            if (existing is not null && !timesEdited)
            {
                // Comment-only edit keeps the original automatic segments (including
                // idle gaps) and therefore the original tracked duration.
                existing.Comment = input.Comment?.Trim() ?? string.Empty;
                existing.UpdatedUtc = now;
                await _repository.SaveSessionAsync(existing, cancellationToken).ConfigureAwait(true);
                SessionsChanged?.Invoke(this, EventArgs.Empty);
                return new SaveSessionResult(true);
            }

            if (input.EndUtc <= input.StartUtc)
            {
                return new SaveSessionResult(false, ManualValidationError.EndNotAfterStart);
            }

            if (input.EndUtc > now)
            {
                return new SaveSessionResult(
                    false,
                    input.StartUtc > now
                        ? ManualValidationError.StartInFuture
                        : ManualValidationError.EndInFuture);
            }

            var overlapping = await _repository.GetSessionsOverlappingAsync(
                input.StartUtc,
                input.EndUtc,
                cancellationToken).ConfigureAwait(true);

            var validation = ManualSessionEditor.Validate(
                input,
                now,
                overlapping,
                editingSessionId,
                editingActiveSession: false);
            if (!validation.IsValid)
            {
                return new SaveSessionResult(false, validation.Error);
            }

            TimeSession session;
            if (existing is not null)
            {
                ManualSessionEditor.ApplyEdit(existing, input, now, timesChanged: true);
                session = existing;
            }
            else
            {
                session = ManualSessionEditor.Create(input, now);
            }

            await _repository.SaveSessionAsync(session, cancellationToken).ConfigureAwait(true);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return new SaveSessionResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось сохранить ручную запись.", exception);
            return new SaveSessionResult(false, TechnicalFailure: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> DeleteSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_engine.HasActiveSession && _engine.ActiveSession?.Id == sessionId)
            {
                return new OperationResult(false);
            }

            await _repository.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(true);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return new OperationResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось удалить запись.", exception);
            return new OperationResult(false, TechnicalFailure: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ExecuteLockedAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task PersistActiveLockedAsync(bool notify, CancellationToken cancellationToken)
    {
        var snapshot = _engine.CreateSnapshot();
        if (snapshot is null)
        {
            return;
        }

        try
        {
            var id = await _repository.SaveSessionAsync(snapshot, cancellationToken).ConfigureAwait(true);
            _engine.AssignActiveSessionId(id);
            if (_engine.Status == TimerStatus.Running)
            {
                _lastHeartbeatMonotonic = _monotonicClock.Elapsed;
            }

            if (notify)
            {
                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось сохранить активную сессию.", exception);
            PersistenceFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EvaluateReminder()
    {
        var nowMono = _monotonicClock.Elapsed;
        if (_lastReminderEvaluationMonotonic != TimeSpan.MinValue &&
            nowMono - _lastReminderEvaluationMonotonic < ReminderEvaluationInterval)
        {
            return;
        }

        _lastReminderEvaluationMonotonic = nowMono;
        var idle = GetIdleTimeSafe();
        if (_reminderPolicy.ShouldShow(CurrentSettings, _engine.Status, idle))
        {
            ReminderDue?.Invoke(this, EventArgs.Empty);
        }
    }

    private TimeSpan GetIdleTimeSafe()
    {
        try
        {
            var idle = _idleTimeProvider.GetIdleTime();
            return idle < TimeSpan.Zero ? TimeSpan.Zero : idle;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private void EngineOnStatusChanged(object? sender, TimerStatusChangedEventArgs e) =>
        TimerStateChanged?.Invoke(this, e);

    private void EngineOnAutoPaused(object? sender, AutoPausedEventArgs e) =>
        AutoPaused?.Invoke(this, e);

    private void EngineOnAutoResumed(object? sender, EventArgs e) =>
        AutoResumed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.StatusChanged -= EngineOnStatusChanged;
        _engine.AutoPaused -= EngineOnAutoPaused;
        _engine.AutoResumed -= EngineOnAutoResumed;
        _operationGate.Dispose();
    }
}
