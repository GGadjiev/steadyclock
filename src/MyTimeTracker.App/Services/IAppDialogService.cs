namespace MyTimeTracker.App.Services;

public enum ExitChoice
{
    Cancel = 0,
    StopAndExit = 1,
    MinimizeToTray = 2
}

public interface IAppDialogService
{
    Task ShowSettingsAsync();
    Task<bool> ShowSessionEditorAsync(long? sessionId);
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false);
    Task<ExitChoice> ShowExitChoiceAsync();
    void ShowError(string message);
    void ShowMessage(string title, string message);
}
