using Avalonia.Controls;

namespace SteadyClock.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public bool AllowClose { get; set; }
}
