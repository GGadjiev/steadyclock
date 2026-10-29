using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Services;

public sealed record SettingsApplyResult(
    bool Success,
    IReadOnlyList<string> Errors,
    bool HotkeysApplied = false);

public sealed class SettingsCoordinator
{
    private readonly ISettingsStore _store;
    private readonly TimeTrackingController _controller;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly AutostartService _autostart;
    private readonly IAppLogger _logger;

    public SettingsCoordinator(
        ISettingsStore store,
        TimeTrackingController controller,
        GlobalHotkeyService hotkeys,
        AutostartService autostart,
        IAppLogger logger)
    {
        _store = store;
        _controller = controller;
        _hotkeys = hotkeys;
        _autostart = autostart;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();
    public HotkeyApplyResult StartupHotkeyResult { get; private set; } = new(true);
    public event EventHandler? SettingsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Current = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);
        _controller.UpdateSettings(Current);
        StartupHotkeyResult = _hotkeys.Apply(Current);
        if (!StartupHotkeyResult.Success)
        {
            _logger.Warn(
                $"Горячие клавиши не зарегистрированы: {StartupHotkeyResult.ConflictDisplayName}.");
        }

        try
        {
            if (Current.AutoStartWithWindows)
            {
                // Re-apply the command so a portable copy that has been moved still
                // starts from its current location.
                if (!_autostart.TrySetEnabled(true, out var error))
                {
                    _logger.Warn($"Не удалось синхронизировать автозапуск: {error}");
                }
            }
            else if (_autostart.IsEnabled())
            {
                if (!_autostart.TrySetEnabled(false, out var error))
                {
                    _logger.Warn($"Не удалось отключить автозапуск: {error}");
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось проверить автозапуск.", exception);
        }
    }

    public async Task<SettingsApplyResult> ApplyAsync(
        AppSettings candidate,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = SettingsValidator.Validate(candidate);
        if (validationErrors.Count > 0)
        {
            return new SettingsApplyResult(false, validationErrors);
        }

        var oldSettings = Current.DeepClone();
        var oldAutostart = oldSettings.AutoStartWithWindows;

        var hotkeyResult = _hotkeys.Apply(candidate);
        if (!hotkeyResult.Success)
        {
            var display = hotkeyResult.ConflictDisplayName ?? string.Empty;
            return new SettingsApplyResult(
                false,
                new[] { Strings.Format("HotkeyConflictFormat", display) });
        }

        var autostartChanged = candidate.AutoStartWithWindows != oldAutostart;
        if (autostartChanged && !_autostart.TrySetEnabled(candidate.AutoStartWithWindows, out var error))
        {
            _hotkeys.Apply(oldSettings);
            _logger.Warn($"Не удалось изменить автозапуск: {error}");
            return new SettingsApplyResult(false, new[] { Strings.Get("AutostartFailed") });
        }

        try
        {
            await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось сохранить настройки.", exception);
            _hotkeys.Apply(oldSettings);
            if (autostartChanged)
            {
                _autostart.TrySetEnabled(oldAutostart, out _);
            }

            return new SettingsApplyResult(false, new[] { Strings.Get("UnknownError") });
        }

        Current = candidate.DeepClone();
        _controller.UpdateSettings(Current);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return new SettingsApplyResult(true, Array.Empty<string>(), HotkeysApplied: true);
    }
}
