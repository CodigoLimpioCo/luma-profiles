using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using LumaProfiles.Models;

namespace LumaProfiles.Services;

public sealed record LanguageOption(string Code, string DisplayName, string FilePath)
{
    public override string ToString() => DisplayName;
}

public sealed record LocalizedOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class LocalizationService
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string LocalesDirectory = Path.Combine(AppContext.BaseDirectory, "Locales");

    public static IReadOnlyList<LanguageOption> DiscoverLanguages()
    {
        EnsureBuiltInLocales();
        if (!Directory.Exists(LocalesDirectory)) return [];

        return Directory.EnumerateFiles(LocalesDirectory, "*.lang")
            .Select(path =>
            {
                var values = Parse(path);
                var code = Value(values, "meta.code", Path.GetFileNameWithoutExtension(path));
                var name = Value(values, "meta.name", code);
                return new LanguageOption(code, name, path);
            })
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string Text(string key, string languageCode)
    {
        var selected = Load(languageCode);
        if (selected.TryGetValue($"ui.{key}", out var value)) return value;

        var fallback = Load("es");
        return fallback.TryGetValue($"ui.{key}", out value) ? value : key;
    }

    public static string Format(string key, string languageCode, params object[] values) =>
        string.Format(Text(key, languageCode), values);

    public static string Category(string category, string languageCode)
    {
        var values = Load(languageCode);
        return values.TryGetValue($"category.{category}", out var translated) ? translated : category;
    }

    public static void LocalizeProfiles(IEnumerable<DisplayProfile> profiles, string languageCode)
    {
        var values = Load(languageCode);
        foreach (var profile in profiles)
        {
            var name = Value(values, $"profile.{profile.Id}.name", profile.Name);
            var description = Value(values, $"profile.{profile.Id}.description", profile.Description);
            profile.SetLocalizedText(name, Category(profile.Category, languageCode), description);
        }
    }

    private static IReadOnlyDictionary<string, string> Load(string languageCode)
    {
        return Cache.GetOrAdd(languageCode, code =>
        {
            var language = DiscoverLanguages().FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            return language is null ? new Dictionary<string, string>() : Parse(language.FilePath);
        });
    }

    private static IReadOnlyDictionary<string, string> Parse(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Replace("\\n", Environment.NewLine);
        }

        return values;
    }

    private static void EnsureBuiltInLocales()
    {
        try
        {
            Directory.CreateDirectory(LocalesDirectory);
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.Contains(".Locales.") && name.EndsWith(".lang")))
            {
                var segments = resourceName.Split('.');
                var destination = Path.Combine(LocalesDirectory, $"{segments[^2]}.lang");
                if (File.Exists(destination)) continue;

                using var source = assembly.GetManifestResourceStream(resourceName);
                using var target = File.Create(destination);
                source?.CopyTo(target);
            }
        }
        catch
        {
            // A read-only install directory must not prevent the app from opening.
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;
}
