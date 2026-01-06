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
    /// Hardware abstraction interface for mount communication.
    /// Defines the contract for low-level hardware operations, enabling testability and multiple mount types.
    /// </summary>
    public interface IMountHardware
    {
        /// <summary>
        /// Gets the mount type (Simulator, SkyWatcher, etc.)
        /// </summary>
        MountType MountType { get; }

        /// <summary>
        /// Gets whether the hardware is initialized and ready
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initialize hardware connection and communication
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        bool Initialize();

        /// <summary>
        /// Shutdown hardware and close connections
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Send a command to the mount and get response
        /// </summary>
        /// <param name="command">Command to send</param>
        /// <returns>Response from mount, or null on error</returns>
        object? SendCommand(object command);

        /// <summary>
        /// Get current axis position in steps
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <returns>Position in steps, or null on error</returns>
        double? GetAxisPosition(int axis);

        /// <summary>
        /// Get current axis position in steps with timestamp
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <returns>Tuple of (position, timestamp) or null on error</returns>
        Tuple<double?, DateTime>? GetAxisPositionWithTime(int axis);

        /// <summary>
        /// Set axis position (used for sync operations)
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <param name="position">Position in degrees</param>
        /// <returns>True if successful</returns>
        bool SetAxisPosition(int axis, double position);

        /// <summary>
        /// Start axis motion at specified rate
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <param name="rate">Rate in degrees/sec</param>
        void StartAxisMotion(int axis, double rate);

        /// <summary>
        /// Stop axis motion
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        void StopAxis(int axis);

        /// <summary>
        /// Emergency stop - immediately halt all motion
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// Gets the steps per revolution for each axis
        /// </summary>
        long[] StepsPerRevolution { get; }

        /// <summary>
        /// Gets the conversion factor from steps to radians
        /// </summary>
        double[] FactorStep { get; }

        /// <summary>
        /// Gets whether the mount is currently moving
        /// </summary>
        bool IsMoving { get; }

        /// <summary>
        /// Gets the last error from hardware communication
        /// </summary>
        Exception? LastError { get; }
    }
}
