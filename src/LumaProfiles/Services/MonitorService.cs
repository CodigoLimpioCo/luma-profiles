using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public sealed class MonitorService
{
    private static readonly byte[] ManagedVcpCodes = [0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x87, 0x8A, 0x89];
    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public ApplyResult Apply(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: true);

    public ApplyResult Preview(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: false);

    public IReadOnlyList<OriginalMonitorState> CaptureOriginalStates(IEnumerable<string> knownMonitorIds)
    {
        var known = knownMonitorIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var captured = new List<OriginalMonitorState>();

        foreach (var monitor in EnumerateMonitors().Where(item => !known.Contains(item.MonitorId)))
        {
            var state = new OriginalMonitorState
            {
                MonitorId = monitor.MonitorId,
                DeviceName = monitor.DeviceName,
                GammaRamp = ReadGammaRamp(monitor.DeviceName) ?? []
            };

            if (NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out var count) && count > 0)
            {
                var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
                if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, physicalMonitors))
                {
                    try
                    {
                        for (var index = 0; index < physicalMonitors.Length; index++)
                        {
                            var physical = physicalMonitors[index];
                            var physicalState = new OriginalPhysicalMonitorState
                            {
                                Index = index,
                                Description = physical.szPhysicalMonitorDescription
                            };
                            foreach (var code in ManagedVcpCodes)
                            {
                                if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                                    physical.hPhysicalMonitor, code, out _, out var currentValue, out _))
                                {
                                    physicalState.Values.Add(new OriginalVcpValue { Code = code, Value = currentValue });
                                }
                            }
                            state.PhysicalMonitors.Add(physicalState);
                        }
                    }
                    finally
                    {
                        NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
                    }
                }
            }

            if (state.GammaRamp.Length == 768 || state.PhysicalMonitors.Any(item => item.Values.Count > 0))
            {
                captured.Add(state);
            }
        }

        return captured;
    }

    public string? GetActivePowerPlan()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var schemePointer) != 0 || schemePointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(schemePointer).ToString();
        }
        finally
        {
            NativeMethods.LocalFree(schemePointer);
        }
    }

    public ApplyResult RestoreOriginal(
        IEnumerable<OriginalMonitorState> originalStates, string target, string? originalPowerPlan)
    {
        var saved = originalStates.ToList();
        var failures = new List<string>();
        var applied = new List<AppliedMonitor>();

        foreach (var monitor in EnumerateMonitors().Where(item => MatchesTarget(item.DeviceName, target)))
        {
            var state = saved.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.MonitorId) &&
                    item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase))
                ?? saved.FirstOrDefault(item =>
                    item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (state is null) continue;

            RestoreDdc(monitor.Handle, state, failures);
            if (state.GammaRamp.Length == 768 && !ApplyGammaRamp(monitor.DeviceName, state.GammaRamp))
            {
                failures.Add($"No fue posible restaurar la gamma original en {monitor.DeviceName}.");
            }
            applied.Add(new AppliedMonitor(monitor.MonitorId, monitor.DeviceName));
        }

        if (target == "Ambas pantallas" && !string.IsNullOrWhiteSpace(originalPowerPlan))
        {
            SetPowerPlan(originalPowerPlan, failures);
        }
        if (applied.Count == 0)
        {
            failures.Add("No se encontró un estado original guardado para el destino seleccionado.");
        }

        return new ApplyResult(applied.Count, failures, applied);
    }

    public ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections)
    {
        var saved = corrections.ToList();
        var failures = new List<string>();
        var applied = new List<AppliedMonitor>();

        foreach (var monitor in EnumerateMonitors())
        {
            var correction = saved.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.MonitorId) &&
                    item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase))
                ?? saved.FirstOrDefault(item =>
                    item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (correction is null) continue;

            var profile = correction.ToProfile();
            ApplyDdc(monitor.Handle, profile, failures, correction.ApplyImageControls);
            if (!ApplyGamma(monitor.DeviceName, profile.Gamma, profile.Red, profile.Green, profile.Blue))
            {
                failures.Add($"No fue posible aplicar gamma en {monitor.DeviceName}.");
            }

            applied.Add(new AppliedMonitor(monitor.MonitorId, monitor.DeviceName));
        }

        return new ApplyResult(applied.Count, failures, applied);
    }

    private static ApplyResult ApplyCore(DisplayProfile profile, string target, bool updatePowerPlan)
    {
        var failures = new List<string>();
        var appliedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var monitors = new List<AppliedMonitor>();
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = NativeMethods.MONITORINFOEX.Create();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !MatchesTarget(info.szDevice, target))
            {
                return true;
            }

            appliedDisplays.Add(info.szDevice);
            monitors.Add(new AppliedMonitor(GetMonitorId(info.szDevice), info.szDevice));
            ApplyDdc(monitor, profile, failures, applyImageControls: true);
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        foreach (var display in appliedDisplays)
        {
            if (!ApplyGamma(display, profile.Gamma, profile.Red, profile.Green, profile.Blue))
            {
                failures.Add($"No fue posible aplicar gamma en {display}.");
            }
        }

        if (updatePowerPlan && target == "Ambas pantallas")
        {
            SetPowerPlan(profile.PowerPlan == "HighPerformance" ? HighPerformancePlan : BalancedPlan, failures);
        }

        if (appliedDisplays.Count == 0)
        {
            failures.Add("No se encontró una pantalla activa para el destino seleccionado.");
        }

        return new ApplyResult(appliedDisplays.Count, failures, monitors);
    }

    public ApplyResult RestoreNeutral(string target)
    {
        var neutral = NeutralProfile();
        var failures = new List<string>();
        var applied = new List<AppliedMonitor>();
        foreach (var monitor in EnumerateMonitors().Where(item => MatchesTarget(item.DeviceName, target)))
        {
            ApplyDdc(monitor.Handle, neutral, failures, applyImageControls: false);
            if (!ApplyGamma(monitor.DeviceName, 1.0, 1.0, 1.0, 1.0))
            {
                failures.Add($"No fue posible aplicar gamma en {monitor.DeviceName}.");
            }
            applied.Add(new AppliedMonitor(monitor.MonitorId, monitor.DeviceName));
        }

        if (applied.Count == 0)
        {
            failures.Add("No se encontró una pantalla activa para el destino seleccionado.");
        }

        return new ApplyResult(applied.Count, failures, applied);
    }

    public static MonitorColorCorrection CreateCorrection(
        DisplayProfile profile, AppliedMonitor monitor, bool applyImageControls = true) => new()
    {
        MonitorId = monitor.MonitorId,
        DeviceName = monitor.DeviceName,
        ProfileId = profile.Id,
        ProfileName = profile.Name,
        ApplyImageControls = applyImageControls,
        Brightness = profile.Brightness,
        Contrast = profile.Contrast,
        Saturation = profile.Saturation,
        Hue = profile.Hue,
        Gamma = profile.Gamma,
        Red = profile.Red,
        Green = profile.Green,
        Blue = profile.Blue,
        ColorTemperature = profile.ColorTemperature
    };

    public static DisplayProfile NeutralProfile() => new()
    {
        Id = "neutral-repair",
        Name = "Neutro",
        Category = "Sistema",
        Description = "Gamma y balance RGB neutros",
        PreviewStart = "#000000",
        PreviewEnd = "#000000",
        Brightness = 80,
        Contrast = 80,
        Saturation = 50,
        Hue = 0,
        Gamma = 1.0,
        Red = 1.0,
        Green = 1.0,
        Blue = 1.0,
        ColorTemperature = "Neutro 6500 K"
    };

    private static bool MatchesTarget(string deviceName, string target) =>
        target == "Ambas pantallas" ||
        (target == "Pantalla 1" && deviceName.EndsWith("DISPLAY1", StringComparison.OrdinalIgnoreCase)) ||
        (target == "Pantalla 2" && deviceName.EndsWith("DISPLAY2", StringComparison.OrdinalIgnoreCase));

    private static void ApplyDdc(IntPtr monitor, DisplayProfile profile, List<string> failures, bool applyImageControls)
    {
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            failures.Add("La pantalla no expone controles DDC/CI.");
            return;
        }

        var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
        {
            failures.Add("No fue posible abrir los controles físicos de la pantalla.");
            return;
        }

        try
        {
            foreach (var physical in physicalMonitors)
            {
                if (applyImageControls)
                {
                    SetVcp(physical.hPhysicalMonitor, 0x10, (uint)profile.Brightness, "brillo", failures);
                    SetVcp(physical.hPhysicalMonitor, 0x12, (uint)profile.Contrast, "contraste", failures);
                }
                SetVcp(physical.hPhysicalMonitor, 0x14, ColorPreset(profile.ColorTemperature), "temperatura de color", failures);
                SetVcp(physical.hPhysicalMonitor, 0x16, 100, "ganancia roja", failures);
                SetVcp(physical.hPhysicalMonitor, 0x18, 100, "ganancia verde", failures);
                SetVcp(physical.hPhysicalMonitor, 0x1A, 100, "ganancia azul", failures);
                SetVcp(physical.hPhysicalMonitor, 0x87, 0, "nitidez artificial", failures);
                SetVcp(physical.hPhysicalMonitor, 0x8A, (uint)profile.Saturation, "saturación", failures);
                SetVcp(physical.hPhysicalMonitor, 0x89, (uint)(profile.Hue + 50), "matiz", failures);
            }
        }
        finally
        {
            NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
        }
    }

    private static void RestoreDdc(IntPtr monitor, OriginalMonitorState state, List<string> failures)
    {
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return;

        var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors)) return;

        try
        {
            foreach (var physicalState in state.PhysicalMonitors)
            {
                if (physicalState.Index < 0 || physicalState.Index >= physicalMonitors.Length) continue;
                var physical = physicalMonitors[physicalState.Index];
                // Restore a named color preset last; writing RGB gains can make some monitors
                // switch back to their user-defined preset.
                foreach (var value in physicalState.Values.OrderBy(item => item.Code == 0x14 ? 1 : 0))
                {
                    if (!NativeMethods.SetVCPFeature(physical.hPhysicalMonitor, value.Code, value.Value))
                    {
                        failures.Add($"La pantalla rechazó restaurar el control 0x{value.Code:X2}.");
                    }
                }
            }
        }
        finally
        {
            NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
        }
    }

    private static IReadOnlyList<ConnectedMonitor> EnumerateMonitors()
    {
        var monitors = new List<ConnectedMonitor>();
        NativeMethods.MonitorEnumProc callback = (handle, _, _, _) =>
        {
            var info = NativeMethods.MONITORINFOEX.Create();
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                monitors.Add(new ConnectedMonitor(handle, GetMonitorId(info.szDevice), info.szDevice));
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

    private static uint ColorPreset(string colorTemperature) => colorTemperature switch
    {
        "Cálido 5000 K" => 4,
        "Neutro 6500 K" => 5,
        "Frío 7500 K" => 6,
        _ => 11
    };

    private static void SetVcp(IntPtr monitor, byte code, uint value, string label, List<string> failures)
    {
        if (!NativeMethods.SetVCPFeature(monitor, code, value))
        {
            failures.Add($"La pantalla rechazó el ajuste de {label}.");
        }
    }

    private static bool ApplyGamma(string display, double gamma, double red, double green, double blue)
    {
        var hdc = NativeMethods.CreateDC("DISPLAY", display, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        var ramp = new ushort[768];
        var gains = new[] { red, green, blue };
        for (var channel = 0; channel < 3; channel++)
        {
            for (var index = 0; index < 256; index++)
            {
                var normalized = index / 255.0;
                var adjusted = Math.Pow(normalized, 1.0 / gamma) * gains[channel];
                ramp[(channel * 256) + index] = (ushort)Math.Round(Math.Clamp(adjusted, 0.0, 1.0) * ushort.MaxValue);
            }
        }

        var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
        try
        {
            return NativeMethods.SetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
            NativeMethods.DeleteDC(hdc);
        }
    }

    private static ushort[]? ReadGammaRamp(string display)
    {
        var hdc = NativeMethods.CreateDC("DISPLAY", display, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return null;

        var ramp = new ushort[768];
        var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
        try
        {
            return NativeMethods.GetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject()) ? ramp : null;
        }
        finally
        {
            handle.Free();
            NativeMethods.DeleteDC(hdc);
        }
    }

    private static bool ApplyGammaRamp(string display, ushort[] ramp)
    {
        var hdc = NativeMethods.CreateDC("DISPLAY", display, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
        try
        {
            return NativeMethods.SetDeviceGammaRamp(hdc, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
            NativeMethods.DeleteDC(hdc);
        }
    }

    private static void SetPowerPlan(string plan, List<string> failures)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/setactive {plan}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(3000);
            if (process is null || process.ExitCode != 0)
            {
                failures.Add("No fue posible cambiar el plan de energía.");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"Plan de energía: {exception.Message}");
        }
    }

    private static class NativeMethods
    {
        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;

            public static MONITORINFOEX Create() => new()
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;

            public static DISPLAY_DEVICE Create() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(string lpDevice, uint deviceNumber, ref DISPLAY_DEVICE displayDevice, uint flags);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint number);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr monitor, byte code, out int codeType, out uint currentValue, out uint maximumValue);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

        [DllImport("powrprof.dll")]
        internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}

internal sealed record ConnectedMonitor(IntPtr Handle, string MonitorId, string DeviceName);

public sealed record AppliedMonitor(string MonitorId, string DeviceName);

public sealed record ApplyResult(int DisplayCount, IReadOnlyList<string> Failures, IReadOnlyList<AppliedMonitor> AppliedMonitors)
{
    public bool Success => DisplayCount > 0 && Failures.Count == 0;
}
