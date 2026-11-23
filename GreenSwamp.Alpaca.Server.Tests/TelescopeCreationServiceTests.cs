using System.Reflection;
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Server.Services;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GreenSwamp.Alpaca.Server.Tests;

public class TelescopeCreationServiceTests
{
    [Theory]
    [InlineData(MountType.Simulator, AlignmentMode.GermanPolar)]
    [InlineData(MountType.Simulator, AlignmentMode.Polar)]
    [InlineData(MountType.Simulator, AlignmentMode.AltAz)]
    [InlineData(MountType.SkyWatcher, AlignmentMode.GermanPolar)]
    [InlineData(MountType.SkyWatcher, AlignmentMode.Polar)]
    [InlineData(MountType.SkyWatcher, AlignmentMode.AltAz)]
    public async Task Create_SavesRequestedValuesAndDiscoveryMetadata(MountType type, AlignmentMode mode)
    {
        var (service, store) = CreateService();
        var request = new TelescopeCreationRequest
        {
            Name = "  My telescope  ", MountType = type, AlignmentMode = mode, PolarSide = "Right",
            Latitude = -33.5, Longitude = 151.2, Elevation = 42,
            Port = " COM7 ", BaudRate = 115200,
            CopiedObservatory = new ObservatoryInfo { GpsPort = "COM8", GpsBaudRate = 4800, UTCOffset = TimeSpan.FromHours(10) }
        };

        var number = await service.CreateAsync(request);
        var saved = store.Devices[number];
        Assert.Equal("My telescope", saved.DeviceName);
        Assert.Equal(type.ToString(), saved.Mount);
        Assert.Equal(mode.ToString(), saved.AlignmentMode);
        Assert.Equal(-33.5, saved.Latitude);
        Assert.Equal(151.2, saved.Longitude);
        Assert.Equal(42, saved.Elevation);
        Assert.Equal("COM8", saved.GpsPort);
        Assert.Equal(4800, saved.GpsBaudRate);
        Assert.Equal(TimeSpan.FromHours(10), saved.UTCOffset);
        Assert.Equal(type == MountType.SkyWatcher ? "COM7" : "template-port", saved.Port);
        Assert.Equal(type == MountType.SkyWatcher ? 115200 : 9600, saved.BaudRate);
        if (mode == AlignmentMode.Polar) Assert.Equal("Right", saved.PolarMode);
        var metadata = Assert.Single(store.Metadata);
        Assert.Equal(number, metadata.DeviceNumber);
        Assert.Equal(saved.DeviceName, metadata.DeviceName);
        Assert.True(Guid.TryParse(metadata.UniqueId, out _));
    }

    [Fact]
    public async Task Create_SkyWatcherWithoutSerial_LeavesTemplateAndEnabled()
    {
        var (service, store) = CreateService();
        var number = await service.CreateAsync(new TelescopeCreationRequest { Name = "Mount", MountType = MountType.SkyWatcher });
        Assert.True(store.Devices[number].Enabled);
        Assert.Equal("template-port", store.Devices[number].Port);
        Assert.Equal(9600, store.Devices[number].BaudRate);
    }

    [Theory]
    [InlineData(-91, 0, 0)]
    [InlineData(91, 0, 0)]
    [InlineData(0, -181, 0)]
    [InlineData(0, 181, 0)]
    [InlineData(0, 0, -501)]
    [InlineData(0, 0, 9001)]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    public async Task Create_RejectsInvalidCoordinates(double latitude, double longitude, double elevation)
    {
        var (service, store) = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new TelescopeCreationRequest
        {
            Name = "Mount", Latitude = latitude, Longitude = longitude, Elevation = elevation
        }));
        Assert.Empty(store.Devices);
        Assert.Empty(store.Metadata);
    }

    [Fact]
    public async Task Create_WhenMetadataWriteFails_RemovesDeviceFile()
    {
        var (service, store) = CreateService();
        store.FailMetadataWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new TelescopeCreationRequest { Name = "Mount" }));
        Assert.Empty(store.Devices);
        Assert.Empty(store.Metadata);
    }

    [Fact]
    public async Task Create_ConcurrentRequests_AssignDistinctNumbers()
    {
        var (service, store) = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(n =>
            service.CreateAsync(new TelescopeCreationRequest { Name = $"Mount {n}" })));
        Assert.Equal(4, results.Distinct().Count());
        Assert.Equal(4, store.Devices.Count);
        Assert.Equal(4, store.Metadata.Count);
    }

    [Fact]
    public async Task Create_OnlyNotifiesAfterSettingsAndMetadataAreSaved()
    {
        var (service, store) = CreateService();
        SkySettings? notified = null;
        service.DeviceCreated += (_, device) =>
        {
            Assert.Same(device, store.Devices[device.DeviceNumber]);
            Assert.Contains(store.Metadata, metadata => metadata.DeviceNumber == device.DeviceNumber);
            notified = device;
        };
        var number = await service.CreateAsync(new TelescopeCreationRequest { Name = "Mount" });
        Assert.Same(store.Devices[number], notified);
    }

    [Fact]
    public async Task Create_WhenAllSlotsAreUsed_DoesNotOverwriteSettings()
    {
        var (service, store) = CreateService();
        for (var number = 0; number < 100; number++)
            store.Metadata.Add(new AlpacaDevice { DeviceNumber = number });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new TelescopeCreationRequest { Name = "Mount" }));
        Assert.Empty(store.Devices);
        Assert.Equal(100, store.Metadata.Count);
    }

    [Fact]
    public async Task Create_AcceptsCoordinateBoundaries()
    {
        var (service, store) = CreateService();
        var number = await service.CreateAsync(new TelescopeCreationRequest { Name = "Mount", Latitude = -90, Longitude = 180, Elevation = -500 });
        Assert.Equal(-90, store.Devices[number].Latitude);
        Assert.Equal(180, store.Devices[number].Longitude);
        Assert.Equal(-500, store.Devices[number].Elevation);
    }

    private static (TelescopeCreationService Service, SettingsProxy Store) CreateService()
    {
        var settings = DispatchProxy.Create<IVersionedSettingsService, SettingsProxy>();
        var store = (SettingsProxy)settings;
        var registry = new UnifiedDeviceRegistry(settings, NullLogger<UnifiedDeviceRegistry>.Instance);
        return (new TelescopeCreationService(settings, registry), store);
    }

    public class SettingsProxy : DispatchProxy
    {
        public Dictionary<int, SkySettings> Devices { get; } = [];
        public List<AlpacaDevice> Metadata { get; } = [];
        public bool FailMetadataWrite { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            args ??= [];
            switch (targetMethod!.Name)
            {
                case "add_DeviceSettingsChanged": return null;
                case nameof(IVersionedSettingsService.GetAlpacaDevices): return Metadata.ToList();
                case nameof(IVersionedSettingsService.DeviceSettingsExist): return Devices.ContainsKey((int)args[0]!);
                case nameof(IVersionedSettingsService.BuildDeviceForMode):
                    return new SkySettings
                    {
                        DeviceNumber = (int)args[0]!, DeviceName = (string)args[1]!,
                        Mount = args[2]!.ToString()!, AlignmentMode = args[3]!.ToString()!,
                        Port = "template-port", BaudRate = 9600, Enabled = true
                    };
                case nameof(IVersionedSettingsService.SaveDeviceSettingsAsync):
                    Devices[(int)args[0]!] = (SkySettings)args[1]!;
                    return Task.CompletedTask;
                case nameof(IVersionedSettingsService.AddAlpacaDeviceAsync):
                    if (FailMetadataWrite) throw new InvalidOperationException("Simulated metadata failure");
                    Metadata.Add((AlpacaDevice)args[0]!);
                    return Task.CompletedTask;
                case nameof(IVersionedSettingsService.DeleteDeviceSettingsAsync):
                    Devices.Remove((int)args[0]!);
                    return Task.CompletedTask;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
