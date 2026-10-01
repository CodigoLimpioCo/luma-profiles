using System.Runtime.InteropServices;
using LumaProfiles.Models;

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
    private const uint VkF1 = 0x70;
    private const int ProfileHotkeyIdBase = 100;

    private static readonly string[] SelectableKeys =
        [.. Enumerable.Range(1, 9).Select(digit => digit.ToString()), .. Enumerable.Range(1, 12).Select(number => $"F{number}")];

    private readonly List<int> _registered = [];
    private readonly Dictionary<int, string> _profileByHotkeyId = [];
    private IntPtr _handle;

    /// <summary>Keys offered for profile shortcuts, always combined with Ctrl+Alt (0 and the arrows stay reserved).</summary>
    public static IReadOnlyList<string> AvailableKeys => SelectableKeys;

    public static bool TryParseKey(string? key, out uint virtualKey)
    {
        virtualKey = 0;
        var index = Array.IndexOf(SelectableKeys, key);
        if (index < 0) return false;
        virtualKey = index < 9 ? VkZero + 1 + (uint)index : VkF1 + (uint)(index - 9);
        return true;
    }

    public void Register(IntPtr windowHandle, IEnumerable<ProfileHotkey>? profileHotkeys = null)
    {
        Unregister();
        _handle = windowHandle;
        var modifiers = ModControl | ModAlt | ModNoRepeat;
        TryRegister(HotkeyAction.NextProfile, modifiers, VkRight);
        TryRegister(HotkeyAction.PreviousProfile, modifiers, VkLeft);
        TryRegister(HotkeyAction.Neutralize, modifiers, VkZero);

        var id = ProfileHotkeyIdBase;
        foreach (var hotkey in profileHotkeys ?? [])
        {
            if (!TryParseKey(hotkey.Key, out var virtualKey)) continue;
            if (TryRegister($"profile {hotkey.ProfileId}", id, modifiers, virtualKey)) _profileByHotkeyId[id] = hotkey.ProfileId;
            id++;
        }
    }

    public void Unregister()
    {
        foreach (var id in _registered) UnregisterHotKey(_handle, id);
        _registered.Clear();
        _profileByHotkeyId.Clear();
    }

    /// <summary>The profile bound to a WM_HOTKEY message, if it is one of the per-profile shortcuts.</summary>
    public string? ProfileFromMessage(int message, IntPtr wParam) =>
        message == WmHotkey && _profileByHotkeyId.TryGetValue(wParam.ToInt32(), out var profileId) ? profileId : null;

    public static HotkeyAction? FromMessage(int message, IntPtr wParam) =>
        message == WmHotkey && Enum.IsDefined(typeof(HotkeyAction), wParam.ToInt32())
            ? (HotkeyAction)wParam.ToInt32()
            : null;

    public void Dispose() => Unregister();

    private void TryRegister(HotkeyAction action, uint modifiers, uint key) =>
        TryRegister(action.ToString(), (int)action, modifiers, key);

    private bool TryRegister(string name, int id, uint modifiers, uint key)
    {
        if (RegisterHotKey(_handle, id, modifiers, key))
        {
            _registered.Add(id);
            return true;
        }

        AppLog.Warn($"Hotkey for {name} is already in use by another application.");
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
