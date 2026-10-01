using LumaProfiles.Models;

namespace LumaProfiles.Services;

/// <summary>Intermediate frames for fading from one profile to another.</summary>
public static class ProfileBlend
{
    /// <summary>
    /// A copy of <paramref name="to"/> whose adjustments sit <paramref name="amount"/> (0..1) of the way from
    /// <paramref name="from"/>. The color preset is a discrete choice, so it switches halfway.
    /// </summary>
    public static DisplayProfile Between(DisplayProfile from, DisplayProfile to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        var frame = to.Clone();
        frame.Brightness = Lerp(from.Brightness, to.Brightness, t);
        frame.Contrast = Lerp(from.Contrast, to.Contrast, t);
        frame.Saturation = Lerp(from.Saturation, to.Saturation, t);
        frame.Hue = Lerp(from.Hue, to.Hue, t);
        frame.Gamma = Lerp(from.Gamma, to.Gamma, t);
        frame.Red = Lerp(from.Red, to.Red, t);
        frame.Green = Lerp(from.Green, to.Green, t);
        frame.Blue = Lerp(from.Blue, to.Blue, t);
        frame.ColorTemperature = t < 0.5 ? from.ColorTemperature : to.ColorTemperature;
        return frame;
    }

    private static int Lerp(int from, int to, double t) => (int)Math.Round(from + (to - from) * t);

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
