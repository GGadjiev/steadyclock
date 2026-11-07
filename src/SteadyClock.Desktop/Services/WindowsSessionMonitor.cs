using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Hidden Windows message window for session lock, power suspend/resume and
/// system time changes. Keeping these notifications separate from the hotkey
/// thread makes each subsystem easier to reason about.
/// </summary>
internal sealed class WindowsSessionMonitor : ISessionMonitor
{
    private const uint WmTimeChange = 0x001E;
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint WmApp = 0x8000;
    private const uint WmShutdown = WmApp + 11;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeAutomatic = 0x0012;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int NotifyForThisSession = 0;

    private readonly IAppLogger _logger;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProcDelegate _wndProc;
    private readonly Thread _thread;
    private IntPtr _handle;
    private bool _wtsRegistered;
    private volatile bool _disposed;

    public WindowsSessionMonitor(IAppLogger logger)
    {
        _logger = logger;
        _wndProc = WindowProc;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SteadyClock session monitor"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3)))
        {
            _logger.Warn("Не удалось создать окно системных событий.");
        }
    }

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;
    public event EventHandler? SystemSuspending;
    public event EventHandler? SystemResumed;
    public event EventHandler? SystemTimeChanged;

    private void Run()
    {
        try
        {
            var className = $"SteadyClockSession_{Guid.NewGuid():N}";
            var instance = GetModuleHandle(null);
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WindowProc = _wndProc,
                Instance = instance,
                ClassName = className
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                _logger.Warn("Не удалось зарегистрировать класс окна системных событий.");
                return;
            }

            _handle = CreateWindowEx(
                0,
                className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (_handle == IntPtr.Zero)
            {
                _logger.Warn("Не удалось создать окно системных событий.");
                return;
            }

            _wtsRegistered = WTSRegisterSessionNotification(_handle, NotifyForThisSession);
            _ready.Set();
            while (!_disposed && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Ошибка монитора системных событий.", exception);
        }
        finally
        {
            if (_wtsRegistered && _handle != IntPtr.Zero)
            {
                WTSUnRegisterSessionNotification(_handle);
                _wtsRegistered = false;
            }

            if (_handle != IntPtr.Zero)
            {
                DestroyWindow(_handle);
                _handle = IntPtr.Zero;
            }

            _ready.Set();
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmTimeChange:
                SystemTimeChanged?.Invoke(this, EventArgs.Empty);
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
            case WmShutdown:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            PostMessage(_handle, WmShutdown, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private delegate IntPtr WndProcDelegate(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(
        out Message message,
        IntPtr hwnd,
        uint filterMin,
        uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
