using System.Windows;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;

    public App()
    {
        // Logging off or shutting down must not be held back by the "minimize to tray" / close prompt.
        SessionEnding += (_, _) => (MainWindow as MainWindow)?.PrepareToExit();
        // Crashes on other people's computers are impossible to diagnose without a trace.
        DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Unhandled exception.", args.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var forceBackground = StartupArguments.IsBackground(e.Args);
        var signIn = StartupArguments.IsSignInLaunch(e.Args) || PackagedStartup.WasLaunchedByStartupTask();

        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.TryAcquire())
        {
            // Opening the app again brings the running copy forward; the sign-in launch stays silent.
            if (!forceBackground && !signIn) _instanceGuard.SignalExisting();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        _instanceGuard.ListenForActivation(() => Dispatcher.BeginInvoke(window.ShowFromTray));
        if (forceBackground || (signIn && window.StartHiddenAtSignIn)) window.StartInBackground(); else window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
