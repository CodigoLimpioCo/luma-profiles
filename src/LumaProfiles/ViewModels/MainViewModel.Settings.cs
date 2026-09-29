using System.Collections.ObjectModel;
using System.Windows.Input;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>State behind the categorized settings page: sections, appearance and per-section reset.</summary>
public sealed partial class MainViewModel
{
    public const double MinScrollBarThickness = 4;
    public const double MaxScrollBarThickness = 14;
    public const double DefaultScrollBarThickness = 7;

    public static IReadOnlyList<string> SettingsSections { get; } =
        ["Appearance", "Layout", "General", "Language", "Automation", "Data"];

    public static IReadOnlyList<string> ThemeModes { get; } = ["Light", "Dark", "System"];

    private string _settingsSection = "Appearance";
    private Func<bool> _systemPrefersDark = WindowsTheme.PrefersDark;

    public ObservableCollection<AccentChoice> AccentChoices { get; } = [];

    public ICommand ResetSectionCommand { get; private set; } = null!;

    private void InitializeSettingsPage()
    {
        ResetSectionCommand = new RelayCommand(ResetSection);
        AccentChoices.Clear();
        foreach (var option in AccentPalette.All)
        {
            AccentChoices.Add(new AccentChoice(option, T("Accent" + option.Key), option.Key == Accent.Key, key => AccentKey = key));
        }
    }

    // ---- sections -------------------------------------------------------------------------

    public string SettingsSection
    {
        get => _settingsSection;
        set
        {
            if (!SettingsSections.Contains(value) || !Set(ref _settingsSection, value)) return;
            RaiseSection();
        }
    }

    public bool IsSectionAppearance { get => SettingsSection == "Appearance"; set { if (value) SettingsSection = "Appearance"; } }
    public bool IsSectionLayout { get => SettingsSection == "Layout"; set { if (value) SettingsSection = "Layout"; } }
    public bool IsSectionGeneral { get => SettingsSection == "General"; set { if (value) SettingsSection = "General"; } }
    public bool IsSectionLanguage { get => SettingsSection == "Language"; set { if (value) SettingsSection = "Language"; } }
    public bool IsSectionAutomation { get => SettingsSection == "Automation"; set { if (value) SettingsSection = "Automation"; } }
    public bool IsSectionData { get => SettingsSection == "Data"; set { if (value) SettingsSection = "Data"; } }

    public string SectionTitle => T("Section" + SettingsSection);
    public string SectionDescription => T("Section" + SettingsSection + "Desc");

    public string SectionGlyph => SettingsSection switch
    {
        "Appearance" => "",
        "Layout" => "",
        "General" => "",
        "Language" => "",
        "Automation" => "",
        _ => ""
    };

    public bool IsResetAvailable => SettingsSection != "Data";

    private void RaiseSection()
    {
        foreach (var name in new[]
                 {
                     nameof(IsSectionAppearance), nameof(IsSectionLayout), nameof(IsSectionGeneral),
                     nameof(IsSectionLanguage), nameof(IsSectionAutomation), nameof(IsSectionData),
                     nameof(SectionTitle), nameof(SectionDescription), nameof(SectionGlyph), nameof(IsResetAvailable)
                 })
        {
            Raise(name);
        }
    }

    // ---- theme -----------------------------------------------------------------------------

    public string ThemeMode
    {
        get => ThemeModes.Contains(_settings.ThemeMode ?? string.Empty)
            ? _settings.ThemeMode!
            : (_settings.IsDarkTheme ? "Dark" : "Light");
        set
        {
            if (!ThemeModes.Contains(value) || _settings.ThemeMode == value) return;
            _settings.ThemeMode = value;
            SaveSettings();
            Raise();
            Raise(nameof(IsThemeLight));
            Raise(nameof(IsThemeDark));
            Raise(nameof(IsThemeSystem));
            RefreshEffectiveTheme();
        }
    }

    public bool IsThemeLight { get => ThemeMode == "Light"; set { if (value) ThemeMode = "Light"; } }
    public bool IsThemeDark { get => ThemeMode == "Dark"; set { if (value) ThemeMode = "Dark"; } }
    public bool IsThemeSystem { get => ThemeMode == "System"; set { if (value) ThemeMode = "System"; } }

    private bool ComputeDarkTheme() => ThemeMode switch
    {
        "Light" => false,
        "System" => _systemPrefersDark(),
        _ => true
    };

    private void RefreshEffectiveTheme()
    {
        var dark = ComputeDarkTheme();
        if (dark == _isDarkTheme) return;
        _isDarkTheme = dark;
        _settings.IsDarkTheme = dark;
        SaveSettings();
        RaiseUiProperties();
    }

    /// <summary>Called when Windows changes its light/dark preference; only matters in "System" mode.</summary>
    public void RefreshSystemTheme()
    {
        if (ThemeMode == "System") RefreshEffectiveTheme();
    }

    /// <summary>Test seam: replaces the Windows theme lookup.</summary>
    internal void UseSystemThemeProvider(Func<bool> prefersDark)
    {
        _systemPrefersDark = prefersDark;
        RefreshEffectiveTheme();
    }

    // ---- accent ----------------------------------------------------------------------------

    public AccentOption Accent => AccentPalette.Get(_settings.AccentColor);

    public string AccentKey
    {
        get => Accent.Key;
        set
        {
            if (AccentPalette.Find(value) is not { } option || Accent.Key == option.Key) return;
            _settings.AccentColor = option.Key;
            SaveSettings();
            Raise();
            Raise(nameof(Accent));
            foreach (var choice in AccentChoices) choice.SetSelectedSilently(choice.Key == option.Key);
        }
    }

    // ---- scrollbar -------------------------------------------------------------------------

    public double ScrollBarThickness
    {
        get => Math.Clamp(_settings.ScrollBarThickness, MinScrollBarThickness, MaxScrollBarThickness);
        set
        {
            var clamped = Math.Clamp(Math.Round(value), MinScrollBarThickness, MaxScrollBarThickness);
            if (Math.Abs(clamped - ScrollBarThickness) < 0.01) return;
            _settings.ScrollBarThickness = clamped;
            SaveSettings();
            Raise();
            Raise(nameof(ScrollBarThicknessLabel));
        }
    }

    public string ScrollBarThicknessLabel => $"{ScrollBarThickness:0} px";

    // ---- layout preferences ----------------------------------------------------------------

    public bool SidebarCollapsedPreference
    {
        get => _settings.IsLeftPanelCollapsed;
        set
        {
            if (_settings.IsLeftPanelCollapsed == value) return;
            _settings.IsLeftPanelCollapsed = value;
            SaveSettings();
            Raise();
            RaiseSidebar();
        }
    }

    public bool LeftPanelRequested
    {
        get => _settings.IsLeftPanelOpen;
        set
        {
            if (_settings.IsLeftPanelOpen == value) return;
            _settings.IsLeftPanelOpen = value;
            SaveSettings();
            Raise();
        }
    }

    public bool RightPanelRequested
    {
        get => _settings.IsRightPanelOpen;
        set
        {
            if (_settings.IsRightPanelOpen == value) return;
            _settings.IsRightPanelOpen = value;
            SaveSettings();
            Raise();
        }
    }

    // ---- reset -----------------------------------------------------------------------------

    private void ResetSection()
    {
        switch (SettingsSection)
        {
            case "Appearance":
                ThemeMode = "Dark";
                AccentKey = AccentPalette.Default.Key;
                ScrollBarThickness = DefaultScrollBarThickness;
                break;
            case "Layout":
                SidebarCollapsedPreference = false;
                LeftPanelRequested = true;
                RightPanelRequested = true;
                ViewMode = ViewModes[0];
                break;
            case "General":
                StartWithWindows = true;
                MinimizeToTray = false;
                ConfirmChanges = true;
                break;
            case "Language":
                if (Languages.FirstOrDefault(language => language.Code == "es") is { } spanish) SelectedLanguage = spanish;
                break;
            case "Automation":
                GlobalHotkeysEnabled = true;
                ScheduleEnabled = false;
                ScheduleDayProfileId = "natural";
                ScheduleNightProfileId = "eyes-night";
                ScheduleDayStart = "07:00";
                ScheduleNightStart = "20:00";
                _settings.AppRules.Clear();
                _ruleEngine.Reset();
                SaveSettings();
                RefreshAppRules();
                break;
            default:
                return;
        }

        StatusMessage = L("SectionReset", SectionTitle);
    }

    private void RefreshSettingsText()
    {
        foreach (var choice in AccentChoices) choice.Name = T("Accent" + choice.Key);
        RaiseSection();
    }
}
