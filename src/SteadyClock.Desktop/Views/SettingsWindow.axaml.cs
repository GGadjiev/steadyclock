using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SteadyClock.Desktop.Resources;
using SteadyClock.Desktop.Services;
using SteadyClock.Desktop.ViewModels;

namespace SteadyClock.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        InputFocusHelper.Attach(this);

        var scroll = this.FindControl<ScrollViewer>("SettingsScroll");
        var content = this.FindControl<StackPanel>("SettingsContent");
        if (scroll is not null && content is not null)
        {
            ScrollGutterBalancer.Attach(scroll, content, 12, 0);
        }
    }

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(bool result) => Close(result);

    private async void ImportCustomSoundClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.Get("NotificationCustomSoundPickerTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(Strings.Get("NotificationCustomSoundFileType"))
                    {
                        Patterns = new[] { "*.wav", "*.mp3", "*.m4a", "*.wma", "*.aac" },
                        MimeTypes = new[]
                        {
                            "audio/wav", "audio/x-wav", "audio/mpeg",
                            "audio/mp4", "audio/x-m4a", "audio/x-ms-wma", "audio/aac"
                        }
                    }
                }
            });

            if (files.Count == 0)
            {
                return;
            }

            var localPath = files[0].TryGetLocalPath();
            await _viewModel.ImportCustomSoundAsync(localPath);
        }
        catch
        {
            _viewModel.SetCustomSoundError(Strings.Get("NotificationCustomSoundPickerError"));
        }
    }
}
