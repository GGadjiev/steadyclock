using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;

namespace MyTimeTracker.App.Services;

public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    public TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // GetLastInputInfo uses a 32-bit tick counter. Unchecked unsigned subtraction
        // handles its wraparound correctly.
        var now = unchecked((uint)Environment.TickCount);
        var idleMs = unchecked(now - info.DwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
