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

namespace GreenSwamp.Alpaca.MountControl.Interfaces
{
    /// <summary>
    /// Phase 3.0: Core mount controller interface for managing telescope mount operations.
    /// Defines the contract for mount lifecycle, connection, and basic control operations.
    /// </summary>
    public interface IMountController
    {
        /// <summary>
        /// Gets the unique identifier for this mount instance
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets a value indicating whether the mount is currently connected and operational
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets a value indicating whether the mount is currently running (connected and initialized)
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Connect to the mount hardware and initialize communication
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        bool Connect();

        /// <summary>
        /// Disconnect from the mount hardware and release resources
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Start mount operations (queues, timers, tracking)
        /// </summary>
        void Start();

        /// <summary>
        /// Stop mount operations (queues, timers, tracking)
        /// </summary>
        void Stop();

        /// <summary>
        /// Reset mount to home position with default settings
        /// </summary>
        void Reset();

        /// <summary>
        /// Perform emergency stop - halt all mount motion immediately
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// Get the current error state of the mount
        /// </summary>
        Exception? GetLastError();
    }
}
