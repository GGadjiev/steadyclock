using System.IO;

namespace MyTimeTracker.App.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        // The legacy folder name is intentional: it preserves existing user history,
        // settings and logs when the application was renamed to SteadyClock.
        Root = Path.Combine(
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

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
