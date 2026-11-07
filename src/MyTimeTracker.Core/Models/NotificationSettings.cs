namespace MyTimeTracker.Core.Models;

public sealed class NotificationRuleSettings
{
    public bool ShowToast { get; set; } = true;

    /// <summary>"none" disables playback; otherwise a local preset id.</summary>
    public string SoundId { get; set; } = "soft";

    public NotificationRuleSettings Clone() => new()
    {
        ShowToast = ShowToast,
        SoundId = SoundId
    };
}

public sealed class NotificationSettings
{
    public const int MinToastDurationSeconds = 4;
    public const int MaxToastDurationSeconds = 30;

    public bool Enabled { get; set; } = true;
    public int ToastDurationSeconds { get; set; } = 8;
    public NotificationRuleSettings AutoPause { get; set; } = new() { SoundId = "soft" };
    public NotificationRuleSettings Reminder { get; set; } = new() { SoundId = "bell" };
    public NotificationRuleSettings Error { get; set; } = new() { SoundId = "warning" };
    public NotificationRuleSettings ProcessStarted { get; set; } = new() { SoundId = "twotone" };
    public NotificationRuleSettings ProcessStopped { get; set; } = new() { SoundId = "click" };

    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStart { get; set; } = "22:00";
    public string QuietHoursEnd { get; set; } = "08:00";

    public NotificationSettings Clone() => new()
    {
        Enabled = Enabled,
        ToastDurationSeconds = ToastDurationSeconds,
        AutoPause = AutoPause.Clone(),
        Reminder = Reminder.Clone(),
        Error = Error.Clone(),
        ProcessStarted = ProcessStarted.Clone(),
        ProcessStopped = ProcessStopped.Clone(),
        QuietHoursEnabled = QuietHoursEnabled,
        QuietHoursStart = QuietHoursStart,
        QuietHoursEnd = QuietHoursEnd
    };
}
