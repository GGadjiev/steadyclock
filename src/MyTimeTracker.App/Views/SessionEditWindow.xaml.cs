using System.Windows;
using MyTimeTracker.App.ViewModels;

namespace MyTimeTracker.App.Views;

public partial class SessionEditWindow : Window
{
    private readonly SessionEditViewModel _viewModel;

    public SessionEditWindow(SessionEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
        Loaded += (_, _) => FitToWorkArea();
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var availableHeight = Math.Max(320, workArea.Height - 32);
        var availableWidth = Math.Max(360, workArea.Width - 32);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MaxHeight = availableHeight;
        MaxWidth = availableWidth;
        if (Height > availableHeight)
        {
            Height = availableHeight;
        }

        if (Width > availableWidth)
        {
            Width = availableWidth;
        }
    }

    private void OnCloseRequested(bool result)
    {
        DialogResult = result;
    }
}
