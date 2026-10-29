using System.Windows;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.Views;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Services;

/// <summary>In-app local toast notifications. No Windows notification servers or network.</summary>
public sealed class NotificationService : IDisposable
{
    private readonly IAppLogger _logger;
    private ToastWindow? _current;
    private string _currentTag = string.Empty;

    public NotificationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void ShowAutoPause(IdlePauseCause cause)
    {
        var message = cause switch
        {
            IdlePauseCause.Idle => Strings.Get("AutopauseIdleMessage"),
            IdlePauseCause.ScreenLock => Strings.Get("AutopauseLockMessage"),
            IdlePauseCause.Sleep => Strings.Get("AutopauseSleepMessage"),
            IdlePauseCause.SystemGap => Strings.Get("AutopauseGapMessage"),
            _ => Strings.Get("AutopauseIdleMessage")
        };

        Show(new ToastWindow(
            Strings.Get("NotificationAutopauseTitle"),
            message,
            autoCloseSeconds: 8,
            tag: "autopause"), "autopause");
    }

    public void ShowForgotTimer(int minutes, Action startTimer)
    {
        Show(new ToastWindow(
            Strings.Get("ReminderTitle"),
            Strings.Format("ReminderMessageFormat", minutes),
            Strings.Get("ReminderStart"),
            startTimer,
            Strings.Get("ReminderSnooze"),
            secondaryAction: null,
            autoCloseSeconds: 12,
            tag: "reminder"), "reminder");
    }

    public void ShowWarning(string title, string message)
    {
        Show(new ToastWindow(
            title,
            message,
            autoCloseSeconds: 10,
            tag: "warning"), "warning");
    }

    public void ShowDataWarning()
    {
        Show(new ToastWindow(
            Strings.Get("PersistenceErrorTitle"),
            Strings.Get("PersistenceErrorMessage"),
            autoCloseSeconds: 10,
            tag: "data-error"), "data-error");
    }

    public void CloseTag(string tag)
    {
        if (_current is not null && string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            _current.Close();
        }
    }

    public void CloseAll() => _current?.Close();

    private void Show(ToastWindow window, string tag)
    {
        try
        {
            if (_current is not null)
            {
                _current.Close();
            }

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
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось показать уведомление.", exception);
        }
    }

    public void Dispose()
    {
        _current?.Close();
        _current = null;
    }
}
