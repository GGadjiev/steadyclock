using SteadyClock.Desktop.Resources;

namespace SteadyClock.Desktop.Services;

public sealed record SoundPreset(
    string Id,
    string FileName,
    string DisplayNameKey)
{
    public string DisplayName => Strings.Get(DisplayNameKey);
}

public static class SoundPresetCatalog
{
    public static readonly SoundPreset None =
        new("none", string.Empty, "SoundNone");

    public static readonly SoundPreset Soft =
        new("soft", "soft.wav", "SoundSoft");

    public static readonly SoundPreset Bell =
        new("bell", "bell.wav", "SoundBell");

    public static readonly SoundPreset TwoTone =
        new("twotone", "twotone.wav", "SoundTwoTone");

    public static readonly SoundPreset Click =
        new("click", "click.wav", "SoundClick");

    public static readonly SoundPreset Warning =
        new("warning", "warning.wav", "SoundWarning");

    public static readonly IReadOnlyList<SoundPreset> Options = new[]
    {
        None, Soft, Bell, TwoTone, Click, Warning
    };

    public static SoundPreset Get(string? id) =>
        Options.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.Ordinal))
        ?? Soft;
}
