using System.IO;

namespace LumaProfiles.Services;

internal static class AppDataPaths
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumaProfiles");
}
