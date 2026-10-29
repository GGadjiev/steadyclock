using MyTimeTracker.App.Infrastructure;
using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.ViewModels;

public sealed class SessionRowViewModel : ObservableObject
{
    private readonly TimeZoneInfo _timeZone;
    private TimeSpan _duration;
    private string _comment;
    private bool _isRunning;

    public SessionRowViewModel(
        long sessionId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeSpan duration,
        string comment,
        SessionSource source,
        bool isActive,
        bool isRunning,
        TimeZoneInfo timeZone,
        Func<SessionRowViewModel, Task> edit,
        Func<SessionRowViewModel, Task> delete)
    {
        SessionId = sessionId;
        _timeZone = timeZone;
        _duration = duration;
        _comment = comment ?? string.Empty;
        Source = source;
        IsActive = isActive;
        _isRunning = isRunning;
        StartTimeText = TimeText.FormatTime(startUtc, timeZone);
        EndTimeText = isRunning ? Strings.Get("Now") : TimeText.FormatTime(endUtc, timeZone);
        CanManage = !isActive;
        EditCommand = new AsyncRelayCommand(
            () => edit(this),
            () => CanManage);
        DeleteCommand = new AsyncRelayCommand(
            () => delete(this),
            () => CanManage);
    }

    public long SessionId { get; }
    public SessionSource Source { get; }
    public bool IsManual => Source == SessionSource.Manual;
    public bool IsActive { get; }
    public bool CanManage { get; }
    public string StartTimeText { get; }
    public string EndTimeText { get; private set; }
    public string Comment => _comment;
    public bool HasComment => !string.IsNullOrWhiteSpace(_comment);
    public string DurationText => TimeText.FormatDuration(_duration);
    public string StartEndText => $"{StartTimeText} – {EndTimeText}";
    public bool IsRunning => _isRunning;
    public string BadgeText => IsActive
        ? (_isRunning ? Strings.Get("Now") : Strings.Get("PausedBadge"))
        : (IsManual ? Strings.Get("ManualBadge") : string.Empty);
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);
    public System.Windows.Input.ICommand EditCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }

    public void UpdateLive(TimeSpan duration, bool isRunning, string comment)
    {
        if (_duration != duration)
        {
            _duration = duration;
            OnPropertyChanged(nameof(DurationText));
        }

        if (_isRunning != isRunning)
        {
            _isRunning = isRunning;
            EndTimeText = isRunning ? Strings.Get("Now") : EndTimeText;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(EndTimeText));
            OnPropertyChanged(nameof(StartEndText));
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(HasBadge));
        }

        if (_comment != comment)
        {
            _comment = comment ?? string.Empty;
            OnPropertyChanged(nameof(Comment));
            OnPropertyChanged(nameof(HasComment));
        }
    }
}
