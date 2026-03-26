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
using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Service responsible for maintaining 1-to-1 synchronization between
    /// AlpacaDevices and Devices arrays in settings files.
    /// </summary>
    public interface IDeviceSynchronizationService
    {
        /// <summary>
        /// Ensures AlpacaDevices entry exists for device.
        /// Creates if missing, updates DeviceName if changed.
        /// </summary>
        /// <param name="deviceNumber">Device number (primary key)</param>
        /// <param name="deviceName">Device display name (synced property)</param>
        /// <returns>Task completing when sync is ensured</returns>
        Task EnsureAlpacaDeviceEntryAsync(int deviceNumber, string deviceName);

        /// <summary>
        /// Removes AlpacaDevices entry when device deleted.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>Task completing when entry removed</returns>
        Task RemoveAlpacaDeviceEntryAsync(int deviceNumber);

        /// <summary>
        /// Validates 1-to-1 synchronization on startup.
        /// Fails fast if arrays out of sync.
        /// </summary>
        /// <param name="doc">Parsed JSON document containing both arrays</param>
        /// <returns>True if synchronized, false if out of sync</returns>
        bool ValidateSynchronization(Dictionary<string, JsonElement> doc);

        /// <summary>
        /// Validates 1-to-1 synchronization with detailed error reporting
        /// </summary>
        /// <param name="doc">Parsed JSON document containing both arrays</param>
        /// <returns>Validation result with detailed errors and warnings</returns>
        ValidationResult ValidateSynchronizationDetailed(Dictionary<string, JsonElement> doc);

        /// <summary>
        /// Generates unique GUID for new device.
        /// </summary>
        /// <returns>GUID string for UniqueId property</returns>
        string GenerateUniqueId();
    }
}
