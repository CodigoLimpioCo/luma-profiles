using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaProfiles.Services;

/// <summary>Flashes each display's number on that display, like "Identify" in Windows display settings.</summary>
public static class DisplayIdentifier
{
    private const int VisibleMilliseconds = 2500;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr TopMost = new(-1);
    private static readonly List<Window> Overlays = [];
    private static DispatcherTimer? _timer;

    public static void Show(IReadOnlyList<DisplayInfo> displays)
    {
        CloseAll();
        foreach (var display in displays)
        {
            try
            {
                Overlays.Add(CreateOverlay(display));
            }
            catch (Exception exception)
            {
                AppLog.Warn($"Could not show the identifier for display {display.Number}.", exception);
            }
        }

        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibleMilliseconds) };
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Stop();
        _timer.Start();
    }

    private static void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        CloseAll();
    }

    private static void CloseAll()
    {
        foreach (var overlay in Overlays.ToArray())
        {
            overlay.Close();
        }

        Overlays.Clear();
    }

    private static Window CreateOverlay(DisplayInfo display)
    {
        var accent = Application.Current?.MainWindow?.TryFindResource("AccentBrush") as Brush
                     ?? new SolidColorBrush(Color.FromRgb(0x55, 0xD6, 0xF5));

        var number = new TextBlock
        {
            Text = display.Number.ToString(),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Border
        {
            CornerRadius = new CornerRadius(28),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0B, 0x12, 0x1A)),
            BorderBrush = accent,
            BorderThickness = new Thickness(4),
            Padding = new Thickness(18),
            Child = new Viewbox { Child = number, Stretch = Stretch.Uniform }
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            Content = content
        };
        window.MouseLeftButtonDown += (_, _) => window.Close();
        window.Show();

        // Position in physical pixels so the overlay lands on the right monitor whatever its DPI.
        var size = Math.Max(160, Math.Min(display.Width, display.Height) / 4);
        var x = display.Left + ((display.Width - size) / 2);
        var y = display.Top + ((display.Height - size) / 2);
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, TopMost, x, y, size, size, SwpNoActivate | SwpShowWindow);
        return window;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
