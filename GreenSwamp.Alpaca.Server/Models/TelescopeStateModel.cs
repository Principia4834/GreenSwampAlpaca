/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

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
        public double LocalHourAngle { get; set; }
        public DateTime UTCDate { get; set; }
        public DateTime LocalDate { get; set; }
        
        // Mount state
        public bool Slewing { get; set; }
        public bool Tracking { get; set; }
        public bool LimitsOn { get; set; }
        public bool LimitWarningActive { get; set; }
        public string LimitWarningMessage { get; set; } = string.Empty;
        public long LimitWarningSequence { get; set; }
        public bool AtPark { get; set; }
        public bool AtHome { get; set; }
        public bool LimitTriggered { get; set; }

        public bool IsMountRunning { get; set; }
        public string ComPort { get; set; } = string.Empty;
        public int ConnectedClientCount { get; set; }
        /// <summary>True once any client has connected since the server started. Never reset on disconnect.</summary>
        public bool HasEverBeenConnected { get; set; }

        // Park positions
        public string? ParkSelectedName { get; set; }
        public List<string> ParkPositionNames { get; set; } = [];

        // Target information
        public double TargetRightAscension { get; set; }
        public double TargetDeclination { get; set; }
        
        // Axis positions (mount-specific)
        public double ActualAxisX { get; set; }
        public double ActualAxisY { get; set; }
        public double AppAxisX { get; set; }
        public double AppAxisY { get; set; }

        // Axis step positions
        public double[] AxisSteps { get; set; } = new double[2];

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

        // Pier flip
        public bool FlipOnNextGoto { get; set; }

        // SkyWatcher-specific
        public double ControllerVoltage { get; set; }
        public bool LowVoltageEvent { get; set; }

        // Voice
        public bool EnableVoice { get; set; }
        public bool VoiceActive { get; set; }
        public string VoiceName { get; set; } = string.Empty;
        public int VoiceVolume { get; set; }

        // AutoHome
        public bool IsAutoHomeRunning { get; set; }
        public int AutoHomeProgressBar { get; set; }
        public bool IsGermanPolarMode { get; set; }
        public double AutoHomeAxisX { get; set; }
        public double AutoHomeAxisY { get; set; }

        // Mount gearing details
        public long[] StepsPerRevolution { get; set; }
        public double [] StepsWormPerRevolution { get; set; }
        public long[] StepsTimeFreq { get; set; }
        public Vector TrackingOffsetRate {  get; set; }

        // Mount capabilities
        public bool CanPPec { get; set; }
        public bool CanHomeSensor { get; set; }
        public bool CanPolarLed { get; set; }
        public bool CanAdvancedCmdSupport { get; set; }
        public string MountName { get; set; } = string.Empty;
        public string[] MountVersion { get; set; } = { string.Empty, string.Empty };
        public string Capabilities { get; set; } = string.Empty;

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
            LocalHourAngle = 0;
            UTCDate = DateTime.MinValue;
            LocalDate = DateTime.MinValue;
            Slewing = false;
            Tracking = false;
            LimitsOn = false;
            LimitWarningActive = false;
            LimitWarningMessage = string.Empty;
            LimitWarningSequence = 0;
            AtPark = false;
            AtHome = false;
            LimitTriggered = false;
            IsMountRunning = false;
            ComPort = string.Empty;
            ConnectedClientCount = 0;
            ParkSelectedName = null;
            ParkPositionNames = [];
            TargetRightAscension = double.NaN;
            TargetDeclination = double.NaN;
            ActualAxisX = double.NaN;
            ActualAxisY = double.NaN;
            AxisSteps = new double[2];
            TrackingRate = DriveRate.Sidereal;
            IsPulseGuidingRa = false;
            IsPulseGuidingDec = false;
            SlewState = SlewType.SlewNone;
            LoopCounter = 0;
            TimerOverruns = 0;
            LastUpdate = DateTime.UtcNow;
            FlipOnNextGoto = false;
            ControllerVoltage = double.NaN;
            LowVoltageEvent = false;
            EnableVoice = true;
            VoiceActive = false;
            VoiceName = string.Empty;
            VoiceVolume = 100;
            IsAutoHomeRunning = false;
            AutoHomeProgressBar = 0;
            IsGermanPolarMode = false;
            AutoHomeAxisX = 90.0;
            AutoHomeAxisY = 90.0;
            StepsPerRevolution = new [] { 0L, 0L };
            StepsWormPerRevolution = new [] { 0.0, 0.0 };
            StepsTimeFreq = new [] { 0L, 0L };
    }
}
}
