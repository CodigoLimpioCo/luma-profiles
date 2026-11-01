using System.Text.Json;
using LumaProfiles.Models;
using LumaProfiles.Services;
using LumaProfiles.ViewModels;

namespace LumaProfiles.Tests;

/// <summary>0.10.0: updates, battery, game mode, per-display profiles, HDR, undo/redo, activities and recents.</summary>
public class FeatureTests
{
    private sealed class RecordingMonitor : IMonitorService
    {
        public List<(string ProfileId, string Target)> Applied { get; } = [];
        public List<DisplayInfo> Displays { get; set; } = [];
        public bool UseMonitorControls { get; set; } = true;
        public bool HdrSafeGamma { get; set; } = true;

        public ApplyResult Apply(DisplayProfile profile, string target)
        {
            Applied.Add((profile.Id, target));
            var hits = Displays.Count == 0
                ? [new AppliedMonitor("mon-1", @"\\.\DISPLAY1")]
                : Displays.Where(display => MonitorService.MatchesTarget(display.DeviceName, target))
                    .Select(display => new AppliedMonitor(display.MonitorId, display.DeviceName)).ToList();
            return new ApplyResult(hits.Count, [], hits);
        }

        public ApplyResult Preview(DisplayProfile profile, string target) => new(1, [], []);
        public IReadOnlyList<OriginalMonitorState> CaptureOriginalStates(IEnumerable<string> knownMonitorIds) => [];
        public string? GetActivePowerPlan() => null;
        public ApplyResult RestoreOriginal(IEnumerable<OriginalMonitorState> s, string t, string? p) => new(1, [], [new AppliedMonitor("mon-1", @"\\.\DISPLAY1")]);
        public ApplyResult Reapply(IEnumerable<MonitorColorCorrection> corrections, CancellationToken cancellationToken = default) => new(0, [], []);
        public IReadOnlyList<OriginalMonitorState> CaptureCurrentStates() => [];
        public ApplyResult RestoreNeutral(string target) => new(1, [], [new AppliedMonitor("mon-1", @"\\.\DISPLAY1")]);
        public IReadOnlyList<DisplayInfo> GetDisplays() => Displays;
        public void UseOriginalStates(IEnumerable<OriginalMonitorState> states) { }
    }

    private sealed class Shell : IShellService
    {
        public List<string> Opened { get; } = [];
        public void OpenUrl(string url) => Opened.Add(url);
        public string? PickSaveFile(string title, string suggestedFileName) => null;
        public string? PickOpenFile(string title) => null;
        public void IdentifyDisplays(IReadOnlyList<DisplayInfo> displays) { }
        public bool Confirm(string title, string message) => true;
        public CloseChoice AskCloseChoice(string title, string message) => CloseChoice.Cancel;
    }

    private sealed class FixedUpdates(UpdateInfo? latest) : IUpdateChecker
    {
        public int Calls { get; private set; }
        public Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(latest);
        }
    }

    private static DisplayInfo Display(int number, bool hdr = false) =>
        new(number, $"\\\\.\\DISPLAY{number}", $"MONITOR\\TEST\\{number}", (number - 1) * 1920, 0, 1920, 1080, number == 1, hdr);

    private static MainViewModel Create(TempDirectory dir, RecordingMonitor monitor, IUpdateChecker? updates = null, Shell? shell = null)
    {
        var settings = new ApplicationSettingsStore(dir.Path, manageStartup: false);
        var initial = settings.Load();
        initial.ConfirmChanges = false;
        settings.Save(initial);
        return new MainViewModel(monitor, new ProfileStore(dir.Path), settings, shell ?? new Shell(),
            workRunner: new InlineWorkRunner(), restorePointStore: new RestorePointStore(dir.Path), updateChecker: updates);
    }

    // ---- updates -----------------------------------------------------------------------------

    [Theory]
    [InlineData("v0.10.0", 0, 10, 0)]
    [InlineData("0.9", 0, 9, 0)]
    [InlineData("V1.2.3-beta", 1, 2, 3)]
    public void TryParseVersion_AcceptsTagsWithPrefixAndSuffix(string tag, int major, int minor, int build)
    {
        Assert.True(GitHubUpdateChecker.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Fact]
    public void Parse_IgnoresDraftsAndPrereleases()
    {
        using var stable = JsonDocument.Parse("""{"tag_name":"v1.0.0","html_url":"https://x/r","draft":false,"prerelease":false}""");
        using var pre = JsonDocument.Parse("""{"tag_name":"v2.0.0","prerelease":true}""");
        Assert.Equal(new Version(1, 0, 0), GitHubUpdateChecker.Parse(stable.RootElement)!.Version);
        Assert.Equal("https://x/r", GitHubUpdateChecker.Parse(stable.RootElement)!.Url);
        Assert.Null(GitHubUpdateChecker.Parse(pre.RootElement));
    }

    [Fact]
    public async Task NewerRelease_IsAnnouncedAndCanBeSkipped()
    {
        using var dir = new TempDirectory();
        var shell = new Shell();
        var vm = Create(dir, new RecordingMonitor(), new FixedUpdates(new UpdateInfo(new Version(99, 0, 0), "https://example/r")), shell);

        await vm.CheckForUpdatesAsync(manual: false);
        Assert.True(vm.IsUpdateAvailable);
        Assert.Contains("99.0.0", vm.UpdateBadgeText);

        vm.OpenUpdateCommand.Execute(null);
        Assert.Equal("https://example/r", shell.Opened.Single());

        vm.SkipUpdateCommand.Execute(null);
        Assert.False(vm.IsUpdateAvailable);
        await vm.CheckForUpdatesAsync(manual: false);
        Assert.False(vm.IsUpdateAvailable);
    }

    [Fact]
    public async Task OlderOrSameRelease_IsNotAnnounced()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor(), new FixedUpdates(new UpdateInfo(new Version(0, 1, 0), "u")));
        await vm.CheckForUpdatesAsync(manual: true);
        Assert.False(vm.IsUpdateAvailable);
    }

    [Fact]
    public void WithoutChecker_NothingIsRequested()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        Assert.False(vm.CanCheckForUpdates);
    }

    // ---- game mode ---------------------------------------------------------------------------

    [Fact]
    public void RuleEngine_UsesFullscreenRuleOnlyWithoutAnExplicitRule()
    {
        var engine = new AppRuleEngine("LumaProfiles");
        var explicitRule = new AppProfileRule { ProcessName = "game", ProfileId = "a" };
        var fullscreen = new AppProfileRule { ProcessName = "(fullscreen)", ProfileId = "b" };

        Assert.Same(explicitRule, engine.Evaluate("game", [explicitRule], isFullscreen: true, fullscreen).Rule);
        Assert.Same(fullscreen, engine.Evaluate("other", [explicitRule], isFullscreen: true, fullscreen).Rule);
        Assert.Equal(RuleAction.Restore, engine.Evaluate("other", [explicitRule], isFullscreen: false, fullscreen).Action);
        Assert.Equal(RuleAction.None, engine.Evaluate("other", [explicitRule], isFullscreen: true, fullscreenRule: null).Action);
    }

    [Fact]
    public void GameMode_AppliesWhileFullscreenAndRestoresAfter()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor();
        var vm = Create(dir, monitor);
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(profile => profile.Id == "natural"));
        vm.GameModeProfileId = "game-fighting";
        vm.GameModeEnabled = true;

        vm.OnForegroundProcessChanged("somegame", isFullscreen: false);
        Assert.Equal("natural", monitor.Applied[^1].ProfileId);

        vm.OnForegroundProcessChanged("somegame", isFullscreen: true);
        Assert.Equal("game-fighting", monitor.Applied[^1].ProfileId);

        vm.OnForegroundProcessChanged("explorer", isFullscreen: false);
        Assert.Equal("natural", monitor.Applied[^1].ProfileId);
    }

    // ---- battery -----------------------------------------------------------------------------

    [Fact]
    public void Battery_AppliesWhenUnpluggedAndRestoresWhenPluggedIn()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor();
        var vm = Create(dir, monitor);
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(profile => profile.Id == "cinema"));
        vm.BatteryProfileEnabled = true;
        Assert.Equal("performance-battery", vm.BatteryProfileId);

        vm.OnPowerSourceChanged(onBattery: true);
        Assert.Equal("performance-battery", monitor.Applied[^1].ProfileId);

        vm.OnPowerSourceChanged(onBattery: false);
        Assert.Equal("cinema", monitor.Applied[^1].ProfileId);
    }

    [Fact]
    public void Battery_LeavesAManualChoiceAloneWhenPluggedIn()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor();
        var vm = Create(dir, monitor);
        vm.BatteryProfileEnabled = true;
        vm.OnPowerSourceChanged(onBattery: true);
        vm.ApplyProfileCommand.Execute(vm.Profiles.First(profile => profile.Id == "reference"));
        var count = monitor.Applied.Count;

        vm.OnPowerSourceChanged(onBattery: false);
        Assert.Equal(count, monitor.Applied.Count);
    }

    [Fact]
    public void Battery_DisabledDoesNothing()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor();
        var vm = Create(dir, monitor);
        vm.OnPowerSourceChanged(onBattery: true);
        Assert.Empty(monitor.Applied);
    }

    // ---- one profile per display ------------------------------------------------------------

    [Fact]
    public void EachDisplayKeepsItsOwnProfile()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor { Displays = [Display(1), Display(2)] };
        var vm = Create(dir, monitor);
        vm.RefreshDisplays();
        var natural = vm.Profiles.First(profile => profile.Id == "natural");
        var fighting = vm.Profiles.First(profile => profile.Id == "game-fighting");

        vm.SelectedMonitorTarget = "Pantalla 1";
        vm.ApplyProfileCommand.Execute(natural);
        vm.SelectedMonitorTarget = "Pantalla 2";
        vm.ApplyProfileCommand.Execute(fighting);

        Assert.True(natural.IsActive);
        Assert.True(fighting.IsActive);
        Assert.Contains("1: " + natural.DisplayName, vm.ActiveByDisplaySummary);
        Assert.Contains("2: " + fighting.DisplayName, vm.ActiveByDisplaySummary);
        Assert.Contains(fighting.DisplayName, vm.DisplayButtons[1].Tooltip);
    }

    [Fact]
    public void RuleWithItsOwnDisplays_IgnoresTheSelector()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor { Displays = [Display(1), Display(2)] };
        var vm = Create(dir, monitor);
        vm.RefreshDisplays();
        vm.SelectedMonitorTarget = "Pantalla 1";

        vm.NewRuleProcess = "game";
        vm.NewRuleProfileId = "game-retro";
        vm.NewRuleTarget = "Pantalla 2";
        vm.AddAppRuleCommand.Execute(null);
        Assert.Contains(vm.AutomationTargetOptions, option => option.Value == "Pantalla 2");
        Assert.Contains("2", vm.AppRules.Single().TargetLabel);

        vm.OnForegroundProcessChanged("game", isFullscreen: false);
        Assert.Equal(("game-retro", "Pantalla 2"), monitor.Applied[^1]);
    }

    [Fact]
    public void HdrDisplay_IsReportedAfterApplying()
    {
        using var dir = new TempDirectory();
        var monitor = new RecordingMonitor { Displays = [Display(1, hdr: true)] };
        var vm = Create(dir, monitor);
        vm.RefreshDisplays();
        Assert.True(vm.AnyDisplayHdr);
        Assert.True(monitor.HdrSafeGamma);

        vm.ApplyProfileCommand.Execute(vm.Profiles.First(profile => profile.Id == "natural"));
        Assert.Contains("HDR", vm.StatusMessage);

        vm.HdrSafeGamma = false;
        Assert.False(monitor.HdrSafeGamma);
    }

    // ---- undo / redo -------------------------------------------------------------------------

    [Fact]
    public void UndoAndRedo_WalkThroughAdjustmentSteps()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        var profile = vm.SelectedProfile;
        var original = profile.Brightness;
        Assert.False(vm.CanUndoAdjustment);

        profile.Brightness = 10;
        profile.Brightness = 12; // same burst (a slider drag): one step
        vm.CloseHistoryStep();
        profile.Contrast = 33;
        vm.CloseHistoryStep();

        vm.UndoAdjustmentCommand.Execute(null);
        Assert.Equal(12, profile.Brightness);
        Assert.NotEqual(33, profile.Contrast);

        vm.UndoAdjustmentCommand.Execute(null);
        Assert.Equal(original, profile.Brightness);
        Assert.False(vm.CanUndoAdjustment);

        vm.RedoAdjustmentCommand.Execute(null);
        vm.RedoAdjustmentCommand.Execute(null);
        Assert.Equal(12, profile.Brightness);
        Assert.Equal(33, profile.Contrast);
        Assert.False(vm.CanRedoAdjustment);
    }

    [Fact]
    public void History_StartsOverForAnotherProfile()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        vm.SelectedProfile.Brightness = 5;
        vm.SelectedProfile = vm.Profiles[1];
        Assert.False(vm.CanUndoAdjustment);
    }

    // ---- activities and recents -------------------------------------------------------------

    [Fact]
    public void EveryActivityProfileExists()
    {
        using var dir = new TempDirectory();
        var ids = new ProfileStore(dir.Path).Defaults.Select(profile => profile.Id).ToHashSet();
        foreach (var intent in ProfileIntents.Keys.Where(key => key != ProfileIntents.All))
        {
            Assert.Contains(ProfileIntents.Suggested(intent)!, ids);
        }

        var field = typeof(ProfileIntents).GetField("IdsByIntent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var map = (Dictionary<string, string[]>)field.GetValue(null)!;
        var unknown = map.Values.SelectMany(list => list).Where(id => !ids.Contains(id)).ToList();
        Assert.True(unknown.Count == 0, "Unknown ids: " + string.Join(", ", unknown));
    }

    [Fact]
    public void EveryDefaultProfileBelongsToSomeActivity()
    {
        using var dir = new TempDirectory();
        var orphans = new ProfileStore(dir.Path).Defaults
            .Where(profile => !ProfileIntents.Keys.Skip(1).Any(key => ProfileIntents.Matches(key, profile)))
            .Select(profile => profile.Id)
            .ToList();
        Assert.True(orphans.Count == 0, "Without activity: " + string.Join(", ", orphans));
    }

    [Fact]
    public void SelectingAnActivity_FiltersAndPutsTheSuggestionFirst()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        vm.SelectIntentCommand.Execute("Night");

        Assert.Equal("Night", vm.SelectedIntent);
        Assert.Equal("eyes-night", vm.VisibleProfiles[0].Id);
        Assert.All(vm.VisibleProfiles, profile => Assert.True(ProfileIntents.Matches("Night", profile)));
        Assert.True(vm.IntentChoices.Single(choice => choice.Key == "Night").IsSelected);
        Assert.Contains("77", vm.ProfileCountSubtitle);

        vm.SelectIntentCommand.Execute("Night"); // a second click goes back to everything
        Assert.Equal(ProfileIntents.All, vm.SelectedIntent);
        Assert.Equal(vm.Profiles.Count, vm.VisibleProfiles.Count);
    }

    [Fact]
    public void ClearFilters_ResetsTheActivity()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        vm.SelectedIntent = "Play";
        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(ProfileIntents.All, vm.SelectedIntent);
        Assert.Equal(vm.Profiles.Count, vm.VisibleProfiles.Count);
    }

    [Fact]
    public void AppliedProfiles_AppearAsRecentNewestFirstAndCapped()
    {
        using var dir = new TempDirectory();
        var vm = Create(dir, new RecordingMonitor());
        Assert.False(vm.HasRecentProfiles);

        foreach (var profile in vm.Profiles.Take(8)) vm.ApplyProfileCommand.Execute(profile);
        vm.ApplyProfileCommand.Execute(vm.Profiles[3]);

        Assert.Equal(6, vm.RecentProfiles.Count);
        Assert.Equal(vm.Profiles[3].Id, vm.RecentProfiles[0].Id);
        Assert.Equal(vm.Profiles[7].Id, vm.RecentProfiles[1].Id);
        Assert.Single(vm.RecentProfiles, profile => profile.Id == vm.Profiles[3].Id);
    }

    [Fact]
    public void FullscreenGeometry_RequiresCoveringTheWholeDisplay()
    {
        var monitor = new FullscreenDetector.NativeMethods.Rect(0, 0, 1920, 1080);
        Assert.True(FullscreenDetector.Covers(new FullscreenDetector.NativeMethods.Rect(0, 0, 1920, 1080), monitor));
        Assert.True(FullscreenDetector.Covers(new FullscreenDetector.NativeMethods.Rect(-8, -8, 1928, 1088), monitor));
        Assert.False(FullscreenDetector.Covers(new FullscreenDetector.NativeMethods.Rect(0, 0, 1920, 1040), monitor));
    }

    [Fact]
    public void AutomationBackup_CarriesBatteryAndGameMode()
    {
        using var dir = new TempDirectory();
        var store = new ProfileStore(dir.Path);
        var path = Path.Combine(dir.Path, "export.json");
        store.Export(path, store.Load(), new AutomationBackup
        {
            BatteryProfileEnabled = true, BatteryProfileId = "performance-battery",
            GameModeEnabled = true, GameModeProfileId = "game-strategy",
        });

        var read = store.ReadAutomation(path)!;
        Assert.True(read.BatteryProfileEnabled);
        Assert.Equal("game-strategy", read.GameModeProfileId);
    }
}
