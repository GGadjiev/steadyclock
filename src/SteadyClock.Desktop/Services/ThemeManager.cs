using Avalonia;
using Avalonia.Styling;
using MyTimeTracker.Core.Models;

namespace SteadyClock.Desktop.Services;

public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}
