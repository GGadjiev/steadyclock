using System.Windows;
using MyTimeTracker.App.Resources;
using MyTimeTracker.App.ViewModels;
using MyTimeTracker.App.Views;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Services;

public sealed class DialogService : IAppDialogService
{
    private readonly TimeTrackingController _controller;
    private readonly SettingsCoordinator _settings;
    private readonly Func<Window?> _ownerProvider;

    public DialogService(
        TimeTrackingController controller,
        SettingsCoordinator settings,
        Func<Window?> ownerProvider)
    {
        _controller = controller;
        _settings = settings;
        _ownerProvider = ownerProvider;
    }

    public Task ShowSettingsAsync()
    {
        var viewModel = new SettingsViewModel(_settings);
        var window = new SettingsWindow(viewModel);
        SetOwner(window);
        window.ShowDialog();
        return Task.CompletedTask;
    }

    public async Task<bool> ShowSessionEditorAsync(long? sessionId)
    {
        try
        {
            TimeSession? existing = null;
            if (sessionId.HasValue)
            {
                existing = await _controller.GetSessionAsync(sessionId.Value);
                if (existing is null)
                {
                    ShowError(Strings.Get("UnknownError"));
                    return false;
                }

                if (existing.IsActive)
                {
                    ShowError(Strings.Get("ActiveEntryCannotEdit"));
                    return false;
                }
            }

            var selection = new TimeSelection(existing, _controller.Engine.UtcNow);
            var viewModel = new SessionEditViewModel(
                _controller,
                selection,
                _controller.LocalTimeZone);
            var window = new SessionEditWindow(viewModel);
            SetOwner(window);
            return window.ShowDialog() == true;
        }
        catch (Exception)
        {
            ShowError(Strings.Get("UnknownError"));
            return false;
        }
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        bool destructive = false)
    {
        var dialog = new ConfirmDialog(
            title,
            message,
            confirmText,
            Strings.Get("CancelButton"),
            destructive);
        SetOwner(dialog);
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task<ExitChoice> ShowExitChoiceAsync()
    {
        var dialog = new ExitChoiceDialog();
        SetOwner(dialog);
        var result = dialog.ShowDialog() == true ? dialog.Choice : ExitChoice.Cancel;
        return Task.FromResult(result);
    }

    public void ShowError(string message)
    {
        var owner = _ownerProvider();
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, message, Strings.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, Strings.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowMessage(string title, string message)
    {
        var owner = _ownerProvider();
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetOwner(Window window)
    {
        var owner = _ownerProvider();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
