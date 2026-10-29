using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MyTimeTracker.App.Services;

/// <summary>
/// Hidden top-level window used for global hotkeys, Windows session lock and
/// power broadcast messages. It never becomes visible.
/// </summary>
public sealed class HiddenMessageWindow : IDisposable
{
    private const int WmTimeChange = 0x001E;
    private const int WmHotkey = 0x0312;
    private const int WmPowerBroadcast = 0x0218;
    private const int WmWtsSessionChange = 0x02B1;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeAutomatic = 0x0012;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int NotifyForThisSession = 0;

    private readonly HwndSource _source;
    private bool _wtsRegistered;
    private bool _disposed;

    public HiddenMessageWindow()
    {
        var parameters = new HwndSourceParameters("SteadyClockHiddenWindow")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        try
        {
            _wtsRegistered = WTSRegisterSessionNotification(Handle, NotifyForThisSession);
        }
        catch
        {
            _wtsRegistered = false;
        }
    }

    public IntPtr Handle => _source.Handle;

    public event EventHandler<int>? HotkeyPressed;
    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;
    public event EventHandler? SystemSuspending;
    public event EventHandler? SystemResumed;
    public event EventHandler? SystemTimeChanged;

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        switch (msg)
        {
            case WmTimeChange:
                SystemTimeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case WmHotkey:
                HotkeyPressed?.Invoke(this, wParam.ToInt32());
                handled = true;
                break;
            case WmWtsSessionChange:
                if (wParam.ToInt32() == WtsSessionLock)
                {
                    SessionLocked?.Invoke(this, EventArgs.Empty);
                }
                else if (wParam.ToInt32() == WtsSessionUnlock)
                {
                    SessionUnlocked?.Invoke(this, EventArgs.Empty);
                }

                break;
            case WmPowerBroadcast:
                switch (wParam.ToInt32())
                {
                    case PbtApmSuspend:
                        SystemSuspending?.Invoke(this, EventArgs.Empty);
                        break;
                    case PbtApmResumeAutomatic:
                    case PbtApmResumeSuspend:
                        SystemResumed?.Invoke(this, EventArgs.Empty);
                        break;
                }

                break;
        }

        return IntPtr.Zero;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_wtsRegistered)
        {
            try
            {
                WTSUnRegisterSessionNotification(Handle);
            }
            catch
            {
                // Ignore during shutdown.
            }
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
