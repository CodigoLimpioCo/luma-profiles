using System.Globalization;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public static class ScheduleResolver
{
    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>
    /// The time of day an entry starts on <paramref name="date"/>; null when it cannot be known (invalid time, a sun
    /// entry without a location, or no sunrise/sunset that day).
    /// </summary>
    public static TimeOnly? EntryTime(ScheduleSettings schedule, ScheduleEntry entry, DateOnly date, TimeSpan utcOffset)
    {
        if (entry.Sun == SunEvent.None) return TryParseTime(entry.Time, out var fixedTime) ? fixedTime : null;
        if (schedule.Latitude is not { } latitude || schedule.Longitude is not { } longitude) return null;

        var (sunrise, sunset) = SolarTimes.Calculate(date, latitude, longitude, utcOffset);
        var sunTime = entry.Sun == SunEvent.Sunrise ? sunrise : sunset;
        return sunTime?.AddMinutes(entry.OffsetMinutes);
    }

    /// <summary>
    /// Returns the entry in effect at <paramref name="now"/>: the latest one that has started today, or, before the
    /// first one starts, the last entry of the previous day. Entries whose time is unknown are ignored.
    /// </summary>
    public static ScheduleEntry? Resolve(ScheduleSettings schedule, DateTime now, TimeZoneInfo? zone = null)
    {
        var offset = (zone ?? TimeZoneInfo.Local).GetUtcOffset(now);
        var date = DateOnly.FromDateTime(now);
        var clock = TimeOnly.FromDateTime(now);

        var timed = new List<(ScheduleEntry Entry, TimeOnly Time)>();
        foreach (var entry in schedule.Entries ?? [])
        {
            if (EntryTime(schedule, entry, date, offset) is { } time) timed.Add((entry, time));
        }
        if (timed.Count == 0) return null;

        var ordered = timed.OrderBy(item => item.Time).ToList();
        var started = ordered.LastOrDefault(item => item.Time <= clock);
        return started.Entry ?? ordered[^1].Entry;
    }

    /// <summary>Fixed entries only; kept for callers that have no date or time zone at hand.</summary>
    public static ScheduleEntry? Resolve(ScheduleSettings schedule, TimeOnly now) =>
        Resolve(schedule, DateTime.Today.Add(now.ToTimeSpan()));
}
