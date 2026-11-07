using CommunityToolkit.Mvvm.Input;
using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.ViewModels;

public sealed class SessionLineViewModel
{
    public SessionLineViewModel(
        SessionDaySlice slice,
        TimeZoneInfo timeZone,
        Func<SessionLineViewModel, Task> edit,
        Func<SessionLineViewModel, Task> delete)
    {
        SessionId = slice.SessionId;
        StartText = TimeText.FormatTime(slice.StartUtc, timeZone);
        EndText = slice.IsRunning ? Strings.Get("Now") : TimeText.FormatTime(slice.EndUtc, timeZone);
        TimeRangeText = $"{StartText} — {EndText}";
        DurationText = TimeText.FormatDuration(slice.Duration);
        Comment = slice.Comment;
        IsManual = slice.IsManual;
        IsActive = slice.IsActive;
        CanManage = !slice.IsActive;
        Badge = slice.IsRunning
            ? Strings.Get("Now")
            : slice.IsManual
                ? Strings.Get("ManualBadge")
                : string.Empty;
        EditCommand = new AsyncRelayCommand(
            () => edit(this),
            () => CanManage);
        DeleteCommand = new AsyncRelayCommand(
            () => delete(this),
            () => CanManage);
    }

    public long SessionId { get; }
    public string StartText { get; }
    public string EndText { get; }
    public string TimeRangeText { get; }
    public string DurationText { get; }
    public string Comment { get; }
    public bool IsManual { get; }
    public bool IsActive { get; }
    public bool CanManage { get; }
    public string Badge { get; }
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    public string EditLabel => Strings.Get("EditShort");
    public string DeleteLabel => Strings.Get("DeleteShort");
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
}
