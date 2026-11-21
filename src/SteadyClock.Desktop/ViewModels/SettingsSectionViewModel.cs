namespace SteadyClock.Desktop.ViewModels;

public enum SettingsSection
{
    Timer,
    TrackerMode,
    Idle,
    Tray,
    Hotkeys,
    Notifications,
    Reminders
}

public sealed record SettingsSectionViewModel(
    SettingsSection Section,
    string Title,
    string IconData)
{
    public override string ToString() => Title;
}
