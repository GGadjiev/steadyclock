using System.Text;
using System.Text.Json;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Data;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string settingsPath, IAppLogger logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(LoadCore, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => SaveCore(settings), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AppSettings LoadCore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        if (!File.Exists(_settingsPath))
        {
            return AppSettings.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ??
                           AppSettings.CreateDefault();
            Normalize(settings);
            return settings;
        }
        catch (Exception exception)
        {
            _logger.Error("Не удалось прочитать файл настроек. Будут использованы значения по умолчанию.", exception);
            TryBackupCorruptedSettings();
            return AppSettings.CreateDefault();
        }
    }

    private void SaveCore(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(_settingsPath))
        {
            File.Replace(temporaryPath, _settingsPath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, _settingsPath);
        }
    }

    private void TryBackupCorruptedSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var backup = _settingsPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
            File.Move(_settingsPath, backup);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Не удалось сохранить резервную копию повреждённых настроек: {exception.Message}");
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.ToggleTimerHotkey ??= HotkeyBinding.DefaultToggle();
        settings.StopTimerHotkey ??= HotkeyBinding.DefaultStop();
        settings.ShowHideHotkey ??= HotkeyBinding.DefaultShowHide();
        if (settings.WorkDays is not { Length: 7 })
        {
            settings.WorkDays = AppSettings.CreateDefault().WorkDays;
        }

        if (string.IsNullOrWhiteSpace(settings.WorkStart))
        {
            settings.WorkStart = "09:00";
        }

        if (string.IsNullOrWhiteSpace(settings.WorkEnd))
        {
            settings.WorkEnd = "18:00";
        }

        settings.Version = AppSettings.CurrentVersion;
    }
}
