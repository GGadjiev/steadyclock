namespace MyTimeTracker.Core.Models;

public sealed class AppSettings
{
    public const int CurrentVersion = 1;
    public const int MinIdleThresholdSeconds = 30;
    public const int MaxIdleThresholdSeconds = 3600;

    public int Version { get; set; } = CurrentVersion;

    // Timer
    public bool AutoStartTimer { get; set; }

    // Idle detection
    public int IdleThresholdSeconds { get; set; } = 120;

    // Tray / startup
    public bool CloseToTray { get; set; } = true;
    public bool AutoStartWithWindows { get; set; }
    public bool LaunchInTrayOnAutoStart { get; set; } = true;

    // Hotkeys
    public HotkeyBinding ToggleTimerHotkey { get; set; } = HotkeyBinding.DefaultToggle();
    public HotkeyBinding StopTimerHotkey { get; set; } = HotkeyBinding.DefaultStop();
    public HotkeyBinding ShowHideHotkey { get; set; } = HotkeyBinding.DefaultShowHide();

    // Reminders
    public bool RemindersEnabled { get; set; } = true;
    public bool ForgotTimerReminderEnabled { get; set; } = true;
    public int ForgotTimerAfterMinutes { get; set; } = 15;
    public int ReminderRepeatMinutes { get; set; } = 30;

    // Monday-first: Monday, Tuesday, ... Sunday.
    public bool[] WorkDays { get; set; } = { true, true, true, true, true, false, false };
    public string WorkStart { get; set; } = "09:00";
    public string WorkEnd { get; set; } = "18:00";

    public static AppSettings CreateDefault() => new();

    public AppSettings DeepClone() => new()
    {
        Version = Version,
        AutoStartTimer = AutoStartTimer,
        IdleThresholdSeconds = IdleThresholdSeconds,
        CloseToTray = CloseToTray,
        AutoStartWithWindows = AutoStartWithWindows,
        LaunchInTrayOnAutoStart = LaunchInTrayOnAutoStart,
        ToggleTimerHotkey = ToggleTimerHotkey.Clone(),
        StopTimerHotkey = StopTimerHotkey.Clone(),
        ShowHideHotkey = ShowHideHotkey.Clone(),
        RemindersEnabled = RemindersEnabled,
        ForgotTimerReminderEnabled = ForgotTimerReminderEnabled,
        ForgotTimerAfterMinutes = ForgotTimerAfterMinutes,
        ReminderRepeatMinutes = ReminderRepeatMinutes,
        WorkDays = WorkDays.ToArray(),
        WorkStart = WorkStart,
        WorkEnd = WorkEnd
    };
}
