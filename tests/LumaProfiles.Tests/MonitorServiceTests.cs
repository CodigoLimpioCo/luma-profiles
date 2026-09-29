using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class MonitorServiceTests
{
    [Theory]
    [InlineData("Ambas pantallas", @"\\.\DISPLAY1", true)]
    [InlineData("Ambas pantallas", @"\\.\DISPLAY2", true)]
    [InlineData("Pantalla 1", @"\\.\DISPLAY1", true)]
    [InlineData("Pantalla 1", @"\\.\DISPLAY2", false)]
    [InlineData("Pantalla 2", @"\\.\display2", true)]
    [InlineData("Pantalla 2", @"\\.\DISPLAY1", false)]
    [InlineData("Pantalla 1, Pantalla 3", @"\\.\DISPLAY1", true)]
    [InlineData("Pantalla 1, Pantalla 3", @"\\.\DISPLAY3", true)]
    [InlineData("Pantalla 1, Pantalla 3", @"\\.\DISPLAY2", false)]
    [InlineData("Pantalla 1", @"\\.\DISPLAY11", false)]
    [InlineData("Pantalla 11", @"\\.\DISPLAY11", true)]
    public void MatchesTarget_SelectsExpectedDisplays(string target, string device, bool expected) =>
        Assert.Equal(expected, MonitorService.MatchesTarget(device, target));

    private static readonly Dictionary<byte, VcpFeature> FullSupport = new()
    {
        [0x10] = new(14, 100), [0x12] = new(68, 100), [0x14] = new(11, 11), [0x8A] = new(50, 100), [0x89] = new(0, 100)
    };

    private static DisplayProfile Profile(string id)
    {
        using var dir = new TempDirectory();
        return new ProfileStore(dir.Path).GetDefault(id);
    }

    [Fact]
    public void Plan_NeverTouchesGainsSharpnessOrAnythingOutsideTheManagedCodes()
    {
        using var dir = new TempDirectory();
        var allowed = new byte[] { 0x10, 0x12, 0x14, 0x8A, 0x89 };
        var originals = new Dictionary<byte, uint> { [0x14] = 5, [0x8A] = 30, [0x89] = 10 };

        foreach (var profile in new ProfileStore(dir.Path).Defaults)
        {
            var codes = MonitorService.PlanDdcWrites(profile, applyImageControls: true, FullSupport, originals).Select(write => write.Code);

            Assert.All(codes, code => Assert.Contains(code, allowed));
        }
    }

    [Fact]
    public void Plan_ScalesBrightnessAndContrastToTheMonitorsOwnRange()
    {
        var profile = Profile("natural"); // brightness 80, contrast 80
        var features = new Dictionary<byte, VcpFeature> { [0x10] = new(0, 255), [0x12] = new(0, 75) };

        var plan = MonitorService.PlanDdcWrites(profile, true, features, null);

        Assert.Equal(204u, plan.Single(write => write.Code == 0x10).Value);
        Assert.Equal(60u, plan.Single(write => write.Code == 0x12).Value);
    }

    [Fact]
    public void Plan_UnreadableBrightnessFallsBackToTheRawPercentage()
    {
        var plan = MonitorService.PlanDdcWrites(Profile("natural"), true, new Dictionary<byte, VcpFeature>(), null);

        Assert.Equal(80u, plan.Single(write => write.Code == 0x10).Value);
        Assert.Equal(80u, plan.Single(write => write.Code == 0x12).Value);
        Assert.DoesNotContain(plan, write => write.Code is 0x14 or 0x8A or 0x89);
    }

    [Fact]
    public void Plan_SkipsImageControlsWhenNotRequestedAndValuesAlreadyEqual()
    {
        var profile = Profile("natural");
        var current = new Dictionary<byte, VcpFeature> { [0x10] = new(80, 100), [0x12] = new(80, 100) };

        Assert.Empty(MonitorService.PlanDdcWrites(profile, true, current, null));
        Assert.Empty(MonitorService.PlanDdcWrites(profile, false, FullSupport, null));
    }

    [Fact]
    public void Plan_OnlyWarmAndCoolChangeThePresetAndNeutralPutsTheOriginalBack()
    {
        var originals = new Dictionary<byte, uint> { [0x14] = 5 };
        var warm = Profile("natural");
        warm.ColorTemperature = "Cálido 5000 K";
        var cool = Profile("natural");
        cool.ColorTemperature = "Frío 7500 K";
        var neutral = Profile("natural");
        neutral.ColorTemperature = "Neutro 6500 K";
        var user = Profile("natural");
        user.ColorTemperature = "Usuario (RGB)";

        Assert.Equal(4u, MonitorService.PlanDdcWrites(warm, false, FullSupport, originals).Single(w => w.Code == 0x14).Value);
        Assert.Equal(6u, MonitorService.PlanDdcWrites(cool, false, FullSupport, originals).Single(w => w.Code == 0x14).Value);
        Assert.Equal(5u, MonitorService.PlanDdcWrites(neutral, false, FullSupport, originals).Single(w => w.Code == 0x14).Value);
        Assert.Equal(5u, MonitorService.PlanDdcWrites(user, false, FullSupport, originals).Single(w => w.Code == 0x14).Value);
        // without a known original the preset is simply left alone
        Assert.DoesNotContain(MonitorService.PlanDdcWrites(neutral, false, FullSupport, null), w => w.Code == 0x14);
    }

    [Fact]
    public void Plan_SaturationAndHueAreScaledWhenChangedAndRestoredWhenNeutral()
    {
        var vivid = Profile("creative-vivid"); // saturation 62
        vivid.Hue = 20;
        var features = new Dictionary<byte, VcpFeature> { [0x8A] = new(50, 200), [0x89] = new(100, 200) };
        var originals = new Dictionary<byte, uint> { [0x8A] = 90, [0x89] = 100 };

        var plan = MonitorService.PlanDdcWrites(vivid, false, features, originals);

        Assert.Equal(124u, plan.Single(w => w.Code == 0x8A).Value); // 62% of 200
        Assert.Equal(140u, plan.Single(w => w.Code == 0x89).Value); // (20 + 50)% of 200

        var neutral = Profile("natural");
        var restore = MonitorService.PlanDdcWrites(neutral, false, features, originals);
        Assert.Equal(90u, restore.Single(w => w.Code == 0x8A).Value);
        Assert.DoesNotContain(restore, w => w.Code == 0x89); // already at its original value
    }

    [Theory]
    [InlineData(80, 100u, 80u)]
    [InlineData(80, 255u, 204u)]
    [InlineData(0, 255u, 0u)]
    [InlineData(150, 100u, 100u)]
    [InlineData(-5, 100u, 0u)]
    public void ScaleToRange_MapsPercentagesOntoTheReportedMaximum(int percent, uint max, uint expected) =>
        Assert.Equal(expected, MonitorService.ScaleToRange(percent, new VcpFeature(0, max)));

    [Fact]
    public void ScaleToRange_WithoutARangeUsesThePercentage() =>
        Assert.Equal(64u, MonitorService.ScaleToRange(64, null));

    [Theory]
    [InlineData("Cálido 5000 K", 4u)]
    [InlineData("Frío 7500 K", 6u)]
    public void ExplicitColorPreset_MapsWarmAndCool(string name, uint expected) =>
        Assert.Equal(expected, MonitorService.ExplicitColorPreset(name));

    [Theory]
    [InlineData("Neutro 6500 K")]
    [InlineData("Usuario (RGB)")]
    [InlineData("otro")]
    public void ExplicitColorPreset_LeavesNeutralAndUserModesAlone(string name) =>
        Assert.Null(MonitorService.ExplicitColorPreset(name));

    [Fact]
    public void BuildGammaRamp_NeutralIsLinear()
    {
        var ramp = MonitorService.BuildGammaRamp(1.0, 1.0, 1.0, 1.0);

        Assert.Equal(768, ramp.Length);
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.Equal(0, ramp[channel * 256]);
            Assert.Equal(ushort.MaxValue, ramp[(channel * 256) + 255]);
            Assert.Equal((ushort)Math.Round(128 / 255.0 * ushort.MaxValue), ramp[(channel * 256) + 128]);
        }
    }

    [Fact]
    public void BuildGammaRamp_GainsScaleEachChannelAndClamp()
    {
        var ramp = MonitorService.BuildGammaRamp(1.0, 1.0, 0.5, 2.0);

        Assert.Equal(ushort.MaxValue, ramp[255]);
        Assert.Equal((ushort)Math.Round(0.5 * ushort.MaxValue), ramp[256 + 255]);
        Assert.Equal(ushort.MaxValue, ramp[512 + 255]);
    }

    [Fact]
    public void BuildGammaRamp_HigherGammaBrightensMidtones()
    {
        var neutral = MonitorService.BuildGammaRamp(1.0, 1, 1, 1);
        var bright = MonitorService.BuildGammaRamp(1.5, 1, 1, 1);

        Assert.True(bright[128] > neutral[128]);
    }

    [Fact]
    public void CreateCorrection_ThenToProfile_PreservesAdjustments()
    {
        using var dir = new TempDirectory();
        var profile = new ProfileStore(dir.Path).GetDefault("eyes-night");

        var correction = MonitorService.CreateCorrection(profile, new AppliedMonitor("id", @"\\.\DISPLAY1"));
        var restored = correction.ToProfile();

        Assert.Equal(profile.Brightness, restored.Brightness);
        Assert.Equal(profile.Blue, restored.Blue);
        Assert.Equal(profile.Id, restored.Id);
        Assert.Equal("id", correction.MonitorId);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\display12", 12)]
    [InlineData("nonsense", 0)]
    public void DisplayNumber_ParsesTrailingNumber(string device, int expected) =>
        Assert.Equal(expected, MonitorService.DisplayNumber(device));

    [Fact]
    public void ApplyResult_SuccessRequiresDisplaysAndNoFailures()
    {
        Assert.True(new ApplyResult(1, [], []).Success);
        Assert.False(new ApplyResult(0, [], []).Success);
        Assert.False(new ApplyResult(1, ["x"], []).Success);
    }
}
