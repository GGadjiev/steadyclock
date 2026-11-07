using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.Views.Controls;

/// <summary>
/// Read-only input that captures a Ctrl/Alt/Shift/Meta + key combination and
/// stores it as the platform-neutral <see cref="HotkeyBinding"/> model.
/// </summary>
public sealed class HotkeyTextBox : TextBox
{
    public static readonly StyledProperty<HotkeyBinding?> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyTextBox, HotkeyBinding?>(
            nameof(Hotkey),
            defaultBindingMode: BindingMode.TwoWay);

    public HotkeyTextBox()
    {
        IsReadOnly = true;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        GotFocus += (_, _) => SelectAll();
        UpdateDisplayText();
    }

    public HotkeyBinding? Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HotkeyProperty)
        {
            UpdateDisplayText();
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin or Key.Apps)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        if (key is Key.Delete or Key.Back && modifiers == KeyModifiers.None)
        {
            Hotkey = new HotkeyBinding();
            SelectAll();
            e.Handled = true;
            return;
        }

        if (key == Key.Tab && modifiers == KeyModifiers.None)
        {
            return;
        }

        var virtualKey = ToVirtualKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        Hotkey = new HotkeyBinding
        {
            Ctrl = (modifiers & KeyModifiers.Control) != 0,
            Alt = (modifiers & KeyModifiers.Alt) != 0,
            Shift = (modifiers & KeyModifiers.Shift) != 0,
            Win = (modifiers & KeyModifiers.Meta) != 0,
            VirtualKey = virtualKey
        };
        SelectAll();
        e.Handled = true;
    }

    private void UpdateDisplayText()
    {
        Text = Hotkey is null || Hotkey.IsEmpty
            ? Strings.Get("HotkeyNotAssigned")
            : Hotkey.DisplayName;
    }

    private static int ToVirtualKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return 0x41 + ((int)key - (int)Key.A);
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return 0x30 + ((int)key - (int)Key.D0);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return 0x60 + ((int)key - (int)Key.NumPad0);
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return 0x70 + ((int)key - (int)Key.F1);
        }

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Return => 0x0D,
            Key.Pause => 0x13,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Print or Key.PrintScreen => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe or Key.OemBackslash => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            _ => 0
        };
    }
}
