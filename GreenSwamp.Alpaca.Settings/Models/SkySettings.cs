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

using System.ComponentModel.DataAnnotations;
using GreenSwamp.Alpaca.Settings.Attributes;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Mount settings - all defaults come from appsettings.json
    /// </summary>
    public class SkySettings
    {
        // Device identification
        public int DeviceNumber { get; set; } = 0;
        public string DeviceName { get; set; } = "Telescope";
        public bool Enabled { get; set; } = true;

        // Connection Settings
        [Required]
        [CommonSetting]
        public string Mount { get; set; } = null!;

        [Required]
        [CommonSetting]
        public string Port { get; set; } = null!;

        [CommonSetting]
        public int BaudRate { get; set; }
        [CommonSetting]
        public int DataBits { get; set; }
        [CommonSetting]
        public string Handshake { get; set; } = null!;
        [CommonSetting]
        public int ReadTimeout { get; set; }
        [CommonSetting]
        public bool DTREnable { get; set; }
        [CommonSetting]
        public bool RTSEnable { get; set; }
        
        // Location Settings
        [Range(-90, 90)]
        [CommonSetting]
        public double Latitude { get; set; }

        [Range(-180, 180)]
        [CommonSetting]
        public double Longitude { get; set; }

        [Range(-500, 9000)]
        [CommonSetting]
        public double Elevation { get; set; }

        [CommonSetting]
        public TimeSpan UTCOffset { get; set; }
        
        // Mount Configuration
        [CommonSetting]
        public bool AutoTrack { get; set; }
        [UniqueSetting]
        public string AlignmentMode { get; set; } = null!;
        [CommonSetting]
        public string EquatorialCoordinateType { get; set; } = null!;
        [UniqueSetting]
        public bool AtPark { get; set; }
        
        // Optical Settings
        [CommonSetting]
        public double ApertureDiameter { get; set; }
        [CommonSetting]
        public double ApertureArea { get; set; }
        [CommonSetting]
        public double FocalLength { get; set; }
        
        // Environmental Settings
        [CommonSetting]
        public bool Refraction { get; set; }
        [CommonSetting]
        public double Temperature { get; set; }
        
        // Custom Gearing
        [CommonSetting]
        public bool CustomGearing { get; set; }
        [CommonSetting]
        public int CustomRa360Steps { get; set; }
        [CommonSetting]
        public int CustomRaWormTeeth { get; set; }
        [CommonSetting]
        public int CustomDec360Steps { get; set; }
        [CommonSetting]
        public int CustomDecWormTeeth { get; set; }
        [CommonSetting]
        public int CustomRaTrackingOffset { get; set; }
        [CommonSetting]
        public int CustomDecTrackingOffset { get; set; }

        // Backlash
        [CommonSetting]
        public int RaBacklash { get; set; }
        [CommonSetting]
        public int DecBacklash { get; set; }

        // Pulse Guide Settings
        [CommonSetting]
        public List<HcPulseGuide> HcPulseGuides { get; set; } = new();
        [CommonSetting]
        public int MinPulseRa { get; set; }
        [CommonSetting]
        public int MinPulseDec { get; set; }
        [CommonSetting]
        public bool DecPulseToGoTo { get; set; }
        [CommonSetting]
        public int St4Guiderate { get; set; }
        [CommonSetting]
        public double GuideRateOffsetX { get; set; }
        [CommonSetting]
        public double GuideRateOffsetY { get; set; }

        // Tracking Settings
        [CommonSetting]
        public string TrackingRate { get; set; } = null!;
        [CommonSetting]
        public double SiderealRate { get; set; }
        [CommonSetting]
        public double LunarRate { get; set; }
        [CommonSetting]
        public double SolarRate { get; set; }
        [CommonSetting]
        public double KingRate { get; set; }
        [CommonSetting]
        public int RATrackingOffset { get; set; }
        
        // Home Settings
        [UniqueSetting]
        public double HomeAxisX { get; set; }
        [UniqueSetting]
        public double HomeAxisY { get; set; }
        [UniqueSetting]
        public double AutoHomeAxisX { get; set; }
        [UniqueSetting]
        public double AutoHomeAxisY { get; set; }
        
        // Park Settings
        [UniqueSetting]
        public string ParkName { get; set; } = null!;
        [UniqueSetting]
        public bool LimitPark { get; set; }
        [UniqueSetting]
        public string ParkLimitName { get; set; } = null!;
        [UniqueSetting]
        public List<ParkPosition> ParkPositions { get; set; } = new();
        [UniqueSetting]
        public double[] ParkAxes { get; set; } = Array.Empty<double>();

        // Limit Settings
        [UniqueSetting]
        public bool LimitTracking { get; set; }
        [UniqueSetting]
        public double HourAngleLimit { get; set; }
        [UniqueSetting]
        public bool NoSyncPastMeridian { get; set; }
        [CommonSetting]
        public int SyncLimit { get; set; }
        [CommonSetting]
        public bool SyncLimitOn { get; set; }
        [UniqueSetting]
        public double AxisTrackingLimit { get; set; }
        [UniqueSetting]
        public bool HzLimitTracking { get; set; }
        [UniqueSetting]
        public string ParkHzLimitName { get; set; } = null!;
        [UniqueSetting]
        public bool HzLimitPark { get; set; }
        [UniqueSetting]
        public double AxisHzTrackingLimit { get; set; }
        [UniqueSetting]
        public double AxisUpperLimitY { get; set; }
        [UniqueSetting]
        public double AxisLowerLimitY { get; set; }
        [UniqueSetting]
        public double AxisLimitX { get; set; }
        
        // PEC/PPEC Settings
        [CommonSetting]
        public bool PecOn { get; set; }
        [CommonSetting]
        public bool PpecOn { get; set; }
        [CommonSetting]
        public bool AlternatingPPEC { get; set; }
        [CommonSetting]
        public int PecOffSet { get; set; }
        [CommonSetting]
        public string PecWormFile { get; set; } = null!;
        [CommonSetting]
        public string Pec360File { get; set; } = null!;
        [CommonSetting]
        public string PecMode { get; set; } = null!;

        // Encoders
        [CommonSetting]
        public bool EncodersOn { get; set; }

        // Hand Controller Settings
        [CommonSetting]
        public string HcSpeed { get; set; } = null!;
        [CommonSetting]
        public string HcMode { get; set; } = null!;
        [CommonSetting]
        public bool HcAntiRa { get; set; }
        [CommonSetting]
        public bool HcAntiDec { get; set; }
        [CommonSetting]
        public bool HcFlipEW { get; set; }
        [CommonSetting]
        public bool HcFlipNS { get; set; }
        [CommonSetting]
        public bool DisableKeysOnGoTo { get; set; }

        // Camera/Eyepiece Settings
        [CommonSetting]
        public double EyepieceFS { get; set; }
        
        // Capabilities
        [CommonSetting]
        public bool CanAlignMode { get; set; }
        [CommonSetting]
        public bool CanAltAz { get; set; }
        [CommonSetting]
        public bool CanDoesRefraction { get; set; }
        [CommonSetting]
        public bool CanEquatorial { get; set; }
        [CommonSetting]
        public bool CanFindHome { get; set; }
        [CommonSetting]
        public bool CanLatLongElev { get; set; }
        [CommonSetting]
        public bool CanOptics { get; set; }
        [CommonSetting]
        public bool CanPark { get; set; }
        [CommonSetting]
        public bool CanPierSide { get; set; }
        [CommonSetting]
        public bool CanPulseGuide { get; set; }
        [CommonSetting]
        public bool CanSetEquRates { get; set; }
        [CommonSetting]
        public bool CanSetGuideRates { get; set; }
        [CommonSetting]
        public bool CanSetPark { get; set; }
        [UniqueSetting]
        public bool CanSetPierSide { get; set; }
        [CommonSetting]
        public bool CanSetTracking { get; set; }
        [CommonSetting]
        public bool CanSiderealTime { get; set; }
        [CommonSetting]
        public bool CanSlew { get; set; }
        [CommonSetting]
        public bool CanSlewAltAz { get; set; }
        [CommonSetting]
        public bool CanSlewAltAzAsync { get; set; }
        [CommonSetting]
        public bool CanSlewAsync { get; set; }
        [CommonSetting]
        public bool CanSync { get; set; }
        [CommonSetting]
        public bool CanSyncAltAz { get; set; }
        [CommonSetting]
        public bool CanUnpark { get; set; }
        [CommonSetting]
        public bool CanTrackingRates { get; set; }
        [CommonSetting]
        public bool CanSetDeclinationRate { get; set; }
        [CommonSetting]
        public bool CanSetRightAscensionRate { get; set; }
        
        // Advanced Settings
        [CommonSetting]
        public bool AllowAdvancedCommandSet { get; set; }
        [CommonSetting]
        public double MaximumSlewRate { get; set; }
        [CommonSetting]
        public double GotoPrecision { get; set; }
        [CommonSetting]
        public bool FullCurrent { get; set; }
        [CommonSetting]
        public int NumMoveAxis { get; set; }
        // Display Settings
        [CommonSetting]
        public int DisplayInterval { get; set; }
        [CommonSetting]
        public bool TraceLogger { get; set; }
        [CommonSetting]
        public int PolarLedLevel { get; set; }

        // GPS Settings
        [CommonSetting]
        public int GpsPort { get; set; }
        [CommonSetting]
        public string GpsBaudRate { get; set; } = null!;

        // Instrument Info
        [CommonSetting]
        public string InstrumentDescription { get; set; } = null!;
        [CommonSetting]
        public string InstrumentName { get; set; } = null!;

        // Alt-Az Tracking
        [CommonSetting]
        public int AltAzTrackingUpdateInterval { get; set; }

        // Polar Alignment
        [UniqueSetting]
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
