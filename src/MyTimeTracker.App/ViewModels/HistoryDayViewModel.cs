using MyTimeTracker.App.Infrastructure;
using MyTimeTracker.App.Resources;
using MyTimeTracker.Core.Common;

namespace MyTimeTracker.App.ViewModels;

public sealed class HistoryDayViewModel : ObservableObject
{
    public HistoryDayViewModel(
        DateOnly date,
        TimeSpan total,
        IReadOnlyList<SessionRowViewModel> sessions)
    {
        Date = date;
        Total = total;
        Sessions = sessions;
        var localDate = date.ToDateTime(TimeOnly.MinValue);
        DateText = localDate.ToString("dddd, dd.MM.yyyy",
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        if (DateText.Length > 0)
        {
            DateText = char.ToUpper(DateText[0], System.Globalization.CultureInfo.CurrentCulture) + DateText[1..];
        }

        TotalText = TimeText.FormatDuration(total);
        SessionCountText = Strings.FormatSessionCount(sessions.Count);
    }

    public DateOnly Date { get; }
    public TimeSpan Total { get; }
    public string DateText { get; }
    public string TotalText { get; }
    public string SessionCountText { get; }
    public IReadOnlyList<SessionRowViewModel> Sessions { get; }
}
