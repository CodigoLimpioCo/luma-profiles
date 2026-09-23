using System.Text.Json;
using System.IO;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumaProfiles");

    private string ProfilesPath => Path.Combine(_dataDirectory, "profiles.json");

    public IReadOnlyList<DisplayProfile> Defaults { get; } = CreateDefaults();

    public List<DisplayProfile> Load()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var saved = JsonSerializer.Deserialize<List<DisplayProfile>>(File.ReadAllText(ProfilesPath), JsonOptions);
                if (saved is { Count: > 0 })
                {
                    var merged = Defaults.Select(defaultProfile =>
                        saved.FirstOrDefault(item => item.Id == defaultProfile.Id) ?? defaultProfile.Clone()).ToList();
                    return merged;
                }
            }
        }
        catch
        {
            // A damaged user file must never prevent the app from starting.
        }

        return Defaults.Select(profile => profile.Clone()).ToList();
    }

    public void Save(IEnumerable<DisplayProfile> profiles)
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, JsonOptions));
    }

    public DisplayProfile GetDefault(string id) =>
        Defaults.First(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Clone();

    private static List<DisplayProfile> CreateDefaults() =>
    [
        Profile("natural", "Natural", "Color fiel", "Color neutro y equilibrado para uso diario, fotografía y diseño.", "#26C6DA", "#2870B5", 80, 80, 50, 1.00, 1.00, 1.00, 1.00),
        Profile("reference", "Referencia", "Color fiel", "Brillo moderado y señal neutra para revisar grises, piel y detalle.", "#AAB7C4", "#485765", 50, 80, 50, 1.00, 1.00, 1.00, 1.00),
        Profile("entertainment", "Entretenimiento", "Entretenimiento", "Un poco más vivo para juegos y vídeo, sin convertir los colores en neón.", "#9C5CFF", "#E34D8D", 85, 80, 56, 1.03, 1.00, 1.00, 1.00),
        Profile("performance", "Rendimiento", "Rendimiento", "Conserva 180 Hz y activa Alto rendimiento para priorizar fotogramas.", "#2D8CFF", "#5EE7F7", 85, 80, 52, 1.00, 1.00, 1.00, 1.00, "HighPerformance"),
        Profile("cinema", "Cine cálido", "Entretenimiento", "Imagen suavemente cálida para películas y contenido nocturno.", "#F3A45B", "#8C3C5D", 70, 80, 52, 1.02, 1.00, 0.99, 0.96),
        Profile("eyes-soft", "Ojos suave", "Cuidado visual", "Brillo moderado y tono apenas cálido para jornadas largas.", "#9EDC8D", "#4FA981", 45, 75, 48, 1.00, 1.00, 0.99, 0.94),
        Profile("eyes-rest", "Ojos descanso", "Cuidado visual", "Brillo bajo y filtro cálido medio para trabajar con poca luz.", "#EFC66A", "#8F7A47", 30, 72, 46, 1.00, 1.00, 0.97, 0.86),
        Profile("eyes-night", "Ojos noche", "Cuidado visual", "Brillo mínimo práctico y filtro ámbar intenso para uso nocturno.", "#FF9B4A", "#693A35", 18, 70, 44, 1.00, 1.00, 0.93, 0.74)
    ];

    private static DisplayProfile Profile(
        string id, string name, string category, string description, string previewStart, string previewEnd,
        int brightness, int contrast, int saturation, double gamma, double red, double green, double blue,
        string powerPlan = "Balanced") => new()
        {
            Id = id,
            Name = name,
            Category = category,
            Description = description,
            PreviewStart = previewStart,
            PreviewEnd = previewEnd,
            Brightness = brightness,
            Contrast = contrast,
            Saturation = saturation,
            Gamma = gamma,
            Red = red,
            Green = green,
            Blue = blue,
            PowerPlan = powerPlan
        };
}
