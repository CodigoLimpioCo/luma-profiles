using System.IO;
using System.Reflection;
using System.Text.Json;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public sealed class ProfileStore
{
    public const int CurrentSchemaVersion = 1;
    private const string DefaultsResourceName = "LumaProfiles.Data.default-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;

    public ProfileStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory ?? AppDataPaths.DefaultDirectory;
        Defaults = LoadDefaults();
    }

    private string ProfilesPath => Path.Combine(_dataDirectory, "profiles.json");

    public IReadOnlyList<DisplayProfile> Defaults { get; }

    /// <summary>Set when the saved file was unreadable and had to be set aside during the last <see cref="Load"/>.</summary>
    public string? LoadWarning { get; private set; }

    public List<DisplayProfile> Load()
    {
        LoadWarning = null;
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var saved = ParseSaved(File.ReadAllText(ProfilesPath));
                if (saved.Count > 0) return Merge(saved);
            }
        }
        catch (Exception exception)
        {
            var backup = AtomicFile.Quarantine(ProfilesPath);
            LoadWarning = backup ?? "profiles.json";
            AppLog.Error($"profiles.json is damaged; moved to '{backup}'. Defaults restored.", exception);
        }

        return Defaults.Select(profile => profile.Clone()).ToList();
    }

    public void Save(IEnumerable<DisplayProfile> profiles) =>
        AtomicFile.WriteAllText(ProfilesPath, Serialize(profiles));

    public void Export(string path, IEnumerable<DisplayProfile> profiles) =>
        AtomicFile.WriteAllText(path, Serialize(profiles));

    /// <summary>Reads a profile file and copies matching adjustments/favorites into <paramref name="target"/>.</summary>
    /// <returns>Number of profiles updated.</returns>
    public int Import(string path, IEnumerable<DisplayProfile> target)
    {
        var imported = ParseSaved(File.ReadAllText(path));
        var updated = 0;
        foreach (var profile in target)
        {
            var source = imported.FirstOrDefault(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            if (source is null) continue;
            profile.CopyAdjustmentsFrom(source);
            profile.IsFavorite = source.IsFavorite;
            updated++;
        }

        return updated;
    }

    public DisplayProfile GetDefault(string id) =>
        Defaults.First(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Clone();

    private List<DisplayProfile> Merge(List<DisplayProfile> saved) =>
        Defaults.Select(defaultProfile =>
        {
            var merged = defaultProfile.Clone();
            var match = saved.FirstOrDefault(item => item.Id == defaultProfile.Id);
            if (match is not null)
            {
                merged.CopyAdjustmentsFrom(match);
                merged.IsFavorite = match.IsFavorite;
            }

            return merged;
        }).ToList();

    private List<DisplayProfile> ParseSaved(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Files written before schema versioning are a bare array of profiles.
        if (root.ValueKind == JsonValueKind.Array)
        {
            var legacy = root.Deserialize<List<DisplayProfile>>(JsonOptions) ?? [];
            var hasTemperature = root.EnumerateArray().Any(item => item.TryGetProperty("ColorTemperature", out _));
            if (!hasTemperature)
            {
                foreach (var profile in legacy)
                {
                    profile.ColorTemperature = Defaults.FirstOrDefault(item => item.Id == profile.Id)?.ColorTemperature
                        ?? "Usuario (RGB)";
                }
            }

            return legacy;
        }

        var file = root.Deserialize<ProfilesFile>(JsonOptions);
        if (file is null) return [];
        if (file.SchemaVersion > CurrentSchemaVersion)
        {
            AppLog.Warn($"profiles.json uses schema {file.SchemaVersion}; this build understands {CurrentSchemaVersion}.");
        }

        return file.Profiles;
    }

    private static string Serialize(IEnumerable<DisplayProfile> profiles) =>
        JsonSerializer.Serialize(new ProfilesFile { SchemaVersion = CurrentSchemaVersion, Profiles = profiles.ToList() }, JsonOptions);

    private static List<DisplayProfile> LoadDefaults()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultsResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultsResourceName}' is missing.");
        var file = JsonSerializer.Deserialize<ProfilesFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Default profile catalog is empty.");
        return file.Profiles;
    }

    private sealed class ProfilesFile
    {
        public int SchemaVersion { get; set; }
        public List<DisplayProfile> Profiles { get; set; } = [];
    }
}
