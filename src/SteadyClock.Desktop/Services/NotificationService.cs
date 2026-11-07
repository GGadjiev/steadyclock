using Avalonia.Threading;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Views;

namespace SteadyClock.Desktop.Services;

public sealed class NotificationService : IDisposable
{
    private readonly TimeTrackingController _controller;
    private readonly SoundService _sounds;
    private readonly ProcessModeService _processMode;
    private readonly IAppLogger _logger;
    private DateTimeOffset _lastErrorNotification = DateTimeOffset.MinValue;
    private ToastWindow? _current;
    private string _currentTag = string.Empty;
    private bool _disposed;

    public NotificationService(
        TimeTrackingController controller,
        SoundService sounds,
        ProcessModeService processMode,
        IAppLogger logger)
    {
        _controller = controller;
        _sounds = sounds;
        _processMode = processMode;
        _logger = logger;

        _controller.AutoPaused += OnAutoPaused;
        _controller.AutoResumed += OnAutoResumed;
        _controller.ReminderDue += OnReminderDue;
        _controller.PersistenceFailed += OnPersistenceFailed;
        _controller.TimerStateChanged += OnTimerStateChanged;
        _processMode.ProcessSessionStarted += OnProcessSessionStarted;
        _processMode.ProcessSessionStopped += OnProcessSessionStopped;
    }

    private NotificationSettings Settings => _controller.CurrentSettings.Notifications;

    private void OnAutoPaused(object? sender, AutoPausedEventArgs e)
    {
        var rule = Settings.AutoPause;
        if (!Settings.Enabled || rule is null)
        {
            return;
        }

        PlayIfAllowed(rule.SoundId);
        if (!rule.ShowToast || IsQuietHours())
        {
            return;
        }

        var message = e.Cause switch
        {
            IdlePauseCause.ScreenLock => Strings.Get("AutopauseLockMessage"),
            IdlePauseCause.Sleep => Strings.Get("AutopauseSleepMessage"),
            IdlePauseCause.SystemGap => Strings.Get("AutopauseGapMessage"),
            _ => Strings.Get("AutopauseIdleMessage")
        };
        Show(
            Strings.Get("NotificationAutopauseTitle"),
            message,
            autoCloseSeconds: Settings.ToastDurationSeconds,
            tag: "autopause");
    }

    private void OnAutoResumed(object? sender, EventArgs e) => CloseTag("autopause");

    private void OnReminderDue(object? sender, EventArgs e)
    {
        var rule = Settings.Reminder;
        if (!Settings.Enabled || rule is null)
        {
            return;
        }

        PlayIfAllowed(rule.SoundId);
        if (!rule.ShowToast || IsQuietHours())
        {
            return;
        }

        void StartTimer()
        {
            Dispatcher.UIThread.Post(() => _ = StartTimerAsync());
        }

        Show(
            Strings.Get("ReminderTitle"),
            Strings.Format("ReminderMessageFormat", _controller.CurrentSettings.ForgotTimerAfterMinutes),
            Strings.Get("ReminderStart"),
            StartTimer,
            Strings.Get("ReminderSnooze"),
            secondaryAction: null,
            autoCloseSeconds: Settings.ToastDurationSeconds,
            tag: "reminder");
    }

    private async Task StartTimerAsync()
    {
        try
        {
            await _controller.StartIfStoppedAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось запустить таймер из уведомления.", exception);
        }
    }

    private void OnTimerStateChanged(object? sender, TimerStatusChangedEventArgs e)
    {
        if (e.Status == TimerStatus.Running)
        {
            CloseTag("reminder");
        }
    }

    private void OnPersistenceFailed(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastErrorNotification < TimeSpan.FromMinutes(5))
        {
            return;
        }

        var rule = Settings.Error;
        if (!Settings.Enabled || rule is null)
        {
            return;
        }

        _lastErrorNotification = now;
        PlayIfAllowed(rule.SoundId);
        if (rule.ShowToast)
        {
            Show(
                Strings.Get("PersistenceErrorTitle"),
                Strings.Get("PersistenceErrorMessage"),
                autoCloseSeconds: Settings.ToastDurationSeconds,
                tag: "error");
        }
    }

    private void OnProcessSessionStarted(object? sender, string applicationName)
    {
        var rule = Settings.ProcessStarted;
        if (!Settings.Enabled || rule is null)
        {
            return;
        }

        PlayIfAllowed(rule.SoundId);
        if (!rule.ShowToast || IsQuietHours())
        {
            return;
        }

        Show(
            Strings.Get("ProcessStartedNotificationTitle"),
            Strings.Format("ProcessStartedNotificationMessageFormat", applicationName),
            autoCloseSeconds: Settings.ToastDurationSeconds,
            tag: "process");
    }

    private void OnProcessSessionStopped(object? sender, string applicationName)
    {
        var rule = Settings.ProcessStopped;
        if (!Settings.Enabled || rule is null)
        {
            return;
        }

        PlayIfAllowed(rule.SoundId);
        if (!rule.ShowToast || IsQuietHours())
        {
            return;
        }

        Show(
            Strings.Get("ProcessStoppedNotificationTitle"),
            Strings.Format("ProcessStoppedNotificationMessageFormat", applicationName),
            autoCloseSeconds: Settings.ToastDurationSeconds,
            tag: "process");
    }

    private void PlayIfAllowed(string? soundId)
    {
        if (!Settings.Enabled || IsQuietHours())
        {
            return;
        }

        _sounds.Play(soundId);
    }

    private bool IsQuietHours()
    {
        if (!Settings.QuietHoursEnabled)
        {
            return false;
        }

        if (!TryParseTime(Settings.QuietHoursStart, out var start) ||
            !TryParseTime(Settings.QuietHoursEnd, out var end))
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            _controller.LocalTimeZone);
        var current = TimeOnly.FromDateTime(local.DateTime);
        if (start == end)
        {
            return false;
        }

        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    private static bool TryParseTime(string? text, out TimeOnly value)
    {
        return TimeOnly.TryParseExact(
            (text ?? string.Empty).Trim(),
            new[] { "H:mm", "HH:mm" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out value);
    }

    private void Show(
        string title,
        string message,
        string? primaryText = null,
        Action? primaryAction = null,
        string? secondaryText = null,
        Action? secondaryAction = null,
        int autoCloseSeconds = 8,
        string tag = "")
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _current?.Close();
                var window = new ToastWindow(
                    title,
                    message,
                    primaryText,
                    primaryAction,
                    secondaryText,
                    secondaryAction,
                    autoCloseSeconds,
                    tag);
                _current = window;
                _currentTag = tag;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_current, window))
                    {
                        _current = null;
                        _currentTag = string.Empty;
                    }
                };
                window.Show();
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось показать уведомление.", exception);
        }
    }

    private void CloseTag(string tag)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_current is not null &&
                string.Equals(_currentTag, tag, StringComparison.Ordinal))
            {
                _current.Close();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.AutoPaused -= OnAutoPaused;
        _controller.AutoResumed -= OnAutoResumed;
        _controller.ReminderDue -= OnReminderDue;
        _controller.PersistenceFailed -= OnPersistenceFailed;
        _controller.TimerStateChanged -= OnTimerStateChanged;
        _processMode.ProcessSessionStarted -= OnProcessSessionStarted;
        _processMode.ProcessSessionStopped -= OnProcessSessionStopped;
        _current?.Close();
        _current = null;
    }
}
