using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Windows global hotkeys. Registration, the message loop and unregistration all
/// run on one dedicated thread, which is the thread that owns the hotkeys.
/// </summary>
internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int ToggleId = 0x4101;
    private const int StopId = 0x4102;
    private const int ShowHideId = 0x4103;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint WmApply = 0x8000;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint PmNoRemove = 0x0000;

    private readonly IAppLogger _logger;
    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Dictionary<int, HotkeyBinding> _registered = new();
    private readonly Thread _thread;
    private uint _threadId;
    private volatile bool _disposed;

    public WindowsGlobalHotkeyService(IAppLogger logger)
    {
        _logger = logger;
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "SteadyClock hotkeys"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new InvalidOperationException("Не удалось запустить поток горячих клавиш.");
        }
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ShowHideRequested;

    public bool IsSupported => true;

    public HotkeyApplyResult Apply(AppSettings settings)
    {
        if (_disposed || _threadId == 0)
        {
            return new HotkeyApplyResult(false);
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
                _logger.Error("Ошибка регистрации глобальных горячих клавиш.", exception);
                result = new HotkeyApplyResult(false);
            }
            finally
            {
                completed.Set();
            }
        });
        PostThreadMessage(_threadId, WmApply, IntPtr.Zero, IntPtr.Zero);

        return completed.Wait(TimeSpan.FromSeconds(3)) && result is not null
            ? result
            : new HotkeyApplyResult(false);
    }

    private HotkeyApplyResult ApplyCore(AppSettings settings)
    {
        var previous = _registered.ToDictionary(pair => pair.Key, pair => pair.Value);
        UnregisterAll();

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

            if (!RegisterHotKey(IntPtr.Zero, id, ToModifiers(binding), (uint)binding.VirtualKey))
            {
                var errorCode = Marshal.GetLastWin32Error();
                _logger.Warn(
                    $"Не удалось зарегистрировать горячую клавишу {binding.DisplayName} (код {errorCode}).");
                UnregisterAll();
                foreach (var pair in previous)
                {
                    RegisterHotKey(
                        IntPtr.Zero,
                        pair.Key,
                        ToModifiers(pair.Value),
                        (uint)pair.Value.VirtualKey);
                    _registered[pair.Key] = pair.Value;
                }

                return new HotkeyApplyResult(false, binding.DisplayName);
            }

            _registered[id] = binding.Clone();
        }

        return new HotkeyApplyResult(true);
    }

    private void RunMessageLoop()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            _ready.Set();

            while (!_disposed && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Id == WmHotkey)
                {
                    DispatchHotkey(unchecked((int)message.WParam.ToUInt32()));
                    continue;
                }

                if (message.Id == WmApply)
                {
                    while (_requests.TryDequeue(out var request))
                    {
                        request();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Ошибка потока горячих клавиш.", exception);
        }
        finally
        {
            UnregisterAll();
            _ready.Set();
        }
    }

    private void DispatchHotkey(int id)
    {
        switch (id)
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
    }

    private static uint ToModifiers(HotkeyBinding binding)
    {
        var modifiers = ModNoRepeat;
        if (binding.Ctrl) modifiers |= ModControl;
        if (binding.Alt) modifiers |= ModAlt;
        if (binding.Shift) modifiers |= ModShift;
        if (binding.Win) modifiers |= ModWin;
        return modifiers;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out Message message,
        IntPtr hwnd,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(
        out Message message,
        IntPtr hwnd,
        uint filterMin,
        uint filterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
