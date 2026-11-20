using System.Reflection;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.TelescopeDriver;
using GreenSwamp.Alpaca.Settings.Services;

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

    private static Telescope CreateTelescope()
    {
        var settingsService = DispatchProxy.Create<IVersionedSettingsService, UnusedSettingsService>();
        var settings = new SkySettings(new GreenSwamp.Alpaca.Settings.Models.SkySettings(), settingsService);
        return new Telescope(0, new GreenSwamp.Alpaca.MountControl.Mount("device-0", settings));
    }

    public class UnusedSettingsService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected settings call: {targetMethod?.Name}");
    }
}
