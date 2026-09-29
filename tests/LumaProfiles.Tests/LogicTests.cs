using System.Runtime.CompilerServices;
using LumaProfiles.Models;
using LumaProfiles.Services;
using LumaProfiles.ViewModels;

namespace LumaProfiles.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RedirectLogs() =>
        AppLog.Configure(Path.Combine(Path.GetTempPath(), "luma-tests-logs"));
}

internal sealed class InlineWorkRunner : IWorkRunner
{
    public Task<T> RunAsync<T>(Func<T> work) => Task.FromResult(work());
}

/// <summary>Lets a test hold background work "in flight" and finish it on demand.</summary>
internal sealed class ManualWorkRunner : IWorkRunner
{
    private readonly Queue<Action> _pending = new();

    public int PendingCount => _pending.Count;

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();
        _pending.Enqueue(() => completion.SetResult(work()));
        return completion.Task;
    }

    public void RunNext() => _pending.Dequeue()();
}

public class ScheduleResolverTests
{
    private static readonly ScheduleSettings Default = new() { DayStart = "07:00", NightStart = "20:00" };

    [Theory]
    [InlineData("06:59", ScheduleSlot.Night)]
    [InlineData("07:00", ScheduleSlot.Day)]
    [InlineData("12:00", ScheduleSlot.Day)]
    [InlineData("19:59", ScheduleSlot.Day)]
    [InlineData("20:00", ScheduleSlot.Night)]
    [InlineData("23:59", ScheduleSlot.Night)]
    [InlineData("00:00", ScheduleSlot.Night)]
    public void Resolve_SplitsTheDayAtConfiguredTimes(string time, ScheduleSlot expected) =>
        Assert.Equal(expected, ScheduleResolver.Resolve(Default, TimeOnly.Parse(time)));

    [Theory]
    [InlineData("22:00", ScheduleSlot.Day)]
    [InlineData("01:00", ScheduleSlot.Day)]
    [InlineData("06:00", ScheduleSlot.Night)]
    public void Resolve_HandlesDayWindowThatWrapsPastMidnight(string time, ScheduleSlot expected)
    {
        var wrapped = new ScheduleSettings { DayStart = "21:00", NightStart = "05:00" };

        Assert.Equal(expected, ScheduleResolver.Resolve(wrapped, TimeOnly.Parse(time)));
    }

    [Fact]
    public void Resolve_InvalidTimesFallBackToDefaults() =>
        Assert.Equal(ScheduleSlot.Night,
            ScheduleResolver.Resolve(new ScheduleSettings { DayStart = "x", NightStart = "y" }, new TimeOnly(21, 0)));

    [Theory]
    [InlineData("20:30", true)]
    [InlineData("7:00", false)]
    [InlineData("25:00", false)]
    [InlineData("", false)]
    public void TryParseTime_RequiresHHmm(string text, bool valid) =>
        Assert.Equal(valid, ScheduleResolver.TryParseTime(text, out _));

    [Fact]
    public void ProfileFor_ReturnsSlotProfile()
    {
        var schedule = new ScheduleSettings { DayProfileId = "a", NightProfileId = "b" };

        Assert.Equal("a", ScheduleResolver.ProfileFor(schedule, ScheduleSlot.Day));
        Assert.Equal("b", ScheduleResolver.ProfileFor(schedule, ScheduleSlot.Night));
    }
}

public class AppRuleEngineTests
{
    private readonly List<AppProfileRule> _rules =
    [
        new() { ProcessName = "game", ProfileId = "gamer-competitive" },
        new() { ProcessName = "editor.exe", ProfileId = "faithful-studio" }
    ];

    [Fact]
    public void Evaluate_MatchingProcessAppliesOnceThenIsQuiet()
    {
        var engine = new AppRuleEngine("LumaProfiles");

        var first = engine.Evaluate("GAME", _rules);
        var second = engine.Evaluate("game", _rules);

        Assert.Equal(RuleAction.Apply, first.Action);
        Assert.Equal("gamer-competitive", first.Rule!.ProfileId);
        Assert.Equal(RuleAction.None, second.Action);
    }

    [Fact]
    public void Evaluate_LeavingRuledAppRestores()
    {
        var engine = new AppRuleEngine();
        engine.Evaluate("game", _rules);

        Assert.Equal(RuleAction.Restore, engine.Evaluate("notepad", _rules).Action);
        Assert.Equal(RuleAction.None, engine.Evaluate("notepad", _rules).Action);
    }

    [Fact]
    public void Evaluate_SwitchingBetweenRuledAppsAppliesTheNewRule()
    {
        var engine = new AppRuleEngine();
        engine.Evaluate("game", _rules);

        var decision = engine.Evaluate("editor", _rules);

        Assert.Equal(RuleAction.Apply, decision.Action);
        Assert.Equal("faithful-studio", decision.Rule!.ProfileId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LumaProfiles")]
    [InlineData("lumaprofiles.exe")]
    public void Evaluate_UnknownOrOwnWindowsNeverChangeState(string? process)
    {
        var engine = new AppRuleEngine("LumaProfiles");
        engine.Evaluate("game", _rules);

        Assert.Equal(RuleAction.None, engine.Evaluate(process, _rules).Action);
        Assert.NotNull(engine.ActiveRule);
    }

    [Fact]
    public void Normalize_StripsExeSuffixAndWhitespace() =>
        Assert.Equal("Game", AppRuleEngine.Normalize("  Game.EXE "));
}

public class MainViewModelTests
{
    private sealed class FakeMonitorService : IMonitorService
    {
        public List<string> Applied { get; } = [];
        public int NeutralCalls { get; private set; }
        public int OriginalCalls { get; private set; }
        public bool HasOriginal { get; set; } = true;
        public bool FailApply { get; set; }
        public bool ThrowOnApply { get; set; }
        public List<DisplayInfo> Displays { get; set; } = [];
        public bool UseMonitorControls { get; set; } = true;
        public List<OriginalMonitorState> KnownOriginals { get; } = [];
        public void UseOriginalStates(IEnumerable<OriginalMonitorState> states)
        {
            KnownOriginals.Clear();
            KnownOriginals.AddRange(states);
        }
        public IReadOnlyList<DisplayInfo> GetDisplays() => Displays;
        public IReadOnlyList<OriginalMonitorState>? LastRestoredStates { get; private set; }
        public string? LastRestoredPlan { get; private set; }

        public ApplyResult Apply(DisplayProfile profile, string target)
        {
            if (ThrowOnApply) throw new InvalidOperationException("boom");
            Applied.Add(profile.Id);
            return FailApply ? new ApplyResult(0, ["no display"], []) : Result();
        }

        public ApplyResult Preview(DisplayProfile profile, string target) => Result();
        public List<OriginalMonitorState> Capturable { get; } = [];

        public IReadOnlyList<OriginalMonitorState> CaptureOriginalStates(IEnumerable<string> knownMonitorIds)
        {
            var known = knownMonitorIds.ToHashSet();
            return Capturable.Where(state => !known.Contains(state.MonitorId)).ToList();
        }
        public string? GetActivePowerPlan() => null;
        public ApplyResult RestoreOriginal(IEnumerable<OriginalMonitorState> s, string t, string? p)
        {
            OriginalCalls++;
            LastRestoredStates = s.ToList();
            LastRestoredPlan = p;
            return HasOriginal ? Result() : new ApplyResult(0, ["none"], []);
        }
        public ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections, CancellationToken cancellationToken = default) => Result();
        private int _snapshots;
        public IReadOnlyList<OriginalMonitorState> CaptureCurrentStates() => [new OriginalMonitorState { MonitorId = $"snapshot-{++_snapshots}" }];

        public ApplyResult RestoreNeutral(string target)
        {
            NeutralCalls++;
            return Result();
        }

        private static ApplyResult Result() =>
            new(1, [], [new AppliedMonitor("mon-1", @"\\.\DISPLAY1")]);
    }

    private sealed class FakeShell : IShellService
    {
        public IReadOnlyList<DisplayInfo>? Identified { get; private set; }
        public void IdentifyDisplays(IReadOnlyList<DisplayInfo> displays) => Identified = displays;
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public List<string> Opened { get; } = [];
        public void OpenUrl(string url) => Opened.Add(url);
        public string? PickSaveFile(string title, string suggestedFileName) => SavePath;
        public string? PickOpenFile(string title) => OpenPath;
    }

    private static DisplayInfo Display(int number) =>
        new(number, $"\\\\.\\DISPLAY{number}", $"MONITOR\\TEST\\{number}", (number - 1) * 1920, 0, 1920, 1080, number == 1);

    private static (MainViewModel Vm, FakeMonitorService Monitor, FakeShell Shell, TempDirectory Dir) Create(int displayCount = 2)
    {
        var dir = new TempDirectory();
        var monitor = new FakeMonitorService { Displays = Enumerable.Range(1, displayCount).Select(Display).ToList() };
        var shell = new FakeShell();
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), shell, workRunner: new InlineWorkRunner());
        return (vm, monitor, shell, dir);
    }

    [Fact]
    public void ApplyProfileCommand_AppliesAndMarksProfileActive()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        var target = vm.Profiles.First(p => p.Id == "eyes-night");

        vm.ApplyProfileCommand.Execute(target);

        Assert.Equal(["eyes-night"], monitor.Applied);
        Assert.True(target.IsActive);
        Assert.Single(vm.Profiles, p => p.IsActive);
        Assert.Same(target, vm.SelectedProfile);
    }

    [Fact]
    public void ToggleFavorite_PersistsAndFiltersFavoritesCategory()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        var profile = vm.Profiles[3];

        vm.ToggleFavoriteCommand.Execute(profile);
        vm.SelectCategoryCommand.Execute("Favoritos");

        Assert.Equal([profile], vm.VisibleProfiles);
        Assert.True(new ProfileStore(dir.Path).Load()[3].IsFavorite);
    }

    [Fact]
    public void SearchText_FiltersByName()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.SearchText = "eSports";

        Assert.NotEmpty(vm.VisibleProfiles);
        Assert.All(vm.VisibleProfiles, p =>
            Assert.Contains("esports", p.DisplayName + p.DisplayDescription + p.DisplayCategory, StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.VisibleProfiles.Count < vm.Profiles.Count);
    }

    [Fact]
    public void RepairCommand_RestoresOriginalStateAndClearsActiveProfile()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        vm.RepairCommand.Execute(null);

        Assert.Equal(1, monitor.OriginalCalls);
        Assert.Equal(0, monitor.NeutralCalls);
        Assert.DoesNotContain(vm.Profiles, p => p.IsActive);
    }

    [Fact]
    public void RepairCommand_WithoutCapturedOriginalFallsBackToNeutral()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        monitor.HasOriginal = false;

        vm.RepairCommand.Execute(null);

        Assert.Equal(1, monitor.NeutralCalls);
    }

    [Fact]
    public void ForegroundRule_AppliesThenRestoresPreviousProfile()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "natural"));
        vm.NewRuleProcess = "game.exe";
        vm.NewRuleProfileId = "gamer-competitive";
        vm.AddAppRuleCommand.Execute(null);
        monitor.Applied.Clear();

        vm.OnForegroundProcessChanged("game");
        vm.OnForegroundProcessChanged("explorer");

        Assert.Equal(["gamer-competitive", "natural"], monitor.Applied);
        Assert.Single(vm.Profiles, p => p.IsActive);
        Assert.True(vm.Profiles.First(p => p.Id == "natural").IsActive);
    }

    [Fact]
    public void ForegroundRule_WithoutPreviousProfileFallsBackToNeutral()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.NewRuleProcess = "game";
        vm.NewRuleProfileId = "gamer-competitive";
        vm.AddAppRuleCommand.Execute(null);

        vm.OnForegroundProcessChanged("game");
        vm.OnForegroundProcessChanged("explorer");

        Assert.Equal(1, monitor.OriginalCalls);
    }

    [Fact]
    public void AddAppRule_ReplacesExistingRuleForSameProcess()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.NewRuleProfileId = "natural";
        vm.NewRuleProcess = "Game.exe";
        vm.AddAppRuleCommand.Execute(null);
        vm.NewRuleProfileId = "gamer-arcade";
        vm.NewRuleProcess = "game";
        vm.AddAppRuleCommand.Execute(null);

        var rule = Assert.Single(vm.AppRules);
        Assert.Equal("game", rule.ProcessName);
        Assert.Equal("gamer-arcade", rule.Rule.ProfileId);
    }

    [Fact]
    public void ExportThenImport_RoundTripsThroughShellDialogs()
    {
        var (vm, _, shell, dir) = Create();
        using var _ = dir;
        var file = Path.Combine(dir.Path, "backup.json");
        vm.Profiles[1].Brightness = 7;
        shell.SavePath = file;
        vm.ExportProfilesCommand.Execute(null);

        vm.Profiles[1].Brightness = 99;
        shell.OpenPath = file;
        vm.ImportProfilesCommand.Execute(null);

        Assert.Equal(7, vm.Profiles[1].Brightness);
    }

    [Fact]
    public void ImportProfiles_WithBrokenFileReportsErrorInsteadOfThrowing()
    {
        var (vm, _, shell, dir) = Create();
        using var _ = dir;
        var file = Path.Combine(dir.Path, "bad.json");
        File.WriteAllText(file, "not json");
        shell.OpenPath = file;

        vm.ImportProfilesCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.StatusMessage));
    }

    [Fact]
    public void ScheduleTime_RejectsInvalidValues()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.ScheduleNightStart = "99:99";

        Assert.Equal("20:00", vm.ScheduleNightStart);
    }

    [Fact]
    public void ViewMode_DefaultsToCardsAndPersistsValidChoices()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.Equal("Cards", vm.ViewMode);
        Assert.True(vm.IsViewCards);

        vm.IsViewDetails = true;

        Assert.Equal("Details", vm.ViewMode);
        Assert.False(vm.IsViewCards);
        var reloaded = new ApplicationSettingsStore(dir.Path, manageStartup: false).Load();
        Assert.Equal("Details", reloaded.ProfilesViewMode);
    }

    [Fact]
    public void ViewMode_IgnoresUnknownValues()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.ViewMode = "Hologram";
        vm.IsViewList = false;

        Assert.Equal("Cards", vm.ViewMode);
        Assert.Equal(5, MainViewModel.ViewModes.Count);
    }

    [Fact]
    public void Sidebar_ToggleCollapsesAndPersists()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.True(vm.IsSidebarExpanded);

        vm.ToggleSidebarCollapseCommand.Execute(null);

        Assert.True(vm.IsSidebarCollapsed);
        Assert.True(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().IsLeftPanelCollapsed);
    }

    [Fact]
    public void Sidebar_ForcedCompactOverridesWithoutSavingAndHidesToggle()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.SetSidebarForcedCompact(true);

        Assert.True(vm.IsSidebarCollapsed);
        Assert.False(vm.IsSidebarToggleAvailable);
        Assert.False(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().IsLeftPanelCollapsed);

        vm.SetSidebarForcedCompact(false);
        Assert.True(vm.IsSidebarExpanded);
    }

    [Fact]
    public void EditProfileCommand_SelectsProfileAndRequestsInspector()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        var requested = 0;
        vm.InspectorRequested += (_, _) => requested++;
        var target = vm.Profiles[5];

        vm.EditProfileCommand.Execute(target);

        Assert.Same(target, vm.SelectedProfile);
        Assert.Equal(1, requested);
    }

    [Fact]
    public void OpenSettingsCommand_TogglesAndCategoryClosesIt()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.OpenSettingsCommand.Execute(null);
        Assert.True(vm.IsSettingsOpen);
        vm.OpenSettingsCommand.Execute(null);
        Assert.False(vm.IsSettingsOpen);

        vm.OpenSettingsCommand.Execute(null);
        vm.SelectCategoryCommand.Execute("Favoritos");
        Assert.False(vm.IsSettingsOpen);
    }

    [Fact]
    public void ApplyFromUi_AsksForConfirmationAndKeepClearsIt()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;

        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        Assert.True(vm.IsConfirmationPending);
        Assert.Equal(30, vm.ConfirmRemainingSeconds);
        vm.KeepChangesCommand.Execute(null);
        Assert.False(vm.IsConfirmationPending);
        Assert.Equal(0, monitor.OriginalCalls);
        Assert.True(vm.Profiles[0].IsActive);
    }

    [Fact]
    public void RevertCommand_RestoresSnapshotAndPreviousProfile()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "natural"));
        vm.KeepChangesCommand.Execute(null);

        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "eyes-night"));
        vm.RevertChangesCommand.Execute(null);

        Assert.False(vm.IsConfirmationPending);
        Assert.Equal("snapshot-2", Assert.Single(monitor.LastRestoredStates!).MonitorId);
        Assert.True(vm.Profiles.First(p => p.Id == "natural").IsActive);
        Assert.False(vm.Profiles.First(p => p.Id == "eyes-night").IsActive);
    }

    [Fact]
    public void Confirmation_TimeoutRevertsAfterThirtyTicks()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        for (var i = 0; i < 29; i++) vm.ConfirmTick();
        Assert.True(vm.IsConfirmationPending);
        Assert.Equal(0, monitor.OriginalCalls);

        vm.ConfirmTick();

        Assert.False(vm.IsConfirmationPending);
        Assert.Equal(1, monitor.OriginalCalls);
        Assert.DoesNotContain(vm.Profiles, p => p.IsActive);
    }

    [Fact]
    public void Confirmation_CanBeDisabledInSettings()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.ConfirmChanges = false;

        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        Assert.False(vm.IsConfirmationPending);
        Assert.False(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().ConfirmChanges);
    }

    [Fact]
    public void Confirmation_NotRequestedWhenNothingChanged()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        monitor.FailApply = true;

        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        Assert.False(vm.IsConfirmationPending);
    }

    [Fact]
    public void RepeatedApply_RevertGoesBackToTheStateBeforeTheFirstChange()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "natural"));
        vm.KeepChangesCommand.Execute(null);

        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "eyes-night"));
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "gamer-competitive"));
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(p => p.Id == "hdr-gaming"));

        Assert.True(vm.IsConfirmationPending);
        Assert.Equal(30, vm.ConfirmRemainingSeconds);
        Assert.Equal(0, monitor.OriginalCalls);

        vm.RevertChangesCommand.Execute(null);

        // snapshot-1 was taken before the very first unconfirmed change (the 2nd snapshot ever, natural came first)
        Assert.Equal("snapshot-2", Assert.Single(monitor.LastRestoredStates!).MonitorId);
        Assert.True(vm.Profiles.First(p => p.Id == "natural").IsActive);
        Assert.Single(vm.Profiles, p => p.IsActive);
    }

    [Fact]
    public void RepeatedApply_KeepAcceptsTheLatestAndStartsAFreshSnapshot()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);
        vm.ApplyProfileCommand.Execute(vm.Profiles[1]);
        vm.KeepChangesCommand.Execute(null);

        vm.ApplyProfileCommand.Execute(vm.Profiles[2]);
        vm.RevertChangesCommand.Execute(null);

        Assert.Equal("snapshot-2", Assert.Single(monitor.LastRestoredStates!).MonitorId);
        Assert.True(vm.Profiles[1].IsActive);
    }

    [Fact]
    public void RepeatedApply_TimeoutAlsoRevertsToTheOriginalState()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);
        for (var i = 0; i < 20; i++) vm.ConfirmTick();
        vm.ApplyProfileCommand.Execute(vm.Profiles[1]);
        Assert.Equal(30, vm.ConfirmRemainingSeconds);

        for (var i = 0; i < 30; i++) vm.ConfirmTick();

        Assert.Equal("snapshot-1", Assert.Single(monitor.LastRestoredStates!).MonitorId);
        Assert.DoesNotContain(vm.Profiles, p => p.IsActive);
    }

    [Fact]
    public void Busy_IsTrueWhileWorkRunsAndButtonsAreDisabled()
    {
        var dir = new TempDirectory();
        using var _ = dir;
        var runner = new ManualWorkRunner();
        var monitor = new FakeMonitorService { Displays = [Display(1)] };
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(), workRunner: runner);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ApplyProfileCommand.CanExecute(vm.Profiles[0]));

        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        Assert.True(vm.IsBusy);
        Assert.False(vm.ApplyProfileCommand.CanExecute(vm.Profiles[0]));
        Assert.False(vm.RepairCommand.CanExecute(null));
        Assert.False(vm.RestoreOriginalCommand.CanExecute(null));
        Assert.Contains(vm.Profiles[0].DisplayName, vm.StatusMessage);
        Assert.Empty(monitor.Applied);

        while (runner.PendingCount > 0) runner.RunNext();

        Assert.False(vm.IsBusy);
        Assert.True(vm.ApplyProfileCommand.CanExecute(vm.Profiles[0]));
        Assert.Equal([vm.Profiles[0].Id], monitor.Applied);
        Assert.True(vm.Profiles[0].IsActive);
    }

    [Fact]
    public void Operations_RunOneAtATimeInOrder()
    {
        var dir = new TempDirectory();
        using var _ = dir;
        var runner = new ManualWorkRunner();
        var monitor = new FakeMonitorService { Displays = [Display(1)] };
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(), workRunner: runner);

        vm.ApplyProfileById("natural");
        vm.ApplyProfileById("eyes-night");

        Assert.Equal(1, runner.PendingCount);
        runner.RunNext();
        Assert.Equal(["natural"], monitor.Applied);
        while (runner.PendingCount > 0) runner.RunNext();
        Assert.Equal(["natural", "eyes-night"], monitor.Applied);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Busy_ClearsEvenWhenTheOperationFails()
    {
        var dir = new TempDirectory();
        using var _ = dir;
        var monitor = new FakeMonitorService { Displays = [Display(1)], ThrowOnApply = true };
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(), workRunner: new InlineWorkRunner());

        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        Assert.False(vm.IsBusy);
        Assert.False(vm.IsConfirmationPending);
        Assert.Contains("boom", vm.StatusMessage);
    }

    [Fact]
    public void NeutralizeAndRestoreFromUi_AlsoAskForConfirmation()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.RepairCommand.Execute(null);
        Assert.True(vm.IsConfirmationPending);
        vm.KeepChangesCommand.Execute(null);

        vm.RestoreOriginalCommand.Execute(null);
        Assert.True(vm.IsConfirmationPending);
    }

    [Fact]
    public void AutomationAndHotkeys_DoNotAskForConfirmation()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.CycleProfile(+1);
        vm.ApplyProfileById("natural");
        vm.ApplyNeutral();

        Assert.False(vm.IsConfirmationPending);
    }

    [Fact]
    public void Shutdown_RevertsAnUnconfirmedChange()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        vm.Shutdown();

        Assert.Equal(1, monitor.OriginalCalls);
        Assert.False(vm.IsConfirmationPending);
    }

    [Fact]
    public void ThemeMode_DefaultsFromLegacyFlagAndPersistsExplicitChoice()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.Equal("Dark", vm.ThemeMode);
        Assert.True(vm.IsDarkTheme);

        vm.IsThemeLight = true;

        Assert.False(vm.IsDarkTheme);
        var saved = new ApplicationSettingsStore(dir.Path, manageStartup: false).Load();
        Assert.Equal("Light", saved.ThemeMode);
        Assert.False(saved.IsDarkTheme);
    }

    [Fact]
    public void ThemeMode_SystemFollowsWindowsPreference()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        var systemDark = false;
        vm.UseSystemThemeProvider(() => systemDark);

        vm.IsThemeSystem = true;
        Assert.False(vm.IsDarkTheme);

        systemDark = true;
        vm.RefreshSystemTheme();
        Assert.True(vm.IsDarkTheme);
    }

    [Fact]
    public void HeaderThemeToggle_SwitchesToExplicitMode()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.ToggleThemeCommand.Execute(null);

        Assert.Equal("Light", vm.ThemeMode);
        Assert.False(vm.IsThemeSystem);
    }

    [Fact]
    public void Accent_SelectingSwatchPersistsAndUpdatesChoices()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.Equal("Cyan", vm.AccentKey);

        vm.AccentChoices.First(choice => choice.Key == "Amber").IsSelected = true;

        Assert.Equal("Amber", vm.AccentKey);
        Assert.Equal("Amber", vm.Accent.Key);
        Assert.Single(vm.AccentChoices, choice => choice.IsSelected);
        Assert.Equal("Amber", new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().AccentColor);
        vm.AccentKey = "NotAColor";
        Assert.Equal("Amber", vm.AccentKey);
    }

    [Fact]
    public void ScrollBarThickness_IsClampedAndRounded()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.ScrollBarThickness = 100;
        Assert.Equal(MainViewModel.MaxScrollBarThickness, vm.ScrollBarThickness);
        vm.ScrollBarThickness = 0;
        Assert.Equal(MainViewModel.MinScrollBarThickness, vm.ScrollBarThickness);
        vm.ScrollBarThickness = 9.4;
        Assert.Equal(9, vm.ScrollBarThickness);
        Assert.Equal("9 px", vm.ScrollBarThicknessLabel);
    }

    [Fact]
    public void SettingsSections_SwitchAndExposeTitles()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.True(vm.IsSectionAppearance);

        vm.IsSectionData = true;

        Assert.Equal("Data", vm.SettingsSection);
        Assert.False(vm.IsSectionAppearance);
        Assert.False(vm.IsResetAvailable);
        vm.SettingsSection = "Bogus";
        Assert.Equal("Data", vm.SettingsSection);
        Assert.All(MainViewModel.SettingsSections, section =>
        {
            vm.SettingsSection = section;
            Assert.False(string.IsNullOrWhiteSpace(vm.SectionTitle));
            Assert.NotEqual("Section" + section, vm.SectionTitle);
        });
    }

    [Fact]
    public void ResetSection_RestoresOnlyThatSectionsDefaults()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.IsThemeLight = true;
        vm.AccentKey = "Rose";
        vm.ScrollBarThickness = 12;
        vm.IsViewList = true;

        vm.SettingsSection = "Appearance";
        vm.ResetSectionCommand.Execute(null);

        Assert.Equal("Dark", vm.ThemeMode);
        Assert.Equal("Cyan", vm.AccentKey);
        Assert.Equal(MainViewModel.DefaultScrollBarThickness, vm.ScrollBarThickness);
        Assert.Equal("List", vm.ViewMode);

        vm.SettingsSection = "Layout";
        vm.SidebarCollapsedPreference = true;
        vm.LeftPanelRequested = false;
        vm.ResetSectionCommand.Execute(null);
        Assert.Equal("Cards", vm.ViewMode);
        Assert.False(vm.SidebarCollapsedPreference);
        Assert.True(vm.LeftPanelRequested);
        Assert.True(vm.RightPanelRequested);
    }

    [Fact]
    public void ResetAutomation_ClearsRulesAndSchedule()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.NewRuleProcess = "game";
        vm.NewRuleProfileId = "natural";
        vm.AddAppRuleCommand.Execute(null);
        vm.GlobalHotkeysEnabled = false;
        vm.ScheduleNightStart = "22:15";

        vm.SettingsSection = "Automation";
        vm.ResetSectionCommand.Execute(null);

        Assert.Empty(vm.AppRules);
        Assert.True(vm.GlobalHotkeysEnabled);
        Assert.Equal("20:00", vm.ScheduleNightStart);
        Assert.False(vm.ScheduleEnabled);
    }

    [Fact]
    public void Displays_OneButtonPerMonitorAndAllSelectedByDefault()
    {
        var (vm, _, _, dir) = Create(displayCount: 3);
        using var _ = dir;

        Assert.Equal(3, vm.DisplayButtons.Count);
        Assert.True(vm.HasMultipleDisplays);
        Assert.True(vm.IsAllDisplaysSelected);
        Assert.Equal("Ambas pantallas", vm.SelectedMonitorTarget);
        Assert.Contains("1920×1080", vm.DisplayButtons[0].Tooltip);
    }

    [Fact]
    public void Displays_CanPickOneAndThreeButNotTwo()
    {
        var (vm, monitor, _, dir) = Create(displayCount: 3);
        using var _ = dir;

        vm.DisplayButtons[1].IsSelected = false;

        Assert.Equal("Pantalla 1, Pantalla 3", vm.SelectedMonitorTarget);
        Assert.False(vm.IsAllDisplaysSelected);
        Assert.True(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().SelectedMonitorTarget == "Pantalla 1, Pantalla 3");

        vm.ApplyProfileById("natural");
        Assert.Equal(["natural"], monitor.Applied);
    }

    [Fact]
    public void Displays_AllButtonSelectsEverythingAndIgnoresSwitchingOff()
    {
        var (vm, _, _, dir) = Create(displayCount: 3);
        using var _ = dir;
        vm.DisplayButtons[0].IsSelected = false;
        vm.DisplayButtons[2].IsSelected = false;
        Assert.Equal("Pantalla 2", vm.SelectedMonitorTarget);

        vm.IsAllDisplaysSelected = true;
        Assert.Equal("Ambas pantallas", vm.SelectedMonitorTarget);
        Assert.All(vm.DisplayButtons, button => Assert.True(button.IsSelected));

        vm.IsAllDisplaysSelected = false;
        Assert.True(vm.IsAllDisplaysSelected);
    }

    [Fact]
    public void Displays_LastSelectedDisplayCannotBeTurnedOff()
    {
        var (vm, _, _, dir) = Create(displayCount: 3);
        using var _ = dir;
        vm.DisplayButtons[0].IsSelected = false;
        vm.DisplayButtons[1].IsSelected = false;

        vm.DisplayButtons[2].IsSelected = false;

        Assert.True(vm.DisplayButtons[2].IsSelected);
        Assert.Equal("Pantalla 3", vm.SelectedMonitorTarget);
    }

    [Fact]
    public void Displays_SelectingEveryButtonNormalizesToAll()
    {
        var (vm, _, _, dir) = Create(displayCount: 3);
        using var _ = dir;
        vm.DisplayButtons[1].IsSelected = false;

        vm.DisplayButtons[1].IsSelected = true;

        Assert.Equal("Ambas pantallas", vm.SelectedMonitorTarget);
        Assert.True(vm.IsAllDisplaysSelected);
    }

    [Fact]
    public void Displays_SelectionSurvivesRestartAndHotPlug()
    {
        var dir = new TempDirectory();
        using var _ = dir;
        var monitor = new FakeMonitorService { Displays = Enumerable.Range(1, 3).Select(Display).ToList() };
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(), workRunner: new InlineWorkRunner());
        vm.DisplayButtons[1].IsSelected = false;

        var restarted = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(), workRunner: new InlineWorkRunner());
        Assert.Equal("Pantalla 1, Pantalla 3", restarted.SelectedMonitorTarget);

        monitor.Displays = [Display(1), Display(2)];
        restarted.OnDisplaysChanged();
        Assert.Equal(2, restarted.DisplayButtons.Count);
        Assert.Equal("Pantalla 1", restarted.SelectedMonitorTarget);

        monitor.Displays = [Display(2)];
        restarted.OnDisplaysChanged();
        Assert.False(restarted.HasMultipleDisplays);
        Assert.Equal("Ambas pantallas", restarted.SelectedMonitorTarget);
    }

    [Fact]
    public void IdentifyCommand_ShowsTheCurrentDisplays()
    {
        var (vm, _, shell, dir) = Create(displayCount: 3);
        using var _ = dir;

        vm.IdentifyDisplaysCommand.Execute(null);

        Assert.Equal([1, 2, 3], shell.Identified!.Select(display => display.Number));
    }

    [Fact]
    public void Search_IgnoresAccentsCaseAndWordOrder()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.SearchText = "DIURNA fotografia";

        var match = Assert.Single(vm.VisibleProfiles);
        Assert.Equal("faithful-photo", match.Id);
        Assert.Equal("fotografia", MainViewModel.Normalize("Fotografía"));
    }

    [Fact]
    public void Search_NoMatchShowsEmptyStateAndClearRestores()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.SearchText = "zzzz-nothing";

        Assert.True(vm.HasNoResults);
        Assert.Equal("0 de 50 perfiles", vm.ProfileCountSubtitle);
        vm.ClearFiltersCommand.Execute(null);
        Assert.False(vm.HasNoResults);
        Assert.Equal(vm.Profiles.Count, vm.VisibleProfiles.Count);
        Assert.Equal(string.Empty, vm.SearchText);
    }

    [Fact]
    public void CategoryChips_CombineSeveralCategories()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        int Count(string category) => vm.Profiles.Count(profile => profile.Category == category);

        vm.CategoryChips.First(chip => chip.Value == "Gamer").IsSelected = true;
        vm.CategoryChips.First(chip => chip.Value == "HDR").IsSelected = true;

        Assert.Equal(Count("Gamer") + Count("HDR"), vm.VisibleProfiles.Count);
        Assert.Equal(string.Empty, vm.SidebarCategory);
        Assert.Equal(1, vm.ActiveFilterCount);
    }

    [Fact]
    public void SidebarCategory_ReplacesTheMixAndHighlightsOneEntry()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.Equal("Todos", vm.SidebarCategory);
        vm.CategoryChips[0].IsSelected = true;
        vm.CategoryChips[1].IsSelected = true;

        vm.SelectCategoryCommand.Execute("Gamer");

        Assert.Equal("Gamer", vm.SidebarCategory);
        Assert.All(vm.VisibleProfiles, profile => Assert.Equal("Gamer", profile.Category));
        Assert.Single(vm.CategoryChips, chip => chip.IsSelected);

        vm.SelectCategoryCommand.Execute("Todos");
        Assert.Equal(vm.Profiles.Count, vm.VisibleProfiles.Count);
        Assert.DoesNotContain(vm.CategoryChips, chip => chip.IsSelected);
    }

    [Fact]
    public void StateFilters_HdrHighPerformanceFavoritesAndModified()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.HdrOnly = true;
        Assert.NotEmpty(vm.VisibleProfiles);
        Assert.All(vm.VisibleProfiles, profile => Assert.True(profile.IsHdr));

        vm.HdrOnly = false;
        vm.HighPerformanceOnly = true;
        Assert.All(vm.VisibleProfiles, profile => Assert.Equal("HighPerformance", profile.PowerPlan));

        vm.HighPerformanceOnly = false;
        vm.ModifiedOnly = true;
        Assert.Empty(vm.VisibleProfiles);
        vm.Profiles[4].Brightness = 3;
        vm.ModifiedOnly = false;
        vm.ModifiedOnly = true;
        Assert.Equal([vm.Profiles[4].Id], vm.VisibleProfiles.Select(profile => profile.Id));

        vm.ModifiedOnly = false;
        vm.ToggleFavoriteCommand.Execute(vm.Profiles[7]);
        vm.FavoritesOnly = true;
        Assert.Equal([vm.Profiles[7].Id], vm.VisibleProfiles.Select(profile => profile.Id));
        Assert.Equal("Favoritos", vm.SidebarCategory);
    }

    [Fact]
    public void TemperatureChips_FilterByColorTemperature()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.TemperatureChips.First(chip => chip.Value == "Neutro 6500 K").IsSelected = true;

        Assert.NotEmpty(vm.VisibleProfiles);
        Assert.All(vm.VisibleProfiles, profile => Assert.Equal("Neutro 6500 K", profile.ColorTemperature));
    }

    [Fact]
    public void Ranges_FilterAndNeverCross()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.BrightnessRange.Min = 85;
        Assert.All(vm.VisibleProfiles, profile => Assert.InRange(profile.Brightness, 85, 100));
        Assert.True(vm.BrightnessRange.IsActive);

        vm.BrightnessRange.Max = 10;
        Assert.Equal(85, vm.BrightnessRange.Max);
        vm.BrightnessRange.Min = 500;
        Assert.Equal(85, vm.BrightnessRange.Min);
        Assert.Equal("85–85", vm.BrightnessRange.Label);
        Assert.Equal(1, vm.ActiveFilterCount);
    }

    [Theory]
    [InlineData("NameAsc")]
    [InlineData("NameDesc")]
    [InlineData("BrightnessDesc")]
    [InlineData("BrightnessAsc")]
    [InlineData("ContrastDesc")]
    [InlineData("SaturationDesc")]
    [InlineData("Category")]
    public void Sorting_OrdersTheVisibleProfiles(string mode)
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.SortMode = mode;

        var items = vm.VisibleProfiles.ToList();
        Assert.Equal(vm.Profiles.Count, items.Count);
        var ordered = mode switch
        {
            "NameAsc" => items.OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "NameDesc" => items.OrderByDescending(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "BrightnessDesc" => items.OrderByDescending(profile => profile.Brightness),
            "BrightnessAsc" => items.OrderBy(profile => profile.Brightness),
            "ContrastDesc" => items.OrderByDescending(profile => profile.Contrast),
            "SaturationDesc" => items.OrderByDescending(profile => profile.Saturation),
            _ => items.OrderBy(profile => profile.DisplayCategory, StringComparer.CurrentCultureIgnoreCase)
        };
        Assert.Equal(ordered.Select(profile => profile.Id), items.Select(profile => profile.Id));
        vm.SortMode = "Bogus";
        Assert.Equal(mode, vm.SortMode);
    }

    [Fact]
    public void ClearFilters_ResetsEveryFilterAndSort()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.SearchText = "gamer";
        vm.HdrOnly = true;
        vm.SortMode = "NameDesc";
        vm.ContrastRange.Min = 50;
        vm.TemperatureChips[0].IsSelected = true;
        vm.CategoryChips[0].IsSelected = true;
        Assert.True(vm.HasActiveFilters);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(0, vm.ActiveFilterCount);
        Assert.False(vm.IsFiltering);
        Assert.Equal("Default", vm.SortMode);
        Assert.False(vm.ContrastRange.IsActive);
        Assert.DoesNotContain(vm.CategoryChips.Concat(vm.TemperatureChips), chip => chip.IsSelected);
        Assert.Equal(vm.Profiles.Count, vm.VisibleProfiles.Count);
    }

    private static MainViewModel CreateWithFonts(TempDirectory dir, params string[] fonts) =>
        new(new FakeMonitorService { Displays = [Display(1)] }, new ProfileStore(dir.Path),
            new ApplicationSettingsStore(dir.Path, manageStartup: false), new FakeShell(),
            workRunner: new InlineWorkRunner(), availableFonts: fonts);

    [Fact]
    public void Fonts_OneChoicePerInstalledFontAndSelectionPersists()
    {
        using var dir = new TempDirectory();
        var vm = CreateWithFonts(dir, "Segoe UI Variable Text", "Georgia", "Consolas");
        Assert.Equal(["Segoe UI Variable Text", "Georgia", "Consolas"], vm.FontChoices.Select(choice => choice.Name));
        Assert.Equal("Segoe UI Variable Text", vm.FontFamilyName);

        vm.FontChoices.First(choice => choice.Name == "Georgia").IsSelected = true;

        Assert.Equal("Georgia", vm.FontFamilyName);
        Assert.Single(vm.FontChoices, choice => choice.IsSelected);
        Assert.Equal("Georgia", new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().FontFamilyName);
        vm.FontFamilyName = "Not Installed";
        Assert.Equal("Georgia", vm.FontFamilyName);
    }

    [Fact]
    public void Fonts_FallBackWhenTheSavedFontIsNoLongerInstalled()
    {
        using var dir = new TempDirectory();
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);
        var settings = store.Load();
        settings.FontFamilyName = "Removed Font";
        store.Save(settings);

        var vm = CreateWithFonts(dir, "Arial", "Georgia");

        Assert.Equal("Arial", vm.FontFamilyName);
        Assert.Single(vm.FontChoices, choice => choice.IsSelected && choice.Name == "Arial");
    }

    [Fact]
    public void UiScale_PreviewOnlyTakesEffectWhenCommitted()
    {
        using var dir = new TempDirectory();
        var vm = CreateWithFonts(dir, "Arial");
        Assert.Equal(100, vm.UiScalePercent);

        vm.UiScalePreview = 123;

        Assert.Equal(125, vm.UiScalePreview);
        Assert.Equal("125%", vm.UiScalePreviewLabel);
        Assert.Equal(100, vm.UiScalePercent);
        Assert.Equal(1.0, vm.UiScaleFactor);

        vm.CommitUiScale();

        Assert.Equal(125, vm.UiScalePercent);
        Assert.Equal(1.25, vm.UiScaleFactor);
        Assert.Equal(125, new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().UiScalePercent);
    }

    [Fact]
    public void UiScale_IsClampedToTheSupportedRange()
    {
        using var dir = new TempDirectory();
        var vm = CreateWithFonts(dir, "Arial");

        vm.UiScalePreview = 500;
        vm.CommitUiScale();
        Assert.Equal(MainViewModel.MaxUiScale, vm.UiScalePercent);

        vm.UiScalePreview = 1;
        vm.CommitUiScale();
        Assert.Equal(MainViewModel.MinUiScale, vm.UiScalePercent);
    }

    [Fact]
    public void ResetAppearance_AlsoRestoresFontAndScale()
    {
        using var dir = new TempDirectory();
        var vm = CreateWithFonts(dir, "Segoe UI Variable Text", "Georgia");
        vm.FontFamilyName = "Georgia";
        vm.UiScalePreview = 140;
        vm.CommitUiScale();

        vm.SettingsSection = "Appearance";
        vm.ResetSectionCommand.Execute(null);

        Assert.Equal("Segoe UI Variable Text", vm.FontFamilyName);
        Assert.Equal(100, vm.UiScalePercent);
        Assert.Equal(100, vm.UiScalePreview);
    }

    [Fact]
    public void MonitorControls_SwitchPersistsAndReachesTheService()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        Assert.True(vm.UseMonitorControls);
        Assert.True(monitor.UseMonitorControls);

        vm.UseMonitorControls = false;

        Assert.False(monitor.UseMonitorControls);
        Assert.False(new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().UseMonitorControls);

        vm.SettingsSection = "General";
        vm.ResetSectionCommand.Execute(null);
        Assert.True(vm.UseMonitorControls);
        Assert.True(monitor.UseMonitorControls);
    }

    [Fact]
    public void SavedMonitorControlsSetting_IsAppliedWhenTheAppStarts()
    {
        using var dir = new TempDirectory();
        var store = new ApplicationSettingsStore(dir.Path, manageStartup: false);
        var settings = store.Load();
        settings.UseMonitorControls = false;
        store.Save(settings);
        var monitor = new FakeMonitorService { Displays = [Display(1)] };

        _ = new MainViewModel(monitor, new ProfileStore(dir.Path), store, new FakeShell(), workRunner: new InlineWorkRunner());

        Assert.False(monitor.UseMonitorControls);
    }

    [Fact]
    public void ServiceReceivesTheOriginalStatesAsTheyAreCaptured()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        monitor.Capturable.Add(new OriginalMonitorState { MonitorId = "MONITOR\\TEST\\1" });
        Assert.Empty(monitor.KnownOriginals);

        vm.ApplyProfileById("natural");

        Assert.Equal(["MONITOR\\TEST\\1"], monitor.KnownOriginals.Select(state => state.MonitorId));
        // captured once: applying again must not duplicate it
        vm.ApplyProfileById("eyes-night");
        Assert.Single(monitor.KnownOriginals);
    }

    [Fact]
    public void SidebarWidth_DefaultsAndIsClampedToItsRange()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        Assert.Equal(MainViewModel.DefaultSidebarWidth, vm.SidebarWidth);

        vm.ResizeSidebar(+1000);
        Assert.Equal(MainViewModel.MaxSidebarWidth, vm.SidebarWidth);

        vm.ResizeSidebar(-1000);
        Assert.Equal(MainViewModel.MinSidebarWidth, vm.SidebarWidth);
    }

    [Fact]
    public void SidebarWidth_RespectsTheRoomLeftByTheWindow()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;

        vm.ResizeSidebar(+300, maxAvailable: 260);
        Assert.Equal(260, vm.SidebarWidth);

        // a window with no room shrinks the menu to its minimum, never below it
        vm.ResizeSidebar(+50, maxAvailable: 10);
        Assert.Equal(MainViewModel.MinSidebarWidth, vm.SidebarWidth);
    }

    [Fact]
    public void SidebarWidth_IsOnlySavedWhenTheDragIsCommitted()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SidebarWidth)) raised++; };

        vm.ResizeSidebar(+30);
        vm.ResizeSidebar(+30);

        Assert.Equal(2, raised);
        Assert.Equal(280, vm.SidebarWidth);
        Assert.Equal(MainViewModel.DefaultSidebarWidth, new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().SidebarWidth);

        vm.CommitSidebarWidth();

        Assert.Equal(280, new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().SidebarWidth);
    }

    [Fact]
    public void SidebarWidth_ResetRestoresTheDefaultAndSavesIt()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.ResizeSidebar(+100);
        vm.CommitSidebarWidth();

        vm.ResetSidebarWidth();

        Assert.Equal(MainViewModel.DefaultSidebarWidth, vm.SidebarWidth);
        Assert.Equal(MainViewModel.DefaultSidebarWidth, new ApplicationSettingsStore(dir.Path, manageStartup: false).Load().SidebarWidth);
    }

    [Fact]
    public void SidebarWidth_LayoutResetAlsoRestoresIt()
    {
        var (vm, _, _, dir) = Create();
        using var _ = dir;
        vm.ResizeSidebar(+80);

        vm.SettingsSection = "Layout";
        vm.ResetSectionCommand.Execute(null);

        Assert.Equal(MainViewModel.DefaultSidebarWidth, vm.SidebarWidth);
    }

    [Fact]
    public void OpenUrlCommand_DelegatesToShell()
    {
        var (vm, _, shell, dir) = Create();
        using var _ = dir;

        vm.OpenUrlCommand.Execute("https://example.com");

        Assert.Equal(["https://example.com"], shell.Opened);
    }

    [Theory]
    [InlineData("b", 1, "c")]
    [InlineData("c", 1, "a")]
    [InlineData("a", -1, "c")]
    [InlineData(null, 1, "a")]
    [InlineData(null, -1, "c")]
    public void Cycle_WrapsAroundThePool(string? current, int direction, string expected)
    {
        var pool = new[] { "a", "b", "c" }
            .Select(id => new DisplayProfile { Id = id, Name = id, Category = id, Description = id, PreviewStart = "#000", PreviewEnd = "#000" })
            .ToList();

        Assert.Equal(expected, MainViewModel.Cycle(pool, current, direction)!.Id);
    }

    [Fact]
    public void Cycle_EmptyPoolReturnsNull() =>
        Assert.Null(MainViewModel.Cycle([], "a", 1));
}
