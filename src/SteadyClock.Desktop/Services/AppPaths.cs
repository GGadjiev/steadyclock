namespace SteadyClock.Desktop.Services;

/// <summary>
/// Windows application data locations. The legacy MyTimeTracker folder is kept
/// so an existing installation keeps its history after the rename.
/// </summary>
public sealed class AppPaths
{
    public AppPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("STEADYCLOCK_DATA_DIR");
        UsesDataDirectoryOverride = !string.IsNullOrWhiteSpace(overrideRoot);
        Root = UsesDataDirectoryOverride
            ? overrideRoot!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyTimeTracker");

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
}
