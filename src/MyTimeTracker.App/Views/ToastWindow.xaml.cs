using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using MyTimeTracker.App.Resources;

namespace MyTimeTracker.App.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Action? _primaryAction;
    private readonly Action? _secondaryAction;
    private bool _closed;

    public ToastWindow(
        string title,
        string message,
        string? primaryActionText = null,
        Action? primaryAction = null,
        string? secondaryActionText = null,
        Action? secondaryAction = null,
        int autoCloseSeconds = 8,
        string tag = "")
    {
        InitializeComponent();
        Tag = tag;
        Title = title;
        ToastTitleText.Text = title;
        ToastMessageText.Text = message;
        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;

        if (string.IsNullOrWhiteSpace(primaryActionText))
        {
            PrimaryActionButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            PrimaryActionButton.Content = primaryActionText;
            AutomationProperties.SetName(PrimaryActionButton, primaryActionText);
        }

        if (string.IsNullOrWhiteSpace(secondaryActionText))
        {
            SecondaryActionButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryActionButton.Content = secondaryActionText;
            AutomationProperties.SetName(SecondaryActionButton, secondaryActionText);
        }

        if (PrimaryActionButton.Visibility == Visibility.Collapsed &&
            SecondaryActionButton.Visibility == Visibility.Collapsed)
        {
            ActionsPanel.Visibility = Visibility.Collapsed;
        }

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(autoCloseSeconds, 3, 60))
        };
        _autoCloseTimer.Tick += (_, _) => Close();
        Loaded += OnLoaded;
        MouseEnter += (_, _) => _autoCloseTimer.Stop();
        MouseLeave += (_, _) => _autoCloseTimer.Start();
        Closed += (_, _) =>
        {
            _autoCloseTimer.Stop();
            _closed = true;
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                args.Handled = true;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 4;
        Top = workArea.Bottom - ActualHeight - 4;
        _autoCloseTimer.Start();
    }

    private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _primaryAction?.Invoke();
        Close();
    }

    private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _secondaryAction?.Invoke();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
