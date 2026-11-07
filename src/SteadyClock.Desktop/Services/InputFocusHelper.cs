using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Clears text input focus when the user clicks outside the input or presses
/// Escape. Attached to windows that contain text boxes.
/// </summary>
public static class InputFocusHelper
{
    public static void Attach(Window window)
    {
        window.Focusable = true;
        window.AddHandler(
            InputElement.PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        window.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        if (e.Source is Visual source &&
            source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            return;
        }

        if (IsTextInputFocused(window))
        {
            window.Focus();
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not Window window)
        {
            return;
        }

        if (IsTextInputFocused(window))
        {
            window.Focus();
            e.Handled = true;
        }
    }

    private static bool IsTextInputFocused(Window window) =>
        window.FocusManager?.GetFocusedElement() is TextBox;
}
