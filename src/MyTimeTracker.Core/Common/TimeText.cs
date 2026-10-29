using System.Globalization;
using MyTimeTracker.Core.Resources;

namespace MyTimeTracker.Core.Common;

public static class TimeText
{
    public static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalHours = (long)value.TotalHours;
        return $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    /// <summary>Compact duration for cards and session rows, e.g. "2 ч 05 мин".</summary>
    public static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        if (value.TotalHours >= 1)
        {
            return CoreStrings.Format("DurationHoursMinutes", (long)value.TotalHours, value.Minutes);
        }

        if (value.TotalMinutes >= 1)
        {
            return CoreStrings.Format("DurationMinutes", value.Minutes);
        }

        return CoreStrings.Format("DurationSeconds", Math.Max(0, value.Seconds));
    }

    public static string FormatDate(DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    public static string FormatTime(DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public static string FormatTimeWithSeconds(DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatDateLong(DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return local.ToString("dddd, dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    }

    public static DateTimeOffset LocalToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            throw new ArgumentException(CoreStrings.Get("DstInvalidTime"));
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetLocalDayRangeUtc(
        DateOnly localDate,
        TimeZoneInfo timeZone)
    {
        var start = ConvertDayBoundary(localDate, timeZone);
        var end = ConvertDayBoundary(localDate.AddDays(1), timeZone);
        return (start, end);
    }

    private static DateTimeOffset ConvertDayBoundary(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        // A few time zones transition at midnight, making 00:00 non-existent.
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static DateOnly GetLocalDate(DateTimeOffset utc, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);
}
