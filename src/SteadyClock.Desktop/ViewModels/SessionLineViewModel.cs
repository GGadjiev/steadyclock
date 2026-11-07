using MyTimeTracker.Core.Common;
using MyTimeTracker.Core.Models;
using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.ViewModels;

public sealed class SessionLineViewModel
{
    public SessionLineViewModel(SessionDaySlice slice, TimeZoneInfo timeZone)
    {
        SessionId = slice.SessionId;
        StartText = TimeText.FormatTime(slice.StartUtc, timeZone);
        EndText = slice.IsRunning ? Strings.Get("Now") : TimeText.FormatTime(slice.EndUtc, timeZone);
        TimeRangeText = $"{StartText} — {EndText}";
        DurationText = TimeText.FormatDuration(slice.Duration);
        Comment = slice.Comment;
        IsManual = slice.IsManual;
        IsActive = slice.IsActive;
        Badge = slice.IsRunning
            ? Strings.Get("Now")
            : slice.IsManual
                ? Strings.Get("ManualBadge")
                : string.Empty;
    }

    public long SessionId { get; }
    public string StartText { get; }
    public string EndText { get; }
    public string TimeRangeText { get; }
    public string DurationText { get; }
    public string Comment { get; }
    public bool IsManual { get; }
    public bool IsActive { get; }
    public string Badge { get; }
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
}
