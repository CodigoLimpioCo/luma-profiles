using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

// Software gamma ramp: building, reading and applying it.
public sealed partial class MonitorService
{
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
}
