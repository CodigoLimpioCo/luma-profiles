using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

public sealed record AppRuleItem(AppProfileRule Rule, string ProfileName)
{
    public string ProcessName => Rule.ProcessName;
}

public sealed partial class MainViewModel : ObservableObject
{
    private const string BothDisplays = MonitorService.AllDisplaysTarget;

    private readonly IMonitorService _monitor;
    private readonly ProfileStore _profileStore;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly IShellService _shell;
    private readonly ApplicationSettings _settings;
    private readonly AppRuleEngine _ruleEngine = new("LumaProfiles");
    private readonly DispatcherTimer _livePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly DispatcherTimer _reapplyTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _scheduleTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    private DisplayProfile _selectedProfile;
    private LanguageOption _selectedLanguage;
    private string _selectedMonitorTarget;
    private string _statusMessage = string.Empty;
    private bool _isDarkTheme;
    private bool _isLivePreviewEnabled;
    private bool _isAboutOpen;
    private bool _isSettingsOpen;
    private bool _startWithWindows;
    private bool _isReapplyingCorrections;
    private bool _reapplyRequested;
    private bool _sidebarForcedCompact;
    private bool _isMaximized;
    private bool _isLeftPanelVisible;
    private bool _isRightPanelVisible;
    private ScheduleSlot? _lastScheduleSlot;
    private string? _profileBeforeRule;
    private string _newRuleProcess = string.Empty;
    private string? _newRuleProfileId;

    public MainViewModel(
        IMonitorService monitor,
        ProfileStore profileStore,
        ApplicationSettingsStore settingsStore,
        IShellService shell,
        Func<bool>? systemPrefersDark = null,
        IWorkRunner? workRunner = null,
        IReadOnlyList<string>? availableFonts = null)
    {
        if (workRunner is not null) _runner = workRunner;
        _availableFonts = availableFonts is { Count: > 0 } ? availableFonts : FontCatalog.Installed();
        if (systemPrefersDark is not null) _systemPrefersDark = systemPrefersDark;
        _monitor = monitor;
        _profileStore = profileStore;
        _settingsStore = settingsStore;
        _shell = shell;

        _settings = settingsStore.Load();
        Profiles = new ObservableCollection<DisplayProfile>(profileStore.Load());
        Languages = LocalizationService.DiscoverLanguages();
        _selectedLanguage = Languages.FirstOrDefault(item => item.Code.Equals(_settings.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(item => item.Code == "es")
            ?? Languages.FirstOrDefault()
            ?? new LanguageOption("es", "Español", string.Empty);
        _isDarkTheme = ComputeDarkTheme();
        _startWithWindows = _settings.StartWithWindows;
        _selectedMonitorTarget = _settings.SelectedMonitorTarget ?? BothDisplays;
        _selectedProfile = Profiles.First();
        _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;

        LocalizationService.LocalizeProfiles(Profiles, _selectedLanguage.Code);
        RefreshLocalizedOptions();
        InitializeFilters();
        RefreshVisibleProfiles();
        RefreshAppRules();

        ApplyProfileCommand = new RelayCommand<DisplayProfile>(profile =>
        {
            SelectedProfile = profile;
            var detail = L("ConfirmDetailApply", profile.DisplayName);
            _ = Enqueue(() => RunWithConfirmationAsync(() => ApplyAsync(profile), detail, L("Applying", profile.DisplayName)));
        }, () => !IsBusy);
        EditProfileCommand = new RelayCommand<DisplayProfile>(profile =>
        {
            SelectedProfile = profile;
            StatusMessage = L("Editing", profile.DisplayName);
            InspectorRequested?.Invoke(this, EventArgs.Empty);
        });
        ToggleFavoriteCommand = new RelayCommand<DisplayProfile>(ToggleFavorite);
        SaveAndApplyCommand = new RelayCommand(() =>
        {
            SaveProfiles();
            var profile = SelectedProfile;
            var detail = L("ConfirmDetailApply", profile.DisplayName);
            _ = Enqueue(() => RunWithConfirmationAsync(() => ApplyAsync(profile), detail, L("Applying", profile.DisplayName)));
        }, () => !IsBusy);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        RepairCommand = new RelayCommand(() => _ = Enqueue(() => RunWithConfirmationAsync(NeutralizeAsync, T("ConfirmDetailNeutralize"), T("Neutralizing"))), () => !IsBusy);
        RestoreOriginalCommand = new RelayCommand(() => _ = Enqueue(() => RunWithConfirmationAsync(RestoreOriginalAsync, T("ConfirmDetailRestore"), T("RestoringOriginalState"))), () => !IsBusy);
        SelectCategoryCommand = new RelayCommand<string>(SelectCategory);
        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        OpenAboutCommand = new RelayCommand(() => IsAboutOpen = true);
        CloseAboutCommand = new RelayCommand(() => IsAboutOpen = false);
        CloseOverlaysCommand = new RelayCommand(() => { IsAboutOpen = false; IsSettingsOpen = false; });
        ToggleSidebarCollapseCommand = new RelayCommand(() =>
        {
            SidebarCollapsedPreference = !SidebarCollapsedPreference;
        });
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        OpenUrlCommand = new RelayCommand<string>(url => _shell.OpenUrl(url));
        OpenHdrSettingsCommand = new RelayCommand(() => _shell.OpenUrl("ms-settings:display"));
        ExportProfilesCommand = new RelayCommand(ExportProfiles);
        ImportProfilesCommand = new RelayCommand(ImportProfiles);
        AddAppRuleCommand = new RelayCommand(AddAppRule);
        RemoveAppRuleCommand = new RelayCommand<AppRuleItem>(RemoveAppRule);

        _monitor.UseMonitorControls = _settings.UseMonitorControls;
        _monitor.UseOriginalStates(_settings.OriginalMonitorStates);
        InitializeConfirmation();
        InitializeSettingsPage();
        InitializeDisplays();
        StatusMessage = T("Ready");
        _livePreviewTimer.Tick += (_, _) => LivePreviewTick();
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            _ = Enqueue(ReapplyPersistentCorrectionsAsync, visible: false);
        };
        _scheduleTimer.Tick += (_, _) => EvaluateSchedule();
    }

    public event EventHandler? ScrollToTopRequested;

    /// <summary>Raised when the user asks to adjust a profile so the view can reveal the adjustment panel.</summary>
    public event EventHandler? InspectorRequested;

    public ObservableCollection<DisplayProfile> Profiles { get; }
    public ObservableCollection<DisplayProfile> VisibleProfiles { get; } = [];
    public IReadOnlyList<LanguageOption> Languages { get; }
    public ObservableCollection<LocalizedOption> ColorTemperatureOptions { get; } = [];
    public ObservableCollection<AppRuleItem> AppRules { get; } = [];

    public ICommand ApplyProfileCommand { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand SaveAndApplyCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand RestoreOriginalCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand CloseAboutCommand { get; }
    public ICommand CloseOverlaysCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ToggleSidebarCollapseCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand OpenHdrSettingsCommand { get; }
    public ICommand ExportProfilesCommand { get; }
    public ICommand ImportProfilesCommand { get; }
    public ICommand AddAppRuleCommand { get; }
    public ICommand RemoveAppRuleCommand { get; }

    public DisplayProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value)) return;
            _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            _selectedProfile = value;
            _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
            Raise();
            ScheduleLivePreview();
        }
    }

    public bool IsLivePreviewEnabled
    {
        get => _isLivePreviewEnabled;
        set
        {
            if (!Set(ref _isLivePreviewEnabled, value)) return;
            StatusMessage = T(value ? "LivePreviewOn" : "LivePreviewOff");
            if (value) ScheduleLivePreview(); else _livePreviewTimer.Stop();
        }
    }

    public bool IsAboutOpen { get => _isAboutOpen; private set => Set(ref _isAboutOpen, value); }

    public bool IsSettingsOpen { get => _isSettingsOpen; private set => Set(ref _isSettingsOpen, value); }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            if (!_settingsStore.SetStartupEnabled(value))
            {
                StatusMessage = T("StartupChangeFailed");
                Raise();
                return;
            }

            _startWithWindows = value;
            _settings.StartWithWindows = value;
            SaveSettings();
            Raise();
            StatusMessage = T(value ? "StartupEnabled" : "StartupDisabled");
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set
        {
            if (_settings.MinimizeToTray == value) return;
            _settings.MinimizeToTray = value;
            SaveSettings();
            Raise();
        }
    }

    public bool GlobalHotkeysEnabled
    {
        get => _settings.GlobalHotkeysEnabled;
        set
        {
            if (_settings.GlobalHotkeysEnabled == value) return;
            _settings.GlobalHotkeysEnabled = value;
            SaveSettings();
            Raise();
        }
    }

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

    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value)) return;
            _selectedLanguage = value;
            Raise();
            _settings.LanguageCode = value.Code;
            SaveSettings();
            ApplyLanguage();
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => ThemeMode = value ? "Dark" : "Light";
    }

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
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public string AboutDescription => L("AboutBody", AppVersion);
    public string ThemeLabel => T(_isDarkTheme ? "ThemeDark" : "ThemeLight");
    public string LanguageCode => _selectedLanguage.Code;

    // The empty setter keeps TwoWay-by-default bindings (e.g. Run.Text) from throwing on a read-only indexer.
    public string this[string key]
    {
        get => T(key);
        set { }
    }

    public IEnumerable<DisplayProfile> Favorites => Profiles.Where(profile => profile.IsFavorite);

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

    /// <summary>Runs the start-up work once the window is on screen.</summary>
    public void Initialize()
    {
        // The very first read of the displays must finish before any saved correction is re-applied.
        _ = Enqueue(async () =>
        {
            await CaptureOriginalAsync();
            if (_settings.MonitorCorrections.Count > 0) await ReapplyPersistentCorrectionsAsync();
        }, visible: false);
        _scheduleTimer.Start();
        EvaluateSchedule();

        var warning = _profileStore.LoadWarning ?? _settingsStore.LoadWarning;
        if (warning is not null) StatusMessage = L("StorageWarning", System.IO.Path.GetFileName(warning));
    }

    public void Shutdown()
    {
        if (_pending is not null) RevertNow();
        _confirmTimer.Stop();
        _reapplyTimer.Stop();
        _livePreviewTimer.Stop();
        _scheduleTimer.Stop();
    }

    /// <summary>Requests a debounced re-application of the saved per-monitor corrections.</summary>
    public void RequestReapply() => SchedulePersistentCorrectionReapply();

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

    public void ApplyNeutral() => _ = EnqueueAutomation(NeutralizeAsync);

    /// <summary>Applies the next/previous profile, preferring favorites when there are any.</summary>
    public void CycleProfile(int direction)
    {
        var pool = Favorites.ToList();
        if (pool.Count == 0) pool = Profiles.ToList();
        var current = Profiles.FirstOrDefault(item => item.IsActive)?.Id ?? SelectedProfile.Id;
        if (Cycle(pool, current, direction) is not { } next) return;
        SelectedProfile = next;
        _ = EnqueueAutomation(() => ApplyAsync(next));
    }

    internal static DisplayProfile? Cycle(IReadOnlyList<DisplayProfile> pool, string? currentId, int direction)
    {
        if (pool.Count == 0) return null;
        var index = pool.ToList().FindIndex(item => item.Id == currentId);
        var next = index < 0
            ? (direction >= 0 ? 0 : pool.Count - 1)
            : (((index + direction) % pool.Count) + pool.Count) % pool.Count;
        return pool[next];
    }

    public void ApplyProfileById(string profileId)
    {
        if (FindProfile(profileId) is not { } profile) return;
        SelectedProfile = profile;
        _ = EnqueueAutomation(() => ApplyAsync(profile));
    }

    private DisplayProfile? FindProfile(string id) =>
        Profiles.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void ToggleFavorite(DisplayProfile profile)
    {
        profile.IsFavorite = !profile.IsFavorite;
        SaveProfiles();
        StatusMessage = L(profile.IsFavorite ? "FavoriteAdded" : "FavoriteRemoved", profile.DisplayName);
        if (_favoritesOnly) OnFiltersChanged();
    }

    private void RestoreDefaults()
    {
        var restored = _profileStore.GetDefault(SelectedProfile.Id);
        SelectedProfile.CopyAdjustmentsFrom(restored);
        SaveProfiles();
        StatusMessage = L("Restored", SelectedProfile.DisplayName);
    }

    private void RemoveCorrectionsFor(ApplyResult result)
    {
        foreach (var monitor in result.AppliedMonitors)
        {
            _settings.MonitorCorrections.RemoveAll(item =>
                item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase) ||
                item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void SaveMonitorCorrections(DisplayProfile profile, ApplyResult result, bool applyImageControls)
    {
        foreach (var monitor in result.AppliedMonitors)
        {
            _settings.MonitorCorrections.RemoveAll(item =>
                item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            _settings.MonitorCorrections.Add(MonitorService.CreateCorrection(profile, monitor, applyImageControls));
        }
        SaveSettings();
    }

    private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisplayProfile.Brightness) or nameof(DisplayProfile.Contrast)
            or nameof(DisplayProfile.Saturation) or nameof(DisplayProfile.Hue)
            or nameof(DisplayProfile.Gamma) or nameof(DisplayProfile.Red)
            or nameof(DisplayProfile.Green) or nameof(DisplayProfile.Blue)
            or nameof(DisplayProfile.ColorTemperature))
        {
            ScheduleLivePreview();
        }
    }

    private void ScheduleLivePreview()
    {
        if (!_isLivePreviewEnabled) return;
        _livePreviewTimer.Stop();
        _livePreviewTimer.Start();
    }

    private void SchedulePersistentCorrectionReapply(bool immediate = false)
    {
        if (_settings.MonitorCorrections.Count == 0) return;
        _reapplyTimer.Stop();
        if (immediate)
        {
            _ = Enqueue(ReapplyPersistentCorrectionsAsync, visible: false);
        }
        else
        {
            _reapplyTimer.Start();
        }
    }

    private async Task ReapplyPersistentCorrectionsAsync()
    {
        if (_isReapplyingCorrections)
        {
            _reapplyRequested = true;
            return;
        }

        _isReapplyingCorrections = true;
        var corrections = _settings.MonitorCorrections.ToArray();
        var cts = new CancellationTokenSource();
        _reapplyCts = cts;
        try
        {
            var result = await Task.Run(() => _monitor.Reapply(corrections, cts.Token), cts.Token);
            if (result.DisplayCount > 0)
            {
                StatusMessage = FormatResult(T("PersistentCorrectionRestored"), result);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer user action superseded this re-application.
        }
        catch (Exception exception)
        {
            AppLog.Error("Re-applying persistent corrections failed.", exception);
            StatusMessage = L("PersistentCorrectionFailed", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_reapplyCts, cts)) _reapplyCts = null;
            cts.Dispose();
            _isReapplyingCorrections = false;
            if (_reapplyRequested)
            {
                _reapplyRequested = false;
                SchedulePersistentCorrectionReapply();
            }
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

    private void ExportProfiles()
    {
        try
        {
            var path = _shell.PickSaveFile(T("ExportProfiles"), "luma-profiles.json");
            if (path is null) return;
            _profileStore.Export(path, Profiles);
            StatusMessage = L("ProfilesExported", System.IO.Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            AppLog.Error("Exporting profiles failed.", exception);
            StatusMessage = L("ProfilesTransferFailed", exception.Message);
        }
    }

    private void ImportProfiles()
    {
        try
        {
            var path = _shell.PickOpenFile(T("ImportProfiles"));
            if (path is null) return;
            var updated = _profileStore.Import(path, Profiles);
            SaveProfiles();
            if (_favoritesOnly) OnFiltersChanged();
            StatusMessage = L("ProfilesImported", updated);
        }
        catch (Exception exception)
        {
            AppLog.Error("Importing profiles failed.", exception);
            StatusMessage = L("ProfilesTransferFailed", exception.Message);
        }
    }

    private void SaveProfiles()
    {
        try
        {
            _profileStore.Save(Profiles);
        }
        catch (Exception exception)
        {
            AppLog.Error("Saving profiles failed.", exception);
            StatusMessage = T("SettingsSaveFailed");
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("Saving settings failed.", exception);
            StatusMessage = T("SettingsSaveFailed");
        }
    }

    private string FormatResult(string successText, ApplyResult result)
    {
        if (result.Failures.Count == 0)
        {
            return L("AppliedDisplays", successText, result.DisplayCount);
        }

        var summary = string.Join(" ", result.Failures.Distinct().Take(2));
        return L("AppliedWarnings", successText, result.DisplayCount, summary);
    }

    private void ApplyLanguage()
    {
        LocalizationService.LocalizeProfiles(Profiles, _selectedLanguage.Code);
        RefreshLocalizedOptions();
        RefreshFilterText();
        RefreshVisibleProfiles();
        RefreshAppRules();
        StatusMessage = T("Ready");
        RaiseUiProperties();
        Raise(nameof(LanguageCode));
        Raise(nameof(SidebarToggleLabel));
        Raise("Item[]");
        Raise(nameof(LeftPanelTooltip));
        Raise(nameof(RightPanelTooltip));
    }

    private void RaiseUiProperties()
    {
        Raise(nameof(ProfileCountSubtitle));
        Raise(nameof(AboutDescription));
        Raise(nameof(IsDarkTheme));
        Raise(nameof(ThemeLabel));
        Raise(nameof(MaximizeTooltip));
    }

    private void RefreshLocalizedOptions()
    {
        ColorTemperatureOptions.Clear();
        ColorTemperatureOptions.Add(new LocalizedOption("Usuario (RGB)", T("UserRgb")));
        ColorTemperatureOptions.Add(new LocalizedOption("Cálido 5000 K", T("Warm5000")));
        ColorTemperatureOptions.Add(new LocalizedOption("Neutro 6500 K", T("Neutral6500")));
        ColorTemperatureOptions.Add(new LocalizedOption("Frío 7500 K", T("Cool7500")));
    }

    private string T(string key) => LocalizationService.Text(key, _selectedLanguage.Code);

    private string L(string key, params object[] values) =>
        LocalizationService.Format(key, _selectedLanguage.Code, values);
}
