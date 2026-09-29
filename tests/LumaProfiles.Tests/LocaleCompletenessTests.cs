using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class LocaleCompletenessTests
{
    private static Dictionary<string, string> Read(string code) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Locales", code + ".lang"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryInterfaceTextExistsInEveryLanguage(string code)
    {
        var spanish = Read("es");
        var other = Read(code);

        var missing = spanish.Keys.Where(key => !key.StartsWith("profile.", StringComparison.Ordinal) && !other.ContainsKey(key)).Order().ToList();

        Assert.True(missing.Count == 0, $"{code}.lang lacks: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryDefaultProfileAndCategoryIsTranslated(string code)
    {
        using var dir = new TempDirectory();
        var profiles = new ProfileStore(dir.Path).Defaults;
        var texts = Read(code);

        var missing = profiles.SelectMany(profile => new[] { $"profile.{profile.Id}.name", $"profile.{profile.Id}.description" })
            .Concat(profiles.Select(profile => $"category.{profile.Category}").Distinct())
            .Where(key => !texts.ContainsKey(key) || texts[key].Length == 0)
            .ToList();

        Assert.True(missing.Count == 0, $"{code}.lang lacks: {string.Join(", ", missing)}");
    }
}
