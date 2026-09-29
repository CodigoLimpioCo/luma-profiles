namespace LumaProfiles.Models;

public sealed partial class ApplicationSettings
{
    public bool IsLeftPanelOpen { get; set; } = true;
    public bool IsRightPanelOpen { get; set; } = true;
    public bool IsLeftPanelCollapsed { get; set; }
    public bool ConfirmChanges { get; set; } = true;
    /// <summary>"Light", "Dark" or "System". Null in files written before theme modes existed.</summary>
    public string? ThemeMode { get; set; }
    public string AccentColor { get; set; } = "Cyan";
    public string FontFamilyName { get; set; } = "Segoe UI Variable Text";
    /// <summary>Scale of the whole interface (text and elements) in percent.</summary>
    public int UiScalePercent { get; set; } = 100;
    /// <summary>Full width in pixels of the scrollbar thumb while hovered (it is slimmer at rest).</summary>
    public double ScrollBarThickness { get; set; } = 7;
    public string ProfilesViewMode { get; set; } = "Cards";
}
