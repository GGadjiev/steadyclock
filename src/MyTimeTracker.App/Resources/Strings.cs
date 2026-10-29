using System.Globalization;
using System.Resources;

namespace MyTimeTracker.App.Resources;

public static class Strings
{
    private static readonly ResourceManager Manager =
        new("MyTimeTracker.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string FormatSessionCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var key = mod100 is >= 11 and <= 14
            ? "SessionCountMany"
            : mod10 switch
            {
                1 => "SessionCountOne",
                2 or 3 or 4 => "SessionCountFew",
                _ => "SessionCountMany"
            };
        return Format(key, count);
    }
}
