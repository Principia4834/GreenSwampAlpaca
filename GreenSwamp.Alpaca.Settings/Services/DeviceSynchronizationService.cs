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
    /// Uses Console logging (Settings project has no dependency on Shared/MonitorLog).
    /// </summary>
    public class DeviceSynchronizationService : IDeviceSynchronizationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _userSettingsPath;

        public DeviceSynchronizationService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _userSettingsPath = GetUserSettingsPath();

            LogInfo("DeviceSynchronizationService initialized");
            LogInfo($"User settings path: {_userSettingsPath}");
        }

        /// <summary>
        /// Ensures AlpacaDevices entry exists for device.
        /// Creates if missing, updates DeviceName if changed.
        /// </summary>
        public async Task EnsureAlpacaDeviceEntryAsync(int deviceNumber, string deviceName)
        {
            if (!File.Exists(_userSettingsPath))
            {
                LogWarning($"User settings file not found: {_userSettingsPath}");
                return;
            }

            var json = await File.ReadAllTextAsync(_userSettingsPath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (doc == null)
            {
                LogError("Failed to parse user settings JSON");
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

                LogInfo($"Created AlpacaDevice entry for device {deviceNumber}: {deviceName}");
            }
            else if (existing.DeviceName != deviceName)
            {
                // Update device name if changed
                existing.DeviceName = deviceName;

                LogInfo($"Updated AlpacaDevice name for device {deviceNumber}: {deviceName}");
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

            LogInfo("AlpacaDevices array updated successfully");
        }

        /// <summary>
        /// Removes AlpacaDevices entry when device deleted.
        /// </summary>
        public async Task RemoveAlpacaDeviceEntryAsync(int deviceNumber)
        {
            if (!File.Exists(_userSettingsPath))
            {
                LogWarning($"User settings file not found: {_userSettingsPath}");
                return;
            }

            var json = await File.ReadAllTextAsync(_userSettingsPath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (doc == null || !doc.ContainsKey("AlpacaDevices"))
            {
                LogWarning("AlpacaDevices array not found in settings file");
                return;
            }

            var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();
            if (alpacaDevices == null)
            {
                LogWarning("Failed to deserialize AlpacaDevices array");
                return;
            }

            var removed = alpacaDevices.RemoveAll(d => d.DeviceNumber == deviceNumber);

            if (removed > 0)
            {
                doc["AlpacaDevices"] = JsonSerializer.SerializeToElement(alpacaDevices);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(doc, options);
                await File.WriteAllTextAsync(_userSettingsPath, updatedJson);

                LogInfo($"Removed AlpacaDevice entry for device {deviceNumber}");
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
                LogWarning("Missing Devices or AlpacaDevices array");
                return false;
            }

            var devices = doc["Devices"].Deserialize<List<SkySettings>>();
            var alpacaDevices = doc["AlpacaDevices"].Deserialize<List<AlpacaDevice>>();

            if (devices == null || alpacaDevices == null)
            {
                LogError("Failed to deserialize Devices or AlpacaDevices arrays");
                return false;
            }

            var deviceNumbers = devices.Select(d => d.DeviceNumber).ToHashSet();
            var alpacaNumbers = alpacaDevices.Select(d => d.DeviceNumber).ToHashSet();

            // Check 1-to-1 mapping
            var onlyInDevices = deviceNumbers.Except(alpacaNumbers).ToList();
            var onlyInAlpaca = alpacaNumbers.Except(deviceNumbers).ToList();

            if (onlyInDevices.Any() || onlyInAlpaca.Any())
            {
                LogError(
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
                    LogWarning(
                        $"DeviceName mismatch for device {device.DeviceNumber}: " +
                        $"Devices='{device.DeviceName}', AlpacaDevices='{alpacaEntry.DeviceName}'");
                }
            }

            LogInfo("AlpacaDevices/Devices synchronization validated successfully");
            return true;
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

        // Simple Console logging (Settings project has no dependency on MonitorLog)
        private void LogInfo(string message) 
            => Console.WriteLine($"ℹ️ [DeviceSynchronizationService] {message}");

        private void LogWarning(string message) 
            => Console.WriteLine($"⚠️ [DeviceSynchronizationService] {message}");

        private void LogError(string message) 
            => Console.WriteLine($"❌ [DeviceSynchronizationService] {message}");
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
