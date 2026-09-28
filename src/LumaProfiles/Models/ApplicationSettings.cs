namespace LumaProfiles.Models;

public sealed partial class ApplicationSettings
{
    public string LanguageCode { get; set; } = "es";
    public bool IsDarkTheme { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string SelectedMonitorTarget { get; set; } = "Ambas pantallas";
    public List<MonitorColorCorrection> MonitorCorrections { get; set; } = [];
    public List<OriginalMonitorState> OriginalMonitorStates { get; set; } = [];
    public string? OriginalPowerPlan { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool GlobalHotkeysEnabled { get; set; } = true;
    public ScheduleSettings Schedule { get; set; } = new();
    public List<AppProfileRule> AppRules { get; set; } = [];
}

/// <summary>Switches between a day and a night profile at fixed local times.</summary>
public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }
    public string DayProfileId { get; set; } = "natural";
    public string NightProfileId { get; set; } = "eyes-night";
    public string DayStart { get; set; } = "07:00";
    public string NightStart { get; set; } = "20:00";
}

/// <summary>Applies a profile while the named process owns the foreground window.</summary>
public sealed class AppProfileRule
{
    public string ProcessName { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
}

public sealed class OriginalMonitorState
{
    public string MonitorId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public List<OriginalPhysicalMonitorState> PhysicalMonitors { get; set; } = [];
    public ushort[] GammaRamp { get; set; } = [];
}

public sealed class OriginalPhysicalMonitorState
{
    public int Index { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<OriginalVcpValue> Values { get; set; } = [];
}

public sealed class OriginalVcpValue
{
    public byte Code { get; set; }
    public uint Value { get; set; }
}

public sealed class MonitorColorCorrection
{
    public string MonitorId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public bool ApplyImageControls { get; set; } = true;
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Saturation { get; set; }
    public int Hue { get; set; }
    public double Gamma { get; set; } = 1.0;
    public double Red { get; set; } = 1.0;
    public double Green { get; set; } = 1.0;
    public double Blue { get; set; } = 1.0;
    public string ColorTemperature { get; set; } = "Neutro 6500 K";

    public DisplayProfile ToProfile() => new()
    {
        Id = string.IsNullOrWhiteSpace(ProfileId) ? "saved-correction" : ProfileId,
        Name = string.IsNullOrWhiteSpace(ProfileName) ? "Corrección guardada" : ProfileName,
        Category = "Sistema",
        Description = "Corrección persistente por monitor",
        PreviewStart = "#000000",
        PreviewEnd = "#000000",
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Hue = Hue,
        Gamma = Gamma,
        Red = Red,
        Green = Green,
        Blue = Blue,
        ColorTemperature = ColorTemperature
    };
}
