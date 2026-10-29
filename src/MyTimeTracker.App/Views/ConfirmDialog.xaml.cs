using System.Windows;

namespace MyTimeTracker.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool destructive)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ConfirmButtonControl.Content = confirmText;
        CancelButtonControl.Content = cancelText;
        ConfirmButtonControl.Style = (Style)FindResource(
            destructive ? "DangerButtonStyle" : "PrimaryButtonStyle");
        ConfirmButtonControl.Click += (_, _) => DialogResult = true;
        CancelButtonControl.Click += (_, _) => DialogResult = false;
        Loaded += (_, _) => CancelButtonControl.Focus();
    }
}
