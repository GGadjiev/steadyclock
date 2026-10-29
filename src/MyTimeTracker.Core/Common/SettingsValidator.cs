using System.Globalization;
using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Resources;

namespace MyTimeTracker.Core.Common;

public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (settings.IdleThresholdSeconds is < AppSettings.MinIdleThresholdSeconds or > AppSettings.MaxIdleThresholdSeconds)
        {
            errors.Add(CoreStrings.Format(
                "IdleThresholdRange",
                AppSettings.MinIdleThresholdSeconds,
                AppSettings.MaxIdleThresholdSeconds));
        }

        if (settings.ForgotTimerAfterMinutes is < 1 or > 1440)
        {
            errors.Add(CoreStrings.Get("ReminderAfterRange"));
        }

        if (settings.ReminderRepeatMinutes is < 5 or > 1440)
        {
            errors.Add(CoreStrings.Get("ReminderRepeatRange"));
        }

        if (settings.WorkDays is not { Length: 7 })
        {
            errors.Add(CoreStrings.Get("WorkDaysCorrupt"));
        }
        else if (settings.RemindersEnabled && settings.ForgotTimerReminderEnabled && !settings.WorkDays.Any(day => day))
        {
            errors.Add(CoreStrings.Get("WorkDaysAtLeastOne"));
        }

        if (!TryParseTime(settings.WorkStart, out var workStart))
        {
            errors.Add(CoreStrings.Get("WorkStartFormat"));
        }

        if (!TryParseTime(settings.WorkEnd, out var workEnd))
        {
            errors.Add(CoreStrings.Get("WorkEndFormat"));
        }

        if (TryParseTime(settings.WorkStart, out workStart) &&
            TryParseTime(settings.WorkEnd, out workEnd) &&
            workEnd <= workStart)
        {
            errors.Add(CoreStrings.Get("WorkEndAfterStart"));
        }

        ValidateHotkey(settings.ToggleTimerHotkey, CoreStrings.Get("ActionToggle"), errors);
        ValidateHotkey(settings.StopTimerHotkey, CoreStrings.Get("ActionStop"), errors);
        ValidateHotkey(settings.ShowHideHotkey, CoreStrings.Get("ActionShowHide"), errors);

        var activeHotkeys = new[]
        {
            settings.ToggleTimerHotkey,
            settings.StopTimerHotkey,
            settings.ShowHideHotkey
        }.Where(key => !key.IsEmpty).ToList();

        for (var i = 0; i < activeHotkeys.Count; i++)
        {
            for (var j = i + 1; j < activeHotkeys.Count; j++)
            {
                if (activeHotkeys[i].SameCombination(activeHotkeys[j]))
                {
                    errors.Add(CoreStrings.Format("HotkeyDuplicate", activeHotkeys[i].DisplayName));
                }
            }
        }

        return errors;
    }

    public static bool TryParseTime(string? text, out TimeOnly value) =>
        TimeOnly.TryParseExact(
            (text ?? string.Empty).Trim(),
            new[] { "H:mm", "HH:mm" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);

    private static void ValidateHotkey(HotkeyBinding? hotkey, string action, ICollection<string> errors)
    {
        if (hotkey is null)
        {
            errors.Add(CoreStrings.Format("HotkeyMissing", action));
            return;
        }

        if (hotkey.IsEmpty)
        {
            return;
        }

        if (!hotkey.Ctrl && !hotkey.Alt && !hotkey.Shift && !hotkey.Win)
        {
            var isFunctionKey = hotkey.VirtualKey is >= 0x70 and <= 0x87;
            if (!isFunctionKey)
            {
                errors.Add(CoreStrings.Format("HotkeyModifierRequired", action));
            }
        }
    }
}
