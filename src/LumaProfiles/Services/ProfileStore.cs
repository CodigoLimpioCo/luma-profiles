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
                var json = File.ReadAllText(ProfilesPath);
                var saved = JsonSerializer.Deserialize<List<DisplayProfile>>(json, JsonOptions);
                if (saved is { Count: > 0 })
                {
                    if (!json.Contains("\"ColorTemperature\"", StringComparison.Ordinal))
                    {
                        foreach (var profile in saved)
                        {
                            profile.ColorTemperature = Defaults.FirstOrDefault(item => item.Id == profile.Id)?.ColorTemperature
                                ?? "Usuario (RGB)";
                        }
                    }

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
        Profile("natural", "Natural", "Color fiel", "Color neutro y equilibrado para uso diario, fotografía y diseño.", "#26C6DA", "#2870B5", 80, 80, 50, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("reference", "Referencia", "Color fiel", "Brillo moderado y señal neutra para revisar grises, piel y detalle.", "#AAB7C4", "#485765", 50, 80, 50, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("entertainment", "Entretenimiento", "Entretenimiento", "Un poco más vivo para juegos y vídeo, sin convertir los colores en neón.", "#9C5CFF", "#E34D8D", 85, 80, 56, 0, 1.03, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("performance", "Rendimiento", "Rendimiento", "Conserva 180 Hz y activa Alto rendimiento para priorizar fotogramas.", "#2D8CFF", "#5EE7F7", 85, 80, 52, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance"),
        Profile("cinema", "Cine cálido", "Entretenimiento", "Imagen suavemente cálida para películas y contenido nocturno.", "#F3A45B", "#8C3C5D", 70, 80, 52, 0, 1.02, 1.00, 0.99, 0.96, "Usuario (RGB)"),
        Profile("eyes-soft", "Ojos suave", "Cuidado visual", "Brillo moderado y tono apenas cálido para jornadas largas.", "#9EDC8D", "#4FA981", 45, 75, 48, 0, 1.00, 1.00, 0.99, 0.94, "Usuario (RGB)"),
        Profile("eyes-rest", "Ojos descanso", "Cuidado visual", "Brillo bajo y filtro cálido medio para trabajar con poca luz.", "#EFC66A", "#8F7A47", 30, 72, 46, 0, 1.00, 1.00, 0.97, 0.86, "Usuario (RGB)"),
        Profile("eyes-night", "Ojos noche", "Cuidado visual", "Brillo mínimo práctico y filtro ámbar intenso para uso nocturno.", "#FF9B4A", "#693A35", 18, 70, 44, 0, 1.00, 1.00, 0.93, 0.74, "Usuario (RGB)"),

        Profile("gv-rts", "RTS/RPG", "ASUS GameVisual", "Contraste y color reforzados para estrategia en tiempo real y juegos de rol.", "#7856D8", "#CB5D95", 82, 82, 58, 0, 1.02, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("gv-fps", "FPS", "ASUS GameVisual", "Aclara sombras y eleva el contraste para localizar detalles en escenas oscuras.", "#365BE2", "#55D6F5", 88, 88, 50, 0, 1.08, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance"),
        Profile("gv-cinema", "Cine ASUS", "ASUS GameVisual", "Contraste cinematográfico y color vivo para películas y series.", "#CE4B68", "#F29B55", 80, 85, 58, 0, 1.02, 1.00, 0.99, 0.95, "Usuario (RGB)"),
        Profile("gv-scenery", "Escenario", "ASUS GameVisual", "Amplía verdes y azules para paisajes, naturaleza y fotografía de viajes.", "#2BBE8C", "#278BD3", 85, 82, 60, 0, 1.02, 0.98, 1.02, 1.02, "Usuario (RGB)"),
        Profile("gv-racing", "Carrera", "ASUS GameVisual", "Respuesta visual equilibrada y limpia para juegos rápidos y conducción.", "#248CE8", "#55E0DD", 80, 80, 50, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance"),
        Profile("gv-srgb", "sRGB", "ASUS GameVisual", "Color contenido y neutro para fotografía, gráficos web y contenido sRGB.", "#9EAAB6", "#506171", 55, 80, 50, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("gv-moba", "MOBA", "ASUS GameVisual", "Colores de interfaz y contraste más claros para arenas multijugador.", "#B444D8", "#EC5B70", 82, 88, 58, 0, 1.04, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance"),

        Profile("hdr-gaming", "Gaming HDR", "HDR", "Base brillante para juegos HDR. Activa HDR de Windows antes de usarla.", "#00A9FF", "#8B5CFF", 95, 88, 55, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance", true),
        Profile("hdr-cinema", "Cine HDR", "HDR", "Base de contraste suave para películas HDR. Requiere HDR activo en Windows.", "#FF7048", "#8841A8", 82, 86, 53, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K", "Balanced", true),
        Profile("hdr-console", "Consola HDR", "HDR", "Punto de partida equilibrado para consolas y capturadoras con señal HDR.", "#27C47D", "#3276E8", 88, 84, 54, 0, 1.00, 1.00, 1.00, 1.00, "Neutro 6500 K", "HighPerformance", true),

        Profile("creative-vivid", "Vibrante realista", "Color creativo", "Más intensidad sin recortar agresivamente tonos de piel ni luces.", "#FF4D76", "#6B58FF", 80, 82, 62, 0, 1.01, 1.00, 1.00, 1.00, "Neutro 6500 K"),
        Profile("creative-skin", "Piel natural", "Color creativo", "Reduce excesos de saturación y conserva tonos de piel agradables.", "#EAA27D", "#BB6B73", 70, 78, 48, 0, 1.00, 1.00, 1.00, 0.98, "Usuario (RGB)"),
        Profile("creative-golden", "Atardecer dorado", "Color creativo", "Calidez expresiva para fotografía, música y contenido ambiental.", "#FFB24C", "#C95155", 65, 80, 55, 0, 1.02, 1.00, 0.96, 0.84, "Usuario (RGB)"),
        Profile("creative-ocean", "Océano frío", "Color creativo", "Azules limpios y sensación fría para tecnología y paisajes marinos.", "#2ED2D0", "#315BDA", 72, 82, 54, 0, 1.01, 0.94, 1.00, 1.04, "Usuario (RGB)"),
        Profile("creative-mono", "Monocromo editorial", "Color creativo", "Blanco y negro suave para lectura visual, fotografía y concentración.", "#A8B0B8", "#343A42", 68, 82, 0, 0, 1.04, 1.00, 1.00, 1.00, "Neutro 6500 K")
    ];

    private static DisplayProfile Profile(
        string id, string name, string category, string description, string previewStart, string previewEnd,
        int brightness, int contrast, int saturation, int hue, double gamma, double red, double green, double blue,
        string colorTemperature = "Usuario (RGB)", string powerPlan = "Balanced", bool isHdr = false) => new()
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
            Hue = hue,
            Gamma = gamma,
            Red = red,
            Green = green,
            Blue = blue,
            ColorTemperature = colorTemperature,
            PowerPlan = powerPlan,
            IsHdr = isHdr
        };
}
