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
    /// <summary>When the restore point (original display state) was captured.</summary>
    public DateTime? OriginalCapturedAt { get; set; }
    public bool MinimizeToTray { get; set; }
    /// <summary>When Windows starts the app at sign-in, keep the window hidden in the tray.</summary>
    public bool StartHiddenAtSignIn { get; set; } = true;
    /// <summary>Set once the user has answered the "keep running in the tray?" question when closing.</summary>
    public bool CloseChoiceAsked { get; set; }
    public bool GlobalHotkeysEnabled { get; set; } = true;
    public ScheduleSettings Schedule { get; set; } = new();
    public List<AppProfileRule> AppRules { get; set; } = [];
    public List<ProfileHotkey> ProfileHotkeys { get; set; } = [];
}

/// <summary>Applies a profile at each configured local time; the latest entry that has started stays in effect.</summary>
public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }

    /// <summary>Seconds a scheduled change takes to fade in; 0 switches instantly.</summary>
    public int TransitionSeconds { get; set; } = 10;

    /// <summary>Decimal degrees (north / east positive); needed only by sunrise and sunset entries.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Null only in files written before multiple time slots existed; see <see cref="EnsureEntries"/>.</summary>
    public List<ScheduleEntry>? Entries { get; set; }

    // Legacy day/night pair, read only to migrate old settings.json files into <see cref="Entries"/>.
    public string DayProfileId { get; set; } = "natural";
    public string NightProfileId { get; set; } = "eyes-night";
    public string DayStart { get; set; } = "07:00";
    public string NightStart { get; set; } = "20:00";

    public List<ScheduleEntry> EnsureEntries() => Entries ??=
    [
        new ScheduleEntry { Time = DayStart, ProfileId = DayProfileId },
        new ScheduleEntry { Time = NightStart, ProfileId = NightProfileId },
    ];
}

public enum SunEvent { None, Sunrise, Sunset }

public sealed class ScheduleEntry
{
    /// <summary>HH:mm for fixed entries; ignored when <see cref="Sun"/> is set.</summary>
    public string Time { get; set; } = "07:00";
    public string ProfileId { get; set; } = string.Empty;
    public SunEvent Sun { get; set; } = SunEvent.None;
    /// <summary>Minutes after (positive) or before (negative) the sun event.</summary>
    public int OffsetMinutes { get; set; }
}

/// <summary>Applies a profile with a global Ctrl+Alt+<see cref="Key"/> shortcut.</summary>
public sealed class ProfileHotkey
{
    public string ProfileId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

/// <summary>Everything under "Automation" that travels with an exported profile file.</summary>
public sealed class AutomationBackup
{
    public ScheduleSettings? Schedule { get; set; }
    public List<AppProfileRule> AppRules { get; set; } = [];
    public List<ProfileHotkey> ProfileHotkeys { get; set; } = [];
    public bool GlobalHotkeysEnabled { get; set; } = true;
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
