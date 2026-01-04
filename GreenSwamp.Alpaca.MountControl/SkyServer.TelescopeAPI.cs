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

// ============================================================================
// SkyServer.TelescopeAPI.cs - ASCOM Telescope API Implementation
// ============================================================================
// This partial class file contains the ASCOM-compliant telescope interface:
// - ASCOM position properties (RA, Dec, Alt, Az, etc.)
// - ASCOM state properties (Tracking, Slewing, Parked, etc.)
// - Mount control methods (Slew, GoTo, Abort, Stop)
// - Parking and homing operations
// - Syncing operations
// - Pulse guiding
// - MoveAxis support
// - Position calculations and validation
// - Tracking and rate control
// - Async operations
// Dependencies: Relies on Core partial class for underlying operations
// ============================================================================

using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Net.Mime;
using System.Reflection;
using GreenSwamp.Alpaca.Mount.AutoHome;
using static System.Math;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    public static partial class SkyServer
    {
        #region ASCOM Position Properties

        /// <summary>
        /// Positions converted from mount
        /// </summary>
        public static double RightAscension
        {
            get => _raDec.X;
            private set
            {
                if (Math.Abs(value - _raDec.X) < 0.000000000000001) return;
                _raDec.X = value;
            }
        }

        /// <summary>
        /// Declination in degrees
        /// </summary>
        public static double Declination
        {
            get => _raDec.Y;
            private set
            {
                if (Math.Abs(value - _raDec.Y) < 0.000000000000001) return;
                _raDec.Y = value;
            }
        }
         
        /// <summary>
        /// UI display for converted ra
        /// </summary>
        public static double RightAscensionXForm
        {
            get => _rightAscensionXForm;
            private set
            {
                _rightAscensionXForm = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// UI display for converted dec
        /// </summary>
        public static double DeclinationXForm
        {
            get => _declinationXForm;
            private set
            {
                _declinationXForm = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Positions converted from mount
        /// </summary>
        public static double Altitude
        {
            get => _altAzm.Y;
            private set
            {
                // if (Math.Abs(value - _altAzm.Y) < 0.000000000000001) { return; }
                _altAzm.Y = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Positions converted from mount
        /// </summary>
        public static double Azimuth
        {
            get => _altAzm.X;
            private set
            {
                // if (Math.Abs(value - _altAzm.X) < 0.000000000000001) { return; }
                _altAzm.X = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Local time
        /// </summary>
        public static double SiderealTime
        {
            get => _siderealTime;
            private set
            {
                _siderealTime = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Local Hour Angle
        /// </summary>
        public static double Lha
        {
            get => _lha;
            private set
            {
                if (Math.Abs(value - _lha) < 0.000000000000001) { return; }
                _lha = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// UI indicator
        /// </summary>
        public static PointingState IsSideOfPier
        {
            get => _isSideOfPier;
            private set
            {
                if (value == IsSideOfPier) return;
                // ToDo re-enable voice prompt later
                //string sideOfPierVoice = string.Empty;
                //switch (SkySettings.AlignmentMode)
                //{
                //    case AlignmentMode.AltAz:
                //        sideOfPierVoice = MediaTypeNames.Application.Current.Resources["vceSop" + ((PierSideUI)value + 4).ToString()].ToString();
                //        break;
                //    case AlignmentMode.Polar:
                //        sideOfPierVoice = MediaTypeNames.Application.Current.Resources["vceSop" + ((PierSideUI)value + 2).ToString()].ToString();
                //        break;
                //    case AlignmentMode.GermanPolar:
                //        sideOfPierVoice = MediaTypeNames.Application.Current.Resources["vceSop" + ((PierSideUI)value + 0).ToString()].ToString();
                //        break;
                //    default:
                //        break;
                //}
                //Synthesizer.Speak(sideOfPierVoice.ToString());
                _isSideOfPier = value;
                OnStaticPropertyChanged();

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{_appAxes.Y}|{_appAxes.Y < 90 || _appAxes.Y.IsEqualTo(90, 0.0000000001)}|{_appAxes.Y > -90 || _appAxes.Y.IsEqualTo(-90, 0.0000000001)} "
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Moves mount to other side / through the pole
        /// If tracking RA and Dec are unchanged otherwise Alt Az
        /// </summary>
        public static PointingState SideOfPier
        {
            get
            {
                PointingState sideOfPier = PointingState.Unknown;
                switch (SkySettings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        sideOfPier = _actualAxisX >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
                        break;
                    case AlignmentMode.Polar:
                        sideOfPier = (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001) ? PointingState.Normal : PointingState.ThroughThePole;
                        break;
                    case AlignmentMode.GermanPolar:
                        if (SouthernHemisphere)
                        {
                            //return _appAxes.Y <= 90 && _appAxes.Y >= -90 ? PointingState.ThroughThePole : PointingState.Normal;
                            // replaced with ...
                            sideOfPier = (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001) ? PointingState.ThroughThePole :PointingState.Normal;
                        }
                        else
                        {
                            // return _appAxes.Y <= 90 && _appAxes.Y >= -90 ? PointingState.Normal : PointingState.ThroughThePole;
                            // replaced with ...
                            sideOfPier = (_appAxes.Y < 90.0000000001 && _appAxes.Y > -90.0000000001) ? PointingState.Normal : PointingState.ThroughThePole;
                        }
                        break;
                    default:
                        break;
                }
                return sideOfPier;
            }
            set
            {
                var axes = new[] { ActualAxisX, ActualAxisY };
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{SkySettings.HourAngleLimit}|{axes[0]}|{axes[1]}"
                };
                if (IsWithinFlipLimits(Axes.AxesMountToApp(axes)))
                {
                    if (Tracking)
                    {
                        FlipOnNextGoto = true;
                        SlewRaDec(RightAscension, Declination, true);
                    }
                    else
                    {
                        FlipOnNextGoto = true;
                        SlewAltAz(Altitude, Azimuth);
                    }
                    MonitorLog.LogToMonitor(monitorItem);
                }
                else
                {
                    monitorItem.Type = MonitorType.Warning;
                    MonitorLog.LogToMonitor(monitorItem);
                    throw new InvalidOperationException(
                        $"SideOfPier ({value}) is outside the range of set Limits");

                }
            }
        }

        #endregion

        #region ASCOM State Properties

        /// <summary>
        /// Tracking status
        /// AltAzPredictor set / reset for Tracking true / false
        /// </summary>
        public static bool Tracking
        {
            get => _trackingMode != TrackingMode.Off;
            set
            {
                if (value == _tracking)
                {
                    OnStaticPropertyChanged();
                    return;
                } //off

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

                SkyPredictor.Reset();
                if (value)
                {
                    // Tracking on
                    if (AtPark)
                    {
                        // ToDo re-enable voice prompt later
                        // if (TrackingSpeak) { Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceParked"].ToString()); }
                        // ToDo: fix exception string
                        // throw new ASCOM.ParkedException(MediaTypeNames.Application.Current.Resources["exParked"].ToString());
                        throw new ASCOM.ParkedException("exParked");
                    }
                    // Set tracking mode based on AlignmentMode and hemisphere
                    SetTrackingMode();
                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            AltAzTrackingMode = AltAzTrackingType.Predictor;
                            // Must have a tracking target for Alt Az otherwise just set the reference time to now
                            if (!SkyPredictor.RaDecSet)
                            {
                                SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, 0, 0);
                            }
                            else
                            {
                                SkyPredictor.ReferenceTime = DateTime.Now;
                            }
                            // ToDo re-enable voice prompt later
                            // if (TrackingSpeak) Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceTrackingOn"].ToString());
                            break;
                        case AlignmentMode.GermanPolar:
                        case AlignmentMode.Polar:
                            // if (TrackingSpeak) Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceTrackingOn"].ToString());
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    // Tracking off
                    IsPulseGuidingDec = false; // Ensure pulses are off
                    IsPulseGuidingRa = false;
                    // ToDo re-enable voice prompt later
                    // if (TrackingSpeak && _trackingMode != TrackingMode.Off) { Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceTrackingOff"].ToString()); }
                    _trackingMode = TrackingMode.Off;
                }
                _tracking = value; //off

                SetTracking();
                OnStaticPropertyChanged();
            }
        }

        private static bool _isSlewing;
        /// <summary>
        /// status for goto
        /// </summary>
        public static bool IsSlewing
        {
            get => _isSlewing;
            private set
            {
                if (_isSlewing == value) { return; }
                _isSlewing = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// within 0.1 degree circular range to trigger home
        /// </summary>
        public static bool AtHome
        {
            get
            {
                // Home axis values are mount values internally
                var home = Axes.AxesMountToApp(new[] { _homeAxes.X, _homeAxes.Y });
                var h = new Vector(home[0], home[1]);
                var m = new Vector(_appAxes.X, _appAxes.Y);
                double dX = Abs(m.X - h.X);
                dX = Min(dX, 360.0 - dX);   // Az Alt can have home (0, 0) so wrap at 360
                double dY = Abs(m.Y - h.Y);
                var d = new Vector(dX, dY);
                var r = d.LengthSquared < 0.01414;
                // only report AtHome when slewing has finished
                return r;
            }
        }

        /// <summary>
        /// Is at park position
        /// </summary>
        public static bool AtPark
        {
            get => SkySettings.AtPark;
            set
            {
                SkySettings.AtPark = value;
                OnStaticPropertyChanged();
                // ToDo re-enable voice prompt later
                // Synthesizer.Speak(value ? MediaTypeNames.Application.Current.Resources["vceParked"].ToString() : MediaTypeNames.Application.Current.Resources["vceUnParked"].ToString());

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Pulse reporting to driver
        /// Alt Az uses both axes so always synchronous pulse guiding on one of Ra or Dec
        /// </summary>
        public static bool IsPulseGuiding => (IsPulseGuidingDec || IsPulseGuidingRa);

        private static bool _flipOnNextGoto;
        /// <summary>
        /// UI Checkbox option to flip on the next goto
        /// </summary>
        public static bool FlipOnNextGoto
        {
            get => _flipOnNextGoto;
            set
            {
                _flipOnNextGoto = value;

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

        private static SlewType _slewState;
        /// <summary>
        /// Set for all types of go tos
        /// </summary>
        public static SlewType SlewState
        {
            get => _slewState;
            private set
            {
                _slewState = value;
            }
        }

        private static Exception _lastAutoHomeError;
        /// <summary>
        /// Checks if the auto home async process is running
        /// </summary>
        public static Exception LastAutoHomeError
        {
            get => _lastAutoHomeError;
            private set
            {
                _lastAutoHomeError = value;
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Target & Rate Properties

        private static Vector _rateRaDec;
        /// <summary>
        /// The declination tracking rate in degrees, DeclinationRate
        /// corrected direction applied
        /// </summary>
        public static double RateDec
        {
            get => _rateRaDec.Y;
            set
            {
                _rateRaDec.Y = value;
                ActionRateRaDec(); // Update the mount tracking rate

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_rateRaDec.Y}|{SkyTrackingOffset[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Store the original DeclinationRate to maintain direction
        /// </summary>
        public static double RateDecOrg { get; set; }

        /// <summary>
        /// The right ascension tracking in degrees, RightAscensionRate
        /// corrected direction applied
        /// </summary>
        public static double RateRa
        {
            get => _rateRaDec.X;
            set
            {
                _rateRaDec.X = value;
                ActionRateRaDec(); // Update the mount tracking rate

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_rateRaDec.X}|{SkyTrackingOffset[0]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Store the original RightAscensionRate and maintain direction
        /// Previous conversions were not exact
        /// </summary>
        public static double RateRaOrg { get; set; }

        private static Vector _targetRaDec;
        /// <summary>
        /// Dec target for slewing, epoch is same as EquatorialSystem Property
        /// convert to top-o-centric for any internal calculations
        /// </summary>
        public static double TargetDec
        {
            get => _targetRaDec.Y;
            set => _targetRaDec.Y = value;
        }

        /// <summary>
        /// Ra target for slewing, epoch is same as EquatorialSystem Property
        /// convert to top-o-centric for any internal calculations
        /// </summary>
        public static double TargetRa
        {
            get => _targetRaDec.X;
            set => _targetRaDec.X = value;
        }

        private static Vector _guideRate;
        /// <summary>
        /// The current Declination movement rate offset for telescope guiding (degrees/sec) 
        /// </summary>
        public static double GuideRateDec
        {
            get => _guideRate.Y;
            set => _guideRate.Y = value;
        }

        /// <summary>
        /// The current Right Ascension movement rate offset for telescope guiding (degrees/sec) 
        /// </summary>
        public static double GuideRateRa
        {
            get => _guideRate.X;
            set => _guideRate.X = value;
        }

        #endregion

        #region Mount Control Methods

        /// <summary>
        /// Starts slew with ra/dec coordinates
        /// </summary>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// <param name="tracking"></param>
        public static void SlewRaDec(double rightAscension, double declination, bool tracking = false)
        {
            SlewMount(new Vector(rightAscension, declination), SlewType.SlewRaDec, tracking);
        }

        /// <summary>
        /// Starts slew with alt/az coordinates
        /// </summary>
        /// <param name="altitude"></param>
        /// <param name="azimuth"></param>
        public static void SlewAltAz(double altitude, double azimuth)
        {
            SlewMount(new Vector(azimuth, altitude), SlewType.SlewAltAz);
        }

        /// <summary>
        /// Starts slew with primary/seconday internal coordinates, not mount positions
        /// </summary>
        /// <param name="primaryAxis"></param>
        /// <param name="secondaryAxis"></param>
        /// <param name="slewState"></param>
        public static void SlewAxes(double primaryAxis, double secondaryAxis, SlewType slewState, bool slewAsync = true)
        {
            SlewMount(new Vector(primaryAxis, secondaryAxis), slewState, false, slewAsync);
        }

        /// <summary>
        /// Initiates an asynchronous GoTo operation to move the mount to the specified target coordinates.
        /// </summary>
        /// <remarks>This method handles various mount states, including stopping any ongoing slewing
        /// operations, canceling pending asynchronous operations, and ensuring the mount is ready for the new GoTo
        /// command. <para> The method supports different mount types and slewing modes, such as equatorial (RA/Dec) and
        /// alt-azimuth slewing. It also manages tracking states and updates the mount's internal state based on the
        /// operation's outcome. </para> <para> If the mount is not running, the method exits early and signals the
        /// <paramref name="goToStarted"/> handle. If the operation is canceled or fails, the mount's axes are stopped,
        /// and the internal state is reset. </para></remarks>
        /// <param name="target">An array of two doubles representing the target coordinates.</param>
        /// <param name="slewState">The type of slew operation to perform, specified as a <see cref="SlewType"/>.</param>
        /// <param name="goToStarted">An <see cref="EventWaitHandle"/> that is signaled when the GoTo operation starts.</param>
        /// <param name="tracking">Optional. A boolean value indicating whether tracking should be enabled after the GoTo operation completes.
        /// Defaults to <see langword="false"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if an unsupported mount type or slew type is specified.</exception>
        // Lock for synchronizing GoToAsync operations
        private static readonly object _goToAsyncLock = new object();
        private static void GoToAsync(double[] target, SlewType slewState, EventWaitHandle goToStarted, bool tracking = false)
        {
            MonitorEntry monitorItem;
            if (!IsMountRunning)
            {
                goToStarted.Set();
                return;
            }

            // bool trackingState;
            // SlewType startingState;

            // Synchronize access to prevent race conditions with cancellation token
            //lock (_goToAsyncLock)
            //{
                CancelAllAsync();
                var swCts = Stopwatch.StartNew();
                while (_ctsGoTo != null && swCts.ElapsedMilliseconds < 5000) Thread.Sleep(10);
                if (IsSlewing)
                {
                    SlewState = SlewType.SlewNone;
                    var stopped = AxesStopValidate();
                    if (!stopped)
                    {
                        AbortSlew(true);
                        monitorItem = new MonitorEntry
                        {
                            Datetime = HiResDateTime.UtcNow,
                            Device = MonitorDevice.Server,
                            Category = MonitorCategory.Server,
                            Type = MonitorType.Warning,
                            Method = MonitorLog.GetCurrentMethod(),
                            Thread = Thread.CurrentThread.ManagedThreadId,
                            Message = "Timeout stopping axes"
                        };
                        MonitorLog.LogToMonitor(monitorItem);
                        return;
                    }
                }

                SlewState = slewState;
                var startingState = slewState;
                // Planetarium fix to set Tracking for non-ASCOM compliant programs - set true by GoToCoordinatesAsync()
                var trackingState = tracking || Tracking;
                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;
                Tracking = false;
                if (slewState == SlewType.SlewRaDec)
                {
                    SkyPredictor.Set(TargetRa, TargetDec, RateRa, RateDec); // 
                }
                IsSlewing = true;
                goToStarted.Set(); // Signal that GoTo has started so async ASCOM operations can return with Slewing = true

                // Create new cancellation token source INSIDE the lock
                // _ctsGoTo = new CancellationTokenSource();
            // } // Release lock before starting the potentially long-running slew operation

            // Assume fail
            try
            {
                _ctsGoTo = new CancellationTokenSource();
                var returnCode = 1;
                switch (SkySettings.Mount)
                {
                    case MountType.Simulator:
                        returnCode = SimGoTo(target, trackingState, slewState, _ctsGoTo.Token);
                        break;
                    case MountType.SkyWatcher:
                        returnCode = SkyGoTo(target, trackingState, slewState, _ctsGoTo.Token);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();

                }

                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;

                if (returnCode == 0)
                {
                    if (SlewState == SlewType.SlewNone)
                    {
                        Tracking = trackingState;
                        // ToDo re-enable voice prompt later
                        // TrackingSpeak = true;
                        return;
                    }

                    switch (startingState)
                    {
                        case SlewType.SlewNone:
                        case SlewType.SlewSettle:
                        case SlewType.SlewMoveAxis:
                            break;
                        case SlewType.SlewRaDec:
                            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                            {
                                // update TargetRa and TargetDec after slewing with offset rates as per ASCOM spec
                                if (SkyPredictor.RatesSet)
                                {
                                    var targetRaDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                                    TargetRa = targetRaDec[0];
                                    TargetDec = targetRaDec[1];
                                }

                                // use tracking to complete slew for Alt Az mounts
                                SkyPredictor.Set(TargetRa, TargetDec);
                                _tracking = true;
                                _trackingMode = TrackingMode.AltAz;
                                SetTracking();
                                var sw = Stopwatch.StartNew();
                                // wait before completing async slew, double time for low resolution mounts 
                                var highResMount = Conversions.StepPerArcSec(Math.Min(StepsPerRevolution[0], StepsPerRevolution[1])) > 5;
                                var waitTime = highResMount ? 2 * SkySettings.AltAzTrackingUpdateInterval : 4 * SkySettings.AltAzTrackingUpdateInterval;
                                while (sw.ElapsedMilliseconds < waitTime)
                                {
                                    if (_ctsGoTo?.IsCancellationRequested == true)
                                    {
                                        // Stop current Alt Az tracking timed action
                                        StopAltAzTrackingTimer();
                                        // Prevent re-enabling by this thread
                                        trackingState = false;
                                        // Stop tracking motion 
                                        StopAxes();
                                        break;
                                    }
                                    else
                                        Thread.Sleep(100);
                                }
                            }

                            break;
                        case SlewType.SlewAltAz:
                            break;
                        case SlewType.SlewPark:
                            trackingState = false;
                            AtPark = true;
                            SkyPredictor.Reset();
                            break;
                        case SlewType.SlewHome:
                            trackingState = false;
                            SkyPredictor.Reset();
                            break;
                        case SlewType.SlewHandpad:
                            // ensure tracking if enabled has the correct target
                            SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
                            break;
                        case SlewType.SlewComplete:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MonitorLog.GetCurrentMethod(),
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message =
                            $"{SlewState} finished|code|{returnCode}|{Utilities.HoursToHMS(RightAscensionXForm, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(DeclinationXForm, " ", ":", "", 2)}|Actual|{ActualAxisX}|{ActualAxisY}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    SlewState = SlewType.SlewNone;
                    // ToDo re-enable voice prompt later
                    // SpeakSlewEnd(startingState);
                    Tracking = trackingState;
                    // ToDo re-enable voice prompt later
                    // TrackingSpeak = true;
                }
                else
                {
                    // Handle can't slew    
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = "GoTo coordinates outside axes limits"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    SlewState = SlewType.SlewNone;
                    // ToDo re-enable voice prompt later
                    // SpeakSlewEnd(startingState);
                    Tracking = false;
                    // ToDo re-enable voice prompt later
                    // TrackingSpeak = true;
                }
            }
            catch (Exception ex)
            {
                // OperationCanceledException thrown by SimGoTo or SkyGoTo
                // AggregateException with base OperationCanceledException thrown by PrecisionGoTo
                var cancelled = ex is OperationCanceledException || ex.GetBaseException() is OperationCanceledException;
                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = cancelled ? "GoTo cancelled by command" : "GoTo failed, axes stopped"
                };
                MonitorLog.LogToMonitor(monitorItem);
                // Reset rates and axis movement
                _rateMoveAxes = new Vector(0, 0);
                MoveAxisActive = false;
                _rateRaDec = new Vector(0, 0);
                // Stop axes
                switch (SkySettings.Mount)
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

                SlewState = SlewType.SlewNone;
                Tracking = trackingState;
                // Some unknown exception
                if (!cancelled)
                    MountError = new Exception($"GoTo Async Error|{ex.Message}");
            }
            finally
            {
                _ctsGoTo?.Dispose();
                _ctsGoTo = null;
            }
        }

        /// <summary>
        /// Abort Slew in a normal motion
        /// </summary>
        public static void AbortSlew(bool speak, EventWaitHandle abortSlewStarted = null)
        {
            if (!IsMountRunning)
            {
                if (abortSlewStarted != null) abortSlewStarted.Set();
                return;
            }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{SlewState}|{Tracking}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            if (abortSlewStarted != null) abortSlewStarted.Set();
            //IsSlewing = false;
            var tracking = Tracking || SlewState == SlewType.SlewRaDec || MoveAxisActive;
            Tracking = false; //added back in for spec "Tracking is returned to its pre-slew state"
            CancelAllAsync();
            // Stop all MoveAxis commands
            MoveAxisActive = false;
            RateMovePrimaryAxis = 0.0;
            RateMoveSecondaryAxis = 0.0;
            _rateRaDec = new Vector(0, 0);

            switch (SkySettings.Mount)
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
            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
            {
                AxesStopValidate();
                // wait for the move to stop - physical overrun
                //var trackingRate = SkyGetRate();
                AxesRateOfChange.Reset();
                do
                {
                    // Update mount velocity
                    MountPositionUpdated = false;
                    UpdateSteps();
                    while (!MountPositionUpdated) Thread.Sleep(50);
                    AxesRateOfChange.Update(_actualAxisX, _actualAxisY, HiResDateTime.UtcNow);
                } while (AxesRateOfChange.AxisVelocity.Length > 0);
                SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
            // ToDo re-enable voice prompt later
            // TrackingSpeak = false;
            Tracking = tracking;
            // TrackingSpeak = true;

            // if (speak) { Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceAbortSlew"].ToString()); }
        }

        /// <summary>
        /// Stop Axes in a normal motion
        /// </summary>
        public static void StopAxes()
        {
            if (!IsMountRunning) { return; }

            AutoHomeStop = true;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{SlewState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CancelAllAsync();
            // Stop all MoveAxis and do not restore tracking
            MoveAxisActive = false;
            RateMovePrimaryAxis = 0.0;
            RateMoveSecondaryAxis = 0.0;
            _rateRaDec = new Vector(0, 0);

            if (!AxesStopValidate())
            {
                switch (SkySettings.Mount)
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
            }

            SlewState = SlewType.SlewNone;
            Tracking = false;
            // ToDo re-enable voice prompt later
            // Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceStop"].ToString());
        }

        #endregion

        #region Parking & Homing

        /// <summary>
        /// Goto home slew
        /// </summary>
        public static void GoToHome()
        {
            if (AtHome || SlewState == SlewType.SlewHome) return;

            Tracking = false;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "Slew to Home"
            };
            MonitorLog.LogToMonitor(monitorItem);
            SlewMount(new Vector(_homeAxes.X, _homeAxes.Y), SlewType.SlewHome);
        }

        /// <summary>
        /// Goto park slew
        /// </summary>
        public static void GoToPark()
        {
            Tracking = false;


            // get position selected could be set from UI or AsCom
            var ps = ParkSelected;
            if (ps == null) { return; }
            if (double.IsNaN(ps.X)) { return; }
            if (double.IsNaN(ps.Y)) { return; }
            SetParkAxis(ps.Name, ps.X, ps.Y);

            // Store for startup default position
            SkySettings.ParkAxes = new[] { ps.X, ps.Y };
            SkySettings.ParkName = ps.Name;

            Tracking = false;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{ps.Name}|{ps.X}|{ps.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            SlewMount(new Vector(ps.X, ps.Y), SlewType.SlewPark);
        }

        /// <summary>
        /// Park position selected set from UI or ASCOM, set using deep copy
        /// </summary>
        public static ParkPosition ParkSelected
        {
            get
            {
                if (_parkSelected == null)
                    _parkSelected = new ParkPosition("Park", SkySettings.ParkAxes[0], SkySettings.ParkAxes[1]);
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

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value.Name}|{value.X}|{value.Y}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Create new park position from internal position
        /// </summary>
        public static void SetParkAxis(string name)
        {
            if (string.IsNullOrEmpty(name)) { name = "Empty"; }

            // convert current position
            var park = Axes.MountAxis2Mount();
            if (park == null) { return; }

            var p = new ParkPosition { Name = name, X = park[0], Y = park[1] };
            ParkSelected = p;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{park[0]}|{park[1]}|{AppAxisX}|{AppAxisY}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // ToDo re-enable voice prompt later
            // Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceParkSet"].ToString());
        }

        /// <summary>
        /// Create park position, expects MountAxis2Mount already done
        /// </summary>
        private static void SetParkAxis(string name, double x, double y)
        {
            if (string.IsNullOrEmpty(name)) name = "Empty";

            var p = new ParkPosition { Name = name, X = x, Y = y };
            ParkSelected = p;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{x}|{y}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Auto home, Slew home based on mount's home sensor
        /// </summary>
        public static async void AutoHomeAsync(int degreeLimit = 100, int offSetDec = 0)
        {
            try
            {
                if (!IsMountRunning) return;
                IsAutoHomeRunning = true;
                LastAutoHomeError = null;

                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Started"
                });

                if (degreeLimit < 20) degreeLimit = 100;
                AutoHomeProgressBar = 0;
                var encoderTemp = SkySettings.Encoders;
                if (Tracking) Tracking = false;
                // ToDo re-enable voice prompt later
                //Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["btnAutoHomeStart"].ToString());
                //Synthesizer.VoicePause = true;

                AutoHomeResult raResult, decResult;

                switch (SkySettings.Mount)
                {
                    case MountType.Simulator:
                        var autoHomeSim = new AutoHomeSim();
                        raResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis1, degreeLimit));
                        AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    case MountType.SkyWatcher:
                        var autoHomeSky = new AutoHomeSky();
                        raResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis1, degreeLimit));
                        AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                SkySettings.Encoders = encoderTemp;
                StopAxes();

                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Complete: {raResult}|{decResult}"
                });

                if (raResult == AutoHomeResult.Success && decResult == AutoHomeResult.Success)
                {
                    ReSyncAxes(new ParkPosition("AutoHome", SkySettings.AutoHomeAxisX, SkySettings.AutoHomeAxisY), false);
                    // ToDo re-enable voice prompt later
                    // Synthesizer.VoicePause = false;
                    Thread.Sleep(1500);
                    // Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["btnAutoHomeComplete"].ToString());
                }
                else if (raResult == AutoHomeResult.StopRequested || decResult == AutoHomeResult.StopRequested)
                {
                    // Cancelled by user, do not throw
                    return;
                }
                else
                {
                    string raMsg = GetAutoHomeResultMessage(raResult, "RA");
                    string decMsg = GetAutoHomeResultMessage(decResult, "Dec");
                    var ex = new Exception($"Incomplete: {raMsg} ({raResult}), {decMsg} ({decResult})");
                    LastAutoHomeError = ex;
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Error,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{ex.Message}|{ex.StackTrace}"
                });
                LastAutoHomeError = ex;
                MountError = ex;
            }
            finally
            {
                AutoHomeProgressBar = 100;
                IsAutoHomeRunning = false;
                // ToDo re-enable voice prompt later
                // Synthesizer.VoicePause = false;
            }
        }

        private static string GetAutoHomeResultMessage(AutoHomeResult result, string axisName)
        {
            switch (result)
            {
                case AutoHomeResult.Success:
                    return $"{axisName} homed successfully";
                case AutoHomeResult.FailedHomeSensorReset:
                    return $"{axisName} failed home sensor reset";
                case AutoHomeResult.HomeSensorNotFound:
                    return $"{axisName} home sensor not found";
                case AutoHomeResult.TooManyRestarts:
                    return $"{axisName} too many restarts";
                case AutoHomeResult.HomeCapabilityCheckFailed:
                    return $"{axisName} home capability check failed";
                case AutoHomeResult.StopRequested:
                    return $"{axisName} auto home stopped";
                default:
                    return $"{axisName} unknown error";
            }
        }

        #endregion

        #region Syncing

        /// <summary>
        /// Sync using az/alt
        /// </summary>
        /// <param name="targetAzimuth"></param>
        /// <param name="targetAltitude"></param>
        public static void SyncToAltAzm(double targetAzimuth, double targetAltitude)
        {
            if (!IsMountRunning) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{targetAzimuth}|{targetAltitude}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var trackingstate = Tracking;
            if (trackingstate)
            {
                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;
                Tracking = false;
            }

            _altAzSync = new Vector(targetAltitude, targetAzimuth);
            switch (SkySettings.Mount)
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

            MountPositionUpdated = false;
            while (!MountPositionUpdated)
            {
                Thread.Sleep(50);
            }

            if (trackingstate)
            {
                Tracking = true;
                // ToDo re-enable voice prompt later
                // TrackingSpeak = true;
            }

            // ToDo re-enable voice prompt later
            // Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceSyncAz"].ToString());
        }

        /// <summary>
        /// Sync using ra/dec
        /// </summary>
        public static void SyncToTargetRaDec()
        {
            if (!IsMountRunning) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $" {TargetRa}|{TargetDec}|{Tracking}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var trackingstate = Tracking;
            if (trackingstate)
            {
                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;
                Tracking = false;
            }

            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes);
                    // ToDo: Remove if not needed
                    //if (AlignmentModel.IsAlignmentOn)
                    //{
                    //    AddAlignmentPoint();
                    //}
                    //else
                    //{
                        SimTasks(MountTaskName.SyncTarget);
                    //}
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
                    // ToDo: Remove if not needed
                    //if (AlignmentModel.IsAlignmentOn)
                    //{
                    //    AddAlignmentPoint();
                    //}
                    //else
                    //{
                        SkyTasks(MountTaskName.SyncTarget);
                    //}

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            MountPositionUpdated = false;
            while (!MountPositionUpdated)
            {
                Thread.Sleep(50);
            }

            if (trackingstate)
            {
                if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                {
                    // set up tracking for Alt Az
                    SkyPredictor.Set(TargetRa, TargetDec);
                    _tracking = true;
                    _trackingMode = TrackingMode.AltAz;
                    SetTracking();
                }
                Tracking = true;
                // ToDo re-enable voice prompt later
                // TrackingSpeak = true;
            }

            // ToDo re-enable voice prompt later
            // Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceSyncCoords"].ToString());
        }

        /// <summary>
        /// Check if sync is too far from RaDec position
        /// </summary>
        /// <param name="ra"></param>
        /// <param name="dec"></param>
        /// <returns>False is out of limit</returns>
        public static bool CheckRaDecSyncLimit(double ra, double dec)
        {
            if (!SkySettings.SyncLimitOn) { return true; }
            if (SkySettings.NoSyncPastMeridian) { return false; } // add more checks later if needed

            //convert ra dec to mount XY positions
            var xy = Axes.RaDecToAxesXy(new[] { ra, dec });
            //convert to app coordinates
            var target = Axes.AxesMountToApp(GetSyncedAxes(xy));

            //get current mount position in app coordinates
            var current = new[] { _appAxisX, _appAxisY };
            //compare ra dec / az alt to current mount position
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > SkySettings.SyncLimit || Math.Abs(b) > SkySettings.SyncLimit);
            if (ret) return true;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{SkySettings.SyncLimit}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            return false;

        }

        /// <summary>
        /// Check if sync is too far from AltAz position
        /// </summary>
        /// <param name="alt"></param>
        /// <param name="az"></param>
        /// <returns>False is out of limit</returns>
        public static bool CheckAltAzSyncLimit(double alt, double az)
        {
            if (!SkySettings.SyncLimitOn) { return true; }
            if (SkySettings.NoSyncPastMeridian) { return false; } // add more checks later if needed

            //convert ra dec to mount XY positions
            var xy = Axes.AzAltToAxesXy(new[] { az, alt });
            //convert to app coordinates
            var target = Axes.AxesMountToApp(GetSyncedAxes(xy));

            //get current mount position in app coordinates
            var current = new[] { _appAxisX, _appAxisY };

            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
            {
                target[0] = az;
                target[1] = alt;
                current[0] = Range.Range360(_appAxisX);
                current[1] = _appAxisY;
            }

            //compare ra dec to current position
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > SkySettings.SyncLimit || Math.Abs(b) > SkySettings.SyncLimit);

            if (ret) return true;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{SkySettings.SyncLimit}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            return false;
        }

        #endregion

        #region Pulse Guiding

        /// <summary>
        /// Execute single axis pulse guide for AltAz using predictor
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="guideRate"></param>
        /// <param name="duration"></param>
        /// <param name="pulseGoTo"></param>
        /// <param name="token"></param>
        private static void PulseGuideAltAz(int axis, double guideRate, int duration, Action<CancellationToken> pulseGoTo, CancellationToken token)
        {
            Task.Run(() =>
            {
                var pulseStartTime = HiResDateTime.UtcNow;
                // stop alt az tracking and set predictor Ra and Dec ready for pulse go to action
                switch (axis)
                {
                    case 0:
                        if (!IsPulseGuidingDec)
                            StopAltAzTrackingTimer();
                        else
                            _ctsPulseGuideDec.Cancel();
                        SkyPredictor.Set(SkyPredictor.Ra - duration * 0.001 * guideRate / SiderealRate, SkyPredictor.Dec);
                        break;
                    case 1:
                        if (!IsPulseGuidingRa)
                            StopAltAzTrackingTimer();
                        else
                            _ctsPulseGuideRa.Cancel();
                        SkyPredictor.Set(SkyPredictor.Ra, SkyPredictor.Dec + duration * guideRate * 0.001);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
                }
                // setup to log and graph the pulse
                var pulseEntry = new PulseEntry();
                if (MonitorPulse)
                {
                    pulseEntry.Axis = axis;
                    pulseEntry.Duration = duration;
                    pulseEntry.Rate = guideRate;
                    pulseEntry.StartTime = pulseStartTime;
                }
                // execute pulse
                pulseGoTo(token);
                // pulse movement finished or cancelled so resume tracking
                SetTracking();
                // wait for pulse duration so completion variable IsPulseGuiding remains true 
                var waitTime = (int)(pulseStartTime.AddMilliseconds(duration) - HiResDateTime.UtcNow).TotalMilliseconds;
                var updateInterval = Math.Max(duration / 20, 50);
                if (waitTime > 0)
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds < waitTime && !token.IsCancellationRequested)
                    {
                        Thread.Sleep(updateInterval);
                        UpdateSteps(); // Process positions while waiting
                    }
                }
                // log and graph pulse
                if (MonitorPulse)
                {
                    MonitorLog.LogToMonitor(pulseEntry);
                }
                if (token.IsCancellationRequested)
                {
                    var monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = MonitorLog.GetCurrentMethod(),
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Axis|{axis}|Async operation cancelled"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                // set pulse guiding status
                switch (axis)
                {
                    case 0:
                        IsPulseGuidingRa = false;
                        break;
                    case 1:
                        IsPulseGuidingDec = false;
                        break;
                }
            });
        }

        /// <summary>
        /// Pulse commands
        /// </summary>
        /// <param name="direction">GuideDirections</param>
        /// <param name="duration">in milliseconds</param>
        /// /// <param name="altRate">alternate rate to replace the guide rate</param>
        public static void PulseGuide(GuideDirection direction, int duration, double altRate)
        {
            if (!IsMountRunning) { throw new Exception("Mount not running"); }

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{direction}|{duration}" };
            MonitorLog.LogToMonitor(monitorItem);

            var useAltRate = Math.Abs(altRate) > 0;

            switch (direction)
            {
                case GuideDirection.North:
                case GuideDirection.South:
                    if (duration == 0)
                    {
                        IsPulseGuidingDec = false;
                        return;
                    }
                    IsPulseGuidingDec = true;
                    HcResetPrevMove(MountAxis.Dec);
                    var decGuideRate = useAltRate ? altRate : Math.Abs(GuideRateDec);
                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (direction == GuideDirection.South) { decGuideRate = -decGuideRate; }
                            break;
                        case AlignmentMode.Polar:
                            if (SideOfPier == PointingState.Normal)
                            {
                                if (direction == GuideDirection.North) { decGuideRate = -decGuideRate; }
                            }
                            else
                            {
                                if (direction == GuideDirection.South) { decGuideRate = -decGuideRate; }
                            }
                            if (PolarMode == PolarMode.Left) decGuideRate = -decGuideRate; // Swap direction because primary OTA is flipped
                            break;
                        case AlignmentMode.GermanPolar:
                            if (SideOfPier == PointingState.Normal)
                            {
                                if (direction == GuideDirection.North) { decGuideRate = -decGuideRate; }
                            }
                            else
                            {
                                if (direction == GuideDirection.South) { decGuideRate = -decGuideRate; }
                            }
                            break;
                    }

                    // Direction switched add backlash compensation
                    var decBacklashAmount = 0;
                    if (direction != LastDecDirection) decBacklashAmount = SkySettings.DecBacklash;
                    LastDecDirection = direction;
                    _ctsPulseGuideDec = new CancellationTokenSource();

                    switch (SkySettings.Mount)
                    {
                        case MountType.Simulator:
                            switch (SkySettings.AlignmentMode)
                            {
                                case AlignmentMode.AltAz:
                                    PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, SimPulseGoto, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.Polar:
                                    if (!SouthernHemisphere) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new CmdAxisPulse(0, Axis.Axis2, decGuideRate, duration,
                                        _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    if (!SouthernHemisphere) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new CmdAxisPulse(0, Axis.Axis2, decGuideRate, duration,
                                        _ctsPulseGuideDec.Token);
                                    break;
                                default:
                                    break;
                            }
                            break;
                        case MountType.SkyWatcher:
                            switch (SkySettings.AlignmentMode)
                            {
                                case AlignmentMode.AltAz:
                                    PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, SkyPulseGoto, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.Polar:
                                    if (!SouthernHemisphere) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new SkyAxisPulse(0, Axis.Axis2, decGuideRate, duration, decBacklashAmount, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    _ = new SkyAxisPulse(0, Axis.Axis2, decGuideRate, duration, decBacklashAmount, _ctsPulseGuideDec.Token);
                                    break;
                                default:
                                    break;
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                case GuideDirection.East:
                case GuideDirection.West:
                    if (duration == 0)
                    {
                        IsPulseGuidingRa = false;
                        return;
                    }
                    IsPulseGuidingRa = true;
                    HcResetPrevMove(MountAxis.Ra);
                    var raGuideRate = useAltRate ? altRate : Math.Abs(GuideRateRa);
                    if (SkySettings.AlignmentMode != AlignmentMode.AltAz)
                    {
                        if (SouthernHemisphere)
                        {
                            if (direction == GuideDirection.West) { raGuideRate = -raGuideRate; }
                        }
                        else
                        {
                            if (direction == GuideDirection.East) { raGuideRate = -raGuideRate; }
                        }
                    }
                    else
                    {
                        if (direction == GuideDirection.East) { raGuideRate = -raGuideRate; }
                    }

                    _ctsPulseGuideRa = new CancellationTokenSource();
                    switch (SkySettings.Mount)
                    {
                        case MountType.Simulator:
                            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                            {
                                PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, SimPulseGoto, _ctsPulseGuideRa.Token);
                            }
                            else
                            {
                                _ = new CmdAxisPulse(0, Axis.Axis1, raGuideRate, duration, _ctsPulseGuideRa.Token);
                            }

                            break;
                        case MountType.SkyWatcher:
                            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                            {
                                PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, SkyPulseGoto, _ctsPulseGuideRa.Token);
                            }
                            else
                            {
                                _ = new SkyAxisPulse(0, Axis.Axis1, raGuideRate, duration, 0, _ctsPulseGuideRa.Token);
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        #endregion

        #region MoveAxis Support

        /// <summary>
        /// Reports to driver is axis can move
        /// </summary>
        /// <param name="axis"></param>
        /// <returns></returns>
        public static bool CanMoveAxis(TelescopeAxis axis)
        {
            var ax = 0;
            switch (axis)
            {
                case TelescopeAxis.Primary:
                    ax = 1;
                    break;
                case TelescopeAxis.Secondary:
                    ax = 2;
                    break;
                case TelescopeAxis.Tertiary:
                    ax = 3;
                    break;
            }

            return ax != 0 && ax <= SkySettings.NumMoveAxis;
        }

        /// <summary>
        /// Status of primary axis move
        /// </summary>
        public static bool MovePrimaryAxisActive => _rateMoveAxes.X != 0.0;

        /// <summary>
        /// Status of secondary axis move
        /// </summary>
        public static bool MoveSecondaryAxisActive => _rateMoveAxes.Y != 0.0;

        public static bool MoveAxisActive
        {
            get => _moveAxisActive;
            set
            {
                _moveAxisActive = value;
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Position Calculations
        /// <summary>
        /// 
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static double[] GetAlternatePosition(double[] position)
        {
            switch (SkySettings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return GetAlternatePositionAltAz(position);
                case AlignmentMode.Polar:
                    return GetAlternatePositionPolar(position);
                case AlignmentMode.GermanPolar:
                    return GetAlternatePositionGEM(position);
                default:
                    throw new ArgumentOutOfRangeException(nameof(SkySettings.AlignmentMode), SkySettings.AlignmentMode, "Unsupported alignment mode for alternate position calculation.");
            }
        }

        /// <summary>
        /// Calculates which pair of axis positions is closer to a given position
        /// </summary>
        /// <param name="position">X axis position</param>
        /// <param name="a">First pair of positions</param>
        /// <param name="b">Seconds pair of positions</param>
        /// <returns>a or b as string</returns>
        private static string ChooseClosestPosition(double position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Abs(a[0] - position);
            var val2 = Math.Abs(b[0] - position);
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        /// <summary>
        /// Calculates which pair of axis positions is closer to a given position
        /// </summary>
        /// <param name="position">X and Y axis positions</param>
        /// <param name="a">First pair of positions</param>
        /// <param name="b">Seconds pair of positions</param>
        /// <returns>a or b as string</returns>
        private static string ChooseClosestPositionPolar(IReadOnlyList<double> position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Max(Math.Abs(a[0] - position[0]), Math.Abs(a[1] - position[1]));
            var val2 = Math.Max(Math.Abs(b[0] - position[0]), Math.Abs(b[1] - position[1]));
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        /// <summary>
        /// Within the meridian limits will check for closest slew
        /// </summary>
        /// <param name="position"></param>
        /// <returns>axis position that is closest</returns>
        private static double[] GetAlternatePositionGEM(double[] position)
        {
            // Check Forced flip for a goto
            var flipGoto = FlipOnNextGoto;
            FlipOnNextGoto = false;

            // See if the target is within flip angle limits
            if (!IsWithinFlipLimits(position)) { return null; }
            var alt = Axes.GetAltAxisPosition(position);
            if (!IsWithinFlipLimits(alt)) { return null; }

            var cl = ChooseClosestPosition(ActualAxisX, position, alt);  //choose the closest angle to slew 
            if (flipGoto) // implement the forced flip for a goto
            {
                cl = cl == "a" ? "b" : "a"; //choose the farthest angle to slew which will flip
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{ActualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }

            if (cl != "b") { return null; }
            return alt;
        }

        /// <summary>
        /// Within hardware limits get alternate position
        /// </summary>
        /// <param name="position">Ra and Dec position</param>
        /// <returns>null or alternate position</returns>
        private static double[] GetAlternatePositionAltAz(double[] position)
        {
            // Check Forced flip for a goto
            var flipGoto = FlipOnNextGoto;
            FlipOnNextGoto = false;

            // See if the target is within flip angle limits
            if (!IsWithinFlipLimits(position)) { return null; }
            var alt = Axes.GetAltAxisPosition(position);
            var cl = ChooseClosestPosition(ActualAxisX, position, alt);  //choose the closest angle to slew 
            if (flipGoto) // implement the forced flip for a goto
            {
                cl = cl == "a" ? "b" : "a"; //choose the farthest angle to slew which will flip
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{ActualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }

            if (cl != "b") { return null; }

            return alt;
        }

        /// <summary>
        /// Within hardware limits and flip angle get alternate position
        /// </summary>
        /// <param name="position">Ra and Dec position</param>
        /// <returns>null or alternate position</returns>
        private static double[] GetAlternatePositionPolar(double[] position)
        {
            // Check Forced flip for a goto
            var flipGoto = FlipOnNextGoto;
            FlipOnNextGoto = false;

            var alt = Axes.GetAltAxisPosition(position);
            alt[0] = Range.Range180(alt[0]); // convert to polar position

            // Check target and altTarget are within hardware limits
            var altOk = IsTargetWithinLimits(alt);
            var posOk = IsTargetWithinLimits(position);

            if (!altOk) return null; // alternate target position not within limits, return null
            if (posOk && altOk)
            {
                var cl = ChooseClosestPositionPolar(new[] { ActualAxisX, ActualAxisY }, position, alt);  //choose the closest angle to slew 
                if (flipGoto) // implement the forced flip for a goto
                {
                    cl = cl == "a" ? "b" : "a"; //choose the farthest angle to slew which will flip
                    var monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"flip|{cl}|{ActualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                if (cl != "b") { return null; }

                return alt;
            }
            return alt;
        }

        /// <summary>
        /// Determine which SideOfPier for a given Ra/Dec coordinate
        /// </summary>
        /// <remarks>Ra/Dec must already be converted using Transforms.CordTypeToInternal.</remarks>
        /// <remarks>Checks if a flip it required for coordinates</remarks>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// <returns></returns>
        public static PointingState DetermineSideOfPier(double rightAscension, double declination)
        {
            var sop = SideOfPier;
            if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
            {
                return PointingState.Unknown;
            }

            var flipReq = Axes.IsFlipRequired(new[] { rightAscension, declination });

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Ra:{rightAscension}|Dec:{declination}|Flip:{flipReq}|SoP:{sop}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            switch (sop)
            {
                case PointingState.Normal:
                    return flipReq ? PointingState.ThroughThePole : PointingState.Normal;
                case PointingState.ThroughThePole:
                    return flipReq ? PointingState.Normal : PointingState.ThroughThePole;
                case PointingState.Unknown:
                    return PointingState.Unknown;
                default:
                    return PointingState.Unknown;
            }
        }

        #endregion

        #region Tracking & Rates

        /// <summary>
        /// Set tracking on or off
        /// </summary>
        private static void SetTracking()
        {
            if (!IsMountRunning) { return; }

            double rateChange = 0;
            Vector rate;
            // Set rate change for tracking mode
            switch (_trackingMode)
            {
                case TrackingMode.Off:
                    break;
                case TrackingMode.AltAz:
                    rateChange = CurrentTrackingRate();
                    break;
                case TrackingMode.EqN:
                    rateChange = CurrentTrackingRate();
                    break;
                case TrackingMode.EqS:
                    rateChange = -CurrentTrackingRate();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor);
                                if (!AltAzTimerIsRunning) StartAltAzTrackingTimer();
                            }
                            else
                            {
                                if (AltAzTimerIsRunning) StopAltAzTrackingTimer();
                                _skyTrackingRate.X = 0.0;
                                _skyTrackingRate.Y = 0.0;
                            }
                            rate = SkyGetRate();
                            // Tracking applied unless MoveAxis is active
                            if (!MovePrimaryAxisActive)
                                _ = new CmdAxisTracking(0, Axis.Axis1, rate.X);
                            if (!MoveSecondaryAxisActive)
                                _ = new CmdAxisTracking(0, Axis.Axis2, rate.Y);
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            if (!MovePrimaryAxisActive) // Set current tracking rate and RA tracking rate offset (0 if not sidereal)
                            {
                                _ = new CmdAxisTracking(0, Axis.Axis1, rateChange);
                            }
                            _ = new CmdRaDecRate(0, Axis.Axis1, GetRaRateDirection(_rateRaDec.X));
                            if (!MoveSecondaryAxisActive) // Set Dec tracking rate offset (0 if not sidereal)
                            {
                                _ = new CmdRaDecRate(0, Axis.Axis2, GetDecRateDirection(_rateRaDec.Y));
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                case MountType.SkyWatcher:
                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor);
                                if (!AltAzTimerIsRunning) StartAltAzTrackingTimer();
                            }
                            else
                            {
                                if (AltAzTimerIsRunning) StopAltAzTrackingTimer();
                                _skyTrackingRate.X = 0.0;
                                _skyTrackingRate.Y = 0.0;
                            }

                            // Get current tracking  including RA and Dec offsets
                            // Tracking applied unless MoveAxis is active
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            _skyTrackingRate.X = rateChange;
                            _skyTrackingRate.Y = 0.0;
                            // Get current tracking including RA and Dec offsets
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    rate = SkyGetRate(); // Get current tracking  including RA and Dec offsets
                    if (!MovePrimaryAxisActive)
                        _ = new SkyAxisSlew(0, Axis.Axis1, rate.X);
                    if (!MoveSecondaryAxisActive)
                        _ = new SkyAxisSlew(0, Axis.Axis2, rate.Y);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // don't log if pec is on
            if (PecOn) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{_trackingMode}|{rateChange * 3600}|{PecBinNow}|{SkyTrackingOffset[0]}|{SkyTrackingOffset[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Set tracking mode based on alignment mode
        /// </summary>
        public static void SetTrackingMode()
        {
            switch (SkySettings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    _trackingMode = TrackingMode.AltAz;
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    _trackingMode = SouthernHemisphere ? TrackingMode.EqS : TrackingMode.EqN;
                    break;
            }
        }

        /// <summary>
        /// Calculates the current RA tracking rate used in arc seconds per second
        /// </summary>
        /// <returns></returns>
        public static double CurrentTrackingRate()
        {
            double rate;
            switch (SkySettings.TrackingRate)
            {
                case DriveRate.Sidereal:
                    rate = SkySettings.SiderealRate;
                    break;
                case DriveRate.Solar:
                    rate = SkySettings.SolarRate;
                    break;
                case DriveRate.Lunar:
                    rate = SkySettings.LunarRate;
                    break;
                case DriveRate.King:
                    rate = SkySettings.KingRate;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (rate < SiderealRate * 2 & rate != 0) //add any custom gearing offset
            {
                rate += TrackingOffsetRaRate;
            }

            //Implement Pec
            if (PecOn && Tracking && PecBinNow != null && !double.IsNaN(PecBinNow.Item2))
            {
                // safety check to make sure factor isn't too big
                if (Math.Abs(PecBinNow.Item2 - 1) < .04)
                {
                    rate *= PecBinNow.Item2;
                }
            }
            rate /= 3600;
            if (SkySettings.RaTrackingOffset <= 0) { return rate; }
            var offsetrate = rate * (Convert.ToDouble(SkySettings.RaTrackingOffset) / 100000);
            rate += offsetrate;
            return rate;
        }

        /// <summary>
        /// Sets up offsets from the selected tracking rate
        /// </summary>
        internal static void SetGuideRates()
        {
            var rate = CurrentTrackingRate();
            _guideRate.X = rate * SkySettings.GuideRateOffsetX;
            _guideRate.Y = rate * SkySettings.GuideRateOffsetY;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{_guideRate.X * 3600}|{_guideRate.Y * 3600}"
            };
            MonitorLog.LogToMonitor(monitorItem);

        }

        /// <summary>
        /// Convert the move rate in hour angle and declination to a move rate in altitude and azimuth
        /// </summary>
        /// <param name="haRate">The ha rate.</param>
        /// <param name="decRate">The dec rate </param>
        /// <param name="targetDec"></param>
        /// <returns></returns>
        private static Vector ConvertRateToAltAz(double haRate, double decRate, double targetDec)
        {
            var change = new Vector(0, 0);
            if (double.IsNaN(targetDec)) { return change; }

            var azimuthRate = new Vector(); // [X,Y] = [ha, dec]
            var altitudeRate = new Vector(); // [X,Y] = [ha, dec]

            var latRad = Principles.Units.Deg2Rad(SkySettings.Latitude);
            var azmRad = Principles.Units.Deg2Rad(Azimuth);
            var haRad = Principles.Units.Hrs2Rad(Lha);
            var decRad = Principles.Units.Deg2Rad(targetDec);
            var zenithAngle = Principles.Units.Deg2Rad((90 - Altitude)); // in radians

            // get the azimuth and altitude geometry factors for changing ha
            altitudeRate.X = Sin(azmRad) * Cos(latRad);
            // fails at zenith so set a very large value, the limit check will trap this
            azimuthRate.X =
                Abs(Altitude - 90.0) > 0
                    ? (Sin(latRad) -
                       Cos(latRad) * Cos(azmRad) / Tan(zenithAngle))
                    :
                    //Abs(Altitude - 90.0) > 0
                    //    ? (Sin(latRad) * Sin(zenithAngle) -
                    //       Cos(latRad) * Cos(zenithAngle) * Cos(azmRad)) / Sin(zenithAngle)
                    //_altAzm.Y != 90.0 ?(Math.Sin(latRad) * Math.Sin(zenithAngle) - Math.Cos(latRad) * Math.Cos(zenithAngle) * Math.Cos(azmRad)) / Math.Sin(zenithAngle) :
                    Azimuth >= 90 && Azimuth <= 270
                        ? 10000
                        : -10000;

            // get the azimuth and altitude geometry factors for changing dec
            // fails at zenith so set a very large value, the limit check will trap this
            altitudeRate.Y =
                Abs(Altitude - 90.0) > 0
                ? (Sin(decRad) * Sin(latRad) -
                   Sin(decRad) * Cos(haRad) * Cos(latRad)) / Sin(zenithAngle)
                :
                Azimuth >= 90 && Azimuth <= 270
                    ? 10000
                    : -10000;
            // fails at zenith so set a very large value, the limit check will trap this
            azimuthRate.Y =
                Abs(Altitude - 90.0) > 0
                ? (Sin(zenithAngle) * Sin(haRad) * Cos(decRad) +
                   Sin(decRad) * Cos(haRad) * Cos(latRad) -
                   Cos(decRad) * Sin(latRad)) /
                  ((Sin(decRad) * Cos(latRad) -
                   Cos(decRad) * Cos(haRad) * Sin(latRad)) * Sin(zenithAngle))
                :
                Azimuth >= 90 && Azimuth <= 270
                    ? 10000
                    : -10000;

            // calculate the rate of change in altitude and azimuth using the hour angle and dec change rate and geometry factors.
            change.Y = altitudeRate.X * haRate + altitudeRate.Y * decRate;
            change.X = azimuthRate.X * haRate + azimuthRate.Y * decRate;
            // stop the secondary going past the vertical
            if (change.Y > 90 - Altitude) { change.Y = 0; }
            // limit the primary to the maximum slew rate
            if (change.X < -SlewSpeedEight) { change.X = -SlewSpeedEight; }
            if (change.X > SlewSpeedEight) { change.X = SlewSpeedEight; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"change:{change.X}|{change.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            return change;
        }

        /// <summary>
        /// Event handler for timed update AltAz tracking
        /// </summary>
        internal static void AltAzTrackingTimerEvent(object sender, EventArgs e)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MonitorLog.GetCurrentMethod(),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"TimerID|{_altAzTrackingTimer?.TimerID}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            // timer must be running to update tracking
            if (_altAzTrackingTimer?.IsRunning == true)
            {
                // handle timer race condition triggering handler
                if (Interlocked.CompareExchange(ref _altAzTrackingLock, -1, 0) == 0)
                {
                    SetTracking();
                    // Release the lock
                    _altAzTrackingLock = 0;
                }
            }
        }

        /// <summary>
        /// Stop Alt Az tracking timer
        /// </summary>
        private static void StopAltAzTrackingTimer()
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MonitorLog.GetCurrentMethod(),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"TimerID|{_altAzTrackingTimer?.TimerID}|Running|{_altAzTrackingTimer?.IsRunning}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            if (_altAzTrackingTimer != null)
            {
                _altAzTrackingTimer.Tick -= AltAzTrackingTimerEvent;
                if (_altAzTrackingTimer.IsRunning)
                {
                    _altAzTrackingTimer.Stop();
                }
                _altAzTrackingTimer.Dispose();
                _altAzTrackingTimer = null;
            }
        }

        /// <summary>
        /// Start Alt Az tracking timer
        /// </summary>
        private static void StartAltAzTrackingTimer()
        {
            var timerId = _altAzTrackingTimer?.TimerID;
            _altAzTrackingTimer = new MediaTimer
            {
                Period = SkySettings.AltAzTrackingUpdateInterval
            };
            _altAzTrackingTimer.Tick += AltAzTrackingTimerEvent;
            _altAzTrackingTimer.Start();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MonitorLog.GetCurrentMethod(),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"TimerID on entry|{timerId}|TimerID|{_altAzTrackingTimer?.TimerID}|Running|{_altAzTrackingTimer?.IsRunning}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Alt Az timer is running property
        /// </summary>
        private static bool AltAzTimerIsRunning => _altAzTrackingTimer?.IsRunning == true;

        /// <summary>
        /// Update AltAz tracking rates including delta for tracking error
        /// </summary>
        private static void SetAltAzTrackingRates(AltAzTrackingType altAzTrackingType)
        {
            switch (altAzTrackingType)
            {
                case AltAzTrackingType.Predictor:
                    double[] delta = { 0.0, 0.0 };
                    if (SkyPredictor.RaDecSet)
                    {
                        // Update mount position
                        MountPositionUpdated = false;
                        UpdateSteps();
                        while (!MountPositionUpdated) Thread.Sleep(10);
                        var steps = Steps;
                        DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(SkySettings.AltAzTrackingUpdateInterval);
                        var raDec = SkyPredictor.GetRaDecAtTime(nextTime);
                        // get required target position in topo coordinates
                        var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1]);
                        var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, GetLocalSiderealTime(nextTime), SkySettings.Latitude);
                        Array.Reverse(skyTarget);
                        skyTarget = GetSyncedAxes(skyTarget);
                        var rawPositions = new[] { ConvertStepsToDegrees(steps[0], 0), ConvertStepsToDegrees(steps[1], 1) };
                        delta[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                        delta[1] = Range.Range180((skyTarget[1] - rawPositions[1]));
                        const double milliSecond = 0.001;
                        _skyTrackingRate.X = delta[0] / (SkySettings.AltAzTrackingUpdateInterval * milliSecond);
                        _skyTrackingRate.Y = delta[1] / (SkySettings.AltAzTrackingUpdateInterval * milliSecond);
                        var monitorItem = new MonitorEntry
                        {
                            Datetime = HiResDateTime.UtcNow,
                            Device = MonitorDevice.Server,
                            Category = MonitorCategory.Server,
                            Type = MonitorType.Data,
                            Method = MethodBase.GetCurrentMethod()?.Name,
                            Thread = Thread.CurrentThread.ManagedThreadId,
                            Message = $"Ra:{internalRaDec.X}|Dec:{internalRaDec.Y}|Azimuth delta:{delta[0]}|Altitude delta:{delta[1]}"
                        };
                        MonitorLog.LogToMonitor(monitorItem);
                    }
                    break;
                case AltAzTrackingType.Rate:
                    _skyTrackingRate = ConvertRateToAltAz(CurrentTrackingRate(), 0.0, DeclinationXForm);
                    break;
            }
        }

        /// <summary>
        /// Evaluate and return slew motion state used by AltAz
        /// Equals condition determines default slew motion when at home position
        /// </summary>
        /// <returns></returns>
        private static PointingState GetAzDirection()
        {
            var azDirection = _actualAxisX >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
            if (AtHome) azDirection = PointingState.Normal;
            return azDirection;
        }

        /// <summary>
        /// Set mechanical direction for dec rate
        /// Positive direction mean go mechanical north
        /// </summary>
        /// <returns></returns>
        private static double GetDecRateDirection(double rate)
        {
            bool moveNorth = rate > 0;
            bool isEast = SideOfPier == PointingState.Normal;
            bool isWest = SideOfPier == PointingState.ThroughThePole;
            bool invert = false;
            rate = Math.Abs(rate);

            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                case MountType.SkyWatcher:
                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            // No direction change needed for AltAz
                            break;

                        case AlignmentMode.Polar:
                            if (isEast || isWest)
                            {
                                if (SkySettings.Mount == MountType.Simulator)
                                {
                                    if (SouthernHemisphere)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (SouthernHemisphere)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    if (PolarMode == PolarMode.Left) invert = !invert;
                                }
                            }
                            break;
                        case AlignmentMode.GermanPolar:
                            if (isEast || isWest)
                            {
                                if (SkySettings.Mount == MountType.Simulator)
                                {
                                    if (SouthernHemisphere)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (SouthernHemisphere)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                }
                            }
                            break;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (invert) rate = -rate;
            return rate;
        }
        /// <summary>
        /// Set mechanical direction for ra rate
        /// Positive direction mean go mechanical east
        /// </summary>
        /// <returns></returns>
        private static double GetRaRateDirection(double rate)
        {
            var east = rate > 0;
            rate = Math.Abs(rate);

            if (SouthernHemisphere)
            {
                if (!east) { rate = -rate; }
            }
            else
            {
                if (east) { rate = -rate; }
            }

            return rate;
        }

        /// <summary>
        /// Cycles tracking
        /// </summary>
        /// <param name="silence">turns off voice</param>
        /// <remarks>planetarium programs fix which doesn't turn on tracking before a goto</remarks>
        public static void CycleOnTracking(bool silence)
        {
            // ToDo re-enable voice prompt later
            // if (silence) { TrackingSpeak = false; }

            // Tracking = false;
            Tracking = true;

            // ToDo re-enable voice prompt later
            // if (silence) { TrackingSpeak = true; }
        }

        #endregion

        #region Validation & Limits

        /// <summary>
        /// Determines whether the specified target is reachable based on the current slew state.
        /// </summary>
        /// <remarks>This method evaluates whether the given target coordinates can be reached based on
        /// the provided slew state. For unsupported slew types, the method will return <see
        /// langword="false"/>.</remarks>
        /// <param name="target">An array of doubles representing the target coordinates. The interpretation of the
        /// coordinates depend on the specified <paramref name="slewState"/>.</param>
        /// <param name="slewState">The type of slew operation to evaluate. Must be either <see cref="SlewType.SlewRaDec"/> or <see
        /// cref="SlewType.SlewAltAz"/>. Other values are not supported.</param>
        /// <returns><see langword="true"/> if the target is reachable for the specified <paramref name="slewState"/>; otherwise,
        /// <see langword="false"/>.</returns>
        public static bool IsTargetReachable(double[] target, SlewType slewState)
        {
            if (SkySettings.AlignmentMode == AlignmentMode.GermanPolar) return true;
            switch (slewState)
            {
                case SlewType.SlewRaDec:
                case SlewType.SlewAltAz:
                    var flipOnNextGoto = FlipOnNextGoto; // Save and restore flip on next goto state
                    var isTargetReachable = IsTargetWithinLimits(MapSlewTargetToAxes(target, slewState));
                    FlipOnNextGoto = flipOnNextGoto;
                    return isTargetReachable;
                default:
                    return false; // Other slews not implemented yet
            }
        }

        /// <summary>
        /// Calculates if axis position is within the defined flip angle
        /// </summary>
        /// <param name="position">X axis position of mount</param>
        /// <returns>True if within limits otherwise false</returns>
        public static bool IsWithinFlipLimits(IReadOnlyList<double> position)
        {
            var absPos0 = Math.Abs(position[0]);
            switch (SkySettings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return (SkySettings.AxisLimitX >= absPos0) && (absPos0 >= 360.0 - SkySettings.AxisLimitX);
                case AlignmentMode.Polar:
                    return (180.0 - SkySettings.AxisLimitX <= absPos0) && (absPos0 <= SkySettings.AxisLimitX);
                case AlignmentMode.GermanPolar:
                    return -SkySettings.HourAngleLimit < absPos0 && absPos0 < SkySettings.HourAngleLimit ||
                           180 - SkySettings.HourAngleLimit < absPos0 && absPos0 < 180 + SkySettings.HourAngleLimit;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// AltAz and Polar checks for elevation limits and azimuth slewing limits through flip angle + slew limit
        /// The flip angle is 180 degrees away from the home angle and the slew limit can positive or negative.
        /// GEM mounts check the hour angle limit.
        /// </summary>
        private static void CheckAxisLimits()
        {
            var limitHitX = false;
            var limitHitY = false;
            var limitHit = false;
            var meridianLimit = false;
            var horizonLimit = false;
            //var msg = string.Empty;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = string.Empty
            };

            //Meridian Limit Test,  combine flip angle and tracking limit for a total limit passed meridian
            var totLimit = SkySettings.HourAngleLimit + SkySettings.AxisTrackingLimit;

            // Check the ranges of the axes primary axis must be in the range plus/minus Flip Anglefor AltAz or Polar
            // and -hourAngleLimit to 180 + hourAngleLimit for german polar
            switch (SkySettings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // Slew limit < 180 there are unreachable positions around the meridian, 
                    // eg +205 degrees and -155 degrees, are unreachable for a slew angle limit of 150 degrees
                    limitHitX = LimitStatus.AtLowerLimitAxisX || LimitStatus.AtUpperLimitAxisX;
                    // The secondary (Alt / Dec) axis must be in the range LowerLimitAxisY to UpperLimitAxisY
                    limitHitY = LimitStatus.AtLowerLimitAxisY || LimitStatus.AtUpperLimitAxisY;
                    meridianLimit = limitHitY;
                    limitHit = LimitStatus.AtLimit;
                    break;
                case AlignmentMode.Polar:
                    // Slew limit > 180 the mount can move past the meridian angle, and two equivalent positions, 
                    // eg +205 degrees and -155 degrees, are equivalent for a slew angle limit of 200 degrees
                    limitHitX = LimitStatus.AtLowerLimitAxisX || LimitStatus.AtUpperLimitAxisX;
                    // The secondary (Alt / Dec) axis must be in the range LowerLimitAxisY to UpperLimitAxisY
                    limitHitY = LimitStatus.AtLowerLimitAxisY || LimitStatus.AtUpperLimitAxisY;
                    meridianLimit = limitHitX;
                    limitHit = LimitStatus.AtLimit;
                    break;
                // Check the ranges of the primary axis and -hourAngleLimit to 180 + hourAngleLimit for german polar
                case AlignmentMode.GermanPolar:
                    // the primary axis needs to be in the range -180 to +180 to correspond with hour angles of -12 to 12.
                    // check if we have hit the hour angle limit 
                    if (SouthernHemisphere)
                    {
                        if (_appAxes.X >= SkySettings.HourAngleLimit ||
                            _appAxes.X <= -SkySettings.HourAngleLimit - 180)
                        {
                            limitHit = true;
                        }

                        // Check tracking limit
                        if (_appAxes.X >= totLimit || _appAxes.X <= -totLimit - 180)
                        {
                            meridianLimit = true;
                        }
                    }
                    else
                    {
                        if (_appAxes.X >= SkySettings.HourAngleLimit + 180 ||
                            _appAxes.X <= -SkySettings.HourAngleLimit)
                        {
                            limitHit = true;
                        }

                        //Check Tracking Limit
                        if (_appAxes.X >= totLimit + 180 || _appAxes.X <= -totLimit)
                        {
                            meridianLimit = true;
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Horizon Limit Test
            if (SkySettings.HzLimitPark || SkySettings.HzLimitTracking) // Skip all if set to do nothing
            {
                switch (SkySettings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        if ((Altitude <= SkySettings.AxisHzTrackingLimit
                             || Altitude <= SkySettings.AxisLowerLimitY
                             || Altitude >= SkySettings.AxisUpperLimitY) && Tracking)
                        {
                            limitHit = true;
                            horizonLimit = true;
                        }
                        break;
                    case AlignmentMode.Polar:
                        if (Altitude <= SkySettings.AxisHzTrackingLimit && Tracking)
                        {
                            limitHit = true;
                            horizonLimit = true;
                        }
                        break;
                    case AlignmentMode.GermanPolar:
                        if (SideOfPier == PointingState.Normal && Altitude <= SkySettings.AxisHzTrackingLimit && Tracking)
                        {
                            limitHit = true;
                            horizonLimit = true;
                        }

                        break;
                }
            }

            // Set the warning indicator light
            // ToDo: improve limit handling
            // LimitAlarm = limitHit;

            // Meridian Triggers
            if (meridianLimit)
            {
                monitorItem.Message =
                    $"Meridian Limit Alarm: Park: {SkySettings.LimitPark} | Position: {SkySettings.ParkLimitName} | Stop Tracking: {SkySettings.LimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);

                if (Tracking && SkySettings.LimitTracking)
                {
                    Tracking = false;
                } // turn off tracking

                if (SkySettings.LimitPark && SlewState != SlewType.SlewPark) // only hit this once while in limit
                {
                    var found = SkySettings.ParkPositions.Find(x => x.Name == SkySettings.ParkLimitName);
                    if (found == null)
                    {
                        StopAxes();
                    }
                    else
                    {
                        ParkSelected = found;
                        GoToPark();
                    }
                }
            }
            // Horizon Triggers
            if (horizonLimit)
            {
                monitorItem.Message =
                    $"Horizon Limit Alarm: Park: {SkySettings.HzLimitPark} | Position:{SkySettings.ParkHzLimitName} | Stop Tracking:{SkySettings.HzLimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);

                if (Tracking && SkySettings.HzLimitTracking)
                {
                    Tracking = false;
                } // turn off tracking

                if (SkySettings.HzLimitPark && SlewState != SlewType.SlewPark) // only hit this once while in limit
                {
                    var found = SkySettings.ParkPositions.Find(x => x.Name == SkySettings.ParkHzLimitName);
                    if (found == null)
                    {
                        StopAxes();
                    }
                    else
                    {
                        ParkSelected = found;
                        GoToPark();
                    }
                }
            }
        }

        /// <summary>
        /// Slew state based on axis status
        /// </summary>
        private static void CheckSlewState()
        {
            var slewing = false;
            switch (SlewState)
            {
                case SlewType.SlewNone:
                    slewing = false;
                    break;
                case SlewType.SlewSettle:
                    slewing = true;
                    break;
                case SlewType.SlewMoveAxis:
                    slewing = true;
                    break;
                case SlewType.SlewRaDec:
                    slewing = true;
                    break;
                case SlewType.SlewAltAz:
                    slewing = true;
                    break;
                case SlewType.SlewPark:
                    slewing = true;
                    // Tracking = false;  // Tracking reject already false issue
                    // AtPark = true;
                    break;
                case SlewType.SlewHome:
                    slewing = true;
                    //  Tracking = false; // Tracking reject already false issue
                    break;
                case SlewType.SlewHandpad:
                    slewing = true;
                    break;
                case SlewType.SlewComplete:
                    SlewState = SlewType.SlewNone;
                    break;
                default:
                    SlewState = SlewType.SlewNone;
                    break;
            }

            if ((Math.Abs(RateMovePrimaryAxis) + Math.Abs(RateMoveSecondaryAxis)) > 0) { slewing = true; }
            IsSlewing = slewing;
        }

        // ToDo: Remove if not needed
        /// <summary>
        /// Resets the spiral list if slew is out of limit
        /// </summary>
        //private static void CheckSpiralLimit()
        //{
        //    if (!SkySettings.SpiralLimits) return;
        //    if (SpiralCollection.Count == 0) return;
        //    var point = SpiralCollection[0];
        //    if (point == null) return;
        //    // calc distance between two coordinates
        //    var distance = Calculations.AngularDistance(RightAscensionXForm, DeclinationXForm, point.RaDec.X, point.RaDec.Y);
        //    if (distance <= SkySettings.SpiralDistance) return;
        //    SpiralCollection.Clear();
        //    SkySettings.SpiralDistance = 0;
        //    SpiralChanged = true;
        //}

        /// <summary>
        /// Checks if the target is within the defined limits.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private static bool IsTargetWithinLimits(double[] target)
        {
            const double oneArcSec = 1.0 / 3600;
            var axisUpperLimitY = SkySettings.AxisUpperLimitY;
            var axisLowerLimitY = SkySettings.AxisLowerLimitY;
            if (SkySettings.AlignmentMode == AlignmentMode.Polar && PolarMode == PolarMode.Left)
            {
                axisLowerLimitY = 180 - SkySettings.AxisUpperLimitY;
                axisUpperLimitY = 180 - SkySettings.AxisLowerLimitY;
            }
            return (-SkySettings.AxisLimitX - oneArcSec <= target[0] && target[0] <= SkySettings.AxisLimitX + oneArcSec) &&
                   (axisLowerLimitY <= target[1] && target[1] <= axisUpperLimitY);
        }

        /// <summary>
        /// Checks for az in plus / minus range centred on 0 degrees for slewing
        /// Eastwards and Westwards flip at 0 degrees and are always in limit
        /// </summary>
        /// <param name="az"></param>
        /// <returns></returns>
        public static bool AzEastWestTrackAtLimit(double az)
        {
            bool atLimit = false;
            if (SkySettings.AlignmentMode != AlignmentMode.GermanPolar)
            {
                switch (GetAzDirection())
                {
                    case PointingState.Normal:
                        atLimit = (az > SkySettings.AxisLimitX + SkySettings.AxisTrackingLimit);
                        break;
                    case PointingState.ThroughThePole:
                        atLimit = (az < -SkySettings.AxisLimitX - SkySettings.AxisTrackingLimit);
                        break;
                }
            }
            return atLimit;
        }

        #endregion

        #region Async Operations

        /// <summary>
        /// Cancel all currently executing async operations
        /// </summary>
        public static void CancelAllAsync()
        {
            if (_ctsGoTo != null || _ctsPulseGuideDec != null || _ctsPulseGuideRa != null || _ctsHcPulseGuide != null)
            {
                _ctsGoTo?.Cancel();
                _ctsPulseGuideDec?.Cancel();
                _ctsPulseGuideRa?.Cancel();
                _ctsHcPulseGuide?.Cancel();
                var sw = Stopwatch.StartNew();
                while (_ctsGoTo != null || _ctsPulseGuideDec != null || _ctsPulseGuideRa != null || _ctsHcPulseGuide != null && sw.ElapsedMilliseconds < 2000)
                    Thread.Sleep(200); // wait for any pending pulse guide operations to wake up and cancel
            }
        }

        #endregion

        #region ASCOM Bridge Properties

        /// <summary>
        /// Bridge property to UI RightAscensionXForm for ASCOM API compatibility
        /// </summary>
        public static double RightAscensionXFormBridge => RightAscensionXForm;

        /// <summary>
        /// Bridge property to UI DeclinationXForm for ASCOM API compatibility  
        /// </summary>
        public static double DeclinationXFormBridge => DeclinationXForm;

        /// <summary>
        /// Bridge property to UI SiderealTime for ASCOM API compatibility
        /// </summary>
        public static double SiderealTimeBridge => SiderealTime;

        /// <summary>
        /// Bridge property to UI Lha for ASCOM API compatibility
        /// </summary>
        public static double LhaBridge => Lha;

        /// <summary>
        /// Bridge property to UI IsSideOfPier for ASCOM API compatibility
        /// </summary>
        public static PointingState SideOfPierBridge => IsSideOfPier;

        #endregion
    }
}
