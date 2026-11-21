using Microsoft.Win32;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>Autostart for the current Windows user through the Run key.</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteadyClock";
    private const string LegacyValueName = "MyTimeTracker";

    private readonly IAppLogger _logger;

    public AutostartService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return HasValue(key, ValueName) || HasValue(key, LegacyValueName);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Не удалось проверить автозапуск: {exception.Message}");
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "Не удалось открыть раздел автозапуска текущего пользователя.";
                return false;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    error = "Не удалось определить путь к исполняемому файлу.";
                    return false;
                }

                key.SetValue(
                    ValueName,
                    $"\"{executable}\" --autostart",
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool HasValue(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);
}
