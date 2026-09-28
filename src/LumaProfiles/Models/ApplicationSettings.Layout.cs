namespace LumaProfiles.Models;

public sealed partial class ApplicationSettings
{
    public bool IsLeftPanelOpen { get; set; } = true;
    public bool IsRightPanelOpen { get; set; } = true;
    public bool IsLeftPanelCollapsed { get; set; }
    public string ProfilesViewMode { get; set; } = "Cards";
}
