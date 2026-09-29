using System.Windows;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class App : Application
{
    public App()
    {
        // Crashes on other people's computers are impossible to diagnose without a trace.
        DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Unhandled exception.", args.ExceptionObject as Exception);
    }
}
