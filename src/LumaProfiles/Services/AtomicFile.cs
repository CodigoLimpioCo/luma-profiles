using System.IO;
using System.Text;

namespace LumaProfiles.Services;

internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a temporary file first so an interrupted write never leaves a truncated target.</summary>
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, Utf8NoBom);
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>Moves an unreadable user file aside so it can be recovered instead of silently overwritten.</summary>
    public static string? Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Move(path, target, overwrite: true);
            return target;
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not quarantine '{path}'.", exception);
            return null;
        }
    }
}
