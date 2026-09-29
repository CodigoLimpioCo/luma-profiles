using LumaProfiles.Models;

namespace LumaProfiles.Services;

/// <summary>Everything about a profile that changes how an image looks.</summary>
public sealed record PreviewSettings(
    int Brightness, int Contrast, int Saturation, int Hue,
    double Gamma, double Red, double Green, double Blue, string ColorTemperature)
{
    public static PreviewSettings From(DisplayProfile profile) => new(
        profile.Brightness, profile.Contrast, profile.Saturation, profile.Hue,
        profile.Gamma, profile.Red, profile.Green, profile.Blue, profile.ColorTemperature ?? string.Empty);

    public string Key => FormattableString.Invariant(
        $"{Brightness}|{Contrast}|{Saturation}|{Hue}|{Gamma:F3}|{Red:F3}|{Green:F3}|{Blue:F3}|{ColorTemperature}");
}

/// <summary>
/// Software simulation of what a profile does to the picture, used for the before/after preview.
/// The reference is the "Natural" profile (brightness 80, contrast 80, saturation 50, gamma 1, neutral RGB),
/// which leaves every pixel unchanged.
/// </summary>
public static class ProfilePreview
{
    public const int NeutralBrightness = 80;
    public const int NeutralContrast = 80;
    public const int NeutralSaturation = 50;

    /// <summary>Transforms BGRA pixels in place.</summary>
    public static void Apply(byte[] bgra, PreviewSettings settings)
    {
        var (tempRed, tempGreen, tempBlue) = TemperatureGains(settings.ColorTemperature);
        var blue = BuildLookup(settings, settings.Blue * tempBlue);
        var green = BuildLookup(settings, settings.Green * tempGreen);
        var red = BuildLookup(settings, settings.Red * tempRed);

        var saturation = settings.Saturation / (double)NeutralSaturation;
        var mixSaturation = settings.Saturation != NeutralSaturation;
        var mixHue = settings.Hue != 0;
        var hue = mixHue ? HueMatrix(settings.Hue * 0.6 * Math.PI / 180.0) : null;

        for (var index = 0; index + 3 < bgra.Length; index += 4)
        {
            double b = blue[bgra[index]];
            double g = green[bgra[index + 1]];
            double r = red[bgra[index + 2]];

            if (mixSaturation)
            {
                var luma = (0.299 * r) + (0.587 * g) + (0.114 * b);
                r = luma + ((r - luma) * saturation);
                g = luma + ((g - luma) * saturation);
                b = luma + ((b - luma) * saturation);
            }

            if (hue is not null)
            {
                var nr = (hue[0] * r) + (hue[1] * g) + (hue[2] * b);
                var ng = (hue[3] * r) + (hue[4] * g) + (hue[5] * b);
                var nb = (hue[6] * r) + (hue[7] * g) + (hue[8] * b);
                r = nr;
                g = ng;
                b = nb;
            }

            bgra[index] = ToByte(b);
            bgra[index + 1] = ToByte(g);
            bgra[index + 2] = ToByte(r);
        }
    }

    /// <summary>Per-channel curve: gamma, channel gain, contrast around mid-grey, then brightness.</summary>
    internal static byte[] BuildLookup(PreviewSettings settings, double channelGain)
    {
        var lookup = new byte[256];
        var gamma = settings.Gamma <= 0 ? 1.0 : settings.Gamma;
        var contrast = 1.0 + ((settings.Contrast - NeutralContrast) / 100.0);
        var brightness = BrightnessFactor(settings.Brightness);

        for (var index = 0; index < 256; index++)
        {
            var value = Math.Pow(index / 255.0, 1.0 / gamma) * channelGain;
            value = ((value - 0.5) * contrast) + 0.5;
            value *= brightness;
            lookup[index] = ToByte(value * 255.0);
        }

        return lookup;
    }

    /// <summary>1.0 at the neutral brightness; a softened curve so low backlight levels stay legible.</summary>
    internal static double BrightnessFactor(int brightness) =>
        0.35 + (0.65 * Math.Clamp(brightness, 0, 100) / NeutralBrightness);

    /// <summary>Approximate RGB gains of the monitor's warm and cool presets.</summary>
    internal static (double Red, double Green, double Blue) TemperatureGains(string colorTemperature) => colorTemperature switch
    {
        "Cálido 5000 K" => (1.0, 0.93, 0.78),
        "Frío 7500 K" => (0.90, 0.97, 1.0),
        _ => (1.0, 1.0, 1.0)
    };

    private static double[] HueMatrix(double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return
        [
            0.213 + (cos * 0.787) - (sin * 0.213), 0.715 - (cos * 0.715) - (sin * 0.715), 0.072 - (cos * 0.072) + (sin * 0.928),
            0.213 - (cos * 0.213) + (sin * 0.143), 0.715 + (cos * 0.285) + (sin * 0.140), 0.072 - (cos * 0.072) - (sin * 0.283),
            0.213 - (cos * 0.213) - (sin * 0.787), 0.715 - (cos * 0.715) + (sin * 0.715), 0.072 + (cos * 0.928) + (sin * 0.072)
        ];
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0.0, 255.0));
}
