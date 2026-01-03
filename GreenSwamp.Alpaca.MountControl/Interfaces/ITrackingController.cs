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
    /// Phase 3.0: Tracking controller interface for managing mount tracking behavior.
    /// Defines the contract for starting/stopping tracking and managing tracking rates.
    /// </summary>
    public interface ITrackingController
    {
        /// <summary>
        /// Gets or sets whether tracking is currently enabled
        /// </summary>
        bool Tracking { get; set; }

        /// <summary>
        /// Gets or sets the tracking rate (Sidereal, Lunar, Solar, King)
        /// </summary>
        DriveRate TrackingRate { get; set; }

        /// <summary>
        /// Gets or sets the Right Ascension tracking rate offset (arcsec/sec)
        /// </summary>
        double RightAscensionRate { get; set; }

        /// <summary>
        /// Gets or sets the Declination tracking rate offset (arcsec/sec)
        /// </summary>
        double DeclinationRate { get; set; }

        /// <summary>
        /// Start tracking at the current tracking rate
        /// </summary>
        void StartTracking();

        /// <summary>
        /// Stop tracking (mount will not follow sky motion)
        /// </summary>
        void StopTracking();

        /// <summary>
        /// Set tracking rate to a predefined drive rate
        /// </summary>
        /// <param name="rate">Drive rate to use</param>
        void SetTrackingRate(DriveRate rate);

        /// <summary>
        /// Set custom tracking rate offsets
        /// </summary>
        /// <param name="raRate">RA rate offset in arcsec/sec</param>
        /// <param name="decRate">Dec rate offset in arcsec/sec</param>
        void SetTrackingOffsets(double raRate, double decRate);

        /// <summary>
        /// Gets whether the mount can set tracking rates
        /// </summary>
        bool CanSetTracking { get; }

        /// <summary>
        /// Gets whether the mount can set declination rate
        /// </summary>
        bool CanSetDeclinationRate { get; }

        /// <summary>
        /// Gets whether the mount can set right ascension rate
        /// </summary>
        bool CanSetRightAscensionRate { get; }
    }
}
