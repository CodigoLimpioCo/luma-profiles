using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void Defaults_HaveUniqueIdsAndValidRanges()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);

        Assert.Equal(60, store.Defaults.Count);
        Assert.Equal(store.Defaults.Count, store.Defaults.Select(p => p.Id).Distinct().Count());
        Assert.All(store.Defaults, p =>
        {
            Assert.InRange(p.Brightness, 0, 100);
            Assert.InRange(p.Contrast, 0, 100);
            Assert.InRange(p.Saturation, 0, 100);
            Assert.InRange(p.Gamma, 0.5, 2.5);
            Assert.InRange(p.Red, 0.0, 1.5);
            Assert.InRange(p.Green, 0.0, 1.5);
            Assert.InRange(p.Blue, 0.0, 1.5);
        });
    }

    [Fact]
    public void Load_WithoutFile_ReturnsDefaults()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);

        var profiles = store.Load();

        Assert.Equal(store.Defaults.Count, profiles.Count);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAdjustmentsAndFavorites()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var profiles = store.Load();
        profiles[0].Brightness = 12;
        profiles[0].IsFavorite = true;
        store.Save(profiles);

        var reloaded = new ProfileStore(dir.Path).Load();

        Assert.Equal(12, reloaded[0].Brightness);
        Assert.True(reloaded[0].IsFavorite);
        Assert.False(File.Exists(Path.Combine(dir.Path, "profiles.json.tmp")));
    }

    [Fact]
    public void Load_CorruptFile_QuarantinesItAndReturnsDefaults()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "profiles.json"), "{ not json");
        var store = new ProfileStore(dir.Path);

        var profiles = store.Load();

        Assert.Equal(store.Defaults.Count, profiles.Count);
        Assert.NotNull(store.LoadWarning);
        Assert.False(File.Exists(Path.Combine(dir.Path, "profiles.json")));
        Assert.Single(Directory.GetFiles(dir.Path, "profiles.json.corrupt-*.bak"));
    }

    [Fact]
    public void Load_LegacyArrayWithoutTemperature_MigratesFromDefaults()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "profiles.json"),
            """
            [{"Id":"natural","Name":"Natural","Category":"Color fiel","Description":"d","PreviewStart":"#000","PreviewEnd":"#000","Brightness":33,"Contrast":80,"Saturation":50,"Hue":0,"Gamma":1.0,"Red":1.0,"Green":1.0,"Blue":1.0}]
            """);

        var natural = store.Load().First(p => p.Id == "natural");

        Assert.Equal(33, natural.Brightness);
        Assert.Equal("Neutro 6500 K", natural.ColorTemperature);
    }

    [Fact]
    public void Load_KeepsNewDefaultTextButUserAdjustments()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var profiles = store.Load();
        profiles[0].Contrast = 41;
        store.Save(profiles);

        var reloaded = new ProfileStore(dir.Path).Load();

        Assert.Equal(41, reloaded[0].Contrast);
        Assert.Equal(store.Defaults[0].Description, reloaded[0].Description);
    }

    [Fact]
    public void ExportThenImport_CopiesAdjustments()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var source = store.Load();
        source[2].Gamma = 1.3;
        source[2].IsFavorite = true;
        var file = Path.Combine(dir.Path, "export.json");
        store.Export(file, source);

        var target = store.Load();
        var updated = store.Import(file, target);

        Assert.Equal(store.Defaults.Count, updated);
        Assert.Equal(1.3, target[2].Gamma);
        Assert.True(target[2].IsFavorite);
    }

    [Fact]
    public void GetDefault_IsCaseInsensitiveAndReturnsCopy()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);

        var a = store.GetDefault("NATURAL");
        a.Brightness = 1;

        Assert.NotEqual(1, store.GetDefault("natural").Brightness);
    }
}

public class LegacyDataTests
{
    [Fact]
    public void Load_NullColorTemperatureKeepsDefault()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "profiles.json"),
            """[{"Id":"natural","Name":"Natural","Category":"Color fiel","Description":"d","PreviewStart":"#000","PreviewEnd":"#000","Brightness":60,"Contrast":80,"Saturation":50,"Hue":0,"Gamma":1.0,"Red":1.0,"Green":1.0,"Blue":1.0,"ColorTemperature":null}]""");

        var natural = store.Load().First(p => p.Id == "natural");

        Assert.Equal(60, natural.Brightness);
        Assert.Equal("Neutro 6500 K", natural.ColorTemperature);
    }
}
