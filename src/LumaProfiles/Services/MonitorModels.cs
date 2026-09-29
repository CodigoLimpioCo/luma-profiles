namespace LumaProfiles.Services;

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
