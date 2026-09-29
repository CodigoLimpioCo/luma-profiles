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

    [Theory]
    [InlineData("Cálido 5000 K", 4u)]
    [InlineData("Neutro 6500 K", 5u)]
    [InlineData("Frío 7500 K", 6u)]
    [InlineData("Usuario (RGB)", 11u)]
    [InlineData("desconocido", 11u)]
    public void ColorPreset_MapsTemperatureToVcpValue(string name, uint expected) =>
        Assert.Equal(expected, MonitorService.ColorPreset(name));

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
