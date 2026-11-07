using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Best-effort cross-platform idle source. Windows is fully supported. macOS uses
/// CoreGraphics. Linux uses XScreenSaver; under Wayland the X11 value may be
/// unavailable, in which case idle detection is disabled rather than guessed.
/// </summary>
public sealed class PlatformIdleTimeProvider : IIdleTimeProvider
{
    private readonly object _x11Sync = new();
    private IntPtr _x11Display;
    private IntPtr _x11Info;

    public TimeSpan GetIdleTime()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return GetWindowsIdle();
            }

            if (OperatingSystem.IsMacOS())
            {
                var seconds = CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);
                return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            }

            if (OperatingSystem.IsLinux() &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                return GetX11Idle();
            }
        }
        catch
        {
            // An unsupported desktop must not break the timer.
        }

        return TimeSpan.Zero;
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

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern double CGEventSourceSecondsSinceLastEventType(uint stateId, uint eventType);

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public IntPtr Window;
        public int State;
        public int Kind;
        public ulong TilOrSince;
        public ulong Idle;
        public ulong EventMask;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libXss.so.1")]
    private static extern IntPtr XScreenSaverAllocInfo();

    [DllImport("libXss.so.1")]
    private static extern bool XScreenSaverQueryInfo(
        IntPtr display,
        IntPtr drawable,
        IntPtr screenSaverInfo);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

    private static TimeSpan GetWindowsIdle()
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

    private TimeSpan GetX11Idle()
    {
        lock (_x11Sync)
        {
            if (_x11Display == IntPtr.Zero)
            {
                _x11Display = XOpenDisplay(null);
                if (_x11Display == IntPtr.Zero)
                {
                    return TimeSpan.Zero;
                }

                _x11Info = XScreenSaverAllocInfo();
                if (_x11Info == IntPtr.Zero)
                {
                    XCloseDisplay(_x11Display);
                    _x11Display = IntPtr.Zero;
                    return TimeSpan.Zero;
                }
            }

            if (!XScreenSaverQueryInfo(
                _x11Display,
                XDefaultRootWindow(_x11Display),
                _x11Info))
            {
                CloseX11Locked();
                return TimeSpan.Zero;
            }

            var data = Marshal.PtrToStructure<XScreenSaverInfo>(_x11Info);
            return TimeSpan.FromMilliseconds(data.Idle);
        }
    }

    private void CloseX11Locked()
    {
        if (_x11Info != IntPtr.Zero)
        {
            XFree(_x11Info);
            _x11Info = IntPtr.Zero;
        }

        if (_x11Display != IntPtr.Zero)
        {
            XCloseDisplay(_x11Display);
            _x11Display = IntPtr.Zero;
        }
    }
}
