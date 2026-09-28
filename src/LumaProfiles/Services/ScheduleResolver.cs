using System.Globalization;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public enum ScheduleSlot { Day, Night }

public static class ScheduleResolver
{
    private static readonly TimeOnly DefaultDayStart = new(7, 0);
    private static readonly TimeOnly DefaultNightStart = new(20, 0);

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Returns which slot is active at <paramref name="now"/>; windows may wrap past midnight.</summary>
    public static ScheduleSlot Resolve(ScheduleSettings schedule, TimeOnly now)
    {
        var dayStart = TryParseTime(schedule.DayStart, out var d) ? d : DefaultDayStart;
        var nightStart = TryParseTime(schedule.NightStart, out var n) ? n : DefaultNightStart;
        if (dayStart == nightStart) return ScheduleSlot.Day;

        var isDay = dayStart < nightStart
            ? now >= dayStart && now < nightStart
            : now >= dayStart || now < nightStart;
        return isDay ? ScheduleSlot.Day : ScheduleSlot.Night;
    }

    public static string ProfileFor(ScheduleSettings schedule, ScheduleSlot slot) =>
        slot == ScheduleSlot.Day ? schedule.DayProfileId : schedule.NightProfileId;
}
