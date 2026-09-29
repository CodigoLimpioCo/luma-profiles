using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

// Display enumeration and target matching ("Ambas pantallas" or "Pantalla 1, Pantalla 3").
public sealed partial class MonitorService
{
    public const string AllDisplaysTarget = "Ambas pantallas";

    public const string DisplayTargetPrefix = "Pantalla ";

    private const uint MonitorInfoPrimary = 1;

    public IReadOnlyList<DisplayInfo> GetDisplays() => EnumerateMonitors()
        .Select(monitor => new DisplayInfo(
            DisplayNumber(monitor.DeviceName), monitor.DeviceName, monitor.MonitorId,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height, monitor.IsPrimary))
        .OrderBy(display => display.Number)
        .ToList();

    /// <summary>
    /// A target is "Ambas pantallas" (every display) or a comma separated list like "Pantalla 1, Pantalla 3".
    /// </summary>
    internal static bool MatchesTarget(string deviceName, string target)
    {
        if (target == AllDisplaysTarget) return true;

        var number = DisplayNumber(deviceName);
        foreach (var part in target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith(DisplayTargetPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(part[DisplayTargetPrefix.Length..], out var wanted) && wanted == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Extracts N from a device name such as \.\DISPLAY3; 0 when it has no number.</summary>
    internal static int DisplayNumber(string deviceName)
    {
        var index = deviceName.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && int.TryParse(deviceName[(index + "DISPLAY".Length)..], out var number) ? number : 0;
    }

    private static IReadOnlyList<ConnectedMonitor> EnumerateMonitors()
    {
        var monitors = new List<ConnectedMonitor>();
        NativeMethods.MonitorEnumProc callback = (handle, _, _, _) =>
        {
            var info = NativeMethods.MONITORINFOEX.Create();
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                monitors.Add(new ConnectedMonitor(
                    handle, GetMonitorId(info.szDevice), info.szDevice,
                    info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top,
                    (info.dwFlags & MonitorInfoPrimary) != 0));
            }
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return monitors;
    }

    private static string GetMonitorId(string deviceName)
    {
        var device = NativeMethods.DISPLAY_DEVICE.Create();
        return NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, 0) && !string.IsNullOrWhiteSpace(device.DeviceID)
            ? device.DeviceID
            : deviceName;
    }
}
