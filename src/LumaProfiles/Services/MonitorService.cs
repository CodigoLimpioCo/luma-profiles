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

/// <summary>Applies profiles to the connected displays. Split by concern: DDC/CI, gamma, displays, saved state and native calls.</summary>
public sealed partial class MonitorService : IMonitorService
{
    public bool UseMonitorControls { get; set; } = true;

    public ApplyResult Apply(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: true);

    public ApplyResult Preview(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: false);

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
}
