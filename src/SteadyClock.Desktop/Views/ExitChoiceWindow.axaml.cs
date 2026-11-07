using Avalonia.Controls;
using Avalonia.Interactivity;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.Views;

public partial class ExitChoiceWindow : Window
{
    public ExitChoiceWindow()
    {
        InitializeComponent();
    }

    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

    private void StopAndExitClicked(object? sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.StopAndExit;
        Close(true);
    }

    private void MinimizeClicked(object? sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.MinimizeToTray;
        Close(true);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.Cancel;
        Close(false);
    }
}
