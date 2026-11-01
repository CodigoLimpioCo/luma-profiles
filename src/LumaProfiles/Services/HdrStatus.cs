using System.Runtime.InteropServices;

namespace LumaProfiles.Services;

/// <summary>Reads which displays currently run in HDR (Windows "Use HDR" / advanced color is on).</summary>
public static class HdrStatus
{
    private const uint QdcOnlyActivePaths = 2;
    private const uint GetSourceName = 1;
    private const uint GetAdvancedColorInfo = 9;
    private const uint AdvancedColorEnabledBit = 0x2;

    /// <summary>GDI device names (\\.\DISPLAY1...) with HDR on; empty when it cannot be read.</summary>
    public static IReadOnlySet<string> ActiveDisplays()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return result;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

            for (var index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                var color = new AdvancedColorInfo
                {
                    Header = Header(GetAdvancedColorInfo, Marshal.SizeOf<AdvancedColorInfo>(), path.TargetAdapterId, path.TargetId),
                };
                if (DisplayConfigGetDeviceInfo(ref color) != 0 || (color.Value & AdvancedColorEnabledBit) == 0) continue;

                var source = new SourceDeviceName
                {
                    Header = Header(GetSourceName, Marshal.SizeOf<SourceDeviceName>(), path.SourceAdapterId, path.SourceId),
                    ViewGdiDeviceName = string.Empty,
                };
                if (DisplayConfigGetDeviceInfo(ref source) == 0 && !string.IsNullOrWhiteSpace(source.ViewGdiDeviceName))
                {
                    result.Add(source.ViewGdiDeviceName);
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            AppLog.Warn("HDR state could not be read.", exception);
        }

        return result;
    }

    private static DeviceInfoHeader Header(uint type, int size, Luid adapter, uint id) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapter, Id = id };

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId;
        public uint SourceModeInfoIdx;
        public uint SourceStatusFlags;
        public Luid TargetAdapterId;
        public uint TargetId;
        public uint TargetModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);
}
