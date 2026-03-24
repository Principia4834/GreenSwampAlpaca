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

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Thread-safe registry for managing multiple telescope device instances.
    /// Implements Phase 4.8 multi-instance support.
    /// </summary>
    public static class MountInstanceRegistry
    {
        private static readonly Dictionary<int, MountInstance> _instances = new Dictionary<int, MountInstance>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Creates and registers a new mount instance.
        /// </summary>
        /// <param name="deviceNumber">Device number (must be >= 0 and unique)</param>
        /// <param name="settings">Settings instance for the mount</param>
        /// <param name="deviceName">Display name for the device</param>
        /// <exception cref="ArgumentException">Device number already exists</exception>
        /// <exception cref="ArgumentNullException">Settings or name is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Device number is negative</exception>
        public static void CreateInstance(int deviceNumber, SkySettingsInstance settings, string deviceName)
        {
            if (deviceNumber < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Device number must be >= 0");
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentNullException(nameof(deviceName));
            }

            lock (_lock)
            {
                if (_instances.ContainsKey(deviceNumber))
                {
                    throw new ArgumentException($"Device number {deviceNumber} already exists", nameof(deviceNumber));
                }

                // Create MountInstance with device name
                var instance = new MountInstance($"device-{deviceNumber}", settings, deviceName);
                _instances[deviceNumber] = instance;
            }
        }

        /// <summary>
        /// Retrieves a mount instance by device number.
        /// </summary>
        /// <param name="deviceNumber">Device number to look up</param>
        /// <returns>MountInstance if found, null otherwise</returns>
        public static MountInstance? GetInstance(int deviceNumber)
        {
            lock (_lock)
            {
                return _instances.TryGetValue(deviceNumber, out var instance) ? instance : null;
            }
        }

        /// <summary>
        /// Removes a mount instance from the registry.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>True if removed, false if device number not found</returns>
        public static bool RemoveInstance(int deviceNumber)
        {
            lock (_lock)
            {
                if (!_instances.TryGetValue(deviceNumber, out var instance))
                {
                    return false;
                }

                // Disconnect if connected (use IsConnected property)
                if (instance.IsConnected)
                {
                    try
                    {
                        instance.Disconnect();
                    }
                    catch
                    {
                        // Best effort disconnect
                    }
                }

                return _instances.Remove(deviceNumber);
            }
        }

        /// <summary>
        /// Gets a read-only snapshot of all registered instances.
        /// </summary>
        /// <returns>Read-only dictionary of device numbers to instances</returns>
        public static IReadOnlyDictionary<int, MountInstance> GetAllInstances()
        {
            lock (_lock)
            {
                return new Dictionary<int, MountInstance>(_instances);
            }
        }

        /// <summary>
        /// Checks if a device number is available for use.
        /// </summary>
        /// <param name="deviceNumber">Device number to check</param>
        /// <returns>True if available, false if in use</returns>
        public static bool IsDeviceNumberAvailable(int deviceNumber)
        {
            lock (_lock)
            {
                return !_instances.ContainsKey(deviceNumber);
            }
        }

        /// <summary>
        /// Finds the next available device number starting from 0.
        /// </summary>
        /// <returns>First available device number</returns>
        public static int GetNextAvailableDeviceNumber()
        {
            lock (_lock)
            {
                int deviceNumber = 0;
                while (_instances.ContainsKey(deviceNumber))
                {
                    deviceNumber++;
                }
                return deviceNumber;
            }
        }
    }
}
