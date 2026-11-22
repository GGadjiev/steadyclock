using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SteadyClock.Desktop.Views.Controls;

/// <summary>
/// Draws a continuous timeline rail and a dot whose centre always sits on the
/// same vertical axis as the line. A single control avoids pixel snapping
/// differences between separately laid-out 1 px lines and ellipses.
/// </summary>
public sealed class TimelineRail : Control
{
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TimelineRail, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<TimelineRail, IBrush?>(nameof(DotBrush));

    public static readonly StyledProperty<double> DotDiameterProperty =
        AvaloniaProperty.Register<TimelineRail, double>(nameof(DotDiameter), 7);

    public static readonly StyledProperty<double> DotCenterYProperty =
        AvaloniaProperty.Register<TimelineRail, double>(nameof(DotCenterY), 20.5);

    public static readonly StyledProperty<bool> ShowTopConnectorProperty =
        AvaloniaProperty.Register<TimelineRail, bool>(nameof(ShowTopConnector), true);

    public static readonly StyledProperty<bool> ShowBottomConnectorProperty =
        AvaloniaProperty.Register<TimelineRail, bool>(nameof(ShowBottomConnector), true);

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public double DotDiameter
    {
        get => GetValue(DotDiameterProperty);
        set => SetValue(DotDiameterProperty, value);
    }

    public double DotCenterY
    {
        get => GetValue(DotCenterYProperty);
        set => SetValue(DotCenterYProperty, value);
    }

    public bool ShowTopConnector
    {
        get => GetValue(ShowTopConnectorProperty);
        set => SetValue(ShowTopConnectorProperty, value);
    }

    public bool ShowBottomConnector
    {
        get => GetValue(ShowBottomConnectorProperty);
        set => SetValue(ShowBottomConnectorProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var centerX = Bounds.Width / 2;
        var radius = Math.Max(0, DotDiameter) / 2;
        var dotTop = DotCenterY - radius;
        var dotBottom = DotCenterY + radius;

        if (LineBrush is { } lineBrush)
        {
            var pen = new Pen(lineBrush, 1);
            if (ShowTopConnector && dotTop > 0)
            {
                context.DrawLine(
                    pen,
                    new Point(centerX, 0),
                    new Point(centerX, dotTop));
            }

            if (ShowBottomConnector && dotBottom < Bounds.Height)
            {
                context.DrawLine(
                    pen,
                    new Point(centerX, dotBottom),
                    new Point(centerX, Bounds.Height));
            }
        }

        if (DotBrush is { } dotBrush && radius > 0)
        {
            context.DrawEllipse(
                dotBrush,
                null,
                new Point(centerX, DotCenterY),
                radius,
                radius);
        }
    }
}
