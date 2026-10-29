using System.ComponentModel;
using System.Windows;
using MyTimeTracker.App.ViewModels;

namespace MyTimeTracker.App;

public partial class MainWindow : Window
{
    private bool _closeRequestedRaised;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    public bool AllowClose { get; set; }
    public event EventHandler? CloseRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var availableHeight = Math.Max(320, workArea.Height - 16);
        var availableWidth = Math.Max(320, workArea.Width - 16);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MinWidth = Math.Min(MinWidth, availableWidth);
        if (Height > availableHeight)
        {
            Height = availableHeight;
        }

        if (Width > availableWidth)
        {
            Width = availableWidth;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_closeRequestedRaised)
        {
            return;
        }

        _closeRequestedRaised = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _closeRequestedRaised = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }));
    }
}
