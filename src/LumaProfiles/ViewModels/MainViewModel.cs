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

public enum CloseDecision { Exit, Hide, Stay }

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
    private ScheduleEntry? _lastScheduleEntry;
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
        IReadOnlyList<string>? availableFonts = null,
        RestorePointStore? restorePointStore = null)
    {
        _restorePoints = restorePointStore ?? new RestorePointStore();
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
        RefreshAutomationLists();

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
        AddProfileHotkeyCommand = new RelayCommand(AddProfileHotkey);
        RemoveProfileHotkeyCommand = new RelayCommand<ProfileHotkeyItem>(RemoveProfileHotkey);
        SaveAsNewProfileCommand = new RelayCommand(SaveAsNewProfile);
        DeleteCustomProfileCommand = new RelayCommand(DeleteCustomProfile);
        AddScheduleEntryCommand = new RelayCommand(AddScheduleEntry);
        RemoveScheduleEntryCommand = new RelayCommand<ScheduleEntryItem>(RemoveScheduleEntry);
        RemoveAppRuleCommand = new RelayCommand<AppRuleItem>(RemoveAppRule);

        AdoptRestorePointCopy();
        _monitor.UseMonitorControls = _settings.UseMonitorControls;
        _monitor.UseOriginalStates(_settings.OriginalMonitorStates);
        InitializeConfirmation();
        InitializeSettingsPage();
        InitializeDisplays();
        InitializeRestorePoint();
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
    public ObservableCollection<ScheduleEntryItem> ScheduleEntries { get; } = [];

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
    public ICommand AddProfileHotkeyCommand { get; }
    public ICommand RemoveProfileHotkeyCommand { get; }
    public ICommand SaveAsNewProfileCommand { get; }
    public ICommand DeleteCustomProfileCommand { get; }
    public ICommand AddScheduleEntryCommand { get; }
    public ICommand RemoveScheduleEntryCommand { get; }
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
            Raise(nameof(SelectedProfileIsCustom));
            Raise(nameof(SelectedProfileIsBuiltIn));
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

    public void ApplyNeutral() => _ = EnqueueAutomation(NeutralizeAsync);

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
        RefreshAutomationLists();
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
        RefreshScheduleOptions();
    }

    private string T(string key) => LocalizationService.Text(key, _selectedLanguage.Code);

    private string L(string key, params object[] values) =>
        LocalizationService.Format(key, _selectedLanguage.Code, values);
}
