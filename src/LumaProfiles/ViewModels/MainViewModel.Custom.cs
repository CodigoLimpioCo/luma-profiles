using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>Profiles the user saves from their own adjustments, next to the built-in catalog.</summary>
public sealed partial class MainViewModel
{
    public const string CustomCategory = "Personalizados";

    private string _newProfileName = string.Empty;

    public string NewProfileName { get => _newProfileName; set => Set(ref _newProfileName, value); }

    public bool SelectedProfileIsCustom => SelectedProfile.IsCustom;

    public bool SelectedProfileIsBuiltIn => !SelectedProfile.IsCustom;

    /// <summary>Saves the values currently shown in the adjustment panel as a new profile of your own.</summary>
    private void SaveAsNewProfile()
    {
        var source = SelectedProfile;
        var custom = new DisplayProfile
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = UniqueCustomName(string.IsNullOrWhiteSpace(NewProfileName) ? L("CustomProfileDefaultName", source.DisplayName) : NewProfileName.Trim()),
            Category = CustomCategory,
            Description = T("CustomProfileDescription"),
            PreviewStart = source.PreviewStart,
            PreviewEnd = source.PreviewEnd,
            PowerPlan = source.PowerPlan,
            IsHdr = source.IsHdr,
            IsCustom = true,
        };
        custom.CopyAdjustmentsFrom(source);

        Profiles.Add(custom);
        SaveProfiles();
        NewProfileName = string.Empty;
        RefreshProfileCatalog();
        SelectedProfile = custom;
        StatusMessage = L("CustomProfileSaved", custom.Name);
    }

    private string UniqueCustomName(string name)
    {
        var candidate = name;
        for (var number = 2; Profiles.Any(profile => profile.IsCustom && profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); number++)
        {
            candidate = $"{name} ({number})";
        }

        return candidate;
    }

    private void DeleteCustomProfile()
    {
        var profile = SelectedProfile;
        if (!profile.IsCustom) return;
        if (!_shell.Confirm(T("DeleteCustomProfile"), L("DeleteCustomProfileConfirm", profile.Name))) return;

        Profiles.Remove(profile);
        RemoveAutomationReferencesTo(profile.Id);
        SaveProfiles();
        SelectedProfile = Profiles[0];
        RefreshProfileCatalog();
        StatusMessage = L("CustomProfileDeleted", profile.Name);
    }

    /// <summary>A deleted profile can no longer be applied, so the schedule, rules and shortcuts that named it go too.</summary>
    private void RemoveAutomationReferencesTo(string profileId)
    {
        bool Matches(string id) => id.Equals(profileId, StringComparison.OrdinalIgnoreCase);

        _settings.Schedule.EnsureEntries().RemoveAll(entry => Matches(entry.ProfileId));
        _settings.AppRules.RemoveAll(rule => Matches(rule.ProfileId));
        _settings.ProfileHotkeys.RemoveAll(hotkey => Matches(hotkey.ProfileId));
        SaveSettings();
        _ruleEngine.Reset();
        RefreshAutomationLists();
        RestartSchedule();
    }

    /// <summary>Rebuilds everything derived from the list of profiles after one is added, removed or imported.</summary>
    private void RefreshProfileCatalog()
    {
        LocalizationService.LocalizeProfiles(Profiles, _selectedLanguage.Code);
        BuildFilterChips();
        RefreshVisibleProfiles();
        Raise(nameof(ProfileCountSubtitle));
        Raise(nameof(Favorites));
    }
}
