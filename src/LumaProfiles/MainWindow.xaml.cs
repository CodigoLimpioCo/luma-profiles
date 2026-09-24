using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProfileStore _profileStore = new();
    private readonly MonitorService _monitorService = new();
    private readonly DispatcherTimer _livePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private DisplayProfile _selectedProfile;
    private string _selectedCategory = "Todos";
    private string _searchText = string.Empty;
    private string _selectedMonitorTarget = "Ambas pantallas";
    private string _statusMessage = string.Empty;
    private LanguageOption _selectedLanguage;
    private bool _isDarkTheme = true;
    private bool _isLivePreviewEnabled;
    private bool _isAboutOpen;
    private string _maximizeGlyph = "\uE922";

    public ObservableCollection<DisplayProfile> Profiles { get; }
    public ObservableCollection<DisplayProfile> VisibleProfiles { get; } = [];
    public IReadOnlyList<LanguageOption> Languages { get; }
    public ObservableCollection<LocalizedOption> MonitorTargets { get; } = [];
    public ObservableCollection<LocalizedOption> ColorTemperatureOptions { get; } = [];

    public DisplayProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value)) return;
            _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            _selectedProfile = value;
            _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedProfile)));
            ScheduleLivePreview();
        }
    }

    public string SelectedMonitorTarget
    {
        get => _selectedMonitorTarget;
        set
        {
            if (_selectedMonitorTarget == value) return;
            _selectedMonitorTarget = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitorTarget)));
            ScheduleLivePreview();
        }
    }

    public bool IsLivePreviewEnabled
    {
        get => _isLivePreviewEnabled;
        set
        {
            if (_isLivePreviewEnabled == value) return;
            _isLivePreviewEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLivePreviewEnabled)));
            StatusMessage = T(value ? "LivePreviewOn" : "LivePreviewOff");
            if (value) ScheduleLivePreview(); else _livePreviewTimer.Stop();
        }
    }

    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        private set => Set(ref _isAboutOpen, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value)) return;
            _selectedLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguage)));
            ApplyLanguage();
        }
    }

    public string MaximizeGlyph
    {
        get => _maximizeGlyph;
        private set => Set(ref _maximizeGlyph, value);
    }

    public string ProfileCountSubtitle => L("ModesSubtitle", Profiles.Count);
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public string AboutDescription => L("AboutBody", AppVersion);
    public bool IsDarkTheme => _isDarkTheme;
    public string ThemeLabel => T(_isDarkTheme ? "ThemeDark" : "ThemeLight");
    public string MaximizeTooltip => T(WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    public string this[string key]
    {
        get => T(key);
        set { }
    }

    public MainWindow()
    {
        Profiles = new ObservableCollection<DisplayProfile>(_profileStore.Load());
        Languages = LocalizationService.DiscoverLanguages();
        _selectedLanguage = Languages.FirstOrDefault(item => item.Code == "es")
            ?? Languages.FirstOrDefault()
            ?? new LanguageOption("es", "Español", string.Empty);
        _selectedProfile = Profiles.First();
        _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
        LocalizationService.LocalizeProfiles(Profiles, _selectedLanguage.Code);
        RefreshLocalizedOptions();
        RefreshVisibleProfiles();

        InitializeComponent();
        DataContext = this;
        StatusMessage = T("Ready");
        ApplyTheme();
        _livePreviewTimer.Tick += LivePreviewTimer_Tick;
        StateChanged += (_, _) => UpdateWindowStateIcon();
        Loaded += (_, _) =>
        {
            ProfilesScrollViewer.ScrollToTop();
            UpdateWindowStateIcon();
        };
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
        {
            _selectedCategory = category;
            RefreshVisibleProfiles();
            ProfilesScrollViewer?.ScrollToTop();
            StatusMessage = category == "Todos"
                ? L("ShowingProfiles", Profiles.Count)
                : L("CategoryStatus", LocalizationService.Category(category, _selectedLanguage.Code));
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        RefreshVisibleProfiles();
        ProfilesScrollViewer?.ScrollToTop();
        StatusMessage = string.IsNullOrWhiteSpace(_searchText)
            ? L("CompleteLibrary", Profiles.Count)
            : L("SearchResults", _searchText);
    }

    private void RefreshVisibleProfiles()
    {
        VisibleProfiles.Clear();
        foreach (var profile in Profiles.Where(MatchesCurrentFilter))
        {
            VisibleProfiles.Add(profile);
        }
    }

    private bool MatchesCurrentFilter(DisplayProfile profile)
    {
        if (_selectedCategory == "Favoritos" && !profile.IsFavorite) return false;
        if (_selectedCategory != "Todos" && _selectedCategory != "Favoritos" && profile.Category != _selectedCategory) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        return profile.DisplayName.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               profile.DisplayCategory.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               profile.DisplayDescription.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        RaiseUiProperties();
    }

    private void OpenCodigoLimpio_Click(object sender, RoutedEventArgs e) => OpenUrl("https://codigolimpio.com.co/");

    private void OpenCodigoLimpioGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/CodigoLimpioCo/luma-profiles");

    private void About_Click(object sender, RoutedEventArgs e) => IsAboutOpen = true;

    private void CloseAbout_Click(object sender, RoutedEventArgs e) => IsAboutOpen = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && IsAboutOpen)
        {
            IsAboutOpen = false;
            e.Handled = true;
        }
    }

    private void OpenHdrSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowStateIcon()
    {
        MaximizeGlyph = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaximizeTooltip)));
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DisplayProfile profile })
        {
            SelectedProfile = profile;
            StatusMessage = L("Editing", profile.DisplayName);
        }
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DisplayProfile profile })
        {
            SelectedProfile = profile;
            Apply(profile);
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DisplayProfile profile }) return;

        profile.IsFavorite = !profile.IsFavorite;
        _profileStore.Save(Profiles);
        StatusMessage = L(profile.IsFavorite ? "FavoriteAdded" : "FavoriteRemoved", profile.DisplayName);
        if (_selectedCategory == "Favoritos") RefreshVisibleProfiles();
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        _profileStore.Save(Profiles);
        Apply(SelectedProfile);
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

    private void LivePreviewTimer_Tick(object? sender, EventArgs e)
    {
        _livePreviewTimer.Stop();
        var result = _monitorService.Preview(SelectedProfile, SelectedMonitorTarget);
        StatusMessage = FormatResult(T("LivePreviewApplied"), result);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var restored = _profileStore.GetDefault(SelectedProfile.Id);
        SelectedProfile.CopyAdjustmentsFrom(restored);
        _profileStore.Save(Profiles);
        StatusMessage = L("Restored", SelectedProfile.DisplayName);
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        StatusMessage = T("Neutralizing");
        var result = _monitorService.RestoreNeutral(SelectedMonitorTarget);
        StatusMessage = FormatResult(T("Neutralized"), result);
    }

    private void Apply(DisplayProfile profile)
    {
        StatusMessage = L("Applying", profile.DisplayName);
        var result = _monitorService.Apply(profile, SelectedMonitorTarget);
        foreach (var item in Profiles) item.IsActive = false;
        profile.IsActive = result.DisplayCount > 0;
        _profileStore.Save(Profiles);
        StatusMessage = FormatResult(L("Applied", profile.DisplayName), result);
        if (profile.IsHdr)
        {
            StatusMessage += T("HdrReminder");
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
        RefreshVisibleProfiles();
        StatusMessage = T("Ready");
        RaiseUiProperties();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void RaiseUiProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileCountSubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AboutDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkTheme)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaximizeTooltip)));
    }

    private void RefreshLocalizedOptions()
    {
        MonitorTargets.Clear();
        MonitorTargets.Add(new LocalizedOption("Ambas pantallas", T("BothDisplays")));
        MonitorTargets.Add(new LocalizedOption("Pantalla 1", T("Display1")));
        MonitorTargets.Add(new LocalizedOption("Pantalla 2", T("Display2")));

        ColorTemperatureOptions.Clear();
        ColorTemperatureOptions.Add(new LocalizedOption("Usuario (RGB)", T("UserRgb")));
        ColorTemperatureOptions.Add(new LocalizedOption("Cálido 5000 K", T("Warm5000")));
        ColorTemperatureOptions.Add(new LocalizedOption("Neutro 6500 K", T("Neutral6500")));
        ColorTemperatureOptions.Add(new LocalizedOption("Frío 7500 K", T("Cool7500")));
    }

    private void ApplyTheme()
    {
        var palette = _isDarkTheme
            ? new Dictionary<string, string>
            {
                ["WindowBrush"] = "#090E14", ["TitleBarBrush"] = "#0A1118", ["SidebarBrush"] = "#0C141C",
                ["InspectorBrush"] = "#0E161F", ["PanelBrush"] = "#121A23", ["CardBrush"] = "#151F29",
                ["SurfaceBrush"] = "#111B24", ["SurfaceAltBrush"] = "#17222C", ["InputBrush"] = "#17232D",
                ["BorderThemeBrush"] = "#21313E", ["BorderStrongBrush"] = "#334756", ["PrimaryTextBrush"] = "#F5F8FB",
                ["SecondaryTextBrush"] = "#DCE7F0", ["MutedBrush"] = "#9DAFC0", ["HoverBrush"] = "#1D2A36",
                ["SelectedBrush"] = "#17303B", ["SecondaryButtonBrush"] = "#22303C", ["ChipBrush"] = "#22313D",
                ["AccentTextBrush"] = "#8EDFF2"
            }
            : new Dictionary<string, string>
            {
                ["WindowBrush"] = "#F3F7FA", ["TitleBarBrush"] = "#FFFFFF", ["SidebarBrush"] = "#F8FBFD",
                ["InspectorBrush"] = "#F7FAFC", ["PanelBrush"] = "#FFFFFF", ["CardBrush"] = "#FFFFFF",
                ["SurfaceBrush"] = "#EEF4F7", ["SurfaceAltBrush"] = "#E8F1F5", ["InputBrush"] = "#FFFFFF",
                ["BorderThemeBrush"] = "#CCD9E1", ["BorderStrongBrush"] = "#AFC2CE", ["PrimaryTextBrush"] = "#13232E",
                ["SecondaryTextBrush"] = "#29404F", ["MutedBrush"] = "#607887", ["HoverBrush"] = "#E6F1F5",
                ["SelectedBrush"] = "#D8F1F7", ["SecondaryButtonBrush"] = "#DDE9EF", ["ChipBrush"] = "#E3EDF2",
                ["AccentTextBrush"] = "#087B95"
            };

        foreach (var (key, color) in palette)
        {
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }

    private string T(string key) => LocalizationService.Text(key, _selectedLanguage.Code);
    private string L(string key, params object[] values) => LocalizationService.Format(key, _selectedLanguage.Code, values);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
