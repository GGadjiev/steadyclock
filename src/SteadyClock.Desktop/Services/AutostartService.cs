using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Login autostart for the current user. Windows uses the Run key, Linux uses an
/// XDG autostart entry and macOS uses a LaunchAgent plist.
/// </summary>
public sealed class AutostartService
{
    private const string WindowsRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsValueName = "SteadyClock";
    private const string WindowsLegacyValueName = "MyTimeTracker";
    private const string LinuxDesktopFileName = "steadyclock.desktop";
    private const string MacLaunchAgentFileName = "com.steadyclock.app.plist";

    private readonly IAppLogger _logger;

    public AutostartService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(WindowsRunKeyPath, writable: false);
                return HasValue(key, WindowsValueName) || HasValue(key, WindowsLegacyValueName);
            }

            if (OperatingSystem.IsMacOS())
            {
                return File.Exists(GetMacLaunchAgentPath());
            }

            return File.Exists(GetLinuxDesktopPath());
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
            if (OperatingSystem.IsWindows())
            {
                return TrySetWindows(enabled, out error);
            }

            if (OperatingSystem.IsMacOS())
            {
                return TrySetMac(enabled, out error);
            }

            return TrySetLinux(enabled, out error);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySetWindows(bool enabled, out string? error)
    {
        error = null;
        using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKeyPath, writable: true);
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
                WindowsValueName,
                $"\"{executable}\" --autostart",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(WindowsValueName, throwOnMissingValue: false);
        }

        // Remove the entry written by the pre-rename MyTimeTracker build.
        key.DeleteValue(WindowsLegacyValueName, throwOnMissingValue: false);
        return true;
    }

    private static bool TrySetLinux(bool enabled, out string? error)
    {
        error = null;
        var path = GetLinuxDesktopPath();
        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "Не удалось определить путь к исполняемому файлу.";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Name=SteadyClock")
            .AppendLine($"Exec=\"{EscapeDesktopArgument(executable)}\" --autostart")
            .AppendLine("Terminal=false")
            .AppendLine("X-GNOME-Autostart-enabled=true")
            .ToString();
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static bool TrySetMac(bool enabled, out string? error)
    {
        error = null;
        var path = GetMacLaunchAgentPath();
        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "Не удалось определить путь к исполняемому файлу.";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var escapedPath = System.Security.SecurityElement.Escape(executable);
        var content =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
            "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "  <key>Label</key>\n" +
            "  <string>com.steadyclock.app</string>\n" +
            "  <key>ProgramArguments</key>\n" +
            "  <array>\n" +
            $"    <string>{escapedPath}</string>\n" +
            "    <string>--autostart</string>\n" +
            "  </array>\n" +
            "  <key>RunAtLoad</key>\n" +
            "  <true/>\n" +
            "</dict>\n" +
            "</plist>\n";
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string GetLinuxDesktopPath()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(configRoot, "autostart", LinuxDesktopFileName);
    }

    private static string GetMacLaunchAgentPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            MacLaunchAgentFileName);

    private static string EscapeDesktopArgument(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    [SupportedOSPlatform("windows")]
    private static bool HasValue(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);
}
