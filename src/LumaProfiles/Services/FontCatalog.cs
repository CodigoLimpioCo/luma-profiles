using System.Windows.Media;

namespace LumaProfiles.Services;

/// <summary>The typefaces offered in Settings, limited to the ones installed on this computer.</summary>
public static class FontCatalog
{
    public const string DefaultFont = "Segoe UI Variable Text";

    /// <summary>Ordered by how well they suit an interface; anything not installed is skipped.</summary>
    public static IReadOnlyList<string> Candidates { get; } =
    [
        "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Candara", "Arial",
        "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Cambria", "Consolas"
    ];

    public static IReadOnlyList<string> Installed()
    {
        try
        {
            var installed = Fonts.SystemFontFamilies.Select(family => family.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = Candidates.Where(installed.Contains).ToList();
            return available.Count > 0 ? available : [DefaultFont];
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not list the installed fonts.", exception);
            return [DefaultFont];
        }
    }
}
