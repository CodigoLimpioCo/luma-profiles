using System.IO;
using System.Text.Json;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

/// <summary>The saved "before Luma Profiles" state of the displays.</summary>
public sealed class RestorePoint
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CapturedAt { get; set; }
    public string? PowerPlan { get; set; }
    public List<OriginalMonitorState> Monitors { get; set; } = [];
}

/// <summary>
/// Keeps a second copy of the restore point in its own file. The copy in settings.json is convenient, but a damaged
/// settings file is set aside and reset, and that must never cost the user the way back to their original screen.
/// </summary>
public sealed class RestorePointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;

    public RestorePointStore(string? dataDirectory = null) =>
        _path = Path.Combine(dataDirectory ?? AppDataPaths.DefaultDirectory, "restore-point.json");

    public RestorePoint? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var point = JsonSerializer.Deserialize<RestorePoint>(File.ReadAllText(_path), JsonOptions);
            return point is { Monitors.Count: > 0 } ? point : null;
        }
        catch (Exception exception)
        {
            AtomicFile.Quarantine(_path);
            AppLog.Warn("restore-point.json is damaged and was set aside.", exception);
            return null;
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception)
        {
            AppLog.Warn("The restore point copy could not be deleted.", exception);
        }
    }

    public void Save(RestorePoint point)
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(point, JsonOptions));
        }
        catch (Exception exception)
        {
            AppLog.Warn("The restore point copy could not be written.", exception);
        }
    }
}
