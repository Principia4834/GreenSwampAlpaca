using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class TelescopeCreationRequest
{
    public string Name { get; set; } = string.Empty;
    public MountType MountType { get; set; } = MountType.Simulator;
    public AlignmentMode AlignmentMode { get; set; } = AlignmentMode.GermanPolar;
    public string PolarSide { get; set; } = "Left";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Elevation { get; set; }
    public ObservatoryInfo? CopiedObservatory { get; set; }
    public string? Port { get; set; }
    public int? BaudRate { get; set; }
}

public sealed class TelescopeCreationService(IVersionedSettingsService settings, UnifiedDeviceRegistry registry)
{
    private readonly SemaphoreSlim _creationLock = new(1, 1);

    public event EventHandler<SkySettings>? DeviceCreated;
    public event EventHandler? DevicesReloaded;

    public void NotifyDevicesReloaded() => DevicesReloaded?.Invoke(this, EventArgs.Empty);

    public async Task<int> CreateAsync(TelescopeCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new ArgumentException("Device name must be 1-100 characters.");
        if (!Enum.IsDefined(request.MountType) || request.AlignmentMode is not (AlignmentMode.GermanPolar or AlignmentMode.Polar or AlignmentMode.AltAz))
            throw new ArgumentException("Invalid mount type or alignment mode.");
        if (request.AlignmentMode == AlignmentMode.Polar && request.PolarSide is not ("Left" or "Right"))
            throw new ArgumentException("Invalid polar side.");
        if (!double.IsFinite(request.Latitude) || request.Latitude < -90 || request.Latitude > 90 ||
            !double.IsFinite(request.Longitude) || request.Longitude < -180 || request.Longitude > 180 ||
            !double.IsFinite(request.Elevation) || request.Elevation < -500 || request.Elevation > 9000)
            throw new ArgumentException("Invalid observatory coordinates or elevation.");
        if (request.BaudRate is <= 0)
            throw new ArgumentException("Baud rate must be positive.");

        await _creationLock.WaitAsync();
        try
        {
            var metadata = settings.GetAlpacaDevices();
            var number = Enumerable.Range(0, 100).FirstOrDefault(n =>
                !settings.DeviceSettingsExist(n) && metadata.All(d => d.DeviceNumber != n) && registry.IsDeviceNumberAvailable(n), -1);
            if (number < 0)
                throw new InvalidOperationException("No available telescope device numbers (0-99).");

            var device = settings.BuildDeviceForMode(number, name, request.MountType, request.AlignmentMode);
            device.Latitude = request.Latitude;
            device.Longitude = request.Longitude;
            device.Elevation = request.Elevation;
            if (request.AlignmentMode == AlignmentMode.Polar)
                device.PolarMode = request.PolarSide;
            if (request.CopiedObservatory is { } observatory)
            {
                device.UTCOffset = observatory.UTCOffset;
                device.GpsPort = observatory.GpsPort;
                device.GpsBaudRate = observatory.GpsBaudRate;
                device.GpsParity = observatory.GpsParity;
                device.GpsStopBits = observatory.GpsStopBits;
                device.GpsDataBits = observatory.GpsDataBits;
                device.GpsTimeout = observatory.GpsTimeout;
                device.GpsHandshake = observatory.GpsHandshake;
                device.CdcAddress = observatory.CdcAddress;
                device.CdcPort = observatory.CdcPort;
            }
            if (request.MountType == MountType.SkyWatcher && !string.IsNullOrWhiteSpace(request.Port))
                device.Port = request.Port.Trim();
            if (request.MountType == MountType.SkyWatcher && request.BaudRate is { } baudRate)
                device.BaudRate = baudRate;

            try
            {
                await settings.SaveDeviceSettingsAsync(number, device);
                await settings.AddAlpacaDeviceAsync(new AlpacaDevice { DeviceNumber = number, DeviceName = name });
            }
            catch (Exception creationError)
            {
                try
                {
                    if (settings.DeviceSettingsExist(number))
                        await settings.DeleteDeviceSettingsAsync(number);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Device creation failed and the settings file could not be removed.", creationError, cleanupError);
                }
                throw;
            }
            DeviceCreated?.Invoke(this, device);
            return number;
        }
        finally
        {
            _creationLock.Release();
        }
    }
}
