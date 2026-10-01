using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class StartupArgumentsTests
{
    [Theory]
    [InlineData("--background", true)]
    [InlineData("--BACKGROUND", true)]
    [InlineData("--other", false)]
    public void IsBackground_RecognizesTheFlag(string argument, bool expected) =>
        Assert.Equal(expected, StartupArguments.IsBackground(["x", argument]));

    [Fact]
    public void IsBackground_IsFalseWithoutArguments() =>
        Assert.False(StartupArguments.IsBackground([]));

    [Theory]
    [InlineData("--startup", true)]
    [InlineData("--STARTUP", true)]
    [InlineData("--background", false)]
    public void IsSignInLaunch_RecognizesOnlyTheSignInFlag(string argument, bool expected) =>
        Assert.Equal(expected, StartupArguments.IsSignInLaunch(["x", argument]));

    [Fact]
    public void StartupCommand_QuotesThePathAndAddsTheFlag()
    {
        const string path = @"C:\Program Files\Luma\LumaProfiles.exe";

        Assert.Equal("\"C:\\Program Files\\Luma\\LumaProfiles.exe\" --startup", StartupArguments.StartupCommand(path));
        Assert.Equal("\"C:\\Program Files\\Luma\\LumaProfiles.exe\" --background", StartupArguments.PreviousStartupCommand(path));
        Assert.Equal("\"C:\\Program Files\\Luma\\LumaProfiles.exe\"", StartupArguments.LegacyStartupCommand(path));
    }
}

public class SingleInstanceGuardTests
{
    private static string UniqueName() => "LumaProfiles.Tests." + Guid.NewGuid().ToString("N");

    // A named mutex is re-entrant for the thread that owns it, so the "second launch" must come from another thread.
    private static bool AcquireOnOtherThread(SingleInstanceGuard guard)
    {
        // A dedicated thread, never Task.Run: waiting on an unstarted task may run it inline on the caller's thread.
        var acquired = false;
        var thread = new Thread(() => acquired = guard.TryAcquire());
        thread.Start();
        thread.Join();
        return acquired;
    }

    [Fact]
    public void OnlyTheFirstCopyAcquiresTheGuard()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.TryAcquire());
        Assert.False(AcquireOnOtherThread(second));
    }

    [Fact]
    public void GuardIsFreeAgainAfterTheFirstCopyExits()
    {
        var name = UniqueName();
        var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var next = new SingleInstanceGuard(name);

        Assert.True(AcquireOnOtherThread(next));
    }

    [Fact]
    public void SecondLaunchAsksTheFirstToComeForward()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        using var activated = new ManualResetEventSlim();
        first.ListenForActivation(activated.Set);

        second.SignalExisting();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisposeStopsTheListenerWithoutActivating()
    {
        var guard = new SingleInstanceGuard(UniqueName());
        Assert.True(guard.TryAcquire());
        var calls = 0;
        guard.ListenForActivation(() => Interlocked.Increment(ref calls));

        guard.Dispose();

        Assert.Equal(0, calls);
    }
}

public class StartupSettingsTests
{
    [Fact]
    public void StartHiddenAtSignIn_DefaultsToTrueAndRoundTrips()
    {
        using var dir = new TempDirectory();
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);
        var settings = store.Load();
        Assert.True(settings.StartHiddenAtSignIn);

        settings.StartHiddenAtSignIn = false;
        store.Save(settings);

        Assert.False(store.Load().StartHiddenAtSignIn);
    }

    [Fact]
    public void OldSettingsFilesWithoutTheOption_StartHidden()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), "{\"MinimizeToTray\":true}");

        Assert.True(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().StartHiddenAtSignIn);
    }

    [Fact]
    public void ManifestDeclaresTheStartupTaskTheCodeAsksFor()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "packaging", "msix", "AppxManifest.xml");
        var manifest = File.ReadAllText(Path.GetFullPath(manifestPath));

        Assert.Contains("windows.startupTask", manifest);
        Assert.Contains($"TaskId=\"{PackagedStartupTaskId}\"", manifest);
        Assert.Contains("Enabled=\"false\"", manifest);
    }

    // Kept in step with PackagedStartup.TaskId (internal to the app assembly, visible here).
    private const string PackagedStartupTaskId = "LumaProfilesStartup";

    [Fact]
    public void TaskIdInCodeMatchesTheManifestConstant() =>
        Assert.Equal(PackagedStartupTaskId, PackagedStartup.TaskId);
}
