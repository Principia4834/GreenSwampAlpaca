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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Mount settings - all defaults come from appsettings.json
    /// </summary>
    public class SkySettings
    {
        // Connection Settings
        [Required]
        public string Mount { get; set; } = null!;
        
        [Required]
        public string Port { get; set; } = null!;
        
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public string Handshake { get; set; } = null!;
        public int ReadTimeout { get; set; }
        public bool DTREnable { get; set; }
        public bool RTSEnable { get; set; }
        
        // Location Settings
        [Range(-90, 90)]
        public double Latitude { get; set; }
        
        [Range(-180, 180)]
        public double Longitude { get; set; }
        
        [Range(-500, 9000)]
        public double Elevation { get; set; }
        
        public TimeSpan UTCOffset { get; set; }
        
        // Mount Configuration
        public bool AutoTrack { get; set; }
        public string AlignmentMode { get; set; } = null!;
        public string EquatorialCoordinateType { get; set; } = null!;
        public bool AtPark { get; set; }
        
        // Optical Settings
        public double ApertureDiameter { get; set; }
        public double ApertureArea { get; set; }
        public double FocalLength { get; set; }
        
        // Environmental Settings
        public bool Refraction { get; set; }
        public double Temperature { get; set; }
        
        // Custom Gearing
        public bool CustomGearing { get; set; }
        public int CustomRa360Steps { get; set; }
        public int CustomRaWormTeeth { get; set; }
        public int CustomDec360Steps { get; set; }
        public int CustomDecWormTeeth { get; set; }
        public int CustomRaTrackingOffset { get; set; }
        public int CustomDecTrackingOffset { get; set; }
        
        // Backlash
        public int RaBacklash { get; set; }
        public int DecBacklash { get; set; }

        // Pulse Guide Settings
        public List<HcPulseGuide> HcPulseGuides { get; set; } = new();
        public int MinPulseRa { get; set; }
        public int MinPulseDec { get; set; }
        public bool DecPulseToGoTo { get; set; }
        public int St4Guiderate { get; set; }
        public double GuideRateOffsetX { get; set; }
        public double GuideRateOffsetY { get; set; }
        
        // Tracking Settings
        public string TrackingRate { get; set; } = null!;
        public double SiderealRate { get; set; }
        public double LunarRate { get; set; }
        public double SolarRate { get; set; }
        public double KingRate { get; set; }
        public int RATrackingOffset { get; set; }
        
        // Home Settings
        public double HomeAxisX { get; set; }
        public double HomeAxisY { get; set; }
        public double AutoHomeAxisX { get; set; }
        public double AutoHomeAxisY { get; set; }
        
        // Park Settings
        public string ParkName { get; set; } = null!;
        public bool LimitPark { get; set; }
        public string ParkLimitName { get; set; } = null!;
        public List<ParkPosition> ParkPositions { get; set; } = new();
        public double[] ParkAxes { get; set; } = Array.Empty<double>();

        // Limit Settings
        public bool LimitTracking { get; set; }
        public double HourAngleLimit { get; set; }
        public bool NoSyncPastMeridian { get; set; }
        public int SyncLimit { get; set; }
        public bool SyncLimitOn { get; set; }
        public double AxisTrackingLimit { get; set; }
        public bool HzLimitTracking { get; set; }
        public string ParkHzLimitName { get; set; } = null!;
        public bool HzLimitPark { get; set; }
        public double AxisHzTrackingLimit { get; set; }
        public double AxisUpperLimitY { get; set; }
        public double AxisLowerLimitY { get; set; }
        public double AxisLimitX { get; set; }
        
        // PEC/PPEC Settings
        public bool PecOn { get; set; }
        public bool PpecOn { get; set; }
        public bool AlternatingPPEC { get; set; }
        public int PecOffSet { get; set; }
        public string PecWormFile { get; set; } = null!;
        public string Pec360File { get; set; } = null!;
        public string PecMode { get; set; } = null!;
        
        // Encoders
        public bool EncodersOn { get; set; }
        
        // Hand Controller Settings
        public string HcSpeed { get; set; } = null!;
        public string HcMode { get; set; } = null!;
        public bool HcAntiRa { get; set; }
        public bool HcAntiDec { get; set; }
        public bool HcFlipEW { get; set; }
        public bool HcFlipNS { get; set; }
        public bool DisableKeysOnGoTo { get; set; }
        
        // Camera/Eyepiece Settings
        public double CameraWidth { get; set; }
        public double CameraHeight { get; set; }
        public double EyepieceFS { get; set; }
        
        // Capabilities
        public bool CanAlignMode { get; set; }
        public bool CanAltAz { get; set; }
        public bool CanDoesRefraction { get; set; }
        public bool CanEquatorial { get; set; }
        public bool CanFindHome { get; set; }
        public bool CanLatLongElev { get; set; }
        public bool CanOptics { get; set; }
        public bool CanPark { get; set; }
        public bool CanPierSide { get; set; }
        public bool CanPulseGuide { get; set; }
        public bool CanSetEquRates { get; set; }
        public bool CanSetGuideRates { get; set; }
        public bool CanSetPark { get; set; }
        public bool CanSetPierSide { get; set; }
        public bool CanSetTracking { get; set; }
        public bool CanSiderealTime { get; set; }
        public bool CanSlew { get; set; }
        public bool CanSlewAltAz { get; set; }
        public bool CanSlewAltAzAsync { get; set; }
        public bool CanSlewAsync { get; set; }
        public bool CanSync { get; set; }
        public bool CanSyncAltAz { get; set; }
        public bool CanUnpark { get; set; }
        public bool CanTrackingRates { get; set; }
        public bool CanSetDeclinationRate { get; set; }
        public bool CanSetRightAscensionRate { get; set; }
        
        // Advanced Settings
        public bool AllowAdvancedCommandSet { get; set; }
        public double MaximumSlewRate { get; set; }
        public double GotoPrecision { get; set; }
        public bool FullCurrent { get; set; }
        public int NumMoveAxis { get; set; }
        public bool VersionOne { get; set; }
        public bool GlobalStopOn { get; set; }
        
        // Display Settings
        public int DisplayInterval { get; set; }
        public bool TraceLogger { get; set; }
        public int PolarLedLevel { get; set; }
        
        // GPS Settings
        public int GpsPort { get; set; }
        public string GpsBaudRate { get; set; } = null!;
        
        // Instrument Info
        public string InstrumentDescription { get; set; } = null!;
        public string InstrumentName { get; set; } = null!;
        
        // Model Settings
        public string ModelType { get; set; } = null!;
        public AxisModelOffset AxisModelOffsets { get; set; } = new();

        // Alt-Az Tracking
        public int AltAzTrackingUpdateInterval { get; set; }
        
        // Polar Alignment
        public string PolarMode { get; set; } = null!;

        public class ParkPosition
        {
            public string Name { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
        }

        public class HcPulseGuide
        {
            public int Speed { get; set; }
            public int Duration { get; set; }
            public int Interval { get; set; }
            public double Rate { get; set; }
        }

        public class AxisModelOffset
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

    }
}
