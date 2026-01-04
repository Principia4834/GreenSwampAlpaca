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
using GreenSwamp.Alpaca.Shared.Transport;
using System.ComponentModel;
using System.Numerics;
using GreenSwamp.Alpaca.MountControl.Pulses;
using Handshake = System.IO.Ports.Handshake;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase A: Instance-based wrapper around static SkySettings.
    /// Converts static access to instance-based while keeping Properties.SkyTelescope backend.
    /// All side effects are preserved by delegating to static setters.
    /// </summary>
    public class SkySettingsInstance
    {
        #region Singleton Pattern
        
        private static SkySettingsInstance? _instance;
        
        /// <summary>
        /// Gets the singleton instance. Throws if not initialized.
        /// </summary>
        public static SkySettingsInstance Instance => _instance 
            ?? throw new InvalidOperationException("SkySettingsInstance not initialized. Call Initialize() first.");

        /// <summary>
        /// Initialize the singleton instance and load settings from static SkySettings.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null)
            {
                throw new InvalidOperationException("SkySettingsInstance already initialized");
            }
            
            _instance = new SkySettingsInstance();
            SkySettings.Load(); // Use existing Load() method
        }

        /// <summary>
        /// Private constructor for singleton pattern
        /// </summary>
        private SkySettingsInstance()
        {
        }

        #endregion

        #region Events

        /// <summary>
        /// Forwards PropertyChanged events from static SkySettings
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => SkySettings.StaticPropertyChanged += value;
            remove => SkySettings.StaticPropertyChanged -= value;
        }

        #endregion

        #region Batch 1: Connection & Mount Settings (20 properties)

        /// <summary>
        /// Mount type (Simulator or SkyWatcher)
        /// ?? SIDE EFFECT: Sets SkyServer.IsMountRunning = false
        /// </summary>
        public MountType Mount
        {
            get => SkySettings.Mount;
            set => SkySettings.Mount = value;
        }

        /// <summary>
        /// Serial port or IP address
        /// </summary>
        public string Port
        {
            get => SkySettings.Port;
            set => SkySettings.Port = value;
        }

        /// <summary>
        /// Serial port baud rate
        /// </summary>
        public SerialSpeed BaudRate
        {
            get => SkySettings.BaudRate;
            set => SkySettings.BaudRate = value;
        }

        /// <summary>
        /// Serial port handshake (read-only)
        /// </summary>
        public Handshake HandShake => SkySettings.HandShake;

        /// <summary>
        /// Serial port data bits (read-only)
        /// </summary>
        public int DataBits => SkySettings.DataBits;

        /// <summary>
        /// Serial port read timeout (read-only)
        /// </summary>
        public int ReadTimeout => SkySettings.ReadTimeout;

        /// <summary>
        /// DTR enable flag (read-only)
        /// </summary>
        public bool DtrEnable => SkySettings.DtrEnable;

        /// <summary>
        /// RTS enable flag (read-only)
        /// </summary>
        public bool RtsEnable => SkySettings.RtsEnable;

        /// <summary>
        /// Mount alignment mode (AltAz, GermanPolar, Polar)
        /// </summary>
        public AlignmentMode AlignmentMode
        {
            get => SkySettings.AlignmentMode;
            set => SkySettings.AlignmentMode = value;
        }

        /// <summary>
        /// Equatorial coordinate type
        /// </summary>
        public EquatorialCoordinateType EquatorialCoordinateType
        {
            get => SkySettings.EquatorialCoordinateType;
            set => SkySettings.EquatorialCoordinateType = value;
        }

        /// <summary>
        /// Mount is at park position
        /// </summary>
        public bool AtPark
        {
            get => SkySettings.AtPark;
            set => SkySettings.AtPark = value;
        }

        /// <summary>
        /// Tracking rate (Sidereal, Lunar, Solar, King)
        /// ?? SIDE EFFECT: Resets rate values if not Sidereal
        /// </summary>
        public DriveRate TrackingRate
        {
            get => SkySettings.TrackingRate;
            set => SkySettings.TrackingRate = value;
        }

        /// <summary>
        /// GPS COM port
        /// </summary>
        public string GpsComPort
        {
            get => SkySettings.GpsComPort;
            set => SkySettings.GpsComPort = value;
        }

        /// <summary>
        /// GPS baud rate
        /// </summary>
        public SerialSpeed GpsBaudRate
        {
            get => SkySettings.GpsBaudRate;
            set => SkySettings.GpsBaudRate = value;
        }

        /// <summary>
        /// Front panel graphic display mode
        /// </summary>
        public FrontGraphic FrontGraphic
        {
            get => SkySettings.FrontGraphic;
            set => SkySettings.FrontGraphic = value;
        }

        /// <summary>
        /// Hand controller slew speed
        /// </summary>
        public SlewSpeed HcSpeed
        {
            get => SkySettings.HcSpeed;
            set => SkySettings.HcSpeed = value;
        }

        /// <summary>
        /// Hand controller mode (Axes, Guiding, Pulse)
        /// </summary>
        public HcMode HcMode
        {
            get => SkySettings.HcMode;
            set => SkySettings.HcMode = value;
        }

        /// <summary>
        /// PEC mode (PecWorm or Pec360)
        /// </summary>
        public PecMode PecMode
        {
            get => SkySettings.PecMode;
            set => SkySettings.PecMode = value;
        }

        /// <summary>
        /// Polar mode (Left or Right)
        /// </summary>
        public PolarMode PolarMode
        {
            get => SkySettings.PolarMode;
            set => SkySettings.PolarMode = value;
        }

        #endregion

        #region Batch 2: Location & Custom Gearing (15 properties)

        /// <summary>
        /// Observatory latitude in degrees
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(SetSouthernHemisphere)
        /// </summary>
        public double Latitude
        {
            get => SkySettings.Latitude;
            set => SkySettings.Latitude = value;
        }

        /// <summary>
        /// Observatory longitude in degrees
        /// </summary>
        public double Longitude
        {
            get => SkySettings.Longitude;
            set => SkySettings.Longitude = value;
        }

        /// <summary>
        /// Observatory elevation in meters
        /// </summary>
        public double Elevation
        {
            get => SkySettings.Elevation;
            set => SkySettings.Elevation = value;
        }

        /// <summary>
        /// Use custom gearing settings
        /// </summary>
        public bool CustomGearing
        {
            get => SkySettings.CustomGearing;
            set => SkySettings.CustomGearing = value;
        }

        /// <summary>
        /// Custom RA 360-degree steps
        /// </summary>
        public int CustomRa360Steps
        {
            get => SkySettings.CustomRa360Steps;
            set => SkySettings.CustomRa360Steps = value;
        }

        /// <summary>
        /// Custom RA worm teeth count
        /// </summary>
        public int CustomRaWormTeeth
        {
            get => SkySettings.CustomRaWormTeeth;
            set => SkySettings.CustomRaWormTeeth = value;
        }

        /// <summary>
        /// Custom Dec 360-degree steps
        /// </summary>
        public int CustomDec360Steps
        {
            get => SkySettings.CustomDec360Steps;
            set => SkySettings.CustomDec360Steps = value;
        }

        /// <summary>
        /// Custom Dec worm teeth count
        /// </summary>
        public int CustomDecWormTeeth
        {
            get => SkySettings.CustomDecWormTeeth;
            set => SkySettings.CustomDecWormTeeth = value;
        }

        /// <summary>
        /// Custom RA tracking offset
        /// </summary>
        public int CustomRaTrackingOffset
        {
            get => SkySettings.CustomRaTrackingOffset;
            set => SkySettings.CustomRaTrackingOffset = value;
        }

        /// <summary>
        /// Custom Dec tracking offset
        /// </summary>
        public int CustomDecTrackingOffset
        {
            get => SkySettings.CustomDecTrackingOffset;
            set => SkySettings.CustomDecTrackingOffset = value;
        }

        /// <summary>
        /// Allow advanced command set
        /// </summary>
        public bool AllowAdvancedCommandSet
        {
            get => SkySettings.AllowAdvancedCommandSet;
            set => SkySettings.AllowAdvancedCommandSet = value;
        }

        #endregion

        #region Batch 3: Tracking Rates (8 properties)

        /// <summary>
        /// Sidereal tracking rate
        /// </summary>
        public double SiderealRate
        {
            get => SkySettings.SiderealRate;
            set => SkySettings.SiderealRate = value;
        }

        /// <summary>
        /// Lunar tracking rate
        /// </summary>
        public double LunarRate
        {
            get => SkySettings.LunarRate;
            set => SkySettings.LunarRate = value;
        }

        /// <summary>
        /// Solar tracking rate
        /// </summary>
        public double SolarRate
        {
            get => SkySettings.SolarRate;
            set => SkySettings.SolarRate = value;
        }

        /// <summary>
        /// King tracking rate
        /// </summary>
        public double KingRate
        {
            get => SkySettings.KingRate;
            set => SkySettings.KingRate = value;
        }

        /// <summary>
        /// Axis tracking limit
        /// </summary>
        public double AxisTrackingLimit
        {
            get => SkySettings.AxisTrackingLimit;
            set => SkySettings.AxisTrackingLimit = value;
        }

        /// <summary>
        /// Horizon axis tracking limit
        /// </summary>
        public double AxisHzTrackingLimit
        {
            get => SkySettings.AxisHzTrackingLimit;
            set => SkySettings.AxisHzTrackingLimit = value;
        }

        /// <summary>
        /// Display update interval in milliseconds
        /// </summary>
        public int DisplayInterval
        {
            get => SkySettings.DisplayInterval;
            set => SkySettings.DisplayInterval = value;
        }

        /// <summary>
        /// Alt-Az tracking update interval
        /// </summary>
        public int AltAzTrackingUpdateInterval
        {
            get => SkySettings.AltAzTrackingUpdateInterval;
            set => SkySettings.AltAzTrackingUpdateInterval = value;
        }

        #endregion

        #region Batch 4: Guiding (8 properties with SIDE EFFECTS)

        /// <summary>
        /// Minimum pulse duration for RA axis in milliseconds
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(MinPulseRa)
        /// </summary>
        public int MinPulseRa
        {
            get => SkySettings.MinPulseRa;
            set => SkySettings.MinPulseRa = value;
        }

        /// <summary>
        /// Minimum pulse duration for Dec axis in milliseconds
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(MinPulseDec)
        /// </summary>
        public int MinPulseDec
        {
            get => SkySettings.MinPulseDec;
            set => SkySettings.MinPulseDec = value;
        }

        /// <summary>
        /// Use Dec pulse for goto operations
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(DecPulseToGoTo)
        /// </summary>
        public bool DecPulseToGoTo
        {
            get => SkySettings.DecPulseToGoTo;
            set => SkySettings.DecPulseToGoTo = value;
        }

        /// <summary>
        /// ST4 guide rate
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(SetSt4Guiderate)
        /// </summary>
        public int St4GuideRate
        {
            get => SkySettings.St4GuideRate;
            set => SkySettings.St4GuideRate = value;
        }

        /// <summary>
        /// Guide rate offset for X axis
        /// ?? SIDE EFFECT: Calls SkyServer.SetGuideRates()
        /// </summary>
        public double GuideRateOffsetX
        {
            get => SkySettings.GuideRateOffsetX;
            set => SkySettings.GuideRateOffsetX = value;
        }

        /// <summary>
        /// Guide rate offset for Y axis
        /// ?? SIDE EFFECT: Calls SkyServer.SetGuideRates()
        /// </summary>
        public double GuideRateOffsetY
        {
            get => SkySettings.GuideRateOffsetY;
            set => SkySettings.GuideRateOffsetY = value;
        }

        /// <summary>
        /// RA backlash in steps
        /// </summary>
        public int RaBacklash
        {
            get => SkySettings.RaBacklash;
            set => SkySettings.RaBacklash = value;
        }

        /// <summary>
        /// Dec backlash in steps
        /// </summary>
        public int DecBacklash
        {
            get => SkySettings.DecBacklash;
            set => SkySettings.DecBacklash = value;
        }

        #endregion

        #region Batch 5: Optics & Camera (6 properties)

        /// <summary>
        /// Telescope focal length in millimeters
        /// </summary>
        public double FocalLength
        {
            get => SkySettings.FocalLength;
            set => SkySettings.FocalLength = value;
        }

        /// <summary>
        /// Camera sensor width in millimeters
        /// </summary>
        public double CameraWidth
        {
            get => SkySettings.CameraWidth;
            set => SkySettings.CameraWidth = value;
        }

        /// <summary>
        /// Camera sensor height in millimeters
        /// </summary>
        public double CameraHeight
        {
            get => SkySettings.CameraHeight;
            set => SkySettings.CameraHeight = value;
        }

        /// <summary>
        /// Eyepiece field stop
        /// </summary>
        public double EyepieceFs
        {
            get => SkySettings.EyepieceFs;
            set => SkySettings.EyepieceFs = value;
        }

        /// <summary>
        /// Aperture area (read-only, calculated)
        /// </summary>
        public double ApertureArea => SkySettings.ApertureArea;

        /// <summary>
        /// Aperture diameter (read-only, calculated)
        /// </summary>
        public double ApertureDiameter => SkySettings.ApertureDiameter;

        #endregion

        #region Batch 6: Advanced Settings (6 properties with SIDE EFFECTS)

        /// <summary>
        /// Maximum slew rate
        /// ?? SIDE EFFECT: Calls SkyServer.SetSlewRates(value)
        /// </summary>
        public double MaxSlewRate
        {
            get => SkySettings.MaxSlewRate;
            set => SkySettings.MaxSlewRate = value;
        }

        /// <summary>
        /// Use full current at low speed
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(FullCurrent)
        /// </summary>
        public bool FullCurrent
        {
            get => SkySettings.FullCurrent;
            set => SkySettings.FullCurrent = value;
        }

        /// <summary>
        /// Enable encoders
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(Encoders)
        /// </summary>
        public bool Encoders
        {
            get => SkySettings.Encoders;
            set => SkySettings.Encoders = value;
        }

        /// <summary>
        /// Use alternating PPEC
        /// ?? SIDE EFFECT: Calls SkyServer.SkyTasks(AlternatingPpec)
        /// </summary>
        public bool AlternatingPPec
        {
            get => SkySettings.AlternatingPPec;
            set => SkySettings.AlternatingPPec = value;
        }

        /// <summary>
        /// Enable global stop
        /// </summary>
        public bool GlobalStopOn
        {
            get => SkySettings.GlobalStopOn;
            set => SkySettings.GlobalStopOn = value;
        }

        /// <summary>
        /// Enable refraction correction
        /// </summary>
        public bool Refraction
        {
            get => SkySettings.Refraction;
            set => SkySettings.Refraction = value;
        }

        /// <summary>
        /// Goto precision (read-only)
        /// </summary>
        public double GotoPrecision => SkySettings.GotoPrecision;

        #endregion

        #region Batch 7: Home & Park (12 properties)

        /// <summary>
        /// Home position X axis in degrees
        /// </summary>
        public double HomeAxisX
        {
            get => SkySettings.HomeAxisX;
            set => SkySettings.HomeAxisX = value;
        }

        /// <summary>
        /// Home position Y axis in degrees
        /// </summary>
        public double HomeAxisY
        {
            get => SkySettings.HomeAxisY;
            set => SkySettings.HomeAxisY = value;
        }

        /// <summary>
        /// Auto home X axis sensor position in degrees
        /// </summary>
        public double AutoHomeAxisX
        {
            get => SkySettings.AutoHomeAxisX;
            set => SkySettings.AutoHomeAxisX = value;
        }

        /// <summary>
        /// Auto home Y axis sensor position in degrees
        /// </summary>
        public double AutoHomeAxisY
        {
            get => SkySettings.AutoHomeAxisY;
            set => SkySettings.AutoHomeAxisY = value;
        }

        /// <summary>
        /// Show home warning dialog
        /// </summary>
        public bool HomeWarning
        {
            get => SkySettings.HomeWarning;
            set => SkySettings.HomeWarning = value;
        }

        /// <summary>
        /// Current park position name
        /// </summary>
        public string ParkName
        {
            get => SkySettings.ParkName;
            set => SkySettings.ParkName = value;
        }

        /// <summary>
        /// Park axes position in mount axes values
        /// </summary>
        public double[] ParkAxes
        {
            get => SkySettings.ParkAxes;
            set => SkySettings.ParkAxes = value;
        }

        /// <summary>
        /// List of park positions
        /// </summary>
        public List<ParkPosition> ParkPositions
        {
            get => SkySettings.ParkPositions;
            set => SkySettings.ParkPositions = value;
        }

        /// <summary>
        /// Enable limit park
        /// </summary>
        public bool LimitPark
        {
            get => SkySettings.LimitPark;
            set => SkySettings.LimitPark = value;
        }

        /// <summary>
        /// Park limit position name
        /// </summary>
        public string ParkLimitName
        {
            get => SkySettings.ParkLimitName;
            set => SkySettings.ParkLimitName = value;
        }

        #endregion

        #region Batch 8: Limits (9 properties)

        /// <summary>
        /// Hour angle limit in degrees
        /// </summary>
        public double HourAngleLimit
        {
            get => SkySettings.HourAngleLimit;
            set => SkySettings.HourAngleLimit = value;
        }

        /// <summary>
        /// X axis limit in degrees
        /// </summary>
        public double AxisLimitX
        {
            get => SkySettings.AxisLimitX;
            set => SkySettings.AxisLimitX = value;
        }

        /// <summary>
        /// Y axis upper limit in degrees
        /// </summary>
        public double AxisUpperLimitY
        {
            get => SkySettings.AxisUpperLimitY;
            set => SkySettings.AxisUpperLimitY = value;
        }

        /// <summary>
        /// Y axis lower limit in degrees
        /// </summary>
        public double AxisLowerLimitY
        {
            get => SkySettings.AxisLowerLimitY;
            set => SkySettings.AxisLowerLimitY = value;
        }

        /// <summary>
        /// Enable tracking limits
        /// </summary>
        public bool LimitTracking
        {
            get => SkySettings.LimitTracking;
            set => SkySettings.LimitTracking = value;
        }

        /// <summary>
        /// Enable sync limits
        /// </summary>
        public bool SyncLimitOn
        {
            get => SkySettings.SyncLimitOn;
            set => SkySettings.SyncLimitOn = value;
        }

        /// <summary>
        /// Enable horizon limit tracking
        /// </summary>
        public bool HzLimitTracking
        {
            get => SkySettings.HzLimitTracking;
            set => SkySettings.HzLimitTracking = value;
        }

        /// <summary>
        /// Enable horizon limit park
        /// </summary>
        public bool HzLimitPark
        {
            get => SkySettings.HzLimitPark;
            set => SkySettings.HzLimitPark = value;
        }

        /// <summary>
        /// Horizon park limit position name
        /// </summary>
        public string ParkHzLimitName
        {
            get => SkySettings.ParkHzLimitName;
            set => SkySettings.ParkHzLimitName = value;
        }

        /// <summary>
        /// Sync limit value (read-only)
        /// </summary>
        public int SyncLimit => SkySettings.SyncLimit;

        #endregion

        #region Batch 9: PEC (6 properties)

        /// <summary>
        /// Enable PEC
        /// </summary>
        public bool PecOn
        {
            get => SkySettings.PecOn;
            set => SkySettings.PecOn = value;
        }

        /// <summary>
        /// Enable PPEC
        /// </summary>
        public bool PPecOn
        {
            get => SkySettings.PPecOn;
            set => SkySettings.PPecOn = value;
        }

        /// <summary>
        /// PEC offset
        /// </summary>
        public int PecOffSet
        {
            get => SkySettings.PecOffSet;
            set => SkySettings.PecOffSet = value;
        }

        /// <summary>
        /// PEC worm file path
        /// </summary>
        public string PecWormFile
        {
            get => SkySettings.PecWormFile;
            set => SkySettings.PecWormFile = value;
        }

        /// <summary>
        /// PEC 360 file path
        /// </summary>
        public string Pec360File
        {
            get => SkySettings.Pec360File;
            set => SkySettings.Pec360File = value;
        }

        /// <summary>
        /// Polar LED level (0-255)
        /// </summary>
        public int PolarLedLevel
        {
            get => SkySettings.PolarLedLevel;
            set => SkySettings.PolarLedLevel = value;
        }

        #endregion

        #region Batch 10: Hand Controller (5 properties)

        /// <summary>
        /// Invert RA hand controller direction
        /// </summary>
        public bool HcAntiRa
        {
            get => SkySettings.HcAntiRa;
            set => SkySettings.HcAntiRa = value;
        }

        /// <summary>
        /// Invert Dec hand controller direction
        /// </summary>
        public bool HcAntiDec
        {
            get => SkySettings.HcAntiDec;
            set => SkySettings.HcAntiDec = value;
        }

        /// <summary>
        /// Flip East/West direction
        /// </summary>
        public bool HcFlipEw
        {
            get => SkySettings.HcFlipEw;
            set => SkySettings.HcFlipEw = value;
        }

        /// <summary>
        /// Flip North/South direction
        /// </summary>
        public bool HcFlipNs
        {
            get => SkySettings.HcFlipNs;
            set => SkySettings.HcFlipNs = value;
        }

        /// <summary>
        /// Hand controller pulse guide settings
        /// </summary>
        public List<HcPulseGuide> HcPulseGuides
        {
            get => SkySettings.HcPulseGuides;
            set => SkySettings.HcPulseGuides = value;
        }

        /// <summary>
        /// Disable keys during goto
        /// </summary>
        public bool DisableKeysOnGoTo
        {
            get => SkySettings.DisableKeysOnGoTo;
            set => SkySettings.DisableKeysOnGoTo = value;
        }

        #endregion

        #region Batch 11: Miscellaneous (5 properties)

        /// <summary>
        /// Ambient temperature in Celsius
        /// </summary>
        public double Temperature
        {
            get => SkySettings.Temperature;
            set => SkySettings.Temperature = value;
        }

        /// <summary>
        /// Instrument description (read-only)
        /// </summary>
        public string InstrumentDescription => SkySettings.InstrumentDescription;

        /// <summary>
        /// Instrument name (read-only)
        /// </summary>
        public string InstrumentName => SkySettings.InstrumentName;

        /// <summary>
        /// Axis model offsets (read-only)
        /// </summary>
        public Vector3 AxisModelOffsets => SkySettings.AxisModelOffsets;

        /// <summary>
        /// Flip RA gauge display
        /// </summary>
        public bool RaGaugeFlip
        {
            get => SkySettings.RaGaugeFlip;
            set => SkySettings.RaGaugeFlip = value;
        }

        /// <summary>
        /// Auto track enabled (read-only)
        /// </summary>
        public bool AutoTrack => SkySettings.AutoTrack;

        /// <summary>
        /// RA tracking offset (read-only)
        /// </summary>
        public int RaTrackingOffset => SkySettings.RaTrackingOffset;

        #endregion

        #region Batch 12: Capabilities (28 read-only properties)

        /// <summary>
        /// Can set alignment mode
        /// </summary>
        public bool CanAlignMode => SkySettings.CanAlignMode;

        /// <summary>
        /// Can operate in Alt-Az mode
        /// </summary>
        public bool CanAltAz => SkySettings.CanAltAz;

        /// <summary>
        /// Can operate in equatorial mode
        /// </summary>
        public bool CanEquatorial => SkySettings.CanEquatorial;

        /// <summary>
        /// Can find home position
        /// </summary>
        public bool CanFindHome => SkySettings.CanFindHome;

        /// <summary>
        /// Can set latitude/longitude/elevation
        /// </summary>
        public bool CanLatLongElev => SkySettings.CanLatLongElev;

        /// <summary>
        /// Can set optical parameters
        /// </summary>
        public bool CanOptics => SkySettings.CanOptics;

        /// <summary>
        /// Can park
        /// </summary>
        public bool CanPark => SkySettings.CanPark;

        /// <summary>
        /// Can pulse guide
        /// </summary>
        public bool CanPulseGuide => SkySettings.CanPulseGuide;

        /// <summary>
        /// Can set equatorial rates
        /// </summary>
        public bool CanSetEquRates => SkySettings.CanSetEquRates;

        /// <summary>
        /// Can set declination rate
        /// </summary>
        public bool CanSetDeclinationRate => SkySettings.CanSetDeclinationRate;

        /// <summary>
        /// Can set guide rates
        /// </summary>
        public bool CanSetGuideRates => SkySettings.CanSetGuideRates;

        /// <summary>
        /// Can set park position
        /// </summary>
        public bool CanSetPark
        {
            get => SkySettings.CanSetPark;
            set => SkySettings.CanSetPark = value;
        }

        /// <summary>
        /// Can set pier side
        /// </summary>
        public bool CanSetPierSide
        {
            get => SkySettings.CanSetPierSide;
            set => SkySettings.CanSetPierSide = value;
        }

        /// <summary>
        /// Can set right ascension rate
        /// </summary>
        public bool CanSetRightAscensionRate => SkySettings.CanSetRightAscensionRate;

        /// <summary>
        /// Can set tracking
        /// </summary>
        public bool CanSetTracking => SkySettings.CanSetTracking;

        /// <summary>
        /// Can provide sidereal time
        /// </summary>
        public bool CanSiderealTime => SkySettings.CanSiderealTime;

        /// <summary>
        /// Can slew
        /// </summary>
        public bool CanSlew => SkySettings.CanSlew;

        /// <summary>
        /// Can slew Alt-Az
        /// </summary>
        public bool CanSlewAltAz => SkySettings.CanSlewAltAz;

        /// <summary>
        /// Can slew Alt-Az asynchronously
        /// </summary>
        public bool CanSlewAltAzAsync => SkySettings.CanSlewAltAzAsync;

        /// <summary>
        /// Can slew asynchronously
        /// </summary>
        public bool CanSlewAsync => SkySettings.CanSlewAsync;

        /// <summary>
        /// Can sync
        /// </summary>
        public bool CanSync => SkySettings.CanSync;

        /// <summary>
        /// Can sync Alt-Az
        /// </summary>
        public bool CanSyncAltAz => SkySettings.CanSyncAltAz;

        /// <summary>
        /// Can set tracking rates
        /// </summary>
        public bool CanTrackingRates => SkySettings.CanTrackingRates;

        /// <summary>
        /// Can unpark
        /// </summary>
        public bool CanUnPark => SkySettings.CanUnPark;

        /// <summary>
        /// Prevent sync past meridian
        /// </summary>
        public bool NoSyncPastMeridian => SkySettings.NoSyncPastMeridian;

        /// <summary>
        /// Number of move axis
        /// </summary>
        public int NumMoveAxis => SkySettings.NumMoveAxis;

        /// <summary>
        /// Version one protocol
        /// </summary>
        public bool VersionOne => SkySettings.VersionOne;

        #endregion

        #region Methods

        /// <summary>
        /// Saves settings using static SkySettings.Save()
        /// This will trigger bridge sync to new settings system
        /// </summary>
        public void Save()
        {
            SkySettings.Save();
        }

        /// <summary>
        /// Loads settings using static SkySettings.Load()
        /// </summary>
        public void Load()
        {
            SkySettings.Load();
        }

        /// <summary>
        /// Resets park positions using static SkySettings.ResetParkPositions()
        /// </summary>
        public void ResetParkPositions()
        {
            SkySettings.ResetParkPositions();
        }

        #endregion
    }
}
