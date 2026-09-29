using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class ProfilePreviewTests
{
    private static readonly PreviewSettings Neutral = new(80, 80, 50, 0, 1.0, 1.0, 1.0, 1.0, "Neutro 6500 K");

    private static byte[] Sample() =>
    [
        // B, G, R, A
        10, 20, 30, 255,
        200, 120, 60, 255,
        128, 128, 128, 255,
        255, 255, 255, 255,
        0, 0, 0, 255
    ];

    private static byte[] Run(PreviewSettings settings)
    {
        var pixels = Sample();
        ProfilePreview.Apply(pixels, settings);
        return pixels;
    }

    private static double Mean(byte[] pixels) =>
        Enumerable.Range(0, pixels.Length / 4).Average(i => (pixels[i * 4] + pixels[(i * 4) + 1] + pixels[(i * 4) + 2]) / 3.0);

    [Fact]
    public void NeutralProfile_LeavesEveryPixelUnchanged() =>
        Assert.Equal(Sample(), Run(Neutral));

    [Fact]
    public void Alpha_IsNeverTouched()
    {
        var result = Run(Neutral with { Brightness = 20, Saturation = 0, Hue = 30 });

        for (var i = 3; i < result.Length; i += 4) Assert.Equal(255, result[i]);
    }

    [Fact]
    public void Brightness_DarkensAndBrightens()
    {
        var neutral = Mean(Run(Neutral));

        Assert.True(Mean(Run(Neutral with { Brightness = 20 })) < neutral);
        Assert.True(Mean(Run(Neutral with { Brightness = 100 })) > neutral);
    }

    [Fact]
    public void BrightnessFactor_IsOneAtTheNeutralLevelAndStaysLegibleWhenLow()
    {
        Assert.Equal(1.0, ProfilePreview.BrightnessFactor(80), 6);
        Assert.InRange(ProfilePreview.BrightnessFactor(0), 0.3, 0.4);
        Assert.True(ProfilePreview.BrightnessFactor(100) > 1.0);
    }

    [Fact]
    public void Contrast_SpreadsValuesAroundMidGrey()
    {
        var low = Run(Neutral with { Contrast = 50 });
        var high = Run(Neutral with { Contrast = 100 });

        // pixel 1 is darker than mid-grey (blue 200 is brighter, red 60 darker): compare the dark red channel
        Assert.True(high[6] < low[6]);
        Assert.True(high[4] > low[4]);
    }

    [Fact]
    public void ZeroSaturation_ProducesGreyPixels()
    {
        var result = Run(Neutral with { Saturation = 0 });

        for (var i = 0; i < result.Length; i += 4)
        {
            Assert.InRange(Math.Abs(result[i] - result[i + 1]), 0, 1);
            Assert.InRange(Math.Abs(result[i + 1] - result[i + 2]), 0, 1);
        }
    }

    [Fact]
    public void WarmPreset_LowersBlueMoreThanRed_AndCoolDoesTheOpposite()
    {
        var warm = Run(Neutral with { ColorTemperature = "Cálido 5000 K" });
        var cool = Run(Neutral with { ColorTemperature = "Frío 7500 K" });
        var neutral = Run(Neutral);

        // white pixel is index 3: B, G, R
        Assert.True(warm[12] < neutral[12]);
        Assert.True(warm[14] >= warm[12]);
        Assert.True(cool[14] < neutral[14]);
        Assert.True(cool[12] >= cool[14]);
    }

    [Fact]
    public void ChannelGains_ScaleOnlyTheirOwnChannel()
    {
        var result = Run(Neutral with { Red = 0.5 });

        Assert.Equal(Sample()[12], result[12]); // blue unchanged
        Assert.True(result[14] < Sample()[14]); // red halved
    }

    [Fact]
    public void Gamma_AboveOneBrightensMidtones()
    {
        Assert.True(Run(Neutral with { Gamma = 1.3 })[8] > Sample()[8]);
        Assert.True(Run(Neutral with { Gamma = 0.8 })[8] < Sample()[8]);
    }

    [Fact]
    public void HueRotation_ChangesColoursButKeepsGreysAndWhite()
    {
        var result = Run(Neutral with { Hue = 40 });
        var original = Sample();

        Assert.NotEqual(original.Take(4), result.Take(4));
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(Math.Abs(result[8 + channel] - original[8 + channel]), 0, 2); // mid grey
            Assert.InRange(Math.Abs(result[12 + channel] - original[12 + channel]), 0, 2); // white
        }
    }

    [Fact]
    public void Key_IdentifiesTheSettingsForCaching()
    {
        Assert.Equal(Neutral.Key, (Neutral with { }).Key);
        Assert.NotEqual(Neutral.Key, (Neutral with { Brightness = 81 }).Key);
        Assert.NotEqual(Neutral.Key, (Neutral with { ColorTemperature = "Cálido 5000 K" }).Key);
    }

    [Fact]
    public void EveryDefaultProfileProducesAValidPreview_AndTheNeutralOnesAreIdentity()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);

        foreach (var profile in store.Defaults)
        {
            var pixels = Sample();
            ProfilePreview.Apply(pixels, PreviewSettings.From(profile));
            Assert.Equal(Sample().Length, pixels.Length);
        }

        Assert.Equal(Sample(), Run(PreviewSettings.From(store.GetDefault("natural"))));
    }
}
