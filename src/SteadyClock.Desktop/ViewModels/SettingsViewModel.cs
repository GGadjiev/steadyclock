using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly TimeTrackingController _controller;
    private readonly ISettingsStore _settingsStore;
    private readonly SoundService _sounds;

    [ObservableProperty]
    private bool _autoStartTimer;

    [ObservableProperty]
    private string _idleThresholdText;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _toastDurationText;

    [ObservableProperty]
    private bool _quietHoursEnabled;

    [ObservableProperty]
    private string _quietHoursStart;

    [ObservableProperty]
    private string _quietHoursEnd;

    [ObservableProperty]
    private bool _remindersEnabled;

    [ObservableProperty]
    private bool _forgotTimerReminderEnabled;

    [ObservableProperty]
    private string _forgotTimerAfterText;

    [ObservableProperty]
    private string _reminderRepeatText;

    [ObservableProperty]
    private string _workStart;

    [ObservableProperty]
    private string _workEnd;

    [ObservableProperty]
    private bool _monday;

    [ObservableProperty]
    private bool _tuesday;

    [ObservableProperty]
    private bool _wednesday;

    [ObservableProperty]
    private bool _thursday;

    [ObservableProperty]
    private bool _friday;

    [ObservableProperty]
    private bool _saturday;

    [ObservableProperty]
    private bool _sunday;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        AppSettings settings,
        TimeTrackingController controller,
        ISettingsStore settingsStore,
        SoundService sounds)
    {
        _original = settings.DeepClone();
        _controller = controller;
        _settingsStore = settingsStore;
        _sounds = sounds;

        AutoStartTimer = settings.AutoStartTimer;
        IdleThresholdText = settings.IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        CloseToTray = settings.CloseToTray;
        NotificationsEnabled = settings.Notifications.Enabled;
        ToastDurationText = settings.Notifications.ToastDurationSeconds.ToString(CultureInfo.InvariantCulture);
        QuietHoursEnabled = settings.Notifications.QuietHoursEnabled;
        QuietHoursStart = settings.Notifications.QuietHoursStart;
        QuietHoursEnd = settings.Notifications.QuietHoursEnd;
        RemindersEnabled = settings.RemindersEnabled;
        ForgotTimerReminderEnabled = settings.ForgotTimerReminderEnabled;
        ForgotTimerAfterText = settings.ForgotTimerAfterMinutes.ToString(CultureInfo.InvariantCulture);
        ReminderRepeatText = settings.ReminderRepeatMinutes.ToString(CultureInfo.InvariantCulture);
        WorkStart = settings.WorkStart;
        WorkEnd = settings.WorkEnd;
        Monday = settings.WorkDays.ElementAtOrDefault(0);
        Tuesday = settings.WorkDays.ElementAtOrDefault(1);
        Wednesday = settings.WorkDays.ElementAtOrDefault(2);
        Thursday = settings.WorkDays.ElementAtOrDefault(3);
        Friday = settings.WorkDays.ElementAtOrDefault(4);
        Saturday = settings.WorkDays.ElementAtOrDefault(5);
        Sunday = settings.WorkDays.ElementAtOrDefault(6);

        Rules = new ObservableCollection<NotificationRuleViewModel>
        {
            new(
                NotificationEventKind.AutoPause,
                Strings.Get("NotificationEventAutoPause"),
                settings.Notifications.AutoPause,
                sound => _sounds.Play(sound.Id)),
            new(
                NotificationEventKind.Reminder,
                Strings.Get("NotificationEventReminder"),
                settings.Notifications.Reminder,
                sound => _sounds.Play(sound.Id)),
            new(
                NotificationEventKind.Error,
                Strings.Get("NotificationEventError"),
                settings.Notifications.Error,
                sound => _sounds.Play(sound.Id))
        };
    }

    public string Title => Strings.Get("SettingsTitle");
    public string SaveButtonText => Strings.Get("SaveButton");
    public string CancelButtonText => Strings.Get("CancelButton");
    public ObservableCollection<NotificationRuleViewModel> Rules { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (!int.TryParse(IdleThresholdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle) ||
            !int.TryParse(ToastDurationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var toastDuration) ||
            !int.TryParse(ForgotTimerAfterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var after) ||
            !int.TryParse(ReminderRepeatText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat))
        {
            ErrorMessage = Strings.Get("SettingsNumberError");
            return;
        }

        var settings = _original.DeepClone();
        settings.AutoStartTimer = AutoStartTimer;
        settings.IdleThresholdSeconds = idle;
        settings.CloseToTray = CloseToTray;
        settings.RemindersEnabled = RemindersEnabled;
        settings.ForgotTimerReminderEnabled = ForgotTimerReminderEnabled;
        settings.ForgotTimerAfterMinutes = after;
        settings.ReminderRepeatMinutes = repeat;
        settings.WorkDays = new[] { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday };
        settings.WorkStart = WorkStart.Trim();
        settings.WorkEnd = WorkEnd.Trim();
        settings.Notifications.Enabled = NotificationsEnabled;
        settings.Notifications.ToastDurationSeconds = toastDuration;
        settings.Notifications.QuietHoursEnabled = QuietHoursEnabled;
        settings.Notifications.QuietHoursStart = QuietHoursStart.Trim();
        settings.Notifications.QuietHoursEnd = QuietHoursEnd.Trim();

        if (SettingsValidator.TryParseTime(settings.WorkStart, out var start))
        {
            settings.WorkStart = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (SettingsValidator.TryParseTime(settings.WorkEnd, out var end))
        {
            settings.WorkEnd = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (SettingsValidator.TryParseTime(settings.Notifications.QuietHoursStart, out var quietStart))
        {
            settings.Notifications.QuietHoursStart = quietStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (SettingsValidator.TryParseTime(settings.Notifications.QuietHoursEnd, out var quietEnd))
        {
            settings.Notifications.QuietHoursEnd = quietEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        foreach (var rule in Rules)
        {
            var target = rule.Kind switch
            {
                NotificationEventKind.AutoPause => settings.Notifications.AutoPause,
                NotificationEventKind.Reminder => settings.Notifications.Reminder,
                _ => settings.Notifications.Error
            };
            rule.ApplyTo(target);
        }

        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        IsBusy = true;
        try
        {
            await _settingsStore.SaveAsync(settings);
            _controller.UpdateSettings(settings);
            CloseRequested?.Invoke(true);
        }
        catch (Exception)
        {
            ErrorMessage = Strings.Get("SettingsSaveFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
