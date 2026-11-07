using Avalonia.Controls;
using SteadyClock.Desktop.ViewModels;

namespace SteadyClock.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(bool result) => Close(result);
}
