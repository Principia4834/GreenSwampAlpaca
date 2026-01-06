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
    /// Position provider interface for reading mount position data.
    /// Defines the contract for accessing mount position in various coordinate systems.
    /// </summary>
    public interface IPositionProvider
    {
        /// <summary>
        /// Get current mount position in degrees (raw from hardware)
        /// </summary>
        /// <returns>Array of [axis1, axis2] positions in degrees, or null on error</returns>
        double[]? GetRawDegrees();

        /// <summary>
        /// Get current mount position in steps (encoder counts)
        /// </summary>
        /// <returns>Array of [axis1, axis2] positions in steps, or null on error</returns>
        double[]? GetRawSteps();

        /// <summary>
        /// Get position for a specific axis in steps with timestamp
        /// </summary>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <returns>Tuple of (steps, timestamp) or null on error</returns>
        Tuple<double?, DateTime>? GetRawStepsWithTime(int axis);

        /// <summary>
        /// Convert steps to degrees for a specific axis
        /// </summary>
        /// <param name="steps">Position in steps</param>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <returns>Position in degrees</returns>
        double ConvertStepsToDegrees(double steps, int axis);

        /// <summary>
        /// Convert degrees to steps for a specific axis
        /// </summary>
        /// <param name="degrees">Position in degrees</param>
        /// <param name="axis">Axis index (0 or 1)</param>
        /// <returns>Position in steps</returns>
        double ConvertDegreesToSteps(double degrees, int axis);

        /// <summary>
        /// Update mount position cache (triggers position read from hardware)
        /// </summary>
        void UpdatePosition();

        /// <summary>
        /// Gets the Right Ascension coordinate (transformed from mount axes)
        /// </summary>
        double RightAscension { get; }

        /// <summary>
        /// Gets the Declination coordinate (transformed from mount axes)
        /// </summary>
        double Declination { get; }

        /// <summary>
        /// Gets the Altitude coordinate
        /// </summary>
        double Altitude { get; }

        /// <summary>
        /// Gets the Azimuth coordinate
        /// </summary>
        double Azimuth { get; }
    }
}
