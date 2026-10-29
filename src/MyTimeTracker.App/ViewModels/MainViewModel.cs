using System.Collections.ObjectModel;
using System.Windows.Input;
using MyTimeTracker.App.Infrastructure;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.Services;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int HistoryChunkDays = 30;
    private readonly TimeTrackingController _controller;
    private readonly IAppDialogService _dialogs;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TimeZoneInfo _timeZone;
    private TimeSpan _lastZoneOffset;

    private IReadOnlyList<TimeSession> _cachedTodaySessions = Array.Empty<TimeSession>();
    private DateOnly _today;
    private DateOnly? _historyStartDate;
    private TimerStatus _status;
    private string _timerText = "00:00:00";
    private string _statusText = Strings.Get("StatusStopped");
    private string _primaryActionText = Strings.Get("Start");
    private string _todayTotalText = "00:00:00";
    private string _todaySessionCountText = Strings.FormatSessionCount(0);
    private string _todayDateText = string.Empty;
    private string _commentText = string.Empty;
    private bool _isTodayEmpty = true;
    private bool _isHistoryEmpty = true;
    private bool _isHistoryPage;
    private bool _isHistoryLoading;
    private bool _isInitialized;
    private bool _suppressComment;
    private long _lastRenderedSecond = -1;
    private bool _dayRefreshScheduled;
    private bool _timeZoneRefreshScheduled;
    private CancellationTokenSource? _commentSaveCts;
    private bool _disposed;

    public MainViewModel(
        TimeTrackingController controller,
        IAppDialogService dialogs,
        IAppLogger logger)
    {
        _controller = controller;
        _dialogs = dialogs;
        _logger = logger;
        _timeZone = controller.LocalTimeZone;
        _lastZoneOffset = _timeZone.GetUtcOffset(controller.Engine.UtcNow);
        _today = TimeText.GetLocalDate(controller.Engine.UtcNow, _timeZone);
        _todayDateText = _today.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy");

        _status = controller.Status;
        UpdateStatusProperties();

        PrimaryActionCommand = new AsyncRelayCommand(
            PrimaryActionAsync,
            onError: HandleCommandException);
        StopCommand = new AsyncRelayCommand(
            StopAsync,
            () => _controller.HasActiveSession,
            HandleCommandException);
        // Navigation buttons are visible only on their respective pages. Their
        // commands intentionally have no CanExecute predicate, so the back
        // button remains enabled after switching to History.
        ShowHistoryCommand = new AsyncRelayCommand(ShowHistoryAsync);
        ShowTimerCommand = new AsyncRelayCommand(ShowTimerAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        AddManualCommand = new AsyncRelayCommand(AddManualAsync);
        LoadEarlierCommand = new AsyncRelayCommand(
            () => LoadHistoryAsync(extend: true),
            () => !IsHistoryLoading);

        _controller.TickCompleted += OnTickCompleted;
        _controller.TimerStateChanged += OnTimerStateChanged;
        _controller.SessionsChanged += OnSessionsChanged;
    }

    public ObservableCollection<SessionRowViewModel> TodaySessions { get; } = new();
    public ObservableCollection<HistoryDayViewModel> HistoryGroups { get; } = new();

    public TimerStatus Status => _status;
    public string StatusText => _statusText;
    public string TimerText => _timerText;
    public string PrimaryActionText => _primaryActionText;
    public string TodayTotalText => _todayTotalText;
    public string TodaySessionCountText => _todaySessionCountText;
    public string TodayDateText => _todayDateText;
    public bool IsTodayEmpty => _isTodayEmpty;
    public bool IsHistoryEmpty => _isHistoryEmpty;
    public bool IsHistoryEmptyVisible => !IsHistoryLoading && IsHistoryEmpty;
    public bool IsHistoryLoading => _isHistoryLoading;
    public bool IsHistoryPage => _isHistoryPage;
    public bool IsTimerPage => !_isHistoryPage;
    public string HeaderTitle => IsHistoryPage ? Strings.Get("HistoryTitle") : Strings.Get("AppTitle");
    public bool HasActiveSession => _controller.HasActiveSession;
    public bool IsRunning => _status == TimerStatus.Running;
    public bool IsPaused => _status is TimerStatus.PausedManual or TimerStatus.PausedIdle;
    public bool IsStopped => _status == TimerStatus.Stopped;
    public bool CanStop => _controller.HasActiveSession;
    public bool IsCommentEnabled => _controller.HasActiveSession;

    public string CommentText
    {
        get => _commentText;
        set
        {
            if (!SetProperty(ref _commentText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCommentEmpty));
            if (_suppressComment || !_controller.HasActiveSession)
            {
                return;
            }

            _controller.SetComment(value);
            ScheduleCommentSave();
        }
    }

    public bool IsCommentEmpty => string.IsNullOrEmpty(CommentText);

    public ICommand PrimaryActionCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ShowHistoryCommand { get; }
    public ICommand ShowTimerCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand AddManualCommand { get; }
    public ICommand LoadEarlierCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _status = _controller.Status;
        UpdateStatusProperties();
        SyncCommentFromEngine();
        UpdateTimerText();
        await RefreshTodayAsync();
        UpdateLiveView(force: true);
    }

    private async Task PrimaryActionAsync()
    {
        switch (_controller.Status)
        {
            case TimerStatus.Stopped:
                await _controller.StartAsync();
                break;
            case TimerStatus.Running:
                await _controller.PauseAsync();
                break;
            default:
                await _controller.ResumeAsync();
                break;
        }
    }

    private async Task StopAsync()
    {
        await _controller.StopAsync();
        CancelPendingCommentSave();
    }

    private async Task ShowHistoryAsync()
    {
        if (_isHistoryPage)
        {
            return;
        }

        _isHistoryPage = true;
        OnPropertiesChanged(
            nameof(IsHistoryPage),
            nameof(IsTimerPage),
            nameof(HeaderTitle));
        await LoadHistoryAsync(extend: false);
    }

    private async Task ShowTimerAsync()
    {
        if (!_isHistoryPage)
        {
            return;
        }

        _isHistoryPage = false;
        OnPropertiesChanged(
            nameof(IsHistoryPage),
            nameof(IsTimerPage),
            nameof(HeaderTitle));
        await RefreshTodayAsync();
    }

    private async Task OpenSettingsAsync()
    {
        await _dialogs.ShowSettingsAsync();
    }

    private async Task AddManualAsync()
    {
        await _dialogs.ShowSessionEditorAsync(null);
    }

    private Task EditSessionAsync(long sessionId) =>
        _dialogs.ShowSessionEditorAsync(sessionId);

    private async Task DeleteSessionAsync(SessionRowViewModel row)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Strings.Get("ConfirmDeleteTitle"),
            Strings.Get("ConfirmDeleteMessage"),
            Strings.Get("DeleteButton"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        var result = await _controller.DeleteSessionAsync(row.SessionId);
        if (!result.Success)
        {
            _dialogs.ShowError(Strings.Get("UnknownError"));
        }
    }

    private async Task RefreshTodayAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var now = _controller.Engine.UtcNow;
            _lastZoneOffset = _timeZone.GetUtcOffset(now);
            _today = TimeText.GetLocalDate(now, _timeZone);
            _todayDateText = _today.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy");
            OnPropertyChanged(nameof(TodayDateText));

            var stored = await _controller.GetSessionsForLocalDayAsync(_today);
            _cachedTodaySessions = stored;
            var summary = BuildTodaySummary();
            ApplyTodaySummary(summary, rebuildRows: true);
            UpdateTimerText();
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось загрузить сессии за сегодня.", exception);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadHistoryAsync(bool extend)
    {
        if (IsHistoryLoading)
        {
            return;
        }

        var today = TimeText.GetLocalDate(_controller.Engine.UtcNow, _timeZone);
        if (_historyStartDate is null)
        {
            _historyStartDate = today.AddDays(-(HistoryChunkDays - 1));
        }
        else if (extend)
        {
            _historyStartDate = _historyStartDate.Value.AddDays(-HistoryChunkDays);
        }

        SetHistoryLoading(true);
        await _refreshGate.WaitAsync();
        try
        {
            var startDate = _historyStartDate!.Value;
            var stored = await _controller.GetSessionsForLocalRangeAsync(startDate, today);
            var merged = MergeActiveSession(stored);
            var summaries = SessionTimeline.BuildRange(
                merged,
                startDate,
                today,
                _timeZone,
                _controller.Engine.UtcNow);

            HistoryGroups.Clear();
            foreach (var summary in summaries.Reverse())
            {
                var rows = BuildRows(summary.Sessions);
                HistoryGroups.Add(new HistoryDayViewModel(
                    summary.Date,
                    summary.TotalDuration,
                    rows));
            }

            _isHistoryEmpty = HistoryGroups.Count == 0;
            OnPropertyChanged(nameof(IsHistoryEmpty));
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось загрузить историю.", exception);
            _dialogs.ShowError(Strings.Get("UnknownError"));
        }
        finally
        {
            _refreshGate.Release();
            SetHistoryLoading(false);
        }
    }

    private void ApplyTodaySummary(LocalDaySummary summary, bool rebuildRows)
    {
        _todayTotalText = TimeText.FormatClock(summary.TotalDuration);
        _todaySessionCountText = Strings.FormatSessionCount(summary.SessionCount);
        _isTodayEmpty = summary.Sessions.Count == 0;
        OnPropertiesChanged(
            nameof(TodayTotalText),
            nameof(TodaySessionCountText),
            nameof(IsTodayEmpty));

        if (rebuildRows)
        {
            TodaySessions.Clear();
            foreach (var row in BuildRows(summary.Sessions))
            {
                TodaySessions.Add(row);
            }
        }
        else
        {
            var activeSlice = summary.Sessions.FirstOrDefault(slice => slice.IsActive);
            var activeRow = TodaySessions.FirstOrDefault(row => row.IsActive);
            if (activeSlice is not null && activeRow is not null)
            {
                activeRow.UpdateLive(activeSlice.Duration, activeSlice.IsRunning, activeSlice.Comment);
            }
        }
    }

    private List<SessionRowViewModel> BuildRows(IReadOnlyList<SessionDaySlice> slices)
    {
        var result = new List<SessionRowViewModel>(slices.Count);
        foreach (var slice in slices)
        {
            var row = new SessionRowViewModel(
                slice.SessionId,
                slice.StartUtc,
                slice.EndUtc,
                slice.Duration,
                slice.Comment,
                slice.Source,
                slice.IsActive,
                slice.IsRunning,
                _timeZone,
                sessionRow => EditSessionAsync(sessionRow.SessionId),
                DeleteSessionAsync);
            result.Add(row);
        }

        return result;
    }

    private LocalDaySummary BuildTodaySummary()
    {
        var merged = MergeActiveSession(_cachedTodaySessions);
        return SessionTimeline.BuildDay(
            merged,
            _today,
            _timeZone,
            _controller.Engine.UtcNow);
    }

    private IReadOnlyList<TimeSession> MergeActiveSession(IEnumerable<TimeSession> storedSessions)
    {
        var list = storedSessions.ToList();
        var active = _controller.Engine.ActiveSession;
        if (_controller.Status == TimerStatus.Stopped || active is null)
        {
            return list;
        }

        // There can only be one current session. Remove the persisted copy (which may
        // be slightly behind the monotonic live value) and overlay the live state.
        list.RemoveAll(session => session.IsActive);
        var clone = active.DeepClone();
        clone.State = _controller.Status;
        if (clone.State == TimerStatus.Running)
        {
            var closedMs = clone.Segments
                .Where(segment => segment.EndUtc.HasValue)
                .Sum(segment => Math.Max(0L, segment.DurationMs));
            var open = clone.Segments.LastOrDefault(segment => segment.EndUtc is null);
            if (open is not null)
            {
                open.DurationMs = Math.Max(
                    0,
                    (long)_controller.CurrentDuration.TotalMilliseconds - closedMs);
            }
        }

        list.Add(clone);
        return list;
    }

    private void OnTickCompleted(object? sender, EventArgs e)
    {
        var now = _controller.Engine.UtcNow;
        var currentZone = _controller.LocalTimeZone;
        var currentOffset = currentZone.GetUtcOffset(now);
        if ((currentZone.Id != _timeZone.Id || currentOffset != _lastZoneOffset) &&
            !_timeZoneRefreshScheduled)
        {
            _timeZoneRefreshScheduled = true;
            _timeZone = currentZone;
            _lastZoneOffset = currentOffset;
            _ = RefreshAfterTimeZoneChangeAsync();
        }

        var localDay = TimeText.GetLocalDate(now, _timeZone);
        if (localDay != _today && !_dayRefreshScheduled)
        {
            _dayRefreshScheduled = true;
            _ = RefreshAfterDayRolloverAsync();
        }

        UpdateLiveView(force: false);
    }

    public async Task HandleSystemTimeChangedAsync()
    {
        _timeZone = _controller.LocalTimeZone;
        _lastZoneOffset = _timeZone.GetUtcOffset(_controller.Engine.UtcNow);
        try
        {
            await RefreshTodayAsync();
            if (IsHistoryPage)
            {
                await LoadHistoryAsync(extend: false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось обновить данные после смены времени.", exception);
        }
    }

    private async Task RefreshAfterTimeZoneChangeAsync()
    {
        try
        {
            await RefreshTodayAsync();
            if (IsHistoryPage)
            {
                await LoadHistoryAsync(extend: false);
            }
        }
        finally
        {
            _timeZoneRefreshScheduled = false;
        }
    }

    private async Task RefreshAfterDayRolloverAsync()
    {
        try
        {
            await RefreshTodayAsync();
            if (IsHistoryPage)
            {
                await LoadHistoryAsync(extend: false);
            }
        }
        finally
        {
            _dayRefreshScheduled = false;
        }
    }

    private void UpdateLiveView(bool force)
    {
        UpdateTimerText(force);

        if (!force && _controller.Status == TimerStatus.Stopped && _lastRenderedSecond < 0)
        {
            return;
        }

        var currentSecond = (long)_controller.CurrentDuration.TotalSeconds;
        if (!force && currentSecond == _lastRenderedSecond)
        {
            return;
        }

        _lastRenderedSecond = currentSecond;
        var summary = BuildTodaySummary();
        ApplyTodaySummary(summary, rebuildRows: false);
    }

    private void UpdateTimerText(bool force = false)
    {
        var text = _controller.Status == TimerStatus.Stopped
            ? "00:00:00"
            : TimeText.FormatClock(_controller.CurrentDuration);
        if (text != _timerText || force)
        {
            _timerText = text;
            OnPropertyChanged(nameof(TimerText));
        }
    }

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e)
    {
        UpdateStatusProperties();
        SyncCommentFromEngine();
        UpdateTimerText(force: true);
        _lastRenderedSecond = -1;
        _ = RefreshTodayAsync();
        if (IsHistoryPage)
        {
            _ = LoadHistoryAsync(extend: false);
        }
    }

    private async void OnSessionsChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshTodayAsync();
            if (IsHistoryPage)
            {
                await LoadHistoryAsync(extend: false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось обновить список записей.", exception);
        }
    }

    private void UpdateStatusProperties()
    {
        _status = _controller.Status;
        _statusText = _status switch
        {
            TimerStatus.Running => Strings.Get("StatusRunning"),
            TimerStatus.PausedManual => Strings.Get("StatusPausedManual"),
            TimerStatus.PausedIdle => Strings.Get("StatusPausedIdle"),
            _ => Strings.Get("StatusStopped")
        };
        _primaryActionText = _status switch
        {
            TimerStatus.Running => Strings.Get("Pause"),
            TimerStatus.PausedManual or TimerStatus.PausedIdle => Strings.Get("Continue"),
            _ => Strings.Get("Start")
        };
        OnPropertiesChanged(
            nameof(Status),
            nameof(StatusText),
            nameof(PrimaryActionText),
            nameof(HasActiveSession),
            nameof(IsRunning),
            nameof(IsPaused),
            nameof(IsStopped),
            nameof(CanStop),
            nameof(IsCommentEnabled));
        (StopCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void SyncCommentFromEngine()
    {
        var comment = _controller.HasActiveSession
            ? _controller.Engine.ActiveSession?.Comment ?? string.Empty
            : string.Empty;
        if (_commentText == comment)
        {
            return;
        }

        _suppressComment = true;
        try
        {
            CommentText = comment;
        }
        finally
        {
            _suppressComment = false;
        }
    }

    private void ScheduleCommentSave()
    {
        CancelPendingCommentSave();
        _commentSaveCts = new CancellationTokenSource();
        var token = _commentSaveCts.Token;
        _ = SaveCommentAfterDelayAsync(token);
    }

    private async Task SaveCommentAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(700, token);
            if (!token.IsCancellationRequested && _controller.HasActiveSession)
            {
                await _controller.PersistCommentAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke or by stopping the timer.
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось сохранить комментарий.", exception);
        }
    }

    private void CancelPendingCommentSave()
    {
        _commentSaveCts?.Cancel();
        _commentSaveCts?.Dispose();
        _commentSaveCts = null;
    }

    private void SetHistoryLoading(bool value)
    {
        if (_isHistoryLoading == value)
        {
            return;
        }

        _isHistoryLoading = value;
        OnPropertiesChanged(nameof(IsHistoryLoading), nameof(IsHistoryEmptyVisible));
        (LoadEarlierCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void HandleCommandException(Exception exception)
    {
        _logger.Error("Ошибка выполнения команды.", exception);
        _dialogs.ShowError(Strings.Get("UnknownError"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.TickCompleted -= OnTickCompleted;
        _controller.TimerStateChanged -= OnTimerStateChanged;
        _controller.SessionsChanged -= OnSessionsChanged;
        CancelPendingCommentSave();
        _refreshGate.Dispose();
    }
}
