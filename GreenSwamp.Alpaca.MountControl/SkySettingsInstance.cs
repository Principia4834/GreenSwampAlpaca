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
using GreenSwamp.Alpaca.MountControl.Pulses;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Settings.Services;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Handshake = System.IO.Ports.Handshake;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Instance-based settings - owns all data (no static delegation).
    /// Multi-telescope ready: each MountInstance has its own SkySettingsInstance.
    /// Direct JSON persistence via IVersionedSettingsService (no bridge required).
    /// </summary>
    public class SkySettingsInstance : INotifyPropertyChanged
    {
        #region Private Fields (134 backing fields)

        // Services
        private readonly IVersionedSettingsService _settingsService;
        private readonly IProfileLoaderService? _profileLoaderService;
        private CancellationTokenSource? _saveCts;

        // Connection & Mount Settings (20 fields)
        private MountType _mount = MountType.Simulator;
        private string _port = "COM3";
        private SerialSpeed _baudRate = SerialSpeed.ps9600;
        private Handshake _handShake = Handshake.None;
        private int _dataBits = 8;
        private int _readTimeout = 5000;
        private bool _dtrEnable = false;
        private bool _rtsEnable = false;
        private AlignmentMode _alignmentMode = AlignmentMode.GermanPolar;
        private EquatorialCoordinateType _equatorialCoordinateType = EquatorialCoordinateType.Other;
        private bool _atPark = false;
        private DriveRate _trackingRate = DriveRate.Sidereal;
        private string _gpsComPort = string.Empty;
        private SerialSpeed _gpsBaudRate = SerialSpeed.ps9600;
        private SlewSpeed _hcSpeed = SlewSpeed.Eight;
        private HcMode _hcMode = HcMode.Guiding;
        private PecMode _pecMode = PecMode.PecWorm;
        private PolarMode _polarMode = PolarMode.Left;

        // Location & Custom Gearing (11 fields)
        private double _latitude = 51.48;
        private double _longitude = -0.0;
        private double _elevation = 0.0;
        private bool _customGearing = false;
        private int _customRa360Steps = 9024000;
        private int _customRaWormTeeth = 130;
        private int _customDec360Steps = 9024000;
        private int _customDecWormTeeth = 130;
        private int _customRaTrackingOffset = 0;
        private int _customDecTrackingOffset = 0;
        private bool _allowAdvancedCommandSet = false;

        // Tracking Rates (8 fields)
        private double _siderealRate = 15.0410671786691;
        private double _lunarRate = 14.511415534643;
        private double _solarRate = 15.0;
        private double _kingRate = 15.0369;
        private double _axisTrackingLimit = 180.0;
        private double _axisHzTrackingLimit = 180.0;
        private int _displayInterval = 200;
        private int _altAzTrackingUpdateInterval = 2500;

        // Guiding (8 fields)
        private int _minPulseRa = 20;
        private int _minPulseDec = 20;
        private bool _decPulseToGoTo = false;
        private int _st4GuideRate = 2;
        private double _guideRateOffsetX = 0.5;
        private double _guideRateOffsetY = 0.5;
        private int _raBacklash = 0;
        private int _decBacklash = 0;

        // Optics & Camera (6 fields)
        private double _focalLength = 1000.0;
        private double _cameraWidth = 10.0;
        private double _cameraHeight = 10.0;
        private double _eyepieceFs = 50.0;
        private double _apertureArea = 0.0;
        private double _apertureDiameter = 0.0;

        // Advanced Settings (7 fields)
        private double _maxSlewRate = 3.4;
        private bool _fullCurrent = false;
        private bool _encoders = false;
        private bool _alternatingPPec = false;
        private bool _globalStopOn = false;
        private bool _refraction = false;
        private double _gotoPrecision = 0.001;

        // Home & Park (9 fields)
        private double _homeAxisX = 0.0;
        private double _homeAxisY = 0.0;
        private double _autoHomeAxisX = 0.0;
        private double _autoHomeAxisY = 0.0;
        private string _parkName = "Default";
        private double[] _parkAxes = new[] { 0.0, 0.0 };
        private List<ParkPosition> _parkPositions = new();
        private bool _limitPark = false;
        private string _parkLimitName = string.Empty;

        // Limits (9 fields)
        private double _hourAngleLimit = 15.0;
        private double _axisLimitX = 180.0;
        private double _axisUpperLimitY = 90.0;
        private double _axisLowerLimitY = -90.0;
        private bool _limitTracking = false;
        private bool _syncLimitOn = false;
        private bool _hzLimitTracking = false;
        private bool _hzLimitPark = false;
        private string _parkHzLimitName = string.Empty;
        private int _syncLimit = 5;

        // PEC (6 fields)
        private bool _pecOn = false;
        private bool _pPecOn = false;
        private int _pecOffSet = 0;
        private string _pecWormFile = string.Empty;
        private string _pec360File = string.Empty;
        private int _polarLedLevel = 0;

        // Hand Controller (6 fields)
        private bool _hcAntiRa = false;
        private bool _hcAntiDec = false;
        private bool _hcFlipEw = false;
        private bool _hcFlipNs = false;
        private List<HcPulseGuide> _hcPulseGuides = new();
        private bool _disableKeysOnGoTo = false;

        // Miscellaneous (5 fields)
        private double _temperature = 15.0;
        private string _instrumentDescription = "GreenSwamp Alpaca Server";
        private string _instrumentName = "GreenSwamp Mount";
        private bool _autoTrack = false;
        private int _raTrackingOffset = 0;

        // Capabilities (28 fields - read-only)
        private bool _canAlignMode = true;
        private bool _canAltAz = true;
        private bool _canEquatorial = true;
        private bool _canFindHome = true;
        private bool _canLatLongElev = true;
        private bool _canOptics = true;
        private bool _canPark = true;
        private bool _canPulseGuide = true;
        private bool _canSetEquRates = true;
        private bool _canSetDeclinationRate = true;
        private bool _canSetGuideRates = true;
        private bool _canSetPark = true;
        private bool _canSetPierSide = true;
        private bool _canSetRightAscensionRate = true;
        private bool _canSetTracking = true;
        private bool _canSiderealTime = true;
        private bool _canSlew = true;
        private bool _canSlewAltAz = true;
        private bool _canSlewAltAzAsync = true;
        private bool _canSlewAsync = true;
        private bool _canSync = true;
        private bool _canSyncAltAz = true;
        private bool _canTrackingRates = true;
        private bool _canUnPark = true;
        private bool _noSyncPastMeridian = false;
        private int _numMoveAxis = 2;
        private bool _versionOne = true;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates instance with direct JSON persistence and optional profile loading
        /// </summary>
        /// <param name="settingsService">Required: Settings service for JSON persistence</param>
        /// <param name="profileLoaderService">Optional: Profile loader service (null for backward compatibility)</param>
        public SkySettingsInstance(
            IVersionedSettingsService settingsService,
            IProfileLoaderService? profileLoaderService = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _profileLoaderService = profileLoaderService;

            // Load settings from profile or JSON
            var settings = LoadSettingsFromSource();

            // Apply settings to instance fields
            ApplySettings(settings);

            LogSettings("Initialized", $"Mount:{_mount}|Port:{_port}");
        }

        #endregion

        #region Helper Methods for Coordinate Transformations

        /// <summary>
        /// Create AxesContext from current instance settings for coordinate transformations
        /// Used by ParkAxes and ParkPositions properties
        /// </summary>
        private AxesContext GetAxesContext()
        {
            return AxesContext.FromSettings(this);
        }

        #endregion

        #region Events

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Auto-save after property changes (debounced)
            QueueSave();
        }

        #endregion

        #region Batch 1: Connection & Mount Settings (20 properties)

        public MountType Mount
        {
            get => _mount;
            set
            {
                if (_mount != value)
                {
                    _mount = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Stop mount when type changes
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.IsMountRunning = false;
                    }
                }
            }
        }

        public string Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value ?? "COM3";
                    OnPropertyChanged();
                }
            }
        }

        public SerialSpeed BaudRate
        {
            get => _baudRate;
            set
            {
                if (_baudRate != value)
                {
                    _baudRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public Handshake HandShake => _handShake;
        public int DataBits => _dataBits;
        public int ReadTimeout => _readTimeout;
        public bool DtrEnable => _dtrEnable;
        public bool RtsEnable => _rtsEnable;

        public AlignmentMode AlignmentMode
        {
            get => _alignmentMode;
            set
            {
                if (_alignmentMode != value)
                {
                    _alignmentMode = value;

                    // Invalidate cached park coordinates when alignment mode changes
                    _parkAxes = new[] { double.NaN, double.NaN };
                    _parkPositions = null;

                    OnPropertyChanged();
                }
            }
        }

        public EquatorialCoordinateType EquatorialCoordinateType
        {
            get => _equatorialCoordinateType;
            set
            {
                if (_equatorialCoordinateType != value)
                {
                    _equatorialCoordinateType = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AtPark
        {
            get => _atPark;
            set
            {
                if (_atPark != value)
                {
                    _atPark = value;
                    OnPropertyChanged();
                }
            }
        }

        public DriveRate TrackingRate
        {
            get => _trackingRate;
            set
            {
                if (_trackingRate != value)
                {
                    _trackingRate = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Reset rate values if not Sidereal
                    if (value != DriveRate.Sidereal)
                    {
                        // Rates are reset by the mount driver
                    }
                }
            }
        }

        public string GpsComPort
        {
            get => _gpsComPort;
            set
            {
                if (_gpsComPort != value)
                {
                    _gpsComPort = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public SerialSpeed GpsBaudRate
        {
            get => _gpsBaudRate;
            set
            {
                if (_gpsBaudRate != value)
                {
                    _gpsBaudRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public SlewSpeed HcSpeed
        {
            get => _hcSpeed;
            set
            {
                if (_hcSpeed != value)
                {
                    _hcSpeed = value;
                    OnPropertyChanged();
                }
            }
        }

        public HcMode HcMode
        {
            get => _hcMode;
            set
            {
                if (_hcMode != value)
                {
                    _hcMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public PecMode PecMode
        {
            get => _pecMode;
            set
            {
                if (_pecMode != value)
                {
                    _pecMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public PolarMode PolarMode
        {
            get => _polarMode;
            set
            {
                if (_polarMode != value)
                {
                    _polarMode = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 2: Location & Custom Gearing (11 properties)

        public double Latitude
        {
            get => _latitude;
            set
            {
                if (Math.Abs(_latitude - value) > 0.0001)
                {
                    _latitude = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Update southern hemisphere flag
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere);
                    }
                }
            }
        }

        public double Longitude
        {
            get => _longitude;
            set
            {
                if (Math.Abs(_longitude - value) > 0.0001)
                {
                    _longitude = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Elevation
        {
            get => _elevation;
            set
            {
                if (Math.Abs(_elevation - value) > 0.01)
                {
                    _elevation = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CustomGearing
        {
            get => _customGearing;
            set
            {
                if (_customGearing != value)
                {
                    _customGearing = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomRa360Steps
        {
            get => _customRa360Steps;
            set
            {
                if (_customRa360Steps != value)
                {
                    _customRa360Steps = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomRaWormTeeth
        {
            get => _customRaWormTeeth;
            set
            {
                if (_customRaWormTeeth != value)
                {
                    _customRaWormTeeth = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomDec360Steps
        {
            get => _customDec360Steps;
            set
            {
                if (_customDec360Steps != value)
                {
                    _customDec360Steps = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomDecWormTeeth
        {
            get => _customDecWormTeeth;
            set
            {
                if (_customDecWormTeeth != value)
                {
                    _customDecWormTeeth = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomRaTrackingOffset
        {
            get => _customRaTrackingOffset;
            set
            {
                if (_customRaTrackingOffset != value)
                {
                    _customRaTrackingOffset = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CustomDecTrackingOffset
        {
            get => _customDecTrackingOffset;
            set
            {
                if (_customDecTrackingOffset != value)
                {
                    _customDecTrackingOffset = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AllowAdvancedCommandSet
        {
            get => _allowAdvancedCommandSet;
            set
            {
                if (_allowAdvancedCommandSet != value)
                {
                    _allowAdvancedCommandSet = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 3: Tracking Rates (8 properties)

        public double SiderealRate
        {
            get => _siderealRate;
            set
            {
                if (Math.Abs(_siderealRate - value) > 0.0001)
                {
                    _siderealRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public double LunarRate
        {
            get => _lunarRate;
            set
            {
                if (Math.Abs(_lunarRate - value) > 0.0001)
                {
                    _lunarRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public double SolarRate
        {
            get => _solarRate;
            set
            {
                if (Math.Abs(_solarRate - value) > 0.0001)
                {
                    _solarRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public double KingRate
        {
            get => _kingRate;
            set
            {
                if (Math.Abs(_kingRate - value) > 0.0001)
                {
                    _kingRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AxisTrackingLimit
        {
            get => _axisTrackingLimit;
            set
            {
                if (Math.Abs(_axisTrackingLimit - value) > 0.01)
                {
                    _axisTrackingLimit = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AxisHzTrackingLimit
        {
            get => _axisHzTrackingLimit;
            set
            {
                if (Math.Abs(_axisHzTrackingLimit - value) > 0.01)
                {
                    _axisHzTrackingLimit = value;
                    OnPropertyChanged();
                }
            }
        }

        public int DisplayInterval
        {
            get => _displayInterval;
            set
            {
                if (_displayInterval != value)
                {
                    _displayInterval = value;
                    OnPropertyChanged();
                }
            }
        }

        public int AltAzTrackingUpdateInterval
        {
            get => _altAzTrackingUpdateInterval;
            set
            {
                if (_altAzTrackingUpdateInterval != value)
                {
                    _altAzTrackingUpdateInterval = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 4: Guiding (8 properties with SIDE EFFECTS)

        public int MinPulseRa
        {
            get => _minPulseRa;
            set
            {
                if (_minPulseRa != value)
                {
                    _minPulseRa = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.MinPulseRa);
                    }
                }
            }
        }

        public int MinPulseDec
        {
            get => _minPulseDec;
            set
            {
                if (_minPulseDec != value)
                {
                    _minPulseDec = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.MinPulseDec);
                    }
                }
            }
        }

        public bool DecPulseToGoTo
        {
            get => _decPulseToGoTo;
            set
            {
                if (_decPulseToGoTo != value)
                {
                    _decPulseToGoTo = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo);
                    }
                }
            }
        }

        public int St4GuideRate
        {
            get => _st4GuideRate;
            set
            {
                if (_st4GuideRate != value)
                {
                    _st4GuideRate = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate);
                    }
                }
            }
        }

        public double GuideRateOffsetX
        {
            get => _guideRateOffsetX;
            set
            {
                if (Math.Abs(_guideRateOffsetX - value) > 0.0001)
                {
                    _guideRateOffsetX = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Update guide rates
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SetGuideRates();
                    }
                }
            }
        }

        public double GuideRateOffsetY
        {
            get => _guideRateOffsetY;
            set
            {
                if (Math.Abs(_guideRateOffsetY - value) > 0.0001)
                {
                    _guideRateOffsetY = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Update guide rates
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SetGuideRates();
                    }
                }
            }
        }

        public int RaBacklash
        {
            get => _raBacklash;
            set
            {
                if (_raBacklash != value)
                {
                    _raBacklash = value;
                    OnPropertyChanged();
                }
            }
        }

        public int DecBacklash
        {
            get => _decBacklash;
            set
            {
                if (_decBacklash != value)
                {
                    _decBacklash = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 5: Optics & Camera (6 properties)

        public double FocalLength
        {
            get => _focalLength;
            set
            {
                if (Math.Abs(_focalLength - value) > 0.01)
                {
                    _focalLength = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CameraWidth
        {
            get => _cameraWidth;
            set
            {
                if (Math.Abs(_cameraWidth - value) > 0.01)
                {
                    _cameraWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CameraHeight
        {
            get => _cameraHeight;
            set
            {
                if (Math.Abs(_cameraHeight - value) > 0.01)
                {
                    _cameraHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public double EyepieceFs
        {
            get => _eyepieceFs;
            set
            {
                if (Math.Abs(_eyepieceFs - value) > 0.01)
                {
                    _eyepieceFs = value;
                    OnPropertyChanged();
                }
            }
        }

        public double ApertureArea => _apertureArea;
        public double ApertureDiameter => _apertureDiameter;

        #endregion

        #region Batch 6: Advanced Settings (7 properties with SIDE EFFECTS)

        public double MaxSlewRate
        {
            get => _maxSlewRate;
            set
            {
                if (Math.Abs(_maxSlewRate - value) > 0.001)
                {
                    _maxSlewRate = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Update slew rates
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SetSlewRates(value);
                    }
                }
            }
        }

        public bool FullCurrent
        {
            get => _fullCurrent;
            set
            {
                if (_fullCurrent != value)
                {
                    _fullCurrent = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.FullCurrent);
                    }
                }
            }
        }

        public bool Encoders
        {
            get => _encoders;
            set
            {
                if (_encoders != value)
                {
                    _encoders = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.Encoders);
                    }
                }
            }
        }

        public bool AlternatingPPec
        {
            get => _alternatingPPec;
            set
            {
                if (_alternatingPPec != value)
                {
                    _alternatingPPec = value;
                    OnPropertyChanged();

                    // SIDE EFFECT: Send to mount
                    if (SkyServer.IsMountRunning)
                    {
                        SkyServer.SkyTasks(MountTaskName.AlternatingPpec);
                    }
                }
            }
        }

        public bool GlobalStopOn
        {
            get => _globalStopOn;
            set
            {
                if (_globalStopOn != value)
                {
                    _globalStopOn = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool Refraction
        {
            get => _refraction;
            set
            {
                if (_refraction != value)
                {
                    _refraction = value;
                    OnPropertyChanged();
                }
            }
        }

        public double GotoPrecision => _gotoPrecision;

        #endregion

        #region Batch 7: Home & Park (9 properties)

        public double HomeAxisX
        {
            get => _homeAxisX;
            set
            {
                if (Math.Abs(_homeAxisX - value) > 0.001)
                {
                    _homeAxisX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double HomeAxisY
        {
            get => _homeAxisY;
            set
            {
                if (Math.Abs(_homeAxisY - value) > 0.001)
                {
                    _homeAxisY = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AutoHomeAxisX
        {
            get => _autoHomeAxisX;
            set
            {
                if (Math.Abs(_autoHomeAxisX - value) > 0.001)
                {
                    _autoHomeAxisX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AutoHomeAxisY
        {
            get => _autoHomeAxisY;
            set
            {
                if (Math.Abs(_autoHomeAxisY - value) > 0.001)
                {
                    _autoHomeAxisY = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ParkName
        {
            get => _parkName;
            set
            {
                if (_parkName != value)
                {
                    _parkName = value ?? "Default";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Park axes position in mount axes values
        /// Polar mode: Stored as Az/Alt (NH convention) in JSON, converted to/from axis coordinates
        /// AltAz and German Polar modes: Stored directly as axis coordinates
        /// </summary>
        public double[] ParkAxes
        {
            get
            {
                // AltAz and German Polar: Return cached axis coordinates directly
                if (_alignmentMode != AlignmentMode.Polar)
                {
                    return _parkAxes;
                }

                // Polar mode: Return cached axis coordinates if available
                if (!double.IsNaN(_parkAxes[0]) && !double.IsNaN(_parkAxes[1]))
                {
                    return _parkAxes;
                }

                // Load from JSON settings service
                var settings = _settingsService.GetSettings();
                var storedAzAlt = settings.ParkAxes;

                if (storedAzAlt == null || storedAzAlt.Length != 2)
                {
                    return new[] { double.NaN, double.NaN };
                }

                double az = storedAzAlt[0];   // Azimuth from storage (NH convention)
                double alt = storedAzAlt[1];  // Altitude from storage

                // Southern Hemisphere: Adjust Az by +180° to convert from NH storage to local coordinates
                if (_latitude < 0)
                {
                    az = Range.Range360(az + 180.0);
                }

                // Convert Az/Alt → Mount axis positions
                var context = AxesContext.FromSettings(this);
                double[] axes = Axes.AzAltToAxesXy(new[] { az, alt }, context);

                // Cache axis coordinates for performance
                _parkAxes = axes;
                return _parkAxes;
            }

            set
            {
                // AltAz and German Polar: Store axis coordinates directly
                if (_alignmentMode != AlignmentMode.Polar)
                {
                    if (Math.Abs(_parkAxes[0] - value[0]) <= 0.0000000000001 &&
                        Math.Abs(_parkAxes[1] - value[1]) <= 0.0000000000001)
                        return;

                    _parkAxes = value;
                    value[0] = Math.Round(value[0], 6);
                    value[1] = Math.Round(value[1], 6);

                    // Directly update settings service (will be saved by QueueSave)
                    var currentSettings = _settingsService.GetSettings();
                    currentSettings.ParkAxes = value;

                    OnPropertyChanged();
                    return;
                }

                // Polar mode: Check if value is different
                if (Math.Abs(_parkAxes[0] - value[0]) <= 0.0000000000001 &&
                    Math.Abs(_parkAxes[1] - value[1]) <= 0.0000000000001)
                    return;

                // Convert axis coordinates to Az/Alt for storage
                var context = AxesContext.FromSettings(this);
                double[] azAlt = Axes.AxesXyToAzAlt(new[] { value[0], value[1] }, context);

                double az = azAlt[0];   // Azimuth (local coordinates)
                double alt = azAlt[1];  // Altitude

                // Southern Hemisphere: Adjust Az by -180° to convert from local to NH storage convention
                if (_latitude < 0)
                {
                    az = Range.Range360(az - 180.0);
                }

                // Round and update settings
                az = Math.Round(az, 6);
                alt = Math.Round(alt, 6);
                var azAltArray = new[] { az, alt };

                var settings = _settingsService.GetSettings();
                settings.ParkAxes = azAltArray;

                // Cache axis coordinates in memory
                _parkAxes = value;

                OnPropertyChanged();
            }
        }


        /// <summary>
        /// Park positions in mount axes values
        /// Polar mode: Stored as Az/Alt (NH convention) in JSON, converted to/from axis coordinates
        /// AltAz and German Polar modes: Stored directly as axis coordinates
        /// </summary>
        public List<ParkPosition> ParkPositions
        {
            get
            {
                // AltAz and German Polar: Return cached axis coordinates directly
                if (_alignmentMode != AlignmentMode.Polar)
                {
                    return _parkPositions;
                }

                // Polar mode: Return cached axis coordinates if available
                if (_parkPositions != null && _parkPositions.Count > 0)
                {
                    return _parkPositions;
                }

                // Load from JSON settings service
                var settings = _settingsService.GetSettings();
                var storedAzAlt = settings.ParkPositions;

                if (storedAzAlt == null || storedAzAlt.Count == 0)
                {
                    return new List<ParkPosition>();
                }

                // Convert each position from Az/Alt to mount axis coordinates
                var axisPositions = new List<ParkPosition>();
                var context = AxesContext.FromSettings(this);

                foreach (var azAltPos in storedAzAlt)
                {
                    double az = azAltPos.X;   // Azimuth from storage (NH convention)
                    double alt = azAltPos.Y;  // Altitude from storage

                    // Southern Hemisphere: Adjust Az by +180° to convert from NH storage to local coordinates
                    if (_latitude < 0)
                    {
                        az = Range.Range360(az + 180.0);
                    }

                    // Convert Az/Alt → Mount axis positions
                    double[] axes = Axes.AzAltToAxesXy(new[] { az, alt }, context);

                    // Create ParkPosition with axis coordinates
                    axisPositions.Add(new ParkPosition
                    {
                        Name = azAltPos.Name,
                        X = Math.Round(axes[0], 6),  // Axis X
                        Y = Math.Round(axes[1], 6)   // Axis Y
                    });
                }

                // Cache axis coordinates for performance
                _parkPositions = axisPositions;
                return _parkPositions;
            }

            set
            {
                // AltAz and German Polar: Store axis coordinates directly
                if (_alignmentMode != AlignmentMode.Polar)
                {
                    _parkPositions = value.OrderBy(p => p.Name).ToList();

                    // Update settings service
                    var currentSettings = _settingsService.GetSettings();
                    currentSettings.ParkPositions = _parkPositions.Select(p =>
                        new Settings.Models.SkySettings.ParkPosition { Name = p.Name, X = p.X, Y = p.Y }).ToList();

                    OnPropertyChanged();
                    return;
                }

                // Polar mode: Convert axis coordinates to Az/Alt for storage
                var azAltPositions = new List<ParkPosition>();
                var context = AxesContext.FromSettings(this);

                foreach (var axisPos in value)
                {
                    // Convert Mount axis positions → Az/Alt
                    double[] azAlt = Axes.AxesXyToAzAlt(new[] { axisPos.X, axisPos.Y }, context);

                    double az = azAlt[0];   // Azimuth (local coordinates)
                    double alt = azAlt[1];  // Altitude

                    // Southern Hemisphere: Adjust Az by -180° to convert from local to NH storage convention
                    if (_latitude < 0)
                    {
                        az = Range.Range360(az - 180.0);
                    }

                    // Create ParkPosition with Az/Alt coordinates for storage (NH convention)
                    azAltPositions.Add(new ParkPosition
                    {
                        Name = axisPos.Name,
                        X = Math.Round(az, 6),   // Azimuth (NH convention)
                        Y = Math.Round(alt, 6)   // Altitude (no adjustment)
                    });
                }

                // Update settings service with Az/Alt (ordered by name)
                var orderedList = azAltPositions.OrderBy(p => p.Name).ToList();
                var settings = _settingsService.GetSettings();
                settings.ParkPositions = orderedList.Select(p =>
                    new Settings.Models.SkySettings.ParkPosition { Name = p.Name, X = p.X, Y = p.Y }).ToList();

                // Cache axis coordinates in memory for performance
                _parkPositions = value.OrderBy(p => p.Name).ToList();

                OnPropertyChanged();
            }
        }

        public bool LimitPark
        {
            get => _limitPark;
            set
            {
                if (_limitPark != value)
                {
                    _limitPark = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ParkLimitName
        {
            get => _parkLimitName;
            set
            {
                if (_parkLimitName != value)
                {
                    _parkLimitName = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 8: Limits (10 properties)

        public double HourAngleLimit
        {
            get => _hourAngleLimit;
            set
            {
                if (Math.Abs(_hourAngleLimit - value) > 0.01)
                {
                    _hourAngleLimit = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AxisLimitX
        {
            get => _axisLimitX;
            set
            {
                if (Math.Abs(_axisLimitX - value) > 0.01)
                {
                    _axisLimitX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AxisUpperLimitY
        {
            get => _axisUpperLimitY;
            set
            {
                if (Math.Abs(_axisUpperLimitY - value) > 0.01)
                {
                    _axisUpperLimitY = value;
                    OnPropertyChanged();
                }
            }
        }

        public double AxisLowerLimitY
        {
            get => _axisLowerLimitY;
            set
            {
                if (Math.Abs(_axisLowerLimitY - value) > 0.01)
                {
                    _axisLowerLimitY = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool LimitTracking
        {
            get => _limitTracking;
            set
            {
                if (_limitTracking != value)
                {
                    _limitTracking = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool SyncLimitOn
        {
            get => _syncLimitOn;
            set
            {
                if (_syncLimitOn != value)
                {
                    _syncLimitOn = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HzLimitTracking
        {
            get => _hzLimitTracking;
            set
            {
                if (_hzLimitTracking != value)
                {
                    _hzLimitTracking = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HzLimitPark
        {
            get => _hzLimitPark;
            set
            {
                if (_hzLimitPark != value)
                {
                    _hzLimitPark = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ParkHzLimitName
        {
            get => _parkHzLimitName;
            set
            {
                if (_parkHzLimitName != value)
                {
                    _parkHzLimitName = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public int SyncLimit => _syncLimit;

        #endregion

        #region Batch 9: PEC (6 properties)

        public bool PecOn
        {
            get => _pecOn;
            set
            {
                if (_pecOn != value)
                {
                    _pecOn = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool PPecOn
        {
            get => _pPecOn;
            set
            {
                if (_pPecOn != value)
                {
                    _pPecOn = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PecOffSet
        {
            get => _pecOffSet;
            set
            {
                if (_pecOffSet != value)
                {
                    _pecOffSet = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PecWormFile
        {
            get => _pecWormFile;
            set
            {
                if (_pecWormFile != value)
                {
                    _pecWormFile = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string Pec360File
        {
            get => _pec360File;
            set
            {
                if (_pec360File != value)
                {
                    _pec360File = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public int PolarLedLevel
        {
            get => _polarLedLevel;
            set
            {
                if (_polarLedLevel != value)
                {
                    _polarLedLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 10: Hand Controller (6 properties)

        public bool HcAntiRa
        {
            get => _hcAntiRa;
            set
            {
                if (_hcAntiRa != value)
                {
                    _hcAntiRa = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HcAntiDec
        {
            get => _hcAntiDec;
            set
            {
                if (_hcAntiDec != value)
                {
                    _hcAntiDec = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HcFlipEw
        {
            get => _hcFlipEw;
            set
            {
                if (_hcFlipEw != value)
                {
                    _hcFlipEw = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HcFlipNs
        {
            get => _hcFlipNs;
            set
            {
                if (_hcFlipNs != value)
                {
                    _hcFlipNs = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<HcPulseGuide> HcPulseGuides
        {
            get => _hcPulseGuides;
            set
            {
                _hcPulseGuides = value ?? new List<HcPulseGuide>();
                OnPropertyChanged();
            }
        }

        public bool DisableKeysOnGoTo
        {
            get => _disableKeysOnGoTo;
            set
            {
                if (_disableKeysOnGoTo != value)
                {
                    _disableKeysOnGoTo = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Batch 11: Miscellaneous (6 properties)

        public double Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) > 0.01)
                {
                    _temperature = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InstrumentDescription => _instrumentDescription;
        public string InstrumentName => _instrumentName;
        public bool AutoTrack => _autoTrack;
        public int RaTrackingOffset => _raTrackingOffset;

        #endregion

        #region Batch 12: Capabilities (28 properties)

        public bool CanAlignMode => _canAlignMode;
        public bool CanAltAz => _canAltAz;
        public bool CanEquatorial => _canEquatorial;
        public bool CanFindHome => _canFindHome;
        public bool CanLatLongElev => _canLatLongElev;
        public bool CanOptics => _canOptics;
        public bool CanPark => _canPark;
        public bool CanPulseGuide => _canPulseGuide;
        public bool CanSetEquRates => _canSetEquRates;
        public bool CanSetDeclinationRate => _canSetDeclinationRate;
        public bool CanSetGuideRates => _canSetGuideRates;

        public bool CanSetPark
        {
            get => _canSetPark;
            set
            {
                if (_canSetPark != value)
                {
                    _canSetPark = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanSetPierSide
        {
            get => _canSetPierSide;
            set
            {
                if (_canSetPierSide != value)
                {
                    _canSetPierSide = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanSetRightAscensionRate => _canSetRightAscensionRate;
        public bool CanSetTracking => _canSetTracking;
        public bool CanSiderealTime => _canSiderealTime;
        public bool CanSlew => _canSlew;
        public bool CanSlewAltAz => _canSlewAltAz;
        public bool CanSlewAltAzAsync => _canSlewAltAzAsync;
        public bool CanSlewAsync => _canSlewAsync;
        public bool CanSync => _canSync;
        public bool CanSyncAltAz => _canSyncAltAz;
        public bool CanTrackingRates => _canTrackingRates;
        public bool CanUnPark => _canUnPark;
        public bool NoSyncPastMeridian => _noSyncPastMeridian;
        public int NumMoveAxis => _numMoveAxis;
        public bool VersionOne => _versionOne;

        #endregion

        #region JSON Persistence Methods

        /// <summary>
        /// Load settings from active profile or fall back to JSON
        /// </summary>
        /// <returns>Settings model from profile or JSON</returns>
        private Settings.Models.SkySettings LoadSettingsFromSource()
        {
            // Try to load from active profile first
            if (_profileLoaderService != null)
            {
                try
                {
                    var profileSettings = _profileLoaderService.LoadActiveProfileAsync()
                        .GetAwaiter()
                        .GetResult();

                    LogSettings("LoadedFromProfile", $"Active profile loaded successfully");
                    return profileSettings;
                }
                catch (Exception ex)
                {
                    // Log failure and fall back to JSON
                    LogSettings("ProfileLoadFailed", $"Falling back to JSON: {ex.Message}");
                }
            }

            // Fall back to JSON (backward compatibility or no profile service)
            return _settingsService.GetSettings();
        }

        /// <summary>
        /// Apply settings from SkySettings model to instance fields
        /// This is the single source of truth for all settings mapping
        /// </summary>
        /// <param name="settings">Settings model (from profile or JSON)</param>
        private void ApplySettings(Settings.Models.SkySettings settings)
        {
            try
            {
                // Batch 1: Connection & Mount
                if (Enum.TryParse<MountType>(settings.Mount, true, out var mountType))
                    _mount = mountType;
                _port = settings.Port ?? "COM3";
                _baudRate = (SerialSpeed)settings.BaudRate;
                if (Enum.TryParse<AlignmentMode>(settings.AlignmentMode, true, out var alignMode))
                    _alignmentMode = alignMode;
                if (Enum.TryParse<EquatorialCoordinateType>(settings.EquatorialCoordinateType, true, out var eqType))
                    _equatorialCoordinateType = eqType;
                _atPark = settings.AtPark;
                if (Enum.TryParse<DriveRate>(settings.TrackingRate, true, out var trackRate))
                    _trackingRate = trackRate;
                // AWW ToDo check for correct type
                _gpsComPort = settings.GpsPort.ToString() ?? string.Empty;
                if (Enum.TryParse<SerialSpeed>(settings.GpsBaudRate, true, out var gpsBaud))
                    _gpsBaudRate = gpsBaud;
                else
                    _gpsBaudRate = SerialSpeed.ps9600;

                if (Enum.TryParse<SlewSpeed>(settings.HcSpeed, true, out var hcSpd))
                    _hcSpeed = hcSpd;
                if (Enum.TryParse<HcMode>(settings.HcMode, true, out var hcMd))
                    _hcMode = hcMd;
                if (Enum.TryParse<PecMode>(settings.PecMode, true, out var pecMd))
                    _pecMode = pecMd;
                if (Enum.TryParse<PolarMode>(settings.PolarMode, true, out var polMd))
                    _polarMode = polMd;

                // Batch 2: Location & Custom Gearing
                _latitude = settings.Latitude;
                _longitude = settings.Longitude;
                _elevation = settings.Elevation;
                _customGearing = settings.CustomGearing;
                _customRa360Steps = settings.CustomRa360Steps;
                _customRaWormTeeth = settings.CustomRaWormTeeth;
                _customDec360Steps = settings.CustomDec360Steps;
                _customDecWormTeeth = settings.CustomDecWormTeeth;
                _customRaTrackingOffset = settings.CustomRaTrackingOffset;
                _customDecTrackingOffset = settings.CustomDecTrackingOffset;
                _allowAdvancedCommandSet = settings.AllowAdvancedCommandSet;

                // Batch 3: Tracking Rates
                _siderealRate = settings.SiderealRate;
                _lunarRate = settings.LunarRate;
                _solarRate = settings.SolarRate;
                _kingRate = settings.KingRate;
                _axisTrackingLimit = settings.AxisTrackingLimit;
                _axisHzTrackingLimit = settings.AxisHzTrackingLimit;
                _displayInterval = settings.DisplayInterval;
                _altAzTrackingUpdateInterval = settings.AltAzTrackingUpdateInterval;

                // Batch 4: Guiding
                _minPulseRa = settings.MinPulseRa;
                _minPulseDec = settings.MinPulseDec;
                _decPulseToGoTo = settings.DecPulseToGoTo;
                _st4GuideRate = settings.St4Guiderate;
                _guideRateOffsetX = settings.GuideRateOffsetX;
                _guideRateOffsetY = settings.GuideRateOffsetY;
                _raBacklash = settings.RaBacklash;
                _decBacklash = settings.DecBacklash;

                // Batch 5: Optics
                _focalLength = settings.FocalLength;
                _cameraWidth = settings.CameraWidth;
                _cameraHeight = settings.CameraHeight;
                _eyepieceFs = settings.EyepieceFS;
                _apertureArea = settings.ApertureArea;
                _apertureDiameter = settings.ApertureDiameter;

                // Batch 6: Advanced
                _maxSlewRate = settings.MaximumSlewRate;
                _fullCurrent = settings.FullCurrent;
                _encoders = settings.EncodersOn;
                _alternatingPPec = settings.AlternatingPPEC;
                _globalStopOn = settings.GlobalStopOn;
                _refraction = settings.Refraction;
                _gotoPrecision = settings.GotoPrecision;

                // Batch 7: Home & Park
                _homeAxisX = settings.HomeAxisX;
                _homeAxisY = settings.HomeAxisY;
                _autoHomeAxisX = settings.AutoHomeAxisX;
                _autoHomeAxisY = settings.AutoHomeAxisY;
                _parkName = settings.ParkName ?? "Default";

                // Load ParkAxes - raw assignment for AltAz/GermanPolar, NaN for Polar (forces transformation)
                if (_alignmentMode == AlignmentMode.Polar)
                {
                    _parkAxes = new[] { double.NaN, double.NaN }; // Force lazy load + transformation
                }
                else
                {
                    _parkAxes = settings.ParkAxes ?? new[] { double.NaN, double.NaN };
                }

                // Load ParkPositions - raw assignment (transformation handled by property getter if needed)
                if (_alignmentMode == AlignmentMode.Polar)
                {
                    _parkPositions = new List<ParkPosition>(); // Force lazy load + transformation
                }
                else
                {
                    _parkPositions = settings.ParkPositions?.Select(p =>
                        new ParkPosition { Name = p.Name, X = p.X, Y = p.Y }).ToList() ?? new List<ParkPosition>();
                }

                _limitPark = settings.LimitPark;
                _parkLimitName = settings.ParkLimitName ?? string.Empty;

                // Batch 8: Limits
                _hourAngleLimit = settings.HourAngleLimit;
                _axisLimitX = settings.AxisLimitX;
                _axisUpperLimitY = settings.AxisUpperLimitY;
                _axisLowerLimitY = settings.AxisLowerLimitY;
                _limitTracking = settings.LimitTracking;
                _syncLimitOn = settings.SyncLimitOn;
                _hzLimitTracking = settings.HzLimitTracking;
                _hzLimitPark = settings.HzLimitPark;
                _parkHzLimitName = settings.ParkHzLimitName ?? string.Empty;
                _syncLimit = settings.SyncLimit;

                // Batch 9: PEC
                _pecOn = settings.PecOn;
                _pPecOn = settings.PpecOn;
                _pecOffSet = settings.PecOffSet;
                _pecWormFile = settings.PecWormFile ?? string.Empty;
                _pec360File = settings.Pec360File ?? string.Empty;
                _polarLedLevel = settings.PolarLedLevel;

                // Batch 10: Hand Controller
                _hcAntiRa = settings.HcAntiRa;
                _hcAntiDec = settings.HcAntiDec;
                _hcFlipEw = settings.HcFlipEW;
                _hcFlipNs = settings.HcFlipNS;
                // HcPulseGuides list loaded separately if needed
                _disableKeysOnGoTo = settings.DisableKeysOnGoTo;

                // Batch 11: Miscellaneous
                _temperature = settings.Temperature;
                _instrumentDescription = settings.InstrumentDescription ?? "GreenSwamp Alpaca Server";
                _instrumentName = settings.InstrumentName ?? "GreenSwamp Mount";
                _autoTrack = settings.AutoTrack;
                _raTrackingOffset = settings.RATrackingOffset;

                // Batch 12: Capabilities (read-only)
                _canAlignMode = settings.CanAlignMode;
                _canAltAz = settings.CanAltAz;
                _canEquatorial = settings.CanEquatorial;
                _canFindHome = settings.CanFindHome;
                _canLatLongElev = settings.CanLatLongElev;
                _canOptics = settings.CanOptics;
                _canPark = settings.CanPark;
                _canPulseGuide = settings.CanPulseGuide;
                _canSetEquRates = settings.CanSetEquRates;
                _canSetDeclinationRate = settings.CanSetDeclinationRate;
                _canSetGuideRates = settings.CanSetGuideRates;
                _canSetPark = settings.CanSetPark;
                _canSetPierSide = settings.CanSetPierSide;
                _canSetRightAscensionRate = settings.CanSetRightAscensionRate;
                _canSetTracking = settings.CanSetTracking;
                _canSiderealTime = settings.CanSiderealTime;
                _canSlew = settings.CanSlew;
                _canSlewAltAz = settings.CanSlewAltAz;
                _canSlewAltAzAsync = settings.CanSlewAltAzAsync;
                _canSlewAsync = settings.CanSlewAsync;
                _canSync = settings.CanSync;
                _canSyncAltAz = settings.CanSyncAltAz;
                _canTrackingRates = settings.CanTrackingRates;
                _canUnPark = settings.CanUnpark;
                _noSyncPastMeridian = settings.NoSyncPastMeridian;
                _numMoveAxis = settings.NumMoveAxis;
                _versionOne = settings.VersionOne;

                LogSettings("AppliedSettings", $"Mount:{_mount}|Port:{_port}");
            }
            catch (Exception ex)
            {
                LogSettings("ApplySettingsFailed", ex.Message);
            }
        }

        /// <summary>
        /// Queue auto-save (debounced 2 seconds)
        /// </summary>
        private void QueueSave()
        {
            _saveCts?.Cancel();
            _saveCts = new CancellationTokenSource();

            Task.Delay(2000, _saveCts.Token).ContinueWith(_ =>
            {
                if (!_.IsCanceled)
                    SaveAsync().GetAwaiter().GetResult();
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Save all settings to JSON
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var settings = _settingsService.GetSettings();

                // Map instance fields → JSON model (93 writable properties)
                settings.Mount = _mount.ToString();
                settings.Port = _port;
                settings.BaudRate = (int)_baudRate;
                settings.AlignmentMode = _alignmentMode.ToString();
                settings.EquatorialCoordinateType = _equatorialCoordinateType.ToString();
                settings.AtPark = _atPark;
                settings.TrackingRate = _trackingRate.ToString();
                // AWW ToDo check for correct type
                // settings.GpsPort = _gpsComPort;
                settings.GpsBaudRate = ((int)_gpsBaudRate).ToString();
                settings.HcSpeed = _hcSpeed.ToString();
                settings.HcMode = _hcMode.ToString();
                settings.PecMode = _pecMode.ToString();
                settings.PolarMode = _polarMode.ToString();

                settings.Latitude = _latitude;
                settings.Longitude = _longitude;
                settings.Elevation = _elevation;
                settings.CustomGearing = _customGearing;
                settings.CustomRa360Steps = _customRa360Steps;
                settings.CustomRaWormTeeth = _customRaWormTeeth;
                settings.CustomDec360Steps = _customDec360Steps;
                settings.CustomDecWormTeeth = _customDecWormTeeth;
                settings.CustomRaTrackingOffset = _customRaTrackingOffset;
                settings.CustomDecTrackingOffset = _customDecTrackingOffset;
                settings.AllowAdvancedCommandSet = _allowAdvancedCommandSet;

                settings.SiderealRate = _siderealRate;
                settings.LunarRate = _lunarRate;
                settings.SolarRate = _solarRate;
                settings.KingRate = _kingRate;
                settings.AxisTrackingLimit = _axisTrackingLimit;
                settings.AxisHzTrackingLimit = _axisHzTrackingLimit;
                settings.DisplayInterval = _displayInterval;
                settings.AltAzTrackingUpdateInterval = _altAzTrackingUpdateInterval;

                settings.MinPulseRa = _minPulseRa;
                settings.MinPulseDec = _minPulseDec;
                settings.DecPulseToGoTo = _decPulseToGoTo;
                settings.St4Guiderate = _st4GuideRate;
                settings.GuideRateOffsetX = _guideRateOffsetX;
                settings.GuideRateOffsetY = _guideRateOffsetY;
                settings.RaBacklash = _raBacklash;
                settings.DecBacklash = _decBacklash;

                settings.FocalLength = _focalLength;
                settings.CameraWidth = _cameraWidth;
                settings.CameraHeight = _cameraHeight;
                settings.EyepieceFS = _eyepieceFs;

                settings.MaximumSlewRate = _maxSlewRate;
                settings.FullCurrent = _fullCurrent;
                settings.EncodersOn = _encoders;
                settings.AlternatingPPEC = _alternatingPPec;
                settings.GlobalStopOn = _globalStopOn;
                settings.Refraction = _refraction;

                settings.HomeAxisX = _homeAxisX;
                settings.HomeAxisY = _homeAxisY;
                settings.AutoHomeAxisX = _autoHomeAxisX;
                settings.AutoHomeAxisY = _autoHomeAxisY;
                settings.ParkName = _parkName;

                settings.LimitPark = _limitPark;
                settings.ParkLimitName = _parkLimitName;

                settings.HourAngleLimit = _hourAngleLimit; settings.AxisLimitX = _axisLimitX;
                settings.AxisUpperLimitY = _axisUpperLimitY;
                settings.AxisLowerLimitY = _axisLowerLimitY;
                settings.LimitTracking = _limitTracking;
                settings.SyncLimitOn = _syncLimitOn;
                settings.HzLimitTracking = _hzLimitTracking;
                settings.HzLimitPark = _hzLimitPark;
                settings.ParkHzLimitName = _parkHzLimitName;

                settings.PecOn = _pecOn;
                settings.PpecOn = _pPecOn;
                settings.PecOffSet = _pecOffSet;
                settings.PecWormFile = _pecWormFile;
                settings.Pec360File = _pec360File;
                settings.PolarLedLevel = _polarLedLevel;

                settings.HcAntiRa = _hcAntiRa;
                settings.HcAntiDec = _hcAntiDec;
                settings.HcFlipEW = _hcFlipEw;
                settings.HcFlipNS = _hcFlipNs;
                settings.DisableKeysOnGoTo = _disableKeysOnGoTo;

                settings.Temperature = _temperature;

                await _settingsService.SaveSettingsAsync(settings);
                LogSettings("SavedToJson", "Success");
            }
            catch (Exception ex)
            {
                LogSettings("SaveToJsonFailed", ex.Message);
            }
        }

        #endregion

        #region Helper Methods

        private void LogSettings(string method, string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Information,
                    Method = $"SkySettingsInstance.{method}",
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = message
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch
            {
                // Fail silently if logging fails
            }
        }

        #endregion
    }
}