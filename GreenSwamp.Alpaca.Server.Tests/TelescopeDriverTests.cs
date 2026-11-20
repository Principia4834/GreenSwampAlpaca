using System.Reflection;
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.TelescopeDriver;
using GreenSwamp.Alpaca.Settings.Services;
using TelescopeMount = GreenSwamp.Alpaca.MountControl.Mount;

namespace GreenSwamp.Alpaca.Server.Tests;

public class TelescopeDriverTests
{
    [Fact]
    public void DriverVersion_IsMajorMinor_AndDriverInfoIncludesFullVersion()
    {
        var telescope = CreateTelescope();

        var version = telescope.DriverVersion;
        var info = telescope.DriverInfo;
        var fullVersionText = info[(info.LastIndexOf("FileVersion=", StringComparison.Ordinal) + "FileVersion=".Length)..];

        Assert.Matches(@"^\d+\.\d+$", version);
        var fullVersion = Version.Parse(fullVersionText);
        Assert.Equal(fullVersion.ToString(2), version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncEquatorialSlew_WhenNotTracking_RejectsWithoutMotion(bool useTarget)
    {
        var telescope = CreateTelescope();

        if (useTarget)
            Assert.Throws<ASCOM.InvalidOperationException>(() => telescope.SlewToTargetAsync());
        else
            Assert.Throws<ASCOM.InvalidOperationException>(() => telescope.SlewToCoordinatesAsync(0, 0));

        Assert.False(telescope.Tracking);
        Assert.False(telescope.Slewing);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(99)]
    public void PulseGuide_InvalidDirection_ReturnsInvalidValueWithoutChangingGuideFlags(int direction)
    {
        var telescope = CreateTelescope(out var mount);
        mount.IsPulseGuidingRa = true;
        mount.IsPulseGuidingDec = true;

        Assert.Throws<ASCOM.InvalidValueException>(() => telescope.PulseGuide((GuideDirection)direction, 100));
        Assert.True(mount.IsPulseGuidingRa);
        Assert.True(mount.IsPulseGuidingDec);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30001)]
    public void PulseGuide_InvalidDuration_ReturnsInvalidValue(int duration)
    {
        var telescope = CreateTelescope();
        Assert.Throws<ASCOM.InvalidValueException>(() => telescope.PulseGuide(GuideDirection.North, duration));
    }

    [Fact]
    public void PulseGuide_WhenNotTracking_RejectsWithoutChangingGuideFlags()
    {
        var telescope = CreateTelescope(out var mount);
        mount.IsPulseGuidingRa = true;

        Assert.Throws<ASCOM.InvalidOperationException>(() => telescope.PulseGuide(GuideDirection.North, 100));
        Assert.True(mount.IsPulseGuidingRa);
        Assert.False(mount.IsPulseGuidingDec);
        Assert.False(telescope.Tracking);
    }

    [Fact]
    public void PulseGuide_WhenSlewing_RejectsWithoutChangingGuideFlags()
    {
        var telescope = CreateTelescope(out var mount);
        typeof(TelescopeMount).GetProperty(nameof(TelescopeMount.Tracking))!.SetValue(mount, true);
        mount.SlewState = SlewType.SlewAltAz;
        mount.IsPulseGuidingRa = true;

        Assert.Throws<ASCOM.InvalidOperationException>(() => telescope.PulseGuide(GuideDirection.North, 100));
        Assert.True(mount.IsPulseGuidingRa);
        Assert.False(mount.IsPulseGuidingDec);
        Assert.True(telescope.Slewing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30000)]
    public void PulseGuide_DurationBoundariesReachMountAndPreserveOtherAxisOnFailure(int duration)
    {
        var telescope = CreateTelescope(out var mount);
        typeof(TelescopeMount).GetProperty(nameof(TelescopeMount.Tracking))!.SetValue(mount, true);
        mount.IsPulseGuidingRa = true;

        var error = Assert.Throws<Exception>(() => telescope.PulseGuide(GuideDirection.North, duration));
        Assert.Equal("Mount not running", error.Message);
        Assert.True(mount.IsPulseGuidingRa);
        Assert.False(mount.IsPulseGuidingDec);
    }

    private static Telescope CreateTelescope() => CreateTelescope(out _);

    private static Telescope CreateTelescope(out TelescopeMount mount)
    {
        var settingsService = DispatchProxy.Create<IVersionedSettingsService, UnusedSettingsService>();
        var settings = new SkySettings(new GreenSwamp.Alpaca.Settings.Models.SkySettings
        {
            CanSlew = true,
            CanSlewAsync = true,
            CanPulseGuide = true
        }, settingsService);
        mount = new TelescopeMount("device-0", settings);
        return new Telescope(0, mount);
    }

    public class UnusedSettingsService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected settings call: {targetMethod?.Name}");
    }
}
