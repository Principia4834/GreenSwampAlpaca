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

using GreenSwamp.Alpaca.MountControl.Interfaces;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase 3.1: Instance-based mount controller that initially delegates to static SkyServer.
    /// This class will gradually take over implementation from static methods in Phase 3.2+.
    /// </summary>
    public class MountInstance : IMountController
    {
        private readonly string _id;
        private readonly SkySettingsInstance _settings;

        /// <summary>
        /// Create a new mount instance
        /// </summary>
        /// <param name="id">Unique identifier for this mount instance</param>
        /// <param name="settings">Settings for this mount instance</param>
        public MountInstance(string id, SkySettingsInstance settings)
        {
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            LogMount($"MountInstance created: {_id}");
        }

        #region IMountController Implementation (Phase 3.1: Delegation)

        /// <summary>
        /// Gets the unique identifier for this mount instance
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Gets whether the mount is currently connected
        /// Phase 3.1: Delegates to static SkyServer
        /// </summary>
        public bool IsConnected => SkyServer.IsMountRunning;

        /// <summary>
        /// Gets whether the mount is currently running
        /// Phase 3.1: Delegates to static SkyServer
        /// </summary>
        public bool IsRunning => SkyServer.IsMountRunning;

        /// <summary>
        /// Connect to mount hardware
        /// Phase 3.1: Delegates to static method (will be renamed to MountConnect_Internal in next step)
        /// Phase 3.2+: Will implement directly
        /// </summary>
        public bool Connect()
        {
            LogMount($"Connect() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            // Phase 3.1.2: Will change to SkyServer.MountConnect_Internal()
            // Phase 3.2: Will implement directly
            return SkyServer.Connect_Stub();
        }

        /// <summary>
        /// Disconnect from mount hardware
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Disconnect()
        {
            LogMount($"Disconnect() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Disconnect_Stub();
        }

        /// <summary>
        /// Start mount operations
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Start()
        {
            LogMount($"Start() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Start_Stub();
        }

        /// <summary>
        /// Stop mount operations
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Stop()
        {
            LogMount($"Stop() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Stop_Stub();
        }

        /// <summary>
        /// Reset mount to home position
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Reset()
        {
            LogMount($"Reset() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.MountReset();
        }

        /// <summary>
        /// Emergency stop - halt all motion immediately
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void EmergencyStop()
        {
            LogMount($"EmergencyStop() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.AbortSlewAsync(speak: false);
        }

        /// <summary>
        /// Get last error from mount
        /// Phase 3.1: Delegates to static property
        /// </summary>
        public Exception? GetLastError()
        {
            return SkyServer.MountError;
        }

        #endregion

        #region Logging

        private void LogMount(string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = "MountInstance",
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

        #endregion
    }
}
