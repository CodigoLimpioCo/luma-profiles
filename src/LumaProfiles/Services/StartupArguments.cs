namespace LumaProfiles.Services;

/// <summary>Command-line switches and the command Windows runs at sign-in.</summary>
public static class StartupArguments
{
    /// <summary>Forces a start hidden in the tray, whatever the settings say.</summary>
    public const string Background = "--background";

    /// <summary>Marks a sign-in launch; whether it opens hidden is the user's choice ("start in the tray").</summary>
    public const string SignIn = "--startup";

    public static bool IsBackground(IEnumerable<string> arguments) => Has(arguments, Background);

    public static bool IsSignInLaunch(IEnumerable<string> arguments) => Has(arguments, SignIn);

    public static string StartupCommand(string executablePath) => $"\"{executablePath}\" {SignIn}";

    /// <summary>The entry older versions wrote: the executable alone, which always opened the window.</summary>
    public static string LegacyStartupCommand(string executablePath) => $"\"{executablePath}\"";

    /// <summary>The entry written by 0.8.1, which always started hidden.</summary>
    public static string PreviousStartupCommand(string executablePath) => $"\"{executablePath}\" {Background}";

    private static bool Has(IEnumerable<string> arguments, string flag) =>
        arguments.Any(argument => argument.Equals(flag, StringComparison.OrdinalIgnoreCase));
}
