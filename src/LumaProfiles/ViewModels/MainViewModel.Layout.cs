using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;


namespace LumaProfiles.ViewModels;

/// <summary>Window layout: library view mode, collapsible sidebar and chrome state.</summary>
public sealed partial class MainViewModel
{
    public static IReadOnlyList<string> ViewModes { get; } = ["Cards", "Large", "Compact", "List", "Details"];

    /// <summary>How the profile library is laid out: Cards, Large, Compact, List or Details.</summary>
    public string ViewMode
    {
        get => ViewModes.Contains(_settings.ProfilesViewMode) ? _settings.ProfilesViewMode : ViewModes[0];
        set
        {
            if (!ViewModes.Contains(value) || _settings.ProfilesViewMode == value) return;
            _settings.ProfilesViewMode = value;
            SaveSettings();
            Raise();
            foreach (var name in new[] { nameof(IsViewCards), nameof(IsViewLarge), nameof(IsViewCompact), nameof(IsViewList), nameof(IsViewDetails) })
            {
                Raise(name);
            }
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // Two-way bool views of ViewMode so RadioButtons work with the mouse, keyboard arrows and UI Automation.
    public bool IsViewCards { get => ViewMode == "Cards"; set { if (value) ViewMode = "Cards"; } }

    public bool IsViewLarge { get => ViewMode == "Large"; set { if (value) ViewMode = "Large"; } }

    public bool IsViewCompact { get => ViewMode == "Compact"; set { if (value) ViewMode = "Compact"; } }

    public bool IsViewList { get => ViewMode == "List"; set { if (value) ViewMode = "List"; } }

    public bool IsViewDetails { get => ViewMode == "Details"; set { if (value) ViewMode = "Details"; } }

    public bool IsSidebarCollapsed => _settings.IsLeftPanelCollapsed || _sidebarForcedCompact;

    public bool IsSidebarExpanded => !IsSidebarCollapsed;

    public bool IsSidebarToggleAvailable => !_sidebarForcedCompact;

    public string SidebarToggleGlyph => IsSidebarCollapsed ? "" : "";

    public string SidebarToggleLabel => T(IsSidebarCollapsed ? "ExpandSidebar" : "CollapseSidebar");

    /// <summary>Narrow windows force the icon-only sidebar without touching the saved preference.</summary>
    public void SetSidebarForcedCompact(bool forced)
    {
        if (_sidebarForcedCompact == forced) return;
        _sidebarForcedCompact = forced;
        RaiseSidebar();
    }

    private void RaiseSidebar()
    {
        Raise(nameof(IsSidebarCollapsed));
        Raise(nameof(IsSidebarExpanded));
        Raise(nameof(IsSidebarToggleAvailable));
        Raise(nameof(SidebarToggleGlyph));
        Raise(nameof(SidebarToggleLabel));
        RefreshSettingsText();
        RefreshDisplays();
    }

    public string MaximizeGlyph => _isMaximized ? "" : "";

    public string MaximizeTooltip => T(_isMaximized ? "Restore" : "Maximize");

    public string LeftPanelTooltip => T(_isLeftPanelVisible ? "HideNavigationPanel" : "ShowNavigationPanel");

    public string RightPanelTooltip => T(_isRightPanelVisible ? "HideColorPanel" : "ShowColorPanel");

    /// <summary>Called by the window whenever chrome state changes so tooltips and glyphs stay in sync.</summary>
    public void UpdateWindowState(bool isMaximized, bool isLeftPanelVisible, bool isRightPanelVisible)
    {
        _isMaximized = isMaximized;
        _isLeftPanelVisible = isLeftPanelVisible;
        _isRightPanelVisible = isRightPanelVisible;
        Raise(nameof(MaximizeGlyph));
        Raise(nameof(MaximizeTooltip));
        Raise(nameof(LeftPanelTooltip));
        Raise(nameof(RightPanelTooltip));
    }

    public void SavePanelPreferences() => SaveSettings();
}
