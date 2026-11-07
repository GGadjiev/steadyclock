using Avalonia;
using Avalonia.Controls;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// Keeps the visible content centered when the vertical scrollbar appears.
/// The right gutter is constant; the left gutter grows by the space reserved
/// for the scrollbar only while it is actually visible.
/// </summary>
public static class ScrollGutterBalancer
{
    public static void Attach(
        ScrollViewer scrollViewer,
        Control content,
        double rightGutter,
        double bottomGutter)
    {
        void Update()
        {
            if (scrollViewer.Bounds.Width <= 0 || scrollViewer.Viewport.Width <= 0)
            {
                return;
            }

            var reserved = scrollViewer.Bounds.Width - scrollViewer.Viewport.Width;
            var left = rightGutter + Math.Clamp(reserved, 0, 24);
            var margin = new Thickness(left, 0, rightGutter, bottomGutter);
            if (content.Margin != margin)
            {
                content.Margin = margin;
            }
        }

        scrollViewer.LayoutUpdated += (_, _) => Update();
        Update();
    }
}
