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

using ASCOM.Common.DeviceInterfaces;

namespace GreenSwamp.Alpaca.MountControl.Interfaces
{
    /// <summary>
    /// Phase 3.0: Slew controller interface for managing telescope mount slewing operations.
    /// Defines the contract for goto, sync, and slew operations.
    /// </summary>
    public interface ISlewController
    {
        /// <summary>
        /// Gets whether the mount is currently slewing
        /// </summary>
        bool IsSlewing { get; }

        /// <summary>
        /// Gets the current slew state
        /// </summary>
        SlewType SlewState { get; }

        /// <summary>
        /// Slew to Right Ascension and Declination coordinates (asynchronous)
        /// </summary>
        /// <param name="rightAscension">Target RA in hours</param>
        /// <param name="declination">Target Dec in degrees</param>
        void SlewToCoordinatesAsync(double rightAscension, double declination);

        /// <summary>
        /// Slew to Right Ascension and Declination coordinates (synchronous - blocks until complete)
        /// </summary>
        /// <param name="rightAscension">Target RA in hours</param>
        /// <param name="declination">Target Dec in degrees</param>
        void SlewToCoordinates(double rightAscension, double declination);

        /// <summary>
        /// Slew to Altitude and Azimuth coordinates (asynchronous)
        /// </summary>
        /// <param name="azimuth">Target azimuth in degrees</param>
        /// <param name="altitude">Target altitude in degrees</param>
        void SlewToAltAzAsync(double azimuth, double altitude);

        /// <summary>
        /// Slew to Altitude and Azimuth coordinates (synchronous - blocks until complete)
        /// </summary>
        /// <param name="azimuth">Target azimuth in degrees</param>
        /// <param name="altitude">Target altitude in degrees</param>
        void SlewToAltAz(double azimuth, double altitude);

        /// <summary>
        /// Slew to target coordinates (asynchronous)
        /// </summary>
        void SlewToTargetAsync();

        /// <summary>
        /// Slew to target coordinates (synchronous - blocks until complete)
        /// </summary>
        void SlewToTarget();

        /// <summary>
        /// Synchronize mount position to specified coordinates
        /// </summary>
        /// <param name="rightAscension">RA to sync to in hours</param>
        /// <param name="declination">Dec to sync to in degrees</param>
        void SyncToCoordinates(double rightAscension, double declination);

        /// <summary>
        /// Synchronize mount position to current target coordinates
        /// </summary>
        void SyncToTarget();

        /// <summary>
        /// Abort any current slew operation
        /// </summary>
        void AbortSlew();

        /// <summary>
        /// Move axis at specified rate
        /// </summary>
        /// <param name="axis">Axis to move (Primary/Secondary)</param>
        /// <param name="rate">Rate in deg/sec (ASCOM rate units)</param>
        void MoveAxis(TelescopeAxis axis, double rate);

        /// <summary>
        /// Pulse guide in specified direction for specified duration
        /// </summary>
        /// <param name="direction">Guide direction</param>
        /// <param name="duration">Duration in milliseconds</param>
        void PulseGuide(GuideDirection direction, int duration);

        /// <summary>
        /// Find home position
        /// </summary>
        void FindHome();

        /// <summary>
        /// Park the mount
        /// </summary>
        void Park();

        /// <summary>
        /// Unpark the mount
        /// </summary>
        void Unpark();

        /// <summary>
        /// Set park position to current position
        /// </summary>
        void SetPark();

        /// <summary>
        /// Gets target Right Ascension coordinate in hours
        /// </summary>
        double TargetRightAscension { get; set; }

        /// <summary>
        /// Gets target Declination coordinate in degrees
        /// </summary>
        double TargetDeclination { get; set; }

        /// <summary>
        /// Gets whether the mount can perform goto operations
        /// </summary>
        bool CanSlew { get; }

        /// <summary>
        /// Gets whether the mount can perform async goto operations
        /// </summary>
        bool CanSlewAsync { get; }

        /// <summary>
        /// Gets whether the mount can sync
        /// </summary>
        bool CanSync { get; }

        /// <summary>
        /// Gets whether the mount can park
        /// </summary>
        bool CanPark { get; }

        /// <summary>
        /// Gets whether the mount can find home
        /// </summary>
        bool CanFindHome { get; }

        /// <summary>
        /// Gets whether the mount can pulse guide
        /// </summary>
        bool CanPulseGuide { get; }
    }
}
