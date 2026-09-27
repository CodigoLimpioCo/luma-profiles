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

    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumaProfiles");

    private string SettingsPath => Path.Combine(_dataDirectory, "settings.json");

    public ApplicationSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (settings is not null)
                {
                    settings.MonitorCorrections ??= [];
                    settings.StartWithWindows = IsStartupEnabled();
                    return settings;
                }
            }
        }
        catch
        {
            // Invalid user settings must never prevent the application from starting.
        }

        var defaults = new ApplicationSettings();
        defaults.StartWithWindows = SetStartupEnabled(enabled: true);
        try
        {
            Save(defaults);
        }
        catch
        {
            // The in-memory defaults still let the application run when storage is unavailable.
        }
        return defaults;
    }

    public void Save(ApplicationSettings settings)
    {
        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

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
        catch
        {
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
        catch
        {
            return false;
        }
    }
}
