using System.Collections.ObjectModel;
using System.Windows.Input;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>One "What are you going to do?" choice above the library.</summary>
public sealed class IntentChoice : ObservableObject
{
    private string _label;
    private int _count;
    private bool _isSelected;

    public IntentChoice(string key, string icon, string label) => (Key, Icon, _label) = (key, icon, label);

    public string Key { get; }
    public string Icon { get; }
    public string Label { get => _label; set => Set(ref _label, value); }
    public int Count { get => _count; set => Set(ref _count, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>
/// Finding a mode among dozens: activity shortcuts that narrow the library, and the most recently applied
/// profiles one click away.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxRecent = 6;

    private static readonly Dictionary<string, string> IntentIcons = new()
    {
        [ProfileIntents.All] = "", ["Play"] = "", ["Watch"] = "", ["Work"] = "",
        ["Read"] = "", ["Night"] = "", ["Create"] = "",
    };

    private string _intent = ProfileIntents.All;

    public ObservableCollection<IntentChoice> IntentChoices { get; } = [];
    public ObservableCollection<DisplayProfile> RecentProfiles { get; } = [];

    public ICommand SelectIntentCommand { get; private set; } = null!;
    public ICommand ApplyRecentCommand { get; private set; } = null!;

    public bool HasRecentProfiles => RecentProfiles.Count > 0;

    /// <summary>The activity shown, "All" for the whole library.</summary>
    public string SelectedIntent
    {
        get => _intent;
        set
        {
            var key = ProfileIntents.Keys.Contains(value) ? value : ProfileIntents.All;
            if (!Set(ref _intent, key)) return;
            foreach (var choice in IntentChoices) choice.IsSelected = choice.Key == key;
            if (key != ProfileIntents.All) IsSettingsOpen = false;
            OnFiltersChanged();
            if (key != ProfileIntents.All) StatusMessage = L("IntentStatus", T("Intent" + key), VisibleProfiles.Count);
        }
    }

    private void InitializeDiscovery()
    {
        SelectIntentCommand = new RelayCommand<string>(key => SelectedIntent = key == SelectedIntent ? ProfileIntents.All : key);
        ApplyRecentCommand = new RelayCommand<DisplayProfile>(profile =>
        {
            SelectedProfile = profile;
            _ = EnqueueUserTriggered(profile);
        }, () => !IsBusy);
        BuildIntentChoices();
        RefreshRecentProfiles();
    }

    private void BuildIntentChoices()
    {
        IntentChoices.Clear();
        foreach (var key in ProfileIntents.Keys)
        {
            IntentChoices.Add(new IntentChoice(key, IntentIcons[key], T("Intent" + key)) { IsSelected = key == _intent });
        }

        RefreshIntentCounts();
    }

    private void RefreshIntentText()
    {
        foreach (var choice in IntentChoices) choice.Label = T("Intent" + choice.Key);
    }

    private void RefreshIntentCounts()
    {
        foreach (var choice in IntentChoices) choice.Count = Profiles.Count(profile => ProfileIntents.Matches(choice.Key, profile));
    }

    /// <summary>For the default order of an activity: its suggested profile first, then favorites.</summary>
    private IEnumerable<DisplayProfile> IntentOrder(IEnumerable<DisplayProfile> profiles)
    {
        if (_intent == ProfileIntents.All) return profiles;
        var suggested = ProfileIntents.Suggested(_intent);
        return profiles
            .OrderByDescending(profile => profile.Id.Equals(suggested, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(profile => profile.IsFavorite);
    }

    private void RememberRecent(DisplayProfile profile)
    {
        var recent = _settings.RecentProfileIds;
        recent.RemoveAll(id => id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, profile.Id);
        if (recent.Count > MaxRecent) recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
        SaveSettings();
        RefreshRecentProfiles();
    }

    private void RefreshRecentProfiles()
    {
        RecentProfiles.Clear();
        foreach (var id in _settings.RecentProfileIds)
        {
            if (FindProfile(id) is { } profile) RecentProfiles.Add(profile);
        }

        Raise(nameof(HasRecentProfiles));
    }
}
