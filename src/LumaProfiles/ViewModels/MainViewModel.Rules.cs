using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>
/// Automation: per-application rules, game mode (any full-screen app) and the battery profile.
/// Priority: an explicit app rule, then game mode, then battery, then the schedule.
/// </summary>
public sealed partial class MainViewModel
{
    private const string GameModeProcess = "(fullscreen)";

    private readonly AppProfileRule _gameModeRule = new() { ProcessName = GameModeProcess };
    private bool _onBattery;
    private bool _batteryApplied;
    private string? _profileBeforeBattery;

    public string NewRuleProcess { get => _newRuleProcess; set => Set(ref _newRuleProcess, value); }

    public string? NewRuleProfileId { get => _newRuleProfileId; set => Set(ref _newRuleProfileId, value); }

    // ---- per-application rules and game mode --------------------------------------------------

    public void OnForegroundProcessChanged(string? processName) => OnForegroundProcessChanged(processName, isFullscreen: false);

    public void OnForegroundProcessChanged(string? processName, bool isFullscreen)
    {
        var gameRule = ActiveGameModeRule;
        if (_settings.AppRules.Count == 0 && gameRule is null && _ruleEngine.ActiveRule is null) return;

        var decision = _ruleEngine.Evaluate(processName, _settings.AppRules, isFullscreen, gameRule);
        switch (decision.Action)
        {
            case RuleAction.Apply when FindProfile(decision.Rule!.ProfileId) is { } profile:
                _profileBeforeRule ??= Profiles.FirstOrDefault(item => item.IsActive)?.Id;
                var ruleStatus = ReferenceEquals(decision.Rule, _gameModeRule)
                    ? L("GameModeApplied", AppRuleEngine.Normalize(processName ?? string.Empty), profile.DisplayName)
                    : L("RuleApplied", decision.Rule.ProcessName, profile.DisplayName);
                var target = decision.Rule.Target;
                _ = EnqueueAutomation(() => ApplyAsync(profile, ruleStatus, target));
                break;
            case RuleAction.Apply:
                _ruleEngine.Reset();
                break;
            case RuleAction.Restore:
                _ = EnqueueAutomation(RestoreAfterRuleAsync);
                break;
        }
    }

    /// <summary>The synthetic rule game mode feeds the engine; null while game mode is off.</summary>
    private AppProfileRule? ActiveGameModeRule
    {
        get
        {
            if (!_settings.GameModeEnabled || string.IsNullOrWhiteSpace(_settings.GameModeProfileId)) return null;
            _gameModeRule.ProfileId = _settings.GameModeProfileId;
            return _gameModeRule;
        }
    }

    public bool GameModeEnabled
    {
        get => _settings.GameModeEnabled;
        set
        {
            if (_settings.GameModeEnabled == value) return;
            _settings.GameModeEnabled = value;
            SaveSettings();
            Raise();
            if (!value && ReferenceEquals(_ruleEngine.ActiveRule, _gameModeRule))
            {
                _ruleEngine.Reset();
                _ = EnqueueAutomation(RestoreAfterRuleAsync);
            }
        }
    }

    public string? GameModeProfileId
    {
        get => _settings.GameModeProfileId;
        set
        {
            if (value is null || _settings.GameModeProfileId == value) return;
            _settings.GameModeProfileId = value;
            SaveSettings();
            Raise();
        }
    }

    private void AddAppRule()
    {
        var process = AppRuleEngine.Normalize(NewRuleProcess ?? string.Empty);
        if (process.Length == 0 || string.IsNullOrWhiteSpace(NewRuleProfileId)) return;

        _settings.AppRules.RemoveAll(rule => AppRuleEngine.Normalize(rule.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase));
        _settings.AppRules.Add(new AppProfileRule { ProcessName = process, ProfileId = NewRuleProfileId, Target = StoredTarget(NewRuleTarget) });
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
            AppRules.Add(new AppRuleItem(rule, name, TargetSuffix(rule.Target)));
        }
    }

    // ---- battery ---------------------------------------------------------------------------

    public bool BatteryProfileEnabled
    {
        get => _settings.BatteryProfileEnabled;
        set
        {
            if (_settings.BatteryProfileEnabled == value) return;
            _settings.BatteryProfileEnabled = value;
            SaveSettings();
            Raise();
            if (value) EvaluateBattery(); else LeaveBattery();
        }
    }

    public string? BatteryProfileId
    {
        get => _settings.BatteryProfileId;
        set
        {
            if (value is null || _settings.BatteryProfileId == value) return;
            _settings.BatteryProfileId = value;
            SaveSettings();
            Raise();
            if (_batteryApplied && FindProfile(value) is { } profile)
            {
                _ = EnqueueAutomation(() => ApplyAsync(profile, L("BatteryApplied", profile.DisplayName)));
            }
        }
    }

    /// <summary>"On battery" / "On AC power" / nothing on a PC without a battery.</summary>
    public string PowerSourceText => _powerKnown ? T(_onBattery ? "PowerOnBattery" : "PowerOnAc") : T("PowerNoBattery");

    private bool _powerKnown;

    private bool BatteryProfileWanted =>
        _settings.BatteryProfileEnabled && _onBattery &&
        !string.IsNullOrWhiteSpace(_settings.BatteryProfileId) && FindProfile(_settings.BatteryProfileId) is not null;

    /// <summary>Called at start-up and whenever the PC is plugged in or unplugged.</summary>
    public void OnPowerSourceChanged(bool onBattery)
    {
        _powerKnown = true;
        _onBattery = onBattery;
        Raise(nameof(PowerSourceText));
        if (onBattery) EvaluateBattery(); else LeaveBattery();
    }

    private void EvaluateBattery()
    {
        // A rule in charge hands over to the battery profile when it ends (see RestoreAfterRuleAsync).
        if (!BatteryProfileWanted || _batteryApplied || _ruleEngine.ActiveRule is not null) return;
        var profile = FindProfile(_settings.BatteryProfileId!)!;
        _batteryApplied = true;
        _profileBeforeBattery = Profiles.FirstOrDefault(item => item.IsActive)?.Id;
        _ = EnqueueAutomation(() => ApplyAsync(profile, L("BatteryApplied", profile.DisplayName)));
    }

    private void LeaveBattery()
    {
        if (!_batteryApplied) return;
        _batteryApplied = false;
        var previous = _profileBeforeBattery;
        _profileBeforeBattery = null;

        // The user picked something else meanwhile, or a rule is in charge: leave it alone.
        var batteryStillShowing = FindProfile(_settings.BatteryProfileId ?? string.Empty)?.IsActive == true;
        if (!batteryStillShowing || _ruleEngine.ActiveRule is not null) return;

        if (_settings.Schedule.Enabled && ScheduleResolver.Resolve(_settings.Schedule, DateTime.Now) is not null)
        {
            RestartSchedule();
            return;
        }

        if (previous is not null && FindProfile(previous) is { } profile)
        {
            _ = EnqueueAutomation(() => ApplyAsync(profile, L("BatteryRestored", profile.DisplayName)));
        }
        else
        {
            _ = EnqueueAutomation(NeutralizeAsync);
        }
    }
}
