namespace SteadyClock.Desktop.Services;

public enum ExitChoice
{
    Cancel = 0,
    StopAndExit = 1,
    MinimizeToTray = 2
}

public interface IUserDialogs
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool destructive = false);

    Task<bool> ShowSessionEditorAsync(long? sessionId);

    Task ShowSettingsAsync();

    Task<ExitChoice> ShowExitChoiceAsync();
}
