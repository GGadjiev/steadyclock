using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace SteadyClock.Desktop.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
        : this(string.Empty, string.Empty, string.Empty, string.Empty, destructive: false, showCancel: true)
    {
    }

    public ConfirmWindow(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool destructive,
        bool showCancel)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        CancelButton.IsVisible = showCancel;
        if (destructive)
        {
            ConfirmButton.Classes.Add("danger");
        }

        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) =>
        {
            if (showCancel)
            {
                CancelButton.Focus();
            }
            else
            {
                ConfirmButton.Focus();
            }
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Escape)
            {
                Close(false);
                args.Handled = true;
            }
            else if (args.Key == Avalonia.Input.Key.Enter)
            {
                Close(true);
                args.Handled = true;
            }
        };
    }
}

