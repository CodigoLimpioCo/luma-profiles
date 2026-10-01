namespace LumaProfiles.Services;

/// <summary>Command-line switches and the command Windows runs at sign-in.</summary>
public static class StartupArguments
{
    /// <summary>Starts hidden in the tray instead of showing the window (used by the Windows startup entry).</summary>
    public const string Background = "--background";

    public static bool IsBackground(IEnumerable<string> arguments) =>
        arguments.Any(argument => argument.Equals(Background, StringComparison.OrdinalIgnoreCase));

    public static string StartupCommand(string executablePath) => $"\"{executablePath}\" {Background}";

    /// <summary>The command older versions wrote: the executable alone, which opened the window at every sign-in.</summary>
    public static string LegacyStartupCommand(string executablePath) => $"\"{executablePath}\"";
}
