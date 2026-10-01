using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>What to do when the user closes the window.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The schedule, app rules and profile shortcuts only act while the process is alive.</summary>
    private bool HasBackgroundAutomation =>
        (_settings.Schedule.Enabled && _settings.Schedule.EnsureEntries().Count > 0)
        || _settings.AppRules.Count > 0
        || _settings.ProfileHotkeys.Count > 0;

    /// <summary>
    /// Hide to the tray when that is enabled. The first time the window is closed with automation configured,
    /// ask whether to keep running in the background instead of silently stopping it.
    /// </summary>
    public CloseDecision DecideOnClose()
    {
        if (MinimizeToTray) return CloseDecision.Hide;
        if (_settings.CloseChoiceAsked || !HasBackgroundAutomation) return CloseDecision.Exit;

        var choice = _shell.AskCloseChoice(T("CloseChoiceTitle"), T("CloseChoiceMessage"));
        if (choice == CloseChoice.Cancel) return CloseDecision.Stay;

        _settings.CloseChoiceAsked = true;
        if (choice == CloseChoice.MinimizeToTray)
        {
            MinimizeToTray = true;
            return CloseDecision.Hide;
        }

        SaveSettings();
        return CloseDecision.Exit;
    }
}
