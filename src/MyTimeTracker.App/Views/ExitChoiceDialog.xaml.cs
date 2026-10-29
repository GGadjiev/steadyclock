using System.Windows;
using MyTimeTracker.App.Services;

namespace MyTimeTracker.App.Views;

public partial class ExitChoiceDialog : Window
{
    public ExitChoiceDialog()
    {
        InitializeComponent();
        StopAndExitButton.Click += (_, _) =>
        {
            Choice = ExitChoice.StopAndExit;
            DialogResult = true;
        };
        MinimizeButton.Click += (_, _) =>
        {
            Choice = ExitChoice.MinimizeToTray;
            DialogResult = true;
        };
        CancelButtonControl.Click += (_, _) =>
        {
            Choice = ExitChoice.Cancel;
            DialogResult = false;
        };
        Loaded += (_, _) => CancelButtonControl.Focus();
    }

    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;
}
