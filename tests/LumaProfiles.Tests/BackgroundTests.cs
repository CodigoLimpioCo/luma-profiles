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

    [Fact]
    public void StartupCommand_QuotesThePathAndAddsTheFlag()
    {
        const string path = @"C:\Program Files\Luma\LumaProfiles.exe";

        Assert.Equal("\"C:\\Program Files\\Luma\\LumaProfiles.exe\" --background", StartupArguments.StartupCommand(path));
        Assert.Equal("\"C:\\Program Files\\Luma\\LumaProfiles.exe\"", StartupArguments.LegacyStartupCommand(path));
    }
}

public class SingleInstanceGuardTests
{
    private static string UniqueName() => "LumaProfiles.Tests." + Guid.NewGuid().ToString("N");

    // A named mutex is re-entrant for the thread that owns it, so the "second launch" must come from another thread.
    private static bool AcquireOnOtherThread(SingleInstanceGuard guard) => Task.Run(guard.TryAcquire).GetAwaiter().GetResult();

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
