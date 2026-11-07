using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Services;

namespace SteadyClock.Desktop.ViewModels;

public enum NotificationEventKind
{
    AutoPause,
    Reminder,
    Error,
    ProcessStarted,
    ProcessStopped
}

public partial class NotificationRuleViewModel : ObservableObject
{
    private readonly Action<SoundPreset> _preview;

    [ObservableProperty]
    private bool _showToast;

    [ObservableProperty]
    private SoundPreset _selectedSound;

    public NotificationRuleViewModel(
        NotificationEventKind kind,
        string title,
        NotificationRuleSettings rule,
        Action<SoundPreset> preview)
    {
        Kind = kind;
        Title = title;
        ShowToast = rule.ShowToast;
        SelectedSound = SoundPresetCatalog.Get(rule.SoundId);
        _preview = preview;
        SoundOptions = SoundPresetCatalog.Options;
    }

    public NotificationEventKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<SoundPreset> SoundOptions { get; }

    [RelayCommand]
    private void PreviewSound() => _preview(SelectedSound);

    public void ApplyTo(NotificationRuleSettings target)
    {
        target.ShowToast = ShowToast;
        target.SoundId = SelectedSound.Id;
    }
}
