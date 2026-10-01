using System.Globalization;
using System.Windows.Media;

namespace LumaProfiles.ViewModels;

/// <summary>
/// One selectable accent: the brand color plus readable text/hover variants for both themes and the color of
/// text and icons drawn on top of the accent (dark on light accents, white on dark ones).
/// </summary>
public sealed record AccentOption(string Key, string Accent, string TextOnDark, string TextOnLight, string Hover, string OnAccent = AccentColors.DarkInk)
{
    /// <summary>True for a color the user composed themselves; its key is the hex value.</summary>
    public bool IsCustom => Key.StartsWith('#');
}

public static class AccentPalette
{
    public const string CustomKey = "Custom";

    /// <summary>The first six are tuned by hand; the rest are derived so every one stays readable in both themes.</summary>
    public static IReadOnlyList<AccentOption> All { get; } =
    [
        new("Cyan", "#55D6F5", "#8EDFF2", "#087B95", "#83E5FA"),
        new("Indigo", "#7B6CF6", "#B3AAFF", "#4F40D0", "#9A8DFF"),
        new("Blue", "#3B82F6", "#93BBFF", "#1D5FD1", "#6FA0FA"),
        new("Emerald", "#34D399", "#86EFC4", "#047857", "#6EE7B7"),
        new("Rose", "#FB7185", "#FDA4B0", "#BE123C", "#FDA4AF"),
        new("Amber", "#F59E0B", "#FCD34D", "#B45309", "#FBBF24"),
        AccentColors.Derive("Sky", "#38BDF8"),
        AccentColors.Derive("Teal", "#14B8A6"),
        AccentColors.Derive("Green", "#22C55E"),
        AccentColors.Derive("Lime", "#A3E635"),
        AccentColors.Derive("Yellow", "#FACC15"),
        AccentColors.Derive("Orange", "#F97316"),
        AccentColors.Derive("Red", "#EF4444"),
        AccentColors.Derive("Pink", "#EC4899"),
        AccentColors.Derive("Fuchsia", "#D946EF"),
        AccentColors.Derive("Violet", "#8B5CF6"),
        AccentColors.Derive("Slate", "#94A3B8"),
    ];

    public static AccentOption Default => All[0];

    /// <summary>A preset by name, or a custom color given as #RRGGBB; null for anything else.</summary>
    public static AccentOption? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (AccentColors.TryNormalizeHex(key, out var hex)) return AccentColors.Derive(hex, hex);
        return All.FirstOrDefault(option => option.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static AccentOption Get(string? key) => Find(key) ?? Default;
}

/// <summary>Color math for accents: hex parsing, HSV conversion and WCAG contrast.</summary>
public static class AccentColors
{
    /// <summary>Text on an accent surface when the accent is light.</summary>
    public const string DarkInk = "#051016";

    /// <summary>Text on an accent surface when the accent is dark.</summary>
    public const string LightInk = "#FFFFFF";

    private const double MinimumContrast = 4.5;
    private const string DarkSurface = "#121A23";
    private const string LightSurface = "#FFFFFF";

    public static bool TryNormalizeHex(string? text, out string hex)
    {
        hex = string.Empty;
        var value = text?.Trim().TrimStart('#') ?? string.Empty;
        if (value.Length == 3) value = string.Concat(value.Select(character => new string(character, 2)));
        if (value.Length != 6 || !value.All(Uri.IsHexDigit)) return false;
        hex = "#" + value.ToUpperInvariant();
        return true;
    }

    /// <summary>Builds the readable variants of any color, for a preset (named) or a custom (hex-keyed) accent.</summary>
    public static AccentOption Derive(string key, string hex)
    {
        TryNormalizeHex(hex, out var accent);
        var (hue, saturation, lightness) = ToHsl(accent);

        var hover = lightness > 0.8 ? FromHsl(hue, saturation, lightness - 0.08) : FromHsl(hue, saturation, Math.Min(0.92, lightness + 0.08));
        var onDark = Readable(hue, saturation, lightness, DarkSurface, lighten: true);
        var onLight = Readable(hue, saturation, lightness, LightSurface, lighten: false);
        var ink = Contrast(accent, DarkInk) >= Contrast(accent, LightInk) ? DarkInk : LightInk;
        return new AccentOption(key, accent, onDark, onLight, hover, ink);
    }

    /// <summary>Moves the lightness until the color reaches the minimum contrast against <paramref name="background"/>.</summary>
    private static string Readable(double hue, double saturation, double lightness, string background, bool lighten)
    {
        var current = lightness;
        var color = FromHsl(hue, saturation, current);
        while (Contrast(color, background) < MinimumContrast && (lighten ? current < 0.98 : current > 0.02))
        {
            current += lighten ? 0.02 : -0.02;
            color = FromHsl(hue, saturation, current);
        }

        return color;
    }

    public static double Contrast(string firstHex, string secondHex)
    {
        var first = Luminance(firstHex);
        var second = Luminance(secondHex);
        var (lighter, darker) = first >= second ? (first, second) : (second, first);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (red, green, blue) = ToRgb(hex);
        static double Channel(double value)
        {
            var scaled = value / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(red) + 0.7152 * Channel(green) + 0.0722 * Channel(blue);
    }

    public static (byte Red, byte Green, byte Blue) ToRgb(string hex)
    {
        var value = hex.TrimStart('#');
        return (byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string ToHex(byte red, byte green, byte blue) => $"#{red:X2}{green:X2}{blue:X2}";

    public static Color ToColor(string hex)
    {
        var (red, green, blue) = ToRgb(hex);
        return Color.FromRgb(red, green, blue);
    }

    /// <summary>Hue 0..360, saturation and value 0..1 (the model the color sliders use).</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(string hex)
    {
        var (red, green, blue) = ToRgb(hex);
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        return (HueOf(r, g, b, max, delta), max == 0 ? 0 : delta / max, max);
    }

    public static string FromHsv(double hue, double saturation, double value)
    {
        var h = (hue % 360 + 360) % 360;
        var s = Math.Clamp(saturation, 0, 1);
        var v = Math.Clamp(value, 0, 1);
        var chroma = v * s;
        var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - chroma;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return ToHex(Scale(r + m), Scale(g + m), Scale(b + m));
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(string hex)
    {
        var (red, green, blue) = ToRgb(hex);
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lightness = (max + min) / 2;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        return (HueOf(r, g, b, max, delta), saturation, lightness);
    }

    private static string FromHsl(double hue, double saturation, double lightness)
    {
        var l = Math.Clamp(lightness, 0, 1);
        var value = l + saturation * Math.Min(l, 1 - l);
        return FromHsv(hue, value == 0 ? 0 : 2 * (1 - l / value), value);
    }

    private static double HueOf(double r, double g, double b, double max, double delta)
    {
        if (delta == 0) return 0;
        var hue = max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    private static byte Scale(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}

/// <summary>Bindable swatch for the accent picker; selecting it notifies the view model.</summary>
public sealed class AccentChoice : ObservableObject
{
    private static readonly Brush Rainbow = CreateRainbow();

    private readonly Action<string> _onSelected;
    private string _name;
    private bool _isSelected;
    private Brush _fill;

    public AccentChoice(AccentOption option, string name, bool isSelected, Action<string> onSelected)
        : this(option, name, isSelected, onSelected, isCustomEntry: false)
    {
    }

    private AccentChoice(AccentOption option, string name, bool isSelected, Action<string> onSelected, bool isCustomEntry)
    {
        Option = option;
        _name = name;
        _isSelected = isSelected;
        _onSelected = onSelected;
        IsCustomEntry = isCustomEntry;
        _fill = Frozen(new SolidColorBrush(AccentColors.ToColor(option.Accent)));
    }

    /// <summary>The "your own color" tile: a rainbow until a custom color is chosen, then that color.</summary>
    public static AccentChoice CreateCustomEntry(string name, string? customHex, bool isSelected, Action<string> onSelected)
    {
        var option = AccentPalette.Find(customHex) ?? AccentPalette.Default;
        var choice = new AccentChoice(option, name, isSelected, onSelected, isCustomEntry: true);
        choice.ShowCustomColor(customHex);
        return choice;
    }

    public AccentOption Option { get; }

    /// <summary>The key sent when the tile is picked: the preset name, or <see cref="AccentPalette.CustomKey"/>.</summary>
    public string Key => IsCustomEntry ? AccentPalette.CustomKey : Option.Key;

    public bool IsCustomEntry { get; }
    public string Color => Option.Accent;

    public Brush Fill
    {
        get => _fill;
        private set => Set(ref _fill, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            if (value) _onSelected(Key);
        }
    }

    /// <summary>Paints the tile with the custom color, or the rainbow when none has been chosen yet.</summary>
    public void ShowCustomColor(string? hex) =>
        Fill = AccentColors.TryNormalizeHex(hex, out var normalized)
            ? Frozen(new SolidColorBrush(AccentColors.ToColor(normalized)))
            : Rainbow;

    /// <summary>Updates the highlight without echoing the change back to the view model.</summary>
    internal void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Brush CreateRainbow()
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
        foreach (var (offset, hex) in new[] { (0.0, "#EF4444"), (0.2, "#FACC15"), (0.4, "#22C55E"), (0.6, "#38BDF8"), (0.8, "#8B5CF6"), (1.0, "#EC4899") })
        {
            brush.GradientStops.Add(new GradientStop(AccentColors.ToColor(hex), offset));
        }

        brush.Freeze();
        return brush;
    }
}
