using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
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

    public const int MinUiScale = 80;
    public const int MaxUiScale = 150;
    public const int UiScaleStep = 5;
    public const int DefaultUiScale = 100;

    private string _settingsSection = "Appearance";
    private IReadOnlyList<string> _availableFonts = [FontCatalog.DefaultFont];
    private double _uiScalePreview = DefaultUiScale;
    private Func<bool> _systemPrefersDark = WindowsTheme.PrefersDark;

    public ObservableCollection<AccentChoice> AccentChoices { get; } = [];
    public ObservableCollection<FontChoice> FontChoices { get; } = [];

    public ICommand ResetSectionCommand { get; private set; } = null!;

    private void InitializeSettingsPage()
    {
        ResetSectionCommand = new RelayCommand(ResetSection);
        _uiScalePreview = UiScalePercent;
        FontChoices.Clear();
        foreach (var name in _availableFonts)
        {
            FontChoices.Add(new FontChoice(name, name == FontFamilyName, selected => FontFamilyName = selected));
        }

        AccentChoices.Clear();
        foreach (var option in AccentPalette.All)
        {
            AccentChoices.Add(new AccentChoice(option, T("Accent" + option.Key), option.Key == Accent.Key, key => AccentKey = key));
        }

        AccentChoices.Add(AccentChoice.CreateCustomEntry(T("Accent" + AccentPalette.CustomKey), _settings.CustomAccentColor,
            Accent.IsCustom, key => AccentKey = key));
        LoadCustomAccentFromSettings();
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

    private double _customHue;
    private double _customSaturation = 0.7;
    private double _customBrightness = 0.9;

    /// <summary>A preset name, a custom color (#RRGGBB), or "Custom" to start from the last color you composed.</summary>
    public string AccentKey
    {
        get => Accent.Key;
        set
        {
            if (value == AccentPalette.CustomKey) value = CustomAccentHex;
            if (AccentPalette.Find(value) is not { } option || Accent.Key == option.Key) return;

            _settings.AccentColor = option.Key;
            if (option.IsCustom)
            {
                _settings.CustomAccentColor = option.Key;
                (_customHue, _customSaturation, _customBrightness) = AccentColors.ToHsv(option.Key);
            }

            SaveSettings();
            RaiseAccent();
        }
    }

    /// <summary>True while the user's own color is the accent, which shows the color sliders.</summary>
    public bool IsCustomAccent => Accent.IsCustom;

    public double CustomAccentHue
    {
        get => _customHue;
        set => SetCustomAccent(hue: value);
    }

    /// <summary>0..100 for the slider; stored as 0..1.</summary>
    public double CustomAccentSaturation
    {
        get => _customSaturation * 100;
        set => SetCustomAccent(saturation: value / 100);
    }

    public double CustomAccentBrightness
    {
        get => _customBrightness * 100;
        set => SetCustomAccent(brightness: value / 100);
    }

    /// <summary>The color as #RRGGBB: the current custom color, else the last one composed, else the current accent.</summary>
    public string CustomAccentHex
    {
        get => Accent.IsCustom ? Accent.Key
            : AccentColors.TryNormalizeHex(_settings.CustomAccentColor, out var remembered) ? remembered
            : Accent.Accent;
        set
        {
            if (!AccentColors.TryNormalizeHex(value, out var hex))
            {
                StatusMessage = T("AccentInvalidHex");
                Raise();
                return;
            }

            AccentKey = hex;
        }
    }

    public Brush CustomHueBrush { get; } = CreateHueBrush();

    public Brush CustomSaturationBrush =>
        CreateGradient(AccentColors.FromHsv(_customHue, 0, _customBrightness), AccentColors.FromHsv(_customHue, 1, _customBrightness));

    public Brush CustomBrightnessBrush =>
        CreateGradient(AccentColors.FromHsv(_customHue, _customSaturation, 0), AccentColors.FromHsv(_customHue, _customSaturation, 1));

    /// <summary>Live while a slider moves: the whole interface follows, and the file is written when the drag ends.</summary>
    private void SetCustomAccent(double? hue = null, double? saturation = null, double? brightness = null)
    {
        if (hue is { } h) _customHue = Math.Clamp(h, 0, 360);
        if (saturation is { } s) _customSaturation = Math.Clamp(s, 0, 1);
        if (brightness is { } v) _customBrightness = Math.Clamp(v, 0, 1);

        var hex = AccentColors.FromHsv(_customHue, _customSaturation, _customBrightness);
        _settings.AccentColor = hex;
        _settings.CustomAccentColor = hex;
        RaiseAccent();
    }

    /// <summary>Saves the custom color once the slider is released or the key is lifted.</summary>
    public void CommitCustomAccent() => SaveSettings();

    private void LoadCustomAccentFromSettings()
    {
        if (AccentColors.TryNormalizeHex(_settings.CustomAccentColor ?? (Accent.IsCustom ? Accent.Key : null), out var hex))
        {
            (_customHue, _customSaturation, _customBrightness) = AccentColors.ToHsv(hex);
        }
    }

    private void RaiseAccent()
    {
        Raise(nameof(AccentKey));
        Raise(nameof(Accent));
        Raise(nameof(IsCustomAccent));
        Raise(nameof(CustomAccentHex));
        Raise(nameof(CustomAccentHue));
        Raise(nameof(CustomAccentSaturation));
        Raise(nameof(CustomAccentBrightness));
        Raise(nameof(CustomSaturationBrush));
        Raise(nameof(CustomBrightnessBrush));
        foreach (var choice in AccentChoices)
        {
            choice.SetSelectedSilently(choice.IsCustomEntry ? Accent.IsCustom : !Accent.IsCustom && choice.Key == Accent.Key);
            if (choice.IsCustomEntry) choice.ShowCustomColor(_settings.CustomAccentColor);
        }
    }

    private static Brush CreateHueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
        for (var step = 0; step <= 6; step++)
        {
            brush.GradientStops.Add(new GradientStop(AccentColors.ToColor(AccentColors.FromHsv(step * 60, 1, 1)), step / 6.0));
        }

        brush.Freeze();
        return brush;
    }

    private static Brush CreateGradient(string from, string to)
    {
        var brush = new LinearGradientBrush(AccentColors.ToColor(from), AccentColors.ToColor(to), 0);
        brush.Freeze();
        return brush;
    }

    // ---- typeface and interface size ---------------------------------------------------------

    /// <summary>The saved typeface, or the first installed one when it is no longer available.</summary>
    public string FontFamilyName
    {
        get
        {
            var saved = _settings.FontFamilyName;
            if (_availableFonts.Contains(saved, StringComparer.OrdinalIgnoreCase)) return saved;
            return _availableFonts.Contains(FontCatalog.DefaultFont) ? FontCatalog.DefaultFont : _availableFonts[0];
        }
        set
        {
            var match = _availableFonts.FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match is null || FontFamilyName == match) return;
            _settings.FontFamilyName = match;
            SaveSettings();
            Raise();
            foreach (var choice in FontChoices) choice.SetSelectedSilently(choice.Name == match);
        }
    }

    /// <summary>Committed interface scale in percent (what the window uses).</summary>
    public int UiScalePercent
    {
        get => Math.Clamp(_settings.UiScalePercent, MinUiScale, MaxUiScale);
        private set
        {
            var clamped = Math.Clamp(value, MinUiScale, MaxUiScale);
            if (clamped == UiScalePercent) return;
            _settings.UiScalePercent = clamped;
            SaveSettings();
            Raise();
            Raise(nameof(UiScaleFactor));
        }
    }

    public double UiScaleFactor => UiScalePercent / 100.0;

    /// <summary>
    /// Value shown by the slider while it is being dragged. It only takes effect on
    /// <see cref="CommitUiScale"/>, because resizing the UI under the pointer would make the slider jump.
    /// </summary>
    public double UiScalePreview
    {
        get => _uiScalePreview;
        set
        {
            var snapped = Math.Clamp(Math.Round(value / UiScaleStep) * UiScaleStep, MinUiScale, MaxUiScale);
            if (!Set(ref _uiScalePreview, snapped)) return;
            Raise(nameof(UiScalePreviewLabel));
            Raise(nameof(UiScalePreviewFontSize));
        }
    }

    public string UiScalePreviewLabel => $"{UiScalePreview:0}%";

    /// <summary>Font size of the sample text: 14 pt scaled by the value being previewed.</summary>
    public double UiScalePreviewFontSize => 14 * UiScalePreview / Math.Max(UiScalePercent, 1);

    public void CommitUiScale() => UiScalePercent = (int)UiScalePreview;

    // ---- monitor controls ---------------------------------------------------------------------

    /// <summary>Off = "software only": profiles change the gamma but never write to the monitor itself.</summary>
    public bool UseMonitorControls
    {
        get => _settings.UseMonitorControls;
        set
        {
            if (_settings.UseMonitorControls == value) return;
            _settings.UseMonitorControls = value;
            _monitor.UseMonitorControls = value;
            SaveSettings();
            Raise();
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

    // ---- resizable side menu ---------------------------------------------------------------

    public const double MinSidebarWidth = 180;
    public const double MaxSidebarWidth = 420;
    public const double DefaultSidebarWidth = 220;

    /// <summary>Width of the expanded side menu, always within its allowed range.</summary>
    public double SidebarWidth => Math.Clamp(_settings.SidebarWidth, MinSidebarWidth, MaxSidebarWidth);

    /// <summary>Moves the menu edge while dragging. The window passes the largest width that still leaves room for the content.</summary>
    public void ResizeSidebar(double delta, double maxAvailable = MaxSidebarWidth)
    {
        var limit = Math.Clamp(maxAvailable, MinSidebarWidth, MaxSidebarWidth);
        SetSidebarWidth(Math.Clamp(SidebarWidth + delta, MinSidebarWidth, limit));
    }

    /// <summary>Saves the width once the drag (or key press) is finished, so the file is not rewritten on every mouse move.</summary>
    public void CommitSidebarWidth() => SaveSettings();

    public void ResetSidebarWidth()
    {
        SetSidebarWidth(DefaultSidebarWidth);
        SaveSettings();
    }

    private void SetSidebarWidth(double width)
    {
        var rounded = Math.Round(width);
        if (Math.Abs(rounded - SidebarWidth) < 0.5) return;
        _settings.SidebarWidth = rounded;
        Raise(nameof(SidebarWidth));
    }

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
                _settings.CustomAccentColor = null;
                AccentKey = AccentPalette.Default.Key;
                RaiseAccent();
                ScrollBarThickness = DefaultScrollBarThickness;
                FontFamilyName = FontCatalog.DefaultFont;
                UiScalePreview = DefaultUiScale;
                CommitUiScale();
                break;
            case "Layout":
                SidebarCollapsedPreference = false;
                ResetSidebarWidth();
                LeftPanelRequested = true;
                RightPanelRequested = true;
                ViewMode = ViewModes[0];
                break;
            case "General":
                StartWithWindows = true;
                MinimizeToTray = false;
                StartHiddenAtSignIn = true;
                _settings.CloseChoiceAsked = false;
                ConfirmChanges = true;
                UseMonitorControls = true;
                break;
            case "Language":
                if (Languages.FirstOrDefault(language => language.Code == "es") is { } spanish) SelectedLanguage = spanish;
                break;
            case "Automation":
                GlobalHotkeysEnabled = true;
                ScheduleEnabled = false;
                _settings.Schedule = new Models.ScheduleSettings();
                _settings.AppRules.Clear();
                _settings.ProfileHotkeys.Clear();
                _ruleEngine.Reset();
                SaveSettings();
                RefreshAutomationLists();
                foreach (var property in new[] { nameof(ScheduleLatitude), nameof(ScheduleLongitude), nameof(ScheduleTransitionSeconds) })
                {
                    Raise(property);
                }
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
