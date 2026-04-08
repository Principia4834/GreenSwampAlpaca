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
            get => _defaultInstance?.RightAscension ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance.RightAscension) < 0.000000000000001) return;
                _defaultInstance.RightAscension = value;
            }
        }

        /// <summary>
        /// Declination in degrees
        /// </summary>
        public static double Declination
        {
            get => _defaultInstance?.Declination ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance.Declination) < 0.000000000000001) return;
                _defaultInstance.Declination = value;
            }
        }

        /// <summary>
        /// UI display for converted ra
        /// </summary>
        public static double RightAscensionXForm
        {
            get => _defaultInstance?.RightAscensionXForm ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                _defaultInstance.RightAscensionXForm = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// UI display for converted dec
        /// </summary>
        public static double DeclinationXForm
        {
            get => _defaultInstance?.DeclinationXForm ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                _defaultInstance.DeclinationXForm = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Positions converted from mount
        /// </summary>
        public static double Altitude
        {
            get => _defaultInstance?.Altitude ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                // if (Math.Abs(value - _defaultInstance.Altitude) < 0.000000000000001) { return; }
                _defaultInstance.Altitude = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Positions converted from mount
        /// </summary>
        public static double Azimuth
        {
            get => _defaultInstance?.Azimuth ?? 0.0;
            private set
            {
                if (_defaultInstance == null) return;
                // if (Math.Abs(value - _defaultInstance.Azimuth) < 0.000000000000001) { return; }
                _defaultInstance.Azimuth = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Local time
        /// </summary>
        public static double SiderealTime
        {
            get => _defaultInstance?.SiderealTime ?? 0.0;
            internal set
            {
                if (_defaultInstance == null) return;
                _defaultInstance.SiderealTime = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Local Hour Angle
        /// </summary>
        public static double Lha
        {
            get => _defaultInstance?.Lha ?? 0.0;
            internal set
            {
                if (_defaultInstance == null) return;
                if (Math.Abs(value - _defaultInstance.Lha) < 0.000000000000001) { return; }
                _defaultInstance.Lha = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// UI indicator
        /// </summary>
        public static PointingState IsSideOfPier
        {
            get => _defaultInstance?.IsSideOfPier ?? PointingState.Unknown;
            internal set
            {
                if (_defaultInstance == null) return;
                if (value == _defaultInstance.IsSideOfPier) return;

                _defaultInstance.IsSideOfPier = value;
                OnStaticPropertyChanged();

                var appAxisY = _defaultInstance._appAxes.Y;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{appAxisY}|{appAxisY < 90 || appAxisY.IsEqualTo(90, 0.0000000001)}|{appAxisY > -90 || appAxisY.IsEqualTo(-90, 0.0000000001)} "
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
                switch (_settings!.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        sideOfPier = (_defaultInstance?._actualAxisX ?? 0.0) >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
                        break;
                    case AlignmentMode.Polar:
                        sideOfPier = ((_defaultInstance?._appAxes.Y ?? 0.0) < 90.0000000001 && (_defaultInstance?._appAxes.Y ?? 0.0) > -90.0000000001) ? PointingState.Normal : PointingState.ThroughThePole;
                        break;
                    case AlignmentMode.GermanPolar:
                        if (_settings!.Latitude < 0)
                        {
                            //return AppAxes.Y <= 90 && AppAxes.Y >= -90 ? PointingState.ThroughThePole : PointingState.Normal;
                            // replaced with ...
                            sideOfPier = ((_defaultInstance?._appAxes.Y ?? 0.0) < 90.0000000001 && (_defaultInstance?._appAxes.Y ?? 0.0) > -90.0000000001) ? PointingState.ThroughThePole : PointingState.Normal;
                        }
                        else
                        {
                            // return AppAxes.Y <= 90 && AppAxes.Y >= -90 ? PointingState.Normal : PointingState.ThroughThePole;
                            // replaced with ...
                            sideOfPier = ((_defaultInstance?._appAxes.Y ?? 0.0) < 90.0000000001 && (_defaultInstance?._appAxes.Y ?? 0.0) > -90.0000000001) ? PointingState.Normal : PointingState.ThroughThePole;
                        }
                        break;
                    default:
                        break;
                }
                return sideOfPier;
            }
            set
            {
                var axes = new[] { _defaultInstance?._actualAxisX ?? 0.0, _defaultInstance?._actualAxisY ?? 0.0 };
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{_settings!.HourAngleLimit}|{axes[0]}|{axes[1]}"
                };
                var context = AxesContext.FromSettings(_settings);
                if (IsWithinFlipLimits(Axes.AxesMountToApp(axes, context)))
                {
                    if (Tracking)
                    {
                        FlipOnNextGoto = true;
                    }
                    else
                    {
                        FlipOnNextGoto = true;
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
            get => (_defaultInstance?.TrackingMode ?? TrackingMode.Off) != TrackingMode.Off;
            set
            {
                if (_defaultInstance == null)
                {
                    OnStaticPropertyChanged();
                    return;
                }

                if (value == _defaultInstance.Tracking)
                {
                    OnStaticPropertyChanged();
                    return;
                } // No change - avoid redundant operations

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

                _defaultInstance?.SkyPredictor.Reset();
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
                    switch (_settings!.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (_defaultInstance != null) _defaultInstance.AltAzTrackingMode = AltAzTrackingType.Predictor;
                            // Must have a tracking target for Alt Az otherwise just set the reference time to now
                            if (!_defaultInstance.SkyPredictor.RaDecSet)
                            {
                                _defaultInstance.SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, 0, 0);
                            }
                            else
                            {
                                _defaultInstance.SkyPredictor.ReferenceTime = DateTime.Now;
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
                    if (_defaultInstance != null)
                    {
                        _defaultInstance._isPulseGuidingDec = false;
                        _ctsPulseGuideDec?.Dispose(); _ctsPulseGuideDec = null;
                        _defaultInstance._isPulseGuidingRa = false;
                        _ctsPulseGuideRa?.Dispose(); _ctsPulseGuideRa = null;
                    }
                    // ToDo re-enable voice prompt later
                    // if (TrackingSpeak && _trackingMode != TrackingMode.Off) { Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceTrackingOff"].ToString()); }
                    _defaultInstance.TrackingMode = TrackingMode.Off;
                }
                _defaultInstance.SetTracking(value); //off

                SetTracking();
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Status for goto - delegates to SlewController if available, also checks MoveAxis activity
        /// </summary>
        public static bool IsSlewing
        {
            get
            {
                // Check MoveAxis activity (direct axis rate commands)
                if ((Math.Abs(_defaultInstance?._rateMoveAxes.X ?? 0.0) + Math.Abs(_defaultInstance?._rateMoveAxes.Y ?? 0.0)) > 0)
                    return true;

                // Fall back to instance backing field
                return _defaultInstance?._isSlewing ?? false;
            }
            private set
            {
                if (_defaultInstance != null)
                {
                    if (_defaultInstance._isSlewing == value) { return; }
                    _defaultInstance._isSlewing = value;
                    OnStaticPropertyChanged();
                }
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
                var context = AxesContext.FromSettings(_settings);
                var home = Axes.AxesMountToApp(new[] { HomeAxes.X, HomeAxes.Y }, context);
                var h = new Vector(home[0], home[1]);
                var m = new Vector(_defaultInstance?._appAxes.X ?? 0.0, _defaultInstance?._appAxes.Y ?? 0.0);
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
            get => _settings!.AtPark;
            set
            {
                _settings!.AtPark = value;
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
        /// UI Checkbox option to flip on the next goto
        /// </summary>
        public static bool FlipOnNextGoto
        {
            get => _defaultInstance?._flipOnNextGoto ?? false;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._flipOnNextGoto = value;

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
        /// Set for all types of go tos
        /// </summary>
        public static SlewType SlewState
        {
            get => _defaultInstance?._slewState ?? SlewType.SlewNone;
            internal set
            {
                if (_defaultInstance != null) _defaultInstance._slewState = value;
            }
        }

        #endregion

        #region Target & Rate Properties

        /// <summary>
        /// The declination tracking rate in degrees, DeclinationRate
        /// corrected direction applied
        /// </summary>
        public static double RateDec
        {
            get => _defaultInstance?.RateDec ?? 0.0;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.RateDec = value;

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{SkyTrackingOffset[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// The right ascension tracking in degrees, RightAscensionRate
        /// corrected direction applied
        /// </summary>
        public static double RateRa
        {
            get => _defaultInstance?.RateRa ?? 0.0;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.RateRa = value;

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}|{SkyTrackingOffset[0]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        #endregion

        #region Position Calculations

        public static double[] GetAlternatePosition(double[] position, AxesContext context)
        {
            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return GetAlternatePositionAltAz(position, context);
                case AlignmentMode.Polar:
                    return GetAlternatePositionPolar(position, context);
                case AlignmentMode.GermanPolar:
                    return GetAlternatePositionGEM(position, context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(context));
            }
        }

        private static string ChooseClosestPosition(double position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Abs(a[0] - position);
            var val2 = Math.Abs(b[0] - position);
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        private static string ChooseClosestPositionPolar(IReadOnlyList<double> position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Max(Math.Abs(a[0] - position[0]), Math.Abs(a[1] - position[1]));
            var val2 = Math.Max(Math.Abs(b[0] - position[0]), Math.Abs(b[1] - position[1]));
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        private static double[] GetAlternatePositionGEM(double[] position, AxesContext context)
        {
            if (!context.IsWithinFlipLimits(position)) { return null; }
            var alt = Axes.GetAltAxisPosition(position, context);
            if (!context.IsWithinFlipLimits(alt)) { return null; }
            var cl = ChooseClosestPosition(_defaultInstance?._actualAxisX ?? 0.0, position, alt);
            if (FlipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                    Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                });
            }
            if (cl != "b") { return null; }
            return alt;
        }

        private static double[] GetAlternatePositionAltAz(double[] position, AxesContext context)
        {
            if (!context.IsWithinFlipLimits(position)) { return null; }
            var alt = Axes.GetAltAxisPosition(position, context);
            var cl = ChooseClosestPosition(_defaultInstance?._actualAxisX ?? 0.0, position, alt);
            if (FlipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                    Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                });
            }
            if (cl != "b") { return null; }
            return alt;
        }

        private static double[] GetAlternatePositionPolar(double[] position, AxesContext context)
        {
            var alt = Axes.GetAltAxisPosition(position, context);
            alt[0] = Range.Range180(alt[0]);
            var altOk = context.IsWithinFlipLimits(alt);
            var posOk = context.IsWithinFlipLimits(position);
            if (!altOk) return null;
            if (posOk && altOk)
            {
                var cl = ChooseClosestPositionPolar([_defaultInstance?._actualAxisX ?? 0.0, _defaultInstance?._actualAxisY ?? 0.0], position, alt);
                if (FlipOnNextGoto)
                {
                    cl = cl == "a" ? "b" : "a";
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                    });
                }
                if (cl != "b") { return null; }
                return alt;
            }
            return alt;
        }

        #endregion

        #region Pulse Guiding

        /// <summary>
        /// Pulse commands
        /// </summary>
        /// <param name="direction">GuideDirections</param>
        /// <param name="duration">in milliseconds</param>
        /// /// <param name="altRate">alternate rate to replace the guide rate</param>
        public static void PulseGuide(GuideDirection direction, int duration, double altRate, MountInstance? instance = null) // J1: instance-aware
        {
            var inst = instance ?? _defaultInstance;     // M0: resolve instance before guard
            var settings = instance?.Settings ?? _settings; // M0: per-instance settings
            if (inst == null || !inst.IsMountRunning) { throw new Exception("Mount not running"); }

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
                        inst!.IsPulseGuidingDec = false; // J1: per-instance
                        return;
                    }
                    inst!.IsPulseGuidingDec = true; // J1: per-instance
                    inst!._hcPrevMoveDec = null; // J1: per-instance (replaces HcResetPrevMove(MountAxis.Dec))
                    var decGuideRate = useAltRate ? altRate : Math.Abs(inst!.GuideRateDec); // J1: per-instance
                    switch (settings!.AlignmentMode) // J1: per-instance settings
                    {
                        case AlignmentMode.AltAz:
                            if (direction == GuideDirection.South) { decGuideRate = -decGuideRate; }
                            break;
                        case AlignmentMode.Polar:
                            if (inst!.SideOfPier == PointingState.Normal) // J1: per-instance
                            {
                                if (direction == GuideDirection.North) { decGuideRate = -decGuideRate; }
                            }
                            else
                            {
                                if (direction == GuideDirection.South) { decGuideRate = -decGuideRate; }
                            }
                            if (inst!.Settings.PolarMode == PolarMode.Left) decGuideRate = -decGuideRate; // J1: per-instance; swap direction because primary OTA is flipped
                            break;
                        case AlignmentMode.GermanPolar:
                            if (inst!.SideOfPier == PointingState.Normal) // J1: per-instance
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
                    if (direction != inst!._lastDecDirection) decBacklashAmount = settings!.DecBacklash; // J1: per-instance
                    inst!._lastDecDirection = direction; // J1: per-instance
                    inst!._ctsPulseGuideDec?.Cancel(); // J1: per-instance
                    inst!._ctsPulseGuideDec?.Dispose();
                    inst!._ctsPulseGuideDec = new CancellationTokenSource();

                    switch (settings!.Mount) // J1: per-instance
                    {
                        case MountType.Simulator:
                        {
                            var mq = inst!.MountQueueInstance!; // J1: per-instance
                            switch (settings!.AlignmentMode) // J1: per-instance
                            {
                                case AlignmentMode.AltAz:
                                    inst!.PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, inst.SimPulseGoto, inst!._ctsPulseGuideDec.Token); // J1: per-instance
                                    break;
                                case AlignmentMode.Polar:
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphereâ†’per-instance
                                    _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis2, decGuideRate, duration,
                                        inst!._ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphereâ†’per-instance
                                    _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis2, decGuideRate, duration,
                                        inst!._ctsPulseGuideDec.Token);
                                    break;
                                default:
                                    break;
                            }
                            break;
                        }
                        case MountType.SkyWatcher:
                        {
                            var sq = inst!.SkyQueueInstance!; // J1: per-instance
                            switch (settings!.AlignmentMode) // J1: per-instance
                            {
                                case AlignmentMode.AltAz:
                                    inst!.PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, inst.SkyPulseGoto, inst!._ctsPulseGuideDec.Token); // J1: per-instance
                                    break;
                                case AlignmentMode.Polar:
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphereâ†’per-instance
                                    _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis2, decGuideRate, duration, decBacklashAmount, inst!._ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis2, decGuideRate, duration, decBacklashAmount, inst!._ctsPulseGuideDec.Token);
                                    break;
                                default:
                                    break;
                            }
                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                case GuideDirection.East:
                case GuideDirection.West:
                    if (duration == 0)
                    {
                        inst!.IsPulseGuidingRa = false; // J1: per-instance
                        return;
                    }
                    inst!.IsPulseGuidingRa = true; // J1: per-instance
                    inst!._hcPrevMoveRa = null; // J1: per-instance (replaces HcResetPrevMove(MountAxis.Ra))
                    var raGuideRate = useAltRate ? altRate : Math.Abs(inst!.GuideRateRa); // J1: per-instance
                    if (settings!.AlignmentMode != AlignmentMode.AltAz) // J1: per-instance
                    {
                        if (inst!.Settings.Latitude < 0) // J1: SouthernHemisphereâ†’per-instance
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

                    inst!._ctsPulseGuideRa?.Cancel(); // J1: per-instance
                    inst!._ctsPulseGuideRa?.Dispose();
                    inst!._ctsPulseGuideRa = new CancellationTokenSource();
                    switch (settings!.Mount) // J1: per-instance
                    {
                        case MountType.Simulator:
                            if (settings!.AlignmentMode == AlignmentMode.AltAz) // J1: per-instance
                            {
                                inst!.PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, inst.SimPulseGoto, inst!._ctsPulseGuideRa.Token); // J1: per-instance
                            }
                            else
                            {
                                var mq = inst!.MountQueueInstance!; // J1: per-instance
                                _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis1, raGuideRate, duration, inst!._ctsPulseGuideRa.Token);
                            }

                            break;
                        case MountType.SkyWatcher:
                            if (settings!.AlignmentMode == AlignmentMode.AltAz) // J1: per-instance
                            {
                                inst!.PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, inst.SkyPulseGoto, inst!._ctsPulseGuideRa.Token); // J1: per-instance
                            }
                            else
                            {
                                var sq = inst!.SkyQueueInstance!; // J1: per-instance
                                _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis1, raGuideRate, duration, 0, inst!._ctsPulseGuideRa.Token);
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

        #region Tracking & Rates

        /// <summary>
        /// Set tracking on or off
        /// </summary>
        internal static void SetTracking(MountInstance? instance = null)
        {
            var inst = instance ?? _defaultInstance;
            var settings = instance?.Settings ?? _settings;
            if (inst == null || !inst.IsMountRunning) { return; }  // M2: per-instance guard

            double rateChange = 0;
            Vector rate;
            // Set rate change for tracking mode
            var currentTrackingMode = inst?.TrackingMode ?? TrackingMode.Off;
            switch (currentTrackingMode)
            {
                case TrackingMode.Off:
                    break;
                case TrackingMode.AltAz:
                    rateChange = CurrentTrackingRate(inst!);
                    break;
                case TrackingMode.EqN:
                    rateChange = CurrentTrackingRate(inst!);
                    break;
                case TrackingMode.EqS:
                    rateChange = -CurrentTrackingRate(inst!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            switch (settings!.Mount)
            {
                case MountType.Simulator:
                    switch (settings!.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor, inst);
                                if (inst!._altAzTrackingTimer?.IsRunning != true) inst!.StartAltAzTrackingTimer(); // J4: per-instance
                            }
                            else
                            {
                                if (inst!._altAzTrackingTimer?.IsRunning == true) inst!.StopAltAzTrackingTimer(); // J4: per-instance
                                inst._skyTrackingRate = new Vector(0, 0);
                            }
                            rate = SkyGetRate(inst);
                            // Tracking applied unless MoveAxis is active
                            {
                                var mq = inst!.MountQueueInstance; // N4: null guard â€” MountStop may race here
                                if (mq == null) return;
                                if (inst._rateMoveAxes.X == 0.0)
                                    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rate.X);
                                if (inst._rateMoveAxes.Y == 0.0)
                                    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis2, rate.Y);
                            }
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            {
                                var mq = inst!.MountQueueInstance!;
                                if (inst._rateMoveAxes.X == 0.0) // Set current tracking rate and RA tracking rate offset (0 if not sidereal)
                                        {
                                            _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
                                        }
                                // Clear rate offsets when tracking is off so simulator physics do not continue drifting
                                var raRate = currentTrackingMode != TrackingMode.Off
                                    ? GetRaRateDirection(inst?.RateRa ?? 0.0, inst!.Settings)
                                    : 0.0;
                                _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, raRate);
                                if (inst._rateMoveAxes.Y == 0.0) // Set Dec tracking rate offset (0 if not sidereal)
                                {
                                    var decRate = currentTrackingMode != TrackingMode.Off
                                        ? GetDecRateDirection(inst?.RateDec ?? 0.0, inst!)
                                        : 0.0;
                                    _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, decRate);
                                }
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                case MountType.SkyWatcher:
                    switch (settings!.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor, inst);
                                if (inst!._altAzTrackingTimer?.IsRunning != true) inst!.StartAltAzTrackingTimer(); // J4: per-instance
                            }
                            else
                            {
                                if (inst!._altAzTrackingTimer?.IsRunning == true) inst!.StopAltAzTrackingTimer(); // J4: per-instance
                                inst._skyTrackingRate = new Vector(0, 0);
                            }

                            // Get current tracking  including RA and Dec offsets
                            // Tracking applied unless MoveAxis is active
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            inst._skyTrackingRate = new Vector(rateChange, 0);
                            // Get current tracking including RA and Dec offsets
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    rate = SkyGetRate(inst); // Get current tracking  including RA and Dec offsets
                    {
                        var sq = inst!.SkyQueueInstance; // N4: null guard â€” MountStop may race here
                        if (sq == null) return;
                        if (inst._rateMoveAxes.X == 0.0)
                            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);
                        if (inst._rateMoveAxes.Y == 0.0)
                            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // don't log if pec is on
            if (inst?.Settings.PecOn == true) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{currentTrackingMode}|{rateChange * 3600}|{inst?._pecBinNow}|{inst?._skyTrackingOffset[0] ?? 0}|{inst?._skyTrackingOffset[1] ?? 0}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Set tracking mode based on alignment mode
        /// </summary>
        public static void SetTrackingMode()
        {
            if (_defaultInstance == null) return;

            switch (_settings!.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    _defaultInstance.TrackingMode = TrackingMode.AltAz;
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    _defaultInstance.TrackingMode = _settings!.Latitude < 0 ? TrackingMode.EqS : TrackingMode.EqN;
                    break;
            }
        }

        /// <summary>
        /// Sets tracking state directly without resetting SkyPredictor.
        /// Used by SlewController for AltAz slew completion.
        /// </summary>
        internal static void SetTrackingDirect(bool tracking, TrackingMode mode)
        {
            if (_defaultInstance == null) return;

            _defaultInstance.SetTracking(tracking);
            _defaultInstance.TrackingMode = mode;
            SetTracking(); // Apply to hardware
            OnStaticPropertyChanged();
        }

        /// <summary>
        /// Calculates the current RA tracking rate for a specific instance (arc seconds per second).
        /// Used by per-instance SkyPredictor to avoid reading from _defaultInstance.
        /// </summary>
        public static double CurrentTrackingRate(MountInstance inst)
        {
            double rate;
            switch (inst.Settings.TrackingRate)
            {
                case DriveRate.Sidereal:
                    rate = inst.Settings.SiderealRate;
                    break;
                case DriveRate.Solar:
                    rate = inst.Settings.SolarRate;
                    break;
                case DriveRate.Lunar:
                    rate = inst.Settings.LunarRate;
                    break;
                case DriveRate.King:
                    rate = inst.Settings.KingRate;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (rate < SiderealRate * 2 & rate != 0)
                rate += inst._trackingOffsetRate.X;

            if (inst.Settings.PecOn && inst.Tracking && inst._pecBinNow != null && !double.IsNaN(inst._pecBinNow.Item2))
                if (Math.Abs(inst._pecBinNow.Item2 - 1) < .04)
                    rate *= inst._pecBinNow.Item2;

            rate /= 3600;
            if (inst.Settings.RaTrackingOffset <= 0) { return rate; }
            var offsetrate = rate * (Convert.ToDouble(inst.Settings.RaTrackingOffset) / 100000);
            rate += offsetrate;
            return rate;
        }

        /// <summary>
        /// Sets up offsets from the selected tracking rate
        /// </summary>
        internal static void SetGuideRates(MountInstance? instance = null)
        {
            var inst = instance ?? _defaultInstance;
            var settings = instance?.Settings ?? _settings;
            var rate = inst != null ? CurrentTrackingRate(inst) : 0.0;
            if (inst != null)
            {
                inst.GuideRateRa = rate * settings!.GuideRateOffsetX;
                inst.GuideRateDec = rate * settings!.GuideRateOffsetY;
            }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{inst?.GuideRateRa ?? 0.0 * 3600}|{inst?.GuideRateDec ?? 0.0 * 3600}"
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

            var latRad = Principles.Units.Deg2Rad(_settings!.Latitude);
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
            if (change.X < -(_defaultInstance?._slewSpeedEight ?? 4.0)) { change.X = -(_defaultInstance?._slewSpeedEight ?? 4.0); }
            if (change.X > (_defaultInstance?._slewSpeedEight ?? 4.0)) { change.X = (_defaultInstance?._slewSpeedEight ?? 4.0); }

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
                    if (_defaultInstance != null &&
                        Interlocked.CompareExchange(ref _defaultInstance._altAzTrackingLock, -1, 0) == 0)
                    {
                        SetTracking();
                        // Release the lock
                        _defaultInstance._altAzTrackingLock = 0;
                    }
            }
        }

        /// <summary>
        /// Stop Alt Az tracking timer
        /// </summary>
        internal static void StopAltAzTrackingTimer()
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
                Period = _settings!.AltAzTrackingUpdateInterval
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
        internal static void SetAltAzTrackingRates(AltAzTrackingType altAzTrackingType, MountInstance inst)
        {
            switch (altAzTrackingType)
            {
                case AltAzTrackingType.Predictor:
                    double[] delta = { 0.0, 0.0 };
                    if (inst.SkyPredictor.RaDecSet)
                    {
                        // Update mount position
                        var evt = inst._mountPositionUpdatedEvent;
                        evt.Reset();
                        inst.UpdateSteps();
                        if (!evt.Wait(5000)) break;
                        var steps = inst._steps;
                        DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(inst.Settings.AltAzTrackingUpdateInterval);
                        var raDec = inst.SkyPredictor.GetRaDecAtTime(nextTime);
                        // get required target position in topo coordinates
                        var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1], settings: inst.Settings);
                        var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, inst.GetLocalSiderealTime(nextTime), inst.Settings.Latitude);
                        Array.Reverse(skyTarget);
                        skyTarget = GetSyncedAxes(skyTarget);
                        var rawPositions = new[] { inst.ConvertStepsToDegrees(steps[0], 0), inst.ConvertStepsToDegrees(steps[1], 1) };
                        delta[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                        delta[1] = Range.Range180((skyTarget[1] - rawPositions[1]));
                        const double milliSecond = 0.001;
                        inst._skyTrackingRate = new Vector(
                            delta[0] / (inst.Settings.AltAzTrackingUpdateInterval * milliSecond),
                            delta[1] / (inst.Settings.AltAzTrackingUpdateInterval * milliSecond)
                        );
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
            }
        }

        /// <summary>
        /// Evaluate and return slew motion state used by AltAz
        /// Equals condition determines default slew motion when at home position
        /// </summary>
        /// <returns></returns>
        private static PointingState GetAzDirection()
        {
            var azDirection = (_defaultInstance?._actualAxisX ?? 0.0) >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
            if (AtHome) azDirection = PointingState.Normal;
            return azDirection;
        }

        /// <summary>
        /// Set mechanical direction for dec rate
        /// Positive direction mean go mechanical north
        /// </summary>
        /// <returns></returns>
        internal static double GetDecRateDirection(double rate, MountInstance inst)
        {
            bool moveNorth = rate > 0;
            bool isEast = inst.SideOfPier == PointingState.Normal;
            bool isWest = inst.SideOfPier == PointingState.ThroughThePole;
            bool invert = false;
            rate = Math.Abs(rate);

            switch (inst.Settings.Mount)
            {
                case MountType.Simulator:
                case MountType.SkyWatcher:
                    switch (inst.Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            // No direction change needed for AltAz
                            break;

                        case AlignmentMode.Polar:
                            if (isEast || isWest)
                            {
                                if (inst.Settings.Mount == MountType.Simulator)
                                {
                                    if (inst.Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (inst.Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    if (inst.Settings.PolarMode == PolarMode.Left) invert = !invert;
                                }
                            }
                            break;
                        case AlignmentMode.GermanPolar:
                            if (isEast || isWest)
                            {
                                if (inst.Settings.Mount == MountType.Simulator)
                                {
                                    if (inst.Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (inst.Settings.Latitude < 0)
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
        internal static double GetRaRateDirection(double rate, SkySettingsInstance settings)
        {
            var east = rate > 0;
            rate = Math.Abs(rate);

            if (settings.Latitude < 0)
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
            if (_settings!.AlignmentMode == AlignmentMode.GermanPolar) return true;
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
            switch (_settings!.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return (_settings!.AxisLimitX >= absPos0) && (absPos0 >= 360.0 - _settings!.AxisLimitX);
                case AlignmentMode.Polar:
                    return (180.0 - _settings!.AxisLimitX <= absPos0) && (absPos0 <= _settings!.AxisLimitX);
                case AlignmentMode.GermanPolar:
                    return -_settings!.HourAngleLimit < absPos0 && absPos0 < _settings!.HourAngleLimit ||
                           180 - _settings!.HourAngleLimit < absPos0 && absPos0 < 180 + _settings!.HourAngleLimit;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Slew state based on axis status
        /// </summary>
        internal static void CheckSlewState()
        {
            var slewing = false;
            switch (_defaultInstance?._slewState ?? SlewType.SlewNone)
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
                    if (_defaultInstance != null) _defaultInstance._slewState = SlewType.SlewNone;
                    break;
                default:
                    if (_defaultInstance != null) _defaultInstance._slewState = SlewType.SlewNone;
                    break;
            }

            if ((Math.Abs(_defaultInstance?._rateMoveAxes.X ?? 0.0) + Math.Abs(_defaultInstance?._rateMoveAxes.Y ?? 0.0)) > 0) { slewing = true; }
            IsSlewing = slewing;
        }

        /// <summary>
        /// Checks if the target is within the defined limits.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private static bool IsTargetWithinLimits(double[] target)
        {
            const double oneArcSec = 1.0 / 3600;
            var axisUpperLimitY = _settings!.AxisUpperLimitY;
            var axisLowerLimitY = _settings!.AxisLowerLimitY;
            if (_settings!.AlignmentMode == AlignmentMode.Polar && _settings!.PolarMode == PolarMode.Left)
            {
                axisLowerLimitY = 180 - _settings!.AxisUpperLimitY;
                axisUpperLimitY = 180 - _settings!.AxisLowerLimitY;
            }
            return (-_settings!.AxisLimitX - oneArcSec <= target[0] && target[0] <= _settings!.AxisLimitX + oneArcSec) &&
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
            if (_settings!.AlignmentMode != AlignmentMode.GermanPolar)
            {
                switch (GetAzDirection())
                {
                    case PointingState.Normal:
                        atLimit = (az > _settings!.AxisLimitX + _settings!.AxisTrackingLimit);
                        break;
                    case PointingState.ThroughThePole:
                        atLimit = (az < -_settings!.AxisLimitX - _settings!.AxisTrackingLimit);
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

            }
        }
