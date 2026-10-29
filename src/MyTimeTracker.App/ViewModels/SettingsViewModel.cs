using System.Globalization;
using MyTimeTracker.App.Infrastructure;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.Services;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsCoordinator _coordinator;
    private string _idleThresholdSeconds = "120";
    private bool _autoStartTimer;
    private bool _closeToTray;
    private bool _autoStartWithWindows;
    private bool _launchInTrayOnAutoStart;
    private bool _remindersEnabled;
    private bool _forgotTimerReminderEnabled;
    private string _forgotTimerAfterMinutes = "15";
    private string _reminderRepeatMinutes = "30";
    private string _workStart = "09:00";
    private string _workEnd = "18:00";
    private bool _monday;
    private bool _tuesday;
    private bool _wednesday;
    private bool _thursday;
    private bool _friday;
    private bool _saturday;
    private bool _sunday;
    private HotkeyBinding _toggleHotkey = HotkeyBinding.DefaultToggle();
    private HotkeyBinding _stopHotkey = HotkeyBinding.DefaultStop();
    private HotkeyBinding _showHideHotkey = HotkeyBinding.DefaultShowHide();
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public SettingsViewModel(SettingsCoordinator coordinator)
    {
        _coordinator = coordinator;
        LoadFrom(coordinator.Current);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    public string Title => Strings.Get("SettingsTitle");
    public string SaveButtonText => Strings.Get("SaveButton");
    public string CancelButtonText => Strings.Get("CancelButton");
    public string ResetButtonText => Strings.Get("ResetSettings");

    public bool IsAutomaticStartEnabled
    {
        get => _autoStartTimer;
        set => SetProperty(ref _autoStartTimer, value);
    }

    public string IdleThresholdSeconds
    {
        get => _idleThresholdSeconds;
        set => SetProperty(ref _idleThresholdSeconds, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set => SetProperty(ref _autoStartWithWindows, value);
    }

    public bool LaunchInTrayOnAutoStart
    {
        get => _launchInTrayOnAutoStart;
        set => SetProperty(ref _launchInTrayOnAutoStart, value);
    }

    public HotkeyBinding ToggleHotkey
    {
        get => _toggleHotkey;
        set => SetProperty(ref _toggleHotkey, value);
    }

    public HotkeyBinding StopHotkey
    {
        get => _stopHotkey;
        set => SetProperty(ref _stopHotkey, value);
    }

    public HotkeyBinding ShowHideHotkey
    {
        get => _showHideHotkey;
        set => SetProperty(ref _showHideHotkey, value);
    }

    public bool RemindersEnabled
    {
        get => _remindersEnabled;
        set => SetProperty(ref _remindersEnabled, value);
    }

    public bool ForgotTimerReminderEnabled
    {
        get => _forgotTimerReminderEnabled;
        set => SetProperty(ref _forgotTimerReminderEnabled, value);
    }

    public string ForgotTimerAfterMinutes
    {
        get => _forgotTimerAfterMinutes;
        set => SetProperty(ref _forgotTimerAfterMinutes, value);
    }

    public string ReminderRepeatMinutes
    {
        get => _reminderRepeatMinutes;
        set => SetProperty(ref _reminderRepeatMinutes, value);
    }

    public string WorkStart
    {
        get => _workStart;
        set => SetProperty(ref _workStart, value);
    }

    public string WorkEnd
    {
        get => _workEnd;
        set => SetProperty(ref _workEnd, value);
    }

    public bool Monday { get => _monday; set => SetProperty(ref _monday, value); }
    public bool Tuesday { get => _tuesday; set => SetProperty(ref _tuesday, value); }
    public bool Wednesday { get => _wednesday; set => SetProperty(ref _wednesday, value); }
    public bool Thursday { get => _thursday; set => SetProperty(ref _thursday, value); }
    public bool Friday { get => _friday; set => SetProperty(ref _friday, value); }
    public bool Saturday { get => _saturday; set => SetProperty(ref _saturday, value); }
    public bool Sunday { get => _sunday; set => SetProperty(ref _sunday, value); }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public event Action<bool>? CloseRequested;

    public void ResetToDefaults()
    {
        LoadFrom(AppSettings.CreateDefault());
        ErrorMessage = string.Empty;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (!int.TryParse(IdleThresholdSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle) ||
            !int.TryParse(ForgotTimerAfterMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var after) ||
            !int.TryParse(ReminderRepeatMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat))
        {
            ErrorMessage = Strings.Get("SettingsNumberError");
            return;
        }

        var start = WorkStart.Trim();
        var end = WorkEnd.Trim();
        if (SettingsValidator.TryParseTime(start, out var parsedStart))
        {
            start = parsedStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (SettingsValidator.TryParseTime(end, out var parsedEnd))
        {
            end = parsedEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var candidate = new AppSettings
        {
            AutoStartTimer = IsAutomaticStartEnabled,
            IdleThresholdSeconds = idle,
            CloseToTray = CloseToTray,
            AutoStartWithWindows = AutoStartWithWindows,
            LaunchInTrayOnAutoStart = LaunchInTrayOnAutoStart,
            ToggleTimerHotkey = ToggleHotkey.Clone(),
            StopTimerHotkey = StopHotkey.Clone(),
            ShowHideHotkey = ShowHideHotkey.Clone(),
            RemindersEnabled = RemindersEnabled,
            ForgotTimerReminderEnabled = ForgotTimerReminderEnabled,
            ForgotTimerAfterMinutes = after,
            ReminderRepeatMinutes = repeat,
            WorkDays = new[] { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday },
            WorkStart = start,
            WorkEnd = end
        };

        IsBusy = true;
        try
        {
            var result = await _coordinator.ApplyAsync(candidate);
            if (result.Success)
            {
                CloseRequested?.Invoke(true);
                return;
            }

            ErrorMessage = string.Join(Environment.NewLine, result.Errors);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadFrom(AppSettings settings)
    {
        IdleThresholdSeconds = settings.IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        IsAutomaticStartEnabled = settings.AutoStartTimer;
        CloseToTray = settings.CloseToTray;
        AutoStartWithWindows = settings.AutoStartWithWindows;
        LaunchInTrayOnAutoStart = settings.LaunchInTrayOnAutoStart;
        RemindersEnabled = settings.RemindersEnabled;
        ForgotTimerReminderEnabled = settings.ForgotTimerReminderEnabled;
        ForgotTimerAfterMinutes = settings.ForgotTimerAfterMinutes.ToString(CultureInfo.InvariantCulture);
        ReminderRepeatMinutes = settings.ReminderRepeatMinutes.ToString(CultureInfo.InvariantCulture);
        WorkStart = settings.WorkStart;
        WorkEnd = settings.WorkEnd;
        Monday = settings.WorkDays.ElementAtOrDefault(0);
        Tuesday = settings.WorkDays.ElementAtOrDefault(1);
        Wednesday = settings.WorkDays.ElementAtOrDefault(2);
        Thursday = settings.WorkDays.ElementAtOrDefault(3);
        Friday = settings.WorkDays.ElementAtOrDefault(4);
        Saturday = settings.WorkDays.ElementAtOrDefault(5);
        Sunday = settings.WorkDays.ElementAtOrDefault(6);
        ToggleHotkey = settings.ToggleTimerHotkey.Clone();
        StopHotkey = settings.StopTimerHotkey.Clone();
        ShowHideHotkey = settings.ShowHideHotkey.Clone();
    }
}
