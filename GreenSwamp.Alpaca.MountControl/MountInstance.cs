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
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.MountControl.Interfaces;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using Range = GreenSwamp.Alpaca.Principles.Range;
using System.Diagnostics;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Instance-based mount controller that initially delegates to static SkyServer.
    /// This class will gradually take over implementation from static methods in Phase 3.2+.
    /// </summary>
    public partial class MountInstance : IMountController
    {
        #region Private backing fields

        private readonly string _instanceName;
        private readonly string _id;
        private readonly string _deviceName;
        private readonly SkySettingsInstance _settings;

        // Instance state fields (migrated from static)
        private MediaTimer? _mediaTimer;
        internal MediaTimer? _altAzTrackingTimer;
        // Phase 4.1: Converted to delegating properties (fields removed, see properties below)
        private Vector _homeAxes;
        internal Vector _appAxes;
        private Vector _targetRaDec;
        internal Exception? _mountError;
        private Vector _altAzSync;

        // Factor steps (conversion ratios) - instance-owned
        internal double[] _factorStep = new double[2];
        internal long[] _stepsPerRevolution = new long[2];
        internal double[] _stepsWormPerRevolution = new double[2];

        // PEC fields
        internal int[] _wormTeethCount = new int[2];
        private double _pecBinSteps;


        // Mount capabilities (instance-owned)
        // Phase 6: widened from private to internal — delegated by static SkyServer properties
        internal bool _canPPec;
        internal bool _canHomeSensor;
        internal bool _canPolarLed;
        internal bool _canAdvancedCmdSupport;
        internal string _mountName = string.Empty;
        internal string _mountVersion = string.Empty;
        internal string _capabilities = string.Empty;

        // Mount state
        private bool _atPark;
        internal double _actualAxisX;
        internal double _actualAxisY;
        internal bool _isHome;
        internal bool _lowVoltageEventState;
        internal bool _monitorPulse;
        internal double _slewSettleTime;

        // Phase 6: new per-instance backing fields (migrated from static SkyServer)
        internal bool _isPulseGuidingRa;
        internal bool _isPulseGuidingDec;
        internal Vector _rateMoveAxes;
        internal bool _moveAxisActive;
        internal bool _isSlewing;
        internal bool _flipOnNextGoto;
        internal SlewType _slewState;
        internal Exception? _lastAutoHomeError;
        internal int _autoHomeProgressBar;
        internal bool _autoHomeStop;
        internal bool _isAutoHomeRunning;
        internal bool _snapPort1Result;
        internal bool _snapPort2Result;

        // Step 2: Per-instance diagnostics and tracking mode (migrated from static SkyServer)
        internal ulong _loopCounter;
        internal int _timerOverruns;
        internal AltAzTrackingType _altAzTrackingMode;
        internal ParkPosition? _parkSelected;
        // Phase 6: AltAz tracking lock (Int32 for Interlocked; direct field access required for ref semantics)
        internal Int32 _altAzTrackingLock;
        // Step 7: Raw step counts from hardware — backing field for SkyServer.Steps
        internal double[] _steps = { 0.0, 0.0 };

        // UpdateSteps fields
        private DateTime _lastUpdateStepsTime = DateTime.MinValue;
        private readonly object _lastUpdateLock = new object();

        // Phase 0 Q2: Queue instances owned by this MountInstance
        internal CommandQueueBase<SkyWatcher> SkyQueueInstance { get; private set; }
        internal CommandQueueBase<Actions> MountQueueInstance { get; private set; }

        // Slew speed fields (internal so SkyServer.SetSlewRates can access them)
        internal double _slewSpeedOne;
        internal double _slewSpeedTwo;
        internal double _slewSpeedThree;
        internal double _slewSpeedFour;
        internal double _slewSpeedFive;
        internal double _slewSpeedSix;
        internal double _slewSpeedSeven;
        internal double _slewSpeedEight;

        // Phase 4.3: Tracking state fields
        private bool _tracking;
        private TrackingMode _trackingMode = TrackingMode.Off;

        // Phase 5.3: CancellationTokenSources (per-instance — prevents cross-device cancellation)
        internal volatile CancellationTokenSource? _ctsGoTo;
        internal volatile CancellationTokenSource? _ctsPulseGuideRa;
        internal volatile CancellationTokenSource? _ctsPulseGuideDec;
        internal volatile CancellationTokenSource? _ctsHcPulseGuide;

        // Phase 5.3: SlewController — per-instance to isolate slew state across devices
        internal SlewController? _slewController;

        // Phase 5.4: Per-instance timer lock (isolates update loop re-entrancy per device)
        private readonly object _timerLock = new object();

        // SkyWatcher tracking rates (internal use only)
        internal Vector _skyHcRate = new Vector(0, 0);
        internal Vector _skyTrackingRate = new Vector(0, 0);

        // Guide rate field
        private Vector _guideRate;

        // Phase 4.4: Rate fields (target and guide rate already exist above)
        private Vector _rateRaDec = new Vector(0, 0);

        // Original rate storage (for direction tracking)
        private double _rateRaOrg;
        private double _rateDecOrg;

        // Phase 4.5: Position and coordinate fields

        private Vector _raDec = new Vector(0, 0);
        private double _rightAscensionXForm;
        private double _declinationXForm;
        private Vector _altAzm = new Vector(0, 0);
        private double _siderealTime;
        private double _lha;
        private PointingState _isSideOfPier = PointingState.Unknown;

        #endregion

        #region Public State Exposure (Phase 4.1)

        /// <summary>
        /// Gets whether the mount is currently running
        /// Delegates to SkyServer.IsMountRunning which checks queue status
        /// </summary>
        public bool IsMountRunning => SkyServer.IsMountRunning;

        /// <summary>
        /// Gets the user-provided device name (Phase 4.8.1)
        /// </summary>
        public string DeviceName => _deviceName;

        /// <summary>
        /// Gets the settings instance for this mount (Phase 4.8.1)
        /// </summary>
        public SkySettingsInstance Settings => _settings;

        /// <summary>
        /// Gets or sets the target RA/Dec position
        /// </summary>
        public Vector TargetRaDec
        {
            get => _targetRaDec;
            set => _targetRaDec = value;
        }


        // Phase 4.3: Tracking state properties
        public bool Tracking => _tracking;

        public TrackingMode TrackingMode
        {
            get => _trackingMode;
            set => _trackingMode = value;
        }

        // SkyWatcher-specific tracking rates (internal access only)
        internal Vector SkyTrackingRate
        {
            get => _skyTrackingRate;
            set => _skyTrackingRate = value;
        }

        internal Vector SkyHcRate
        {
            get => _skyHcRate;
            set => _skyHcRate = value;
        }

        // Phase 4.4: Target and rate properties
        public double TargetRa
        {
            get => _targetRaDec.X;
            set => _targetRaDec.X = value;
        }

        public double TargetDec
        {
            get => _targetRaDec.Y;
            set => _targetRaDec.Y = value;
        }

        public double RateRa
        {
            get => _rateRaDec.X;
            set => _rateRaDec.X = value;
        }

        public double RateDec
        {
            get => _rateRaDec.Y;
            set => _rateRaDec.Y = value;
        }

        public double RateRaOrg
        {
            get => _rateRaOrg;
            set => _rateRaOrg = value;
        }

        public double RateDecOrg
        {
            get => _rateDecOrg;
            set => _rateDecOrg = value;
        }

        public double GuideRateRa
        {
            get => _guideRate.X;
            set => _guideRate.X = value;
        }

        public double GuideRateDec
        {
            get => _guideRate.Y;
            set => _guideRate.Y = value;
        }

        // Phase 4.5: Position and coordinate properties
        public double RightAscension
        {
            get => _raDec.X;
            set => _raDec.X = value;
        }

        public double Declination
        {
            get => _raDec.Y;
            set => _raDec.Y = value;
        }

        public double RightAscensionXForm
        {
            get => _rightAscensionXForm;
            set => _rightAscensionXForm = value;
        }

        public double DeclinationXForm
        {
            get => _declinationXForm;
            set => _declinationXForm = value;
        }

        public double Altitude
        {
            get => _altAzm.Y;
            set => _altAzm.Y = value;
        }

        public double Azimuth
        {
            get => _altAzm.X;
            set => _altAzm.X = value;
        }

        public double SiderealTime
        {
            get => _siderealTime;
            set => _siderealTime = value;
        }

        public double Lha
        {
            get => _lha;
            set => _lha = value;
        }

        public PointingState IsSideOfPier
        {
            get => _isSideOfPier;
            set => _isSideOfPier = value;
        }

        // Phase 8: Computed pier-side (same logic as static SkyServer.SideOfPier)
        public PointingState SideOfPier
        {
            get
            {
                switch (_settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        return _actualAxisX >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
                    case AlignmentMode.Polar:
                        return (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001)
                            ? PointingState.Normal : PointingState.ThroughThePole;
                    case AlignmentMode.GermanPolar:
                        bool southernHemisphere = SkyServer.SouthernHemisphere;
                        if (southernHemisphere)
                            return (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001)
                                ? PointingState.ThroughThePole : PointingState.Normal;
                        else
                            return (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001)
                                ? PointingState.Normal : PointingState.ThroughThePole;
                    default:
                        return PointingState.Unknown;
                }
            }
        }

        // Phase 8: AtHome — computed from current appAxes vs homeAxes
        public bool AtHome
        {
            get
            {
                var context = AxesContext.FromSettings(_settings);
                var home = Axes.AxesMountToApp(new[] { _homeAxes.X, _homeAxes.Y }, context);
                double dX = Math.Abs(_appAxes.X - home[0]);
                dX = Math.Min(dX, 360.0 - dX);
                double dY = Math.Abs(_appAxes.Y - home[1]);
                return (dX * dX + dY * dY) < 0.01414;
            }
        }

        // Phase 8: AtPark — delegates to settings (same source as SkyServer.AtPark)
        public bool AtPark
        {
            get => _settings.AtPark;
            set => _settings.AtPark = value;
        }

        // Phase 8: IsSlewing — mirrors SkyServer.IsSlewing logic using per-instance fields
        public bool IsSlewing =>
            (_slewController?.IsSlewing == true) ||
            _moveAxisActive ||
            _isSlewing;

        // Phase 8: IsPulseGuiding — combined pulse guide state
        public bool IsPulseGuiding => _isPulseGuidingRa || _isPulseGuidingDec;

        // Phase 8: IsPulseGuidingRa / IsPulseGuidingDec — public access for Telescope.cs
        public bool IsPulseGuidingRa
        {
            get => _isPulseGuidingRa;
            set => _isPulseGuidingRa = value;
        }

        public bool IsPulseGuidingDec
        {
            get => _isPulseGuidingDec;
            set => _isPulseGuidingDec = value;
        }

        // Phase 8: SlewState — public access to per-instance slew state
        public SlewType SlewState
        {
            get => _slewState;
            set => _slewState = value;
        }

        // Phase 8: SlewSettleTime — public access to per-instance settle time
        public double SlewSettleTime
        {
            get => _slewSettleTime;
            set => _slewSettleTime = value;
        }

        // Step 2: Per-instance diagnostics and tracking mode
        public ulong LoopCounter { get => _loopCounter; internal set => _loopCounter = value; }
        public int TimerOverruns { get => _timerOverruns; internal set => _timerOverruns = value; }
        public AltAzTrackingType AltAzTrackingMode { get => _altAzTrackingMode; set => _altAzTrackingMode = value; }

        #endregion

        #region Internal State Exposure (for other MountControl classes)

        /// <summary>
        /// Sets the tracking state (internal method for SkyServer)
        /// Phase 4.3: Called by SkyServer.Tracking property setter
        /// </summary>
        /// <param name="tracking">New tracking state</param>
        internal void SetTracking(bool tracking)
        {
            _tracking = tracking;
        }
        
        /// <summary>
        /// Alt/Az sync position for Alt/Az mode syncing
        /// /// </summary>
        internal Vector AltAzSync
        {
            get => _altAzSync;
            set => _altAzSync = value;
        }
        // Phase 4.2: Expose slew speeds (read-only for
        // now)
        public double SlewSpeedOne => _slewSpeedOne;
        public double SlewSpeedTwo => _slewSpeedTwo;
        public double SlewSpeedThree => _slewSpeedThree;
        public double SlewSpeedFour => _slewSpeedFour;
        public double SlewSpeedFive => _slewSpeedFive;
        public double SlewSpeedSix => _slewSpeedSix;
        public double SlewSpeedSeven => _slewSpeedSeven;
        public double SlewSpeedEight => _slewSpeedEight;
        #endregion
        /// <summary>
        /// Phase 4.2: Constructor with optional settings file path
        /// Phase 4.8.1: Added deviceName parameter for user-visible device identification
        /// </summary>
        /// <param name="id">Unique instance identifier (e.g., "telescope-0")</param>
        /// <param name="settings">Settings instance (can be file-based or static)</param>
        /// <param name="deviceName">User-provided device name (defaults to id if null)</param>
        public MountInstance(string id, SkySettingsInstance settings, string? deviceName = null)
        {
            _id = id ?? "mount-0";
            _instanceName = id ?? "default";
            _deviceName = deviceName ?? id ?? "Unnamed Device";
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Phase4.2|MountInstance created|ID:{_id}|Mount:{_settings.Mount}|Port:{_settings.Port}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #region IMountController Implementation (Delegation)

        /// <summary>
        /// Gets the unique identifier for this mount instance
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Gets whether the mount is currently connected
        /// Delegates to static SkyServer
        /// </summary>
        public bool IsConnected => SkyServer.IsMountRunning;

        /// <summary>
        /// Gets whether the mount is currently running
        /// Delegates to static SkyServer
        /// </summary>
        public bool IsRunning => SkyServer.IsMountRunning;

        /// <summary>
        /// Connect to mount hardware
        /// Implemented (was stub in 3.1)
        /// </summary>
        public bool Connect()
        {
            LogMount($"Connect() called on instance {_id}");

            // Call the actual connect implementation
            // This will be MountConnect() migrated from static
            return MountConnect();
        }

        /// <summary>
        /// Sets up defaults after an established connection
        /// Migrated from SkyServer.MountConnect()
        /// </summary>
        private bool MountConnect()
        {
            _targetRaDec = new Vector(double.NaN, double.NaN);
            var positions = SkyServer.GetDefaultPositions_Internal();
            double[]? rawPositions = null;
            var counter = 0;
            int raWormTeeth;
            int decWormTeeth;
            bool positionsSet = false;
            MonitorEntry monitorItem;
            string msg;

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    // defaults
                    SkyServer.SimTasks(MountTaskName.MountName);
                    SkyServer.SimTasks(MountTaskName.MountVersion);
                    SkyServer.SimTasks(MountTaskName.StepsPerRevolution);
                    SkyServer.SimTasks(MountTaskName.StepsWormPerRevolution);
                    SkyServer.SimTasks(MountTaskName.CanHomeSensor);
                    SkyServer.SimTasks(MountTaskName.GetFactorStep);
                    SkyServer.SimTasks(MountTaskName.Capabilities);


                    // Copy capabilities from static
                    _canPPec = SkyServer.CanPPec;
                    _canHomeSensor = SkyServer.CanHomeSensor;
                    _canPolarLed = SkyServer.CanPolarLed;
                    _canAdvancedCmdSupport = SkyServer.CanAdvancedCmdSupport;
                    _mountName = SkyServer.MountName ?? string.Empty;
                    _mountVersion = SkyServer.MountVersion ?? string.Empty;
                    _capabilities = SkyServer.Capabilities ?? string.Empty;

                    // Log instance values for verification
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Phase4.1|Instance:{_id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{_canPPec}|MountName:{_mountName}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / PecBinCount;

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetRawDegrees:{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
                        MonitorLog.LogToMonitor(monitorItem);

                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (_atPark)
                        {
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                        _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }
                    // Update AlignmentModel settings.
                    SkyServer.ConnectAlignmentModel();

                    break;
                case MountType.SkyWatcher:
                    SkyHcRate = new Vector(0, 0);
                    SkyTrackingRate = new Vector(0, 0);

                    // create a command and put in queue to test connection
                    var init = new SkyGetMotorCardVersion(SkyQueue.NewId, Axis.Axis1);
                    _ = (string)SkyQueue.GetCommandResult(init).Result;
                    if (!init.Successful && init.Exception != null)
                    {
                        // ToDo: fix string resource
                        init.Exception = new Exception($"CheckMount{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        // init.Exception = new Exception($"{MediaTypeNames.Application.Current.Resources["CheckMount"]}{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        SkyServer.SkyErrorHandler(init.Exception);
                        return false;
                    }

                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Voltage|{SkyServer.ControllerVoltage.ToString("F2") + " V"}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    // defaults
                    if (_settings.Mount == MountType.SkyWatcher)
                    {
                        SkyServer.SkyTasks(MountTaskName.AllowAdvancedCommandSet);
                    }
                    SkyServer.SkyTasks(MountTaskName.LoadDefaults);
                    SkyServer.SkyTasks(MountTaskName.StepsPerRevolution);
                    SkyServer.SkyTasks(MountTaskName.StepsWormPerRevolution);
                    SkyServer.SkyTasks(MountTaskName.StopAxes);
                    SkyServer.SkyTasks(MountTaskName.Encoders);
                    SkyServer.SkyTasks(MountTaskName.FullCurrent);
                    SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate);
                    SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere);
                    SkyServer.SkyTasks(MountTaskName.MountName);
                    SkyServer.SkyTasks(MountTaskName.MountVersion);
                    SkyServer.SkyTasks(MountTaskName.StepTimeFreq);
                    SkyServer.SkyTasks(MountTaskName.CanPpec);
                    SkyServer.SkyTasks(MountTaskName.CanPolarLed);
                    SkyServer.SkyTasks(MountTaskName.PolarLedLevel);
                    SkyServer.SkyTasks(MountTaskName.CanHomeSensor);
                    SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo);
                    SkyServer.SkyTasks(MountTaskName.AlternatingPpec);
                    SkyServer.SkyTasks(MountTaskName.MinPulseDec);
                    SkyServer.SkyTasks(MountTaskName.MinPulseRa);
                    SkyServer.SkyTasks(MountTaskName.GetFactorStep);
                    SkyServer.SkyTasks(MountTaskName.Capabilities);
                    SkyServer.SkyTasks(MountTaskName.CanAdvancedCmdSupport);
                    if (SkyServer.CanPPec) SkyServer.SkyTasks(MountTaskName.Pec);


                    // Copy capabilities from static
                    _canPPec = SkyServer.CanPPec;
                    _canHomeSensor = SkyServer.CanHomeSensor;
                    _canPolarLed = SkyServer.CanPolarLed;
                    _canAdvancedCmdSupport = SkyServer.CanAdvancedCmdSupport;
                    _mountName = SkyServer.MountName ?? string.Empty;
                    _mountVersion = SkyServer.MountVersion ?? string.Empty;
                    _capabilities = SkyServer.Capabilities ?? string.Empty;

                    // Log instance values for verification
                    var monitorItemSky = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Phase4.1|Instance:{_id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{_canPPec}|MountName:{_mountName}"
                    };
                    MonitorLog.LogToMonitor(monitorItemSky);

                    //CanHomeSensor = true; //test auto home

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / PecBinCount;

                    SkyServer.CalcCustomTrackingOffset();

                    // Initialize slew speeds
                    SkyServer.SetSlewRates(_settings.MaxSlewRate);

                    //log current positions
                    var steps = GetRawSteps();
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"GetSteps:{steps[0]}|{steps[1]}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        //get positions and log them
                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetDegrees|{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
                        MonitorLog.LogToMonitor(monitorItem);

                        //if an error getting positions then stay in while loop and try again
                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (_atPark)
                        {
                            _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        //was mount powered and at 0,0  are both axes close to home?  if not then don't change current mount positions 
                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        //Mount is close to home 0,0 so set the default position
                        _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                        _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    // Update AlignmentModel settings.
                    SkyServer.ConnectAlignmentModel();

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            msg = positionsSet ? $"SetPositions|{positions[0]}|{positions[1]}" : $"PositionsNotSet";
            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"MountAxes|{_appAxes.X}|{_appAxes.Y}|Actual|{SkyServer.ActualAxisX}|{SkyServer.ActualAxisY}" };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"StepsPerRevolution|{SkyServer.StepsPerRevolution[0]}|{SkyServer.StepsPerRevolution[1]}" };
            MonitorLog.LogToMonitor(monitorItem);

            //Load Pec Files
            var pecmsg = string.Empty;
            if (File.Exists(_settings.PecWormFile))
            {
                LoadPecFile(_settings.PecWormFile);
                pecmsg += _settings.PecWormFile;
            }

            if (File.Exists(_settings.Pec360File))
            {
                LoadPecFile(_settings.Pec360File);
                pecmsg += ", " + _settings.Pec360File;
            }

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Pec: {pecmsg}" };
            MonitorLog.LogToMonitor(monitorItem);

            try
            {
                // Get path to current version's appsettings.user.json file
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                // Get version from assembly (matches VersionedSettingsService logic)
                var infoVersionAttr = assembly
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as AssemblyInformationalVersionAttribute;

                var version = infoVersionAttr?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString()
                    ?? "1.0.0";

                // Remove build metadata (e.g., +commitHash)
                var plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                {
                    version = version.Substring(0, plusIndex);
                }

                var userSettingsPath = Path.Combine(appData, "GreenSwampAlpaca", version, "appsettings.user.json");
                var logDirectoryPath = GsFile.GetLogPath();

                if (File.Exists(userSettingsPath))
                {
                    // Copy the appsettings.user.json file to the log directory
                    var destinationPath = Path.Combine(logDirectoryPath, "appsettings.user.json");
                    File.Copy(userSettingsPath, destinationPath, true);

                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Copied appsettings.user.json to {logDirectoryPath}" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                else
                {
                    // Settings file doesn't exist yet - log info (it will be created later by the settings service)
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"appsettings.user.json not found at {userSettingsPath} - will be created on first settings save" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Cannot copy appsettings.user.json. {e.Message}" };
                MonitorLog.LogToMonitor(monitorItem);
            }

            return true;
        }
        
        /// <summary>
        /// Disconnect from mount hardware
        /// Delegates to static method
        /// </summary>
        public void Disconnect()
        {
            LogMount($"Disconnect() called on instance {_id}");

            // Stop mount operations (timers, tracking, queues, serial)
            MountStop();

        }

        /// <summary>
        /// Start mount operations
        /// Delegates to static method
        /// </summary>
        public void Start()
        {
            LogMount($"Start() called on instance {_id}");

            // Call instance method directly
            MountStart();
        }

        /// <summary>
        /// Stop mount operations
        /// Delegates to static method
        /// </summary>
        public void Stop()
        {
            LogMount($"Stop() called on instance {_id}");

            // Call instance method directly
            MountStop();
        }

        /// <summary>
        /// Reset mount to home position
        /// Delegates to static method
        /// </summary>
        public void Reset()
        {
            LogMount($"Reset() called on instance {_id}");

            // Call instance method directly
            MountReset();
        }
        /// <summary>
        /// Emergency stop - halt all motion immediately
        /// Delegates to static method
        /// </summary>
        public void EmergencyStop()
        {
            LogMount($"EmergencyStop() called on instance {_id}");
            
            // Delegate to static
            SkyServer.AbortSlewAsync(speak: false);
        }

        /// <summary>
        /// Get last error from mount
        /// Delegates to static property
        /// </summary>
        public Exception? GetLastError()
        {
            return SkyServer.MountError;
        }

        #endregion

        #region Position Methods (Migrated from static)

        /// <summary>
        /// Maps a slew target to the corresponding axes based on the specified slew type.
        /// Migrated from SkyServer.MapSlewTargetToAxes()
        /// </summary>
        /// <remarks>The mapping behavior depends on the specified slew type:
        /// - For SlewRaDec: target is converted to RA/Dec axes and synchronized
        /// - For SlewAltAz: target is converted to Alt/Az axes
        /// - For SlewPark, SlewHome, SlewMoveAxis: target is converted to mount-specific axes
        /// </remarks>
        /// <param name="target">Target coordinates to be mapped</param>
        /// <param name="slewType">Type of slew operation</param>
        /// <param name="atTime">Optional UTC time for LST calculation (used for predicted coordinates)</param>
        /// <returns>Target coordinates mapped to appropriate axes</returns>        
        public double[] MapSlewTargetToAxes(double[] target, SlewType slewType, DateTime? atTime = null)
        {
            // Convert target to axes based on slew type
            // Create context from current settings
            var context = AxesContext.FromSettings(_settings);

            // If a specific time is provided, calculate LST at that time
            if (atTime.HasValue)
            {
                var lst = SkyServer.GetLocalSiderealTime(atTime.Value);
                context = context with { LocalSiderealTime = lst };
            }

            switch (slewType)
            {
                case SlewType.SlewRaDec:
                    // convert target to axis for Ra / Dec slew
                    target = Axes.RaDecToAxesXy(target, context);
                    // Convert to synced axes
                    target = SkyServer.GetSyncedAxes(target);
                    break;
                case SlewType.SlewAltAz:
                    // convert target to axis for Az / Alt slew
                    target = Axes.AzAltToAxesXy(target, context);
                    break;
                case SlewType.SlewHome:
                    break;
                case SlewType.SlewPark:
                    // convert to mount coordinates for park
                    target = Axes.AxesAppToMount(target, context);
                    break;
                case SlewType.SlewMoveAxis:
                    target = Axes.AxesAppToMount(target, context);
                    break;
                default:
                    break;
            }
            return target;
        }

        /// <summary>
        /// Gets current converted positions from the mount in degrees
        /// Migrated from SkyServer.GetRawDegrees()
        /// </summary>
        internal double[]? GetRawDegrees()
        {
            var actualDegrees = new[] { double.NaN, double.NaN };
            if (!SkyServer.IsMountRunning) { return actualDegrees; }

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    actualDegrees = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    break;

                case MountType.SkyWatcher:
                    var skyPositions = new SkyGetPositionsInDegrees(SkyQueue.NewId);
                    actualDegrees = (double[])SkyQueue.GetCommandResult(skyPositions).Result;
                    if (!skyPositions.Successful || skyPositions.Exception != null)
                        return null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return actualDegrees;
        }

        /// <summary>
        /// Convert steps to degrees
        /// Migrated from SkyServer.ConvertStepsToDegrees()
        /// </summary>
        internal double ConvertStepsToDegrees(double steps, int axis)
        {
            double degrees;
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    degrees = steps / _factorStep[axis];
                    break;

                case MountType.SkyWatcher:
                    degrees = Principles.Units.Rad2Deg1(steps * _factorStep[axis]);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            return degrees;
        }

        /// <summary>
        /// Get steps from the mount
        /// Migrated from SkyServer.GetRawSteps()
        /// </summary>
        internal double[]? GetRawSteps()
        {
            var steps = new[] { double.NaN, double.NaN };
            if (!SkyServer.IsMountRunning) { return steps; }

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    steps = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    steps[0] *= _factorStep[0];
                    steps[1] *= _factorStep[1];
                    break;

                case MountType.SkyWatcher:
                    var skySteps = new SkyGetSteps(SkyQueue.NewId);
                    steps = (double[])SkyQueue.GetCommandResult(skySteps).Result;
                    if (!skySteps.Successful || skySteps.Exception != null)
                        return null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return steps;
        }

        /// <summary>
        /// Gets current positions from the mount in steps for a specific axis
        /// Migrated from SkyServer.GetRawSteps(int axis)
        /// </summary>
        /// <param name="axis">Axis index (0 = RA/Az, 1 = Dec/Alt)</param>
        /// <returns>Position in steps, or null if not available</returns>
        internal double? GetRawSteps(int axis)
        {
            if (!SkyServer.IsMountRunning) { return null; }

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxisSteps(MountQueueInstance!.NewId, MountQueueInstance);
                    var a = (int[])MountQueueInstance.GetCommandResult(simPositions).Result;

                    switch (axis)
                    {
                        case 0:
                            return Convert.ToDouble(a[0]);
                        case 1:
                            return Convert.ToDouble(a[1]);
                        default:
                            return null;
                    }

                case MountType.SkyWatcher:
                    switch (axis)
                    {
                        case 0:
                            var b = new SkyGetAxisPositionCounter(SkyQueueInstance!.NewId, SkyQueueInstance, Axis.Axis1);
                            return Convert.ToDouble(SkyQueueInstance.GetCommandResult(b).Result);
                        case 1:
                            var c = new SkyGetAxisPositionCounter(SkyQueueInstance!.NewId, SkyQueueInstance, Axis.Axis2);
                            return Convert.ToDouble(SkyQueueInstance.GetCommandResult(c).Result);
                        default:
                            return null;
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Main get for the Steps
        /// Migrated from SkyServer.UpdateSteps()
        /// </summary>
        internal void UpdateSteps()
        {
            lock (_lastUpdateLock)
            {
                if (SkyServer.IsMountRunning || (_lastUpdateStepsTime.AddMilliseconds(100) < HiResDateTime.UtcNow))
                {
                    switch (_settings.Mount)
                    {
                        case MountType.Simulator:
                            _ = new CmdAxesSteps(MountQueueInstance!.NewId, MountQueueInstance);
                            break;
                        case MountType.SkyWatcher:
                            _ = new SkyUpdateSteps(SkyQueueInstance!.NewId, SkyQueueInstance);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    _lastUpdateStepsTime = HiResDateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Runs the coordinate conversion pipeline for a new set of hardware axis step counts.
        /// Migrated from SkyServer.Steps setter.
        /// </summary>
        /// <param name="steps">Raw step counts from the mount hardware [axis0, axis1]</param>
        internal void SetSteps(double[] steps)
        {
            // Build axes context from instance settings
            var context = AxesContext.FromSettings(_settings);

            // Implement PEC
            PecCheck();

            // Convert raw steps to degrees
            var rawPositions = new[]
            {
                ConvertStepsToDegrees(steps[0], 0),
                ConvertStepsToDegrees(steps[1], 1)
            };
            SkyServer.UpdateMountLimitStatus(rawPositions);

            // UI diagnostics in degrees
            _actualAxisX = rawPositions[0];
            _actualAxisY = rawPositions[1];

            // Convert physical positions to local app axes
            var axes = Axes.AxesMountToApp(rawPositions, context);

            // UI diagnostics for local app axes
            _appAxes.X = axes[0];
            _appAxes.Y = axes[1];

            // Calculate mount Alt/Az
            var altAz = Axes.AxesXyToAzAlt(axes, context);
            _altAzm.X = altAz[0];
            _altAzm.Y = altAz[1];

            // Calculate topocentric RA/Dec
            var raDec = Axes.AxesXyToRaDec(axes, context);
            _raDec.X = raDec[0];
            _raDec.Y = raDec[1];

            // Calculate EquatorialSystem RA/Dec for UI
            var xy = Transforms.InternalToCoordType(raDec[0], raDec[1]);
            _rightAscensionXForm = xy.X;
            _declinationXForm = xy.Y;
        }

        /// <summary>
        /// Get home axes adjusted for angle offset
        /// Migrated from SkyServer.GetHomeAxes()
        /// </summary>
        /// <param name="xAxis">X axis position</param>
        /// <param name="yAxis">Y axis position</param>
        /// <returns>Home axes vector adjusted for alignment mode and hemisphere</returns>
        internal Vector GetHomeAxes(double xAxis, double yAxis)
        {
            // Create context from current settings
            var context = AxesContext.FromSettings(_settings);
            var home = new[] { xAxis, yAxis };
            if (_settings.AlignmentMode != AlignmentMode.Polar)
            {
                home = Axes.AxesAppToMount(new[] { xAxis, yAxis }, context);
            }
            else
            {
                var angleOffset = SkyServer.SouthernHemisphere ? 180.0 : 0.0;
                home[0] -= angleOffset;
                home = Axes.AzAltToAxesXy(home, context);
            }
            return new Vector(home[0], home[1]);
        }

        #endregion

        #region Core Operations (Migrated from static)

        /// <summary>
        /// Load default settings and slew rates
        /// Migrated from SkyServer.Defaults()
        /// </summary>
        internal void Defaults()
        {
            SkyServer.SlewSettleTime = 0;

            // Initialize FactorStep array (already initialized in constructor, but keep for compatibility)
            // _factorStep is already initialized as new double[2]

            // home axes
            _homeAxes = GetHomeAxes(_settings.HomeAxisX, _settings.HomeAxisY);

            // set the slew speeds, the longest distance is using the higher speed for longer
            _slewSpeedOne = Principles.Units.Deg2Rad1((int)_settings.HcSpeed * (15.0 / 3600)); //1x 15"/s
            _slewSpeedTwo = _slewSpeedOne * 2; //2x
            _slewSpeedThree = _slewSpeedOne * 8; //8x
            _slewSpeedFour = _slewSpeedOne * 16; //16x
            _slewSpeedFive = _slewSpeedOne * 32; //32x
            _slewSpeedSix = _slewSpeedOne * 64; //64x
            _slewSpeedSeven = _slewSpeedOne * 600; //600x
            _slewSpeedEight = _slewSpeedOne * 800; //800x

            var maxSlew = Principles.Units.Deg2Rad1(_settings.MaxSlewRate);
            SkyServer.SetSlewRates(maxSlew);

            // set the guiderates
            _guideRate = new Vector(_settings.GuideRateOffsetY, _settings.GuideRateOffsetX);
            SkyServer.SetGuideRates();
        }

        /// <summary>
        /// Reset mount to home position
        /// Migrated from SkyServer.MountReset()
        /// </summary>
        internal void MountReset()
        {
            // Phase A.6: Settings already loaded from JSON via bridge
            // Bridge keeps settings current - no need to reload from user.config
            // All 121 properties are synced bidirectionally by the bridge

            // Set home positions using current settings (already loaded)
            _homeAxes = GetHomeAxes(_settings.HomeAxisX, _settings.HomeAxisY);

            // Set axis positions
            _appAxes = new Vector(_homeAxes.X, _homeAxes.Y);
        }

        // Expose internal state for static facade backward compatibility
        internal Vector HomeAxes => _homeAxes;
        internal Vector AppAxes => _appAxes;

        /// <summary>
        /// Start connection, queues, and events
        /// Migrated from SkyServer.MountStart()
        /// </summary>
        internal void MountStart()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{_settings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // setup server defaults, stop auto-discovery, connect serial port, start queues
            Defaults();
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    Mount.Simulator.Settings.AutoHomeAxisX = (int)_settings.AutoHomeAxisX;
                    Mount.Simulator.Settings.AutoHomeAxisY = (int)_settings.AutoHomeAxisY;
                    var mqImpl = new GreenSwamp.Alpaca.Mount.Simulator.MountQueueImplementation();
                    mqImpl.SetupCallbacks(
                        steps => GreenSwamp.Alpaca.Mount.Simulator.MountQueue.Steps = steps,
                        v => GreenSwamp.Alpaca.Mount.Simulator.MountQueue.IsPulseGuidingRa = v,
                        v => GreenSwamp.Alpaca.Mount.Simulator.MountQueue.IsPulseGuidingDec = v);
                    GreenSwamp.Alpaca.Mount.Simulator.MountQueue.RegisterInstance(mqImpl);
                    MountQueue.Start();
                    MountQueueInstance = mqImpl;
                    if (MountQueue.IsRunning) { SkyServer.ConnectAlignmentModel(); }
                    else
                    { throw new Exception("Failed to start simulator queue"); }

                    break;
                case MountType.SkyWatcher:
                    // open serial port
                    SkySystem.ConnectSerial = false;
                    SkySystem.ConnectSerial = true;
                    if (!SkySystem.ConnectSerial)
                    {
                        throw new SkyServerException(ErrorCode.ErrSerialFailed,
                            $"Connection Failed: {SkySystem.Error}");
                    }
                    // Start up, pass custom mount gearing if needed
                    var custom360Steps = new[] { 0, 0 };
                    var customWormSteps = new[] { 0.0, 0.0 };
                    if (_settings.CustomGearing)
                    {
                        custom360Steps = new[] { _settings.CustomRa360Steps, _settings.CustomDec360Steps };
                        customWormSteps = new[] { (double)_settings.CustomRa360Steps / _settings.CustomRaWormTeeth, (double)_settings.CustomDec360Steps / _settings.CustomDecWormTeeth };
                    }

                    // Q2: Create instance-owned queue; register on static facade before starting
                    var sqImpl = new GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueueImplementation();
                    sqImpl.SetupCallbacks(
                        steps => GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueue.Steps = steps,
                        v => GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueue.IsPulseGuidingRa = v,
                        v => GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueue.IsPulseGuidingDec = v);
                    GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueue.RegisterInstance(sqImpl);
                    sqImpl.Start(SkySystem.Serial, custom360Steps, customWormSteps, SkyServer.LowVoltageEventSet);
                    SkyQueueInstance = sqImpl;
                    if (!SkyQueue.IsRunning)
                    {
                        throw new SkyServerException(ErrorCode.ErrMount, "Failed to start sky queue");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Run mount default commands and start the UI updates
            if (MountConnect())
            {
                // start with a stop
                SkyServer.AxesStopValidate();

                // Event to get mount positions and update UI
                // Ensure DisplayInterval is valid for MediaTimer (must be > 0)
                var displayInterval = _settings.DisplayInterval > 0 ? _settings.DisplayInterval : 200;
                _mediaTimer = new MediaTimer { Period = displayInterval, Resolution = 5 };
                _mediaTimer.Tick += OnUpdateServerEvent;
                _mediaTimer.Start();
            }
            else
            {
                MountStop();
            }
        }

        /// <summary>
        /// Stop queues and events
        /// Migrated from SkyServer.MountStop()
        /// </summary>
        internal void MountStop()
        {
            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{_settings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // Stop all asynchronous operations
            SkyServer.Tracking = false;
            SkyServer.CancelAllAsync();
            SkyServer.AxesStopValidate();
            if (_mediaTimer != null) { _mediaTimer.Tick -= OnUpdateServerEvent; }
            _mediaTimer?.Stop();
            _mediaTimer?.Dispose();
            if (_altAzTrackingTimer != null) { _altAzTrackingTimer.Tick -= SkyServer.AltAzTrackingTimerEvent; }
            _altAzTrackingTimer?.Stop();
            _altAzTrackingTimer?.Dispose();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 1000) { } //change
            sw.Stop();

            if (MountQueue.IsRunning) { MountQueue.Stop(); }

            if (SkyQueue.IsRunning)
            {
                SkyQueue.Stop();
                SkySystem.ConnectSerial = false;
            }

            MountQueueInstance = null;
            SkyQueueInstance = null;

            // ToDo - fix cleanup
            // Dispose SlewController
            _slewController?.Dispose();
            _slewController = null;

        }

        /// <summary>
        /// Phase 5.4: Instance-owned per-tick update loop.
        /// Replaces static UpdateServerEvent body — per-instance lock prevents cross-device re-entrancy.
        /// </summary>
        internal void OnUpdateServerEvent(object sender, EventArgs e)
        {
            var hasLock = false;
            try
            {
                Monitor.TryEnter(_timerLock, ref hasLock);
                if (!hasLock)
                {
                    _timerOverruns++;
                    return;
                }

                _loopCounter++;
                SkyServer.SiderealTime = SkyServer.GetLocalSiderealTime();
                SkyServer.UpdateSteps();
                SkyServer.Lha = Coordinate.Ra2Ha12(SkyServer.RightAscensionXForm, SkyServer.SiderealTime);
                SkyServer.CheckSlewState();
                SkyServer.CheckAxisLimits();
                // ToDo: Remove if not needed
                // SkyServer.CheckSpiralLimit();
                CheckPecTraining();
                SkyServer.IsHome = SkyServer.AtHome;
                switch (_settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                    case AlignmentMode.Polar:
                    case AlignmentMode.GermanPolar:
                        SkyServer.IsSideOfPier = SkyServer.SideOfPier;
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                SkyServer.SkyErrorHandler(ex);
            }
            finally
            {
                if (hasLock) { Monitor.Exit(_timerLock); }
            }
        }

        #endregion

        #region Mount Operations (Instance Methods)

        /// <summary>
        /// Simulator GOTO slew operation
        /// </summary>
        internal int SimGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            const int success = 0;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|from|{SkyServer.ActualAxisX}|{SkyServer.ActualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            token.ThrowIfCancellationRequested();
            if (_settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
            {
                var predictorRaDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                target = new[] { internalRaDec.X, internalRaDec.Y };
            }
            var simTarget = MapSlewTargetToAxes(target, slewType);
            // Work out how long the slew will take by comparing the current position and the target
            var rawPositions = GetRawDegrees();
            var deltaTime = new[] { Math.Abs(rawPositions[0] - simTarget[0]) / 8.0, Math.Abs(rawPositions[1] - simTarget[1]) / 8.0 };
            // Now update simTarget which works in physical mount axis values to the end of slew values
            DateTime targetTime = DateTime.Now.AddSeconds(Math.Max(deltaTime[0], deltaTime[1]));
            simTarget = MapSlewTargetToAxes(target, slewType, targetTime);
            // Reset flip after mapping axes
            SkyServer.FlipOnNextGoto = false;
            const int timer = 120;
            var stopwatch = Stopwatch.StartNew();

            SkyServer.SimTasks(MountTaskName.StopAxes);

            #region First Slew
            token.ThrowIfCancellationRequested();
            _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]);
            _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusx = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis1);
                var axis1Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(statusx).Result;
                var axis1Stopped = axis1Status.Stopped;

                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusy = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis2);
                var axis2Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(statusy).Result;
                var axis2Stopped = axis2Status.Stopped;

                if (!axis1Stopped || !axis2Stopped) continue;
                if (SkyServer.SlewSettleTime > 0)
                    Tasks.DelayHandler(TimeSpan.FromSeconds(SkyServer.SlewSettleTime).Milliseconds);
                break;
            }
            stopwatch.Stop();

            SkyServer.AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|GoToSeconds|{stopwatch.Elapsed.TotalSeconds}|Target|{simTarget[0]}|{simTarget[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SimPrecisionGoto(target, slewType, token);
            #endregion

            SkyServer.SimTasks(MountTaskName.StopAxes);
            return success;
        }

        /// <summary>
        /// Simulator precision GOTO operation
        /// </summary>
        internal int SimPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|from|({SkyServer.ActualAxisX},{SkyServer.ActualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            var maxTries = 0;
            double[] deltaDegree = [0.0, 0.0];
            // double[] gotoPrecision = { ConvertStepsToDegrees(4, 0), ConvertStepsToDegrees(4, 1) };
            double[] gotoPrecision = [0.5 / 3600.0, 0.5 / 3600.0];
            var deltaTime = 800; // initial delta time for predictor

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var loopTimer = Stopwatch.StartNew();

                if (maxTries > 10) { break; }
                maxTries++;

                DateTime? predictedTime = null;
                if (_settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                    target = [internalRaDec.X, internalRaDec.Y];
                    predictedTime = nextTime; // Pass the prediction time to MapSlewTargetToAxes
                }

                var simTargetAtTime = MapSlewTargetToAxes(target, slewType, predictedTime);
                var simTargetNow = MapSlewTargetToAxes(target, slewType);
                var rawPositions = GetRawDegrees();

                if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                { break; }

                deltaDegree[0] = Range.Range180(simTargetNow[0] - rawPositions[0]);
                deltaDegree[1] = Range.Range180(simTargetNow[1] - rawPositions[1]);

                var axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0];
                var axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1];
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested();
                if (!axis1AtTarget)
                    _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTargetAtTime[0] + 0.125 * deltaDegree[0]);
                token.ThrowIfCancellationRequested();
                if (!axis2AtTarget)
                    _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTargetAtTime[1] + 0.05 * deltaDegree[1]);

                var axis1Stopped = false;
                var axis2Stopped = false;

                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested();

                    if (!axis1Stopped)
                    {
                        var status1 = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis1);
                        var axis1Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(status1).Result;
                        axis1Stopped = axis1Status.Stopped;
                    }

                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Stopped)
                    {
                        var status2 = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis2);
                        var axis2Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(status2).Result;
                        axis2Stopped = axis2Status.Stopped;
                    }

                    if (axis1Stopped && axis2Stopped) { break; }
                }
                loopTimer.Stop();
                deltaTime = loopTimer.Elapsed.Milliseconds;

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Instance:{_instanceName}|Delta|({deltaDegree[0]},{deltaDegree[1]})|Seconds|{loopTimer.Elapsed.TotalSeconds}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return returnCode;
        }

        /// <summary>
        /// Simulator pulse GOTO operation for continuous tracking correction
        /// </summary>
        internal void SimPulseGoto(CancellationToken token)
        {
            var maxTries = 0;
            double[] deltaDegree = [0.0, 0.0];
            var axis1AtTarget = false;
            var axis2AtTarget = false;
            double[] gotoPrecision = [ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1)];
            long deltaTime = 250; // 250mS for simulator slew

            try
            {
                while (true)
                {
                    if (maxTries > 5) { break; }
                    maxTries++;
                    double[] simTargetNow = [0.0, 0.0];
                    double[] simTargetAtTime = [0.0, 0.0];

                    if (_settings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        var now = HiResDateTime.UtcNow;
                        var predictorRaDec = SkyPredictor.GetRaDecAtTime(now.AddMilliseconds(deltaTime));
                        var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                        simTargetAtTime = MapSlewTargetToAxes([internalRaDec.X, internalRaDec.Y], SlewType.SlewRaDec);
                        predictorRaDec = SkyPredictor.GetRaDecAtTime(now);
                        internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                        simTargetNow = MapSlewTargetToAxes([internalRaDec.X, internalRaDec.Y], SlewType.SlewRaDec);
                    }

                    var rawPositions = GetRawDegrees();
                    if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                    { break; }

                    deltaDegree[0] = Range.Range180(simTargetNow[0] - rawPositions[0]);
                    deltaDegree[1] = Range.Range180(simTargetNow[1] - rawPositions[1]);

                    axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                    axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                    if (axis1AtTarget && axis2AtTarget) { break; }

                    if (!axis1AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTargetAtTime[0]);
                    }
                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTargetAtTime[1]);
                    }

                    var stopwatch1 = Stopwatch.StartNew();
                    var axis1Stopped = false;
                    var axis2Stopped = false;

                    while (stopwatch1.Elapsed.TotalMilliseconds < 500)
                    {
                        token.ThrowIfCancellationRequested();
                        Thread.Sleep(100);

                        if (!axis1Stopped)
                        {
                                var status1 = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis1);
                                var axis1Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(status1).Result;
                                axis1Stopped = axis1Status.Stopped;
                            }

                            Thread.Sleep(100);

                            if (!axis2Stopped)
                            {
                                var status2 = new CmdAxisStatus(MountQueueInstance.NewId, MountQueueInstance, Axis.Axis2);
                                var axis2Status = (Mount.Simulator.AxisStatus)MountQueueInstance.GetCommandResult(status2).Result;
                                axis2Stopped = axis2Status.Stopped;
                            }

                        if (axis1Stopped && axis2Stopped) { break; }
                    }
                    stopwatch1.Stop();
                    deltaTime = stopwatch1.Elapsed.Milliseconds;
                    deltaTime += deltaTime / 10; // add 10% feed forward
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when operation is cancelled
            }
        }

        /// <summary>
        /// SkyWatcher GOTO slew operation
        /// </summary>
        internal int SkyGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            const int success = 0;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|from|{SkyServer.ActualAxisX}|{SkyServer.ActualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}|slewing|{slewType}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            token.ThrowIfCancellationRequested();
            var skyTarget = MapSlewTargetToAxes(target, slewType);
            // Work out how long the slew will take by comparing the current position and the target
            var rawPositions = GetRawDegrees();
            var deltaTime = new[] { Math.Abs(rawPositions[0] - skyTarget[0]) / 4.0, Math.Abs(rawPositions[1] - skyTarget[1]) / 4.0 };
            // Now update skyTarget which works in physical mount axis values to the end of slew values
            DateTime targetTime = DateTime.Now.AddSeconds(Math.Max(deltaTime[0], deltaTime[1]));
            skyTarget = MapSlewTargetToAxes(target, slewType, targetTime);
            // Reset flip after mapping axes
            SkyServer.FlipOnNextGoto = false;
            const int timer = 240;
            var stopwatch = Stopwatch.StartNew();

            SkyServer.SkyTasks(MountTaskName.StopAxes);

            #region First Slew
            token.ThrowIfCancellationRequested();
            _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);
            _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusx = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis1);
                var x = SkyQueueInstance.GetCommandResult(statusx);
                var axis1Stopped = Convert.ToBoolean(x.Result);

                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusy = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis2);
                var y = SkyQueueInstance.GetCommandResult(statusy);
                var axis2Stopped = Convert.ToBoolean(y.Result);

                if (!axis1Stopped || !axis2Stopped) { continue; }

                if (SkyServer.SlewSettleTime > 0)
                    Tasks.DelayHandler(TimeSpan.FromSeconds(SkyServer.SlewSettleTime).Milliseconds);
                break;
            }
            stopwatch.Stop();

            SkyServer.AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|Seconds|{stopwatch.Elapsed.TotalSeconds}|Target|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SkyPrecisionGoto(target, slewType, token);
            #endregion

            SkyServer.SkyTasks(MountTaskName.StopAxes);
            return success;
        }

        /// <summary>
        /// SkyWatcher precision GOTO operation
        /// </summary>
        internal int SkyPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Instance:{_instanceName}|from|({SkyServer.ActualAxisX},{SkyServer.ActualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            var maxTries = 0;
            double[] deltaDegree = [0.0, 0.0];
            var axis1AtTarget = false;
            var axis2AtTarget = false;
            double[] gotoPrecision = [_settings.GotoPrecision, _settings.GotoPrecision];
            long deltaTime = 800;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var loopTimer = Stopwatch.StartNew();

                // Event-based position update waiting
                SkyServer.MountPositionUpdatedEvent.Reset();
                UpdateSteps();

                if (!SkyServer.MountPositionUpdatedEvent.Wait(5000))
                {
                    var errorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Error,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Instance:{_instanceName}|Timeout waiting for position update|Try:{maxTries}"
                    };
                    MonitorLog.LogToMonitor(errorItem);
                    throw new TimeoutException($"Mount position update timeout in precision goto (instance: {_instanceName})");
                }

                if (maxTries >= 5) { break; }
                maxTries++;

                DateTime? predictedTime = null;
                if (_settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                    target = [internalRaDec.X, internalRaDec.Y];
                    predictedTime = nextTime; // Pass the prediction time to MapSlewTargetToAxes
                }

                var skyTargetAtTime = MapSlewTargetToAxes(target, slewType, predictedTime);
                var skyTargetNow = MapSlewTargetToAxes(target, slewType);
                var rawPositions = GetRawDegrees();

                deltaDegree[0] = Range.Range180((skyTargetNow[0] - rawPositions[0]));
                deltaDegree[1] = Range.Range180(skyTargetNow[1] - rawPositions[1]);

                axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested();
                if (!axis1AtTarget)
                {
                    _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTargetAtTime[0] + 0.25 * deltaDegree[0]);
                }

                var axis1Done = axis1AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    if (token.IsCancellationRequested) { break; }

                    if (!axis1Done)
                    {
                        var status1 = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis1);
                        axis1Done = Convert.ToBoolean(SkyQueueInstance.GetCommandResult(status1).Result);
                    }
                    if (axis1Done) { break; }
                }

                if (!axis2AtTarget)
                {
                    token.ThrowIfCancellationRequested();
                    _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTargetAtTime[1] + 0.1 * deltaDegree[1]);
                }

                var axis2Done = axis2AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Done)
                    {
                        var status2 = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis2);
                        axis2Done = Convert.ToBoolean(SkyQueueInstance.GetCommandResult(status2).Result);
                    }
                    if (axis2Done) { break; }
                }

                loopTimer.Stop();
                deltaTime = loopTimer.ElapsedMilliseconds;

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Instance:{_instanceName}|Delta|{deltaDegree[0]}|{deltaDegree[1]}|Seconds|{loopTimer.Elapsed.TotalSeconds}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return returnCode;
        }

        /// <summary>
        /// SkyWatcher pulse GOTO operation for continuous tracking correction
        /// </summary>
        internal void SkyPulseGoto(CancellationToken token)
        {
            var maxTries = 0;
            double[] deltaDegree = [0.0, 0.0];
            var axis1AtTarget = false;
            var axis2AtTarget = false;
            double[] gotoPrecision = [_settings.GotoPrecision, _settings.GotoPrecision];
            long deltaTime = 400;

            try
            {
                while (true)
                {
                    var loopTimer = Stopwatch.StartNew();

                    // Event-based position update waiting
                    SkyServer.MountPositionUpdatedEvent.Reset();
                    UpdateSteps();

                    if (!SkyServer.MountPositionUpdatedEvent.Wait(5000))
                    {
                        var errorItem = new MonitorEntry
                        {
                            Datetime = HiResDateTime.UtcNow,
                            Device = MonitorDevice.Server,
                            Category = MonitorCategory.Server,
                            Type = MonitorType.Error,
                            Method = MethodBase.GetCurrentMethod()?.Name,
                            Thread = Thread.CurrentThread.ManagedThreadId,
                            Message = $"Instance:{_instanceName}|Timeout waiting for position update in pulse goto"
                        };
                        MonitorLog.LogToMonitor(errorItem);
                        throw new TimeoutException($"Mount position update timeout in pulse goto (instance: {_instanceName})");
                    }

                    if (maxTries >= 5) { break; }
                    maxTries++;
                    double[] skyTarget = [0.0, 0.0];

                    if (_settings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                        var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                        var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                        skyTarget = MapSlewTargetToAxes([internalRaDec.X, internalRaDec.Y], SlewType.SlewRaDec);
                    }

                    var rawPositions = GetRawDegrees();
                    if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                    { break; }

                    deltaDegree[0] = skyTarget[0] - rawPositions[0];
                    deltaDegree[1] = skyTarget[1] - rawPositions[1];

                    axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                    axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                    if (axis1AtTarget && axis2AtTarget) { break; }

                    if (!axis1AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);
                    }

                    var axis1Done = axis1AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        if (SkyServer.SlewState == SlewType.SlewNone) { break; }
                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis1Done)
                        {
                            var status1 = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis1);
                            axis1Done = Convert.ToBoolean(SkyQueueInstance.GetCommandResult(status1).Result);
                        }
                        if (axis1Done) { break; }
                    }

                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);
                    }

                    var axis2Done = axis2AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        if (SkyServer.SlewState == SlewType.SlewNone) { break; }
                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis2Done)
                        {
                            var status2 = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis2);
                            axis2Done = Convert.ToBoolean(SkyQueueInstance.GetCommandResult(status2).Result);
                        }
                        if (axis2Done) { break; }
                    }

                    loopTimer.Stop();
                    deltaTime = loopTimer.ElapsedMilliseconds;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when operation is cancelled
            }
        }

        #endregion
        #region SlewController (Phase 5.3)

        /// <summary>
        /// Ensures the SlewController is initialized for this instance.
        /// </summary>
        internal void EnsureSlewController()
        {
            if (_slewController == null)
            {
                _slewController = new SlewController();

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = nameof(EnsureSlewController),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"SlewController initialized|Instance:{_id}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Modern async slew implementation using SlewController.
        /// Returns immediately after setup phase completes (&lt; 1 second).
        /// </summary>
        internal async Task<SlewResult> SlewAsync(double[] target, SlewType slewType, bool tracking = false)
        {
            EnsureSlewController();
            var operation = new SlewOperation(target, slewType, tracking);
            return await _slewController!.ExecuteSlewAsync(operation);
        }

        /// <summary>
        /// Synchronous wrapper — blocks until slew completes.
        /// Used for synchronous ASCOM methods (FindHome, SlewToCoordinates).
        /// </summary>
        internal void SlewSync(double[] target, SlewType slewType, bool tracking = false)
        {
            EnsureSlewController();
            var operation = new SlewOperation(target, slewType, tracking);
            var setupResult = _slewController!.ExecuteSlewAsync(operation).Result;
            if (!setupResult.CanProceed)
                throw new InvalidOperationException($"Slew setup failed: {setupResult.ErrorMessage}");
            _slewController.WaitForSlewCompletionAsync().Wait();
        }

        /// <summary>
        /// Wait for current slew to complete (for async operations that need completion).
        /// </summary>
        internal async Task WaitForSlewCompletionAsync()
        {
            if (_slewController != null)
                await _slewController.WaitForSlewCompletionAsync();
        }

        #endregion
        #region Logging

        private void LogMount(string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = "MountInstance",
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
