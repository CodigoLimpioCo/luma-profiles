using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;


namespace LumaProfiles.ViewModels;

/// <summary>Automation: per-application rules, the day/night schedule and profile cycling.</summary>
public sealed partial class MainViewModel
{
    public bool ScheduleEnabled
    {
        get => _settings.Schedule.Enabled;
        set
        {
            if (_settings.Schedule.Enabled == value) return;
            _settings.Schedule.Enabled = value;
            SaveSettings();
            Raise();
            RestartSchedule();
        }
    }

    public string? ScheduleDayProfileId
    {
        get => _settings.Schedule.DayProfileId;
        set
        {
            if (value is null || _settings.Schedule.DayProfileId == value) return;
            _settings.Schedule.DayProfileId = value;
            SaveSettings();
            Raise();
            RestartSchedule();
        }
    }

    public string? ScheduleNightProfileId
    {
        get => _settings.Schedule.NightProfileId;
        set
        {
            if (value is null || _settings.Schedule.NightProfileId == value) return;
            _settings.Schedule.NightProfileId = value;
            SaveSettings();
            Raise();
            RestartSchedule();
        }
    }

    public string ScheduleDayStart
    {
        get => _settings.Schedule.DayStart;
        set => SetScheduleTime(value, time => _settings.Schedule.DayStart = time, nameof(ScheduleDayStart));
    }

    public string ScheduleNightStart
    {
        get => _settings.Schedule.NightStart;
        set => SetScheduleTime(value, time => _settings.Schedule.NightStart = time, nameof(ScheduleNightStart));
    }

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

    private void SetScheduleTime(string value, Action<string> assign, string propertyName)
    {
        if (!ScheduleResolver.TryParseTime(value, out var time))
        {
            StatusMessage = T("ScheduleInvalidTime");
            Raise(propertyName);
            return;
        }

        assign(time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        SaveSettings();
        Raise(propertyName);
        RestartSchedule();
    }

    private void RestartSchedule()
    {
        _lastScheduleSlot = null;
        EvaluateSchedule();
    }

    /// <summary>Applies the day/night profile once per boundary crossing so manual changes are not overridden.</summary>
    internal void EvaluateSchedule()
    {
        if (!_settings.Schedule.Enabled || _ruleEngine.ActiveRule is not null) return;

        var slot = ScheduleResolver.Resolve(_settings.Schedule, TimeOnly.FromDateTime(DateTime.Now));
        if (slot == _lastScheduleSlot) return;
        _lastScheduleSlot = slot;

        if (FindProfile(ScheduleResolver.ProfileFor(_settings.Schedule, slot)) is not { } profile) return;
        var scheduleStatus = L("ScheduleApplied", profile.DisplayName);
        _ = EnqueueAutomation(() => ApplyAsync(profile, scheduleStatus));
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
