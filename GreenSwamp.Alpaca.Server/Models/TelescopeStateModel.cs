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
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.Server.Models
{
    /// <summary>
    /// Model representing telescope device state aligned with ASCOM IDeviceState
    /// Updates with each server loop iteration
    /// </summary>
    public class TelescopeStateModel
    {
        // Core positioning (ASCOM ITelescopeV4)
        public double Altitude { get; set; }
        public double Azimuth { get; set; }
        public double Declination { get; set; }
        public double RightAscension { get; set; }
        
        // Pier side and timing
        public PointingState SideOfPier { get; set; }
        public double SiderealTime { get; set; }
        public DateTime UTCDate { get; set; }
        public DateTime LocalDate { get; set; }
        
        // Mount state
        public bool Slewing { get; set; }
        public bool Tracking { get; set; }
        public bool AtPark { get; set; }
        public bool AtHome { get; set; }
        public bool IsMountRunning { get; set; }
        
        // Target information
        public double TargetRightAscension { get; set; }
        public double TargetDeclination { get; set; }
        
        // Axis positions (mount-specific)
        public double ActualAxisX { get; set; }
        public double ActualAxisY { get; set; }
        public double AppAxisX { get; set; }
        public double AppAxisY { get; set; }
        
        // Rates and guides
        public DriveRate TrackingRate { get; set; }
        public bool IsPulseGuidingRa { get; set; }
        public bool IsPulseGuidingDec { get; set; }
        
        // Slew type
        public SlewType SlewState { get; set; }
        
        // Performance
        public ulong LoopCounter { get; set; }
        public int TimerOverruns { get; set; }
        
        // Timestamp
        public DateTime LastUpdate { get; set; }
        
        /// <summary>
        /// Constructor initializes with default/invalid values
        /// </summary>
        public TelescopeStateModel()
        {
            Altitude = double.NaN;
            Azimuth = double.NaN;
            Declination = double.NaN;
            RightAscension = double.NaN;
            SideOfPier = PointingState.Unknown;
            SiderealTime = 0;
            UTCDate = DateTime.MinValue;
            LocalDate = DateTime.MinValue;
            Slewing = false;
            Tracking = false;
            AtPark = false;
            AtHome = false;
            IsMountRunning = false;
            TargetRightAscension = double.NaN;
            TargetDeclination = double.NaN;
            ActualAxisX = double.NaN;
            ActualAxisY = double.NaN;
            AppAxisX = double.NaN;
            AppAxisY = double.NaN;
            TrackingRate = DriveRate.Sidereal;
            IsPulseGuidingRa = false;
            IsPulseGuidingDec = false;
            SlewState = SlewType.SlewNone;
            LoopCounter = 0;
            TimerOverruns = 0;
            LastUpdate = DateTime.UtcNow;
        }
    }
}
