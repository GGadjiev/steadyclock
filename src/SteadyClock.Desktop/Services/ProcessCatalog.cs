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

/// <summary>Windows process discovery for the application-triggered mode.</summary>
public sealed class ProcessCatalog : IProcessCatalog
{
    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var processes = GetWindowsProcesses();
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

            if (!string.IsNullOrWhiteSpace(target.Name))
            {
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
                        // Fall through to the full scan when MainModule is denied.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return false;
            }

            foreach (var process in GetWindowsProcesses())
            {
                if (!string.IsNullOrWhiteSpace(process.Path) &&
                    PathsEqual(target.Path, process.Path))
                {
                    return true;
                }
            }

            return false;
        }, cancellationToken);
    }

    private static List<RunningProcessInfo> GetWindowsProcesses()
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
                }

                result.Add(new RunningProcessInfo(
                    name,
                    path,
                    string.IsNullOrWhiteSpace(path) ? name : $"{name} — {path}"));
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

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
