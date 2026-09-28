using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LumaProfiles.Services;
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

    private const double ExpandedSidebarPixels = 220;
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

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.ScrollToTopRequested += (_, _) => ProfilesScrollViewer?.ScrollToTop();
        _foregroundWatcher.ForegroundProcessChanged += _viewModel.OnForegroundProcessChanged;
        StateChanged += (_, _) => PushWindowState();
        Loaded += (_, _) =>
        {
            ProfilesScrollViewer.ScrollToTop();
            ApplyResponsiveLayout(enforceCompactMode: true);
            _viewModel.Initialize();
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

        UpdateHotkeys();
        _foregroundWatcher.Start();
        _tray = new TrayIconService(_viewModel, ShowFromTray, ExitApplication);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
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
                break;
            case nameof(MainViewModel.GlobalHotkeysEnabled):
                UpdateHotkeys();
                break;
            case nameof(MainViewModel.IsSidebarCollapsed):
                ApplyResponsiveLayout(enforceCompactMode: false);
                break;
        }
    }

    private void ApplyTheme()
    {
        var source = _viewModel.IsDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
    }

    private void UpdateHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (_viewModel.GlobalHotkeysEnabled) _hotkeys.Register(handle); else _hotkeys.Unregister();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LeftPanelRequested = !_isLeftPanelVisible;
        if (_viewModel.LeftPanelRequested && ActualWidth < InspectorMinWidth)
        {
            _viewModel.RightPanelRequested = false;
        }
        _viewModel.SavePanelPreferences();
        ApplyResponsiveLayout(enforceCompactMode: false);
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RightPanelRequested = !_isRightPanelVisible;
        if (_viewModel.RightPanelRequested && ActualWidth < InspectorMinWidth)
        {
            _viewModel.LeftPanelRequested = false;
        }
        _viewModel.SavePanelPreferences();
        ApplyResponsiveLayout(enforceCompactMode: false);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyResponsiveLayout(enforceCompactMode: true);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_viewModel.IsAboutOpen || _viewModel.IsSettingsOpen))
        {
            _viewModel.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ApplyResponsiveLayout(bool enforceCompactMode)
    {
        var showLeft = _viewModel.LeftPanelRequested;
        var showRight = _viewModel.RightPanelRequested;

        if (enforceCompactMode)
        {
            if (ActualWidth < BothPanelsHiddenWidth)
            {
                showLeft = false;
                showRight = false;
            }
            else if (ActualWidth < InspectorMinWidth)
            {
                showRight = false;
            }
        }

        // Narrow windows switch the sidebar to icons only; the saved preference is left untouched.
        _viewModel.SetSidebarForcedCompact(showLeft && ActualWidth < CompactSidebarWidth);
        LeftSidebarColumn.Width = showLeft
            ? new GridLength(_viewModel.IsSidebarCollapsed ? CollapsedSidebarPixels : ExpandedSidebarPixels)
            : new GridLength(0);
        RightInspectorColumn.Width = showRight ? new GridLength(320) : new GridLength(0);
        LeftSidebar.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed;
        RightInspector.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        _isLeftPanelVisible = showLeft;
        _isRightPanelVisible = showRight;

        TitleStatus.Visibility = ActualWidth >= 1280 ? Visibility.Visible : Visibility.Collapsed;
        TitleWebsiteButton.Visibility = ActualWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed;
        TitleLanguageSelector.Visibility = ActualWidth >= 960 ? Visibility.Visible : Visibility.Collapsed;
        BrandSubtitle.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
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

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (HotkeyService.FromMessage(message, wParam) is { } hotkey)
        {
            handled = true;
            switch (hotkey)
            {
                case HotkeyAction.NextProfile: _viewModel.CycleProfile(+1); break;
                case HotkeyAction.PreviousProfile: _viewModel.CycleProfile(-1); break;
                case HotkeyAction.Neutralize: _viewModel.ApplyNeutral(); break;
            }
        }
        else if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDeviceChange)
        {
            _viewModel.RequestReapply();
        }
        else if (message == NativeMethods.WmPowerBroadcast)
        {
            var powerEvent = wParam.ToInt32();
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
        return IntPtr.Zero;
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
