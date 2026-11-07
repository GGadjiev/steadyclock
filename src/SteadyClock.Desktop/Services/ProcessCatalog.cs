using System.Diagnostics;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

public sealed record RunningProcessInfo(
    string Name,
    string Path,
    string DisplayName);

public interface IProcessCatalog
{
    Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsRunningAsync(
        ProcessTriggerSettings target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Local process discovery. Windows/macOS use System.Diagnostics; Linux reads
/// /proc so that regular native packages work without extra permissions.
/// Flatpak/Snap sandboxes may hide host processes.
/// </summary>
public sealed class ProcessCatalog : IProcessCatalog
{
    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var processes = OperatingSystem.IsLinux()
                ? GetLinuxProcesses()
                : GetDotNetProcesses();

            return (IReadOnlyList<RunningProcessInfo>)processes
                .Where(process => !string.IsNullOrWhiteSpace(process.Name))
                .GroupBy(
                    process => string.IsNullOrWhiteSpace(process.Path)
                        ? process.Name
                        : process.Path,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(process => process.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public Task<bool> IsRunningAsync(
        ProcessTriggerSettings target,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!target.HasTarget)
            {
                return false;
            }

            if (!OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(target.Name))
            {
                // Fast path: ask Windows/macOS only for processes with the saved
                // name instead of inspecting every process twice per second.
                foreach (var process in Process.GetProcessesByName(target.Name))
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(target.Path) ||
                            (!string.IsNullOrWhiteSpace(path) &&
                             PathsEqual(target.Path, path)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // MainModule access can be denied; try the full scan below.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                if (string.IsNullOrWhiteSpace(target.Path))
                {
                    return false;
                }
            }

            foreach (var process in OperatingSystem.IsLinux()
                         ? GetLinuxProcesses()
                         : GetDotNetProcesses())
            {
                if (!string.IsNullOrWhiteSpace(target.Path) &&
                    !string.IsNullOrWhiteSpace(process.Path) &&
                    PathsEqual(target.Path, process.Path))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(target.Name) &&
                    string.Equals(target.Name, process.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }, cancellationToken);
    }

    private static List<RunningProcessInfo> GetDotNetProcesses()
    {
        var result = new List<RunningProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var path = string.Empty;
                try
                {
                    path = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // MainModule can be denied for system or other-user processes.
                    // The process name is still a usable fallback.
                }

                result.Add(new RunningProcessInfo(
                    name,
                    path,
                    BuildDisplayName(name, path)));
            }
            catch
            {
                // The process exited while it was being inspected.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static List<RunningProcessInfo> GetLinuxProcesses()
    {
        var result = new List<RunningProcessInfo>();
        if (!Directory.Exists("/proc"))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                var path = new FileInfo(Path.Combine(directory, "exe")).LinkTarget ?? string.Empty;

                var nameFile = Path.Combine(directory, "comm");
                var name = File.Exists(nameFile)
                    ? File.ReadAllText(nameFile).Trim()
                    : Path.GetFileName(path);
                result.Add(new RunningProcessInfo(
                    name,
                    path,
                    BuildDisplayName(name, path)));
            }
            catch
            {
                // Process exited or is not readable.
            }
        }

        return result;
    }

    private static string BuildDisplayName(string name, string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? name
            : $"{name} — {path}";
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.GetFullPath(left);
            var normalizedRight = Path.GetFullPath(right);
            return string.Equals(
                normalizedLeft,
                normalizedRight,
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
