using System.Globalization;
using MyTimeTracker.Core.Common;
using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.ViewModels;

public sealed class HistoryGroupViewModel
{
    public HistoryGroupViewModel(
        DateOnly date,
        TimeSpan total,
        IReadOnlyList<SessionLineViewModel> sessions)
    {
        Date = date;
        Sessions = sessions;
        var localDate = date.ToDateTime(TimeOnly.MinValue);
        var text = localDate.ToString(
            "dddd, dd.MM.yyyy",
            CultureInfo.GetCultureInfo("ru-RU"));
        DateText = text.Length > 0
            ? char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..]
            : text;
        TotalText = TimeText.FormatDuration(total);
        SessionCountText = Strings.FormatSessionCount(sessions.Count);
    }

    public DateOnly Date { get; }
    public string DateText { get; }
    public string TotalText { get; }
    public string SessionCountText { get; }
    public IReadOnlyList<SessionLineViewModel> Sessions { get; }
}
