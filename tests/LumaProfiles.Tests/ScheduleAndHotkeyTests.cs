using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class SolarTimesTests
{
    private static readonly TimeSpan Bogota = TimeSpan.FromHours(-5);

    [Fact]
    public void Calculate_NearTheEquator_GivesRoughlyTwelveHourDaysAroundSixAndEighteen()
    {
        var (sunrise, sunset) = SolarTimes.Calculate(new DateOnly(2026, 1, 15), 4.71, -74.07, Bogota);

        Assert.InRange(sunrise!.Value.ToTimeSpan().TotalMinutes, 5 * 60 + 40, 6 * 60 + 20);
        Assert.InRange(sunset!.Value.ToTimeSpan().TotalMinutes, 17 * 60 + 30, 18 * 60 + 15);
    }

    [Fact]
    public void Calculate_SummerAtHighLatitude_HasLongerDaysThanWinter()
    {
        var offset = TimeSpan.FromHours(1);
        var (summerRise, summerSet) = SolarTimes.Calculate(new DateOnly(2026, 6, 21), 52.5, 13.4, offset);
        var (winterRise, winterSet) = SolarTimes.Calculate(new DateOnly(2026, 12, 21), 52.5, 13.4, offset);

        var summer = summerSet!.Value - summerRise!.Value;
        var winter = winterSet!.Value - winterRise!.Value;
        Assert.True(summer > winter + TimeSpan.FromHours(6));
    }

    [Theory]
    [InlineData(2026, 6, 21, 80)]
    [InlineData(2026, 12, 21, 80)]
    public void Calculate_PolarDayOrNight_HasNoSunEvents(int year, int month, int day, double latitude)
    {
        var (sunrise, sunset) = SolarTimes.Calculate(new DateOnly(year, month, day), latitude, 15, TimeSpan.FromHours(1));

        Assert.Null(sunrise);
        Assert.Null(sunset);
    }
}

public class SunScheduleResolverTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("test-bogota", TimeSpan.FromHours(-5), "t", "t");

    private static ScheduleSettings Schedule(params ScheduleEntry[] entries) => new()
    {
        Latitude = 4.71,
        Longitude = -74.07,
        Entries = [.. entries],
    };

    private static ScheduleEntry Sun(SunEvent sun, string profile, int offset = 0) =>
        new() { Sun = sun, ProfileId = profile, OffsetMinutes = offset };

    private static DateTime At(int hour, int minute = 0) => new(2026, 1, 15, hour, minute, 0);

    [Theory]
    [InlineData(3, "night")]
    [InlineData(5, "night")]
    [InlineData(7, "day")]
    [InlineData(12, "day")]
    [InlineData(17, "day")]
    [InlineData(19, "night")]
    [InlineData(23, "night")]
    public void Resolve_FollowsSunriseAndSunset(int hour, string expected)
    {
        var schedule = Schedule(Sun(SunEvent.Sunrise, "day"), Sun(SunEvent.Sunset, "night"));

        Assert.Equal(expected, ScheduleResolver.Resolve(schedule, At(hour), Bogota)?.ProfileId);
    }

    [Fact]
    public void Resolve_OffsetShiftsTheSlot()
    {
        var plain = Schedule(Sun(SunEvent.Sunset, "night"), Sun(SunEvent.Sunrise, "day"));
        var shifted = Schedule(Sun(SunEvent.Sunset, "night", -120), Sun(SunEvent.Sunrise, "day"));

        Assert.Equal("day", ScheduleResolver.Resolve(plain, At(16, 30), Bogota)?.ProfileId);
        Assert.Equal("night", ScheduleResolver.Resolve(shifted, At(16, 30), Bogota)?.ProfileId);
    }

    [Fact]
    public void Resolve_SunEntriesWithoutLocationAreIgnored()
    {
        var schedule = new ScheduleSettings
        {
            Entries = [Sun(SunEvent.Sunrise, "sun"), new ScheduleEntry { Time = "10:00", ProfileId = "fixed" }],
        };

        Assert.Equal("fixed", ScheduleResolver.Resolve(schedule, At(3), Bogota)?.ProfileId);
        Assert.Null(ScheduleResolver.EntryTime(schedule, schedule.Entries[0], new DateOnly(2026, 1, 15), TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void Resolve_MixesFixedAndSunEntries()
    {
        var schedule = Schedule(
            Sun(SunEvent.Sunrise, "morning"),
            new ScheduleEntry { Time = "13:00", ProfileId = "afternoon" },
            Sun(SunEvent.Sunset, "evening"));

        Assert.Equal("morning", ScheduleResolver.Resolve(schedule, At(9), Bogota)?.ProfileId);
        Assert.Equal("afternoon", ScheduleResolver.Resolve(schedule, At(14), Bogota)?.ProfileId);
        Assert.Equal("evening", ScheduleResolver.Resolve(schedule, At(21), Bogota)?.ProfileId);
    }
}

public class ProfileBlendTests
{
    private static DisplayProfile Make(int brightness, double gamma, string temperature) => new()
    {
        Id = "p", Name = "p", Category = "c", Description = "d", PreviewStart = "#000000", PreviewEnd = "#ffffff",
        Brightness = brightness, Contrast = brightness, Saturation = 50, Hue = 0, Gamma = gamma,
        Red = 1, Green = 1, Blue = 1, ColorTemperature = temperature,
    };

    [Fact]
    public void Between_InterpolatesNumbersAndSwitchesTemperatureHalfway()
    {
        var from = Make(20, 1.0, "Cálido 5000 K");
        var to = Make(80, 1.2, "Neutro 6500 K");

        var quarter = ProfileBlend.Between(from, to, 0.25);
        var half = ProfileBlend.Between(from, to, 0.5);

        Assert.Equal(35, quarter.Brightness);
        Assert.Equal(1.05, quarter.Gamma, 3);
        Assert.Equal("Cálido 5000 K", quarter.ColorTemperature);
        Assert.Equal(50, half.Brightness);
        Assert.Equal("Neutro 6500 K", half.ColorTemperature);
    }

    [Fact]
    public void Between_ClampsAmountAndNeverTouchesTheInputs()
    {
        var from = Make(20, 1.0, "a");
        var to = Make(80, 1.2, "b");

        Assert.Equal(80, ProfileBlend.Between(from, to, 3).Brightness);
        Assert.Equal(20, ProfileBlend.Between(from, to, -1).Brightness);
        Assert.Equal(20, from.Brightness);
        Assert.Equal(80, to.Brightness);
    }
}

public class ProfileHotkeyKeyTests
{
    [Theory]
    [InlineData("1", 0x31u)]
    [InlineData("9", 0x39u)]
    [InlineData("F1", 0x70u)]
    [InlineData("F12", 0x7Bu)]
    public void TryParseKey_MapsDigitsAndFunctionKeys(string key, uint expected)
    {
        Assert.True(HotkeyService.TryParseKey(key, out var virtualKey));
        Assert.Equal(expected, virtualKey);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("F13")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseKey_RejectsReservedOrUnknownKeys(string? key) =>
        Assert.False(HotkeyService.TryParseKey(key, out _));

    [Fact]
    public void AvailableKeys_AreAllParsable() =>
        Assert.All(HotkeyService.AvailableKeys, key => Assert.True(HotkeyService.TryParseKey(key, out _)));
}

public class CustomProfileStoreTests
{
    [Fact]
    public void Load_KeepsCustomProfilesAfterTheBuiltInOnes()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var profiles = store.Load();
        profiles.Add(new DisplayProfile
        {
            Id = "custom-1", Name = "Mío", Category = "Personalizados", Description = "x",
            PreviewStart = "#000000", PreviewEnd = "#ffffff", IsCustom = true, Brightness = 42,
        });
        store.Save(profiles);

        var reloaded = new ProfileStore(dir.Path).Load();

        Assert.Equal(store.Defaults.Count + 1, reloaded.Count);
        Assert.True(reloaded[^1].IsCustom);
        Assert.Equal(42, reloaded[^1].Brightness);
        Assert.Equal("Mío", reloaded[^1].DisplayName);
    }

    [Fact]
    public void ReadAutomation_IsNullForFilesWithoutIt()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var file = Path.Combine(dir.Path, "plain.json");
        store.Export(file, store.Load());

        Assert.Null(store.ReadAutomation(file));
    }
}
