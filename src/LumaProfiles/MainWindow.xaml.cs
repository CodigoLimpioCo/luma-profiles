using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LumaProfiles.Services;
using Microsoft.Win32;
using LumaProfiles.ViewModels;

namespace LumaProfiles;

/// <summary>Window chrome, responsive layout and Win32 plumbing; all application state lives in <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ForegroundWatcher _foregroundWatcher = new();
    private readonly HotkeyService _hotkeys = new();
    private TrayIconService? _tray;
    private HwndSource? _windowSource;
    private IntPtr _consoleDisplayNotification;
    private IntPtr _sessionDisplayNotification;
    private bool _isLeftPanelVisible;
    private bool _isRightPanelVisible;
    private bool _isExiting;
    private bool _initialized;
    private PinnedPanel _pinnedPanel;

    private enum PinnedPanel { None, Left, Right }

    private const double MinContentWidth = 520;
    private const double InspectorPixels = 320;
    private const double KeyboardResizeStep = 12;
    private const double CollapsedSidebarPixels = 80;
    private const double CompactSidebarWidth = 1100;
    private const double InspectorMinWidth = 1040;
    private const double BothPanelsHiddenWidth = 760;

    public MainWindow()
    {
        _viewModel = new MainViewModel(new MonitorService(), new ProfileStore(), new ApplicationSettingsStore(), new ShellService());

        InitializeComponent();
        DataContext = _viewModel;
        ApplyTheme();
        ApplyAppearance();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.InspectorRequested += (_, _) =>
        {
            if (!_isRightPanelVisible) SetInspectorOpen(true);
        };
        _viewModel.ScrollToTopRequested += (_, _) => ProfilesScrollViewer?.ScrollToTop();
        _foregroundWatcher.ForegroundProcessChanged += _viewModel.OnForegroundProcessChanged;
        StateChanged += (_, _) => PushWindowState();
        Loaded += (_, _) =>
        {
            ProfilesScrollViewer.ScrollToTop();
            ApplyResponsiveLayout();
            EnsureInitialized();
        };
    }

    /// <summary>Starts the schedule, rules and display watching once, whether the window was shown or never was.</summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        _viewModel.Initialize();
    }

    /// <summary>Runs everything except the visible window: tray icon, hotkeys, schedule and rules (sign-in launch).</summary>
    public void StartInBackground()
    {
        new WindowInteropHelper(this).EnsureHandle();
        EnsureInitialized();
    }

    /// <summary>Windows is signing out or shutting down: close for real instead of hiding to the tray.</summary>
    public void PrepareToExit() => _isExiting = true;

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

        UpdateHotkeys();
        _foregroundWatcher.Start();
        _tray = new TrayIconService(_viewModel, ShowFromTray, ExitApplication);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            switch (_viewModel.DecideOnClose())
            {
                case CloseDecision.Stay:
                    e.Cancel = true;
                    return;
                case CloseDecision.Hide:
                    e.Cancel = true;
                    Hide();
                    return;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        _viewModel.Shutdown();
        _foregroundWatcher.Dispose();
        _hotkeys.Dispose();
        _tray?.Dispose();
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsDarkTheme):
                ApplyTheme();
                ApplyAppearance();
                break;
            case nameof(MainViewModel.IsBusy):
                Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.AppStarting : null;
                break;
            case nameof(MainViewModel.AccentKey):
            case nameof(MainViewModel.ScrollBarThickness):
            case nameof(MainViewModel.FontFamilyName):
                ApplyAppearance();
                break;
            case nameof(MainViewModel.UiScalePercent):
                ApplyAppearance();
                ApplyResponsiveLayout();
                break;
            case nameof(MainViewModel.LeftPanelRequested):
            case nameof(MainViewModel.RightPanelRequested):
                ApplyResponsiveLayout();
                break;
            case nameof(MainViewModel.GlobalHotkeysEnabled):
            case nameof(MainViewModel.ProfileHotkeyBindings):
                UpdateHotkeys();
                break;
            case nameof(MainViewModel.SidebarWidth):
                ApplyResponsiveLayout();
                break;
            case nameof(MainViewModel.IsSidebarCollapsed):
                ApplyResponsiveLayout();
                break;
        }
    }

    private void ApplyTheme()
    {
        var source = _viewModel.IsDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
    }

    /// <summary>Accent color and scrollbar width come from settings and override the theme dictionary.</summary>
    private void ApplyAppearance()
    {
        var accent = _viewModel.Accent;
        Resources["AccentBrush"] = CreateBrush(accent.Accent);
        Resources["AccentHoverBrush"] = CreateBrush(accent.Hover);
        Resources["AccentTextBrush"] = CreateBrush(_viewModel.IsDarkTheme ? accent.TextOnDark : accent.TextOnLight);
        FontFamily = new FontFamily(_viewModel.FontFamilyName);
        Resources["UiScaleTransform"] = new ScaleTransform(_viewModel.UiScaleFactor, _viewModel.UiScaleFactor);
        Resources["ScrollThumbWidth"] = _viewModel.ScrollBarThickness;
        Resources["ScrollBarTrackWidth"] = _viewModel.ScrollBarThickness + 6;
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void SystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.BeginInvoke(_viewModel.RefreshSystemTheme);
        }
    }

    private void UpdateHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (_viewModel.GlobalHotkeysEnabled) _hotkeys.Register(handle, _viewModel.ProfileHotkeyBindings); else _hotkeys.Unregister();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        ApplyResponsiveLayout();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>Sidebar entries react to clicks, arrow keys and automation alike (not only to mouse clicks).</summary>
    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string category } && _viewModel.SidebarCategory != category)
        {
            _viewModel.SelectCategoryCommand.Execute(category);
        }
    }

    private void UiScale_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _viewModel.CommitUiScale();

    private void UiScale_MouseUp(object sender, MouseButtonEventArgs e) => _viewModel.CommitUiScale();

    private void UiScale_KeyUp(object sender, KeyEventArgs e) => _viewModel.CommitUiScale();

    /// <summary>The widest the menu may get while the content area keeps a usable width.</summary>
    private double MaxSidebarForWindow =>
        LayoutWidth - MinContentWidth - (_isRightPanelVisible ? InspectorPixels : 0);

    private void SidebarResizer_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) =>
        _viewModel.ResizeSidebar(e.HorizontalChange, MaxSidebarForWindow);

    private void SidebarResizer_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _viewModel.CommitSidebarWidth();

    private void SidebarResizer_DoubleClick(object sender, MouseButtonEventArgs e) => _viewModel.ResetSidebarWidth();

    /// <summary>Keyboard access: arrows resize, Home restores the default width.</summary>
    private void SidebarResizer_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.ResizeSidebar(-KeyboardResizeStep, MaxSidebarForWindow);
                break;
            case Key.Right:
                _viewModel.ResizeSidebar(KeyboardResizeStep, MaxSidebarForWindow);
                break;
            case Key.Home:
                _viewModel.ResetSidebarWidth();
                break;
            default:
                return;
        }

        _viewModel.CommitSidebarWidth();
        e.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        var open = !_isLeftPanelVisible;
        _viewModel.LeftPanelRequested = open;
        _pinnedPanel = open && IsNarrowForInspector ? PinnedPanel.Left : PinnedPanel.None;
        _viewModel.SavePanelPreferences();
        ApplyResponsiveLayout();
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e) => SetInspectorOpen(!_isRightPanelVisible);

    /// <summary>Opens the adjustment panel, e.g. when the user clicks "Ajustar" while it is hidden.</summary>
    private void SetInspectorOpen(bool open)
    {
        _viewModel.RightPanelRequested = open;
        _pinnedPanel = open && IsNarrowForInspector ? PinnedPanel.Right : PinnedPanel.None;
        _viewModel.SavePanelPreferences();
        ApplyResponsiveLayout();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyResponsiveLayout();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_viewModel.IsAboutOpen || _viewModel.IsSettingsOpen))
        {
            _viewModel.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Window width in the units of the scaled content (a 150% interface has less room).</summary>
    private double LayoutWidth => ActualWidth / Math.Max(0.5, _viewModel.UiScaleFactor);

    private bool IsNarrowForInspector => LayoutWidth < InspectorMinWidth;

    private void ApplyResponsiveLayout()
    {
        var narrow = IsNarrowForInspector;
        if (!narrow) _pinnedPanel = PinnedPanel.None;

        var showLeft = _viewModel.LeftPanelRequested;
        var showRight = _viewModel.RightPanelRequested;

        // Small windows drop panels unless the user opened one explicitly ("pinned"); the saved preference is untouched.
        if (LayoutWidth < BothPanelsHiddenWidth)
        {
            showLeft = _pinnedPanel == PinnedPanel.Left;
            showRight = _pinnedPanel == PinnedPanel.Right;
        }
        else if (narrow)
        {
            showRight = _pinnedPanel == PinnedPanel.Right;
        }

        if (narrow && _pinnedPanel == PinnedPanel.Right) showLeft = false;
        if (narrow && _pinnedPanel == PinnedPanel.Left) showRight = false;

        // Narrow windows switch the sidebar to icons only; the saved preference is left untouched.
        _viewModel.SetSidebarForcedCompact(showLeft && LayoutWidth < CompactSidebarWidth);
        LeftSidebarColumn.Width = showLeft
            ? new GridLength(_viewModel.IsSidebarCollapsed ? CollapsedSidebarPixels : _viewModel.SidebarWidth)
            : new GridLength(0);
        RightInspectorColumn.Width = showRight ? new GridLength(320) : new GridLength(0);
        LeftSidebar.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed;
        RightInspector.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        _isLeftPanelVisible = showLeft;
        _isRightPanelVisible = showRight;

        TitleStatus.Visibility = LayoutWidth >= 1280 ? Visibility.Visible : Visibility.Collapsed;
        TitleWebsiteButton.Visibility = LayoutWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed;
        TitleLanguageSelector.Visibility = LayoutWidth >= 960 ? Visibility.Visible : Visibility.Collapsed;
        BrandSubtitle.Visibility = LayoutWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        PushWindowState();
    }

    private void PushWindowState() =>
        _viewModel.UpdateWindowState(WindowState == WindowState.Maximized, _isLeftPanelVisible, _isRightPanelVisible);

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

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>A borderless window maximizes past the work area (under the taskbar); clamp it to the monitor's work area.</summary>
    private static void ConstrainMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return;

        var minMax = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
        var work = monitorInfo.WorkArea;
        var screen = monitorInfo.Monitor;
        minMax.MaxPosition.X = work.Left - screen.Left;
        minMax.MaxPosition.Y = work.Top - screen.Top;
        minMax.MaxSize.X = work.Right - work.Left;
        minMax.MaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(minMax, lParam, true);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmGetMinMaxInfo)
        {
            ConstrainMaximizedBounds(hwnd, lParam);
            handled = true;
        }
        else if (HandleHotkey(message, wParam))
        {
            handled = true;
        }
        else if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDeviceChange)
        {
            _viewModel.OnDisplaysChanged();
            _viewModel.RequestReapply();
        }
        else if (message == NativeMethods.WmPowerBroadcast)
        {
            HandlePowerBroadcast(wParam.ToInt32(), lParam);
        }
        return IntPtr.Zero;
    }

    private bool HandleHotkey(int message, IntPtr wParam)
    {
        if (_hotkeys.ProfileFromMessage(message, wParam) is { } profileId)
        {
            _viewModel.ApplyProfileById(profileId);
            return true;
        }

        if (HotkeyService.FromMessage(message, wParam) is not { } hotkey) return false;
        switch (hotkey)
        {
            case HotkeyAction.NextProfile: _viewModel.CycleProfile(+1); break;
            case HotkeyAction.PreviousProfile: _viewModel.CycleProfile(-1); break;
            case HotkeyAction.Neutralize: _viewModel.ApplyNeutral(); break;
        }

        return true;
    }

    private void HandlePowerBroadcast(int powerEvent, IntPtr lParam)
    {
        if (powerEvent is NativeMethods.PbtApmResumeAutomatic or NativeMethods.PbtApmResumeSuspend)
        {
            _viewModel.RequestReapply();
        }
        else if (powerEvent == NativeMethods.PbtPowerSettingChange && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<NativeMethods.PowerBroadcastSetting>(lParam);
            if (setting.DataLength > 0 && setting.Data != 0)
            {
                _viewModel.RequestReapply();
            }
        }
    }

    private static class NativeMethods
    {
        internal const uint MonitorDefaultToNearest = 2;
        internal const int WmGetMinMaxInfo = 0x0024;
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
        internal struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
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
