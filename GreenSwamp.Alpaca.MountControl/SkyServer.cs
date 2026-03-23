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
// ReSharper disable RedundantAssignment
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using static System.Net.Mime.MediaTypeNames;


namespace GreenSwamp.Alpaca.MountControl
{
    public static partial class SkyServer
    {
        #region Events

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Fires StaticPropertyChanged("Steps") for Blazor UI observers.
        /// Called by MountInstance.ReceiveSteps instead of routing through the static Steps setter.
        /// </summary>
        internal static void NotifyStepsChanged()
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(Steps)));
        }

        #endregion

        #region Property Settings 

        #region Backers
        // private static bool _alertState;
        // private static bool _asComOn;
        // private static bool _limitAlarm;
        // private static bool _rotate3DModel;
        // Phase 6: _autoHomeProgressBar, _autoHomeStop, _canHomeSensor, _capabilities,
        //          _isAutoHomeRunning, _isPulseGuidingDec, _isPulseGuidingRa, _canPPec,
        //          _canPolarLed, _canAdvancedCmdSupport, _rateMoveAxes, _moveAxisActive,
        //          _snapPort1Result, _snapPort2Result moved to MountInstance backing fields.
        private static bool _mountRunning;
        // Phase 6: _parkSelected moved to MountInstance backing field
        private static ParkPosition? _parkSelected
        {
            get => _defaultInstance?._parkSelected;
            set { if (_defaultInstance != null) _defaultInstance._parkSelected = value; }
        }
        // Step 7: _steps moved to MountInstance backing field

        // Position update signaling - replaces MountPositionUpdated boolean
        private static readonly ManualResetEventSlim _mountPositionUpdatedEvent =
            new ManualResetEventSlim(false);

        /// <summary>
        /// Internal access to position update event for instance methods
        /// </summary>
        internal static ManualResetEventSlim MountPositionUpdatedEvent => _mountPositionUpdatedEvent;

        #endregion

        #region PEC

        /// <summary>
        /// Can mount do PPec
        /// </summary>
        internal static bool CanPPec
        {
            get => _defaultInstance?._canPPec ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._canPPec == value) return;
                _defaultInstance._canPPec = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// sets up bool to load a test tab
        /// </summary>
        public static bool PecShow
        {
            get => _defaultInstance?._pecShow ?? false;
            set { if (_defaultInstance == null) return; _defaultInstance._pecShow = value; OnStaticPropertyChanged(); }
        }

        /// <summary>
        /// Pec status
        /// </summary>
        internal static bool PecOn
        {
            get => _defaultInstance?.Settings.PecOn ?? false;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance.Settings.PecOn = value;
                // set back to normal tracking
                if (!value && Tracking) { SetTracking(); }
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec Currently used bin for Pec
        /// </summary>
        public static Tuple<int, double, int> PecBinNow
        {
            get => _defaultInstance?._pecBinNow;
            internal set
            {
                if (_defaultInstance == null) return;
                if (Equals(_defaultInstance._pecBinNow, value)) { return; }
                _defaultInstance._pecBinNow = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec Worm count bins
        /// </summary>
        internal static int PecBinCount
        {
            get => _defaultInstance?.PecBinCount ?? 0;
            set { if (_defaultInstance != null) _defaultInstance.PecBinCount = value; }
        }

        /// <summary>
        /// Turn on/off mount PPec
        /// </summary>
        internal static bool PPecOn
        {
            get => _defaultInstance?.Settings.PPecOn ?? false;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance.Settings.PPecOn = value;
                SkyTasks(MountTaskName.Pec);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// turn on/off mount training
        /// </summary>
        internal static bool PecTraining
        {
            get => _defaultInstance?._pPecTraining ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._pPecTraining == value) return;
                _defaultInstance._pPecTraining = value;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                SkyTasks(MountTaskName.PecTraining);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Tracks training within mount
        /// </summary>
        public static bool PecTrainInProgress
        {
            get => _defaultInstance?._pPecTrainInProgress ?? false;
            internal set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._pPecTrainInProgress == value) return;
                _defaultInstance._pPecTrainInProgress = value;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec 360 mode, list that holds all pec rate factors
        /// </summary>
        public static SortedList<int, Tuple<double, int>> Pec360Master => _defaultInstance?.Pec360Master;

        /// <summary>
        /// Loads both types of pec files
        /// </summary>
        /// <param name="fileName"></param>
        public static void LoadPecFile(string fileName) => _defaultInstance?.LoadPecFile(fileName);
        #endregion

        /// <summary>
        /// Actual positions in degrees
        /// </summary>
        public static double ActualAxisX
        {
            get => _defaultInstance?._actualAxisX ?? 0.0;
            set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance._actualAxisX) < 0.0001) return;
                _defaultInstance._actualAxisX = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Actual positions in degrees
        /// </summary>
        public static double ActualAxisY
        {
            get => _defaultInstance?._actualAxisY ?? 0.0;
            set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance._actualAxisY) < 0.0001) return;
                _defaultInstance._actualAxisY = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// X-axis position in local app coordinates
        /// </summary>
        public static double AppAxisX
        {
            get => _defaultInstance?._appAxes.X ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance._appAxes.X) < 0.000000000000001) return;
                _defaultInstance._appAxes.X = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        ///Y-axis position in local app coordinates
        /// </summary>
        public static double AppAxisY
        {
            get => _defaultInstance?._appAxes.Y ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance._appAxes.Y) < 0.000000000000001) return;
                _defaultInstance._appAxes.Y = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Low voltage event from mount status
        /// </summary>
        public static bool LowVoltageEventState
        {
            get => _defaultInstance?._lowVoltageEventState ?? false;
            private set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._lowVoltageEventState = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Get the controller voltage
        /// </summary>
        public static double ControllerVoltage
        {
            get
            {
                try
                {
                    var status = new SkyGetControllerVoltage(SkyQueue.NewId, Axis.Axis1);
                    return SkyQueue.GetCommandResult(status).Result;
                }
                catch (Exception)
                {
                    return double.NaN;
                }
            }
        }

        /// <summary>
        /// Gets the current polar mode based on the alignment mode and mount type.
        /// </summary>
        public static PolarMode PolarMode
        {
            get
            {
                if (_settings!.AlignmentMode == AlignmentMode.Polar)
                {
                    return _settings!.Mount == MountType.SkyWatcher ? _settings!.PolarMode : PolarMode.Right;
                }
                else
                {
                    // default to right
                    return PolarMode.Right;
                }
            }
        }

        /// <summary>
        /// UI indicator for at home
        /// </summary>
        public static bool IsHome
        {
            get => _defaultInstance?._isHome ?? false;
            internal set
            {
                if (_defaultInstance == null) return;
                if (value == _defaultInstance._isHome) return;
                _defaultInstance._isHome = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Factor to convert steps, Sky Watcher in rad
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._factorStep instead
        /// </summary>
        internal static double[] FactorStep
        {
            get => _defaultInstance?._factorStep ?? new double[] { 0.0, 0.0 };
            set { if (_defaultInstance != null) _defaultInstance._factorStep = value; }
        }

        /// <summary>
        /// applies backlash to pulse
        /// </summary>
        private static GuideDirection LastDecDirection { get; set; }

        /// <summary>
        /// Count number of times server loop is executed
        /// </summary>
        public static ulong LoopCounter
        {
            get => _defaultInstance?._loopCounter ?? 0;
            internal set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._loopCounter = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// use monitoring for charts
        /// </summary>
        public static bool MonitorPulse
        {
            private get => _defaultInstance?._monitorPulse ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._monitorPulse == value) return;
                _defaultInstance._monitorPulse = value;

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);

                SimTasks(MountTaskName.MonitorPulse);
                SkyTasks(MountTaskName.MonitorPulse);
            }
        }

        /// <summary>
        /// Used to inform and show error on the UI thread
        /// </summary>
        public static Exception MountError
        {
            get => _defaultInstance?._mountError;
            internal set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._mountError = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Is Dec pulse guiding
        /// </summary>
        public static bool IsPulseGuidingDec
        {
            get => _defaultInstance?._isPulseGuidingDec ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._isPulseGuidingDec != value)
                {
                    _defaultInstance._isPulseGuidingDec = value;
                    // reset Dec pulse guiding cancellation token source
                    if (!_defaultInstance._isPulseGuidingDec && _ctsPulseGuideDec != null)
                    {
                        _ctsPulseGuideDec?.Dispose();
                        _ctsPulseGuideDec = null;
                    }
                }
            }
        }

        /// <summary>
        /// Is Ra pulse guiding
        /// </summary>
        public static bool IsPulseGuidingRa
        {
            get => _defaultInstance?._isPulseGuidingRa ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._isPulseGuidingRa != value)
                {
                    _defaultInstance._isPulseGuidingRa = value;
                    // reset Ra pulse guiding cancellation token source
                    if (!_defaultInstance._isPulseGuidingRa && _ctsPulseGuideRa != null)
                    {
                        _ctsPulseGuideRa?.Dispose();
                        _ctsPulseGuideRa = null;
                    }
                }
            }
        }

        /// <summary>
        /// Move Secondary axis at the given rate in degrees, MoveAxis
        /// Tracking if enabled:
        /// - is restored for the Secondary axis when MoveAxis is called with rate = 0
        /// - continues for the Primary axis unless it is also executing a MoveAxis command
        /// </summary>
        public static double RateMoveSecondaryAxis
        {
            private get => _defaultInstance?._rateMoveAxes.Y ?? 0.0;
            set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(_defaultInstance._rateMoveAxes.Y - value) < .0000000001) return;
                _defaultInstance._rateMoveAxes.Y = value;
                CancelAllAsync();
                // Set slewing state
                SetRateMoveSlewState();
                // Move axis at requested rate
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(0, Axis.Axis2, -_defaultInstance._rateMoveAxes.Y);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(0, Axis.Axis2, _defaultInstance._rateMoveAxes.Y);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                // Update tracking if required
                if (Tracking) SetTracking();

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_defaultInstance._rateMoveAxes.Y}|{SkyTrackingOffset[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Move Primary axis at the given rate in degrees, MoveAxis
        /// Tracking if enabled:
        /// - is restored for the Primary axis when MoveAxis is called with rate = 0
        /// - continues for the Secondary axis unless it is also executing a MoveAxis command
        /// </summary>
        public static double RateMovePrimaryAxis
        {
            private get => _defaultInstance?._rateMoveAxes.X ?? 0.0;
            set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(_defaultInstance._rateMoveAxes.X - value) < 0.0000000001) return;
                _defaultInstance._rateMoveAxes.X = value;
                CancelAllAsync();
                // Set slewing state
                SetRateMoveSlewState();
                // Move axis at requested rate
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(0, Axis.Axis1, _defaultInstance._rateMoveAxes.X);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(0, Axis.Axis1, _defaultInstance._rateMoveAxes.X);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                // Update tracking if required
                if (Tracking) SetTracking();
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_defaultInstance._rateMoveAxes.X}|{SkyTrackingOffset[0]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        

        /// <summary>
        /// time in seconds for mount to settle after slew
        /// </summary>
        public static double SlewSettleTime
        {
            get => _defaultInstance?._slewSettleTime ?? 0.0;
            set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(_defaultInstance._slewSettleTime - value) <= 0) return;
                _defaultInstance._slewSettleTime = value;
            }
        }

        /// <summary>
        /// Total steps per 360
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._stepsPerRevolution instead
        /// </summary>
        public static long[] StepsPerRevolution
        {
            get => _defaultInstance?._stepsPerRevolution ?? new long[] { 0, 0 };
            set { if (_defaultInstance != null) _defaultInstance._stepsPerRevolution = value; }
        }

        /// <summary>
        /// :b Timer Freq
        /// </summary>
        public static long[] StepsTimeFreq { get; private set; } = { 0, 0 };

        /// <summary>
        /// current micro steps, used to update SkyServer and UI
        /// </summary>
        public static double[] Steps 
        {
            get => _defaultInstance?._steps ?? new double[] { 0.0, 0.0 };
            set
            {
                // Delegate to the per-instance pipeline (sets _steps, runs coordinate conversion,
                // signals _mountPositionUpdatedEvent, and fires NotifyStepsChanged for Blazor UI)
                _defaultInstance?.ReceiveSteps(value);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Total worm teeth
        /// </summary>
        public static int[] WormTeethCount
        {
            get => _defaultInstance?._wormTeethCount ?? new int[] { 0, 0 };
            set { if (_defaultInstance != null) _defaultInstance._wormTeethCount = value; }
        }

        /// <summary>
        /// Total worm step per 360
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._stepsWormPerRevolution instead
        /// </summary>
        public static double[] StepsWormPerRevolution
        {
            get => _defaultInstance?._stepsWormPerRevolution ?? new double[] { 0.0, 0.0 };
            set { if (_defaultInstance != null) _defaultInstance._stepsWormPerRevolution = value; }
        }

        /// <summary>
        /// Southern alignment status
        /// </summary>
        public static bool SouthernHemisphere => _settings!.Latitude < 0;

        /// <summary>
        /// Counts any overlapping events with updating UI that might occur
        /// should always be 0 or event interval is too fast
        /// </summary>
        internal static int TimerOverruns
        {
            get => _defaultInstance?._timerOverruns ?? 0;
            set { if (_defaultInstance != null) _defaultInstance._timerOverruns = value; }
        }

        /// <summary>
        /// Current Alt/Az tracking mode - RA/Dec predictor or calculated tracking rate
        /// </summary>
        public static AltAzTrackingType AltAzTrackingMode
        {
            get => _defaultInstance?._altAzTrackingMode ?? default;
            set { if (_defaultInstance != null) _defaultInstance._altAzTrackingMode = value; }
        }

        #endregion

        #region Simulator Items

        #endregion

        #region SkyWatcher Items

        #endregion

        #region Shared Mount Items

        ///// <summary>
        ///// Convert the move rate in hour angle and declination to a move rate in altitude and azimuth
        ///// </summary>
        ///// <param name="haRate">The ha rate.</param>
        ///// <param name="decRate">The dec rate </param>
        ///// <returns></returns>
        //private static Vector ConvertRateToAltAz(double haRate, double decRate)
        //{
        //    return ConvertRateToAltAz(haRate, decRate, TargetDec);
        //}

        ///// <summary>
        ///// gets HC speed in degrees
        ///// </summary>
        ///// <param name="speed"></param>
        ///// <returns></returns>
        //public static double GetSlewSpeed(SlewSpeed speed)
        //{
        //    switch (speed)
        //    {
        //        case SlewSpeed.One:
        //            return SlewSpeedOne;
        //        case SlewSpeed.Two:
        //            return SlewSpeedTwo;
        //        case SlewSpeed.Three:
        //            return SlewSpeedThree;
        //        case SlewSpeed.Four:
        //            return SlewSpeedFour;
        //        case SlewSpeed.Five:
        //            return SlewSpeedFive;
        //        case SlewSpeed.Six:
        //            return SlewSpeedSix;
        //        case SlewSpeed.Seven:
        //            return SlewSpeedSeven;
        //        case SlewSpeed.Eight:
        //            return SlewSpeedEight;
        //        default:
        //            return 0.0;
        //    }
        //}

        /// <summary>
        /// Resets the anti-backlash for the hand controller
        /// </summary>
        private static void HcResetPrevMove(MountAxis axis)
        {
            switch (axis)
            {
                case MountAxis.Dec:
                    _hcPrevMoveDec = null;
                    break;
                case MountAxis.Ra:
                    _hcPrevMoveRa = null;
                    break;
            }
        }
        #endregion

        #region Resync
        /// <summary>
        /// Reset positions for the axes.
        /// </summary>
        /// <param name="parkPosition">ParkPosition or Null for home</param>
        public static void ReSyncAxes(ParkPosition parkPosition = null, bool saveParkPosition = true)
        {
            if (!IsMountRunning) { return; }
            Tracking = false;
            StopAxes();

            //set to home position
            double[] position = { HomeAxes.X, HomeAxes.Y };
            var name = "home";

            //set to park position
            if (parkPosition != null)
            {
                // Set context from current settings
                var context = AxesContext.FromSettings(_settings);
                position = Axes.AxesAppToMount(new[] { parkPosition.X, parkPosition.Y }, context);
                name = parkPosition.Name;
            }

            //log
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{position[0]}|{position[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            switch (_settings!.Mount) // mount type check
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes);
                    _ = new CmdAxisToDegrees(0, Axis.Axis1, position[0]);
                    _ = new CmdAxisToDegrees(0, Axis.Axis2, position[1]);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
                    _ = new SkySetAxisPosition(0, Axis.Axis1, position[0]);
                    _ = new SkySetAxisPosition(0, Axis.Axis2, position[1]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //all good, go ahead and set dropdown to the park position and park
            if (parkPosition != null && saveParkPosition)
            {
                ParkSelected = parkPosition;
                GoToPark();
            }

            //reset any hc moves
            HcResetPrevMove(MountAxis.Ra);
            HcResetPrevMove(MountAxis.Dec);
        }

        #endregion

        #region Alignment

        // internal static double DegToRad(double degree) { return (degree / 180.0 * Math.PI); }
        // internal static double RadToDeg(double rad) { return (rad / Math.PI * 180.0); }

        #endregion

        #region Snap Ports

        /// <summary>
        /// Camera Port
        /// </summary>
        public static bool SnapPort1 { get; set; }

        public static bool SnapPort2 { get; set; }

        public static bool SnapPort1Result
        {
            get => _defaultInstance?._snapPort1Result ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._snapPort1Result == value) { return; }
                _defaultInstance._snapPort1Result = value;
                OnStaticPropertyChanged();
            }
        }

        public static bool SnapPort2Result
        {
            get => _defaultInstance?._snapPort2Result ?? false;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._snapPort2Result == value) { return; }
                _defaultInstance._snapPort2Result = value;
                OnStaticPropertyChanged();
            }
        }

        // SnapPort1, SnapPort2, SnapPort1Result, SnapPort2Result will be moved here

        #endregion

        #region Park Management

        public static ParkPosition GetStoredParkPosition()
        {
            var p = new ParkPosition { Name = _settings!.ParkName, X = _settings!.ParkAxes[0], Y = _settings!.ParkAxes[1] };
            return p;
        }

        // ParkSelected, SetParkAxis will be moved here

        #endregion

        #region Auto Home

        /// <summary>
        /// UI progress bar for autoHome 
        /// </summary>
        public static int AutoHomeProgressBar
        {
            get => _defaultInstance?._autoHomeProgressBar ?? 0;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._autoHomeProgressBar = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Cancel button status for auto home
        /// </summary>
        public static bool AutoHomeStop
        {
            get => _defaultInstance?._autoHomeStop ?? false;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._autoHomeStop = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Checks if the auto home async process is running
        /// </summary>
        public static bool IsAutoHomeRunning
        {
            get => _defaultInstance?._isAutoHomeRunning ?? false;
            private set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._isAutoHomeRunning = value;
                OnStaticPropertyChanged();
            }
        }

        // AutoHomeAsync, GetAutoHomeResultMessage, IsAutoHomeRunning, etc. will be moved here

        #endregion

        #region Server Items

        /// <summary>
        /// Mount name
        /// </summary>
        public static string MountName
        {
            get => _defaultInstance?._mountName ?? string.Empty;
            private set { if (_defaultInstance != null) _defaultInstance._mountName = value; }
        }

        /// <summary>
        /// Controller board version
        /// </summary>
        public static string MountVersion
        {
            get => _defaultInstance?._mountVersion ?? string.Empty;
            private set { if (_defaultInstance != null) _defaultInstance._mountVersion = value; }
        }

        /// <summary>
        /// Starts/Stops current selected mount
        /// </summary>
        public static bool IsMountRunning
        {
            get => _defaultInstance?.IsMountRunning ?? _mountRunning;
            set
            {
                _mountRunning = value;
                LoopCounter = 0;
                if (value)
                {
                    MountStart();
                }
                else
                {
                    MountStop();
                }

                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public struct LimitStatusType
        {
            public bool AtLowerLimitAxisX { get; set; }
            public bool AtUpperLimitAxisX { get; set; }
            public bool AtLowerLimitAxisY { get; set; }
            public bool AtUpperLimitAxisY { get; set; }
            public bool AtLimit
            {
                get => AtLowerLimitAxisX || AtUpperLimitAxisX || AtLowerLimitAxisY || AtUpperLimitAxisY;
            }
        }

        public static LimitStatusType LimitStatus = new LimitStatusType();

        public static bool CanPolarLed
        {
            get => _defaultInstance?._canPolarLed ?? false;
            private set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._canPolarLed == value) { return; }
                _defaultInstance._canPolarLed = value;
                OnStaticPropertyChanged();
            }
        }

        public static bool CanAdvancedCmdSupport
        {
            get => _defaultInstance?._canAdvancedCmdSupport ?? false;
            private set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._canAdvancedCmdSupport == value) { return; }
                _defaultInstance._canAdvancedCmdSupport = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Support for a home sensor
        /// </summary>
        public static bool CanHomeSensor
        {
            get => _defaultInstance?._canHomeSensor ?? false;
            private set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._canHomeSensor == value) { return; }
                _defaultInstance._canHomeSensor = value;
                OnStaticPropertyChanged();
            }
        }

        public static string Capabilities
        {
            get => _defaultInstance?._capabilities ?? string.Empty;
            set
            {
                if (_defaultInstance == null) return;
                if (_defaultInstance._capabilities == value) { return; }
                _defaultInstance._capabilities = value;
                OnStaticPropertyChanged();
            }
        }

        #endregion

    }
}
