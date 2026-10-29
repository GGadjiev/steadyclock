using System.Globalization;
using System.Resources;

namespace MyTimeTracker.Core.Resources;

public static class CoreStrings
{
    private static readonly ResourceManager Manager =
        new("MyTimeTracker.Core.Resources.CoreStrings", typeof(CoreStrings).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
