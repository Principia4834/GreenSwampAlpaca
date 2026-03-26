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
using Microsoft.Extensions.Logging;
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
        private readonly ILogger? _logger;
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
            IDeviceSynchronizationService syncService,
            ILogger? logger = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _logger = logger;

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
        /// <param name="logger">Logger instance</param>
        /// <param name="customSettingsPath">Custom path to settings file (overrides default)</param>
        /// <param name="syncService">Device synchronization service</param>
        public VersionedSettingsService(
            IConfiguration configuration,
            ILogger? logger,
            string customSettingsPath,
            IDeviceSynchronizationService syncService)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _logger = logger;
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

                _logger?.LogInformation("Phase 4.2: Using custom settings path: {Path}", _customSettingsPath);
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
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            // If no user settings file exists, create default with single device
            if (!File.Exists(userSettingsPath))
            {
                CreateDefaultUserSettings(CurrentVersion);
                return new List<SkySettings> { CreateDefaultDevice() };
            }

            try
            {
                var json = File.ReadAllText(userSettingsPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                // Phase 3 baseline: Strict validation - DO NOT overwrite existing files
                if (doc == null || !doc.ContainsKey("Devices"))
                {
                    throw new InvalidOperationException(
                        $"Settings file at '{userSettingsPath}' is missing the required 'Devices' array " +
                        $"(invalid format for v1.0.0+). Please delete this file and restart the application " +
                        $"to create a new configuration with the correct format.");
                }

                var devices = doc["Devices"].Deserialize<List<SkySettings>>();
                if (devices == null || !devices.Any())
                {
                    throw new InvalidOperationException(
                        $"Settings file at '{userSettingsPath}' has an empty 'Devices' array. " +
                        $"Please delete this file and restart the application to create a new " +
                        $"configuration with a default device.");
                }

                // Validate 1-to-1 synchronization with AlpacaDevices
                if (!_syncService.ValidateSynchronization(doc)) 
                {
                     throw new InvalidOperationException(
                        $"AlpacaDevices/Devices arrays out of sync in '{userSettingsPath}'. " +
                        $"Please check the console log for details or delete this file to regenerate.");
                }

                _logger?.LogInformation("Loaded {Count} device(s)", devices.Count);
                return devices;
            }
            catch (InvalidOperationException)
            {
                // Re-throw validation errors unchanged
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error reading settings file at '{userSettingsPath}': {ex.Message}. " +
                    $"The file may be corrupted. Please delete this file and restart the application " +
                    $"to create a new configuration.", ex);
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
                    _logger?.LogWarning("AlpacaDevices array not found in settings file");
                    return new List<AlpacaDevice>();
                }

                var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();
                if (alpacaDevices == null)
                {
                    _logger?.LogWarning("Failed to deserialize AlpacaDevices array");
                    return new List<AlpacaDevice>();
                }

                _logger?.LogInformation("Loaded {Count} AlpacaDevice(s)", alpacaDevices.Count);
                return alpacaDevices;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading AlpacaDevices from settings file");
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

                _logger?.LogInformation("Saved {Count} device(s) to version {Version}", devices.Count, CurrentVersion);
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
                _logger?.LogWarning("Devices:0 section not found in appsettings.json - using hardcoded defaults");
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
                    _logger?.LogInformation("Updated device {DeviceNumber} ({DeviceName})", 
                        settings.DeviceNumber, settings.DeviceName);
                }
                else
                {
                    // Add new device
                    devices.Add(settings);
                    _logger?.LogInformation("Added new device {DeviceNumber} ({DeviceName})", 
                        settings.DeviceNumber, settings.DeviceName);
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

                _logger?.LogInformation("Monitor settings saved to version {Version}", CurrentVersion);
                
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
                _logger?.LogInformation("No previous versions found for migration");
                return false;
            }

            var previousVersion = versions.First();

            // Phase 3 baseline (v1.0.0): Reject pre-1.0.0 versions
            var previousVersionParsed = new Version(previousVersion);
            var baselineVersion = new Version(1, 0, 0);
            if (previousVersionParsed < baselineVersion)
            {
                _logger?.LogError(
                    "Cannot migrate from pre-1.0.0 version {Previous}. Version 1.0.0 is the baseline. " +
                    "Please delete the old settings file at {Path} and restart the application to create a new configuration.",
                    previousVersion, GetUserSettingsPath(previousVersion));
                return false;
            }

            var previousSettingsPath = GetUserSettingsPath(previousVersion);

            if (!File.Exists(previousSettingsPath))
            {
                return false;
            }

            try
            {
                _logger?.LogInformation("Migrating settings from {Previous} to {Current}", 
                    previousVersion, CurrentVersion);

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
                    _logger?.LogError(
                        "Previous version {Previous} settings file is missing 'Devices' array (invalid format). " +
                        "Please delete {Path} and restart to create a new configuration.",
                        previousVersion, previousSettingsPath);
                    return false;
                }

                var devicesToMigrate = previousSettings["Devices"].Deserialize<List<SkySettings>>();
                if (devicesToMigrate == null || !devicesToMigrate.Any())
                {
                    _logger?.LogWarning("No devices found in previous version");
                    return false;
                }

                // Apply version-specific migrations to each device
                var migratedDevices = devicesToMigrate
                    .Select(d => ApplyMigrations(d, previousVersion, CurrentVersion))
                    .ToList();

                // Save all migrated devices
                await SaveAllDevicesAsync(migratedDevices);

                _logger?.LogInformation("Successfully migrated {Count} device(s) from version {Previous}", 
                    migratedDevices.Count, previousVersion);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to migrate settings from version {Previous}", 
                    previousVersion);
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
            
            _logger?.LogInformation("Settings reset to defaults for version {Version}", CurrentVersion);
            
            // Reload settings and notify
            var settings = GetSettings();
            SettingsChanged?.Invoke(this, settings);
            
            await Task.CompletedTask;
        }

        private void InitializeVersionedSettings()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            if (File.Exists(userSettingsPath))
            {
                _logger?.LogInformation("Using existing settings for version {Version}", CurrentVersion);
                return;
            }

            // Check if migration is needed
            if (File.Exists(_versionFile))
            {
                var lastVersion = File.ReadAllText(_versionFile).Trim();
                if (lastVersion != CurrentVersion)
                {
                    _logger?.LogInformation("Version change detected: {Old} ? {New}", 
                        lastVersion, CurrentVersion);
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

            _logger?.LogInformation("Created default settings with AlpacaDevices and Devices arrays for version {Version}", version);
        }

        private SkySettings ApplyMigrations(SkySettings settings, string fromVersion, string toVersion)
        {
            // Version-specific migration logic
            var from = new Version(fromVersion);
            var to = new Version(toVersion);

            // Example: Migrate from 1.0 to 1.1
            if (from < new Version("1.1.0") && to >= new Version("1.1.0"))
            {
                _logger?.LogInformation("Applying 1.0 ? 1.1 migration");
                // Example: New setting added in 1.1
                // if (settings.NewProperty == default) settings.NewProperty = defaultValue;
            }

            // Example: Migrate from 1.1 to 2.0
            if (from < new Version("2.0.0") && to >= new Version("2.0.0"))
            {
                _logger?.LogInformation("Applying 1.1 ? 2.0 migration");
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
                _logger?.LogWarning(ex, "Could not parse version from path: {Path}", path);
            }

            // Fall back to current assembly version
            return GetAssemblyVersion();
        }
    }
}
