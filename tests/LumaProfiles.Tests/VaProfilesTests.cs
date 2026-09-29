using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class VaProfilesTests
{
    private const string Category = "Pantallas VA";

    private static IReadOnlyList<LumaProfiles.Models.DisplayProfile> VaProfiles()
    {
        using var dir = new TempDirectory();
        return new ProfileStore(dir.Path).Defaults.Where(profile => profile.Category == Category).ToList();
    }

    [Fact]
    public void TenProfilesForVaPanelsExistWithUniqueIds()
    {
        var profiles = VaProfiles();

        Assert.Equal(10, profiles.Count);
        Assert.Equal(10, profiles.Select(profile => profile.Id).Distinct().Count());
        Assert.All(profiles, profile => Assert.StartsWith("va-", profile.Id));
    }

    [Fact]
    public void VaProfilesRespectWhatBudgetVaPanelsNeed()
    {
        Assert.All(VaProfiles(), profile =>
        {
            // contrast above ~80 clips the whites of these panels
            Assert.InRange(profile.Contrast, 60, 80);
            // shadows are lifted with gamma (VA crushes blacks), never darkened
            Assert.InRange(profile.Gamma, 1.0, 1.2);
            // the factory white is cool: blue is trimmed, never boosted, and red is left alone
            Assert.InRange(profile.Blue, 0.6, 1.0);
            Assert.Equal(1.0, profile.Red);
            Assert.InRange(profile.Saturation, 35, 65);
            Assert.False(profile.IsHdr);
            // software correction plus brightness/contrast only: no forced monitor color preset
            Assert.Equal("Usuario (RGB)", profile.ColorTemperature);
        });
    }

    [Fact]
    public void OnlyTheGamingModesAskForHighPerformance()
    {
        var highPerformance = VaProfiles().Where(profile => profile.PowerPlan == "HighPerformance").Select(profile => profile.Id);

        Assert.Equal(["va-fps", "va-gaming-120"], highPerformance.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("es")]
    public void TheCategoryAndSidebarEntryAreTranslated(string code)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Locales", code + ".lang"));

        Assert.Contains(lines, line => line.StartsWith("category.Pantallas VA=") && line.Length > "category.Pantallas VA=".Length);
        Assert.Contains(lines, line => line.StartsWith("ui.VaScreens=") && line.Length > "ui.VaScreens=".Length);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryVaProfileHasATranslatedNameAndDescription(string code)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Locales", code + ".lang"));

        Assert.All(VaProfiles(), profile =>
        {
            Assert.Contains(lines, line => line.StartsWith($"profile.{profile.Id}.name=") && line.Length > $"profile.{profile.Id}.name=".Length);
            Assert.Contains(lines, line => line.StartsWith($"profile.{profile.Id}.description=") && line.Length > $"profile.{profile.Id}.description=".Length);
        });
    }

    [Fact]
    public void EachVaProfilePreviewDiffersFromTheOriginalPicture()
    {
        var neutral = Enumerable.Range(0, 64).SelectMany(i => new byte[] { (byte)(i * 4), (byte)(255 - (i * 4)), (byte)(i * 2), 255 }).ToArray();

        foreach (var profile in VaProfiles())
        {
            var pixels = (byte[])neutral.Clone();
            ProfilePreview.Apply(pixels, PreviewSettings.From(profile));
            Assert.NotEqual(neutral, pixels);
        }
    }
}
