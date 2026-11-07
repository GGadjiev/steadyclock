using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// X11 global hotkeys. All Xlib calls run on a single dedicated thread, so the
/// service is safe to use from the Avalonia UI thread. Wayland sessions without
/// XWayland cannot open a display and fall back to the null implementation.
/// </summary>
internal sealed class X11GlobalHotkeyService : IGlobalHotkeyService
{
    private const int ToggleId = 0x4101;
    private const int StopId = 0x4102;
    private const int ShowHideId = 0x4103;
    private const int KeyPress = 2;
    private const int GrabModeAsync = 1;
    private const uint ShiftMask = 1;
    private const uint LockMask = 2;
    private const uint ControlMask = 4;
    private const uint Mod1Mask = 8;
    private const uint Mod2Mask = 16;
    private const uint Mod4Mask = 64;

    private readonly IAppLogger _logger;
    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly XErrorHandlerDelegate _errorHandler;
    private readonly Thread _thread;
    private readonly List<HotkeyLookup> _lookup = new();
    private readonly List<GrabDefinition> _grabs = new();
    private IntPtr _display;
    private IntPtr _root;
    private bool _isAvailable;
    private volatile bool _disposed;
    private volatile bool _grabError;

    public X11GlobalHotkeyService(IAppLogger logger)
    {
        _logger = logger;
        _errorHandler = GrabErrorHandler;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "SteadyClock X11 hotkeys"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3)))
        {
            return;
        }

        _isAvailable = _display != IntPtr.Zero;
    }

    public bool IsAvailable => _isAvailable;
    public bool IsSupported => _isAvailable;

    public event EventHandler? ToggleRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ShowHideRequested;

    public HotkeyApplyResult Apply(AppSettings settings)
    {
        if (!_isAvailable || _disposed)
        {
            return new HotkeyApplyResult(true);
        }

        HotkeyApplyResult? result = null;
        var completed = new ManualResetEventSlim(false);
        _requests.Enqueue(() =>
        {
            try
            {
                result = ApplyCore(settings);
            }
            catch (Exception exception)
            {
                _logger.Error("Ошибка регистрации горячих клавиш X11.", exception);
                result = new HotkeyApplyResult(false);
            }
            finally
            {
                completed.Set();
            }
        });

        return completed.Wait(TimeSpan.FromSeconds(3)) && result is not null
            ? result
            : new HotkeyApplyResult(false);
    }

    private HotkeyApplyResult ApplyCore(AppSettings settings)
    {
        var previousLookup = _lookup.ToList();
        var previousGrabs = _grabs.ToList();
        UngrabAll();

        _grabError = false;
        var previousHandler = XSetErrorHandler(
            Marshal.GetFunctionPointerForDelegate(_errorHandler));
        try
        {
            foreach (var (id, binding) in new (int Id, HotkeyBinding? Binding)[]
                     {
                         (ToggleId, settings.ToggleTimerHotkey),
                         (StopId, settings.StopTimerHotkey),
                         (ShowHideId, settings.ShowHideHotkey)
                     })
            {
                if (binding is null || binding.IsEmpty)
                {
                    continue;
                }

                var keysymName = KeysymName(binding.VirtualKey);
                var keysym = keysymName is null ? UIntPtr.Zero : XStringToKeysym(keysymName);
                var keycode = keysym == UIntPtr.Zero ? 0 : XKeysymToKeycode(_display, keysym);
                if (keycode == 0)
                {
                    _logger.Warn(
                        $"Клавиша {binding.DisplayName} не поддерживается в X11.");
                    Restore(previousLookup, previousGrabs);
                    return new HotkeyApplyResult(false, binding.DisplayName);
                }

                var modifiers = ToModifiers(binding);
                foreach (var lockMask in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask })
                {
                    var fullModifiers = modifiers | lockMask;
                    XGrabKey(
                        _display,
                        keycode,
                        fullModifiers,
                        _root,
                        ownerEvents: false,
                        GrabModeAsync,
                        GrabModeAsync);
                    _grabs.Add(new GrabDefinition(keycode, fullModifiers));
                }

                _lookup.Add(new HotkeyLookup(id, keycode, modifiers));
            }

            XSync(_display, discard: false);
        }
        finally
        {
            XSetErrorHandler(previousHandler);
        }

        if (_grabError)
        {
            _logger.Warn("Одна из глобальных горячих клавиш уже занята в X11.");
            Restore(previousLookup, previousGrabs);
            return new HotkeyApplyResult(
                false,
                settings.ToggleTimerHotkey?.DisplayName);
        }

        return new HotkeyApplyResult(true);
    }

    private void Restore(
        List<HotkeyLookup> previousLookup,
        List<GrabDefinition> previousGrabs)
    {
        UngrabAll();
        foreach (var grab in previousGrabs)
        {
            XGrabKey(
                _display,
                grab.KeyCode,
                grab.Modifiers,
                _root,
                ownerEvents: false,
                GrabModeAsync,
                GrabModeAsync);
            _grabs.Add(grab);
        }

        _lookup.AddRange(previousLookup);
        XSync(_display, discard: false);
    }

    private void UngrabAll()
    {
        foreach (var grab in _grabs)
        {
            XUngrabKey(_display, grab.KeyCode, grab.Modifiers, _root);
        }

        _grabs.Clear();
        _lookup.Clear();
    }

    private void Run()
    {
        try
        {
            _display = XOpenDisplay(null);
            if (_display == IntPtr.Zero)
            {
                return;
            }

            _root = XDefaultRootWindow(_display);
            _ready.Set();

            while (!_disposed)
            {
                while (_requests.TryDequeue(out var request))
                {
                    request();
                }

                if (XPending(_display) > 0)
                {
                    XNextEvent(_display, out var xevent);
                    if (xevent.Type == KeyPress)
                    {
                        Dispatch(xevent);
                    }
                }
                else
                {
                    Thread.Sleep(25);
                }
            }

            UngrabAll();
            XSync(_display, discard: false);
        }
        catch (Exception exception)
        {
            _logger.Error("Ошибка потока горячих клавиш X11.", exception);
        }
        finally
        {
            _ready.Set();
            if (_display != IntPtr.Zero)
            {
                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
    }

    private void Dispatch(XEvent xevent)
    {
        var state = xevent.State & (ShiftMask | ControlMask | Mod1Mask | Mod4Mask);
        foreach (var entry in _lookup)
        {
            if (entry.KeyCode != (int)xevent.Keycode || entry.Modifiers != state)
            {
                continue;
            }

            switch (entry.Id)
            {
                case ToggleId:
                    ToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case StopId:
                    StopRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case ShowHideId:
                    ShowHideRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }

            return;
        }
    }

    private int GrabErrorHandler(IntPtr display, IntPtr errorEvent)
    {
        _grabError = true;
        return 0;
    }

    private static uint ToModifiers(HotkeyBinding binding)
    {
        var modifiers = 0u;
        if (binding.Ctrl) modifiers |= ControlMask;
        if (binding.Alt) modifiers |= Mod1Mask;
        if (binding.Shift) modifiers |= ShiftMask;
        if (binding.Win) modifiers |= Mod4Mask;
        return modifiers;
    }

    private static string? KeysymName(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"KP_{virtualKey - 0x60}";
        }

        return virtualKey switch
        {
            0x08 => "BackSpace",
            0x09 => "Tab",
            0x0D => "Return",
            0x13 => "Pause",
            0x1B => "Escape",
            0x20 => "space",
            0x21 => "Prior",
            0x22 => "Next",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "KP_Multiply",
            0x6B => "KP_Add",
            0x6D => "KP_Subtract",
            0x6E => "KP_Decimal",
            0x6F => "KP_Divide",
            0xBA => "semicolon",
            0xBB => "equal",
            0xBC => "comma",
            0xBD => "minus",
            0xBE => "period",
            0xBF => "slash",
            0xC0 => "grave",
            0xDB => "bracketleft",
            0xDC => "backslash",
            0xDD => "bracketright",
            0xDE => "apostrophe",
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _thread.Join(TimeSpan.FromSeconds(2));
        _isAvailable = false;
    }

    private readonly record struct HotkeyLookup(int Id, int KeyCode, uint Modifiers);
    private readonly record struct GrabDefinition(int KeyCode, uint Modifiers);

    private delegate int XErrorHandlerDelegate(IntPtr display, IntPtr errorEvent);

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(80)] public uint State;
        [FieldOffset(84)] public uint Keycode;
    }

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? displayName);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XDefaultRootWindow")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XStringToKeysym")]
    private static extern UIntPtr XStringToKeysym(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libX11.so.6", EntryPoint = "XKeysymToKeycode")]
    private static extern int XKeysymToKeycode(IntPtr display, UIntPtr keysym);

    [DllImport("libX11.so.6", EntryPoint = "XGrabKey")]
    private static extern int XGrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        IntPtr grabWindow,
        [MarshalAs(UnmanagedType.Bool)] bool ownerEvents,
        int pointerMode,
        int keyboardMode);

    [DllImport("libX11.so.6", EntryPoint = "XUngrabKey")]
    private static extern int XUngrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        IntPtr grabWindow);

    [DllImport("libX11.so.6", EntryPoint = "XSync")]
    private static extern int XSync(
        IntPtr display,
        [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport("libX11.so.6", EntryPoint = "XPending")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XNextEvent")]
    private static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);
}
