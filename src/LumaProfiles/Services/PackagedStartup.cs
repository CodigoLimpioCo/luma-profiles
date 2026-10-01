using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace LumaProfiles.Services;

/// <summary>
/// Sign-in startup for the Microsoft Store (MSIX) build. Packaged apps cannot use the registry "Run" key; they declare a
/// <c>windows.startupTask</c> in the manifest and ask Windows to enable it. The user can also turn it off from
/// Windows Settings > Apps > Startup or Task Manager, which this class reports as <see cref="StartupTaskState"/>.
/// </summary>
internal static class PackagedStartup
{
    /// <summary>Must match <c>uap5:StartupTask/@TaskId</c> in <c>packaging/msix/AppxManifest.xml</c>.</summary>
    public const string TaskId = "LumaProfilesStartup";

    private const int NoPackage = 15700;

    public static bool IsPackaged { get; } = DetectPackaged();

    /// <summary>True when Windows started this process because the user enabled the startup task.</summary>
    public static bool WasLaunchedByStartupTask()
    {
        if (!IsPackaged) return false;
        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not read the activation kind.", exception);
            return false;
        }
    }

    /// <summary>Whether the task is on; null when it cannot be read (not packaged, or Windows refused).</summary>
    public static bool? IsEnabled()
    {
        if (!IsPackaged) return null;
        try
        {
            // Not awaited on the UI thread: the call completes on its own thread, so blocking here cannot deadlock.
            var task = Task.Run(async () => await StartupTask.GetAsync(TaskId)).GetAwaiter().GetResult();
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not read the startup task state.", exception);
            return null;
        }
    }

    /// <summary>
    /// Turns the startup task on or off. Call from the UI thread: enabling may show a Windows confirmation.
    /// </summary>
    public static async Task<StartupResult> SetEnabledAsync(bool enable)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enable)
            {
                task.Disable();
                return StartupResult.Done;
            }

            return (await task.RequestEnableAsync()) switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupResult.Done,
                StartupTaskState.DisabledByUser => StartupResult.BlockedByUser,
                StartupTaskState.DisabledByPolicy => StartupResult.BlockedByPolicy,
                _ => StartupResult.Failed,
            };
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not change the startup task.", exception);
            return StartupResult.Failed;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    private static bool DetectPackaged()
    {
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != NoPackage;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}

public enum StartupResult { Done, BlockedByUser, BlockedByPolicy, Failed }
