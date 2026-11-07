using System.Runtime.InteropServices;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// macOS global hotkeys through the Carbon event manager. Carbon is deprecated
/// but remains the only stable system API for process-wide hotkeys. Registration
/// failures are reported to the settings UI instead of crashing startup.
/// </summary>
internal sealed class MacGlobalHotkeyService : IGlobalHotkeyService
{
    private const int ToggleId = 1;
    private const int StopId = 2;
    private const int ShowHideId = 3;
    private const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint EventHotKeyPressed = 5;
    private const uint EventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'
    private const uint Signature = 0x53434C4B; // 'SCLK'
    private const uint CmdKey = 0x0100;
    private const uint ShiftKey = 0x0200;
    private const uint OptionKey = 0x0800;
    private const uint ControlKey = 0x1000;

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<int, RegisteredHotkey> _registered = new();
    private readonly EventHandlerDelegate _handler;
    private IntPtr _handlerRef;
    private bool _disposed;

    public MacGlobalHotkeyService(IAppLogger logger)
    {
        _logger = logger;
        _handler = OnCarbonEvent;
        var eventType = new EventTypeSpec
        {
            EventClass = EventClassKeyboard,
            EventKind = EventHotKeyPressed
        };
        var status = InstallEventHandler(
            GetApplicationEventTarget(),
            _handler,
            1,
            new[] { eventType },
            IntPtr.Zero,
            out _handlerRef);
        IsSupported = status == 0 && _handlerRef != IntPtr.Zero;
        if (!IsSupported)
        {
            _logger.Warn($"Carbon event handler недоступен: OSStatus {status}.");
        }
    }

    public bool IsSupported { get; }

    public event EventHandler? ToggleRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ShowHideRequested;

    public HotkeyApplyResult Apply(AppSettings settings)
    {
        if (!IsSupported || _disposed)
        {
            return new HotkeyApplyResult(true);
        }

        lock (_sync)
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

                var keyCode = ToMacKeyCode(binding.VirtualKey);
                if (keyCode < 0)
                {
                    Restore(previous);
                    return new HotkeyApplyResult(false, binding.DisplayName);
                }

                var hotkeyId = new EventHotKeyId
                {
                    Signature = Signature,
                    Id = (uint)id
                };
                var status = RegisterEventHotKey(
                    (uint)keyCode,
                    ToModifiers(binding),
                    hotkeyId,
                    GetApplicationEventTarget(),
                    0,
                    out var hotkeyRef);
                if (status != 0)
                {
                    _logger.Warn(
                        $"Не удалось зарегистрировать {binding.DisplayName} на macOS: OSStatus {status}.");
                    Restore(previous);
                    return new HotkeyApplyResult(false, binding.DisplayName);
                }

                _registered[id] = new RegisteredHotkey(binding.Clone(), hotkeyRef);
            }

            return new HotkeyApplyResult(true);
        }
    }

    private void Restore(Dictionary<int, RegisteredHotkey> previous)
    {
        UnregisterAll();
        if (!IsSupported)
        {
            return;
        }

        foreach (var pair in previous)
        {
            var binding = pair.Value.Binding;
            if (binding.IsEmpty)
            {
                continue;
            }

            var keyCode = ToMacKeyCode(binding.VirtualKey);
            if (keyCode < 0)
            {
                continue;
            }

            var hotkeyId = new EventHotKeyId
            {
                Signature = Signature,
                Id = (uint)pair.Key
            };
            if (RegisterEventHotKey(
                (uint)keyCode,
                ToModifiers(binding),
                hotkeyId,
                GetApplicationEventTarget(),
                0,
                out var hotkeyRef) == 0)
            {
                _registered[pair.Key] = new RegisteredHotkey(binding, hotkeyRef);
            }
        }
    }

    private int OnCarbonEvent(IntPtr callRef, IntPtr theEvent, IntPtr userData)
    {
        try
        {
            var hotkeyId = new EventHotKeyId();
            var size = (uint)Marshal.SizeOf<EventHotKeyId>();
            if (GetEventParameter(
                theEvent,
                EventParamDirectObject,
                TypeEventHotKeyId,
                IntPtr.Zero,
                size,
                IntPtr.Zero,
                ref hotkeyId) != 0)
            {
                return 0;
            }

            switch (hotkeyId.Id)
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
        catch
        {
            // Native callbacks must never propagate managed exceptions.
        }

        return 0;
    }

    private static uint ToModifiers(HotkeyBinding binding)
    {
        var modifiers = 0u;
        if (binding.Ctrl) modifiers |= ControlKey;
        if (binding.Alt) modifiers |= OptionKey;
        if (binding.Shift) modifiers |= ShiftKey;
        if (binding.Win) modifiers |= CmdKey;
        return modifiers;
    }

    private static int ToMacKeyCode(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return virtualKey switch
            {
                0x41 => 0x00, // A
                0x53 => 0x01, // S
                0x44 => 0x02, // D
                0x46 => 0x03, // F
                0x48 => 0x04, // H
                0x47 => 0x05, // G
                0x5A => 0x06, // Z
                0x58 => 0x07, // X
                0x43 => 0x08, // C
                0x56 => 0x09, // V
                0x42 => 0x0B, // B
                0x51 => 0x0C, // Q
                0x57 => 0x0D, // W
                0x45 => 0x0E, // E
                0x52 => 0x0F, // R
                0x59 => 0x10, // Y
                0x54 => 0x11, // T
                0x4F => 0x1F, // O
                0x55 => 0x20, // U
                0x49 => 0x22, // I
                0x50 => 0x23, // P
                0x4C => 0x25, // L
                0x4A => 0x26, // J
                0x4B => 0x28, // K
                0x4E => 0x2D, // N
                0x4D => 0x2E, // M
                _ => -1
            };
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return virtualKey switch
            {
                0x31 => 0x12,
                0x32 => 0x13,
                0x33 => 0x14,
                0x34 => 0x15,
                0x35 => 0x17,
                0x36 => 0x16,
                0x37 => 0x1A,
                0x38 => 0x1C,
                0x39 => 0x19,
                0x30 => 0x1D,
                _ => -1
            };
        }

        return virtualKey switch
        {
            0x08 => 0x33, // Backspace
            0x09 => 0x30, // Tab
            0x0D => 0x24, // Return
            0x1B => 0x35, // Escape
            0x20 => 0x31, // Space
            0x21 => 0x74, // PageUp
            0x22 => 0x79, // PageDown
            0x23 => 0x77, // End
            0x24 => 0x73, // Home
            0x25 => 0x7B, // Left
            0x26 => 0x7E, // Up
            0x27 => 0x7C, // Right
            0x28 => 0x7D, // Down
            0x2D => 0x72, // Insert/Help
            0x2E => 0x75, // Forward delete
            0x70 => 0x7A, // F1
            0x71 => 0x78, // F2
            0x72 => 0x63, // F3
            0x73 => 0x76, // F4
            0x74 => 0x60, // F5
            0x75 => 0x61, // F6
            0x76 => 0x62, // F7
            0x77 => 0x64, // F8
            0x78 => 0x65, // F9
            0x79 => 0x6D, // F10
            0x7A => 0x67, // F11
            0x7B => 0x6F, // F12
            0xBA => 0x29, // ;
            0xBB => 0x18, // =
            0xBC => 0x2B, // ,
            0xBD => 0x1B, // -
            0xBE => 0x2F, // .
            0xBF => 0x2C, // /
            0xC0 => 0x32, // `
            0xDB => 0x21, // [
            0xDC => 0x2A, // backslash
            0xDD => 0x1E, // ]
            0xDE => 0x27, // '
            _ => -1
        };
    }

    private void UnregisterAll()
    {
        foreach (var hotkey in _registered.Values)
        {
            UnregisterEventHotKey(hotkey.Ref);
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
        lock (_sync)
        {
            UnregisterAll();
            if (_handlerRef != IntPtr.Zero)
            {
                RemoveEventHandler(_handlerRef);
                _handlerRef = IntPtr.Zero;
            }
        }
    }

    private delegate int EventHandlerDelegate(
        IntPtr callRef,
        IntPtr theEvent,
        IntPtr userData);

    private readonly record struct RegisteredHotkey(
        HotkeyBinding Binding,
        IntPtr Ref);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(
        IntPtr target,
        EventHandlerDelegate handler,
        uint numberOfTypes,
        EventTypeSpec[] eventTypes,
        IntPtr userData,
        out IntPtr handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyId hotkeyId,
        IntPtr target,
        uint options,
        out IntPtr hotkeyRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(IntPtr hotkeyRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int GetEventParameter(
        IntPtr theEvent,
        uint name,
        uint desiredType,
        IntPtr actualType,
        uint bufferSize,
        IntPtr actualSize,
        ref EventHotKeyId data);
}
