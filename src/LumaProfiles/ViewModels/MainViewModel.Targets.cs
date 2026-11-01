using System.Collections.ObjectModel;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>
/// One profile per display: every display keeps its own saved correction, automation can name the displays it
/// changes, and the header shows what each display is running.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The value of the "follow the selector" choice in automation forms (stored as null).</summary>
    public const string FollowSelectorTarget = "";

    private string _newRuleTarget = FollowSelectorTarget;
    private string _newScheduleTarget = FollowSelectorTarget;
    private string _newHotkeyTarget = FollowSelectorTarget;

    /// <summary>Choices for the "Displays" field of schedule slots, rules and shortcuts.</summary>
    public ObservableCollection<LocalizedOption> AutomationTargetOptions { get; } = [];

    public string NewRuleTarget { get => _newRuleTarget; set => Set(ref _newRuleTarget, value ?? FollowSelectorTarget); }
    public string NewScheduleTarget { get => _newScheduleTarget; set => Set(ref _newScheduleTarget, value ?? FollowSelectorTarget); }
    public string NewHotkeyTarget { get => _newHotkeyTarget; set => Set(ref _newHotkeyTarget, value ?? FollowSelectorTarget); }

    /// <summary>"1: Natural · 2: Shooter táctico (HDR)"; empty with a single display.</summary>
    public string ActiveByDisplaySummary
    {
        get
        {
            if (_lastDisplays.Count < 2) return string.Empty;
            return string.Join("   ·   ", _lastDisplays.Select(display =>
                $"{display.Number}: {ActiveProfileNameOn(display) ?? T("DisplayNoProfile")}{(display.IsHdrActive ? " (HDR)" : string.Empty)}"));
        }
    }

    public bool HasActiveByDisplaySummary => ActiveByDisplaySummary.Length > 0;

    private static string? StoredTarget(string target) => string.IsNullOrWhiteSpace(target) ? null : target;

    /// <summary>The displays an automation item changes: its own choice, or the selector when it has none.</summary>
    private string ResolveTarget(string? target) => string.IsNullOrWhiteSpace(target) ? SelectedMonitorTarget : target;

    private MonitorColorCorrection? CorrectionFor(DisplayInfo display) =>
        _settings.MonitorCorrections.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.MonitorId) && item.MonitorId.Equals(display.MonitorId, StringComparison.OrdinalIgnoreCase))
        ?? _settings.MonitorCorrections.FirstOrDefault(item => item.DeviceName.Equals(display.DeviceName, StringComparison.OrdinalIgnoreCase));

    private string? ActiveProfileNameOn(DisplayInfo display) =>
        CorrectionFor(display) is { } correction ? FindProfile(correction.ProfileId)?.DisplayName : null;

    /// <summary>
    /// Marks as active every profile that some connected display is running (with no display list, every saved
    /// correction counts).
    /// </summary>
    private void RefreshActiveFlags()
    {
        var ids = _lastDisplays.Count == 0
            ? _settings.MonitorCorrections.Select(item => item.ProfileId)
            : _lastDisplays.Select(CorrectionFor).Where(item => item is not null).Select(item => item!.ProfileId);
        var active = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Profiles) profile.IsActive = active.Contains(profile.Id);
        RaiseDisplayState();
    }

    private void RaiseDisplayState()
    {
        foreach (var button in DisplayButtons) button.Tooltip = DisplayTooltip(button.Display);
        Raise(nameof(ActiveByDisplaySummary));
        Raise(nameof(HasActiveByDisplaySummary));
        Raise(nameof(AnyDisplayHdr));
    }

    private void RefreshTargetOptions()
    {
        var keep = (_newRuleTarget, _newScheduleTarget, _newHotkeyTarget);
        AutomationTargetOptions.Clear();
        AutomationTargetOptions.Add(new LocalizedOption(FollowSelectorTarget, T("TargetFollowSelector")));
        AutomationTargetOptions.Add(new LocalizedOption(BothDisplays, T("MonitorAll")));
        foreach (var display in _lastDisplays)
        {
            AutomationTargetOptions.Add(new LocalizedOption(DisplayTargetPrefix + display.Number, L("TargetDisplay", display.Number)));
        }

        // Clearing the list made the ComboBoxes write null back; put the choices back.
        (_newRuleTarget, _newScheduleTarget, _newHotkeyTarget) = keep;
        Raise(nameof(NewRuleTarget));
        Raise(nameof(NewScheduleTarget));
        Raise(nameof(NewHotkeyTarget));
    }

    /// <summary>" · Pantalla 2" for list rows; empty when the item follows the selector.</summary>
    private string TargetSuffix(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        if (target == BothDisplays) return "  ·  " + T("MonitorAll");
        var numbers = ParseTarget(target);
        return numbers is null ? string.Empty : "  ·  " + string.Join(", ", numbers.Order().Select(number => L("TargetDisplay", number)));
    }
}
