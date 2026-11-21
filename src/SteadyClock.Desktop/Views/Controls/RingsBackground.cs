using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SteadyClock.Desktop.Views.Controls;

/// <summary>Subtle concentric rings behind the timer.</summary>
public sealed class RingsBackground : Control
{
    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<RingsBackground, IBrush?>(nameof(RingBrush));

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (RingBrush is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var max = Math.Min(Bounds.Width, Bounds.Height);
        var pen = new Pen(RingBrush, 1);
        for (var index = 0; index < 4; index++)
        {
            var radius = max * (0.18 + index * 0.105);
            context.DrawEllipse(null, pen, center, radius, radius);
        }
    }
}
