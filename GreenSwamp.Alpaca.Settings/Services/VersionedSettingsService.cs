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

using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Versioned settings service that manages user settings with version-specific folders
    /// </summary>
    public class VersionedSettingsService : IVersionedSettingsService
    {
        private readonly string _appDataRoot;
        private readonly string _currentVersionPath;
        private readonly string _versionFile;
        private readonly IConfiguration _configuration;
        private readonly IDeviceSynchronizationService _syncService;
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string? _customSettingsPath;

        // Phase 4.2: Custom path support
        public string CurrentVersion { get; private set; }
        public string[] AvailableVersions => GetAvailableVersions();
        public string UserSettingsPath => _customSettingsPath ?? GetUserSettingsPath(CurrentVersion);

        public event EventHandler<SkySettings>? SettingsChanged;
        public event EventHandler<MonitorSettings>? MonitorSettingsChanged;

        public VersionedSettingsService(
            IConfiguration configuration,
            IDeviceSynchronizationService syncService)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

            // Get current app version from assembly
            CurrentVersion = GetAssemblyVersion();

            // Setup versioned folder structure
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataRoot = Path.Combine(appData, "GreenSwampAlpaca");
            _currentVersionPath = Path.Combine(_appDataRoot, CurrentVersion);
            _versionFile = Path.Combine(_appDataRoot, "current.version");

            Directory.CreateDirectory(_currentVersionPath);

            // Initialize versioned settings
            InitializeVersionedSettings();
        }


        /// <summary>
        /// Phase 4.2: Constructor with custom settings file path
        /// </summary>
        /// <param name="configuration">Configuration root</param>
        /// <param name="customSettingsPath">Custom path to settings file (overrides default)</param>
        /// <param name="syncService">Device synchronization service</param>
        public VersionedSettingsService(
            IConfiguration configuration,
            string customSettingsPath,
            IDeviceSynchronizationService syncService)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _customSettingsPath = customSettingsPath;

            if (!string.IsNullOrEmpty(_customSettingsPath))
            {
                // Phase 4.2: Extract version and setup from custom path
                CurrentVersion = GetVersionFromPath(_customSettingsPath);
                var customDir = Path.GetDirectoryName(_customSettingsPath) ??
                               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GreenSwampAlpaca", CurrentVersion);

                _appDataRoot = Path.GetDirectoryName(customDir) ??
                              Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GreenSwampAlpaca");
                _currentVersionPath = customDir;
                _versionFile = Path.Combine(_appDataRoot, "current.version");

                Directory.CreateDirectory(_currentVersionPath);

                ASCOM.Alpaca.Logging.LogVerbose($"Phase 4.2: Using custom settings path: {_customSettingsPath}");
            }
            else
            {
                // Use default path logic
                CurrentVersion = GetAssemblyVersion();
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _appDataRoot = Path.Combine(appData, "GreenSwampAlpaca");
                _currentVersionPath = Path.Combine(_appDataRoot, CurrentVersion);
                _versionFile = Path.Combine(_appDataRoot, "current.version");

                Directory.CreateDirectory(_currentVersionPath);
            }

            // Initialize versioned settings
            InitializeVersionedSettings();
        }

        public SkySettings GetSettings()
        {
            // Phase 3 baseline (v1.0.0+): Return first device for backward compatibility with single-device code
            var devices = GetAllDevices();
            return devices.FirstOrDefault() ?? CreateDefaultDevice();
        }

        /// <summary>
        /// Phase 3 baseline (v1.0.0+): Gets all configured devices from Devices array
        /// </summary>
        public List<SkySettings> GetAllDevices()
        {
            // Call the validation overload and discard validation results
            return GetAllDevices(out _);
        }

        /// <summary>
        /// Gets all configured devices with detailed validation results
        /// Invalid devices are quarantined (not included in returned list)
        /// </summary>
        public List<SkySettings> GetAllDevices(out ValidationResult validationResult)
        {
            validationResult = new ValidationResult { IsValid = true };
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            // Phase 1: File existence check
            if (!File.Exists(userSettingsPath))
            {
                validationResult.Warnings.Add(new ValidationError
                {
                    ErrorCode = "FILE_NOT_FOUND",
                    Severity = "info",
                    Message = "User settings file not found. Creating default settings.",
                    Resolution = "Automatic - default settings will be created.",
                    IsAutoRepairable = true
                });

                LogSafe("INFO", "FILE_NOT_FOUND: Creating default settings");
                CreateDefaultUserSettings(CurrentVersion);
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            // Phase 2: File reading
            string jsonContent;
            try
            {
                jsonContent = File.ReadAllText(userSettingsPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add(new ValidationError
                {
                    ErrorCode = "FILE_ACCESS_DENIED",
                    Severity = "error",
                    Message = $"Cannot read settings file: {ex.Message}",
                    Resolution = $"Check file permissions on {userSettingsPath}",
                    IsAutoRepairable = false
                });

                LogSafe("ERROR", $"FILE_ACCESS_DENIED: {userSettingsPath} - {ex.Message}");
                return new List<SkySettings> { CreateDefaultDevice() };
            }
            catch (IOException ex)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add(new ValidationError
                {
                    ErrorCode = "FILE_LOCKED",
                    Severity = "warning",
                    Message = $"Settings file is locked: {ex.Message}",
                    Resolution = "Close other applications that may be using this file and retry.",
                    IsAutoRepairable = false
                });

                LogSafe("WARNING", $"FILE_LOCKED: {userSettingsPath} - {ex.Message}");
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            // Phase 3: JSON parsing
            Dictionary<string, JsonElement> doc;
            try
            {
                doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent) ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException ex)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add(new ValidationError
                {
                    ErrorCode = "FILE_PARSE_ERROR",
                    Severity = "error",
                    Message = $"Settings file contains invalid JSON: {ex.Message}",
                    Resolution = $"Delete {userSettingsPath} and restart to regenerate default settings.",
                    IsAutoRepairable = false
                });

                LogSafe("ERROR", $"FILE_PARSE_ERROR: Invalid JSON in {userSettingsPath} - {ex.Message}");
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            // Phase 4: Structural validation
            if (!doc.ContainsKey("Devices"))
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add(new ValidationError
                {
                    ErrorCode = "MISSING_DEVICES_ARRAY",
                    Severity = "error",
                    Message = "Settings file missing 'Devices' array.",
                    Resolution = "Use 'Repair Settings' button to regenerate structure.",
                    IsAutoRepairable = true
                });

                LogSafe("ERROR", "MISSING_DEVICES_ARRAY: Settings file missing Devices array");
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            List<SkySettings>? devices;
            try
            {
                devices = doc["Devices"].Deserialize<List<SkySettings>>();
            }
            catch (JsonException ex)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add(new ValidationError
                {
                    ErrorCode = "INVALID_ARRAY_TYPE",
                    Severity = "error",
                    Message = $"Devices array has invalid format: {ex.Message}",
                    Resolution = "Use 'Repair Settings' button to regenerate Devices array.",
                    IsAutoRepairable = true
                });

                LogSafe("ERROR", $"INVALID_ARRAY_TYPE: Cannot deserialize Devices array - {ex.Message}");
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            if (devices == null || !devices.Any())
            {
                validationResult.Warnings.Add(new ValidationError
                {
                    ErrorCode = "EMPTY_DEVICES_ARRAY",
                    Severity = "warning",
                    Message = "Devices array is empty. Creating default device.",
                    Resolution = "Automatic - default device will be added.",
                    IsAutoRepairable = true
                });

                LogSafe("WARNING", "EMPTY_DEVICES_ARRAY: No devices configured, creating default");
                devices = new List<SkySettings> { CreateDefaultDevice() };
                Task.Run(() => SaveAllDevicesAsync(devices)).Wait();
            }

            // Phase 5: Synchronization validation
            var syncValidation = _syncService.ValidateSynchronizationDetailed(doc);
            validationResult.Errors.AddRange(syncValidation.Errors);
            validationResult.Warnings.AddRange(syncValidation.Warnings);

            if (syncValidation.Errors.Any())
            {
                validationResult.IsValid = false;
                LogSafe("ERROR", $"Array synchronization errors: {syncValidation.Errors.Count} found");
            }

            // Phase 6: Device-level validation
            var validDevices = new List<SkySettings>();
            var deviceNumbers = new HashSet<int>();

            foreach (var device in devices)
            {
                var deviceValidation = ValidateDevice(device, deviceNumbers);

                if (deviceValidation.HasErrors)
                {
                    validationResult.IsValid = false;
                    validationResult.Errors.AddRange(deviceValidation.Errors);

                    LogSafe("ERROR", $"Device {device.DeviceNumber} validation failed: {deviceValidation.Errors.Count} errors");

                    // Quarantine invalid device (don't add to validDevices list)
                    continue;
                }

                if (deviceValidation.HasWarnings)
                {
                    validationResult.Warnings.AddRange(deviceValidation.Warnings);
                }

                validDevices.Add(device);
                deviceNumbers.Add(device.DeviceNumber);
            }

            LogSafe("INFO", $"Loaded {validDevices.Count} valid device(s), quarantined {devices.Count - validDevices.Count} invalid device(s)");

            // Return only valid devices (invalid devices are quarantined)
            return validDevices;
        }

        /// <summary>
        /// Validates current settings without loading devices
        /// </summary>
        public ValidationResult ValidateSettings()
        {
            GetAllDevices(out var validationResult);
            return validationResult;
        }

        /// <summary>
        /// Gets the full path to the user settings file
        /// </summary>
        public string GetUserSettingsPath()
        {
            return GetUserSettingsPath(CurrentVersion);
        }

        /// <summary>
        /// Validates individual device configuration
        /// </summary>
        private ValidationResult ValidateDevice(SkySettings device, HashSet<int> existingDeviceNumbers)
        {
            var result = new ValidationResult { IsValid = true };

            // Rule 1: DeviceNumber uniqueness
            if (existingDeviceNumbers.Contains(device.DeviceNumber))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DUPLICATE_DEVICE_NUMBER",
                    Severity = "error",
                    DeviceNumber = device.DeviceNumber,
                    Message = $"DeviceNumber {device.DeviceNumber} appears multiple times in Devices array.",
                    Resolution = "Remove duplicate device entries via Device Management UI.",
                    IsAutoRepairable = false
                });
            }

            // Rule 2: DeviceNumber >= 0
            if (device.DeviceNumber < 0)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "NEGATIVE_DEVICE_NUMBER",
                    Severity = "error",
                    DeviceNumber = device.DeviceNumber,
                    Message = $"DeviceNumber must be >= 0, found: {device.DeviceNumber}",
                    Resolution = "Edit device configuration to set valid DeviceNumber.",
                    IsAutoRepairable = false
                });
            }

            // Rule 3: DeviceName required
            if (string.IsNullOrWhiteSpace(device.DeviceName))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "MISSING_DEVICE_NAME",
                    Severity = "error",
                    DeviceNumber = device.DeviceNumber,
                    Message = "DeviceName is required but was null or empty.",
                    Resolution = "Use 'Repair Settings' to auto-generate device name.",
                    IsAutoRepairable = true
                });
            }

            // Rule 4: AlignmentMode validation (property is string containing enum name)
            if (string.IsNullOrEmpty(device.AlignmentMode) || !Enum.TryParse<AlignmentMode>(device.AlignmentMode, out _))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "INVALID_ALIGNMENT_MODE",
                    Severity = "error",
                    DeviceNumber = device.DeviceNumber,
                    Message = $"AlignmentMode '{device.AlignmentMode}' is not valid. Expected: AltAz, GermanPolar, or Polar.",
                    Resolution = "Use 'Repair Settings' to reset to default (GermanPolar).",
                    IsAutoRepairable = true
                });
            }

            // Rule 5: Mount validation (property is string, just check if null/empty)
            if (string.IsNullOrEmpty(device.Mount))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "MISSING_MOUNT_TYPE",
                    Severity = "error",
                    DeviceNumber = device.DeviceNumber,
                    Message = "Mount type is required but was null or empty.",
                    Resolution = "Use 'Repair Settings' to reset to default (Simulator).",
                    IsAutoRepairable = true
                });
            }

            return result;
        }

        /// <summary>
        /// Safe logging that works during early initialization (before MonitorQueue starts)
        /// </summary>
        private void LogSafe(string level, string message)
        {
            try
            {
                // Try ASCOM logging (standard pattern for this project)
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
                // Fallback to Console for very early startup
                var prefix = level switch
                {
                    "ERROR" => "❌ ERROR",
                    "WARNING" => "⚠️ WARNING",
                    _ => "ℹ️ INFO"
                };
                Console.WriteLine($"{prefix} [VersionedSettingsService]: {message}");
            }
        }

        /// <summary>
        /// Gets Alpaca device discovery metadata for all configured devices.
        /// Returns list from AlpacaDevices array with DeviceNumber, DeviceName, DeviceType, and UniqueId.
        /// </summary>
        public List<AlpacaDevice> GetAlpacaDevices()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            // If no user settings file exists, create default
            if (!File.Exists(userSettingsPath))
            {
                CreateDefaultUserSettings(CurrentVersion);
                // Return default AlpacaDevice for device 0
                return new List<AlpacaDevice>
                {
                    new AlpacaDevice
                    {
                        DeviceNumber = 0,
                        DeviceName = "Telescope",
                        DeviceType = "Telescope",
                        UniqueId = Guid.NewGuid().ToString()
                    }
                };
            }

            try
            {
                var json = File.ReadAllText(userSettingsPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (doc == null || !doc.ContainsKey("AlpacaDevices"))
                {
                    ASCOM.Alpaca.Logging.LogWarning("AlpacaDevices array not found in settings file");
                    return new List<AlpacaDevice>();
                }

                var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();
                if (alpacaDevices == null)
                {
                    ASCOM.Alpaca.Logging.LogWarning("Failed to deserialize AlpacaDevices array");
                    return new List<AlpacaDevice>();
                }

                ASCOM.Alpaca.Logging.LogVerbose($"Loaded {alpacaDevices.Count} AlpacaDevice(s)");
                return alpacaDevices;
            }
            catch (Exception ex)
            {
                ASCOM.Alpaca.Logging.LogError($"Error reading AlpacaDevices from settings file: {ex.Message}");
                return new List<AlpacaDevice>();
            }
        }

        /// <summary>
        /// Phase 3: Saves all device settings to Devices array
        /// </summary>
        private async Task SaveAllDevicesAsync(List<SkySettings> devices)
        {
            await _fileLock.WaitAsync();
            try
            {
                var userSettingsPath = GetUserSettingsPath(CurrentVersion);

                var userSettings = new Dictionary<string, JsonElement>
                {
                    ["Devices"] = JsonSerializer.SerializeToElement(devices),
                    ["Version"] = JsonSerializer.SerializeToElement(CurrentVersion),
                    ["LastModified"] = JsonSerializer.SerializeToElement(DateTime.UtcNow)
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(userSettings, options);
                await File.WriteAllTextAsync(userSettingsPath, json);

                // Update version file
                await File.WriteAllTextAsync(_versionFile, CurrentVersion);

                ASCOM.Alpaca.Logging.LogVerbose($"Saved {devices.Count} device(s) to version {CurrentVersion}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        /// <summary>
        /// Phase 3: Creates a default device configuration
        /// </summary>
        private SkySettings CreateDefaultDevice()
        {
            var settings = new SkySettings();

            // Phase 3 baseline (v1.0.0+): Read from Devices array, device 0
            var devicesSection = _configuration.GetSection("Devices:0");
            if (devicesSection.Exists())
            {
                devicesSection.Bind(settings);
            }
            else
            {
                ASCOM.Alpaca.Logging.LogWarning("Devices:0 section not found in appsettings.json - using hardcoded defaults");
            }

            // Ensure device identification properties are set (Phase 3 required properties)
            settings.DeviceNumber = 0;
            if (string.IsNullOrEmpty(settings.DeviceName))
            {
                settings.DeviceName = "Telescope";
            }
            settings.Enabled = true;

            return settings;
        }

        public async Task SaveSettingsAsync(SkySettings settings)
        {
            // Phase 3: Update the specific device in the Devices array
            await _fileLock.WaitAsync();
            try
            {
                var devices = GetAllDevices();

                // Find and update the matching device by DeviceNumber
                var existingIndex = devices.FindIndex(d => d.DeviceNumber == settings.DeviceNumber);

                if (existingIndex >= 0)
                {
                    devices[existingIndex] = settings;
                    ASCOM.Alpaca.Logging.LogVerbose($"Updated device {settings.DeviceNumber} ({settings.DeviceName})");
                }
                else
                {
                    // Add new device
                    devices.Add(settings);
                    ASCOM.Alpaca.Logging.LogVerbose($"Added new device {settings.DeviceNumber} ({settings.DeviceName})");
                }

                await SaveAllDevicesAsync(devices);

                // Ensure AlpacaDevices entry synchronized
                await _syncService.EnsureAlpacaDeviceEntryAsync(
                    settings.DeviceNumber,
                    settings.DeviceName);

                // Raise settings changed event
                SettingsChanged?.Invoke(this, settings);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public MonitorSettings GetMonitorSettings()
        {
            var settings = new MonitorSettings();
            _configuration.GetSection("MonitorSettings").Bind(settings);
            return settings;
        }

        public async Task SaveMonitorSettingsAsync(MonitorSettings settings)
        {
            await _fileLock.WaitAsync();
            try
            {
                var userSettingsPath = GetUserSettingsPath(CurrentVersion);
                
                var userSettings = await ReadUserSettingsFileAsync(userSettingsPath);
                userSettings["MonitorSettings"] = JsonSerializer.SerializeToElement(settings);
                
                // Update version and timestamp if not already present
                if (!userSettings.ContainsKey("Version"))
                {
                    userSettings["Version"] = JsonSerializer.SerializeToElement(CurrentVersion);
                }
                userSettings["LastModified"] = JsonSerializer.SerializeToElement(DateTime.UtcNow);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(userSettings, options);
                await File.WriteAllTextAsync(userSettingsPath, json);

                // Update version file
                await File.WriteAllTextAsync(_versionFile, CurrentVersion);

                ASCOM.Alpaca.Logging.LogVerbose($"Monitor settings saved to version {CurrentVersion}");

                // Raise settings changed event
                MonitorSettingsChanged?.Invoke(this, settings);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> MigrateFromPreviousVersionAsync()
        {
            var versions = GetAvailableVersions()
                .Where(v => v != CurrentVersion)
                .OrderByDescending(v => new Version(v))
                .ToList();

            if (!versions.Any())
            {
                ASCOM.Alpaca.Logging.LogVerbose("No previous versions found for migration");
                return false;
            }

            var previousVersion = versions.First();

            // Phase 3 baseline (v1.0.0): Reject pre-1.0.0 versions
            var previousVersionParsed = new Version(previousVersion);
            var baselineVersion = new Version(1, 0, 0);
            if (previousVersionParsed < baselineVersion)
            {
                ASCOM.Alpaca.Logging.LogError(
                    $"Cannot migrate from pre-1.0.0 version {previousVersion}. Version 1.0.0 is the baseline. " +
                    $"Please delete the old settings file at {GetUserSettingsPath(previousVersion)} and restart the application to create a new configuration.");
                return false;
            }

            var previousSettingsPath = GetUserSettingsPath(previousVersion);

            if (!File.Exists(previousSettingsPath))
            {
                return false;
            }

            try
            {
                ASCOM.Alpaca.Logging.LogVerbose($"Migrating settings from {previousVersion} to {CurrentVersion}");

                // Read previous version settings
                var previousJson = await File.ReadAllTextAsync(previousSettingsPath);
                var previousSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(previousJson);

                if (previousSettings == null)
                {
                    return false;
                }

                // Phase 3 baseline: All v1.0.0+ versions MUST have Devices array
                if (!previousSettings.ContainsKey("Devices"))
                {
                    ASCOM.Alpaca.Logging.LogError(
                        $"Previous version {previousVersion} settings file is missing 'Devices' array (invalid format). " +
                        $"Please delete {previousSettingsPath} and restart to create a new configuration.");
                    return false;
                }

                var devicesToMigrate = previousSettings["Devices"].Deserialize<List<SkySettings>>();
                if (devicesToMigrate == null || !devicesToMigrate.Any())
                {
                    ASCOM.Alpaca.Logging.LogWarning("No devices found in previous version");
                    return false;
                }

                // Apply version-specific migrations to each device
                var migratedDevices = devicesToMigrate
                    .Select(d => ApplyMigrations(d, previousVersion, CurrentVersion))
                    .ToList();

                // Save all migrated devices
                await SaveAllDevicesAsync(migratedDevices);

                ASCOM.Alpaca.Logging.LogVerbose($"Successfully migrated {migratedDevices.Count} device(s) from version {previousVersion}");

                return true;
            }
            catch (Exception ex)
            {
                ASCOM.Alpaca.Logging.LogError($"Failed to migrate settings from version {previousVersion}: {ex.Message}");
                return false;
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            if (File.Exists(userSettingsPath))
            {
                // Backup before deleting
                var backupPath = userSettingsPath + ".backup";
                File.Copy(userSettingsPath, backupPath, true);
                File.Delete(userSettingsPath);
            }

            CreateDefaultUserSettings(CurrentVersion);

            ASCOM.Alpaca.Logging.LogVerbose($"Settings reset to defaults for version {CurrentVersion}");

            // Reload settings and notify
            var settings = GetSettings();
            SettingsChanged?.Invoke(this, settings);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs automatic repair of common settings errors
        /// Creates backup before repair, restores on failure
        /// </summary>
        public async Task<RepairResult> RepairSettingsAsync()
        {
            var result = new RepairResult();

            await _fileLock.WaitAsync();
            try
            {
                var userSettingsPath = GetUserSettingsPath(CurrentVersion);

                // Step 1: Create backup
                var backupPath = CreateBackup(userSettingsPath);
                result.BackupPath = backupPath;

                LogSafe("INFO", $"Created backup: {backupPath}");

                // Step 2: Load current settings (without validation to see raw data)
                if (!File.Exists(userSettingsPath))
                {
                    result.Success = false;
                    result.Message = "Settings file not found. Cannot repair.";
                    return result;
                }

                string jsonContent;
                try
                {
                    jsonContent = File.ReadAllText(userSettingsPath);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Cannot read settings file: {ex.Message}";
                    return result;
                }

                Dictionary<string, JsonElement> doc;
                try
                {
                    doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent) 
                        ?? new Dictionary<string, JsonElement>();
                }
                catch (JsonException ex)
                {
                    result.Success = false;
                    result.Message = $"Settings file contains invalid JSON: {ex.Message}. Cannot auto-repair.";
                    return result;
                }

                // Step 3: Repair Devices array
                List<SkySettings> devices;
                if (!doc.ContainsKey("Devices"))
                {
                    devices = new List<SkySettings> { CreateDefaultDevice() };
                    result.ActionsPerformed.Add("Created Devices array (was missing)");
                }
                else
                {
                    try
                    {
                        devices = doc["Devices"].Deserialize<List<SkySettings>>() ?? new List<SkySettings>();
                    }
                    catch
                    {
                        devices = new List<SkySettings> { CreateDefaultDevice() };
                        result.ActionsPerformed.Add("Regenerated Devices array (was corrupted)");
                    }
                }

                if (!devices.Any())
                {
                    devices.Add(CreateDefaultDevice());
                    result.ActionsPerformed.Add("Added default device (Devices array was empty)");
                }

                // Step 4: Fix device-level issues
                foreach (var device in devices)
                {
                    var changed = false;

                    // Fix missing DeviceName
                    if (string.IsNullOrWhiteSpace(device.DeviceName))
                    {
                        device.DeviceName = $"Telescope {device.DeviceNumber}";
                        result.ActionsPerformed.Add($"Set DeviceName for device {device.DeviceNumber}");
                        changed = true;
                    }

                    // Fix invalid AlignmentMode (property is string, not enum)
                    if (string.IsNullOrEmpty(device.AlignmentMode) || !Enum.TryParse<AlignmentMode>(device.AlignmentMode, out _))
                    {
                        device.AlignmentMode = AlignmentMode.GermanPolar.ToString();
                        result.ActionsPerformed.Add($"Reset AlignmentMode for device {device.DeviceNumber} to GermanPolar");
                        changed = true;
                    }

                    // Fix invalid Mount (property is string, validation logic needs Mount enum type)
                    if (string.IsNullOrEmpty(device.Mount))
                    {
                        device.Mount = "Simulator";
                        result.ActionsPerformed.Add($"Set Mount for device {device.DeviceNumber} to Simulator (was null/empty)");
                        changed = true;
                    }
                    // Note: Mount enum validation happens in ValidateDevice(), which checks enum validity
                    // If we need to validate Mount enum values, we'd need to know the Mount enum type
                    // For now, we just ensure it's not null/empty

                    if (changed)
                    {
                        result.DevicesRepaired++;
                    }
                }

                // Step 5: Regenerate AlpacaDevices array from Devices
                var alpacaDevices = new List<AlpacaDevice>();
                foreach (var device in devices)
                {
                    alpacaDevices.Add(new AlpacaDevice
                    {
                        DeviceNumber = device.DeviceNumber,
                        DeviceName = device.DeviceName ?? $"Telescope {device.DeviceNumber}",
                        DeviceType = "Telescope",
                        UniqueId = _syncService.GenerateUniqueId()
                    });
                }

                result.ActionsPerformed.Add($"Regenerated AlpacaDevices array ({alpacaDevices.Count} entries)");

                // Step 6: Write repaired settings
                doc["Devices"] = JsonSerializer.SerializeToElement(devices);
                doc["AlpacaDevices"] = JsonSerializer.SerializeToElement(alpacaDevices);
                doc["Version"] = JsonSerializer.SerializeToElement(CurrentVersion);
                doc["LastModified"] = JsonSerializer.SerializeToElement(DateTime.UtcNow);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var repairedJson = JsonSerializer.Serialize(doc, options);
                await File.WriteAllTextAsync(userSettingsPath, repairedJson);

                // Step 7: Validate repaired settings
                var validation = GetAllDevices(out var validationResult);

                if (validationResult.IsValid)
                {
                    result.Success = true;
                    result.Message = $"Settings repaired successfully. {result.ActionsPerformed.Count} actions performed.";
                    LogSafe("INFO", result.Message);
                }
                else
                {
                    result.Success = false;
                    result.Message = "Repair partially successful but validation still has errors.";
                    result.RemainingErrors = validationResult.Errors;

                    LogSafe("WARNING", $"Repair completed but {validationResult.Errors.Count} errors remain");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Repair failed: {ex.Message}";

                LogSafe("ERROR", $"Repair operation failed: {ex.Message}");

                // Restore backup
                if (!string.IsNullOrEmpty(result.BackupPath) && File.Exists(result.BackupPath))
                {
                    var userSettingsPath = GetUserSettingsPath(CurrentVersion);
                    File.Copy(result.BackupPath, userSettingsPath, true);
                    result.Message += " Backup restored.";
                    LogSafe("INFO", "Backup restored after repair failure");
                }
            }
            finally
            {
                _fileLock.Release();
            }

            return result;
        }

        /// <summary>
        /// Creates a timestamped backup of the settings file
        /// </summary>
        private string CreateBackup(string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                return string.Empty;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = settingsPath + $".backup-{timestamp}";
            File.Copy(settingsPath, backupPath, true);
            return backupPath;
        }

        private void InitializeVersionedSettings()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            if (File.Exists(userSettingsPath))
            {
                ASCOM.Alpaca.Logging.LogVerbose($"Using existing settings for version {CurrentVersion}");
                return;
            }

            // Check if migration is needed
            if (File.Exists(_versionFile))
            {
                var lastVersion = File.ReadAllText(_versionFile).Trim();
                if (lastVersion != CurrentVersion)
                {
                    ASCOM.Alpaca.Logging.LogVerbose($"Version change detected: {lastVersion} ? {CurrentVersion}");
                }
            }

            // Create initial settings from defaults
            CreateDefaultUserSettings(CurrentVersion);
        }

        private void CreateDefaultUserSettings(string version)
        {
            var userSettingsPath = GetUserSettingsPath(version);

            // Phase 3 baseline (v1.0.0+): Create default settings with Devices array format
            var defaultDevice = CreateDefaultDevice();
            var devices = new List<SkySettings> { defaultDevice };

            var defaultSettings = new
            {
                AlpacaDevices = new[]
                {
                    new
                    {
                        DeviceNumber = 0,
                        DeviceName = defaultDevice.DeviceName,
                        DeviceType = "Telescope",
                        UniqueId = Guid.NewGuid().ToString()
                    }
                },
                Devices = devices,
                Version = version,
                CreatedDate = DateTime.UtcNow
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(defaultSettings, options);
            File.WriteAllText(userSettingsPath, json);

            ASCOM.Alpaca.Logging.LogVerbose($"Created default settings with AlpacaDevices and Devices arrays for version {version}");
        }

        private SkySettings ApplyMigrations(SkySettings settings, string fromVersion, string toVersion)
        {
            // Version-specific migration logic
            var from = new Version(fromVersion);
            var to = new Version(toVersion);

            // Example: Migrate from 1.0 to 1.1
            if (from < new Version("1.1.0") && to >= new Version("1.1.0"))
            {
                ASCOM.Alpaca.Logging.LogVerbose("Applying 1.0 ? 1.1 migration");
                // Example: New setting added in 1.1
                // if (settings.NewProperty == default) settings.NewProperty = defaultValue;
            }

            // Example: Migrate from 1.1 to 2.0
            if (from < new Version("2.0.0") && to >= new Version("2.0.0"))
            {
                ASCOM.Alpaca.Logging.LogVerbose("Applying 1.1 ? 2.0 migration");
                // Example: Breaking changes in 2.0
            }

            return settings;
        }

        private string GetUserSettingsPath(string version)
        {
            var versionPath = Path.Combine(_appDataRoot, version);
            Directory.CreateDirectory(versionPath);
            return Path.Combine(versionPath, "appsettings.user.json");
        }

        private async Task<Dictionary<string, JsonElement>> ReadUserSettingsFileAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, JsonElement>();
            }

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }

        private string[] GetAvailableVersions()
        {
            if (!Directory.Exists(_appDataRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(_appDataRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && Version.TryParse(name, out _))
                .ToArray()!;
        }

        private string GetAssemblyVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            
            // Try to get informational version first (includes pre-release tags)
            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;
            
            var informationalVersion = infoVersionAttr?.InformationalVersion;
            
            if (!string.IsNullOrEmpty(informationalVersion))
            {
                // Remove any build metadata (e.g., +commitHash)
                var plusIndex = informationalVersion.IndexOf('+');
                if (plusIndex > 0)
                {
                    informationalVersion = informationalVersion.Substring(0, plusIndex);
                }
                return informationalVersion;
            }
            
            // Fall back to assembly version
            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }

        /// <summary>
        /// Phase 4.2: Extract version from custom settings path
        /// </summary>
        private string GetVersionFromPath(string path)
        {
            try
            {
                // Try to extract version from path like: .../GreenSwampAlpaca/1.2.3/telescope-0.json
                var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(parts[i]) && Version.TryParse(parts[i], out _))
                    {
                        return parts[i];
                    }
                }
            }
            catch (Exception ex)
            {
                ASCOM.Alpaca.Logging.LogWarning($"Could not parse version from path: {path} - {ex.Message}");
            }

            // Fall back to current assembly version
            return GetAssemblyVersion();
        }
    }
}
