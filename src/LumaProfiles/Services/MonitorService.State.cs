using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

// Original state: capture, restore, power plans and the first-ever values used as neutral.
public sealed partial class MonitorService
{
    private IReadOnlyDictionary<string, OriginalMonitorState> _originals =
        new Dictionary<string, OriginalMonitorState>(StringComparer.OrdinalIgnoreCase);

    public void UseOriginalStates(IEnumerable<OriginalMonitorState> states) =>
        _originals = states
            .Where(state => !string.IsNullOrWhiteSpace(state.MonitorId))
            .GroupBy(state => state.MonitorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

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
}
