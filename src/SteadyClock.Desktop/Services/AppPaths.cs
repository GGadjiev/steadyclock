namespace SteadyClock.Desktop.Services;

/// <summary>
/// Cross-platform application data locations. On Windows the legacy MyTimeTracker
/// folder is kept so an existing 1.x installation keeps its history after the rename.
/// </summary>
public sealed class AppPaths
{
    public AppPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("STEADYCLOCK_DATA_DIR");
        UsesDataDirectoryOverride = !string.IsNullOrWhiteSpace(overrideRoot);
        Root = UsesDataDirectoryOverride
            ? overrideRoot!
            : GetDefaultRoot();

        DatabasePath = Path.Combine(Root, "data.db");
        SettingsPath = Path.Combine(Root, "settings.json");
        LogsDirectory = Path.Combine(Root, "logs");
    }

    public string Root { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string LogsDirectory { get; }
    public bool UsesDataDirectoryOverride { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static string GetDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyTimeTracker");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "SteadyClock");
        }

        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataRoot = string.IsNullOrWhiteSpace(xdgData)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share")
            : xdgData;
        return Path.Combine(dataRoot, "steadyclock");
    }
}
