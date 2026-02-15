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
    /// Enforces reserved slots: 0 (simulator) and 1 (physical mount).
    /// Phase 4.11: Architectural cleanup to prevent duplicate device numbers.
    /// </summary>
    public static class UnifiedDeviceRegistry
    {
        // Reserved slot constants
        private const int SimulatorSlot = 0;
        private const int PhysicalMountSlot = 1;
        private const int FirstDynamicSlot = 2;

        /// <summary>
        /// Checks if a device number is a reserved slot (0 or 1).
        /// Reserved slots cannot be deleted but can have profiles changed.
        /// </summary>
        public static bool IsReservedSlot(int deviceNumber)
        {
            return deviceNumber == SimulatorSlot || deviceNumber == PhysicalMountSlot;
        }

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
            // 1. Register with MountInstanceRegistry (internal control)
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
        /// Gets the next available device number starting from slot 2.
        /// Slots 0 and 1 are reserved and excluded from auto-assignment.
        /// </summary>
        public static int GetNextAvailableDeviceNumber()
        {
            // Start from 2 (after reserved slots 0 and 1)
            for (int i = FirstDynamicSlot; i < int.MaxValue; i++)
            {
                if (IsDeviceNumberAvailable(i))
                    return i;
            }

            throw new InvalidOperationException("No available device numbers");
        }

        /// <summary>
        /// Removes a device from the registry (dynamic slots only).
        /// Reserved slots (0, 1) cannot be removed.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>True if removed, false if not found</returns>
        /// <exception cref="InvalidOperationException">If attempting to remove a reserved slot</exception>
        public static bool RemoveDevice(int deviceNumber)
        {
            // Protection: Can't delete reserved slots
            if (IsReservedSlot(deviceNumber))
            {
                throw new InvalidOperationException(
                    $"Cannot delete reserved device slot {deviceNumber}. Reserved slots (0=Simulator, 1=Physical Mount) are permanent.");
            }

            // Remove from MountInstanceRegistry
            bool removed = MountInstanceRegistry.RemoveInstance(deviceNumber);

            // Note: DeviceManager doesn't have a Remove() method in ASCOM.Alpaca.Razor
            // Device will remain in DeviceManager.Telescopes until server restart
            // This is acceptable because:
            // - MountInstanceRegistry controls actual device behavior
            // - Removed devices become non-functional (no MountInstance)
            // - ASCOM clients use UniqueID for persistence, not device number
            // - Reserved slots prevent critical configuration loss

            return removed;
        }

        /// <summary>
        /// Gets all devices from the registry with reserved slot information.
        /// </summary>
        public static IReadOnlyDictionary<int, MountInstance> GetAllDevices()
        {
            return MountInstanceRegistry.GetAllInstances();
        }

        /// <summary>
        /// Initializes the registry with reserved slots.
        /// This method should be called at application startup BEFORE loading dynamic devices.
        /// </summary>
        /// <param name="simulatorSettings">Settings instance for simulator (slot 0)</param>
        /// <param name="simulatorName">Display name for simulator device</param>
        /// <param name="simulatorUniqueId">ASCOM unique ID for simulator</param>
        /// <param name="simulatorDriver">Telescope driver for simulator</param>
        /// <param name="physicalMountSettings">Settings instance for physical mount (slot 1)</param>
        /// <param name="physicalMountName">Display name for physical mount device</param>
        /// <param name="physicalMountUniqueId">ASCOM unique ID for physical mount</param>
        /// <param name="physicalMountDriver">Telescope driver for physical mount</param>
        public static void InitializeReservedSlots(
            SkySettingsInstance simulatorSettings,
            string simulatorName,
            string simulatorUniqueId,
            ASCOM.Common.DeviceInterfaces.ITelescopeV4 simulatorDriver,
            SkySettingsInstance physicalMountSettings,
            string physicalMountName,
            string physicalMountUniqueId,
            ASCOM.Common.DeviceInterfaces.ITelescopeV4 physicalMountDriver)
        {
            // Register slot 0: Simulator
            RegisterDevice(SimulatorSlot, simulatorName, simulatorUniqueId, simulatorSettings, simulatorDriver);

            // Register slot 1: Physical Mount
            RegisterDevice(PhysicalMountSlot, physicalMountName, physicalMountUniqueId, physicalMountSettings, physicalMountDriver);
        }
    }
}
