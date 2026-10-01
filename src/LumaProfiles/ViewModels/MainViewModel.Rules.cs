using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;


namespace LumaProfiles.ViewModels;

/// <summary>Automation: per-application rules (the schedule lives in the Schedule partial).</summary>
public sealed partial class MainViewModel
{
    public string NewRuleProcess { get => _newRuleProcess; set => Set(ref _newRuleProcess, value); }

    public string? NewRuleProfileId { get => _newRuleProfileId; set => Set(ref _newRuleProfileId, value); }

    public void OnForegroundProcessChanged(string? processName)
    {
        if (_settings.AppRules.Count == 0 && _ruleEngine.ActiveRule is null) return;

        var decision = _ruleEngine.Evaluate(processName, _settings.AppRules);
        switch (decision.Action)
        {
            case RuleAction.Apply when FindProfile(decision.Rule!.ProfileId) is { } profile:
                _profileBeforeRule ??= Profiles.FirstOrDefault(item => item.IsActive)?.Id;
                var ruleStatus = L("RuleApplied", decision.Rule.ProcessName, profile.DisplayName);
                _ = EnqueueAutomation(() => ApplyAsync(profile, ruleStatus));
                break;
            case RuleAction.Apply:
                _ruleEngine.Reset();
                break;
            case RuleAction.Restore:
                _ = EnqueueAutomation(RestoreAfterRuleAsync);
                break;
        }
    }

    private void AddAppRule()
    {
        var process = AppRuleEngine.Normalize(NewRuleProcess ?? string.Empty);
        if (process.Length == 0 || string.IsNullOrWhiteSpace(NewRuleProfileId)) return;

        _settings.AppRules.RemoveAll(rule => AppRuleEngine.Normalize(rule.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase));
        _settings.AppRules.Add(new AppProfileRule { ProcessName = process, ProfileId = NewRuleProfileId });
        SaveSettings();
        NewRuleProcess = string.Empty;
        _ruleEngine.Reset();
        RefreshAppRules();
    }

    private void RemoveAppRule(AppRuleItem item)
    {
        _settings.AppRules.Remove(item.Rule);
        SaveSettings();
        if (ReferenceEquals(_ruleEngine.ActiveRule, item.Rule)) _ = EnqueueAutomation(RestoreAfterRuleAsync);
        _ruleEngine.Reset();
        RefreshAppRules();
    }

    private void RefreshAppRules()
    {
        AppRules.Clear();
        foreach (var rule in _settings.AppRules)
        {
            var name = FindProfile(rule.ProfileId)?.DisplayName ?? rule.ProfileId;
            AppRules.Add(new AppRuleItem(rule, name));
        }
    }
}
