using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.App.Views.Controls;

/// <summary>Read-only input that captures a Ctrl/Alt/Shift + key combination.</summary>
public sealed class HotkeyTextBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(HotkeyBinding),
        typeof(HotkeyTextBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnHotkeyChanged));

    public HotkeyTextBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        PreviewKeyDown += OnPreviewKeyDown;
        GotKeyboardFocus += (_, _) => SelectAll();
        UpdateDisplayText();
    }

    public HotkeyBinding? Hotkey
    {
        get => (HotkeyBinding?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private static void OnHotkeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((HotkeyTextBox)dependencyObject).UpdateDisplayText();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Apps)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (key is Key.Delete or Key.Back && modifiers == ModifierKeys.None)
        {
            Hotkey = new HotkeyBinding();
            SelectAll();
            e.Handled = true;
            return;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        Hotkey = new HotkeyBinding
        {
            Ctrl = (modifiers & ModifierKeys.Control) != 0,
            Alt = (modifiers & ModifierKeys.Alt) != 0,
            Shift = (modifiers & ModifierKeys.Shift) != 0,
            Win = false,
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
}
