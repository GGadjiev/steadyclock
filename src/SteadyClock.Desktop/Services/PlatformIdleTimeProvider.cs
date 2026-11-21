using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>Windows idle source based on the last user input event.</summary>
public sealed class PlatformIdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan GetIdleTime()
    {
        try
        {
            var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            var now = unchecked((uint)Environment.TickCount);
            var idleMs = unchecked(now - info.DwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);
}
