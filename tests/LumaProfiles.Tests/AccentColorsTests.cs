using LumaProfiles.ViewModels;

namespace LumaProfiles.Tests;

public class AccentColorsTests
{
    private const string DarkSurface = "#121A23";
    private const string LightSurface = "#FFFFFF";

    [Fact]
    public void EveryPresetIsReadableInBothThemes()
    {
        Assert.True(AccentPalette.All.Count >= 17);
        Assert.Equal(AccentPalette.All.Count, AccentPalette.All.Select(option => option.Key).Distinct().Count());
        foreach (var option in AccentPalette.All)
        {
            Assert.True(AccentColors.Contrast(option.TextOnDark, DarkSurface) >= 4.5, $"{option.Key} text on dark");
            Assert.True(AccentColors.Contrast(option.TextOnLight, LightSurface) >= 4.5, $"{option.Key} text on light");
            Assert.True(AccentColors.Contrast(option.Accent, option.OnAccent) >= 3.0, $"{option.Key} ink on accent");
        }
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#101010")]
    [InlineData("#1E3A8A")]
    [InlineData("#7C3AED")]
    [InlineData("#FF0000")]
    [InlineData("#FFFF00")]
    [InlineData("#F0F0F0")]
    [InlineData("#FFFFFF")]
    public void AnyCustomColorGetsReadableVariants(string hex)
    {
        var option = AccentColors.Derive(hex, hex);

        Assert.True(option.IsCustom);
        Assert.Equal(hex, option.Accent);
        Assert.True(AccentColors.Contrast(option.TextOnDark, DarkSurface) >= 4.5, "text on dark");
        Assert.True(AccentColors.Contrast(option.TextOnLight, LightSurface) >= 4.5, "text on light");
        Assert.True(AccentColors.Contrast(option.Accent, option.OnAccent) >= 3.0, "ink on accent");
    }

    [Fact]
    public void DarkAccentsGetWhiteInkAndLightOnesDarkInk()
    {
        Assert.Equal(AccentColors.LightInk, AccentColors.Derive("#1E3A8A", "#1E3A8A").OnAccent);
        Assert.Equal(AccentColors.DarkInk, AccentColors.Derive("#FACC15", "#FACC15").OnAccent);
    }

    [Theory]
    [InlineData("#3b82f6", "#3B82F6")]
    [InlineData("3B82F6", "#3B82F6")]
    [InlineData("  #abc ", "#AABBCC")]
    public void TryNormalizeHex_AcceptsCommonSpellings(string text, string expected)
    {
        Assert.True(AccentColors.TryNormalizeHex(text, out var hex));
        Assert.Equal(expected, hex);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("blue")]
    public void TryNormalizeHex_RejectsAnythingElse(string? text) =>
        Assert.False(AccentColors.TryNormalizeHex(text, out _));

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#3B82F6")]
    [InlineData("#808080")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void HsvRoundTripsThroughHex(string hex)
    {
        var (hue, saturation, value) = AccentColors.ToHsv(hex);

        Assert.Equal(hex, AccentColors.FromHsv(hue, saturation, value));
    }

    [Fact]
    public void Find_UnderstandsPresetsAndHexColors()
    {
        Assert.Equal("Amber", AccentPalette.Find("amber")?.Key);
        Assert.Equal("#3B82F6", AccentPalette.Find("#3b82f6")?.Key);
        Assert.True(AccentPalette.Find("#3B82F6")!.IsCustom);
        Assert.Null(AccentPalette.Find("NotAColor"));
        Assert.Equal(AccentPalette.Default, AccentPalette.Get("NotAColor"));
    }
}
