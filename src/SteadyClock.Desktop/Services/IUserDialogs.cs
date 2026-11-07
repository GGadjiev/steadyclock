namespace SteadyClock.Desktop.Services;

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
}
