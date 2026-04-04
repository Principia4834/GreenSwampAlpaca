/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using GreenSwamp.Alpaca.Settings.Attributes;
using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Versioned settings service — per-device file storage redesign (S1–S8).
    /// Each device's settings are stored in device-nn.settings.json.
    /// Alpaca discovery metadata is stored in appsettings.alpaca.user.json.
    /// Server-wide monitor settings are stored in appsettings.user.json.
    /// </summary>
    public class VersionedSettingsService : IVersionedSettingsService
    {
        private readonly string _appDataRoot;
        private readonly string _currentVersionPath;
        private readonly IConfiguration _configuration;

        private readonly SemaphoreSlim _alpacaFileLock = new(1, 1);
        private readonly SemaphoreSlim _monitorFileLock = new(1, 1);
        private readonly SemaphoreSlim _observatoryFileLock = new(1, 1);
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceFileLocks = new();

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public string CurrentVersion { get; private set; }
        public string UserSettingsPath => Path.Combine(_currentVersionPath, "appsettings.user.json");
        public string AlpacaSettingsPath => Path.Combine(_currentVersionPath, "appsettings.alpaca.user.json");

        public event EventHandler<SkySettings>? DeviceSettingsChanged;
        public event EventHandler<MonitorSettings>? MonitorSettingsChanged;

        public VersionedSettingsService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            CurrentVersion = GetAssemblyVersion();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataRoot = Path.Combine(appData, "GreenSwampAlpaca");
            _currentVersionPath = Path.Combine(_appDataRoot, CurrentVersion);

            Directory.CreateDirectory(_currentVersionPath);
        }

        // ── Path helpers ──────────────────────────────────────────────────────

        public string GetDeviceSettingsPath(int deviceNumber)
        {
            if (deviceNumber < 0 || deviceNumber > 99)
                throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Device number must be 0–99.");
            return Path.Combine(_currentVersionPath, $"device-{deviceNumber:D2}.settings.json");
        }

        private SemaphoreSlim GetDeviceLock(int deviceNumber)
            => _deviceFileLocks.GetOrAdd(deviceNumber, _ => new SemaphoreSlim(1, 1));

        // ── Per-device settings ───────────────────────────────────────────────

        public SkySettings? GetDeviceSettings(int deviceNumber)
        {
            var path = GetDeviceSettingsPath(deviceNumber);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<SkySettings>(json);
                if (settings == null) return null;

                if (settings.DeviceNumber != deviceNumber)
                {
                    LogSafe("WARNING", $"DeviceNumber mismatch in device-{deviceNumber:D2}.settings.json: " +
                        $"expected {deviceNumber}, found {settings.DeviceNumber}. Correcting.");
                    settings.DeviceNumber = deviceNumber;
                }

                return settings;
            }
            catch (Exception ex)
            {
                LogSafe("ERROR", $"Error reading device-{deviceNumber:D2}.settings.json: {ex.Message}");
                return null;
            }
        }

        public List<SkySettings> GetAllDeviceSettings()
        {
            var files = Directory.GetFiles(_currentVersionPath, "device-??.settings.json");

            if (files.Length == 0)
            {
                RunFirstRunDeviceInit();
                files = Directory.GetFiles(_currentVersionPath, "device-??.settings.json");
            }

            var result = new List<SkySettings>();
            foreach (var file in files)
            {
                // Parse device number from "device-00.settings.json" → nn=0
                var stem = Path.GetFileNameWithoutExtension(file);         // "device-00.settings"
                var stem2 = Path.GetFileNameWithoutExtension(stem);        // "device-00"
                var nnStr = stem2.Length >= 9 ? stem2.Substring(7) : null; // "00"
                if (nnStr == null || !int.TryParse(nnStr, out var nn)) continue;

                var settings = GetDeviceSettings(nn);
                if (settings != null)
                    result.Add(settings);
            }

            return result.OrderBy(d => d.DeviceNumber).ToList();
        }

        public async Task SaveDeviceSettingsAsync(int deviceNumber, SkySettings settings)
        {
            if (deviceNumber < 0 || deviceNumber > 99)
                throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Device number must be 0–99.");

            settings.DeviceNumber = deviceNumber;

            var deviceLock = GetDeviceLock(deviceNumber);
            if (!await deviceLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"Timeout acquiring lock for device {deviceNumber}.");

            try
            {
                var path = GetDeviceSettingsPath(deviceNumber);
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                var sortedNode = new JsonObject(
                    JsonNode.Parse(json)!.AsObject()
                        .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value?.DeepClone())));
                json = sortedNode.ToJsonString(_jsonOptions);

                // Atomic write: temp file → rename (prevents partial-write corruption)
                var tempPath = path + ".tmp";
                await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, path, overwrite: true);

                LogSafe("INFO", $"Saved device-{deviceNumber:D2}.settings.json");
            }
            finally
            {
                deviceLock.Release();
            }

            DeviceSettingsChanged?.Invoke(this, settings);
        }

        public async Task DeleteDeviceSettingsAsync(int deviceNumber)
        {
            var path = GetDeviceSettingsPath(deviceNumber);
            var deviceLock = GetDeviceLock(deviceNumber);

            if (!await deviceLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"Timeout acquiring lock for device {deviceNumber}.");

            try
            {
                if (File.Exists(path))
                {
                    // Hard-delete for initial implementation.
                    // Future: could rename to .bak for soft-delete support.
                    File.Delete(path);
                    LogSafe("INFO", $"Deleted device-{deviceNumber:D2}.settings.json");
                }
            }
            finally
            {
                deviceLock.Release();
            }
        }

        public bool DeviceSettingsExist(int deviceNumber)
            => File.Exists(GetDeviceSettingsPath(deviceNumber));

        // ── First-run initialisation ──────────────────────────────────────────

        private void RunFirstRunDeviceInit()
        {
            LogSafe("INFO", "No device files found — running first-run device initialisation from DeviceTemplates.");

            var devicesSection = _configuration.GetSection("Devices");
            if (!devicesSection.Exists())
            {
                LogSafe("WARNING", "Devices section not found in appsettings.json — cannot initialise device files.");
                return;
            }

            var stubs = devicesSection.Get<List<SkySettings>>();
            if (stubs == null || stubs.Count == 0)
            {
                LogSafe("WARNING", "Devices array in appsettings.json is empty — cannot initialise device files.");
                return;
            }

            var observatory = GetObservatorySettings();
            foreach (var stub in stubs)
            {
                if (string.IsNullOrEmpty(stub.AlignmentMode) || !Enum.TryParse<AlignmentMode>(stub.AlignmentMode, out _))
                {
                    LogSafe("WARNING", $"Device {stub.DeviceNumber} has unknown AlignmentMode '{stub.AlignmentMode}' — skipping.");
                    continue;
                }

                try
                {
                    var device = new SkySettings();
                    _configuration.GetSection($"DeviceTemplates:{stub.AlignmentMode}").Bind(device);

                    device.Latitude = observatory.Latitude;
                    device.Longitude = observatory.Longitude;
                    device.Elevation = observatory.Elevation;
                    device.UTCOffset = observatory.UTCOffset;

                    device.DeviceNumber = stub.DeviceNumber;
                    device.DeviceName = stub.DeviceName;
                    device.Enabled = stub.Enabled;

                    var path = GetDeviceSettingsPath(device.DeviceNumber);
                    var json = JsonSerializer.Serialize(device, _jsonOptions);
                    var sortedNode = new JsonObject(
                        JsonNode.Parse(json)!.AsObject()
                            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value?.DeepClone())));
                    json = sortedNode.ToJsonString(_jsonOptions);
                    File.WriteAllText(path, json, Encoding.UTF8);
                    LogSafe("INFO", $"Created device-{device.DeviceNumber:D2}.settings.json from DeviceTemplates:{stub.AlignmentMode}.");
                }
                catch (Exception ex)
                {
                    LogSafe("ERROR", $"Failed to create device-{stub.DeviceNumber:D2}.settings.json: {ex.Message}");
                }
            }

            if (!File.Exists(AlpacaSettingsPath))
                InitialiseAlpacaSettingsFile();
        }

        private void InitialiseAlpacaSettingsFile()
        {
            var alpacaSection = _configuration.GetSection("AlpacaDevices");
            List<AlpacaDevice> alpacaDevices;

            if (alpacaSection.Exists())
                alpacaDevices = alpacaSection.Get<List<AlpacaDevice>>() ?? new List<AlpacaDevice>();
            else
                alpacaDevices = new List<AlpacaDevice>();

            try
            {
                var doc = new { AlpacaDevices = alpacaDevices };
                var json = JsonSerializer.Serialize(doc, _jsonOptions);
                File.WriteAllText(AlpacaSettingsPath, json, Encoding.UTF8);
                LogSafe("INFO", "Created appsettings.alpaca.user.json from factory defaults.");
            }
            catch (Exception ex)
            {
                LogSafe("ERROR", $"Failed to create appsettings.alpaca.user.json: {ex.Message}");
            }
        }

        // ── Alpaca discovery ──────────────────────────────────────────────────

        public List<AlpacaDevice> GetAlpacaDevices()
        {
            if (!File.Exists(AlpacaSettingsPath))
                InitialiseAlpacaSettingsFile();

            try
            {
                var json = File.ReadAllText(AlpacaSettingsPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (doc != null && doc.TryGetValue("AlpacaDevices", out var element))
                    return element.Deserialize<List<AlpacaDevice>>() ?? new List<AlpacaDevice>();
            }
            catch (Exception ex)
            {
                LogSafe("ERROR", $"Error reading AlpacaDevices from appsettings.alpaca.user.json: {ex.Message}");
            }

            return new List<AlpacaDevice>();
        }

        public async Task SaveAlpacaDevicesAsync(List<AlpacaDevice> devices)
        {
            if (!await _alpacaFileLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timeout acquiring Alpaca file lock.");

            try
            {
                await WriteAlpacaDevicesUnlockedAsync(devices);
            }
            finally
            {
                _alpacaFileLock.Release();
            }
        }

        public async Task AddAlpacaDeviceAsync(AlpacaDevice device)
        {
            if (!await _alpacaFileLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timeout acquiring Alpaca file lock.");

            try
            {
                var devices = ReadAlpacaDevicesUnlocked();

                if (devices.Count >= 100)
                    throw new InvalidOperationException("Maximum of 100 devices reached.");

                if (devices.Any(d => d.DeviceNumber == device.DeviceNumber))
                    throw new InvalidOperationException($"Device {device.DeviceNumber} already exists in AlpacaDevices.");

                devices.Add(device);
                await WriteAlpacaDevicesUnlockedAsync(devices);
            }
            finally
            {
                _alpacaFileLock.Release();
            }
        }

        public async Task RemoveAlpacaDeviceAsync(int deviceNumber)
        {
            if (!await _alpacaFileLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timeout acquiring Alpaca file lock.");

            try
            {
                var devices = ReadAlpacaDevicesUnlocked();
                devices.RemoveAll(d => d.DeviceNumber == deviceNumber);
                await WriteAlpacaDevicesUnlockedAsync(devices);
            }
            finally
            {
                _alpacaFileLock.Release();
            }
        }

        private List<AlpacaDevice> ReadAlpacaDevicesUnlocked()
        {
            if (!File.Exists(AlpacaSettingsPath)) return new List<AlpacaDevice>();

            try
            {
                var json = File.ReadAllText(AlpacaSettingsPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (doc != null && doc.TryGetValue("AlpacaDevices", out var element))
                    return element.Deserialize<List<AlpacaDevice>>() ?? new List<AlpacaDevice>();
            }
            catch { }

            return new List<AlpacaDevice>();
        }

        private async Task WriteAlpacaDevicesUnlockedAsync(List<AlpacaDevice> devices)
        {
            var doc = new { AlpacaDevices = devices };
            var json = JsonSerializer.Serialize(doc, _jsonOptions);
            var tempPath = AlpacaSettingsPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, AlpacaSettingsPath, overwrite: true);
        }

        // ── Validation ────────────────────────────────────────────────────────

        public ValidationResult ValidateDeviceSettings(int deviceNumber)
        {
            var result = new ValidationResult { IsValid = true };
            var path = GetDeviceSettingsPath(deviceNumber);

            if (!File.Exists(path))
            {
                result.Warnings.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_FILE_NOT_FOUND",
                    Severity = "info",
                    DeviceNumber = deviceNumber,
                    Message = $"device-{deviceNumber:D2}.settings.json not found.",
                    Resolution = "File will be created on first save or first-run initialisation."
                });
                return result;
            }

            SkySettings? settings = null;
            try
            {
                var json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<SkySettings>(json);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_FILE_PARSE_ERROR",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = $"Invalid JSON in device-{deviceNumber:D2}.settings.json: {ex.Message}",
                    Resolution = "Delete the file and restart to regenerate from factory defaults."
                });
                return result;
            }

            if (settings == null)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_FILE_PARSE_ERROR",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = $"device-{deviceNumber:D2}.settings.json deserialised to null.",
                    Resolution = "Delete the file and restart to regenerate from factory defaults."
                });
                return result;
            }

            if (settings.DeviceNumber != deviceNumber)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_NUMBER_MISMATCH",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = $"DeviceNumber in file ({settings.DeviceNumber}) does not match filename ({deviceNumber}).",
                    Resolution = "Edit the file to set DeviceNumber to match the filename, or delete and regenerate."
                });
            }

            if (string.IsNullOrWhiteSpace(settings.DeviceName))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_MISSING_REQUIRED",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = "DeviceName is required but is null or empty.",
                    Resolution = "Edit device-{deviceNumber:D2}.settings.json to set a non-empty DeviceName."
                });
            }

            if (string.IsNullOrEmpty(settings.AlignmentMode) || !Enum.TryParse<AlignmentMode>(settings.AlignmentMode, out _))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_MISSING_REQUIRED",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = $"AlignmentMode '{settings.AlignmentMode}' is not valid. Expected: AltAz, GermanPolar, or Polar.",
                    Resolution = "Edit the file to set a valid AlignmentMode."
                });
            }

            if (string.IsNullOrEmpty(settings.Mount))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_MISSING_REQUIRED",
                    Severity = "error",
                    DeviceNumber = deviceNumber,
                    Message = "Mount type is required but was null or empty.",
                    Resolution = "Edit the file to set a valid Mount type."
                });
            }

            return result;
        }

        public ValidationResult ValidateAlpacaDevices()
        {
            var result = new ValidationResult { IsValid = true };

            if (!File.Exists(AlpacaSettingsPath))
            {
                result.Warnings.Add(new ValidationError
                {
                    ErrorCode = "ALPACA_FILE_NOT_FOUND",
                    Severity = "info",
                    Message = "appsettings.alpaca.user.json not found.",
                    Resolution = "File will be created on first run."
                });
                return result;
            }

            List<AlpacaDevice> devices;
            try
            {
                var json = File.ReadAllText(AlpacaSettingsPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (doc == null || !doc.TryGetValue("AlpacaDevices", out var element))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        ErrorCode = "ALPACA_FILE_PARSE_ERROR",
                        Severity = "error",
                        Message = "AlpacaDevices key missing from appsettings.alpaca.user.json.",
                        Resolution = "Delete the file and restart to regenerate."
                    });
                    return result;
                }
                devices = element.Deserialize<List<AlpacaDevice>>() ?? new List<AlpacaDevice>();
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "ALPACA_FILE_PARSE_ERROR",
                    Severity = "error",
                    Message = $"Invalid JSON in appsettings.alpaca.user.json: {ex.Message}",
                    Resolution = "Delete the file and restart to regenerate."
                });
                return result;
            }

            if (devices.Count > 100)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "ALPACA_MAX_DEVICES_EXCEEDED",
                    Severity = "error",
                    Message = $"AlpacaDevices array has {devices.Count} entries; maximum is 100.",
                    Resolution = "Remove excess entries from appsettings.alpaca.user.json."
                });
            }

            var seen = new HashSet<int>();
            foreach (var d in devices)
            {
                if (d.DeviceNumber < 0 || d.DeviceNumber > 99)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        ErrorCode = "ALPACA_DEVICE_NUMBER_OUT_OF_RANGE",
                        Severity = "error",
                        DeviceNumber = d.DeviceNumber,
                        Message = $"DeviceNumber {d.DeviceNumber} is out of range 0–99.",
                        Resolution = "Correct the DeviceNumber in appsettings.alpaca.user.json."
                    });
                }

                if (!seen.Add(d.DeviceNumber))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        ErrorCode = "ALPACA_DUPLICATE_DEVICE_NUMBER",
                        Severity = "error",
                        DeviceNumber = d.DeviceNumber,
                        Message = $"Duplicate DeviceNumber {d.DeviceNumber} in AlpacaDevices.",
                        Resolution = "Remove duplicate entries from appsettings.alpaca.user.json."
                    });
                }

                // Q2: Warn if no corresponding device file (no auto-repair in v1)
                if (!DeviceSettingsExist(d.DeviceNumber))
                {
                    result.Warnings.Add(new ValidationError
                    {
                        ErrorCode = "ALPACA_ORPHANED_ENTRY",
                        Severity = "warning",
                        DeviceNumber = d.DeviceNumber,
                        Message = $"AlpacaDevices entry for device {d.DeviceNumber} has no corresponding device-{d.DeviceNumber:D2}.settings.json.",
                        Resolution = "Create the device settings file or remove the AlpacaDevices entry."
                        // TODO: Future auto-repair: add device file from template and create matching device file.
                    });
                }
            }

            return result;
        }

        // ── Monitor settings ──────────────────────────────────────────────────

        public MonitorSettings GetMonitorSettings()
        {
            var settings = new MonitorSettings();
            _configuration.GetSection("MonitorSettings").Bind(settings);
            return settings;
        }

        public async Task SaveMonitorSettingsAsync(MonitorSettings settings)
        {
            if (!await _monitorFileLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timeout acquiring monitor settings lock.");

            try
            {
                var userSettings = await ReadSettingsFileAsync(UserSettingsPath);
                userSettings["MonitorSettings"] = JsonSerializer.SerializeToElement(settings);

                var json = JsonSerializer.Serialize(userSettings, _jsonOptions);
                var tempPath = UserSettingsPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, UserSettingsPath, overwrite: true);

                LogSafe("INFO", $"Monitor settings saved for version {CurrentVersion}");
            }
            finally
            {
                _monitorFileLock.Release();
            }

            MonitorSettingsChanged?.Invoke(this, settings);
        }

        // ── Observatory settings (Behaviour B4) ──────────────────────────────

        public ObservatorySettings GetObservatorySettings()
        {
            var observatoryPath = Path.Combine(_currentVersionPath, "observatory.settings.json");

            if (File.Exists(observatoryPath))
            {
                try
                {
                    var json = File.ReadAllText(observatoryPath);
                    var saved = JsonSerializer.Deserialize<ObservatorySettings>(json);
                    if (saved != null)
                    {
                        LogSafe("INFO", "Loaded observatory settings from observatory.settings.json");
                        return saved;
                    }
                }
                catch (Exception ex)
                {
                    LogSafe("WARNING", $"Failed to read observatory.settings.json: {ex.Message} — using ObservatoryDefaults");
                }
            }

            // First run (B4): bind from ObservatoryDefaults and write the file
            var defaults = new ObservatorySettings();
            _configuration.GetSection("ObservatoryDefaults").Bind(defaults);
            LogSafe("INFO", "observatory.settings.json not found — first-run write from ObservatoryDefaults.");
            try
            {
                var json = JsonSerializer.Serialize(defaults, _jsonOptions);
                var tempPath = observatoryPath + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, observatoryPath, overwrite: true);
                LogSafe("INFO", "Created observatory.settings.json from ObservatoryDefaults.");
            }
            catch (Exception ex)
            {
                LogSafe("WARNING", $"Failed to write observatory.settings.json on first run: {ex.Message}");
            }
            return defaults;
        }

        public async Task SaveObservatorySettingsAsync(ObservatorySettings settings)
        {
            // TODO: Future feature — optionally push updated observatory settings to all registered
            // device-nn.settings.json files. This would iterate GetAllDeviceSettings(), update
            // observatory properties in each, and call SaveDeviceSettingsAsync(). Deferred to v2;
            // requires explicit user confirmation in UI to avoid accidental overwrites.
            var observatoryPath = Path.Combine(_currentVersionPath, "observatory.settings.json");

            if (!await _observatoryFileLock.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timeout acquiring observatory settings lock.");

            try
            {
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                var tempPath = observatoryPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, observatoryPath, overwrite: true);
                LogSafe("INFO", $"Observatory settings saved to {observatoryPath}");
            }
            finally
            {
                _observatoryFileLock.Release();
            }
        }

        // ── Mode-aware device creation and change ─────────────────────────────

        public async Task CreateDeviceForModeAsync(int deviceNumber, string deviceName, AlignmentMode mode)
        {
            var modeName = mode.ToString(); // "GermanPolar", "Polar", or "AltAz"

            // B1: Bind factory defaults for the requested alignment mode from DeviceTemplates
            var device = new SkySettings();
            _configuration.GetSection($"DeviceTemplates:{modeName}").Bind(device);

            // B1: Override observatory properties with current observatory.settings.json values
            var observatory = GetObservatorySettings();
            device.Latitude = observatory.Latitude;
            device.Longitude = observatory.Longitude;
            device.Elevation = observatory.Elevation;
            device.UTCOffset = observatory.UTCOffset;

            device.DeviceNumber = deviceNumber;
            device.DeviceName = deviceName;
            device.Enabled = true;

            await SaveDeviceSettingsAsync(deviceNumber, device);
            LogSafe("INFO", $"Created device {deviceNumber} ({deviceName}) for mode {modeName}");
        }

        public async Task ChangeAlignmentModeAsync(int deviceNumber, AlignmentMode newMode)
        {
            var modeName = newMode.ToString(); // "GermanPolar", "Polar", or "AltAz"

            var existing = GetDeviceSettings(deviceNumber)
                ?? throw new InvalidOperationException($"Device {deviceNumber} not found.");

            // B2: Bind the new mode's template to get [UniqueSetting] replacement values
            var modeTemplate = new SkySettings();
            _configuration.GetSection($"DeviceTemplates:{modeName}").Bind(modeTemplate);

            // B2: Copy [UniqueSetting] properties from template; all [CommonSetting] properties are preserved
            var uniqueProps = typeof(SkySettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<UniqueSettingAttribute>() != null && p.CanWrite);

            foreach (var prop in uniqueProps)
                prop.SetValue(existing, prop.GetValue(modeTemplate));

            existing.AlignmentMode = newMode.ToString();
            await SaveDeviceSettingsAsync(deviceNumber, existing);
            LogSafe("INFO", $"Changed device {deviceNumber} alignment mode to {modeName}");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<Dictionary<string, JsonElement>> ReadSettingsFileAsync(string path)
        {
            if (!File.Exists(path))
                return new Dictionary<string, JsonElement>();

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch
            {
                return new Dictionary<string, JsonElement>();
            }
        }

        private string GetAssemblyVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;

            var informationalVersion = infoVersionAttr?.InformationalVersion;

            if (!string.IsNullOrEmpty(informationalVersion))
            {
                var plusIndex = informationalVersion.IndexOf('+');
                if (plusIndex > 0)
                    informationalVersion = informationalVersion.Substring(0, plusIndex);
                return informationalVersion;
            }

            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }

        private void LogSafe(string level, string message)
        {
            try
            {
                switch (level.ToUpperInvariant())
                {
                    case "ERROR":
                        ASCOM.Alpaca.Logging.LogError(message);
                        break;
                    case "WARNING":
                        ASCOM.Alpaca.Logging.LogWarning(message);
                        break;
                    default:
                        ASCOM.Alpaca.Logging.LogVerbose(message);
                        break;
                }
            }
            catch
            {
                var prefix = level switch
                {
                    "ERROR" => "❌ ERROR",
                    "WARNING" => "⚠️ WARNING",
                    _ => "ℹ️ INFO"
                };
                Console.WriteLine($"{prefix} [VersionedSettingsService]: {message}");
            }
        }
    }
}
