using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int HistoryChunkDays = 30;
    private readonly TimeTrackingController _controller;
    private readonly IUserDialogs _dialogs;
    private readonly IAppLogger _logger;
    private readonly TimeZoneInfo _timeZone;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<TimeSession> _cachedTodaySessions = Array.Empty<TimeSession>();
    private DateOnly _today;
    private DateOnly? _historyStartDate;
    private long _lastRenderedSecond = -1;
    private bool _disposed;

    [ObservableProperty]
    private string _timerText = "00:00:00";

    [ObservableProperty]
    private string _statusText = Strings.Get("StatusStopped");

    [ObservableProperty]
    private string _primaryActionText = Strings.Get("Start");

    [ObservableProperty]
    private string _todayTotalText = "00:00:00";

    [ObservableProperty]
    private string _todaySessionCountText = Strings.FormatSessionCount(0);

    [ObservableProperty]
    private string _todayDateText = string.Empty;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private TimerStatus _status = TimerStatus.Stopped;

    [ObservableProperty]
    private bool _isHistoryPage;

    [ObservableProperty]
    private bool _isHistoryLoading;

    [ObservableProperty]
    private bool _isHistoryEmpty = true;

    [ObservableProperty]
    private bool _canLoadEarlier;

    public MainViewModel(
        TimeTrackingController controller,
        IUserDialogs dialogs,
        IAppLogger logger)
    {
        _controller = controller;
        _dialogs = dialogs;
        _logger = logger;
        _timeZone = controller.LocalTimeZone;
        _today = TimeText.GetLocalDate(controller.Engine.UtcNow, _timeZone);
        TodayDateText = _today.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy");
        ApplyStatus(controller.Status);

        _controller.TickCompleted += OnTickCompleted;
        _controller.TimerStateChanged += OnTimerStateChanged;
        _controller.SessionsChanged += OnSessionsChanged;
    }

    public ObservableCollection<SessionLineViewModel> TodaySessions { get; } = new();
    public ObservableCollection<HistoryGroupViewModel> HistoryGroups { get; } = new();

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsTimerPage => !IsHistoryPage;
    public string HeaderTitle => IsHistoryPage ? Strings.Get("HistoryTitle") : Strings.Get("AppTitle");

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsHistoryPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimerPage));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    partial void OnIsHistoryLoadingChanged(bool value) => CanLoadEarlier = !value;

    public async Task InitializeAsync()
    {
        await RefreshTodayAsync();
        UpdateLiveView(force: true);
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
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
        catch (Exception exception)
        {
            _logger.Error("Ошибка управления таймером.", exception);
            ErrorMessage = Strings.Get("UnknownError");
        }
        finally
        {
            IsBusy = false;
            ApplyStatus(_controller.Status);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await _controller.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось остановить таймер.", exception);
            ErrorMessage = Strings.Get("UnknownError");
        }
        finally
        {
            IsBusy = false;
            ApplyStatus(_controller.Status);
        }
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        if (IsHistoryPage)
        {
            return;
        }

        IsHistoryPage = true;
        _historyStartDate ??= _today.AddDays(-(HistoryChunkDays - 1));
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task ShowTimerAsync()
    {
        if (!IsHistoryPage)
        {
            return;
        }

        IsHistoryPage = false;
        await RefreshTodayAsync();
    }

    [RelayCommand]
    private Task AddManualAsync() => _dialogs.ShowSessionEditorAsync(null);

    [RelayCommand]
    private async Task LoadEarlierAsync()
    {
        if (IsHistoryLoading)
        {
            return;
        }

        _historyStartDate = (_historyStartDate ?? _today)
            .AddDays(-HistoryChunkDays);
        await LoadHistoryAsync();
    }

    private async Task EditSessionAsync(SessionLineViewModel row)
    {
        try
        {
            await _dialogs.ShowSessionEditorAsync(row.SessionId);
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось открыть запись.", exception);
            ErrorMessage = Strings.Get("UnknownError");
        }
    }

    private async Task DeleteSessionAsync(SessionLineViewModel row)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Strings.Get("ConfirmDeleteTitle"),
            Strings.Get("ConfirmDeleteMessage"),
            Strings.Get("DeleteButton"),
            Strings.Get("CancelButton"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        var result = await _controller.DeleteSessionAsync(row.SessionId);
        if (!result.Success)
        {
            ErrorMessage = Strings.Get("UnknownError");
        }
    }

    public void UpdateLiveView(bool force = false)
    {
        var now = _controller.Engine.UtcNow;
        var localDay = TimeText.GetLocalDate(now, _timeZone);
        if (localDay != _today)
        {
            _today = localDay;
            TodayDateText = _today.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy");
            _ = RefreshAfterDayChangeAsync();
            return;
        }

        var seconds = (long)_controller.CurrentDuration.TotalSeconds;
        if (!force && seconds == _lastRenderedSecond)
        {
            return;
        }

        _lastRenderedSecond = seconds;
        TimerText = _controller.Status == TimerStatus.Stopped
            ? "00:00:00"
            : TimeText.FormatClock(_controller.CurrentDuration);
        ApplyStatus(_controller.Status);

        var summary = BuildTodaySummary(now);
        TodayTotalText = TimeText.FormatClock(summary.TotalDuration);
        TodaySessionCountText = Strings.FormatSessionCount(summary.SessionCount);
        IsEmpty = summary.Sessions.Count == 0;

        TodaySessions.Clear();
        foreach (var row in BuildRows(summary.Sessions))
        {
            TodaySessions.Add(row);
        }
    }

    private async Task RefreshAfterDayChangeAsync()
    {
        _historyStartDate = _today.AddDays(-(HistoryChunkDays - 1));
        await RefreshTodayAsync();
        if (IsHistoryPage)
        {
            await LoadHistoryAsync();
        }
    }

    private async Task RefreshTodayAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            _today = TimeText.GetLocalDate(_controller.Engine.UtcNow, _timeZone);
            TodayDateText = _today.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy");
            _cachedTodaySessions = await _controller.GetSessionsForLocalDayAsync(_today);
            UpdateLiveView(force: true);
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось загрузить сессии за сегодня.", exception);
            ErrorMessage = Strings.Get("UnknownError");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (IsHistoryLoading)
        {
            return;
        }

        IsHistoryLoading = true;
        IsHistoryEmpty = false;
        await _refreshGate.WaitAsync();
        try
        {
            var start = _historyStartDate ?? _today.AddDays(-(HistoryChunkDays - 1));
            var stored = await _controller.GetSessionsForLocalRangeAsync(start, _today);
            var now = _controller.Engine.UtcNow;
            var merged = MergeActiveSession(stored, now);
            var summaries = SessionTimeline.BuildRange(
                merged,
                start,
                _today,
                _timeZone,
                now);

            HistoryGroups.Clear();
            foreach (var summary in summaries.Reverse())
            {
                HistoryGroups.Add(new HistoryGroupViewModel(
                    summary.Date,
                    summary.TotalDuration,
                    BuildRows(summary.Sessions)));
            }

            _historyStartDate = start;
            IsHistoryEmpty = HistoryGroups.Count == 0;
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось загрузить историю.", exception);
            ErrorMessage = Strings.Get("UnknownError");
        }
        finally
        {
            _refreshGate.Release();
            IsHistoryLoading = false;
        }
    }

    private LocalDaySummary BuildTodaySummary(DateTimeOffset now)
    {
        var merged = MergeActiveSession(_cachedTodaySessions, now);
        return SessionTimeline.BuildDay(merged, _today, _timeZone, now);
    }

    private IReadOnlyList<TimeSession> MergeActiveSession(
        IEnumerable<TimeSession> storedSessions,
        DateTimeOffset now)
    {
        var sessions = storedSessions.ToList();
        var active = _controller.Engine.ActiveSession;
        if (_controller.Status == TimerStatus.Stopped || active is null)
        {
            return sessions;
        }

        sessions.RemoveAll(session => session.IsActive);
        var clone = active.DeepClone();
        clone.State = _controller.Status;
        if (clone.State == TimerStatus.Running)
        {
            var closed = clone.Segments
                .Where(segment => segment.EndUtc.HasValue)
                .Sum(segment => Math.Max(0L, segment.DurationMs));
            var open = clone.Segments.LastOrDefault(segment => segment.EndUtc is null);
            if (open is not null)
            {
                open.DurationMs = Math.Max(
                    0,
                    (long)_controller.CurrentDuration.TotalMilliseconds - closed);
            }
        }

        _ = now;
        sessions.Add(clone);
        return sessions;
    }

    private List<SessionLineViewModel> BuildRows(IReadOnlyList<SessionDaySlice> slices)
    {
        return slices
            .Select(slice => new SessionLineViewModel(
                slice,
                _timeZone,
                EditSessionAsync,
                DeleteSessionAsync))
            .ToList();
    }

    private void ApplyStatus(TimerStatus status)
    {
        Status = status;
        StatusText = status switch
        {
            TimerStatus.Running => Strings.Get("StatusRunning"),
            TimerStatus.PausedManual => Strings.Get("StatusPausedManual"),
            TimerStatus.PausedIdle => Strings.Get("StatusPausedIdle"),
            _ => Strings.Get("StatusStopped")
        };
        PrimaryActionText = status switch
        {
            TimerStatus.Running => Strings.Get("Pause"),
            TimerStatus.PausedManual or TimerStatus.PausedIdle => Strings.Get("Continue"),
            _ => Strings.Get("Start")
        };
        CanStop = _controller.HasActiveSession;
    }

    private void OnTickCompleted(object? sender, EventArgs e) => UpdateLiveView();

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e)
    {
        ApplyStatus(e.Status);
        UpdateLiveView(force: true);
    }

    private async void OnSessionsChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshTodayAsync();
            if (IsHistoryPage)
            {
                await LoadHistoryAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось обновить список сессий.", exception);
        }
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
        _refreshGate.Dispose();
    }
}

