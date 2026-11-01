using System.Runtime.InteropServices;
using System.Text;

namespace LumaProfiles.Services;

/// <summary>Tells whether the foreground window fills a whole display (a game or video in full screen).</summary>
public static class FullscreenDetector
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool IsForegroundFullscreen()
    {
        try
        {
            var state = NativeMethods.SHQueryUserNotificationState(out var notification) == 0 ? notification : 0;
            // QUNS_RUNNING_D3D_FULL_SCREEN: an exclusive full-screen Direct3D app (most games).
            if (state == 3) return true;

            var window = NativeMethods.GetForegroundWindow();
            return window != IntPtr.Zero && CoversMonitor(window);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Full-screen detection failed.", exception);
            return false;
        }
    }

    private static bool CoversMonitor(IntPtr window)
    {
        if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return false;

        var name = new StringBuilder(64);
        NativeMethods.GetClassName(window, name, name.Capacity);
        if (ShellClasses.Contains(name.ToString(), StringComparer.Ordinal)) return false;

        if (!NativeMethods.GetWindowRect(window, out var rect)) return false;
        var monitor = NativeMethods.MonitorFromWindow(window, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)) return false;

        return Covers(rect, info.Monitor);
    }

    /// <summary>True when the window is at least as large as the display on every side (borderless full screen).</summary>
    internal static bool Covers(NativeMethods.Rect window, NativeMethods.Rect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top &&
        window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("shell32.dll")]
        internal static extern int SHQueryUserNotificationState(out int state);
    }
}
