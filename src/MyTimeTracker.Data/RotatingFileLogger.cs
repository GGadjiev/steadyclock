using System.Globalization;
using System.Text;
using MyTimeTracker.Core.Abstractions;

namespace MyTimeTracker.Data;

/// <summary>
/// Small local logger. It never logs comments, session text or user names on purpose.
/// Rotation: up to five 1 MiB files per day.
/// </summary>
public sealed class RotatingFileLogger : IAppLogger, IDisposable
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 5;
    private readonly string _directory;
    private readonly object _sync = new();
    private readonly string _sessionSuffix = Guid.NewGuid().ToString("N")[..6];

    public RotatingFileLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        PruneOldLogs();
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text);
    }

    private void Write(string level, string message)
    {
        try
        {
            lock (_sync)
            {
                var path = Path.Combine(
                    _directory,
                    $"app-{DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{_sessionSuffix}.log");
                RotateIfNeeded(path);
                var line = $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never bring the application down.
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var files = Directory.GetFiles(_directory, "app-*.log*")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            for (var index = 0; index < files.Count; index++)
            {
                if (index >= 20 || files[index].LastWriteTimeUtc < cutoff)
                {
                    files[index].Delete();
                }
            }
        }
        catch
        {
            // Retention cleanup is best effort.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes)
        {
            return;
        }

        for (var index = MaxFiles - 1; index >= 1; index--)
        {
            var source = path + "." + index.ToString(CultureInfo.InvariantCulture);
            var destination = path + "." + (index + 1).ToString(CultureInfo.InvariantCulture);
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        File.Move(path, path + ".1", overwrite: true);
    }

    public void Dispose()
    {
        // Files are opened per write; nothing to dispose.
    }
}
