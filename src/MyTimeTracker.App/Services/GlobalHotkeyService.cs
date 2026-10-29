using System.Runtime.InteropServices;
using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Services;

public sealed record HotkeyApplyResult(bool Success, string? ConflictDisplayName = null);

public sealed class GlobalHotkeyService : IDisposable
{
    private const int ToggleId = 0x4101;
    private const int StopId = 0x4102;
    private const int ShowHideId = 0x4103;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HiddenMessageWindow _messageWindow;
    private readonly IAppLogger _logger;
    private readonly Dictionary<int, HotkeyBinding> _registered = new();
    private IntPtr _handle;

    public GlobalHotkeyService(HiddenMessageWindow messageWindow, IAppLogger logger)
    {
        _messageWindow = messageWindow;
        _logger = logger;
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ShowHideRequested;

    public HotkeyApplyResult Apply(AppSettings settings)
    {
        _handle = _messageWindow.Handle;
        var old = _registered.ToDictionary(pair => pair.Key, pair => pair.Value);
        UnregisterAll();

        var candidates = new (int Id, HotkeyBinding? Binding)[]
        {
            (ToggleId, settings.ToggleTimerHotkey),
            (StopId, settings.StopTimerHotkey),
            (ShowHideId, settings.ShowHideHotkey)
        };

        foreach (var (id, binding) in candidates)
        {
            if (binding is null || binding.IsEmpty)
            {
                continue;
            }

            if (!TryRegister(id, binding, out _))
            {
                UnregisterAll();
                foreach (var oldBinding in old)
                {
                    TryRegister(oldBinding.Key, oldBinding.Value, out _);
                }

                _logger.Warn("Не удалось зарегистрировать глобальную горячую клавишу.");
                return new HotkeyApplyResult(false, binding.DisplayName);
            }

            _registered[id] = binding.Clone();
        }

        return new HotkeyApplyResult(true);
    }

    private bool TryRegister(int id, HotkeyBinding binding, out int errorCode)
    {
        errorCode = 0;
        var result = RegisterHotKey(_handle, id, ToModifiers(binding), (uint)binding.VirtualKey);
        if (!result)
        {
            errorCode = Marshal.GetLastWin32Error();
        }

        return result;
    }

    private void OnHotkeyPressed(object? sender, int id)
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
        if (_handle == IntPtr.Zero)
        {
            _registered.Clear();
            return;
        }

        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(_handle, id);
        }

        _registered.Clear();
    }

    public string ConflictMessage(string displayName) =>
        Strings.Format("HotkeyConflictFormat", displayName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Dispose()
    {
        _messageWindow.HotkeyPressed -= OnHotkeyPressed;
        UnregisterAll();
    }
}
