using System.Globalization;
using System.Text.Json.Serialization;

namespace MyTimeTracker.Core.Models;

/// <summary>A global hotkey, serialized as modifier flags plus a virtual-key code.</summary>
public sealed class HotkeyBinding
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public int VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0;

    public HotkeyBinding Clone() => new()
    {
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Win = Win,
        VirtualKey = VirtualKey
    };

    public bool SameCombination(HotkeyBinding? other) =>
        other is not null &&
        !IsEmpty &&
        !other.IsEmpty &&
        Ctrl == other.Ctrl &&
        Alt == other.Alt &&
        Shift == other.Shift &&
        Win == other.Win &&
        VirtualKey == other.VirtualKey;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

            var parts = new List<string>(5);
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(VirtualKeyName(VirtualKey));
            return string.Join("+", parts);
        }
    }

    public override string ToString() => DisplayName;

    public static HotkeyBinding DefaultToggle() => new()
    {
        Ctrl = true,
        Alt = true,
        VirtualKey = 0x20 // Space
    };

    public static HotkeyBinding DefaultStop() => new()
    {
        Ctrl = true,
        Alt = true,
        Shift = true,
        VirtualKey = 0x20 // Space
    };

    public static HotkeyBinding DefaultShowHide() => new()
    {
        Ctrl = true,
        Alt = true,
        Shift = true,
        VirtualKey = 0x4F // O
    };

    public static string VirtualKeyName(int virtualKey)
    {
        if (virtualKey <= 0)
        {
            return string.Empty;
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString(CultureInfo.InvariantCulture);
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString(CultureInfo.InvariantCulture);
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"Num{SerializableDigit(virtualKey - 0x60)}";
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{SerializableDigit(virtualKey - 0x6F)}";
        }

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK{unchecked((uint)virtualKey):X2}"
        };
    }

    private static string SerializableDigit(int value) => value.ToString(CultureInfo.InvariantCulture);
}
