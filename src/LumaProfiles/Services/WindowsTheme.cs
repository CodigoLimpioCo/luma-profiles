using Microsoft.Win32;

namespace LumaProfiles.Services;

/// <summary>Reads the "apps use light theme" preference of Windows.</summary>
public static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool PrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not read the Windows theme; assuming dark.", exception);
            return true;
        }
    }
}
