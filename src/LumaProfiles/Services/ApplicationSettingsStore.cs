using System.IO;
using System.Text.Json;
using LumaProfiles.Models;
using Microsoft.Win32;

namespace LumaProfiles.Services;

public sealed class ApplicationSettingsStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "LumaProfiles";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;
    private readonly bool _manageStartup;

    /// <param name="dataDirectory">Override for tests; defaults to %LOCALAPPDATA%\LumaProfiles.</param>
    /// <param name="manageStartup">When false the Windows Run registry key is never touched.</param>
    public ApplicationSettingsStore(string? dataDirectory = null, bool manageStartup = true)
    {
        _dataDirectory = dataDirectory ?? AppDataPaths.DefaultDirectory;
        _manageStartup = manageStartup;
    }

    private string SettingsPath => Path.Combine(_dataDirectory, "settings.json");

    /// <summary>Set when settings.json was unreadable and had to be set aside during the last <see cref="Load"/>.</summary>
    public string? LoadWarning { get; private set; }

    public ApplicationSettings Load()
    {
        LoadWarning = null;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (settings is not null)
                {
                    settings.MonitorCorrections ??= [];
                    settings.OriginalMonitorStates ??= [];
                    settings.AppRules ??= [];
                    settings.ProfileHotkeys ??= [];
                    settings.Schedule ??= new ScheduleSettings();
                    settings.Schedule.EnsureEntries();
                    if (_manageStartup) settings.StartWithWindows = IsStartupEnabled();
                    return settings;
                }
            }
        }
        catch (Exception exception)
        {
            var backup = AtomicFile.Quarantine(SettingsPath);
            LoadWarning = backup ?? "settings.json";
            AppLog.Error($"settings.json is damaged; moved to '{backup}'. Defaults restored.", exception);
        }

        var defaults = new ApplicationSettings();
        defaults.Schedule.EnsureEntries();
        if (_manageStartup) defaults.StartWithWindows = SetStartupEnabled(enabled: true);
        try
        {
            Save(defaults);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not persist default settings; running with in-memory defaults.", exception);
        }
        return defaults;
    }

    public void Save(ApplicationSettings settings) =>
        AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    public bool SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath)) return false;
                key.SetValue(RunValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not update the Windows startup entry.", exception);
            return false;
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not read the startup registry entry; assuming it is disabled.", exception);
            return false;
        }
    }
}
