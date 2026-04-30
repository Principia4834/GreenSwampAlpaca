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
using GreenSwamp.Alpaca.Shared.Transport;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// 
    /// </summary>
    public partial class Mount : IMountController
    {
        #region Private backing fields

        private readonly string _mountId;
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


        // Mount capabilities
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

        // Queues owned by this Mount
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

        public double SiderealTime => GetLocalSiderealTime(Settings.Longitude);

        public double Lha => Coordinate.Ra2Ha12(RightAscensionXForm, SiderealTime);

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
                WaitUpdateMountPosition();
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

        // IsSlewing — mirrors SkyServer.IsSlewing logic
        public bool IsSlewing =>
            (_slewController?.IsSlewing == true) ||
            (Math.Abs(_rateMoveAxes.X) + Math.Abs(_rateMoveAxes.Y)) > 0 ||
            _moveAxisActive ||
            _slewState != SlewType.SlewNone;

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
        /// <param name="id">Unique identifier (e.g., "telescope-0")</param>
        /// <param name="settings">Settings (can be file-based or static)</param>
        /// <param name="deviceName">User-provided device name (defaults to id if null)</param>
        public Mount(string id, SkySettings settings, string? deviceName = null)
        {
            Id = id ?? "mount-0";
            _mountId = id ?? "default";
            DeviceName = deviceName ?? id ?? "Unnamed Device";
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            // Wire settings back-reference
            Settings._owner = this;
            SkyPredictor = new SkyPredictor(() => CurrentTrackingRate());

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
        /// Gets the unique identifier for this mount
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

        // IMountController explicit implementation
        bool IMountController.Connect() => MountConnect();
        void IMountController.Disconnect() => MountStop();
        void IMountController.Start() => MountStart();
        void IMountController.Stop() => MountStop();
        void IMountController.Reset() => MountReset();

        /// <summary>
        /// Disconnect from the mount (stops all operations)
        /// </summary>
        public void Disconnect() => MountStop();

        /// <summary>
        /// Stop mount operations
        /// </summary>
        public void Stop() => MountStop();

        /// <summary>
        /// Emergency stop - halt all motion immediately
        /// </summary>
        public void EmergencyStop()
        {
            LogMount($"EmergencyStop() called on mount {Id}");
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

        #region Telescope API Bridge Methods

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
                    SimTasks(MountTaskName.StopAxes);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
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

        /// <summary>Synchronous Alt/Az slew — dispatches directly.</summary>
        public void SlewAltAz(double altitude, double azimuth) =>
            SlewSync([azimuth, altitude], SlewType.SlewAltAz);

        /// <summary>Async Alt/Az slew — dispatches directly.</summary>
        public Task<SlewResult> SlewAltAzAsync(double altitude, double azimuth) =>
            SlewAsync([azimuth, altitude], SlewType.SlewAltAz);

        /// <summary>Synchronous RA/Dec slew — dispatches directly.</summary>
        public void SlewRaDec(double rightAscension, double declination, bool tracking = false) =>
            SlewSync([rightAscension, declination], SlewType.SlewRaDec, tracking);

        /// <summary>Async RA/Dec slew — dispatches directly.</summary>
        public Task<SlewResult> SlewRaDecAsync(double rightAscension, double declination, bool tracking = false) =>
            SlewAsync([rightAscension, declination], SlewType.SlewRaDec, tracking);

        /// <summary>Enable tracking on a slew cycle</summary>
        public void CycleOnTracking(bool silence) => ApplyTracking(true);

        /// <summary>Save current position as a named park position</summary>
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

        /// <summary>Sync to given Alt/Az position</summary>
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
                    SimTasks(MountTaskName.StopAxes);
                    SimTasks(MountTaskName.SyncAltAz);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
                    SkyTasks(MountTaskName.SyncAltAz);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            WaitUpdateMountPosition(5000);
            if (trackingstate)
            {
                var internalAltAz = Transforms.CoordTypeToInternal(azimuth, altitude);
                var raDec = Coordinate.AltAz2RaDec(internalAltAz.X, internalAltAz.Y, SiderealTime, Settings.Latitude);
                SkyPredictor.Set(raDec[0], raDec[1]);
                ApplyTrackingDirect(true, TrackingMode.AltAz);
            }
        }

        /// <summary>Sync to current target RA/Dec</summary>
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
                    SimTasks(MountTaskName.StopAxes);
                    SimTasks(MountTaskName.SyncTarget);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
                    SkyTasks(MountTaskName.SyncTarget);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (!WaitUpdateMountPosition(5000)) throw new TimeoutException("Timeout waiting for mount position update after SyncToTargetRaDec");
            if (trackingstate)
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    SkyPredictor.Set(TargetRa, TargetDec);
                    ApplyTrackingDirect(true, TrackingMode.AltAz);
                }
                else
                {
                    ApplyTracking(true);
                }
            }
        }

        /// <summary>Check if RA/Dec is within sync limits</summary>
        public bool CheckRaDecSyncLimit(double ra, double dec)
        {
            if (!Settings.SyncLimitOn) { return true; }
            if (Settings.NoSyncPastMeridian) { return false; }
            var xy = Axes.RaDecToAxesXy([ra, dec], Settings);
            var target = Axes.AxesMountToApp(xy, Settings);
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

        /// <summary>Check if Alt/Az is within sync limits</summary>
        public bool CheckAltAzSyncLimit(double alt, double az)
        {
            if (!Settings.SyncLimitOn) { return true; }
            if (Settings.NoSyncPastMeridian) { return false; }
            var xy = Axes.AzAltToAxesXy([az, alt], Settings);
            var target = Axes.AxesMountToApp(xy, Settings);
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

        /// <summary>Check if target is within reachable hardware limits</summary>
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
        /// Waits for a single, event-driven mount position update to complete.
        /// </summary>
        /// <param name="waitTime">Maximum time to wait, in milliseconds. Default is 100 ms.</param>
        /// <remarks>
        /// This method implements an event-based update sequence:
        /// 1. Resets the _mountPositionUpdatedEvent.
        /// 2. Calls `UpdateSteps()` to request an immediate position/step update.
        /// 3. Waits up to <paramref name="waitTime"/> milliseconds for `_mountPositionUpdatedEvent` to be signalled.
        ///
        /// On timeout the method does not throw; instead it logs a warning to the monitor using
        /// `MonitorLog.LogToMonitor(...)`.
        /// </remarks>
        public bool WaitUpdateMountPosition(int waitTime = 100)
        {
            // Event-based position update waiting
            _mountPositionUpdatedEvent.Reset();
            UpdateSteps();
            var result = true;
            if (!_mountPositionUpdatedEvent.Wait(waitTime))
            {
                var errorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"Mount:{_mountId}|Timeout waiting for position update"
                };
                MonitorLog.LogToMonitor(errorItem);
                result = false;
            }
            return result;
        }

        /// <summary>
        /// Main get for the Steps
        /// Migrated from SkyServer.UpdateSteps()
        /// </summary>
        internal void UpdateSteps()
        {
            lock (_lastUpdateLock)
            {
                if (IsMountRunning) // || (_lastUpdateStepsTime.AddMilliseconds(100) < HiResDateTime.UtcNow))
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
            var lst = GetLocalSiderealTime(Settings.Longitude);

            // Implement PEC
            PecCheck();

            // Convert raw steps to degrees
            var rawPositions = new[]
            {
                ConvertStepsToDegrees(steps[0], 0),
                ConvertStepsToDegrees(steps[1], 1)
            };

            // Limit status
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
        /// Runs the full position pipeline, signals the position event,
        /// and notifies static observers (Blazor UI) for backward compatibility.
        /// </summary>
        internal void ReceiveSteps(double[] steps)
        {
            _steps = steps;
            SetSteps(steps);
            _mountPositionUpdatedEvent.Set();
        }

        #endregion

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
                var angleOffset = Settings.Latitude < 0 ? 180.0 : 0.0;
                home[0] -= angleOffset;
                home = Axes.AzAltToAxesXy(home, Settings);
            }
            return new Vector(home[0], home[1]);
        }

        #region AltAz Tracking Timer

        /// <summary>
        /// AltAz tracking timer tick handler.
        /// Posts a <see cref="TimerTickCommand"/> to the processor and returns
        /// immediately. Tick de-duplication is handled by the processor.
        /// </summary>
        private void AltAzTrackingTimerTick(object sender, EventArgs e)
        {
            _trackingProcessor?.PostTick();
        }

        /// <summary>
        /// Start the AltAz tracking timer. Called exclusively from
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
        /// Stop and dispose the AltAz tracking timer. Called
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

        #region Mount Operations

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
                Message = $"Mount:{_mountId}|from|{_actualAxisX}|{_actualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            token.ThrowIfCancellationRequested();
            var simTarget = MapSlewTargetToAxes(target, slewType);
            const int timer = 120;
            var stopwatch = Stopwatch.StartNew();

            SimTasks(MountTaskName.StopAxes);

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

            AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_mountId}|GoToSeconds|{stopwatch.Elapsed.TotalSeconds}|Target|{simTarget[0]}|{simTarget[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SimPrecisionGoto(target, slewType, token);
            #endregion

            SimTasks(MountTaskName.StopAxes);
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
                Message = $"Mount:{_mountId}|from|({_actualAxisX},{_actualAxisY})|to|({target[0]},{target[1]})"
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
                    Message = $"Mount:{_mountId}|Delta|({deltaDegree[0]},{deltaDegree[1]})|Seconds|{loopTimer.Elapsed.TotalSeconds}"
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
                Message = $"Mount:{_mountId}|from|{_actualAxisX}|{_actualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}|slewing|{slewType}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            token.ThrowIfCancellationRequested();

            var skyTarget = MapSlewTargetToAxes(target, slewType);
            const int timer = 240;
            var stopwatch = Stopwatch.StartNew();

            SkyTasks(MountTaskName.StopAxes);

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

            AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_mountId}|Seconds|{stopwatch.Elapsed.TotalSeconds}|Target|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                SkyPrecisionGoto(target, slewType, token);
            #endregion

            SkyTasks(MountTaskName.StopAxes);
            return success;
        }

        /// <summary>
        /// SkyWatcher pulse GOTO operation for continuous tracking correction
        /// </summary>
        #endregion
        #region SlewController

        #endregion

        #region AltAz Pulse Guide

        /// <summary>
        /// Execute single axis pulse guide for AltAz using SkyPredictor.
        /// </summary>
        internal void PulseGuideAltAz(int axis, double guideRate, int duration, Action<CancellationToken> pulseGoTo, CancellationToken token)
        {
            Task.Run(() =>
            {
                var pulseStartTime = Principles.HiResDateTime.UtcNow;
                // Route predictor adjustments through the queue.
                // Wait for the consumer to stop the timer (SlewBoundaryCommand ACK) before
                // pulseGoTo starts, so the hardware action cannot race the predictor write.
                switch (axis)
                {
                    case 0:
                        if (!_isPulseGuidingDec)
                        {
                            var ackRa = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            _trackingProcessor?.Post(new SlewBoundaryCommand(ackRa));
                            ackRa.Task.Wait(500); // wait for timer to stop
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
                            ackDec.Task.Wait(500); // wait for timer to stop
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
