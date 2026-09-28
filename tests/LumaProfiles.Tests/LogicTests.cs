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

        public ApplyResult Apply(DisplayProfile profile, string target)
        {
            Applied.Add(profile.Id);
            return Result();
        }

        public ApplyResult Preview(DisplayProfile profile, string target) => Result();
        public IReadOnlyList<OriginalMonitorState> CaptureOriginalStates(IEnumerable<string> knownMonitorIds) => [];
        public string? GetActivePowerPlan() => null;
        public ApplyResult RestoreOriginal(IEnumerable<OriginalMonitorState> s, string t, string? p) => Result();
        public ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections) => Result();

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
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public List<string> Opened { get; } = [];
        public void OpenUrl(string url) => Opened.Add(url);
        public string? PickSaveFile(string title, string suggestedFileName) => SavePath;
        public string? PickOpenFile(string title) => OpenPath;
    }

    private static (MainViewModel Vm, FakeMonitorService Monitor, FakeShell Shell, TempDirectory Dir) Create()
    {
        var dir = new TempDirectory();
        var monitor = new FakeMonitorService();
        var shell = new FakeShell();
        var vm = new MainViewModel(monitor, new ProfileStore(dir.Path), new ApplicationSettingsStore(dir.Path, manageStartup: false), shell);
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
    public void RepairCommand_NeutralizesAndClearsActiveProfile()
    {
        var (vm, monitor, _, dir) = Create();
        using var _ = dir;
        vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

        vm.RepairCommand.Execute(null);

        Assert.Equal(1, monitor.NeutralCalls);
        Assert.DoesNotContain(vm.Profiles, p => p.IsActive);
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

        Assert.Equal(1, monitor.NeutralCalls);
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
