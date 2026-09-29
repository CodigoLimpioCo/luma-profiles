namespace LumaProfiles.Models;

public sealed partial class ApplicationSettings
{
    public bool IsLeftPanelOpen { get; set; } = true;
    public bool IsRightPanelOpen { get; set; } = true;
    public bool IsLeftPanelCollapsed { get; set; }
    public bool ConfirmChanges { get; set; } = true;
    public string ProfilesViewMode { get; set; } = "Cards";
}
