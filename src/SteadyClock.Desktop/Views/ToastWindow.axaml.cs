using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SteadyClock.Desktop.Views;

public partial class ToastWindow : Window
{
    private readonly Action? _primaryAction;
    private readonly Action? _secondaryAction;
    private readonly DispatcherTimer _autoCloseTimer;
    private bool _closed;

    public ToastWindow()
        : this(string.Empty, string.Empty, autoCloseSeconds: 8)
    {
    }

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
        NotificationTag = tag;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;

        PrimaryButton.IsVisible = !string.IsNullOrWhiteSpace(primaryActionText);
        PrimaryButton.Content = primaryActionText;
        SecondaryButton.IsVisible = !string.IsNullOrWhiteSpace(secondaryActionText);
        SecondaryButton.Content = secondaryActionText;
        ActionsPanel.IsVisible = PrimaryButton.IsVisible || SecondaryButton.IsVisible;

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(autoCloseSeconds, 4, 30))
        };
        _autoCloseTimer.Tick += (_, _) => Close();
        Opened += OnOpened;
        PointerEntered += (_, _) => _autoCloseTimer.Stop();
        PointerExited += (_, _) => _autoCloseTimer.Start();
        Closed += (_, _) =>
        {
            _autoCloseTimer.Stop();
            _closed = true;
        };
    }

    public string NotificationTag { get; }

    private void OnOpened(object? sender, EventArgs e)
    {
        _autoCloseTimer.Start();
        var screen = Screens.Primary;
        if (screen is not null)
        {
            var pixelsWidth = (int)Math.Ceiling(ClientSize.Width * screen.Scaling);
            var pixelsHeight = (int)Math.Ceiling(ClientSize.Height * screen.Scaling);
            Position = new PixelPoint(
                screen.WorkingArea.Right - pixelsWidth - 18,
                screen.WorkingArea.Bottom - pixelsHeight - 18);
        }
    }

    private void PrimaryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _primaryAction?.Invoke();
        Close();
    }

    private void SecondaryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _secondaryAction?.Invoke();
        Close();
    }

    private void CloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
