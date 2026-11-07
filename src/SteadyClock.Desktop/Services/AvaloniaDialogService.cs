using Avalonia.Controls;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.ViewModels;
using SteadyClock.Desktop.Views;

namespace SteadyClock.Desktop.Services;

public sealed class AvaloniaDialogService : IUserDialogs
{
    private readonly TimeTrackingController _controller;
    private readonly IAppLogger _logger;
    private readonly Func<Window?> _ownerProvider;

    public AvaloniaDialogService(
        TimeTrackingController controller,
        IAppLogger logger,
        Func<Window?> ownerProvider)
    {
        _controller = controller;
        _logger = logger;
        _ownerProvider = ownerProvider;
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool destructive = false)
    {
        var dialog = new ConfirmWindow(
            title,
            message,
            confirmText,
            cancelText,
            destructive,
            showCancel: true);
        var owner = _ownerProvider();
        return owner is { IsVisible: true }
            ? await dialog.ShowDialog<bool>(owner)
            : false;
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
                    _logger.Warn("Запись для редактирования не найдена.");
                    return false;
                }

                if (existing.IsActive)
                {
                    return false;
                }
            }

            var viewModel = new SessionEditViewModel(
                _controller,
                _controller.LocalTimeZone,
                existing,
                _controller.Engine.UtcNow);
            var window = new SessionEditWindow(viewModel);
            var owner = _ownerProvider();
            if (owner is not { IsVisible: true })
            {
                return false;
            }

            return await window.ShowDialog<bool>(owner);
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось открыть редактор записи.", exception);
            return false;
        }
    }
}
