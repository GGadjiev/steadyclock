using Avalonia.Controls;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InputFocusHelper.Attach(this);

        var timerScroll = this.FindControl<ScrollViewer>("TimerScroll");
        var timerContent = this.FindControl<StackPanel>("TimerContent");
        if (timerScroll is not null && timerContent is not null)
        {
            ScrollGutterBalancer.Attach(timerScroll, timerContent, 12, 22);
        }

        var historyScroll = this.FindControl<ScrollViewer>("HistoryScroll");
        var historyContent = this.FindControl<StackPanel>("HistoryContent");
        if (historyScroll is not null && historyContent is not null)
        {
            ScrollGutterBalancer.Attach(historyScroll, historyContent, 12, 22);
        }
    }

    public bool AllowClose { get; set; }
}
