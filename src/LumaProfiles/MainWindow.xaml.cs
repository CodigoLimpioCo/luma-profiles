using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProfileStore _profileStore = new();
    private readonly ApplicationSettingsStore _settingsStore = new();
    private readonly MonitorService _monitorService = new();
    private readonly DispatcherTimer _livePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly DispatcherTimer _reapplyTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly ApplicationSettings _settings;
    private DisplayProfile _selectedProfile;
    private string _selectedCategory = "Todos";
    private string _searchText = string.Empty;
    private string _selectedMonitorTarget = "Ambas pantallas";
    private string _statusMessage = string.Empty;
    private LanguageOption _selectedLanguage;
    private bool _isDarkTheme = true;
    private bool _isLivePreviewEnabled;
    private bool _isAboutOpen;
    private bool _isSettingsOpen;
    private bool _startWithWindows;
    private bool _leftPanelRequested;
    private bool _rightPanelRequested;
    private bool _isLeftPanelVisible;
    private bool _isRightPanelVisible;
    private bool _isReapplyingCorrections;
    private bool _reapplyRequested;
    private string _maximizeGlyph = "\uE922";
    private HwndSource? _windowSource;
    private IntPtr _consoleDisplayNotification;
    private IntPtr _sessionDisplayNotification;

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
            _settings.SelectedMonitorTarget = value;
            SaveSettings();
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

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set => Set(ref _isSettingsOpen, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            if (!_settingsStore.SetStartupEnabled(value))
            {
                StatusMessage = T("StartupChangeFailed");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartWithWindows)));
                return;
            }

            _startWithWindows = value;
            _settings.StartWithWindows = value;
            SaveSettings();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartWithWindows)));
            StatusMessage = T(value ? "StartupEnabled" : "StartupDisabled");
        }
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
            _settings.LanguageCode = value.Code;
            SaveSettings();
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
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme == value) return;
            _isDarkTheme = value;
            _settings.IsDarkTheme = value;
            SaveSettings();
            ApplyTheme();
            RaiseUiProperties();
        }
    }
    public string ThemeLabel => T(_isDarkTheme ? "ThemeDark" : "ThemeLight");
    public string MaximizeTooltip => T(WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    public string LeftPanelTooltip => T(_isLeftPanelVisible ? "HideNavigationPanel" : "ShowNavigationPanel");
    public string RightPanelTooltip => T(_isRightPanelVisible ? "HideColorPanel" : "ShowColorPanel");
    public string this[string key]
    {
        get => T(key);
        set { }
    }

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        Profiles = new ObservableCollection<DisplayProfile>(_profileStore.Load());
        Languages = LocalizationService.DiscoverLanguages();
        _selectedLanguage = Languages.FirstOrDefault(item => item.Code.Equals(_settings.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(item => item.Code == "es")
            ?? Languages.FirstOrDefault()
            ?? new LanguageOption("es", "Español", string.Empty);
        _isDarkTheme = _settings.IsDarkTheme;
        _startWithWindows = _settings.StartWithWindows;
        _leftPanelRequested = _settings.IsLeftPanelOpen;
        _rightPanelRequested = _settings.IsRightPanelOpen;
        _selectedMonitorTarget = IsKnownMonitorTarget(_settings.SelectedMonitorTarget)
            ? _settings.SelectedMonitorTarget
            : "Ambas pantallas";
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
        _reapplyTimer.Tick += ReapplyTimer_Tick;
        StateChanged += (_, _) => UpdateWindowStateIcon();
        Loaded += (_, _) =>
        {
            ProfilesScrollViewer.ScrollToTop();
            UpdateWindowStateIcon();
            CaptureOriginalDisplayState();
            SchedulePersistentCorrectionReapply(immediate: true);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        FitWindowToCurrentMonitor(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _consoleDisplayNotification = NativeMethods.RegisterPowerSettingNotification(
            handle, NativeMethods.GuidConsoleDisplayState, NativeMethods.DeviceNotifyWindowHandle);
        _sessionDisplayNotification = NativeMethods.RegisterPowerSettingNotification(
            handle, NativeMethods.GuidSessionDisplayStatus, NativeMethods.DeviceNotifyWindowHandle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _reapplyTimer.Stop();
        _livePreviewTimer.Stop();
        _windowSource?.RemoveHook(WindowMessageHook);
        if (_consoleDisplayNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterPowerSettingNotification(_consoleDisplayNotification);
        }
        if (_sessionDisplayNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterPowerSettingNotification(_sessionDisplayNotification);
        }
        base.OnClosed(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ApplyResponsiveLayout(enforceCompactMode: true);
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
        IsDarkTheme = !IsDarkTheme;
    }

    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        _leftPanelRequested = !_isLeftPanelVisible;
        if (_leftPanelRequested && ActualWidth < 1120)
        {
            _rightPanelRequested = false;
        }
        SavePanelPreferences();
        ApplyResponsiveLayout(enforceCompactMode: false);
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        _rightPanelRequested = !_isRightPanelVisible;
        if (_rightPanelRequested && ActualWidth < 1120)
        {
            _leftPanelRequested = false;
        }
        SavePanelPreferences();
        ApplyResponsiveLayout(enforceCompactMode: false);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyResponsiveLayout(enforceCompactMode: true);
    }

    private void ApplyResponsiveLayout(bool enforceCompactMode)
    {
        var showLeft = _leftPanelRequested;
        var showRight = _rightPanelRequested;

        if (enforceCompactMode)
        {
            if (ActualWidth < 850)
            {
                showLeft = false;
                showRight = false;
            }
            else if (ActualWidth < 1120)
            {
                showRight = false;
            }
        }

        LeftSidebarColumn.Width = showLeft ? new GridLength(220) : new GridLength(0);
        RightInspectorColumn.Width = showRight ? new GridLength(320) : new GridLength(0);
        LeftSidebar.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed;
        RightInspector.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        _isLeftPanelVisible = showLeft;
        _isRightPanelVisible = showRight;

        TitleStatus.Visibility = ActualWidth >= 1280 ? Visibility.Visible : Visibility.Collapsed;
        TitleWebsiteButton.Visibility = ActualWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed;
        TitleLanguageSelector.Visibility = ActualWidth >= 960 ? Visibility.Visible : Visibility.Collapsed;
        BrandSubtitle.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPanelTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPanelTooltip)));
    }

    private void SavePanelPreferences()
    {
        _settings.IsLeftPanelOpen = _leftPanelRequested;
        _settings.IsRightPanelOpen = _rightPanelRequested;
        SaveSettings();
    }

    private void FitWindowToCurrentMonitor(IntPtr handle)
    {
        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return;

        var fromDevice = _windowSource?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top));
        var bottomRight = fromDevice.Transform(new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
        var workWidth = Math.Max(1, bottomRight.X - topLeft.X);
        var workHeight = Math.Max(1, bottomRight.Y - topLeft.Y);
        const double outerMargin = 12;

        MinWidth = Math.Min(720, Math.Max(1, workWidth - outerMargin * 2));
        MinHeight = Math.Min(560, Math.Max(1, workHeight - outerMargin * 2));
        Width = Math.Max(MinWidth, Math.Min(1280, workWidth - outerMargin * 2));
        Height = Math.Max(MinHeight, Math.Min(820, workHeight - outerMargin * 2));
        Left = topLeft.X + Math.Max(outerMargin, (workWidth - Width) / 2);
        Top = topLeft.Y + Math.Max(outerMargin, (workHeight - Height) / 2);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => IsSettingsOpen = true;

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => IsSettingsOpen = false;

    private void OpenCodigoLimpio_Click(object sender, RoutedEventArgs e) => OpenUrl("https://codigolimpio.com.co/");

    private void OpenCodigoLimpioGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/CodigoLimpioCo/luma-profiles");

    private void About_Click(object sender, RoutedEventArgs e) => IsAboutOpen = true;

    private void CloseAbout_Click(object sender, RoutedEventArgs e) => IsAboutOpen = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && (IsAboutOpen || IsSettingsOpen))
        {
            IsAboutOpen = false;
            IsSettingsOpen = false;
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftPanelTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightPanelTooltip)));
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
        CaptureOriginalDisplayState();
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
        SaveMonitorCorrections(MonitorService.NeutralProfile(), result, applyImageControls: false);
        StatusMessage = FormatResult(T("Neutralized"), result);
    }

    private void RestoreOriginalDisplayState_Click(object sender, RoutedEventArgs e)
    {
        StatusMessage = T("RestoringOriginalState");
        var result = _monitorService.RestoreOriginal(
            _settings.OriginalMonitorStates, SelectedMonitorTarget, _settings.OriginalPowerPlan);

        foreach (var monitor in result.AppliedMonitors)
        {
            _settings.MonitorCorrections.RemoveAll(item =>
                item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase) ||
                item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        }
        if (result.DisplayCount > 0)
        {
            foreach (var profile in Profiles) profile.IsActive = false;
            _profileStore.Save(Profiles);
        }
        SaveSettings();
        StatusMessage = FormatResult(T("OriginalStateRestored"), result);
    }

    private void Apply(DisplayProfile profile)
    {
        CaptureOriginalDisplayState();
        StatusMessage = L("Applying", profile.DisplayName);
        var result = _monitorService.Apply(profile, SelectedMonitorTarget);
        foreach (var item in Profiles) item.IsActive = false;
        profile.IsActive = result.DisplayCount > 0;
        _profileStore.Save(Profiles);
        SaveMonitorCorrections(profile, result, applyImageControls: true);
        StatusMessage = FormatResult(L("Applied", profile.DisplayName), result);
        if (profile.IsHdr)
        {
            StatusMessage += T("HdrReminder");
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

    private void CaptureOriginalDisplayState()
    {
        var captured = _monitorService.CaptureOriginalStates(
            _settings.OriginalMonitorStates.Select(item => item.MonitorId));
        var changed = false;
        foreach (var state in captured)
        {
            _settings.OriginalMonitorStates.Add(state);
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan))
        {
            _settings.OriginalPowerPlan = _monitorService.GetActivePowerPlan();
            changed |= !string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan);
        }
        if (changed) SaveSettings();
    }

    private void SchedulePersistentCorrectionReapply(bool immediate = false)
    {
        if (_settings.MonitorCorrections.Count == 0) return;
        _reapplyTimer.Stop();
        if (immediate)
        {
            ReapplyPersistentCorrections();
        }
        else
        {
            _reapplyTimer.Start();
        }
    }

    private void ReapplyTimer_Tick(object? sender, EventArgs e)
    {
        _reapplyTimer.Stop();
        ReapplyPersistentCorrections();
    }

    private async void ReapplyPersistentCorrections()
    {
        if (_isReapplyingCorrections)
        {
            _reapplyRequested = true;
            return;
        }

        _isReapplyingCorrections = true;
        var corrections = _settings.MonitorCorrections.ToArray();
        try
        {
            var result = await Task.Run(() => _monitorService.Reapply(corrections));
            if (result.DisplayCount > 0)
            {
                StatusMessage = FormatResult(T("PersistentCorrectionRestored"), result);
            }
        }
        catch (Exception exception)
        {
            StatusMessage = L("PersistentCorrectionFailed", exception.Message);
        }
        finally
        {
            _isReapplyingCorrections = false;
            if (_reapplyRequested)
            {
                _reapplyRequested = false;
                SchedulePersistentCorrectionReapply();
            }
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDeviceChange)
        {
            SchedulePersistentCorrectionReapply();
        }
        else if (message == NativeMethods.WmPowerBroadcast)
        {
            var powerEvent = wParam.ToInt32();
            if (powerEvent is NativeMethods.PbtApmResumeAutomatic or NativeMethods.PbtApmResumeSuspend)
            {
                SchedulePersistentCorrectionReapply();
            }
            else if (powerEvent == NativeMethods.PbtPowerSettingChange && lParam != IntPtr.Zero)
            {
                var setting = Marshal.PtrToStructure<NativeMethods.PowerBroadcastSetting>(lParam);
                if (setting.DataLength > 0 && setting.Data != 0)
                {
                    SchedulePersistentCorrectionReapply();
                }
            }
        }
        return IntPtr.Zero;
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            StatusMessage = T("SettingsSaveFailed");
        }
    }

    private static bool IsKnownMonitorTarget(string target) =>
        target is "Ambas pantallas" or "Pantalla 1" or "Pantalla 2";

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

    private static class NativeMethods
    {
        internal const uint MonitorDefaultToNearest = 2;
        internal const int WmDisplayChange = 0x007E;
        internal const int WmDeviceChange = 0x0219;
        internal const int WmPowerBroadcast = 0x0218;
        internal const int PbtApmResumeSuspend = 0x0007;
        internal const int PbtApmResumeAutomatic = 0x0012;
        internal const int PbtPowerSettingChange = 0x8013;
        internal const int DeviceNotifyWindowHandle = 0;
        internal static readonly Guid GuidConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
        internal static readonly Guid GuidSessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerBroadcastSetting
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, in Guid powerSettingGuid, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
    }
}
