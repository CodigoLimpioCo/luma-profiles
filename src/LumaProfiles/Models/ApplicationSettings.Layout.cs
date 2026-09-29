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
    /// <summary>Full width in pixels of the scrollbar thumb while hovered (it is slimmer at rest).</summary>
    public double ScrollBarThickness { get; set; } = 7;
    public string ProfilesViewMode { get; set; } = "Cards";
}
