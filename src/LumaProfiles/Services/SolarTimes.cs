namespace LumaProfiles.Services;

/// <summary>Sunrise and sunset from the NOAA general solar position equations; accurate to a few minutes, no network needed.</summary>
public static class SolarTimes
{
    private const double SunriseZenithDegrees = 90.833;

    /// <summary>Local clock times of sunrise and sunset, or nulls during polar day or night.</summary>
    public static (TimeOnly? Sunrise, TimeOnly? Sunset) Calculate(
        DateOnly date, double latitude, double longitude, TimeSpan utcOffset)
    {
        var fraction = 2 * Math.PI / 365 * (date.DayOfYear - 1);
        var equationOfTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(fraction) - 0.032077 * Math.Sin(fraction)
            - 0.014615 * Math.Cos(2 * fraction) - 0.040849 * Math.Sin(2 * fraction));
        var declination = 0.006918 - 0.399912 * Math.Cos(fraction) + 0.070257 * Math.Sin(fraction)
            - 0.006758 * Math.Cos(2 * fraction) + 0.000907 * Math.Sin(2 * fraction)
            - 0.002697 * Math.Cos(3 * fraction) + 0.00148 * Math.Sin(3 * fraction);

        var latitudeRadians = ToRadians(latitude);
        var cosHourAngle = Math.Cos(ToRadians(SunriseZenithDegrees)) / (Math.Cos(latitudeRadians) * Math.Cos(declination))
            - Math.Tan(latitudeRadians) * Math.Tan(declination);
        if (cosHourAngle is > 1 or < -1) return (null, null);

        var hourAngle = Math.Acos(cosHourAngle) * 180 / Math.PI;
        var sunrise = 720 - 4 * (longitude + hourAngle) - equationOfTime;
        var sunset = 720 - 4 * (longitude - hourAngle) - equationOfTime;
        return (ToLocal(sunrise, utcOffset), ToLocal(sunset, utcOffset));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static TimeOnly ToLocal(double utcMinutes, TimeSpan utcOffset)
    {
        var minutes = (utcMinutes + utcOffset.TotalMinutes) % 1440;
        if (minutes < 0) minutes += 1440;
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
    }
}
