using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is TimerStatus timerStatus ? timerStatus : TimerStatus.Stopped;
        var suffix = parameter as string ?? "Background";
        var key = $"Status{status}{suffix}Brush";
        if (Application.Current is { } application)
        {
            if (application.TryFindResource(key, application.ActualThemeVariant, out var themedBrush))
            {
                return themedBrush;
            }

            if (application.TryFindResource(key, out var brush))
            {
                return brush;
            }
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
