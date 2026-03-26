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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Service for maintaining 1-to-1 synchronization between AlpacaDevices and Devices arrays.
    /// Ensures atomic operations for device CRUD to prevent data inconsistency.
    /// Uses ASCOM.Alpaca.Logging for consistent logging across the application.
    /// </summary>
    public class DeviceSynchronizationService : IDeviceSynchronizationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _userSettingsPath;

        public DeviceSynchronizationService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _userSettingsPath = GetUserSettingsPath();

            ASCOM.Alpaca.Logging.LogVerbose("DeviceSynchronizationService initialized");
            ASCOM.Alpaca.Logging.LogVerbose($"User settings path: {_userSettingsPath}");
        }

        /// <summary>
        /// Ensures AlpacaDevices entry exists for device.
        /// Creates if missing, updates DeviceName if changed.
        /// </summary>
        public async Task EnsureAlpacaDeviceEntryAsync(int deviceNumber, string deviceName)
        {
            if (!File.Exists(_userSettingsPath))
            {
                ASCOM.Alpaca.Logging.LogWarning($"User settings file not found: {_userSettingsPath}");
                return;
            }

            var json = await File.ReadAllTextAsync(_userSettingsPath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (doc == null)
            {
                ASCOM.Alpaca.Logging.LogError("Failed to parse user settings JSON");
                return;
            }

            // Get or create AlpacaDevices array
            var alpacaDevices = doc.ContainsKey("AlpacaDevices")
                ? doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>() ?? new List<AlpacaDevice>()
                : new List<AlpacaDevice>();

            // Check if entry exists
            var existing = alpacaDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);

            if (existing == null)
            {
                // Create new entry
                var newEntry = new AlpacaDevice
                {
                    DeviceNumber = deviceNumber,
                    DeviceName = deviceName,
                    DeviceType = "Telescope",
                    UniqueId = GenerateUniqueId()
                };
                alpacaDevices.Add(newEntry);

                ASCOM.Alpaca.Logging.LogVerbose($"Created AlpacaDevice entry for device {deviceNumber}: {deviceName}");
            }
            else if (existing.DeviceName != deviceName)
            {
                // Update device name if changed
                existing.DeviceName = deviceName;

                ASCOM.Alpaca.Logging.LogVerbose($"Updated AlpacaDevice name for device {deviceNumber}: {deviceName}");
            }
            else
            {
                // Already synchronized
                return;
            }

            // Write updated array back to file
            doc["AlpacaDevices"] = JsonSerializer.SerializeToElement(alpacaDevices);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(doc, options);
            await File.WriteAllTextAsync(_userSettingsPath, updatedJson);

            ASCOM.Alpaca.Logging.LogVerbose("AlpacaDevices array updated successfully");
        }

        /// <summary>
        /// Removes AlpacaDevices entry when device deleted.
        /// </summary>
        public async Task RemoveAlpacaDeviceEntryAsync(int deviceNumber)
        {
            if (!File.Exists(_userSettingsPath))
            {
                ASCOM.Alpaca.Logging.LogWarning($"User settings file not found: {_userSettingsPath}");
                return;
            }

            var json = await File.ReadAllTextAsync(_userSettingsPath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (doc == null || !doc.ContainsKey("AlpacaDevices"))
            {
                ASCOM.Alpaca.Logging.LogWarning("AlpacaDevices array not found in settings file");
                return;
            }

            var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();
            if (alpacaDevices == null)
            {
                ASCOM.Alpaca.Logging.LogWarning("Failed to deserialize AlpacaDevices array");
                return;
            }

            var removed = alpacaDevices.RemoveAll(d => d.DeviceNumber == deviceNumber);

            if (removed > 0)
            {
                doc["AlpacaDevices"] = JsonSerializer.SerializeToElement(alpacaDevices);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(doc, options);
                await File.WriteAllTextAsync(_userSettingsPath, updatedJson);

                ASCOM.Alpaca.Logging.LogVerbose($"Removed AlpacaDevice entry for device {deviceNumber}");
            }
        }

        /// <summary>
        /// Validates 1-to-1 synchronization on startup.
        /// Fails fast if arrays out of sync.
        /// </summary>
        public bool ValidateSynchronization(Dictionary<string, JsonElement> doc)
        {
            if (!doc.ContainsKey("Devices") || !doc.ContainsKey("AlpacaDevices"))
            {
                ASCOM.Alpaca.Logging.LogWarning("Missing Devices or AlpacaDevices array");
                return false;
            }

            var devices = doc["Devices"].Deserialize<List<SkySettings>>();
            var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();

            if (devices == null || alpacaDevices == null)
            {
                ASCOM.Alpaca.Logging.LogError("Failed to deserialize Devices or AlpacaDevices arrays");
                return false;
            }

            var deviceNumbers = devices.Select(d => d.DeviceNumber).ToHashSet();
            var alpacaNumbers = alpacaDevices.Select(d => d.DeviceNumber).ToHashSet();

            // Check 1-to-1 mapping
            var onlyInDevices = deviceNumbers.Except(alpacaNumbers).ToList();
            var onlyInAlpaca = alpacaNumbers.Except(deviceNumbers).ToList();

            if (onlyInDevices.Any() || onlyInAlpaca.Any())
            {
                ASCOM.Alpaca.Logging.LogError(
                    $"AlpacaDevices/Devices arrays out of sync. " +
                    $"Only in Devices: [{string.Join(",", onlyInDevices)}], " +
                    $"Only in Alpaca: [{string.Join(",", onlyInAlpaca)}]");
                return false;
            }

            // Check DeviceName synchronization
            foreach (var device in devices)
            {
                var alpacaEntry = alpacaDevices.FirstOrDefault(a => a.DeviceNumber == device.DeviceNumber);

                if (alpacaEntry != null && alpacaEntry.DeviceName != device.DeviceName)
                {
                    ASCOM.Alpaca.Logging.LogWarning(
                        $"DeviceName mismatch for device {device.DeviceNumber}: " +
                        $"Devices='{device.DeviceName}', AlpacaDevices='{alpacaEntry.DeviceName}'");
                }
            }

            ASCOM.Alpaca.Logging.LogVerbose("AlpacaDevices/Devices synchronization validated successfully");
            return true;
        }

        /// <summary>
        /// Validates 1-to-1 synchronization with detailed error reporting
        /// </summary>
        public ValidationResult ValidateSynchronizationDetailed(Dictionary<string, JsonElement> doc)
        {
            var result = new ValidationResult { IsValid = true };

            // Check both arrays exist
            if (!doc.ContainsKey("Devices"))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "MISSING_DEVICES_ARRAY",
                    Severity = "error",
                    Message = "Settings file missing 'Devices' array.",
                    Resolution = "Use 'Repair Settings' to regenerate.",
                    IsAutoRepairable = true
                });
                return result;
            }

            if (!doc.ContainsKey("AlpacaDevices"))
            {
                result.Warnings.Add(new ValidationError
                {
                    ErrorCode = "MISSING_ALPACA_ARRAY",
                    Severity = "warning",
                    Message = "Settings file missing 'AlpacaDevices' array.",
                    Resolution = "Use 'Repair Settings' to regenerate from Devices array.",
                    IsAutoRepairable = true
                });
                return result; // Can continue with just Devices array
            }

            var devices = doc["Devices"].Deserialize<List<SkySettings>>();
            var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();

            if (devices == null || alpacaDevices == null)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "INVALID_ARRAY_TYPE",
                    Severity = "error",
                    Message = "Failed to deserialize Devices or AlpacaDevices arrays.",
                    Resolution = "Use 'Repair Settings' to regenerate arrays.",
                    IsAutoRepairable = true
                });
                return result;
            }

            var deviceNumbers = devices.Select(d => d.DeviceNumber).ToList();
            var alpacaNumbers = alpacaDevices.Select(d => d.DeviceNumber).ToList();

            // Check count mismatch
            if (deviceNumbers.Count != alpacaNumbers.Count)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "ARRAY_COUNT_MISMATCH",
                    Severity = "error",
                    Message = $"Devices array has {deviceNumbers.Count} entries, AlpacaDevices has {alpacaNumbers.Count}.",
                    Resolution = "Use 'Repair Settings' to regenerate AlpacaDevices array.",
                    IsAutoRepairable = true
                });
            }

            // Check missing in AlpacaDevices
            var missingInAlpaca = deviceNumbers.Except(alpacaNumbers).ToList();
            foreach (var deviceNum in missingInAlpaca)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "DEVICE_NOT_IN_ALPACA",
                    Severity = "error",
                    DeviceNumber = deviceNum,
                    Message = $"Device {deviceNum} exists in Devices but not in AlpacaDevices.",
                    Resolution = "Use 'Repair Settings' to add missing AlpacaDevices entry.",
                    IsAutoRepairable = true
                });
            }

            // Check orphaned in AlpacaDevices
            var orphanedInAlpaca = alpacaNumbers.Except(deviceNumbers).ToList();
            foreach (var deviceNum in orphanedInAlpaca)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    ErrorCode = "ALPACA_NOT_IN_DEVICES",
                    Severity = "error",
                    DeviceNumber = deviceNum,
                    Message = $"AlpacaDevices entry {deviceNum} has no matching Device entry.",
                    Resolution = "Use 'Repair Settings' to remove orphaned AlpacaDevices entry.",
                    IsAutoRepairable = true
                });
            }

            // Check DeviceName synchronization
            foreach (var device in devices)
            {
                var alpacaEntry = alpacaDevices.FirstOrDefault(a => a.DeviceNumber == device.DeviceNumber);
                if (alpacaEntry != null && alpacaEntry.DeviceName != device.DeviceName)
                {
                    result.Warnings.Add(new ValidationError
                    {
                        ErrorCode = "DEVICE_NAME_MISMATCH",
                        Severity = "warning",
                        DeviceNumber = device.DeviceNumber,
                        Message = $"DeviceName mismatch: Devices='{device.DeviceName}', AlpacaDevices='{alpacaEntry.DeviceName}'.",
                        Resolution = "Use 'Repair Settings' to synchronize names.",
                        IsAutoRepairable = true
                    });
                }
            }

            // Check UniqueId presence
            foreach (var alpacaEntry in alpacaDevices)
            {
                if (string.IsNullOrWhiteSpace(alpacaEntry.UniqueId))
                {
                    result.Warnings.Add(new ValidationError
                    {
                        ErrorCode = "MISSING_UNIQUE_ID",
                        Severity = "warning",
                        DeviceNumber = alpacaEntry.DeviceNumber,
                        Message = $"AlpacaDevice entry {alpacaEntry.DeviceNumber} missing UniqueId.",
                        Resolution = "Use 'Repair Settings' to generate UniqueId.",
                        IsAutoRepairable = true
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Generates unique GUID for new device.
        /// </summary>
        public string GenerateUniqueId()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Gets user settings path from configuration.
        /// </summary>
        private string GetUserSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var version = GetVersion();
            return Path.Combine(appData, "GreenSwampAlpaca", version, "appsettings.user.json");
        }

        /// <summary>
        /// Gets application version from assembly attributes.
        /// </summary>
        private string GetVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;

            var version = infoVersionAttr?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "1.0.0";

            // Remove build metadata (e.g., +commitHash)
            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }

            return version;
        }
    }

    /// <summary>
    /// Represents Alpaca device discovery metadata.
    /// </summary>
    public class AlpacaDevice
    {
        public int DeviceNumber { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = "Telescope";
        public string UniqueId { get; set; } = string.Empty;
    }
}
