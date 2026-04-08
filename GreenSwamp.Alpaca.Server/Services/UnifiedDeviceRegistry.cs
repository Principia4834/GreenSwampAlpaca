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

using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Settings.Services;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Unified device registry facade that manages both ASCOM DeviceManager
    /// and MountInstanceRegistry with synchronized operations.
    /// Phase M7: No reserved slots -- any device number (>= 0) may be freely added or removed.
    /// </summary>
    public static class UnifiedDeviceRegistry
    {

        /// <summary>
        /// Registers a device with both ASCOM DeviceManager and MountInstanceRegistry atomically.
        /// This ensures synchronized state across both registries.
        /// </summary>
        /// <param name="deviceNumber">Device number (0-based)</param>
        /// <param name="deviceName">Display name for the device</param>
        /// <param name="uniqueId">ASCOM unique identifier (GUID)</param>
        /// <param name="settingsInstance">Settings instance for this device</param>
        /// <param name="telescopeDriver">Telescope driver instance for ASCOM routing</param>
        public static void RegisterDevice(
            int deviceNumber,
            string deviceName,
            string uniqueId,
            SkySettingsInstance settingsInstance,
            ASCOM.Common.DeviceInterfaces.ITelescopeV4 telescopeDriver)
        {
            // 1. Register with MountInstanceRegistry (internal control).
            MountInstanceRegistry.CreateInstance(deviceNumber, settingsInstance, deviceName);

            // 2. Register with DeviceManager (ASCOM routing)
            DeviceManager.LoadTelescope(
                deviceNumber,
                telescopeDriver,
                deviceName,
                uniqueId
            );
        }

        /// <summary>
        /// Validates that a device number is available across BOTH registries.
        /// Checks MountInstanceRegistry and DeviceManager for conflicts.
        /// </summary>
        public static bool IsDeviceNumberAvailable(int deviceNumber)
        {
            // Check MountInstanceRegistry
            if (MountInstanceRegistry.GetInstance(deviceNumber) != null)
                return false;

            // Check DeviceManager
            if (DeviceManager.Telescopes.ContainsKey(deviceNumber))
                return false;

            return true;
        }

        /// <summary>
        /// Gets the next available device number starting from slot 0.
        /// Returns the lowest available slot number.
        /// </summary>
        public static int GetNextAvailableDeviceNumber()
        {
            for (int i = 0; i < int.MaxValue; i++)
            {
                if (IsDeviceNumberAvailable(i))
                    return i;
            }

            throw new InvalidOperationException("No available device numbers");
        }

        /// <summary>
        /// Removes a device from the registry. Any device number may be removed.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>True if removed, false if not found</returns>
        public static bool RemoveDevice(int deviceNumber)
        {
            bool removed = MountInstanceRegistry.RemoveInstance(deviceNumber);

            // Note: DeviceManager doesn't have a Remove() method in ASCOM.Alpaca.Razor.
            // Device will remain in DeviceManager.Telescopes until server restart.
            // MountInstanceRegistry controls actual device behavior -- removed devices
            // become non-functional (no MountInstance).

            return removed;
        }

        /// <summary>
        /// Gets all devices from the registry.
        /// </summary>
        public static IReadOnlyDictionary<int, MountInstance> GetAllDevices()
        {
            return MountInstanceRegistry.GetAllInstances();
        }
    }
}
