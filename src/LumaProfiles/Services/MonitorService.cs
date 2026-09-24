using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public sealed class MonitorService
{
    private const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public ApplyResult Apply(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: true);

    public ApplyResult Preview(DisplayProfile profile, string target)
        => ApplyCore(profile, target, updatePowerPlan: false);

    private static ApplyResult ApplyCore(DisplayProfile profile, string target, bool updatePowerPlan)
    {
        var failures = new List<string>();
        var appliedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = NativeMethods.MONITORINFOEX.Create();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !MatchesTarget(info.szDevice, target))
            {
                return true;
            }

            appliedDisplays.Add(info.szDevice);
            ApplyDdc(monitor, profile, failures);
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

        return new ApplyResult(appliedDisplays.Count, failures);
    }

    public ApplyResult RestoreNeutral(string target)
    {
        var neutral = new DisplayProfile
        {
            Id = "neutral-repair",
            Name = "Neutro",
            Category = "Sistema",
            Description = "Gamma neutra",
            PreviewStart = "#000000",
            PreviewEnd = "#000000",
            Brightness = 80,
            Contrast = 80,
            Saturation = 50,
            Gamma = 1.0,
            Red = 1.0,
            Green = 1.0,
            Blue = 1.0
        };
        return Apply(neutral, target);
    }

    private static bool MatchesTarget(string deviceName, string target) =>
        target == "Ambas pantallas" ||
        (target == "Pantalla 1" && deviceName.EndsWith("DISPLAY1", StringComparison.OrdinalIgnoreCase)) ||
        (target == "Pantalla 2" && deviceName.EndsWith("DISPLAY2", StringComparison.OrdinalIgnoreCase));

    private static void ApplyDdc(IntPtr monitor, DisplayProfile profile, List<string> failures)
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
                SetVcp(physical.hPhysicalMonitor, 0x10, (uint)profile.Brightness, "brillo", failures);
                SetVcp(physical.hPhysicalMonitor, 0x12, (uint)profile.Contrast, "contraste", failures);
                SetVcp(physical.hPhysicalMonitor, 0x14, ColorPreset(profile.ColorTemperature), "temperatura de color", failures);
                SetVcp(physical.hPhysicalMonitor, 0x16, 100, "ganancia roja", failures);
                SetVcp(physical.hPhysicalMonitor, 0x18, 100, "ganancia verde", failures);
                SetVcp(physical.hPhysicalMonitor, 0x1A, 100, "ganancia azul", failures);
                SetVcp(physical.hPhysicalMonitor, 0x87, 0, "nitidez artificial", failures);
                SetVcp(physical.hPhysicalMonitor, 0x8A, (uint)profile.Saturation, "saturación", failures);
                if (profile.Hue != 0)
                {
                    SetVcp(physical.hPhysicalMonitor, 0x89, (uint)(profile.Hue + 50), "matiz", failures);
                }
            }
        }
        finally
        {
            NativeMethods.DestroyPhysicalMonitors(count, physicalMonitors);
        }
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

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);
    }
}

public sealed record ApplyResult(int DisplayCount, IReadOnlyList<string> Failures)
{
    public bool Success => DisplayCount > 0 && Failures.Count == 0;
}
