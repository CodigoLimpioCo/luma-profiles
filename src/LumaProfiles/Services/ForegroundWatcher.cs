using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumaProfiles.Services;

/// <summary>
/// Raises an event whenever another application takes the foreground (event-driven) or the foreground window
/// enters or leaves full screen (checked every two seconds: games often go full screen after taking focus).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly WinEventProc _callback;
    private readonly System.Windows.Threading.DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IntPtr _hook;
    private string? _lastProcess;
    private bool _lastFullscreen;

    public ForegroundWatcher()
    {
        _callback = OnForegroundChanged;
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
    }

    /// <summary>Process name of the foreground window (null when unknown) and whether it fills its display.</summary>
    public event Action<string?, bool>? ForegroundProcessChanged;

    /// <summary>Must be called on a thread with a message loop (the UI thread).</summary>
    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        if (_hook == IntPtr.Zero) AppLog.Warn("Foreground watcher could not be installed; app rules are disabled.");
        _fullscreenTimer.Start();
    }

    public void Dispose()
    {
        _fullscreenTimer.Stop();
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint timestamp)
    {
        string? name = null;
        try
        {
            if (GetWindowThreadProcessId(window, out var processId) != 0 && processId != 0)
            {
                using var process = Process.GetProcessById((int)processId);
                name = process.ProcessName;
            }
        }
        catch
        {
            // The process may have exited or be inaccessible; treat it as unknown.
        }

        _lastProcess = name;
        _lastFullscreen = name is not null && FullscreenDetector.IsForegroundFullscreen();
        ForegroundProcessChanged?.Invoke(name, _lastFullscreen);
    }

    private void CheckFullscreen()
    {
        if (_lastProcess is null) return;
        var fullscreen = FullscreenDetector.IsForegroundFullscreen();
        if (fullscreen == _lastFullscreen) return;
        _lastFullscreen = fullscreen;
        ForegroundProcessChanged?.Invoke(_lastProcess, fullscreen);
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint timestamp);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
