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

namespace GreenSwamp.Alpaca.Shared
{
    /// <summary>
    /// Phase 2: Bridge for Monitor/logging settings.
    /// Maintains compatibility with existing static Settings class.
    /// Phase 2: Minimal implementation - logs initialization only.
    /// Full sync will be added in Phase 3-4 when Monitor settings migrate to new system.
    /// </summary>
    public static class MonitorSettingsBridge
    {
        private static IVersionedSettingsService? _settingsService;
        private static bool _isUpdating;
        private static bool _initialized;

        /// <summary>
        /// Initialize the monitor settings bridge
        /// </summary>
        public static void Initialize(IVersionedSettingsService service)
        {
            if (_initialized)
            {
                return;
            }

            _settingsService = service ?? throw new ArgumentNullException(nameof(service));
            
            // Phase 2: Just log initialization
            // Full monitor settings sync will be added in Phase 3-4
            
            _initialized = true;
            LogBridge("Monitor settings bridge initialized (minimal Phase 2 implementation)");
        }

        /// <summary>
        /// Called when monitor settings are saved.
        /// Phase 2: Placeholder - full implementation in Phase 3-4.
        /// </summary>
        public static void OnMonitorSettingsSaved()
        {
            if (!_initialized || _settingsService == null || _isUpdating)
            {
                return;
            }
            
            // Phase 2: Minimal implementation - just log
            LogBridge("Monitor settings saved (bridge sync not yet implemented)");
        }

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
                    Method = "MonitorSettingsBridge",
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
