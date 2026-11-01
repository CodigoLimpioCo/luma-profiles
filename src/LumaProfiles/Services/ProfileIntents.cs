using LumaProfiles.Models;

namespace LumaProfiles.Services;

/// <summary>
/// "What are you going to do?": groups the library by activity (play, watch, work, read, night, create) so a
/// long list of modes can be narrowed down in one click, without knowing the category names.
/// </summary>
public static class ProfileIntents
{
    public const string All = "All";

    public static IReadOnlyList<string> Keys { get; } = [All, "Play", "Watch", "Work", "Read", "Night", "Create"];

    private static readonly Dictionary<string, string[]> CategoriesByIntent = new()
    {
        ["Play"] = ["Gamer", "ASUS GameVisual"],
        ["Watch"] = ["Entretenimiento"],
        ["Create"] = ["Color creativo"],
    };

    private static readonly Dictionary<string, string[]> IdsByIntent = new()
    {
        ["Play"] = ["performance", "performance-esports", "performance-latency", "hdr-gaming", "hdr-console", "va-gaming-120", "va-fps", "va-shadows"],
        ["Watch"] = ["cinema", "gv-cinema", "hdr-cinema", "hdr-darkroom", "hdr-bright", "va-cinema", "va-vivid"],
        ["Work"] = ["natural", "reference", "faithful-studio", "faithful-web", "faithful-video-call", "eyes-office", "eyes-coding",
                    "va-balanced", "va-office", "va-cool-fix", "performance-battery"],
        ["Read"] = ["eyes-soft", "eyes-rest", "eyes-reading", "eyes-yellow-reading", "eyes-sensitive", "eyes-monochrome", "va-reading"],
        ["Night"] = ["eyes-night", "eyes-red-night", "eyes-sunset", "eyes-dark-room", "eyes-coding", "va-night",
                     "entertainment-streaming", "hdr-darkroom", "game-horror"],
        ["Create"] = ["faithful-photo", "faithful-print", "faithful-web", "faithful-studio", "reference", "creative-blue-filter", "va-vivid"],
    };

    public static bool Matches(string intent, DisplayProfile profile)
    {
        if (intent == All) return true;
        // A custom profile has no known purpose; it is always one click away under "My profiles".
        if (profile.IsCustom) return false;
        return (CategoriesByIntent.TryGetValue(intent, out var categories) && categories.Contains(profile.Category, StringComparer.Ordinal))
            || (IdsByIntent.TryGetValue(intent, out var ids) && ids.Contains(profile.Id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Profiles listed first for an activity (the safest starting point).</summary>
    public static string? Suggested(string intent) => intent switch
    {
        "Play" => "gamer-competitive",
        "Watch" => "cinema",
        "Work" => "natural",
        "Read" => "eyes-reading",
        "Night" => "eyes-night",
        "Create" => "faithful-photo",
        _ => null,
    };
}
