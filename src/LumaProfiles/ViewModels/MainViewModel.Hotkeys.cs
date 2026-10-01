using System.Collections.ObjectModel;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

public sealed record ProfileHotkeyItem(ProfileHotkey Hotkey, string ProfileName)
{
    public string Shortcut => "Ctrl+Alt+" + Hotkey.Key;
}

/// <summary>Per-profile global shortcuts (Ctrl+Alt plus a digit or function key).</summary>
public sealed partial class MainViewModel
{
    private string _newHotkeyKey = HotkeyService.AvailableKeys[0];
    private string? _newHotkeyProfileId;

    public ObservableCollection<ProfileHotkeyItem> ProfileHotkeys { get; } = [];

    public IReadOnlyList<string> HotkeyKeyOptions => HotkeyService.AvailableKeys;

    /// <summary>The shortcuts the window registers; raised whenever the list changes so it can register them again.</summary>
    public IReadOnlyList<ProfileHotkey> ProfileHotkeyBindings => _settings.ProfileHotkeys;

    public string NewHotkeyKey { get => _newHotkeyKey; set => Set(ref _newHotkeyKey, value); }

    public string? NewHotkeyProfileId { get => _newHotkeyProfileId; set => Set(ref _newHotkeyProfileId, value); }

    private void AddProfileHotkey()
    {
        if (string.IsNullOrWhiteSpace(NewHotkeyProfileId) || !HotkeyService.TryParseKey(NewHotkeyKey, out _)) return;

        // One shortcut per key and one per profile: the newest assignment wins.
        _settings.ProfileHotkeys.RemoveAll(hotkey =>
            hotkey.Key == NewHotkeyKey || hotkey.ProfileId.Equals(NewHotkeyProfileId, StringComparison.OrdinalIgnoreCase));
        _settings.ProfileHotkeys.Add(new ProfileHotkey { ProfileId = NewHotkeyProfileId, Key = NewHotkeyKey });
        CommitProfileHotkeys();
    }

    private void RemoveProfileHotkey(ProfileHotkeyItem item)
    {
        _settings.ProfileHotkeys.Remove(item.Hotkey);
        CommitProfileHotkeys();
    }

    private void CommitProfileHotkeys()
    {
        SaveSettings();
        RefreshProfileHotkeys();
        Raise(nameof(ProfileHotkeyBindings));
    }

    private void RefreshProfileHotkeys()
    {
        ProfileHotkeys.Clear();
        foreach (var hotkey in _settings.ProfileHotkeys.OrderBy(item => HotkeyService.AvailableKeys.ToList().IndexOf(item.Key)))
        {
            var name = FindProfile(hotkey.ProfileId)?.DisplayName ?? hotkey.ProfileId;
            ProfileHotkeys.Add(new ProfileHotkeyItem(hotkey, name));
        }
    }

    /// <summary>Re-reads every list in the automation settings after profiles or the language changed.</summary>
    private void RefreshAutomationLists()
    {
        RefreshAppRules();
        RefreshScheduleEntries();
        RefreshProfileHotkeys();
        Raise(nameof(ProfileHotkeyBindings));
    }
}
