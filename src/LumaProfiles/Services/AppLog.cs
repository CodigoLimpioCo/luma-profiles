using System.IO;
using System.Text;

namespace LumaProfiles.Services;

/// <summary>Minimal file logger. DDC/CI failures depend heavily on the monitor, so logs make support possible.</summary>
public static class AppLog
{
    private const int RetentionDays = 14;
    private static readonly object Gate = new();
    private static string _directory = Path.Combine(AppDataPaths.DefaultDirectory, "logs");
    private static bool _pruned;

    public static string DirectoryPath => _directory;

    public static void Configure(string logDirectory)
    {
        lock (Gate)
        {
            _directory = logDirectory;
            _pruned = false;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                PruneOldLogs();
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(' ').Append(level).Append(' ').Append(message);
                if (exception is not null) line.AppendLine().Append(exception);
                line.AppendLine();
                File.AppendAllText(Path.Combine(_directory, $"luma-{DateTime.Now:yyyyMMdd}.log"), line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the application.
        }
    }

    private static void PruneOldLogs()
    {
        if (_pruned) return;
        _pruned = true;
        var limit = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "luma-*.log"))
        {
            if (File.GetLastWriteTime(file) < limit) File.Delete(file);
        }
    }
}
