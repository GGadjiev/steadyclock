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
    private readonly IProcessCatalog _processCatalog;
    private readonly AutostartService _autostart;
    private readonly IGlobalHotkeyService _hotkeys;
    private bool _isRefreshingProcesses;

    [ObservableProperty]
    private bool _autoStartTimer;

    [ObservableProperty]
    private bool _autoStartWithWindows;

    [ObservableProperty]
    private bool _launchInTrayOnAutoStart;

    [ObservableProperty]
    private HotkeyBinding _toggleHotkey;

    [ObservableProperty]
    private HotkeyBinding _stopHotkey;

    [ObservableProperty]
    private HotkeyBinding _showHideHotkey;

    [ObservableProperty]
    private bool _useProcessMode;

    [ObservableProperty]
    private ProcessOptionViewModel? _selectedProcess;

    [ObservableProperty]
    private bool _stopWhenClosed;

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
        SoundService sounds,
        IProcessCatalog processCatalog,
        AutostartService autostart,
        IGlobalHotkeyService hotkeys)
    {
        _original = settings.DeepClone();
        _controller = controller;
        _settingsStore = settingsStore;
        _sounds = sounds;
        _processCatalog = processCatalog;
        _autostart = autostart;
        _hotkeys = hotkeys;

        AutoStartTimer = settings.AutoStartTimer;
        AutoStartWithWindows = settings.AutoStartWithWindows;
        LaunchInTrayOnAutoStart = settings.LaunchInTrayOnAutoStart;
        ToggleHotkey = settings.ToggleTimerHotkey.Clone();
        StopHotkey = settings.StopTimerHotkey.Clone();
        ShowHideHotkey = settings.ShowHideHotkey.Clone();
        UseProcessMode = settings.TrackerMode == TrackingMode.Process;
        StopWhenClosed = settings.ProcessTrigger?.StopWhenClosed ?? true;
        ProcessOptions = new ObservableCollection<ProcessOptionViewModel>();
        if (settings.ProcessTrigger is { HasTarget: true } savedTrigger)
        {
            ProcessOptions.Add(new ProcessOptionViewModel(
                savedTrigger.Name,
                savedTrigger.Path,
                string.IsNullOrWhiteSpace(savedTrigger.DisplayName)
                    ? savedTrigger.Name
                    : savedTrigger.DisplayName,
                IsRunning: false));
            SelectedProcess = ProcessOptions[0];
        }
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
                sound => _sounds.Play(sound.Id)),
            new(
                NotificationEventKind.ProcessStarted,
                Strings.Get("NotificationEventProcessStarted"),
                settings.Notifications.ProcessStarted,
                sound => _sounds.Play(sound.Id)),
            new(
                NotificationEventKind.ProcessStopped,
                Strings.Get("NotificationEventProcessStopped"),
                settings.Notifications.ProcessStopped,
                sound => _sounds.Play(sound.Id))
        };

        _ = RefreshProcessesAsync();
    }

    public string Title => Strings.Get("SettingsTitle");
    public string SaveButtonText => Strings.Get("SaveButton");
    public string CancelButtonText => Strings.Get("CancelButton");
    public ObservableCollection<NotificationRuleViewModel> Rules { get; }
    public ObservableCollection<ProcessOptionViewModel> ProcessOptions { get; }
    public bool HotkeysSupported => _hotkeys.IsSupported;
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
        settings.AutoStartWithWindows = AutoStartWithWindows;
        settings.LaunchInTrayOnAutoStart = LaunchInTrayOnAutoStart;
        settings.ToggleTimerHotkey = ToggleHotkey.Clone();
        settings.StopTimerHotkey = StopHotkey.Clone();
        settings.ShowHideHotkey = ShowHideHotkey.Clone();
        settings.TrackerMode = UseProcessMode ? TrackingMode.Process : TrackingMode.Normal;
        if (SelectedProcess is not null)
        {
            settings.ProcessTrigger = new ProcessTriggerSettings
            {
                Name = SelectedProcess.Name,
                Path = SelectedProcess.Path,
                DisplayName = SelectedProcess.DisplayName,
                StopWhenClosed = StopWhenClosed
            };
        }
        else if (UseProcessMode)
        {
            ErrorMessage = Strings.Get("SelectProcessError");
            return;
        }

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
                NotificationEventKind.ProcessStarted => settings.Notifications.ProcessStarted,
                NotificationEventKind.ProcessStopped => settings.Notifications.ProcessStopped,
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

        var hotkeyResult = _hotkeys.Apply(settings);
        if (!hotkeyResult.Success)
        {
            ErrorMessage = Strings.Format(
                "HotkeyConflictFormat",
                hotkeyResult.ConflictDisplayName ?? string.Empty);
            return;
        }

        var autostartChanged = settings.AutoStartWithWindows != _original.AutoStartWithWindows;
        if (autostartChanged &&
            !_autostart.TrySetEnabled(settings.AutoStartWithWindows, out _))
        {
            _hotkeys.Apply(_original);
            ErrorMessage = Strings.Get("AutostartFailed");
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
            _hotkeys.Apply(_original);
            if (autostartChanged)
            {
                _autostart.TrySetEnabled(_original.AutoStartWithWindows, out _);
            }

            ErrorMessage = Strings.Get("SettingsSaveFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        _isRefreshingProcesses = true;
        try
        {
            var previous = SelectedProcess;
            IReadOnlyList<RunningProcessInfo> running;
            try
            {
                running = await _processCatalog.GetRunningProcessesAsync();
            }
            catch
            {
                running = Array.Empty<RunningProcessInfo>();
            }

            var options = running
                .Select(process => new ProcessOptionViewModel(
                    process.Name,
                    process.Path,
                    process.DisplayName,
                    IsRunning: true))
                .ToList();

            var selected = previous;
            if (previous is not null)
            {
                var match = options.FirstOrDefault(option => TargetsEqual(option, previous));
                if (match is null)
                {
                    selected = previous with { IsRunning = false };
                    options.Insert(0, selected);
                }
                else
                {
                    selected = match;
                }
            }

            ProcessOptions.Clear();
            foreach (var option in options)
            {
                ProcessOptions.Add(option);
            }

            SelectedProcess = selected;
        }
        finally
        {
            _isRefreshingProcesses = false;
        }
    }

    private static bool TargetsEqual(
        ProcessOptionViewModel left,
        ProcessOptionViewModel right)
    {
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!string.IsNullOrWhiteSpace(left.Path) &&
            !string.IsNullOrWhiteSpace(right.Path) &&
            string.Equals(left.Path, right.Path, comparison))
        {
            return true;
        }

        return string.Equals(left.Name, right.Name, comparison);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
