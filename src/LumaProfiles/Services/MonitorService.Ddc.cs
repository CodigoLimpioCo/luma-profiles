using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

// DDC/CI: planning, writing and restoring the VCP codes this app manages.
public sealed partial class MonitorService
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

    private const int RestoreAttempts = 3;

    private const int RestoreSettleMilliseconds = 90;

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

    private static void SetVcp(IntPtr monitor, byte code, uint value, string label, List<string> failures)
    {
        if (!NativeMethods.SetVCPFeature(monitor, code, value))
        {
            AppLog.Warn($"DDC/CI rejected {label} (VCP 0x{code:X2}, value {value}).");
            failures.Add($"La pantalla rechazó el ajuste de {label}.");
        }
    }
}
