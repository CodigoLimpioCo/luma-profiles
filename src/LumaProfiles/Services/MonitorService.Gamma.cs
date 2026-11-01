using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

// Software gamma ramp: building, reading and applying it.
public sealed partial class MonitorService
{
    /// <summary>
    /// Applies the curve; when Windows refuses it (it rejects ramps too far from linear unless the
    /// GdiIcmGammaRange registry value is raised) the strongest version it accepts is applied instead,
    /// so a strong custom RGB balance is softened rather than silently dropped.
    /// </summary>
    private static bool ApplyGamma(string display, double gamma, double red, double green, double blue, List<string>? failures = null)
    {
        var target = BuildGammaRamp(gamma, red, green, blue);
        if (ApplyGammaRamp(display, target)) return true;

        var identity = BuildGammaRamp(1.0, 1.0, 1.0, 1.0);
        double accepted = 0, rejected = 1;
        for (var attempt = 0; attempt < 7; attempt++)
        {
            var strength = (accepted + rejected) / 2;
            if (ApplyGammaRamp(display, BlendGammaRamp(identity, target, strength))) accepted = strength;
            else rejected = strength;
        }

        // A rejected call leaves the screen untouched, so the last accepted ramp is the one showing.
        if (accepted <= 0) return false;
        AppLog.Warn($"Windows rejected the full gamma ramp on {display}; applied {accepted:P0} of it.");
        failures?.Add($"Windows limitó el balance de color en {display}: se aplicó al {accepted:P0}.");
        return true;
    }

    /// <summary>Moves a ramp toward <paramref name="from"/>: 0 returns it unchanged, 1 returns <paramref name="to"/>.</summary>
    internal static ushort[] BlendGammaRamp(ushort[] from, ushort[] to, double strength)
    {
        var blended = new ushort[from.Length];
        for (var index = 0; index < from.Length; index++)
        {
            blended[index] = (ushort)Math.Round(from[index] + ((to[index] - from[index]) * strength));
        }

        return blended;
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
}
