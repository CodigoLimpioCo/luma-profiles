using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public interface IMonitorService
{
    ApplyResult Apply(DisplayProfile profile, string target);
    ApplyResult Preview(DisplayProfile profile, string target);
    IReadOnlyList<OriginalMonitorState> CaptureOriginalStates(IEnumerable<string> knownMonitorIds);
    string? GetActivePowerPlan();
    ApplyResult RestoreOriginal(IEnumerable<OriginalMonitorState> originalStates, string target, string? originalPowerPlan);
    ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections, CancellationToken cancellationToken = default);

    /// <summary>Reads the live DDC/CI values and gamma ramp of every display (used to undo an unconfirmed change).</summary>
    IReadOnlyList<OriginalMonitorState> CaptureCurrentStates();
    ApplyResult RestoreNeutral(string target);

    /// <summary>The currently connected displays, ordered by their Windows display number.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>The first-ever state of each display; used as the neutral value for settings a profile leaves alone.</summary>
    void UseOriginalStates(IEnumerable<OriginalMonitorState> states);

    /// <summary>When false only the software gamma is changed and the monitor itself is never written to.</summary>
    bool UseMonitorControls { get; set; }
}

public sealed class MonitorService : IMonitorService
{
    /// <summary>The only VCP codes this app ever writes; everything else is left exactly as the monitor has it.</summary>
    private static readonly byte[] ManagedVcpCodes = [0x10, 0x12, 0x14, 0x8A, 0x89];
    private const byte VcpBrightness = 0x10;
    private const byte VcpContrast = 0x12;
    private const byte VcpColorPreset = 0x14;
    private const byte VcpSaturation = 0x8A;
    private const byte VcpHue = 0x89;
    private const int NeutralSaturation = 50;
    private const int NeutralHue = 0;

    private IReadOnlyDictionary<string, OriginalMonitorState> _originals =
        new Dictionary<string, OriginalMonitorState>(StringComparer.OrdinalIgnoreCase);

    public bool UseMonitorControls { get; set; } = true;

    public void UseOriginalStates(IEnumerable<OriginalMonitorState> states) =>
        _originals = states
            .Where(state => !string.IsNullOrWhiteSpace(state.MonitorId))
            .GroupBy(state => state.MonitorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string AllDisplaysTarget = "Ambas pantallas";
    public const string DisplayTargetPrefix = "Pantalla ";
    private const uint MonitorInfoPrimary = 1;
    private const int RestoreAttempts = 3;
    private const int RestoreSettleMilliseconds = 90;

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

            captured.Add(state);
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
            // A display whose ramp could not be read when the restore point was taken goes back to Windows' neutral ramp.
            var gammaRestored = state.GammaRamp.Length == 768
                ? ApplyGammaRamp(monitor.DeviceName, state.GammaRamp)
                : ApplyGamma(monitor.DeviceName, 1.0, 1.0, 1.0, 1.0);
            if (!gammaRestored)
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

    public IReadOnlyList<OriginalMonitorState> CaptureCurrentStates() => CaptureOriginalStates([]);

    public IReadOnlyList<DisplayInfo> GetDisplays() => EnumerateMonitors()
        .Select(monitor => new DisplayInfo(
            DisplayNumber(monitor.DeviceName), monitor.DeviceName, monitor.MonitorId,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height, monitor.IsPrimary))
        .OrderBy(display => display.Number)
        .ToList();

    public ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections, CancellationToken cancellationToken = default)
    {
        var saved = corrections.ToList();
        var failures = new List<string>();
        var applied = new List<AppliedMonitor>();

        foreach (var monitor in EnumerateMonitors())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var correction = saved.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.MonitorId) &&
                    item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase))
                ?? saved.FirstOrDefault(item =>
                    item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (correction is null) continue;

            var profile = correction.ToProfile();
            ApplyDdc(monitor.Handle, monitor.MonitorId, profile, failures, correction.ApplyImageControls);
            if (!ApplyGamma(monitor.DeviceName, profile.Gamma, profile.Red, profile.Green, profile.Blue))
            {
                failures.Add($"No fue posible aplicar gamma en {monitor.DeviceName}.");
            }

            applied.Add(new AppliedMonitor(monitor.MonitorId, monitor.DeviceName));
        }

        return new ApplyResult(applied.Count, failures, applied);
    }

    private ApplyResult ApplyCore(DisplayProfile profile, string target, bool updatePowerPlan)
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
            ApplyDdc(monitor, GetMonitorId(info.szDevice), profile, failures, applyImageControls: true);
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
            ApplyDdc(monitor.Handle, monitor.MonitorId, neutral, failures, applyImageControls: false);
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

    private void ApplyDdc(IntPtr monitor, string monitorId, DisplayProfile profile, List<string> failures, bool applyImageControls)
    {
        if (!UseMonitorControls) return;

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
            for (var index = 0; index < physicalMonitors.Length; index++)
            {
                var handle = physicalMonitors[index].hPhysicalMonitor;
                var plan = PlanDdcWrites(profile, applyImageControls, ReadFeatures(handle), OriginalValues(monitorId, index));
                foreach (var write in plan)
                {
                    SetVcp(handle, write.Code, write.Value, write.Label, failures);
                }
            }
        }
        finally
        {
            NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
        }
    }

    private IReadOnlyDictionary<byte, uint>? OriginalValues(string monitorId, int physicalIndex) =>
        _originals.TryGetValue(monitorId, out var state)
            ? state.PhysicalMonitors.FirstOrDefault(item => item.Index == physicalIndex)?.Values
                .GroupBy(value => value.Code).ToDictionary(group => group.Key, group => group.First().Value)
            : null;

    /// <summary>Reads the current value and range of every code we might write; unsupported codes are absent.</summary>
    private static Dictionary<byte, VcpFeature> ReadFeatures(IntPtr physical)
    {
        var features = new Dictionary<byte, VcpFeature>();
        foreach (var code in ManagedVcpCodes)
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(physical, code, out _, out var current, out var maximum))
            {
                features[code] = new VcpFeature(current, maximum);
            }
        }

        return features;
    }

    /// <summary>
    /// Decides what to send to the monitor. It never guesses: values are scaled to the range the monitor reports,
    /// codes the monitor does not expose are skipped, and anything the profile leaves neutral is put back to what
    /// the monitor had before this app touched it. RGB gains, sharpness and forced color presets are never written,
    /// because factory white balance differs per monitor and forcing "100" tints the whole image.
    /// </summary>
    internal static IReadOnlyList<VcpWrite> PlanDdcWrites(
        DisplayProfile profile,
        bool applyImageControls,
        IReadOnlyDictionary<byte, VcpFeature> supported,
        IReadOnlyDictionary<byte, uint>? originals)
    {
        var plan = new List<VcpWrite>();

        void Add(byte code, uint value, string label)
        {
            if (supported.TryGetValue(code, out var feature) && feature.Current == value) return;
            plan.Add(new VcpWrite(code, value, label));
        }

        // Neutral value = what the monitor had originally; only written when it differs from the current one.
        void Restore(byte code, string label)
        {
            if (originals is not null && originals.TryGetValue(code, out var original)) Add(code, original, label);
        }

        if (applyImageControls)
        {
            // Brightness and contrast are near-universal, so they are attempted even when they cannot be read.
            Add(VcpBrightness, ScaleToRange(profile.Brightness, supported.GetValueOrDefault(VcpBrightness)), "brillo");
            Add(VcpContrast, ScaleToRange(profile.Contrast, supported.GetValueOrDefault(VcpContrast)), "contraste");
        }

        if (supported.ContainsKey(VcpColorPreset))
        {
            if (ExplicitColorPreset(profile.ColorTemperature) is { } preset) Add(VcpColorPreset, preset, "temperatura de color");
            else Restore(VcpColorPreset, "temperatura de color");
        }

        if (supported.TryGetValue(VcpSaturation, out var saturation))
        {
            if (profile.Saturation != NeutralSaturation) Add(VcpSaturation, ScaleToRange(profile.Saturation, saturation), "saturación");
            else Restore(VcpSaturation, "saturación");
        }

        if (supported.TryGetValue(VcpHue, out var hue))
        {
            if (profile.Hue != NeutralHue) Add(VcpHue, ScaleToRange(profile.Hue + 50, hue), "matiz");
            else Restore(VcpHue, "matiz");
        }

        return plan;
    }

    /// <summary>Maps a 0–100 value onto the monitor's own range (0–max); without a reported range it is used as is.</summary>
    internal static uint ScaleToRange(int percent, VcpFeature? feature)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return feature is { Maximum: > 0 } range
            ? (uint)Math.Round(clamped / 100.0 * range.Maximum)
            : (uint)clamped;
    }

    /// <summary>Only warm and cool are sent to the monitor; neutral/user leave its own preset alone.</summary>
    internal static uint? ExplicitColorPreset(string colorTemperature) => colorTemperature switch
    {
        "Cálido 5000 K" => 4,
        "Frío 7500 K" => 6,
        _ => null
    };

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
                RestorePhysicalMonitor(physicalMonitors[physicalState.Index].hPhysicalMonitor, physicalState, failures);
            }
        }
        finally
        {
            NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
        }
    }

    /// <summary>
    /// Writes the saved values, then reads them back. DDC/CI writes can be silently dropped while a monitor is
    /// busy, so anything that did not stick is written again before reporting a failure.
    /// </summary>
    private static void RestorePhysicalMonitor(IntPtr physical, OriginalPhysicalMonitorState state, List<string> failures)
    {
        // Restore a named color preset last; writing RGB gains can make some monitors
        // switch back to their user-defined preset.
        // Everything that was captured is restored, including RGB gains saved by older versions that used to force them.
        var pending = state.Values.OrderBy(item => item.Code == VcpColorPreset ? 1 : 0).ToList();
        for (var attempt = 0; attempt < RestoreAttempts && pending.Count > 0; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RestoreSettleMilliseconds);
            foreach (var value in pending)
            {
                if (!NativeMethods.SetVCPFeature(physical, value.Code, value.Value) && attempt == RestoreAttempts - 1)
                {
                    failures.Add($"La pantalla rechazó restaurar el control 0x{value.Code:X2}.");
                }
            }

            Thread.Sleep(RestoreSettleMilliseconds);
            pending = pending.Where(value => !HasValue(physical, value)).ToList();
        }

        foreach (var value in pending)
        {
            AppLog.Warn($"Monitor did not keep VCP 0x{value.Code:X2}={value.Value} after restore.");
            failures.Add($"El monitor no confirmó el control 0x{value.Code:X2} tras restaurar.");
        }
    }

    private static bool HasValue(IntPtr physical, OriginalVcpValue expected) =>
        NativeMethods.GetVCPFeatureAndVCPFeatureReply(physical, expected.Code, out _, out var current, out _) &&
        current == expected.Value;

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


    private static void SetVcp(IntPtr monitor, byte code, uint value, string label, List<string> failures)
    {
        if (!NativeMethods.SetVCPFeature(monitor, code, value))
        {
            AppLog.Warn($"DDC/CI rejected {label} (VCP 0x{code:X2}, value {value}).");
            failures.Add($"La pantalla rechazó el ajuste de {label}.");
        }
    }

    private static bool ApplyGamma(string display, double gamma, double red, double green, double blue)
    {
        var hdc = NativeMethods.CreateDC("DISPLAY", display, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        var ramp = BuildGammaRamp(gamma, red, green, blue);

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

    /// <summary>Builds the 3x256 gamma ramp (red, green, blue) applied through SetDeviceGammaRamp.</summary>
    internal static ushort[] BuildGammaRamp(double gamma, double red, double green, double blue)
    {
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

        return ramp;
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
            AppLog.Warn("powercfg failed.", exception);
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

/// <summary>Current value and maximum reported by a monitor for one VCP code.</summary>
public sealed record VcpFeature(uint Current, uint Maximum);

/// <summary>One DDC/CI write decided by <see cref="MonitorService.PlanDdcWrites"/>.</summary>
public sealed record VcpWrite(byte Code, uint Value, string Label);

internal sealed record ConnectedMonitor(
    IntPtr Handle, string MonitorId, string DeviceName, int Left, int Top, int Width, int Height, bool IsPrimary);

/// <summary>A connected display as shown in the UI (bounds are in physical pixels).</summary>
public sealed record DisplayInfo(int Number, string DeviceName, string MonitorId, int Left, int Top, int Width, int Height, bool IsPrimary);

public sealed record AppliedMonitor(string MonitorId, string DeviceName);

public sealed record ApplyResult(int DisplayCount, IReadOnlyList<string> Failures, IReadOnlyList<AppliedMonitor> AppliedMonitors)
{
    public bool Success => DisplayCount > 0 && Failures.Count == 0;
}
