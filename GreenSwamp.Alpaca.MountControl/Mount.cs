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
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.MountControl.Interfaces;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using Range = GreenSwamp.Alpaca.Principles.Range;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Reflection;
using System.ComponentModel;
using GreenSwamp.Alpaca.Shared.Transport;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// 
    /// </summary>
    public partial class Mount : IMountController
    {
        #region Private backing fields

        private readonly string _instanceName;
        public readonly SkyPredictor SkyPredictor;

        // State fields
        private MediaTimer? _mediaTimer;
        private MediaTimer? _altAzTrackingTimer;
        internal TrackingCommandProcessor? _trackingProcessor;
        private Vector _homeAxes;
        private Vector _appAxes;
        private Vector _targetRaDec;
        internal Exception? _mountError;
        internal Vector _altAzSync;

        // Factor steps (conversion ratios)
        internal double[] _factorStep = new double[2];
        internal long[] _stepsPerRevolution = new long[2];
        internal long[] _stepsTimeFreq = [0, 0];
        internal double[] _stepsWormPerRevolution = new double[2];

        // PEC fields
        private int[] _wormTeethCount = new int[2];
        private double _pecBinSteps;


        // Mount capabilities (instance-owned)
        internal bool _canPPec;
        internal bool _canHomeSensor;
        internal bool _canPolarLed;
        internal bool _canAdvancedCmdSupport;
        internal string _mountName = string.Empty;
        internal string _mountVersion = string.Empty;
        internal string _capabilities = string.Empty;

        // Mount state
        private bool _atPark;
        private double _actualAxisX;
        private double _actualAxisY;
        private bool _isHome;
        private bool _lowVoltageEventState;
        internal bool _monitorPulse;
        private double _slewSettleTime;

        // AltAz limit status
        private LimitStatusType _limitStatus;

        // Backing fields
        private bool _isPulseGuidingRa;
        private bool _isPulseGuidingDec;
        internal Vector _rateMoveAxes;
        internal bool _moveAxisActive;
        private bool _isSlewing;
        private bool _flipOnNextGoto;
        internal SlewType _slewState;
        private Exception? _lastAutoHomeError;
        internal int _autoHomeProgressBar;
        internal bool _autoHomeStop;
        private bool _isAutoHomeRunning;
        internal bool _snapPort1Result;
        internal bool _snapPort2Result;
        internal bool _snapPort1;
        internal bool _snapPort2;
        internal GuideDirection _lastDecDirection;

        // Diagnostics
        private ulong _loopCounter;
        private int _timerOverruns;
        private AltAzTrackingType _altAzTrackingMode;
        private ParkPosition? _parkSelected;
        //Raw step counts from hardware — backing field Steps
        internal double[] _steps = [0.0, 0.0];

        // Position-update event _mountPositionUpdatedEvent
        internal readonly ManualResetEventSlim _mountPositionUpdatedEvent = new(false);

        // UpdateSteps fields
        private DateTime _lastUpdateStepsTime = DateTime.MinValue;
        private readonly object _lastUpdateLock = new();

        // Queue instances owned by this Mount
        internal CommandQueueBase<SkyWatcher> SkyQueue { get; private set; }
        internal CommandQueueBase<Actions> SimQueue { get; private set; }

        // Slew speed fields
        // All speeds stored in degrees/second for ASCOM AxisRates compliance
        // Hardware layer (SkyWatcher.AxisSlew, Simulator.MoveAxisRate) converts to radians as needed
        private double _slewSpeedOne;      // Speed level 1: maxRate × 0.0034
        private double _slewSpeedTwo;      // Speed level 2: maxRate × 0.0068
        private double _slewSpeedThree;    // Speed level 3: maxRate × 0.047
        private double _slewSpeedFour;     // Speed level 4: maxRate × 0.068
        private double _slewSpeedFive;     // Speed level 5: maxRate × 0.2
        private double _slewSpeedSix;      // Speed level 6: maxRate × 0.4
        private double _slewSpeedSeven;    // Speed level 7: maxRate × 0.8
        private double _slewSpeedEight;    // Speed level 8: maxRate × 1.0 (max slew rate)

        // Tracking state fields

        // CancellationTokenSources
        internal volatile CancellationTokenSource? _ctsGoTo;
        internal volatile CancellationTokenSource? _ctsPulseGuideRa;
        internal volatile CancellationTokenSource? _ctsPulseGuideDec;
        internal volatile CancellationTokenSource? _ctsHcPulseGuide;

        // SlewController — isolate slew state across devices
        private SlewController? _slewController;

        // Timer lock (isolates update loop re-entrancy per device)
        private readonly object _timerLock = new();

        // SkyWatcher tracking rates (internal use only)
        internal Vector _skyTrackingRate = new(0, 0);

        // HC anti-backlash direction state
        internal HcPrevMove? _hcPrevMoveRa;
        internal HcPrevMove? _hcPrevMoveDec;
        internal readonly IList<double> _hcPrevMovesDec = new List<double>();

        // Custom tracking rate offset
        internal Vector _trackingOffsetRate;

        // SkyWatcher :I offset accumulator
        private readonly int[] _skyTrackingOffset = [0, 0];

        // Guide rate field
        private Vector _guideRate;

        // Rate fields (target and guide rate already exist above)
        private Vector _rateRaDec = new(0, 0);

        // Original rate storage (for direction tracking)

        // Position and coordinate fields
        private Vector _raDec = new(0, 0);
        private Vector _altAzm = new(0, 0);

        // Serial connection fields
        private ISerialPort? _serial;
        private ConnectType _connectType = ConnectType.None;
        private Exception? _serialError;
        private readonly ConcurrentDictionary<long, bool> _connectStates = new();
        public bool Connecting { get; private set; }

        #endregion

        #region Public State Exposure

        /// <summary>
        /// Gets whether the mount hardware queue is currently running.
        /// </summary>
        public bool IsMountRunning => Settings.Mount switch
        {
            MountType.Simulator => SimQueue?.IsRunning ?? false,
            MountType.SkyWatcher => SkyQueue?.IsRunning ?? false,
            _ => false
        };

        /// <summary>
        /// Gets the user-provided device name
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Gets the settings for this mount
        /// </summary>
        public SkySettings Settings { get; }

        /// <summary>
        /// Gets or sets the target RA/Dec position
        /// </summary>
        public Vector TargetRaDec
        {
            get => _targetRaDec;
            set => _targetRaDec = value;
        }


        // Tracking state properties
        public bool Tracking { get; internal set; }

        internal TrackingMode TrackingMode { get; set; } = TrackingMode.Off;

        // SkyWatcher-specific tracking rates (internal access only)
        internal Vector SkyTrackingRate
        {
            get => _skyTrackingRate;
            set => _skyTrackingRate = value;
        }

        private Vector SkyHcRate { get; set; } = new Vector(0, 0);

        // Target and rate properties
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

        internal double RateRa
        {
            get => _rateRaDec.X;
            set => _rateRaDec.X = value;
        }

        internal double RateDec
        {
            get => _rateRaDec.Y;
            set => _rateRaDec.Y = value;
        }

        public double RateRaOrg { get; set; }

        public double RateDecOrg { get; set; }

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

        // Position and coordinate properties
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

        public double RightAscensionXForm { get; private set; }

        public double DeclinationXForm { get; private set; }

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

        public double SiderealTime { get; private set; }

        public double Lha { get; private set; }

        public PointingState IsSideOfPier { get; set; } = PointingState.Unknown;

        // Computed pier-side (same logic as static SkyServer.SideOfPier)
        public PointingState SideOfPier
        {
            get
            {
                switch (Settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        return _actualAxisX >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
                    case AlignmentMode.Polar:
                        return (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001)
                            ? PointingState.Normal : PointingState.ThroughThePole;
                    case AlignmentMode.GermanPolar:
                        bool southernHemisphere = Settings.Latitude < 0;
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

        // AtHome — computed from current appAxes vs homeAxes
        public bool AtHome
        {
            get
            {
                if (!IsConnected) return false;
                var home = Axes.AxesMountToApp([_homeAxes.X, _homeAxes.Y], Settings);
                double dX = Math.Abs(_appAxes.X - home[0]);
                dX = Math.Min(dX, 360.0 - dX);
                double dY = Math.Abs(_appAxes.Y - home[1]);
                return (dX * dX + dY * dY) < 0.01414;
            }
        }

        //AtPark — delegates to settings (same source as SkyServer.AtPark)
        public bool AtPark
        {
            get => Settings.AtPark;
            set => Settings.AtPark = value;
        }

        // IsSlewing — mirrors SkyServer.IsSlewing logic using per-instance fields
        public bool IsSlewing =>
            (_slewController?.IsSlewing == true) ||
            (Math.Abs(_rateMoveAxes.X) + Math.Abs(_rateMoveAxes.Y)) > 0 ||
            _moveAxisActive ||
            _isSlewing;

        // IsPulseGuiding — combined pulse guide state
        public bool IsPulseGuiding => _isPulseGuidingRa || _isPulseGuidingDec;

        // IsPulseGuidingRa / IsPulseGuidingDec — public access for Telescope.cs
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

        // SlewState — public access to slew state
        public SlewType SlewState
        {
            get => _slewState;
            set => _slewState = value;
        }

        // SlewSettleTime — public access to settle time
        public double SlewSettleTime
        {
            get => _slewSettleTime;
            set => _slewSettleTime = value;
        }

        // Diagnostics and tracking mode
        public ulong LoopCounter { get => _loopCounter; internal set => _loopCounter = value; }
        public int TimerOverruns { get => _timerOverruns; internal set => _timerOverruns = value; }
        public AltAzTrackingType AltAzTrackingMode { get => _altAzTrackingMode; set => _altAzTrackingMode = value; }

        // Blazor UI: axis and step positions
        public double ActualAxisX => _actualAxisX;
        public double ActualAxisY => _actualAxisY;
        public double AppAxisX => _appAxes.X;
        public double AppAxisY => _appAxes.Y;
        public double[] Steps => _steps;

        public int AutoHomeProgressBar
        {
            get => _autoHomeProgressBar;
            set => _autoHomeProgressBar = value;
        }

        public bool AutoHomeStop
        {
            get => _autoHomeStop;
            set => _autoHomeStop = value;
        }

        public bool IsAutoHomeRunning
        {
            get => _isAutoHomeRunning;
            internal set => _isAutoHomeRunning = value;
        }

        public Exception? LastAutoHomeError
        {
            get => _lastAutoHomeError;
            internal set => _lastAutoHomeError = value;
        }

        #endregion

        #region Internal State Exposure (for other MountControl classes)

        /// <summary>
        /// Sets the tracking state (internal method for SkyServer)
        /// Called by SkyServer.Tracking property setter
        /// </summary>
        /// <param name="tracking">New tracking state</param>
        private void SetTracking(bool tracking)
        {
            Tracking = tracking;
        }
        
        /// <summary>
        /// Alt/Az sync position for Alt/Az mode syncing
        /// /// </summary>
        internal Vector AltAzSync
        {
            get => _altAzSync;
            set => _altAzSync = value;
        }

        /// <summary>
        /// Slew speeds for hand controller and ASCOM AxisRates (read-only)
        /// All values in degrees/second per ASCOM specification
        /// </summary>
        public double SlewSpeedOne => _slewSpeedOne;
        public double SlewSpeedTwo => _slewSpeedTwo;
        public double SlewSpeedThree => _slewSpeedThree;
        public double SlewSpeedFour => _slewSpeedFour;
        public double SlewSpeedFive => _slewSpeedFive;
        public double SlewSpeedSix => _slewSpeedSix;
        public double SlewSpeedSeven => _slewSpeedSeven;
        public double SlewSpeedEight => _slewSpeedEight;
        public bool SnapPort1 { get => _snapPort1; set => _snapPort1 = value; }
        public bool SnapPort2 { get => _snapPort2; set => _snapPort2 = value; }
        public bool SnapPort1Result => _snapPort1Result;
        public bool SnapPort2Result => _snapPort2Result;
        #endregion
        /// <summary>
        /// Constructor with optional settings file path
        /// Added deviceName parameter for user-visible device identification
        /// </summary>
        /// <param name="id">Unique instance identifier (e.g., "telescope-0")</param>
        /// <param name="settings">Settings instance (can be file-based or static)</param>
        /// <param name="deviceName">User-provided device name (defaults to id if null)</param>
        public Mount(string id, SkySettings settings, string? deviceName = null)
        {
            Id = id ?? "mount-0";
            _instanceName = id ?? "default";
            DeviceName = deviceName ?? id ?? "Unnamed Device";
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            // Wire settings back-reference so settings can call instance-aware tasks
            Settings._owner = this;
            SkyPredictor = new SkyPredictor(() => SkyServer.CurrentTrackingRate(this));

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount created|ID:{Id}|Mount:{Settings.Mount}|Port:{Settings.Port}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #region IMountController Implementation (Delegation)

        /// <summary>
        /// Gets the unique identifier for this mount instance
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets whether the mount is currently connected
        /// </summary>
        public bool IsConnected => !_connectStates.IsEmpty;

        /// <summary>
        /// Gets whether the mount is currently running
        /// </summary>
        public bool IsRunning => IsMountRunning;

        /// <summary>
        /// Connect to mount hardware
        /// Implemented (was stub in 3.1)
        /// </summary>
        public bool Connect()
        {
            LogMount($"Connect() called on instance {Id}");

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
            var positions = this.GetDefaultPositions();
            double[]? rawPositions = null;
            var counter = 0;
            int raWormTeeth;
            int decWormTeeth;
            bool positionsSet = false;
            MonitorEntry monitorItem;
            string msg;

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    // defaults
                    SkyServer.SimTasks(MountTaskName.MountName, this);
                    SkyServer.SimTasks(MountTaskName.MountVersion, this);
                    SkyServer.SimTasks(MountTaskName.StepsPerRevolution, this);
                    SkyServer.SimTasks(MountTaskName.StepsWormPerRevolution, this);
                    SkyServer.SimTasks(MountTaskName.CanHomeSensor, this);
                    SkyServer.SimTasks(MountTaskName.GetFactorStep, this);
                    SkyServer.SimTasks(MountTaskName.Capabilities, this);


                    // Log instance values for verification
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{Id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{_canPPec}|MountName:{_mountName}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = [raWormTeeth, decWormTeeth];
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / PecBinCount;

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetRawDegrees:{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
                        MonitorLog.LogToMonitor(monitorItem);

                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (_atPark)
                        {
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                        _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    break;
                case MountType.SkyWatcher:
                    SkyHcRate = new Vector(0, 0);
                    SkyTrackingRate = new Vector(0, 0);

                    // create a command and put in queue to test connection
                    var init = new SkyGetMotorCardVersion(SkyQueue!.NewId, SkyQueue, Axis.Axis1);
                    _ = (string)SkyQueue.GetCommandResult(init).Result;
                    if (!init.Successful && init.Exception != null)
                    {
                        // ToDo: fix string resource
                        init.Exception = new Exception($"CheckMount{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        // init.Exception = new Exception($"{MediaTypeNames.Application.Current.Resources["CheckMount"]}{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        SkyServer.SkyErrorHandler(init.Exception, this);
                        return false;
                    }

                    var controllerVoltage = double.NaN;
                    try
                    {
                        if (SkyQueue != null)
                        {
                            var vs = new SkyGetControllerVoltage(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                            controllerVoltage = (double)SkyQueue.GetCommandResult(vs).Result;
                        }
                    }
                    catch { }
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Voltage|{controllerVoltage:F2} V"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    // defaults
                    if (Settings.Mount == MountType.SkyWatcher)
                    {
                        SkyServer.SkyTasks(MountTaskName.AllowAdvancedCommandSet, this);
                    }
                    SkyServer.SkyTasks(MountTaskName.LoadDefaults, this);
                    SkyServer.SkyTasks(MountTaskName.StepsPerRevolution, this);
                    SkyServer.SkyTasks(MountTaskName.StepsWormPerRevolution, this);
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    SkyServer.SkyTasks(MountTaskName.Encoders, this);
                    SkyServer.SkyTasks(MountTaskName.FullCurrent, this);
                    SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate, this);
                    SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere, this);
                    SkyServer.SkyTasks(MountTaskName.MountName, this);
                    SkyServer.SkyTasks(MountTaskName.MountVersion, this);
                    SkyServer.SkyTasks(MountTaskName.StepTimeFreq, this);
                    SkyServer.SkyTasks(MountTaskName.CanPpec, this);
                    SkyServer.SkyTasks(MountTaskName.CanPolarLed, this);
                    SkyServer.SkyTasks(MountTaskName.PolarLedLevel, this);
                    SkyServer.SkyTasks(MountTaskName.CanHomeSensor, this);
                    SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo, this);
                    SkyServer.SkyTasks(MountTaskName.AlternatingPpec, this);
                    SkyServer.SkyTasks(MountTaskName.MinPulseDec, this);
                    SkyServer.SkyTasks(MountTaskName.MinPulseRa, this);
                    SkyServer.SkyTasks(MountTaskName.GetFactorStep, this);
                    SkyServer.SkyTasks(MountTaskName.Capabilities, this);
                    SkyServer.SkyTasks(MountTaskName.CanAdvancedCmdSupport, this);
                    if (_canPPec) SkyServer.SkyTasks(MountTaskName.Pec, this);


                    // Log instance values for verification
                    var monitorItemSky = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{Id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{_canPPec}|MountName:{_mountName}"
                    };
                    MonitorLog.LogToMonitor(monitorItemSky);

                    //CanHomeSensor = true; //test auto home

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = [raWormTeeth, decWormTeeth];
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / PecBinCount;

                    this.CalcCustomTrackingOffset();

                    // Initialize slew speeds
                    this.SetSlewRates(Settings.MaxSlewRate);

                    //log current positions
                    var steps = GetRawSteps();
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"GetSteps:{steps[0]}|{steps[1]}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        //get positions and log them
                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetDegrees|{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
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
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        //was mount powered and at 0,0  are both axes close to home?  if not then don't change current mount positions 
                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        //Mount is close to home 0,0 so set the default position
                        _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis1, positions[0]);
                        _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            msg = positionsSet ? $"SetPositions|{positions[0]}|{positions[1]}" : $"PositionsNotSet";
            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"MountAxes|{_appAxes.X}|{_appAxes.Y}|Actual|{_actualAxisX}|{_actualAxisY}" };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"StepsPerRevolution|{_stepsPerRevolution[0]}|{_stepsPerRevolution[1]}" };
            MonitorLog.LogToMonitor(monitorItem);

            //Load Pec Files
            var pecmsg = string.Empty;
            if (File.Exists(Settings.PecWormFile))
            {
                LoadPecFile(Settings.PecWormFile);
                pecmsg += Settings.PecWormFile;
            }

            if (File.Exists(Settings.Pec360File))
            {
                LoadPecFile(Settings.Pec360File);
                pecmsg += ", " + Settings.Pec360File;
            }

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Pec: {pecmsg}" };
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
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Copied appsettings.user.json to {logDirectoryPath}" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                else
                {
                    // Settings file doesn't exist yet - log info (it will be created later by the settings service)
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"appsettings.user.json not found at {userSettingsPath} - will be created on first settings save" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Cannot copy appsettings.user.json. {e.Message}" };
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
            LogMount($"Disconnect() called on instance {Id}");

            // Stop mount operations (timers, tracking, queues, serial)
            MountStop();

        }

        /// <summary>
        /// Start mount operations
        /// Delegates to static method
        /// </summary>
        public void Start()
        {
            LogMount($"Start() called on instance {Id}");

            // Call instance method directly
            MountStart();
        }

        /// <summary>
        /// Stop mount operations
        /// Delegates to static method
        /// </summary>
        public void Stop()
        {
            LogMount($"Stop() called on instance {Id}");

            // Call instance method directly
            MountStop();
        }

        /// <summary>
        /// Reset mount to home position
        /// Delegates to static method
        /// </summary>
        public void Reset()
        {
            LogMount($"Reset() called on instance {Id}");

            // Call instance method directly
            MountReset();
        }
        /// <summary>
        /// Emergency stop - halt all motion immediately
        /// </summary>
        public void EmergencyStop()
        {
            LogMount($"EmergencyStop() called on instance {Id}");
            AbortSlewAsync(speak: false);
        }

        /// <summary>
        /// Get last error from mount
        /// </summary>
        public Exception? GetLastError()
        {
            return _mountError;
        }

        #endregion

        #region Telescope API Bridge Methods (Step 9 — fully migrated per-instance implementations)

        /// <summary>Rate move on primary axis — L: dispatches to this device's hardware queue.</summary>
        public double RateMovePrimaryAxis
        {
            get => _rateMoveAxes.X;
            set
            {
                if (Math.Abs(_rateMoveAxes.X - value) < 0.0000000001) return;
                _rateMoveAxes.X = value;
                CancelAllAsync();
                SetRateMoveSlewState();
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(SimQueue!.NewId, SimQueue, Axis.Axis1, _rateMoveAxes.X);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(SkyQueue!.NewId, SkyQueue, Axis.Axis1, _rateMoveAxes.X);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                if (Tracking) this.SetTracking();
                LogMount($"RateMovePrimaryAxis|{_rateMoveAxes.X}|offset:{_skyTrackingOffset[0]}");
            }
        }

        /// <summary>Rate move on secondary axis — L: dispatches to this device's hardware queue.</summary>
        public double RateMoveSecondaryAxis
        {
            get => _rateMoveAxes.Y;
            set
            {
                if (Math.Abs(_rateMoveAxes.Y - value) < 0.0000000001) return;
                _rateMoveAxes.Y = value;
                CancelAllAsync();
                SetRateMoveSlewState();
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(SimQueue!.NewId, SimQueue, Axis.Axis2, -_rateMoveAxes.Y);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(SkyQueue!.NewId, SkyQueue, Axis.Axis2, _rateMoveAxes.Y);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                if (Tracking) this.SetTracking();
                LogMount($"RateMoveSecondaryAxis|{_rateMoveAxes.Y}|offset:{_skyTrackingOffset[1]}");
            }
        }

        /// <summary>Selected park position — L: uses this device's _parkSelected field and settings.</summary>
        public ParkPosition ParkSelected
        {
            get
            {
                if (_parkSelected == null)
                {
                    if (!string.IsNullOrEmpty(Settings.ParkName))
                    {
                        var found = Settings.ParkPositions?.Find(x => x.Name == Settings.ParkName);
                        if (found != null)
                        {
                            _parkSelected = new ParkPosition(found.Name, found.X, found.Y);
                            return _parkSelected;
                        }
                    }
                    if (Settings.ParkAxes != null && Settings.ParkAxes.Length >= 2)
                    {
                        _parkSelected = new ParkPosition("Park", Settings.ParkAxes[0], Settings.ParkAxes[1]);
                    }
                }
                return _parkSelected;
            }
            set
            {
                if (_parkSelected != null)
                {
                    if (_parkSelected.Name == value.Name && Math.Abs(_parkSelected.X - value.X) < 0 &&
                        Math.Abs(_parkSelected.Y - value.Y) < 0) { return; }
                }
                _parkSelected = new ParkPosition(value.Name, value.X, value.Y);
                LogMount($"ParkSelected|{value.Name}|{value.X}|{value.Y}");
            }
        }

        /// <summary>Set SideOfPier (triggers pier flip).</summary>
        public void SetSideOfPier(PointingState value)
        {
            var axes = new[] { _actualAxisX, _actualAxisY };
            if (IsWithinFlipLimits(Axes.AxesMountToApp(axes, Settings)))
            {
                _flipOnNextGoto = true;
                if (Tracking)
                    _ = SlewRaDecAsync(RightAscensionXForm, DeclinationXForm, true);
                else
                    _ = SlewAltAzAsync(Altitude, Azimuth);
                LogMount($"SetSideOfPier|{value}|limit:{Settings.HourAngleLimit}|{axes[0]}|{axes[1]}");
            }
            else
            {
                throw new InvalidOperationException($"SideOfPier ({value}) is outside the range of set Limits");
            }
        }

        /// <summary>Set RateDec with ActionRateRaDec side effect — L: uses this device's fields.</summary>
        public void SetRateDec(double degrees)
        {
            RateDec = degrees;
            if (Settings.AlignmentMode == AlignmentMode.AltAz && _trackingProcessor != null)
            {
                // Writer-side merge: carry current RateRa so the consumer applies both axes atomically (D2).
                _trackingProcessor.Post(new RateChangeCommand(RateRa, degrees));
            }
            else
            {
                ActionRateRaDec();
            }
            LogMount($"SetRateDec|{degrees}|offset:{_skyTrackingOffset[1]}");
        }

        /// <summary>Set RateRa with ActionRateRaDec side effect — L: uses this device's fields.</summary>
        public void SetRateRa(double degrees)
        {
            RateRa = degrees;
            if (Settings.AlignmentMode == AlignmentMode.AltAz && _trackingProcessor != null)
            {
                // Writer-side merge: carry current RateDec so the consumer applies both axes atomically (D2).
                _trackingProcessor.Post(new RateChangeCommand(degrees, RateDec));
            }
            else
            {
                ActionRateRaDec();
            }
            LogMount($"SetRateRa|{degrees}|offset:{_skyTrackingOffset[0]}");
        }

        /// <summary>Abort any active slew — L: operates on this device's hardware queue.</summary>
        public void AbortSlewAsync(bool speak)
        {
            if (!IsMountRunning) return;
            var tracking = Tracking || _slewState == SlewType.SlewRaDec || _moveAxisActive;
            // Abort path is synchronous for all alignment modes — bypasses the tracking queue
            // to avoid consumer-dispatch latency during an abort. The queue is still used for
            // normal rate-change and timer-tick paths.
            ApplyTracking(false);
            // Signal cancellation — returns immediately per ASCOM non-blocking spec.
            // Background task (ExecuteMovementAndCompletionAsync) handles hardware deceleration
            // and sets SlewController.IsSlewing=false when axes physically stop.
            // Mount.IsSlewing stays true via _slewController?.IsSlewing until that completes.
            _slewController?.RequestCancellation();
            CancelAllAsync();
            _moveAxisActive = false;
            _rateMoveAxes = new Vector(0, 0);
            _rateRaDec = new Vector(0, 0);
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SkyServer.SimTasks(MountTaskName.StopAxes, this);
                    break;
                case MountType.SkyWatcher:
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (Settings.AlignmentMode == AlignmentMode.AltAz)
                SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            ApplyTracking(tracking);
            _slewState = SlewType.SlewNone;
            LogMount($"AbortSlewAsync|restored tracking:{tracking}");
        }

        /// <summary>Returns whether the specified axis can be moved — L: uses this device's NumMoveAxis setting.</summary>
        public bool CanMoveAxis(TelescopeAxis axis)
        {
            var ax = axis switch
            {
                TelescopeAxis.Primary => 1,
                TelescopeAxis.Secondary => 2,
                TelescopeAxis.Tertiary => 3,
                _ => 0
            };
            return ax != 0 && ax <= Settings.NumMoveAxis;
        }

        /// <summary>Determine side of pier for given RA/Dec — L: uses this device's SideOfPier and settings.</summary>
        public PointingState DetermineSideOfPier(double rightAscension, double declination)
        {
            if (Settings.AlignmentMode == AlignmentMode.AltAz)
                return PointingState.Unknown;
            var sop = SideOfPier;
            var flipReq = Axes.IsFlipRequired([rightAscension, declination], Settings, sop);
            LogMount($"DetermineSideOfPier|Ra:{rightAscension}|Dec:{declination}|Flip:{flipReq}|SoP:{sop}");
            return sop switch
            {
                PointingState.Normal => flipReq ? PointingState.ThroughThePole : PointingState.Normal,
                PointingState.ThroughThePole => flipReq ? PointingState.Normal : PointingState.ThroughThePole,
                _ => PointingState.Unknown
            };
        }

        /// <summary>Start GoTo Home — L: uses this device's HomeAxes, AtHome, and SlewAsync.</summary>
        public Task<SlewResult> GoToHome()
        {
            if (AtHome || _slewState == SlewType.SlewHome)
                return Task.FromResult(SlewResult.Failed("Already at home or home slew in progress"));
            ApplyTracking(false);
            LogMount("GoToHome|Async using per-instance SlewController");
            var target = new[] { _homeAxes.X, _homeAxes.Y };
            return SlewAsync(target, SlewType.SlewHome, tracking: false);
        }

        /// <summary>Start park async — L: uses this device's ParkSelected and SlewAsync.</summary>
        public async Task<SlewResult> GoToParkAsync()
        {
            ApplyTracking(false);
            var ps = ParkSelected;
            if (ps == null)
                return SlewResult.Failed("No park position selected");
            if (double.IsNaN(ps.X) || double.IsNaN(ps.Y))
                return SlewResult.Failed("Invalid park coordinates");
            Settings.ParkAxes = [ps.X, ps.Y];
            Settings.ParkName = ps.Name;
            LogMount($"GoToParkAsync|{ps.Name}|{ps.X}|{ps.Y}");
            var target = new[] { ps.X, ps.Y };
            return await SlewAsync(target, SlewType.SlewPark, tracking: false);
        }

        /// <summary>Issue a pulse guide command — passes this instance to SkyServer.PulseGuide (J1).</summary>
        public void PulseGuide(GuideDirection direction, int duration, double altRate) =>
            SkyServer.PulseGuide(direction, duration, altRate, this); // J1: per-instance

        /// <summary>Synchronous Alt/Az slew — dispatches on this instance directly.</summary>
        public void SlewAltAz(double altitude, double azimuth) =>
            SlewSync([azimuth, altitude], SlewType.SlewAltAz);

        /// <summary>Async Alt/Az slew — dispatches on this instance directly.</summary>
        public Task<SlewResult> SlewAltAzAsync(double altitude, double azimuth) =>
            SlewAsync([azimuth, altitude], SlewType.SlewAltAz);

        /// <summary>Synchronous RA/Dec slew — dispatches on this instance directly.</summary>
        public void SlewRaDec(double rightAscension, double declination, bool tracking = false) =>
            SlewSync([rightAscension, declination], SlewType.SlewRaDec, tracking);

        /// <summary>Async RA/Dec slew — dispatches on this instance directly.</summary>
        public Task<SlewResult> SlewRaDecAsync(double rightAscension, double declination, bool tracking = false) =>
            SlewAsync([rightAscension, declination], SlewType.SlewRaDec, tracking);

        /// <summary>Enable tracking on a slew cycle — delegates to per-instance tracking.</summary>
        public void CycleOnTracking(bool silence) => ApplyTracking(true);

        /// <summary>Save current position as a named park position — instance version.</summary>
        public void SetParkAxis(string name)
        {
            if (string.IsNullOrEmpty(name)) { name = "Empty"; }
            var park = Axes.MountAxis2Mount(Settings, _appAxes.X, _appAxes.Y);
            if (park == null) { return; }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{name}|{park[0]}|{park[1]}|{_appAxes.X}|{_appAxes.Y}"
            });
            SetParkAxis(name, park[0], park[1]);
        }

        /// <summary>Sync to given Alt/Az position — instance version.</summary>
        public void SyncToAltAzm(double azimuth, double altitude)
        {
            if (!IsMountRunning) { return; }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{azimuth}|{altitude}"
            });
            var trackingstate = Tracking;
            if (trackingstate) { ApplyTracking(false); }
            _altAzSync = new Vector(altitude, azimuth);
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SkyServer.SimTasks(MountTaskName.StopAxes, this);
                    SkyServer.SimTasks(MountTaskName.SyncAltAz, this);
                    break;
                case MountType.SkyWatcher:
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    SkyServer.SkyTasks(MountTaskName.SyncAltAz, this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            WaitMountPositionUpdated();
            if (trackingstate)
            {
                var internalAltAz = Transforms.CoordTypeToInternal(azimuth, altitude);
                var raDec = Coordinate.AltAz2RaDec(internalAltAz.X, internalAltAz.Y, SiderealTime, Settings.Latitude);
                SkyPredictor.Set(raDec[0], raDec[1]);
                InstanceApplyTrackingDirect(true, TrackingMode.AltAz);
            }
        }

        /// <summary>Sync to current target RA/Dec — instance version.</summary>
        public void SyncToTargetRaDec()
        {
            if (!IsMountRunning) { return; }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $" {TargetRa}|{TargetDec}|{Tracking}"
            });
            var trackingstate = Tracking;
            if (trackingstate) { ApplyTracking(false); }
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SkyServer.SimTasks(MountTaskName.StopAxes, this);
                    SkyServer.SimTasks(MountTaskName.SyncTarget, this);
                    break;
                case MountType.SkyWatcher:
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    SkyServer.SkyTasks(MountTaskName.SyncTarget, this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            WaitMountPositionUpdated();
            if (trackingstate)
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    SkyPredictor.Set(TargetRa, TargetDec);
                    InstanceApplyTrackingDirect(true, TrackingMode.AltAz);
                }
                else
                {
                    ApplyTracking(true);
                }
            }
        }

        /// <summary>Block until mount position is updated or timeout — instance version.</summary>
        public void WaitMountPositionUpdated()
        {
            _mountPositionUpdatedEvent.Reset();
            UpdateSteps();
            if (!_mountPositionUpdatedEvent.Wait(5000))
                throw new TimeoutException();
        }

        /// <summary>Check if RA/Dec is within sync limits — instance version.</summary>
        public bool CheckRaDecSyncLimit(double ra, double dec)
        {
            if (!Settings.SyncLimitOn) { return true; }
            if (Settings.NoSyncPastMeridian) { return false; }
            var xy = Axes.RaDecToAxesXy([ra, dec], Settings);
            var target = Axes.AxesMountToApp(SkyServer.GetSyncedAxes(xy), Settings);
            var current = new[] { _appAxes.X, _appAxes.Y };
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > Settings.SyncLimit || Math.Abs(b) > Settings.SyncLimit);
            if (ret) return true;
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{Settings.SyncLimit}"
            });
            return false;
        }

        /// <summary>Check if Alt/Az is within sync limits — instance version.</summary>
        public bool CheckAltAzSyncLimit(double alt, double az)
        {
            if (!Settings.SyncLimitOn) { return true; }
            if (Settings.NoSyncPastMeridian) { return false; }
            var xy = Axes.AzAltToAxesXy([az, alt], Settings);
            var target = Axes.AxesMountToApp(SkyServer.GetSyncedAxes(xy), Settings);
            var current = new[] { _appAxes.X, _appAxes.Y };
            if (Settings.AlignmentMode == AlignmentMode.AltAz)
            {
                target[0] = az;
                target[1] = alt;
                current[0] = Range.Range360(_appAxes.X);
                current[1] = _appAxes.Y;
            }
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > Settings.SyncLimit || Math.Abs(b) > Settings.SyncLimit);
            if (ret) return true;
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{Settings.SyncLimit}"
            });
            return false;
        }

        /// <summary>Check if target is within reachable hardware limits — instance version.</summary>
        public bool IsTargetReachable(double[] target, SlewType slewType)
        {
            if (Settings.AlignmentMode == AlignmentMode.GermanPolar) return true;
            switch (slewType)
            {
                case SlewType.SlewRaDec:
                case SlewType.SlewAltAz:
                    var savedFlip = _flipOnNextGoto;
                    var result = IsTargetWithinLimits(MapSlewTargetToAxes(target, slewType));
                    _flipOnNextGoto = savedFlip;
                    return result;
                default:
                    return false;
            }
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
        /// <returns>Target coordinates mapped to appropriate axes</returns>        
        public double[] MapSlewTargetToAxes(double[] target, SlewType slewType)
        {
            // Convert target to axes based on slew type
            switch (slewType)
            {
                case SlewType.SlewRaDec:
                    // convert target to axis for Ra / Dec slew
                    target = Axes.RaDecToAxesXy(target, Settings, selectAlternatePosition: GetAlternatePosition);
                    // Convert to synced axes
                    // target = SkyServer.GetSyncedAxes(target);
                    break;
                case SlewType.SlewAltAz:
                    // convert target to axis for Az / Alt slew
                    target = Axes.AzAltToAxesXy(target, Settings, selectAlternatePosition: GetAlternatePosition);
                    break;
                case SlewType.SlewHome:
                    break;
                case SlewType.SlewPark:
                    // convert to mount coordinates for park
                    target = Axes.AxesAppToMount(target, Settings);
                    break;
                case SlewType.SlewMoveAxis:
                    target = Axes.AxesAppToMount(target, Settings);
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
            if (!IsMountRunning) { return actualDegrees; }

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(SimQueue!.NewId, SimQueue);
                    actualDegrees = (double[])SimQueue.GetCommandResult(simPositions).Result;
                    break;

                case MountType.SkyWatcher:
                    var skyPositions = new SkyGetPositionsInDegrees(SkyQueue!.NewId, SkyQueue);
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
            switch (Settings.Mount)
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
            if (!IsMountRunning) { return steps; }

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(SimQueue!.NewId, SimQueue);
                    steps = (double[])SimQueue.GetCommandResult(simPositions).Result;
                    steps[0] *= _factorStep[0];
                    steps[1] *= _factorStep[1];
                    break;

                case MountType.SkyWatcher:
                    var skySteps = new SkyGetSteps(SkyQueue!.NewId, SkyQueue);
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
            if (!IsMountRunning) { return null; }

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxisSteps(SimQueue!.NewId, SimQueue);
                    var a = (int[])SimQueue.GetCommandResult(simPositions).Result;

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
                            var b = new SkyGetAxisPositionCounter(SkyQueue!.NewId, SkyQueue, Axis.Axis1);
                            return Convert.ToDouble(SkyQueue.GetCommandResult(b).Result);
                        case 1:
                            var c = new SkyGetAxisPositionCounter(SkyQueue!.NewId, SkyQueue, Axis.Axis2);
                            return Convert.ToDouble(SkyQueue.GetCommandResult(c).Result);
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
                if (IsMountRunning || (_lastUpdateStepsTime.AddMilliseconds(100) < HiResDateTime.UtcNow))
                {
                    switch (Settings.Mount)
                    {
                        case MountType.Simulator:
                            _ = new CmdAxesSteps(SimQueue!.NewId, SimQueue);
                            break;
                        case MountType.SkyWatcher:
                            _ = new SkyUpdateSteps(SkyQueue!.NewId, SkyQueue);
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
            // N7: Pass computed LST directly — avoids SkyServer.SiderealTime which reads _defaultInstance
            //     (device-00), so device-01 would always see LST=0.0 if device-00 is not running.
            var lst = SkyServer.GetLocalSiderealTime(Settings.Longitude);

            // Implement PEC
            PecCheck();

            // Convert raw steps to degrees
            var rawPositions = new[]
            {
                ConvertStepsToDegrees(steps[0], 0),
                ConvertStepsToDegrees(steps[1], 1)
            };

            // J5: Per-instance limit status
            {
                const double oneArcSec = 1.0 / 3600;
                _limitStatus.AtLowerLimitAxisX = rawPositions[0] <= -Settings.AxisLimitX - oneArcSec;
                _limitStatus.AtUpperLimitAxisX = rawPositions[0] >= Settings.AxisLimitX + oneArcSec;
                var axisUpperLimitY = Settings.AxisUpperLimitY;
                var axisLowerLimitY = Settings.AxisLowerLimitY;
                if (Settings.AlignmentMode == AlignmentMode.Polar && Settings.PolarMode == PolarMode.Left)
                {
                    axisLowerLimitY = 180 - Settings.AxisUpperLimitY;
                    axisUpperLimitY = 180 - Settings.AxisLowerLimitY;
                }
                _limitStatus.AtLowerLimitAxisY = rawPositions[1] <= axisLowerLimitY - oneArcSec;
                _limitStatus.AtUpperLimitAxisY = rawPositions[1] >= axisUpperLimitY + oneArcSec;
            }

            // UI diagnostics in degrees
            _actualAxisX = rawPositions[0];
            _actualAxisY = rawPositions[1];

            // Convert physical positions to local app axes
            var axes = Axes.AxesMountToApp(rawPositions, Settings);

            // UI diagnostics for local app axes
            _appAxes.X = axes[0];
            _appAxes.Y = axes[1];

            // Calculate mount Alt/Az
            var altAz = Axes.AxesXyToAzAlt(axes, Settings);
            _altAzm.X = altAz[0];
            _altAzm.Y = altAz[1];

            // Calculate topocentric RA/Dec
            var raDec = Axes.AxesXyToRaDec(axes, Settings, lst);
            _raDec.X = raDec[0];
            _raDec.Y = raDec[1];

            // Calculate EquatorialSystem RA/Dec for UI
            var xy = Transforms.InternalToCoordType(raDec[0], raDec[1], settings: Settings);
            RightAscensionXForm = xy.X;
            DeclinationXForm = xy.Y;
        }

        /// <summary>
        /// Called by queue callbacks when the hardware delivers new step counts.
        /// Runs the full position pipeline, signals the per-instance position event,
        /// and notifies static observers (Blazor UI) for backward compatibility.
        /// </summary>
        internal void ReceiveSteps(double[] steps)
        {
            _steps = steps;
            SetSteps(steps);
            _mountPositionUpdatedEvent.Set();
        }

        /// <summary>
        /// Get home axes adjusted for angle offset
        /// Migrated from SkyServer.GetHomeAxes()
        /// </summary>
        /// <param name="xAxis">X axis position</param>
        /// <param name="yAxis">Y axis position</param>
        /// <returns>Home axes vector adjusted for alignment mode and hemisphere</returns>
        private Vector GetHomeAxes(double xAxis, double yAxis)
        {
            var home = new[] { xAxis, yAxis };
            if (Settings.AlignmentMode != AlignmentMode.Polar)
            {
                home = Axes.AxesAppToMount([xAxis, yAxis], Settings);
            }
            else
            {
                var angleOffset = Settings.Latitude < 0 ? 180.0 : 0.0; // J2: use per-instance settings
                home[0] -= angleOffset;
                home = Axes.AzAltToAxesXy(home, Settings);
            }
            return new Vector(home[0], home[1]);
        }

        #endregion

        #region Core Operations (Migrated from static)

        /// <summary>
        /// Load default settings and slew rates
        /// Migrated from SkyServer.Defaults()
        /// </summary>
        private void Defaults()
        {
            _slewSettleTime = 0;

            // Initialize FactorStep array (already initialized in constructor, but keep for compatibility)
            // _factorStep is already initialized as new double[2]

            // home axes
            _homeAxes = GetHomeAxes(Settings.HomeAxisX, Settings.HomeAxisY);

            // Initialize ParkSelected from ParkName setting
            if (!string.IsNullOrEmpty(Settings.ParkName))
            {
                var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkName);
                if (found != null)
                {
                    _parkSelected = found;
                    // Also ensure ParkAxes is populated for backward compatibility
                    Settings.ParkAxes = [found.X, found.Y];
                }
            }

            // Set slew rates for all speed levels
            // SetSlewRates expects degrees/second and stores values in degrees
            // (hardware layer converts to radians when needed)
            this.SetSlewRates(Settings.MaxSlewRate);

            // set the guiderates
            _guideRate = new Vector(Settings.GuideRateOffsetY, Settings.GuideRateOffsetX);
            this.SetGuideRates();
        }

        /// <summary>
        /// Reset mount to home position
        /// Migrated from SkyServer.MountReset()
        /// </summary>
        private void MountReset()
        {
            // Set home positions using current settings (already loaded)
            _homeAxes = GetHomeAxes(Settings.HomeAxisX, Settings.HomeAxisY);

            // Set axis positions
            _appAxes = new Vector(_homeAxes.X, _homeAxes.Y);
        }

        // Expose internal state for static facade backward compatibility
        internal Vector HomeAxes => _homeAxes;
        internal Vector AppAxes => _appAxes;

        #region Serial connection (migrated from SkySystem)

        /// <summary>
        /// Adds or removes the given client ID from the connected-client set.
        /// On first connect, starts the mount hardware in a background task (ASCOM async pattern).
        /// Returns within &lt;1 second per ASCOM specification. Connection completes when Connecting becomes false.
        /// On last disconnect, the hardware continues running until explicitly stopped.
        /// </summary>
        public void SetConnected(long id, bool value)
        {
            if (value)
            {
                if (_connectStates.IsEmpty) { Connecting = true; }
                var notAlreadyPresent = _connectStates.TryAdd(id, true);

                if (!_connectStates.IsEmpty && !IsMountRunning)
                {
                    _loopCounter = 0;

                    // Phase 1: Start mount initialization in background to comply with ASCOM <1 second return requirement
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            MountStart();

                            // Wait for initial stability (up to 5 seconds)
                            var connectionTimer = Stopwatch.StartNew();
                            while (_loopCounter < 2 && connectionTimer.ElapsedMilliseconds < 5000)
                                Thread.Sleep(100);

                            // Mark connection as complete
                            Connecting = false;

                            var completionItem = new MonitorEntry
                            {
                                Datetime = HiResDateTime.UtcNow,
                                Device = MonitorDevice.Server,
                                Category = MonitorCategory.Server,
                                Type = MonitorType.Information,
                                Method = nameof(SetConnected),
                                Thread = Environment.CurrentManagedThreadId,
                                Message = $"Connection complete|Mount:{Id}|LoopCount:{_loopCounter}"
                            };
                            MonitorLog.LogToMonitor(completionItem);
                        }
                        catch (Exception ex)
                        {
                            // Risk 2: Store exception for later retrieval
                            _mountError = ex;
                            Connecting = false;

                            var errorItem = new MonitorEntry
                            {
                                Datetime = HiResDateTime.UtcNow,
                                Device = MonitorDevice.Server,
                                Category = MonitorCategory.Server,
                                Type = MonitorType.Error,
                                Method = nameof(SetConnected),
                                Thread = Environment.CurrentManagedThreadId,
                                Message = $"Connection failed|Mount:{Id}|Error:{ex.Message}"
                            };
                            MonitorLog.LogToMonitor(errorItem);
                        }
                    });

                    // Return immediately - connection proceeds in background
                    var startItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Add|{id}|{notAlreadyPresent}|StartingAsync|Connecting:{Connecting}"
                    };
                    MonitorLog.LogToMonitor(startItem);
                    return; // Early return - Connecting remains true until background task completes
                }

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Add|{id}|{notAlreadyPresent}" };
                MonitorLog.LogToMonitor(monitorItem);
            }
            else
            {
                if (_connectStates.Count == 1) { Connecting = true; }
                var successfullyRemoved = _connectStates.TryRemove(id, out _);
                if (_connectStates.IsEmpty)
                {
                    MountStop();
                }
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Remove|{id}|{successfullyRemoved}" };
                MonitorLog.LogToMonitor(monitorItem);
            }
            Connecting = false;
        }

        /// <summary>
        /// Opens the serial or UDP port defined in settings. Equivalent to SkySystem.ConnectSerial = true.
        /// </summary>
        private void OpenSerial()
        {
            _serialError = null;
            try
            {
                _serial?.Dispose();
                _serial = null;
                _connectType = ConnectType.None;

                var readTimeout = TimeSpan.FromMilliseconds(Settings.ReadTimeout);
                if (Settings.Port.Contains("COM"))
                {
                    var options = SerialOptions.DiscardNull
                        | (Settings.DtrEnable ? SerialOptions.DtrEnable : SerialOptions.None)
                        | (Settings.RtsEnable ? SerialOptions.RtsEnable : SerialOptions.None);

                    _serial = new GsSerialPort(
                        Settings.Port,
                        (int)Settings.BaudRate,
                        readTimeout,
                        Settings.HandShake,
                        Parity.None,
                        StopBits.One,
                        Settings.DataBits,
                        options);
                    _connectType = ConnectType.Com;
                }
                else
                {
                    var endpoint = CreateIpEndPoint(Settings.Port);
                    _serial = new SerialOverUdpPort(endpoint, readTimeout);
                    _connectType = ConnectType.Wifi;
                }
                _serial?.Open();
            }
            catch (Exception ex)
            {
                _serialError = ex;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{ex.Message}|{ex.InnerException?.Message}" };
                MonitorLog.LogToMonitor(monitorItem);
                _serial = null;
                _connectType = ConnectType.None;
            }
        }

        /// <summary>
        /// Closes and disposes the serial port. Equivalent to SkySystem.ConnectSerial = false.
        /// </summary>
        private void CloseSerial()
        {
            _serial?.Dispose();
            _serial = null;
            _connectType = ConnectType.None;
        }

        /// <summary>
        /// Parses a "host:port" string into an IPEndPoint. Handles IPv4 and IPv6.
        /// </summary>
        private static IPEndPoint CreateIpEndPoint(string endPoint)
        {
            var ep = endPoint.Split(':');
            if (ep.Length < 2) { throw new FormatException("Invalid endpoint format"); }
            IPAddress ip;
            if (ep.Length > 2)
            {
                if (!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip))
                { throw new FormatException("Invalid ip-address"); }
            }
            else
            {
                if (!IPAddress.TryParse(ep[0], out ip))
                { throw new FormatException("Invalid ip-address"); }
            }
            return !int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out var port)
                ? throw new FormatException("Invalid port")
                : new IPEndPoint(ip, port);
        }

        #endregion

        /// <summary>
        /// Start connection, queues, and events
        /// Migrated from SkyServer.MountStart()
        /// </summary>
        private void MountStart()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Settings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // setup server defaults, stop auto-discovery, connect serial port, start queues
            Defaults();
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    Alpaca.Mount.Simulator.Settings.AutoHomeAxisX = (int)Settings.AutoHomeAxisX;
                    Alpaca.Mount.Simulator.Settings.AutoHomeAxisY = (int)Settings.AutoHomeAxisY;
                    var mqImpl = new GreenSwamp.Alpaca.Mount.Simulator.MountQueueImplementation();
                    mqImpl.SetupCallbacks(
                        steps => ReceiveSteps(steps),
                        v => { _isPulseGuidingRa = v; },
                        v => { _isPulseGuidingDec = v; });
                    // Start the instance-owned simulator queue directly (no static facade)
                    mqImpl.Start();
                    SimQueue = mqImpl;
                    if (!mqImpl.IsRunning)
                    {
                        throw new Exception("Failed to start simulator queue");
                    }

                    break;
                case MountType.SkyWatcher:
                    // open serial port
                    CloseSerial();
                    OpenSerial();
                    if (_serial?.IsOpen != true)
                    {
                        throw new SkyServerException(ErrorCode.ErrSerialFailed,
                            $"Connection Failed: {_serialError}");
                    }
                    // Start up, pass custom mount gearing if needed
                    var custom360Steps = new[] { 0, 0 };
                    var customWormSteps = new[] { 0.0, 0.0 };
                    if (Settings.CustomGearing)
                    {
                        custom360Steps = [Settings.CustomRa360Steps, Settings.CustomDec360Steps];
                        customWormSteps = [(double)Settings.CustomRa360Steps / Settings.CustomRaWormTeeth, (double)Settings.CustomDec360Steps / Settings.CustomDecWormTeeth
                        ];
                    }

                    // Create queue for SkyWatcher.
                    var sqImpl = new GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueueImplementation();
                    sqImpl.SetupCallbacks(
                        steps => ReceiveSteps(steps),
                        v => { _isPulseGuidingRa = v; },
                        v => { _isPulseGuidingDec = v; });
                    sqImpl.Start(_serial, custom360Steps, customWormSteps, this.OnLowVoltageEvent);
                    SkyQueue = sqImpl;
                    if (!sqImpl.IsRunning)
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
                // Start the tracking command processor (queue-based AltAz serialisation)
                _trackingProcessor = new TrackingCommandProcessor(this);
                _trackingProcessor.Start(CancellationToken.None);

                // start with a stop
                SkyServer.AxesStopValidate(this);

                // Event to get mount positions and update UI
                // Ensure DisplayInterval is valid for MediaTimer (must be > 0)
                var displayInterval = Settings.DisplayInterval > 0 ? Settings.DisplayInterval : 200;
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Settings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // Stop all asynchronous operations
            TrackingMode = TrackingMode.Off;
            Tracking = false;
            _ctsGoTo?.Cancel();
            _ctsPulseGuideRa?.Cancel();
            _ctsPulseGuideDec?.Cancel();
            _ctsHcPulseGuide?.Cancel();
            // Complete the tracking command processor before stopping the timer
            _trackingProcessor?.StopAsync().GetAwaiter().GetResult();
            _trackingProcessor = null;
            // N6: Stop timers BEFORE AxesStopValidate
            //     after the stop commands, which would leave the motor running after Disconnect.
            if (_altAzTrackingTimer != null) { _altAzTrackingTimer.Tick -= AltAzTrackingTimerTick; } // J4 / N6: moved before AxesStopValidate
            _altAzTrackingTimer?.Stop();
            _altAzTrackingTimer?.Dispose();
            _altAzTrackingTimer = null;  // N6: null the field — was missing, causing IsRunning check on disposed timer after reconnect
            if (_mediaTimer != null) { _mediaTimer.Tick -= OnUpdateServerEvent; }
            _mediaTimer?.Stop();
            _mediaTimer?.Dispose();
            SkyServer.AxesStopValidate(this); // N6: now safe — no timer can race and re-queue motion

            if (SimQueue?.IsRunning == true) { SimQueue.Stop(); }

            if (SkyQueue?.IsRunning == true)
            {
                SkyQueue.Stop();
                CloseSerial();
            }

            SimQueue = null;
            SkyQueue = null;

            // ToDo - fix cleanup
            // Dispose SlewController
            _slewController?.Dispose();
            _slewController = null;

        }

        /// <summary>
        /// Per-tick update loop.
        /// Replaces static UpdateServerEvent body — per-instance lock prevents cross-device re-entrancy.
        /// </summary>
        private void OnUpdateServerEvent(object sender, EventArgs e)
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
                SiderealTime = SkyServer.GetLocalSiderealTime(Settings.Longitude);
                this.UpdateSteps();
                Lha = Coordinate.Ra2Ha12(RightAscensionXForm, SiderealTime);
                CheckSlewState();    // Updates this device's _isSlewing
                CheckAxisLimits();   // Checks this device's axis limits
                CheckPecTraining();
                _isHome = this.AtHome;
                switch (Settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                    case AlignmentMode.Polar:
                    case AlignmentMode.GermanPolar:
                        IsSideOfPier = this.SideOfPier;
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                SkyServer.SkyErrorHandler(ex, this);
            }
            finally
            {
                if (hasLock) { Monitor.Exit(_timerLock); }
            }
        }

        /// <summary>
        /// Wires per-device settings change notifications.
        /// </summary>
        public void InitializeSettings()
        {
            Settings.PropertyChanged -= OnPropertyChangedSkySettings; // Prevent double-wiring
            Settings.PropertyChanged += OnPropertyChangedSkySettings;
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Settings listener wired | Mount:{Settings.Mount} | Port:{Settings.Port}"
            });
        }

        private void OnPropertyChangedSkySettings(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AtPark":
                    if (AtPark != Settings.AtPark) AtPark = Settings.AtPark;
                    break;
                case "AlignmentMode":
                    SetTracking(false);
                    SkyPredictor.Reset();
                    break;
            }
        }

        /// <summary>
        /// Low voltage event handler
        /// </summary>
        private void OnLowVoltageEvent(object sender, EventArgs e)
        {
            _lowVoltageEventState = true;
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Error,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = "Mount detected low voltage: check power supply and wiring"
            });
        }

        /// <summary>
        /// Slew-state check
        /// Updates this device's _isSlewing from its own _slewState and _rateMoveAxes.
        /// </summary>
        private void CheckSlewState()
        {
            var slewing = false;
            switch (_slewState)
            {
                case SlewType.SlewNone:     slewing = false; break;
                case SlewType.SlewSettle:   slewing = true;  break;
                case SlewType.SlewMoveAxis: slewing = true;  break;
                case SlewType.SlewRaDec:    slewing = true;  break;
                case SlewType.SlewAltAz:    slewing = true;  break;
                case SlewType.SlewPark:     slewing = true;  break;
                case SlewType.SlewHome:     slewing = true;  break;
                case SlewType.SlewHandpad:  slewing = true;  break;
                case SlewType.SlewComplete: _slewState = SlewType.SlewNone; break;
                default:                    _slewState = SlewType.SlewNone; break;
            }
            if ((Math.Abs(_rateMoveAxes.X) + Math.Abs(_rateMoveAxes.Y)) > 0) { slewing = true; }
            _isSlewing = slewing;
        }

        /// <summary>
        /// Per Axis-limit check — replaces static SkyServer.CheckAxisLimits().
        /// Reads per-instance _limitStatus instead of static SkyServer.LimitStatus.
        /// J7: calls StopAxes/GoToPark to halt the correct physical device.
        /// </summary>
        private void CheckAxisLimits()
        {
            var meridianLimit = false;
            var horizonLimit = false;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                Category = MonitorCategory.Server, Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId, Message = string.Empty
            };
            var totLimit = Settings.HourAngleLimit + Settings.AxisTrackingLimit;
            switch (Settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    meridianLimit = _limitStatus.AtLowerLimitAxisY || _limitStatus.AtUpperLimitAxisY; // J5: per-instance
                    break;
                case AlignmentMode.Polar:
                    break;
                case AlignmentMode.GermanPolar:
                    bool sh = Settings.Latitude < 0;
                    if (sh)
                    {
                        if (_appAxes.X >= Settings.HourAngleLimit || _appAxes.X <= -Settings.HourAngleLimit - 180) meridianLimit = true;
                    }
                    else
                    {
                        if (_appAxes.X >= Settings.HourAngleLimit + 180 || _appAxes.X <= -Settings.HourAngleLimit) meridianLimit = true;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (Settings.HzLimitPark || Settings.HzLimitTracking)
            {
                switch (Settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        if ((_altAzm.Y <= Settings.AxisHzTrackingLimit || _altAzm.Y <= Settings.AxisLowerLimitY || _altAzm.Y >= Settings.AxisUpperLimitY) && TrackingMode != TrackingMode.Off)
                        { horizonLimit = true; }
                        break;
                    case AlignmentMode.Polar:
                        break;
                    case AlignmentMode.GermanPolar:
                        if (this.SideOfPier == PointingState.Normal && _altAzm.Y <= Settings.AxisHzTrackingLimit && TrackingMode != TrackingMode.Off)
                        { horizonLimit = true; }
                        break;
                }
            }
            if (meridianLimit)
            {
                monitorItem.Message = $"Meridian Limit Alarm: Park: {Settings.LimitPark} | Position: {Settings.ParkLimitName} | Stop Tracking: {Settings.LimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);
                if (TrackingMode != TrackingMode.Off && Settings.LimitTracking)
                { TrackingMode = TrackingMode.Off; Tracking = false; }
                if (Settings.LimitPark && _slewState != SlewType.SlewPark)
                {
                    var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkLimitName);
                    if (found == null) InstanceStopAxes(); else { _parkSelected = found; StartGoToParkAsync(); } // J7: per-instance
                }
            }
            if (horizonLimit)
            {
                monitorItem.Message = $"Horizon Limit Alarm: Park: {Settings.HzLimitPark} | Position:{Settings.ParkHzLimitName} | Stop Tracking:{Settings.HzLimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);
                if (TrackingMode != TrackingMode.Off && Settings.HzLimitTracking)
                { TrackingMode = TrackingMode.Off; Tracking = false; }
                if (Settings.HzLimitPark && _slewState != SlewType.SlewPark)
                {
                    var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkHzLimitName);
                    if (found == null) InstanceStopAxes(); else { _parkSelected = found; StartGoToParkAsync(); } // J7: per-instance
                }
            }
        }

        /// <summary>
        /// J7: Per-instance axis stop — halts this device's mount axes without affecting other devices.
        /// Cancels any active GoTo, clears rate moves, then issues hardware stop commands.
        /// </summary>
        internal void InstanceStopAxes()
        {
            _ctsGoTo?.Cancel();
            _moveAxisActive = false;
            _rateMoveAxes = new Vector(0, 0);
            _rateRaDec = new Vector(0, 0);
            if (!SkyServer.AxesStopValidate(this))
            {
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        SkyServer.SimTasks(MountTaskName.StopAxes, this);
                        break;
                    case MountType.SkyWatcher:
                        SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                        break;
                }
            }
            _slewState = SlewType.SlewNone;
            Tracking = false;
            TrackingMode = TrackingMode.Off;
        }

        /// <summary>
        /// Start an asynchronous park (slew-to-park) operation using the instance's currently selected park position.
        /// If no valid park position is set the method returns immediately. Disables tracking, writes the selected
        /// park coordinates and name into the instance settings, and starts the park slew by invoking SlewAsync(double[], SlewType, bool) 
        /// with SlewType.SlewPark in a fire-and-forget manner (the returned Task is not awaited).
        /// </summary>
        private void StartGoToParkAsync()
        {
            var ps = _parkSelected;
            if (ps == null || double.IsNaN(ps.X) || double.IsNaN(ps.Y)) return;
            Tracking = false;
            TrackingMode = TrackingMode.Off;
            Settings.ParkAxes = [ps.X, ps.Y];
            Settings.ParkName = ps.Name;
            _ = SlewAsync([ps.X, ps.Y], SlewType.SlewPark, tracking: false);
        }
        /// <summary>
        /// K: Per-instance equivalent of SkyServer.SetTrackingMode.
        /// Sets _trackingMode from this device alignment and hemisphere settings.
        /// </summary>
        private void InstanceSetTrackingMode()
        {
            switch (Settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    TrackingMode = TrackingMode.AltAz;
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    TrackingMode = Settings.Latitude < 0 ? TrackingMode.EqS : TrackingMode.EqN;
                    break;
            }
        }

        /// <summary>
        /// K: Per-instance equivalent of SkyServer.Tracking setter.
        /// Resets SkyPredictor, sets tracking mode, and applies hardware.
        /// Early-exits if already in requested state (mirrors SkyServer.Tracking early-exit).
        /// </summary>
        public void ApplyTracking(bool tracking)
        {
            if (tracking == Tracking) return;
            Tracking = tracking;
            if (tracking)
            {
                InstanceSetTrackingMode();
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    _altAzTrackingMode = AltAzTrackingType.Predictor;
                    if (!SkyPredictor.RaDecSet)
                        SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, 0, 0); // N5: first-time seed
                    else
                        SkyPredictor.ReferenceTime = DateTime.Now; // N5: preserve existing target — don't reset
                }
            }
            else
            {
                SkyPredictor.Reset(); // N5: reset on tracking OFF only — never on re-enable
                _isPulseGuidingRa = false;
                _isPulseGuidingDec = false;
                TrackingMode = TrackingMode.Off;
            }
            this.SetTracking();
        }

        /// <summary>
        /// K: Per-instance equivalent of SkyServer.SetTrackingDirect.
        /// Sets tracking state and mode without resetting SkyPredictor.
        /// </summary>
        internal void InstanceApplyTrackingDirect(bool tracking, TrackingMode mode)
        {
            Tracking = tracking;
            TrackingMode = mode;
            this.SetTracking();
        }

        /// <summary>
        /// K: Per-instance park completion. Replaces SkyServer.CompletePark().
        /// Sets AtPark, disables tracking, and resets predictor for this device.
        /// </summary>
        internal void InstanceCompletePark()
        {
            AtPark = true;
            Tracking = false;
            TrackingMode = TrackingMode.Off;
            SkyPredictor.Reset();
            this.SetTracking();
        }

        /// <summary>
        /// L: Per-instance cancel all async operations.
        /// Cancels this device's GoTo, pulse guide, and HC pulse guide tasks.
        /// A short yield gives background Task.Run lambdas time to observe cancellation
        /// before StopAxes is issued. The previous 2 s spin-wait could never exit early
        /// because the CTS fields are only nulled when a new pulse starts, not on completion.
        /// </summary>
        private void CancelAllAsync()
        {
            var anyActive = _ctsGoTo != null || _ctsPulseGuideDec != null
                            || _ctsPulseGuideRa != null || _ctsHcPulseGuide != null;
            if (!anyActive) return;

            _ctsGoTo?.Cancel();
            _ctsPulseGuideDec?.Cancel();
            _ctsPulseGuideRa?.Cancel();
            _ctsHcPulseGuide?.Cancel();

            // Brief yield — gives fire-and-forget Task.Run lambdas time to observe
            // cancellation before the caller issues StopAxes hardware commands.
            Thread.Sleep(50);
        }

        /// <summary>
        /// L: Per-instance set/reset tracking and slewing state while MoveAxis is active.
        /// Mirrors SkyServer.SetRateMoveSlewState using this device's own fields.
        /// </summary>
        private void SetRateMoveSlewState()
        {
            bool primaryActive = _rateMoveAxes.X != 0.0;
            bool secondaryActive = _rateMoveAxes.Y != 0.0;
            if (primaryActive || secondaryActive)
            {
                _moveAxisActive = true;
                _isSlewing = true;
                _slewState = SlewType.SlewMoveAxis;
            }
            if (!primaryActive && !secondaryActive)
            {
                _moveAxisActive = false;
                _isSlewing = false;
                _slewState = SlewType.SlewNone;
                if (Tracking) SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
        }

        /// <summary>
        /// L: Per-instance Ra/Dec rate action — updates this device's predictor and applies hardware tracking rate.
        /// Mirrors SkyServer.ActionRateRaDec using this device's own fields.
        /// </summary>
        private void ActionRateRaDec()
        {
            if (Tracking)
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    var raDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                    SkyPredictor.Set(raDec[0], raDec[1], _rateRaDec.X, _rateRaDec.Y);
                }
                this.SetTracking();
            }
            else
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, _rateRaDec.X, _rateRaDec.Y);
                }
            }
        }

        #endregion

        #region AltAz Tracking Timer (J4 — per-instance)

        /// <summary>
        /// J4: Per-instance AltAz tracking timer tick handler.
        /// Posts a <see cref="TimerTickCommand"/> to the processor and returns
        /// immediately (D5). Tick de-duplication is handled by the processor.
        /// </summary>
        private void AltAzTrackingTimerTick(object sender, EventArgs e)
        {
            _trackingProcessor?.PostTick();
        }

        /// <summary>
        /// Start the per-instance AltAz tracking timer. Called exclusively from
        /// the consumer task thread via <see cref="TrackingCommandProcessor"/>.
        /// </summary>
        internal void StartAltAzTrackingTimerInternal()
        {
            StopAltAzTrackingTimerInternal();
            _altAzTrackingTimer = new MediaTimer { Period = Settings.AltAzTrackingUpdateInterval };
            _altAzTrackingTimer.Tick += AltAzTrackingTimerTick;
            _altAzTrackingTimer.Start();
        }

        /// <summary>
        /// Stop and dispose the per-instance AltAz tracking timer. Called
        /// exclusively from the consumer task thread via
        /// <see cref="TrackingCommandProcessor"/>.
        /// </summary>
        internal void StopAltAzTrackingTimerInternal()
        {
            if (_altAzTrackingTimer != null)
            {
                _altAzTrackingTimer.Tick -= AltAzTrackingTimerTick;
                if (_altAzTrackingTimer.IsRunning) _altAzTrackingTimer.Stop();
                _altAzTrackingTimer.Dispose();
                _altAzTrackingTimer = null;
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
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|from|{_actualAxisX}|{_actualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            token.ThrowIfCancellationRequested();
            var simTarget = MapSlewTargetToAxes(target, slewType);
            const int timer = 120;
            var stopwatch = Stopwatch.StartNew();

            SkyServer.SimTasks(MountTaskName.StopAxes, this);

            #region First Slew
            token.ThrowIfCancellationRequested();
            _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, simTarget[0]);
            _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis2, simTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusX = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                var axis1Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(statusX).Result;
                var axis1Stopped = axis1Status.Stopped;

                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var statusY = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                var axis2Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(statusY).Result;
                var axis2Stopped = axis2Status.Stopped;

                if (!axis1Stopped || !axis2Stopped) continue;
                if (_slewSettleTime > 0)
                    Tasks.DelayHandler(TimeSpan.FromSeconds(_slewSettleTime).Milliseconds);
                break;
            }
            stopwatch.Stop();

            SkyServer.AxesStopValidate(this);
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|GoToSeconds|{stopwatch.Elapsed.TotalSeconds}|Target|{simTarget[0]}|{simTarget[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SimPrecisionGoto(target, slewType, token);
            #endregion

            SkyServer.SimTasks(MountTaskName.StopAxes, this);
            return success;
        }

        /// <summary>
        /// Simulator precision GOTO operation
        /// </summary>
        private int SimPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|from|({_actualAxisX},{_actualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            // var gotoPrecision = SkySettings.GotoPrecision;
            var maxTries = 0;
            double[] deltaDegree = [ 0.0, 0.0 ];
            double[] gotoPrecision = [ ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) ];
            const double milliSeconds = 0.001;
            var deltaTime = 75 * milliSeconds; // 75mS for simulator slew

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var loopTimer = Stopwatch.StartNew();

                if (maxTries > 5) { break; }
                maxTries++;

                if (Settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                    // get predicted RA and Dec at update time
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    // convert to internal Ra and Dec
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1], settings: Settings);
                    target = [internalRaDec.X, internalRaDec.Y];
                }

                var simTarget = MapSlewTargetToAxes(target, slewType);
                var rawPositions = GetRawDegrees();

                if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                { break; }

                deltaDegree[0] = Range.Range180(simTarget[0] - rawPositions[0]);
                deltaDegree[1] = Range.Range180(simTarget[1] - rawPositions[1]);

                var axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0];
                var axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1];
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested();
                if (!axis1AtTarget)
                    _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, simTarget[0] + 0.125 * deltaDegree[0]);
                token.ThrowIfCancellationRequested();
                if (!axis2AtTarget)
                    _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis2, simTarget[1] + 0.05 * deltaDegree[1]);

                var axis1Stopped = false;
                var axis2Stopped = false;

                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested();

                    if (!axis1Stopped)
                    {
                        var status1 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                        var axis1Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(status1).Result;
                        axis1Stopped = axis1Status.Stopped;
                    }

                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Stopped)
                    {
                        var status2 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                        var axis2Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(status2).Result;
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
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"Mount:{_instanceName}|Delta|({deltaDegree[0]},{deltaDegree[1]})|Seconds|{loopTimer.Elapsed.TotalSeconds}"
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

                    if (Settings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        var now = HiResDateTime.UtcNow;
                        var predictorRaDec = SkyPredictor.GetRaDecAtTime(now.AddMilliseconds(deltaTime));
                        var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1], settings: Settings);
                        simTargetAtTime = MapSlewTargetToAxes([internalRaDec.X, internalRaDec.Y], SlewType.SlewRaDec);
                        predictorRaDec = SkyPredictor.GetRaDecAtTime(now);
                        internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1], settings: Settings);
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
                        _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, simTargetAtTime[0]);
                    }
                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis2, simTargetAtTime[1]);
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
                                var status1 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                                var axis1Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(status1).Result;
                                axis1Stopped = axis1Status.Stopped;
                        }

                        Thread.Sleep(100);

                        if (!axis2Stopped)
                        {
                            var status2 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                            var axis2Status = (Alpaca.Mount.Simulator.AxisStatus)SimQueue.GetCommandResult(status2).Result;
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
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|from|{_actualAxisX}|{_actualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}|slewing|{slewType}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            token.ThrowIfCancellationRequested();

            var skyTarget = MapSlewTargetToAxes(target, slewType);
            const int timer = 240;
            var stopwatch = Stopwatch.StartNew();

            SkyServer.SkyTasks(MountTaskName.StopAxes, this);

            #region First Slew
            token.ThrowIfCancellationRequested();
            _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0]);
            _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
            var axis1Stopped = false;
            var axis2Stopped = false;
            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                if (!axis1Stopped)
                {
                    token.WaitHandle.WaitOne(250);
                    token.ThrowIfCancellationRequested();

                    var statusX = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                    var x = SkyQueue.GetCommandResult(statusX);
                    axis1Stopped = Convert.ToBoolean(x.Result);
                }

                if (!axis2Stopped)
                {
                    token.WaitHandle.WaitOne(250);
                    token.ThrowIfCancellationRequested();

                    var statusY = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                    var y = SkyQueue.GetCommandResult(statusY);
                    axis2Stopped = Convert.ToBoolean(y.Result);
                }

                if (!axis1Stopped || !axis2Stopped) { continue; }

                if (_slewSettleTime > 0)
                    Tasks.DelayHandler(TimeSpan.FromSeconds(_slewSettleTime).Milliseconds);
                break;
            }
            stopwatch.Stop();

            SkyServer.AxesStopValidate(this);
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|Seconds|{stopwatch.Elapsed.TotalSeconds}|Target|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SkyPrecisionGoto(target, slewType, token);
            #endregion

            SkyServer.SkyTasks(MountTaskName.StopAxes, this);
            return success;
        }

        /// <summary>
        /// SkyWatcher precision GOTO operation
        /// </summary>
        private int SkyPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_instanceName}|from|({_actualAxisX},{_actualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            var maxTries = 0;
            double[] deltaDegree = [0.0, 0.0];
            var axis1AtTarget = false;
            var axis2AtTarget = false;
            double[] gotoPrecision = [Settings.GotoPrecision, Settings.GotoPrecision];
            long deltaTime = 800;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var loopTimer = Stopwatch.StartNew();

                // Event-based position update waiting (per-instance event — Step 6)
                _mountPositionUpdatedEvent.Reset();
                UpdateSteps();

                if (!_mountPositionUpdatedEvent.Wait(5000, token))
                {
                    var errorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Error,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{_instanceName}|Timeout waiting for position update|Try:{maxTries}"
                    };
                    MonitorLog.LogToMonitor(errorItem);
                    throw new TimeoutException($"Mount position update timeout in precision goto (Mount: {_instanceName})");
                }

                if (maxTries >= 5) { break; }
                maxTries++;

                if (Settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                    // get predicted RA and Dec at update time
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    // convert to internal Ra and Dec
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1], settings: Settings);
                    target = [internalRaDec.X, internalRaDec.Y];
                }

                var skyTarget = MapSlewTargetToAxes(target, slewType);

                // Calculate error
                var rawPositions = GetRawDegrees();
                if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1])) { break; }
                deltaDegree[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                deltaDegree[1] = Range.Range180(skyTarget[1] - rawPositions[1]);

                axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested();
                if (!axis1AtTarget)
                {
                    _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0] + 0.25 * deltaDegree[0]);
                }
                var axis1Done = axis1AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();

                    if (!axis1Done)
                    {
                        var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                        axis1Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status1).Result);
                    }
                    if (axis1Done) { break; }
                }

                token.ThrowIfCancellationRequested();
                if (!axis2AtTarget)
                {
                    _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1] + 0.1 * deltaDegree[1]);
                }

                var axis2Done = axis2AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Done)
                    {
                        var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                        axis2Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status2).Result);
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
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"Mount:{_instanceName}|Delta|{deltaDegree[0]}|{deltaDegree[1]}|Seconds|{loopTimer.Elapsed.TotalSeconds}"
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
            double[] gotoPrecision = [Settings.GotoPrecision, Settings.GotoPrecision];
            long deltaTime = 400;

            try
            {
                while (true)
                {
                    var loopTimer = Stopwatch.StartNew();

                    // Event-based position update waiting
                    _mountPositionUpdatedEvent.Reset();
                    UpdateSteps();

                    if (!_mountPositionUpdatedEvent.Wait(5000, token))
                    {
                        var errorItem = new MonitorEntry
                        {
                            Datetime = HiResDateTime.UtcNow,
                            Device = MonitorDevice.Server,
                            Category = MonitorCategory.Server,
                            Type = MonitorType.Error,
                            Method = MethodBase.GetCurrentMethod()?.Name,
                            Thread = Environment.CurrentManagedThreadId,
                            Message = $"Mount:{_instanceName}|Timeout waiting for position update in pulse goto"
                        };
                        MonitorLog.LogToMonitor(errorItem);
                        throw new TimeoutException($"Mount position update timeout in pulse goto (Mount: {_instanceName})");
                    }

                    if (maxTries >= 5) { break; }
                    maxTries++;
                    double[] skyTarget = [0.0, 0.0];
                    double[] skyTargetNow = [0.0, 0.0];

                    if (Settings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        // Fix 1: compute two separate targets per iteration, mirroring SimPulseGoto.
                        // skyTarget      — feed-forward position for the hardware goto command.
                        // skyTargetNow   — where the mount should be RIGHT NOW, used only for the
                        //                  convergence check so that a pure Dec pulse does not
                        //                  produce a spurious Axis1 (Az) goto command.
                        var now = HiResDateTime.UtcNow;
                        var predictorRaDecAtTime = SkyPredictor.GetRaDecAtTime(now.AddMilliseconds(deltaTime));
                        var internalRaDecAtTime = Transforms.CoordTypeToInternal(predictorRaDecAtTime[0], predictorRaDecAtTime[1], settings: Settings);
                        skyTarget = MapSlewTargetToAxes([internalRaDecAtTime.X, internalRaDecAtTime.Y], SlewType.SlewRaDec);

                        var predictorRaDecNow = SkyPredictor.GetRaDecAtTime(now);
                        var internalRaDecNow = Transforms.CoordTypeToInternal(predictorRaDecNow[0], predictorRaDecNow[1], settings: Settings);
                        skyTargetNow = MapSlewTargetToAxes([internalRaDecNow.X, internalRaDecNow.Y], SlewType.SlewRaDec);
                    }

                    var rawPositions = GetRawDegrees();
                    if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                    { break; }

                    // Fix 1 (continued): use skyTargetNow for the convergence delta, with Range180
                    // wrapping, so delta[0] ≈ 0 for a pure Dec pulse and no Axis1 goto is issued.
                    deltaDegree[0] = Range.Range180(skyTargetNow[0] - rawPositions[0]);
                    deltaDegree[1] = Range.Range180(skyTargetNow[1] - rawPositions[1]);

                    axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                    axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                    if (axis1AtTarget && axis2AtTarget) { break; }

                    if (!axis1AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0]);
                    }

                    // Fix 2: remove the _slewState == SlewType.SlewNone early-break which fired
                    // immediately because pulse guide never sets _slewState, causing SkyPulseGoto
                    // to return while the hardware goto was still in flight.
                    var axis1Done = axis1AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis1Done)
                        {
                            var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                            axis1Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status1).Result);
                        }
                        if (axis1Done) { break; }
                    }

                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
                    }

                    var axis2Done = axis2AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis2Done)
                        {
                            var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                            axis2Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status2).Result);
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
        #region SlewController

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
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"SlewController initialized|Mount:{Id}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Modern async slew implementation using SlewController.
        /// Returns immediately after setup phase completes.
        /// </summary>
        private async Task<SlewResult> SlewAsync(double[] target, SlewType slewType, bool tracking = false)
        {
            EnsureSlewController();
            // Capture this instance's offset rates now — SkyServer.RateRa/Dec always
            // delegate to _defaultInstance and would be wrong for non-default instances.
            var operation = new SlewOperation(this, target, slewType, tracking, _rateRaDec.X, _rateRaDec.Y);
            return await _slewController!.ExecuteSlewAsync(operation);
        }

        /// <summary>
        /// Synchronous wrapper — blocks until slew completes.
        /// Used for synchronous ASCOM methods (FindHome, SlewToCoordinates).
        /// </summary>
        internal void SlewSync(double[] target, SlewType slewType, bool tracking = false)
        {
            EnsureSlewController();
            var operation = new SlewOperation(this, target, slewType, tracking, _rateRaDec.X, _rateRaDec.Y);
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

        #region AltAz Pulse Guide

        /// <summary>
        /// Execute single axis pulse guide for AltAz using this instance's SkyPredictor.
        /// </summary>
        internal void PulseGuideAltAz(int axis, double guideRate, int duration, Action<CancellationToken> pulseGoTo, CancellationToken token)
        {
            Task.Run(() =>
            {
                var pulseStartTime = Principles.HiResDateTime.UtcNow;
                // Route predictor adjustments through the queue (D4/D8).
                // Wait for the consumer to stop the timer (SlewBoundaryCommand ACK) before
                // pulseGoTo starts, so the hardware action cannot race the predictor write.
                switch (axis)
                {
                    case 0:
                        if (!_isPulseGuidingDec)
                        {
                            var ackRa = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            _trackingProcessor?.Post(new SlewBoundaryCommand(ackRa));
                            ackRa.Task.Wait(500); // wait for timer to stop (D7 timeout)
                        }
                        else
                            _ctsPulseGuideDec?.Cancel();
                        _trackingProcessor?.Post(new PulseGuideCommand(axis, guideRate, duration));
                        break;
                    case 1:
                        if (!_isPulseGuidingRa)
                        {
                            var ackDec = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            _trackingProcessor?.Post(new SlewBoundaryCommand(ackDec));
                            ackDec.Task.Wait(500); // wait for timer to stop (D7 timeout)
                        }
                        else
                            _ctsPulseGuideRa?.Cancel();
                        _trackingProcessor?.Post(new PulseGuideCommand(axis, guideRate, duration));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
                }
                // setup to log and graph the pulse
                var pulseEntry = new PulseEntry();
                if (_monitorPulse)
                {
                    pulseEntry.Axis = axis;
                    pulseEntry.Duration = duration;
                    pulseEntry.Rate = guideRate;
                    pulseEntry.StartTime = pulseStartTime;
                }
                // execute pulse
                pulseGoTo(token);

                // Only resume tracking when the pulse completed normally.
                // On cancellation the abort path (StopTrackingCommand + TrackingStateCommand)
                // owns the final tracking state; posting ResumeTrackingCommand here would
                // insert a spurious SetTracking() / SkyAxisSlew between the stop and the
                // abort's intended restore.
                if (!token.IsCancellationRequested)
                {
                    if (_trackingProcessor != null)
                        _trackingProcessor.Post(new ResumeTrackingCommand());
                    else
                        this.SetTracking();
                }

                // On cancellation clear the pulse-guiding flags immediately and skip the
                // remaining duration wait — the pulse is already stopped.
                if (token.IsCancellationRequested)
                {
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = Principles.HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = MonitorLog.GetCurrentMethod(),
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Axis|{axis}|Async operation cancelled"
                    });
                    switch (axis)
                    {
                        case 0: _isPulseGuidingRa = false; break;
                        case 1: _isPulseGuidingDec = false; break;
                    }
                    return;
                }

                // wait for pulse duration so completion variable IsPulseGuiding remains true
                var waitTime = (int)(pulseStartTime.AddMilliseconds(duration) - Principles.HiResDateTime.UtcNow).TotalMilliseconds;
                var updateInterval = Math.Max(duration / 20, 50);
                if (waitTime > 0)
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds < waitTime && !token.IsCancellationRequested)
                    {
                        Thread.Sleep(updateInterval);
                        this.UpdateSteps(); // Process positions while waiting
                    }
                }
                // log and graph pulse
                if (_monitorPulse)
                {
                    MonitorLog.LogToMonitor(pulseEntry);
                }
                // set pulse guiding status
                switch (axis)
                {
                    case 0: _isPulseGuidingRa = false; break;
                    case 1: _isPulseGuidingDec = false; break;
                }
            });
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
                    Method = "Mount",
                    Thread = Environment.CurrentManagedThreadId,
                    Message = message
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch
            {
                // Fail silently if logging fails
            }
        }

        /// <summary>
        /// Called by <see cref="TrackingCommandProcessor"/> when the consumer loop
        /// catches an unhandled exception. Logs to the monitor and preserves the
        /// last error so callers can inspect via <see cref="GetLastError"/>.
        /// </summary>
        internal void LogTrackingError(Exception ex)
        {
            _mountError = ex;
            LogMount($"TrackingProcessor|Error|{ex.GetType().Name}|{ex.Message}");
        }

        #endregion
    }
}
