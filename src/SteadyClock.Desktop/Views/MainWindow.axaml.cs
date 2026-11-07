using Avalonia.Controls;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InputFocusHelper.Attach(this);
    }

    public bool AllowClose { get; set; }
}
