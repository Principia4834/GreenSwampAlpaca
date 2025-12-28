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

using GreenSwamp.Alpaca.Settings.Services;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using ASCOM.Common.DeviceInterfaces;
using System.IO.Ports;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase 2: Bridge to synchronize between legacy static SkySettings and new DI-based settings.
    /// Provides bidirectional synchronization to maintain compatibility during migration.
    /// Phase 2 syncs only critical properties - full sync happens in Phase 4.
    /// </summary>
    public static class SkySettingsBridge
    {
        private static IVersionedSettingsService? _settingsService;
        private static bool _isUpdating; // Prevent recursive updates
        private static bool _initialized;

        /// <summary>
        /// Initialize the bridge with the new settings service
        /// </summary>
        public static void Initialize(IVersionedSettingsService service)
        {
            if (_initialized)
            {
                LogBridge("Already initialized, skipping");
                return;
            }

            _settingsService = service ?? throw new ArgumentNullException(nameof(service));
            
            // Subscribe to changes from new system
            _settingsService.SettingsChanged += OnNewSettingsChanged;
            
            // Initial sync: new ? old
            SyncNewToOld();
            
            _initialized = true;
            LogBridge("Bridge initialized and synced");
        }

        /// <summary>
        /// Called when old settings are saved - sync to new system
        /// </summary>
        public static void OnOldSettingsSaved()
        {
            if (!_initialized || _settingsService == null || _isUpdating)
            {
                return;
            }
            
            try
            {
                _isUpdating = true;
                SyncOldToNew();
                LogBridge("Synced old settings ? new settings");
            }
            catch (Exception ex)
            {
                LogBridge($"Error syncing old ? new: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Sync new settings to old static properties.
        /// Phase 2: Only critical properties (8 total) - expanded in Phase 4.
        /// </summary>
        private static void SyncNewToOld()
        {
            if (_settingsService == null || _isUpdating)
            {
                return;
            }
            
            try
            {
                _isUpdating = true;
                var newSettings = _settingsService.GetSettings();
                
                // Phase 2: Sync only critical properties
                // Connection Settings
                SkySettings.Mount = ParseMountType(newSettings.Mount);
                SkySettings.Port = newSettings.Port;
                SkySettings.BaudRate = ParseSerialSpeed(newSettings.BaudRate);
                
                // Location Settings
                SkySettings.Latitude = newSettings.Latitude;
                SkySettings.Longitude = newSettings.Longitude;
                SkySettings.Elevation = newSettings.Elevation;
                
                // Mount Configuration
                SkySettings.AlignmentMode = ParseAlignmentMode(newSettings.AlignmentMode);
                SkySettings.AtPark = newSettings.AtPark;
                
                LogBridge($"Synced 8 critical properties from new ? old");
            }
            catch (Exception ex)
            {
                LogBridge($"Error in SyncNewToOld: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Sync old static properties to new settings (write path).
        /// Phase 2: Only when explicitly saved via Save() method.
        /// </summary>
        private static void SyncOldToNew()
        {
            if (_settingsService == null)
            {
                return;
            }
            
            try
            {
                // Get current settings
                var newSettings = _settingsService.GetSettings();
                
                // Update from static properties (8 critical properties)
                newSettings.Mount = SkySettings.Mount.ToString();
                newSettings.Port = SkySettings.Port;
                newSettings.BaudRate = (int)SkySettings.BaudRate;
                newSettings.Latitude = SkySettings.Latitude;
                newSettings.Longitude = SkySettings.Longitude;
                newSettings.Elevation = SkySettings.Elevation;
                newSettings.AlignmentMode = SkySettings.AlignmentMode.ToString();
                newSettings.AtPark = SkySettings.AtPark;
                
                // Save asynchronously (use Wait for synchronous context)
                _settingsService.SaveSettingsAsync(newSettings).Wait();
                
                LogBridge("Saved 8 properties old ? new settings");
            }
            catch (Exception ex)
            {
                LogBridge($"Error in SyncOldToNew: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when new settings change externally (e.g., from UI)
        /// </summary>
        private static void OnNewSettingsChanged(object? sender, Settings.Models.SkySettings e)
        {
            if (_isUpdating)
            {
                return;
            }
            
            SyncNewToOld();
            LogBridge("Synced new settings ? old settings (external change)");
        }

        #region Type Converters
        
        private static MountType ParseMountType(string value)
        {
            return Enum.TryParse<MountType>(value, true, out var result) 
                ? result 
                : MountType.Simulator;
        }
            
        private static SerialSpeed ParseSerialSpeed(int value)
        {
            // Map baud rate integer to SerialSpeed enum
            return value switch
            {
                4800 => SerialSpeed.ps4800,
                9600 => SerialSpeed.ps9600,
                19200 => SerialSpeed.ps19200,
                38400 => SerialSpeed.ps38400,
                57600 => SerialSpeed.ps57600,
                115200 => SerialSpeed.ps115200,
                _ => SerialSpeed.ps9600
            };
        }
                
        private static AlignmentMode ParseAlignmentMode(string value)
        {
            return Enum.TryParse<AlignmentMode>(value, true, out var result) 
                ? result 
                : AlignmentMode.GermanPolar;
        }
        
        #endregion

        private static void LogBridge(string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = "SkySettingsBridge",
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = message
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch
            {
                // Fail silently if logging fails
            }
        }
    }
}
