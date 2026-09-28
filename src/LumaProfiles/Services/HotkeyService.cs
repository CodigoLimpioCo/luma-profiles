using System.Runtime.InteropServices;

namespace LumaProfiles.Services;

public enum HotkeyAction { NextProfile = 1, PreviousProfile = 2, Neutralize = 3 }

/// <summary>Registers system-wide Ctrl+Alt hotkeys against a window handle.</summary>
public sealed class HotkeyService : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkLeft = 0x25;
    private const uint VkRight = 0x27;
    private const uint VkZero = 0x30;

    private readonly List<int> _registered = [];
    private IntPtr _handle;

    public void Register(IntPtr windowHandle)
    {
        Unregister();
        _handle = windowHandle;
        var modifiers = ModControl | ModAlt | ModNoRepeat;
        TryRegister(HotkeyAction.NextProfile, modifiers, VkRight);
        TryRegister(HotkeyAction.PreviousProfile, modifiers, VkLeft);
        TryRegister(HotkeyAction.Neutralize, modifiers, VkZero);
    }

    public void Unregister()
    {
        foreach (var id in _registered) UnregisterHotKey(_handle, id);
        _registered.Clear();
    }

    public static HotkeyAction? FromMessage(int message, IntPtr wParam) =>
        message == WmHotkey && Enum.IsDefined(typeof(HotkeyAction), wParam.ToInt32())
            ? (HotkeyAction)wParam.ToInt32()
            : null;

    public void Dispose() => Unregister();

    private void TryRegister(HotkeyAction action, uint modifiers, uint key)
    {
        if (RegisterHotKey(_handle, (int)action, modifiers, key))
        {
            _registered.Add((int)action);
        }
        else
        {
            AppLog.Warn($"Hotkey for {action} is already in use by another application.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
