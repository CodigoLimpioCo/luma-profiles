using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class ApplicationSettingsStoreTests
{
    [Fact]
    public void Load_FirstRun_CreatesDefaultsFile()
    {
        using var dir = new TempDirectory();
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);

        var settings = store.Load();

        Assert.Equal("es", settings.LanguageCode);
        Assert.True(File.Exists(Path.Combine(dir.Path, "settings.json")));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsNewFeatureSettings()
    {
        using var dir = new TempDirectory();
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);
        var settings = store.Load();
        settings.IsDarkTheme = false;
        settings.Schedule = new ScheduleSettings { Enabled = true, NightStart = "21:30" };
        settings.AppRules.Add(new AppProfileRule { ProcessName = "game", ProfileId = "gamer-competitive" });
        store.Save(settings);

        var reloaded = store.Load();

        Assert.False(reloaded.IsDarkTheme);
        Assert.True(reloaded.Schedule.Enabled);
        Assert.Equal("21:30", reloaded.Schedule.NightStart);
        Assert.Equal("gamer-competitive", Assert.Single(reloaded.AppRules).ProfileId);
    }

    [Fact]
    public void Load_CorruptFile_QuarantinesAndRestoresDefaults()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), "garbage");
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);

        var settings = store.Load();

        Assert.NotNull(store.LoadWarning);
        Assert.Equal("es", settings.LanguageCode);
        Assert.Single(Directory.GetFiles(dir.Path, "settings.json.corrupt-*.bak"));
    }

    [Fact]
    public void Load_OldFileWithoutNewSections_FillsDefaults()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"LanguageCode":"en"}""");

        var settings = new ApplicationSettingsStore(dir.Path, manageStartup: false).Load();

        Assert.Equal("en", settings.LanguageCode);
        Assert.NotNull(settings.Schedule);
        Assert.Empty(settings.AppRules);
    }
}
