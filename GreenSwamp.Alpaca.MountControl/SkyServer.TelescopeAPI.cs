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
                        _ = SlewRaDecAsync(RightAscension, Declination, true);
                    }
                    else
                    {
                        FlipOnNextGoto = true;
                        _ = SlewAltAzAsync(Altitude, Azimuth);
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
                // Check SlewController first (slew operations)
                if (_slewController?.IsSlewing == true)
                    return true;

                // Check MoveAxis activity (direct axis rate commands)
                if ((Math.Abs(_defaultInstance?._rateMoveAxes.X ?? 0.0) + Math.Abs(_defaultInstance?._rateMoveAxes.Y ?? 0.0)) > 0)
                    return true;

                // Fall back to instance backing field
                return _defaultInstance?._isSlewing ?? false;
            }
            private set
            {
                // Only update backing field if SlewController is not being used
                if (_slewController == null && _defaultInstance != null)
                {
                    if (_defaultInstance._isSlewing == value) { return; }
                    _defaultInstance._isSlewing = value;
                    OnStaticPropertyChanged();
                }
                // If SlewController exists, setter is a no-op (SlewController manages its own state)
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
        /// Pulse reporting to driver
        /// Alt Az uses both axes so always synchronous pulse guiding on one of Ra or Dec
        /// </summary>
        public static bool IsPulseGuiding => (_defaultInstance?._isPulseGuidingDec ?? false) || (_defaultInstance?._isPulseGuidingRa ?? false);

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

                ActionRateRaDec(); // Update the mount tracking rate

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
        /// Store the original DeclinationRate to maintain direction
        /// </summary>
        public static double RateDecOrg
        {
            get => _defaultInstance?.RateDecOrg ?? 0.0;
            set { if (_defaultInstance != null) _defaultInstance.RateDecOrg = value; }
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

                ActionRateRaDec(); // Update the mount tracking rate

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

        /// <summary>
        /// Store the original RightAscensionRate and maintain direction
        /// Previous conversions were not exact
        /// </summary>
        public static double RateRaOrg
        {
            get => _defaultInstance?.RateRaOrg ?? 0.0;
            set { if (_defaultInstance != null) _defaultInstance.RateRaOrg = value; }
        }

        /// <summary>
        /// Dec target for slewing, epoch is same as EquatorialSystem Property
        /// initialised to NaN to catch read before write
        /// convert to top-o-centric for any internal calculations
        /// </summary>
        public static double TargetDec
        {
            get => _defaultInstance?.TargetDec ?? double.NaN;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.TargetDec = value;
            }
        }

        /// <summary>
        /// Ra target for slewing, epoch is same as EquatorialSystem Property
        /// initialised to NaN to catch read before write
        /// convert to top-o-centric for any internal calculations
        /// </summary>
        public static double TargetRa
        {
            get => _defaultInstance?.TargetRa ?? double.NaN;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.TargetRa = value;
            }
        }

        /// <summary>
        /// The current Declination movement rate offset for telescope guiding (degrees/sec) 
        /// </summary>
        public static double GuideRateDec
        {
            get => _defaultInstance?.GuideRateDec ?? 0.0;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.GuideRateDec = value;
            }
        }

        /// <summary>
        /// The current Right Ascension movement rate offset for telescope guiding (degrees/sec) 
        /// </summary>
        public static double GuideRateRa
        {
            get => _defaultInstance?.GuideRateRa ?? 0.0;
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.GuideRateRa = value;
            }
        }

        #endregion

        #region Mount Control Methods

        /// <summary>
        /// Starts slew with ra/dec coordinates - synchronous version that blocks until complete.
        /// Used for synchronous ASCOM operations.
        /// </summary>
        /// <param name="rightAscension">Right ascension in hours</param>
        /// <param name="declination">Declination in degrees</param>
        /// <param name="tracking">Enable tracking after slew completes</param>
        public static void SlewRaDec(double rightAscension, double declination, bool tracking = false)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Ra/Dec (using SlewController): RA={Utilities.HoursToHMS(rightAscension, "h ", ":", "", 2)}, Dec={Utilities.DegreesToDMS(declination, "\u00B0 ", ":", "", 2)}, Tracking={tracking}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // NEW: Use SlewController - coordinates are [rightAscension, declination]
            var target = new[] { rightAscension, declination };
            SlewSync(target, SlewType.SlewRaDec, tracking);
        }

        /// <summary>
        /// Starts slew with ra/dec coordinates - async version for ITelescopeV4.
        /// Returns immediately after setup phase completes.
        /// </summary>
        /// <param name="rightAscension">Right ascension in hours</param>
        /// <param name="declination">Declination in degrees</param>
        /// <param name="tracking">Enable tracking after slew completes</param>
        /// <returns>Result of the setup phase</returns>
        public static async Task<SlewResult> SlewRaDecAsync(double rightAscension, double declination, bool tracking = false)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Ra/Dec Async (using SlewController): RA={Utilities.HoursToHMS(rightAscension, "h ", ":", "", 2)}, Dec={Utilities.DegreesToDMS(declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // NEW: Use SlewController - coordinates are [rightAscension, declination]
            var target = new[] { rightAscension, declination };
            return await SlewAsync(target, SlewType.SlewRaDec, tracking);
        }

        /// <summary>
        /// Starts slew with alt/az coordinates - synchronous version that blocks until complete.
        /// Used for synchronous ASCOM operations.
        /// </summary>
        /// <param name="altitude">Target altitude in degrees</param>
        /// <param name="azimuth">Target azimuth in degrees</param>
        public static void SlewAltAz(double altitude, double azimuth)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Alt/Az (using SlewController): Az={azimuth:F4}, Alt={altitude:F4}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // NEW: Use SlewController - coordinates are [azimuth, altitude]
            var target = new[] { azimuth, altitude };
            SlewSync(target, SlewType.SlewAltAz, tracking: false);
        }

        /// <summary>
        /// Starts slew with alt/az coordinates - async version for ITelescopeV4.
        /// Returns immediately after setup phase completes.
        /// </summary>
        /// <param name="altitude">Target altitude in degrees</param>
        /// <param name="azimuth">Target azimuth in degrees</param>
        /// <returns>Result of the setup phase</returns>
        public static async Task<SlewResult> SlewAltAzAsync(double altitude, double azimuth)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Alt/Az Async (using SlewController): Az={azimuth:F4}, Alt={altitude:F4}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // NEW: Use SlewController - coordinates are [azimuth, altitude]
            var target = new[] { azimuth, altitude };
            return await SlewAsync(target, SlewType.SlewAltAz, tracking: false);
        }        /// <summary>
                 /// Starts slew with primary/seconday internal coordinates, not mount positions
                 /// </summary>
                 /// <param name="primaryAxis"></param>
                 /// <param name="secondaryAxis"></param>
                 /// <param name="slewState"></param>
        public static void SlewAxes(double primaryAxis, double secondaryAxis, SlewType slewState, bool slewAsync = true, MountInstance? instance = null)
        {
            SlewMount(new Vector(primaryAxis, secondaryAxis), slewState, false, slewAsync, instance);
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
        private static void GoToAsync(double[] target, SlewType slewState, EventWaitHandle goToStarted, bool tracking = false, MountInstance? instance = null)
        {
            var effectiveInstance = instance ?? _defaultInstance!;
            MonitorEntry monitorItem;
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MonitorLog.GetCurrentMethod(),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Starting {slewState}|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            if (!effectiveInstance.IsMountRunning)  // M3: per-instance guard
            {
                goToStarted.Set();
                return;
            }

            effectiveInstance.InstanceCancelAllAsync();  // M3: per-instance cancel
                var swCts = Stopwatch.StartNew();
                while (effectiveInstance._ctsGoTo != null && swCts.ElapsedMilliseconds < 5000) Thread.Sleep(10);  // M3
                if (effectiveInstance._isSlewing)  // M3: per-instance
                {
                    effectiveInstance._slewState = SlewType.SlewNone;  // M3
                    var stopped = AxesStopValidate(effectiveInstance);
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

                effectiveInstance._slewState = slewState;  // M3
                var startingState = slewState;
                // Planetarium fix to set Tracking for non-ASCOM compliant programs - set true by GoToCoordinatesAsync()
                var trackingState = tracking || effectiveInstance.Tracking;  // M3
                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;
                effectiveInstance.SetTracking(false);  // M3
                if (slewState == SlewType.SlewRaDec)
                {
                    effectiveInstance.SkyPredictor.Set(effectiveInstance.TargetRa, effectiveInstance.TargetDec, effectiveInstance.RateRa, effectiveInstance.RateDec);  // M3
                }
                effectiveInstance._isSlewing = true;  // M3
                goToStarted.Set(); // Signal that GoTo has started so async ASCOM operations can return with Slewing = true

                // Assume fail
                try
                {
                    effectiveInstance._ctsGoTo = new CancellationTokenSource();  // M3
                var returnCode = 1;
                switch (effectiveInstance.Settings.Mount)  // M3
                {
                    case MountType.Simulator:
                        returnCode = effectiveInstance.SimGoTo(target, trackingState, slewState, effectiveInstance._ctsGoTo.Token);  // M3
                        break;
                    case MountType.SkyWatcher:
                        returnCode = effectiveInstance.SkyGoTo(target, trackingState, slewState, effectiveInstance._ctsGoTo.Token);  // M3
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();

                }

                // ToDo re-enable voice prompt later
                // TrackingSpeak = false;

                if (returnCode == 0)
                {
                    if (effectiveInstance._slewState == SlewType.SlewNone)  // M3
                    {
                        effectiveInstance.SetTracking(trackingState);  // M3
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
                            if (effectiveInstance.Settings.AlignmentMode == AlignmentMode.AltAz)  // M3
                            {
                                // update TargetRa and TargetDec after slewing with offset rates as per ASCOM spec
                                if (effectiveInstance.SkyPredictor.RatesSet)
                                {
                                    var targetRaDec = effectiveInstance.SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                                    effectiveInstance.TargetRa = targetRaDec[0];  // M3
                                    effectiveInstance.TargetDec = targetRaDec[1];  // M3
                                }

                                // use tracking to complete slew for Alt Az mounts
                                effectiveInstance.SkyPredictor.Set(effectiveInstance.TargetRa, effectiveInstance.TargetDec);  // M3
                                effectiveInstance.SetTracking(true);
                                effectiveInstance.TrackingMode = TrackingMode.AltAz;
                                SetTracking(effectiveInstance); var sw = Stopwatch.StartNew();  // M3
                                // wait before completing async slew, double time for low resolution mounts 
                                var highResMount = Conversions.StepPerArcSec(Math.Min(effectiveInstance._stepsPerRevolution[0], effectiveInstance._stepsPerRevolution[1])) > 5;  // M3
                                var waitTime = highResMount ? 2 * effectiveInstance.Settings.AltAzTrackingUpdateInterval : 4 * effectiveInstance.Settings.AltAzTrackingUpdateInterval;  // M3
                                while (sw.ElapsedMilliseconds < waitTime)
                                {
                                    if (effectiveInstance._ctsGoTo?.IsCancellationRequested == true)  // M3
                                    {
                                        // Stop current Alt Az tracking timed action
                                        effectiveInstance.StopAltAzTrackingTimer();  // M3
                                        // Prevent re-enabling by this thread
                                        trackingState = false;
                                        // Stop tracking motion 
                                        effectiveInstance.InstanceStopAxes();  // M3
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
                            effectiveInstance.AtPark = true;  // M3
                            effectiveInstance.SkyPredictor.Reset();
                            break;
                        case SlewType.SlewHome:
                            trackingState = false;
                            effectiveInstance.SkyPredictor.Reset();
                            break;
                        case SlewType.SlewHandpad:
                            // ensure tracking if enabled has the correct target
                            effectiveInstance.SkyPredictor.Set(effectiveInstance.RightAscensionXForm, effectiveInstance.DeclinationXForm);  // M3
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
                                $"{effectiveInstance._slewState} finished|code|{returnCode}|{Utilities.HoursToHMS(effectiveInstance.RightAscensionXForm, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(effectiveInstance.DeclinationXForm, " ", ":", "", 2)}|Actual|{effectiveInstance._actualAxisX}|{effectiveInstance._actualAxisY}"  // M3
                        };
                        MonitorLog.LogToMonitor(monitorItem);
                        effectiveInstance._slewState = SlewType.SlewNone;  // M3
                        // ToDo re-enable voice prompt later
                        // SpeakSlewEnd(startingState);
                        effectiveInstance.SetTracking(trackingState);  // M3
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
                    effectiveInstance._slewState = SlewType.SlewNone;  // M3
                    // ToDo re-enable voice prompt later
                    // SpeakSlewEnd(startingState);
                    effectiveInstance.SetTracking(false);  // M3
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
                if (effectiveInstance != null) effectiveInstance._rateMoveAxes = new Vector(0, 0);
                effectiveInstance._moveAxisActive = false;  // M3
                if (effectiveInstance != null)
                {
                    effectiveInstance.RateRa = 0.0;
                    effectiveInstance.RateDec = 0.0;
                }
                // Stop axes
                switch (effectiveInstance.Settings.Mount)  // M3
                {
                    case MountType.Simulator:
                        SimTasks(MountTaskName.StopAxes, effectiveInstance);
                        break;
                    case MountType.SkyWatcher:
                        SkyTasks(MountTaskName.StopAxes, effectiveInstance);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                effectiveInstance._slewState = SlewType.SlewNone;  // M3
                effectiveInstance.SetTracking(trackingState);  // M3
                // Some unknown exception
                if (!cancelled)
                    effectiveInstance._mountError = new Exception($"GoTo Async Error|{ex.Message}");  // M3
            }
            finally
            {
                effectiveInstance._ctsGoTo?.Dispose();  // M3
                effectiveInstance._ctsGoTo = null;  // M3
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
            // CRITICAL: Cancel SlewController operations FIRST
            if (_slewController != null)
            {
                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Cancelling SlewController operation"
                };
                MonitorLog.LogToMonitor(monitorItem);

                // Cancel synchronously - wait for it to complete
                _slewController.CancelCurrentSlewAsync().Wait();
            }

            // Cancel legacy async operations (pulse guiding, etc.)
            CancelAllAsync();
            // Stop all MoveAxis commands
            MoveAxisActive = false;
            if (_defaultInstance != null)
            {
                _defaultInstance._rateMoveAxes.X = 0.0;
                _defaultInstance._rateMoveAxes.Y = 0.0;
                _defaultInstance.RateRa = 0.0;
                _defaultInstance.RateDec = 0.0;
            }

            switch (_settings!.Mount)
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes, _defaultInstance!);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes, _defaultInstance!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (_settings!.AlignmentMode == AlignmentMode.AltAz)
            {
//                AxesStopValidate();
                // wait for the move to stop - physical overrun
                //var trackingRate = SkyGetRate();
                AxesRateOfChange.Reset();
                //do
                //{
                //    // Update mount velocity
                //    MountPositionUpdated = false;
                //    UpdateSteps();
                //    while (!MountPositionUpdated) Thread.Sleep(50);
                //    AxesRateOfChange.Update(_actualAxisX, _actualAxisY, HiResDateTime.UtcNow);
                //} while (AxesRateOfChange.AxisVelocity.Length > 0);
                _defaultInstance.SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
            // ToDo re-enable voice prompt later
            // TrackingSpeak = false;
            Tracking = tracking;
            // TrackingSpeak = true;

            // if (speak) { Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["vceAbortSlew"].ToString()); }
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "AbortSlew completed"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Stop Axes in a normal motion
        /// </summary>
        public static void StopAxes()
        {
            if (!IsMountRunning) { return; }

            if (_defaultInstance != null) _defaultInstance.AutoHomeStop = true;

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
            if (_defaultInstance != null)
            {
                _defaultInstance._rateMoveAxes.X = 0.0;
                _defaultInstance._rateMoveAxes.Y = 0.0;
                _defaultInstance.RateRa = 0.0;
                _defaultInstance.RateDec = 0.0;
            }

            if (!SkyServer.AxesStopValidate(_defaultInstance!))
            {
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        SimTasks(MountTaskName.StopAxes, _defaultInstance!);
                        break;
                    case MountType.SkyWatcher:
                        SkyTasks(MountTaskName.StopAxes, _defaultInstance!);
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
        /// Goto home slew - synchronous version that blocks until complete.
        /// </summary>
        public static void GoToHomeX()
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
                Message = "Slew to Home (using SlewController)"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // NEW: Use SlewController
            var target = new[] { HomeAxes.X, HomeAxes.Y };
            SlewSync(target, SlewType.SlewHome, tracking: false);
        }

        /// <summary>
        /// Goto home slew - async version for ITelescopeV4.
        /// </summary>
        public static async Task<SlewResult> GoToHome()
        {
            if (AtHome || SlewState == SlewType.SlewHome)
            {
                return SlewResult.Failed("Already at home or home slew in progress");
            }

            Tracking = false;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "Slew to Home (Async using SlewController)"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var target = new[] { HomeAxes.X, HomeAxes.Y };
            return await SlewAsync(target, SlewType.SlewHome, tracking: false);
        }

        /// <summary>
        /// Goto park slew - synchronous version that blocks until complete.
        /// </summary>
        public static void GoToPark()
        {
            Tracking = false;

            // Get position selected (could be set from UI or ASCOM)
            var ps = ParkSelected;
            if (ps == null) { return; }
            if (double.IsNaN(ps.X)) { return; }
            if (double.IsNaN(ps.Y)) { return; }
            SetParkAxis(ps.Name, ps.X, ps.Y);

            // Store for startup default position
            _settings!.ParkAxes = new[] { ps.X, ps.Y };
            _settings!.ParkName = ps.Name;

            Tracking = false;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Park (using SlewController): {ps.Name}|{ps.X}|{ps.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // ✅ NEW: Use SlewController
            var target = new[] { ps.X, ps.Y };
            SlewSync(target, SlewType.SlewPark, tracking: false);
        }

        /// <summary>
        /// Goto park slew - async version for ITelescopeV4.
        /// Returns immediately after setup phase completes.
        /// </summary>
        public static async Task<SlewResult> GoToParkAsync()
        {
            Tracking = false;

            var ps = ParkSelected;
            if (ps == null)
            {
                return SlewResult.Failed("No park position selected");
            }
            if (double.IsNaN(ps.X) || double.IsNaN(ps.Y))
            {
                return SlewResult.Failed("Invalid park coordinates");
            }

            SetParkAxis(ps.Name, ps.X, ps.Y);
            _settings!.ParkAxes = new[] { ps.X, ps.Y };
            _settings!.ParkName = ps.Name;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Park Async (using SlewController): {ps.Name}|{ps.X}|{ps.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var target = new[] { ps.X, ps.Y };
            return await SlewAsync(target, SlewType.SlewPark, tracking: false);
        }

        /// <summary>
        /// Park position selected set from UI or ASCOM, set using deep copy
        /// </summary>
        public static ParkPosition ParkSelected
        {
            get
            {
                if (_parkSelected == null)
                {
                    // Try to initialize from ParkName if available
                    if (!string.IsNullOrEmpty(_settings?.ParkName))
                    {
                        var found = _settings.ParkPositions?.Find(x => x.Name == _settings.ParkName);
                        if (found != null)
                        {
                            _parkSelected = new ParkPosition(found.Name, found.X, found.Y);
                            return _parkSelected;
                        }
                    }

                    // Fallback: try to use ParkAxes if populated
                    if (_settings?.ParkAxes != null && _settings.ParkAxes.Length >= 2)
                    {
                        _parkSelected = new ParkPosition("Park", _settings.ParkAxes[0], _settings.ParkAxes[1]);
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
            var context = AxesContext.FromSettings(_settings);
            var park = Axes.MountAxis2Mount(context);
            if (park == null) { return; }

            var p = new ParkPosition (name, park[0], park[1]);
            ParkSelected = p;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{park[0]}|{park[1]}|{_defaultInstance?._appAxes.X ?? 0.0}|{_defaultInstance?._appAxes.Y ?? 0.0}"
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

            var p = new ParkPosition(name, x, y );
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
                if (_defaultInstance != null)
                {
                    _defaultInstance.IsAutoHomeRunning = true;
                    _defaultInstance.LastAutoHomeError = null;
                }

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
                if (_defaultInstance != null) _defaultInstance.AutoHomeProgressBar = 0;
                var encoderTemp = _settings!.Encoders;
                if (Tracking) Tracking = false;
                // ToDo re-enable voice prompt later
                //Synthesizer.Speak(MediaTypeNames.Application.Current.Resources["btnAutoHomeStart"].ToString());
                //Synthesizer.VoicePause = true;

                AutoHomeResult raResult, decResult;

                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        var autoHomeSim = new AutoHomeSim(_settings, _defaultInstance!.MountQueueInstance!, _defaultInstance!);
                        raResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis1, degreeLimit));
                        if (_defaultInstance != null) _defaultInstance.AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    case MountType.SkyWatcher:
                        var autoHomeSky = new AutoHomeSky(_settings, _defaultInstance!.SkyQueueInstance!, _defaultInstance!);
                        raResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis1, degreeLimit));
                        if (_defaultInstance != null) _defaultInstance.AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _settings!.Encoders = encoderTemp;
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
                    ReSyncAxes(new ParkPosition("AutoHome", _settings!.AutoHomeAxisX, _settings!.AutoHomeAxisY), false);
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
                    if (_defaultInstance != null) _defaultInstance.LastAutoHomeError = ex;
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
                if (_defaultInstance != null) _defaultInstance.LastAutoHomeError = ex;
                if (_defaultInstance != null) _defaultInstance._mountError = ex;
            }
            finally
            {
                if (_defaultInstance != null)
                {
                    _defaultInstance.AutoHomeProgressBar = 100;
                    _defaultInstance.IsAutoHomeRunning = false;
                }
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

        #region SlewController Integration

        // Phase 5.3: SlewController — delegate to default instance
        private static SlewController? _slewController
        {
            get => _defaultInstance?._slewController;
        }

        /// <summary>
        /// Ensures the SlewController is initialized.
        /// </summary>
        private static void EnsureSlewController()
        {
            _defaultInstance?.EnsureSlewController();
        }

        /// <summary>
        /// Modern async slew implementation using SlewController.
        /// Returns immediately after setup phase completes (< 1 second).
        /// </summary>
        public static async Task<SlewResult> SlewAsync(
            double[] target,
            SlewType slewType,
            bool tracking = false)
        {
            if (_defaultInstance == null)
                return SlewResult.Failed("No mount instance available");
            return await _defaultInstance.SlewAsync(target, slewType, tracking);
        }

        /// <summary>
        /// Synchronous wrapper - blocks until slew completes.
        /// Used for synchronous ASCOM methods (FindHome, SlewToCoordinates).
        /// </summary>
        public static void SlewSync(
            double[] target,
            SlewType slewType,
            bool tracking = false)
        {
            if (_defaultInstance == null)
                throw new InvalidOperationException("No mount instance available");
            _defaultInstance.SlewSync(target, slewType, tracking);
        }

        /// <summary>
        /// Wait for current slew to complete (for async operations that need completion)
        /// </summary>
        public static async Task WaitForSlewCompletionAsync()
        {
            if (_defaultInstance != null)
                await _defaultInstance.WaitForSlewCompletionAsync();
        }

        /// <summary>
        /// Complete park operation - sets AtPark, stops tracking, resets predictor
        /// </summary>
        public static void CompletePark()
        {
            AtPark = true;
            Tracking = false;
            _defaultInstance.SkyPredictor.Reset();

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "Park completed - AtPark set, tracking disabled, predictor reset"
            };
            MonitorLog.LogToMonitor(monitorItem);
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

            AltAzSync = new Vector(targetAltitude, targetAzimuth);
            switch (_settings!.Mount)
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes, _defaultInstance!);
                    SimTasks(MountTaskName.SyncAltAz, _defaultInstance!);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes, _defaultInstance!);
                    SkyTasks(MountTaskName.SyncAltAz, _defaultInstance!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            WaitMountPositionUpdated();

            if (trackingstate)
            {
                // Calculate RA/Dec from synced Alt/Az coordinates
                // The mount has just synced to targetAltitude/targetAzimuth, so convert those to RA/Dec
                var internalAltAz = Transforms.CoordTypeToInternal(targetAzimuth, targetAltitude);
                var raDec = Coordinate.AltAz2RaDec(internalAltAz.X, internalAltAz.Y, SiderealTime, _settings!.Latitude);

                // Set predictor to the RA/Dec corresponding to synced Alt/Az
                // NOTE: Use SetTrackingDirect to avoid Tracking property's _defaultInstance.SkyPredictor.Reset()
                _defaultInstance.SkyPredictor.Set(raDec[0], raDec[1]);
                SetTrackingDirect(true, TrackingMode.AltAz);

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

            switch (_settings!.Mount)
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes, _defaultInstance!);
                    // ToDo: Remove if not needed
                    //if (AlignmentModel.IsAlignmentOn)
                    //{
                    //    AddAlignmentPoint();
                    //}
                    //else
                    //{
                        SimTasks(MountTaskName.SyncTarget, _defaultInstance!);
                    //}
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes, _defaultInstance!);
                    // ToDo: Remove if not needed
                    //if (AlignmentModel.IsAlignmentOn)
                    //{
                    //    AddAlignmentPoint();
                    //}
                    //else
                    //{
                        SkyTasks(MountTaskName.SyncTarget, _defaultInstance!);
                    //}

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            WaitMountPositionUpdated();

            if (trackingstate)
            {
                if (_settings!.AlignmentMode == AlignmentMode.AltAz)
                {
                    // set up tracking for Alt Az
                    // NOTE: Use SetTrackingDirect to avoid Tracking property's _defaultInstance.SkyPredictor.Reset()
                    _defaultInstance.SkyPredictor.Set(TargetRa, TargetDec);
                    SetTrackingDirect(true, TrackingMode.AltAz);
                }
                else
                {
                    // Polar/GermanPolar modes - safe to use Tracking property
                    Tracking = true;
                }
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
            if (!_settings!.SyncLimitOn) { return true; }
            if (_settings!.NoSyncPastMeridian) { return false; } // add more checks later if needed

            //convert ra dec to mount XY positions
            var context = AxesContext.FromSettings(_settings);
            var xy = Axes.RaDecToAxesXy(new[] { ra, dec }, context);
            //convert to app coordinates
            var target = Axes.AxesMountToApp(GetSyncedAxes(xy), context);

            //get current mount position in app coordinates
            var current = new[] { _defaultInstance?._appAxes.X ?? 0.0, _defaultInstance?._appAxes.Y ?? 0.0 };
            //compare ra dec / az alt to current mount position
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > _settings!.SyncLimit || Math.Abs(b) > _settings!.SyncLimit);
            if (ret) return true;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{_settings!.SyncLimit}"
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
            if (!_settings!.SyncLimitOn) { return true; }
            if (_settings!.NoSyncPastMeridian) { return false; } // add more checks later if needed

            //convert ra dec to mount XY positions
            var context = AxesContext.FromSettings(_settings);
            var xy = Axes.AzAltToAxesXy(new[] { az, alt }, context);
            //convert to app coordinates
            var target = Axes.AxesMountToApp(GetSyncedAxes(xy), context);

            //get current mount position in app coordinates
            var current = new[] { _defaultInstance?._appAxes.X ?? 0.0, _defaultInstance?._appAxes.Y ?? 0.0 };

            if (_settings!.AlignmentMode == AlignmentMode.AltAz)
            {
                target[0] = az;
                target[1] = alt;
                current[0] = Range.Range360(_defaultInstance?._appAxes.X ?? 0.0);
                current[1] = _defaultInstance?._appAxes.Y ?? 0.0;
            }

            //compare ra dec to current position
            var a = Math.Abs(target[0]) - Math.Abs(current[0]);
            var b = Math.Abs(target[1]) - Math.Abs(current[1]);
            var ret = !(Math.Abs(a) > _settings!.SyncLimit || Math.Abs(b) > _settings!.SyncLimit);

            if (ret) return true;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{xy[0]}|{xy[1]}|{target[0]}|{target[1]}|{current[0]}|{current[1]}|{_settings!.SyncLimit}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            return false;
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
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphere→per-instance
                                    _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis2, decGuideRate, duration,
                                        inst!._ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphere→per-instance
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
                                    if (!(inst!.Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate); // J1: SouthernHemisphere→per-instance
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
                        if (inst!.Settings.Latitude < 0) // J1: SouthernHemisphere→per-instance
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

            return ax != 0 && ax <= _settings!.NumMoveAxis;
        }

        /// <summary>
        /// Status of primary axis move
        /// </summary>
        public static bool MovePrimaryAxisActive => (_defaultInstance?._rateMoveAxes.X ?? 0.0) != 0.0;

        /// <summary>
        /// Status of secondary axis move
        /// </summary>
        public static bool MoveSecondaryAxisActive => (_defaultInstance?._rateMoveAxes.Y ?? 0.0) != 0.0;

        public static bool MoveAxisActive
        {
            get => _defaultInstance?._moveAxisActive ?? false;
            set
            {
                if (_defaultInstance == null) return;
                _defaultInstance._moveAxisActive = value;
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
            switch (_settings!.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return GetAlternatePositionAltAz(position);
                case AlignmentMode.Polar:
                    return GetAlternatePositionPolar(position);
                case AlignmentMode.GermanPolar:
                    return GetAlternatePositionGEM(position);
                default:
                    throw new ArgumentOutOfRangeException("AlignmentMode", _settings!.AlignmentMode, "Unsupported alignment mode for alternate position calculation.");
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
            // See if the target is within flip angle limits
            if (!IsWithinFlipLimits(position)) { return null; }
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            if (!IsWithinFlipLimits(alt)) { return null; }

            var cl = ChooseClosestPosition(_defaultInstance?._actualAxisX ?? 0.0, position, alt);  //choose the closest angle to slew 
            if (FlipOnNextGoto) // implement the forced flip for a goto
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
                    Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
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
            // See if the target is within flip angle limits
            if (!IsWithinFlipLimits(position)) { return null; }
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            var cl = ChooseClosestPosition(_defaultInstance?._actualAxisX ?? 0.0, position, alt);  //choose the closest angle to slew 
            if (FlipOnNextGoto) // implement the forced flip for a goto
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
                    Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
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
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            alt[0] = Range.Range180(alt[0]); // convert to polar position

            // Check target and altTarget are within hardware limits
            var altOk = IsTargetWithinLimits(alt);
            var posOk = IsTargetWithinLimits(position);

            if (!altOk) return null; // alternate target position not within limits, return null
            if (posOk && altOk)
            {
                var cl = ChooseClosestPositionPolar([_defaultInstance?._actualAxisX ?? 0.0, _defaultInstance?._actualAxisY ?? 0.0], position, alt);  //choose the closest angle to slew 
                if (FlipOnNextGoto) // implement the forced flip for a goto
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
                        Message = $"flip|{cl}|{_defaultInstance?._actualAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
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
            if (_settings!.AlignmentMode == AlignmentMode.AltAz)
            {
                return PointingState.Unknown;
            }
            var context = AxesContext.FromSettings(_settings);
            var flipReq = Axes.IsFlipRequired(new[] { rightAscension, declination }, context);

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
                                var mq = inst!.MountQueueInstance; // N4: null guard — MountStop may race here
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
                                    ? GetRaRateDirection(inst?.RateRa ?? 0.0)
                                    : 0.0;
                                _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, raRate);
                                if (inst._rateMoveAxes.Y == 0.0) // Set Dec tracking rate offset (0 if not sidereal)
                                {
                                    var decRate = currentTrackingMode != TrackingMode.Off
                                        ? GetDecRateDirection(inst?.RateDec ?? 0.0)
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
                        var sq = inst!.SkyQueueInstance; // N4: null guard — MountStop may race here
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
        internal static void SetAltAzTrackingRates(AltAzTrackingType altAzTrackingType, MountInstance? instance = null)
        {
            var inst = instance ?? _defaultInstance!;
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
                        DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(_settings!.AltAzTrackingUpdateInterval);
                        var raDec = inst.SkyPredictor.GetRaDecAtTime(nextTime);
                        // get required target position in topo coordinates
                        var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1]);
                        var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, GetLocalSiderealTime(nextTime), _settings!.Latitude);
                        Array.Reverse(skyTarget);
                        skyTarget = GetSyncedAxes(skyTarget);
                        var rawPositions = new[] { inst.ConvertStepsToDegrees(steps[0], 0), inst.ConvertStepsToDegrees(steps[1], 1) };
                        delta[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                        delta[1] = Range.Range180((skyTarget[1] - rawPositions[1]));
                        const double milliSecond = 0.001;
                        inst._skyTrackingRate = new Vector(
                            delta[0] / (_settings!.AltAzTrackingUpdateInterval * milliSecond),
                            delta[1] / (_settings!.AltAzTrackingUpdateInterval * milliSecond)
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
                case AltAzTrackingType.Rate:
                    inst._skyTrackingRate = ConvertRateToAltAz(CurrentTrackingRate(inst), 0.0, inst.DeclinationXForm);
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
        internal static double GetDecRateDirection(double rate)
        {
            bool moveNorth = rate > 0;
            bool isEast = SideOfPier == PointingState.Normal;
            bool isWest = SideOfPier == PointingState.ThroughThePole;
            bool invert = false;
            rate = Math.Abs(rate);

            switch (_settings!.Mount)
            {
                case MountType.Simulator:
                case MountType.SkyWatcher:
                    switch (_settings!.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            // No direction change needed for AltAz
                            break;

                        case AlignmentMode.Polar:
                            if (isEast || isWest)
                            {
                                if (_settings!.Mount == MountType.Simulator)
                                {
                                    if (_settings!.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (_settings!.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    if (_settings!.PolarMode == PolarMode.Left) invert = !invert;
                                }
                            }
                            break;
                        case AlignmentMode.GermanPolar:
                            if (isEast || isWest)
                            {
                                if (_settings!.Mount == MountType.Simulator)
                                {
                                    if (_settings!.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else // SkyWatcher
                                {
                                    if (_settings!.Latitude < 0)
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
        internal static double GetRaRateDirection(double rate)
        {
            var east = rate > 0;
            rate = Math.Abs(rate);

            if (_settings!.Latitude < 0)
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
        /// AltAz and Polar checks for elevation limits and azimuth slewing limits through flip angle + slew limit
        /// The flip angle is 180 degrees away from the home angle and the slew limit can positive or negative.
        /// GEM mounts check the hour angle limit.
        /// </summary>
        internal static void CheckAxisLimits()
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
            var totLimit = _settings!.HourAngleLimit + _settings!.AxisTrackingLimit;

            // Check the ranges of the axes primary axis must be in the range plus/minus Flip Angle for AltAz or Polar
            // and -hourAngleLimit to 180 + hourAngleLimit for german polar
            switch (_settings!.AlignmentMode)
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
                    // AWW ToDo fix this after debug
                    // Slew limit > 180 the mount can move past the meridian angle, and two equivalent positions, 
                    // eg +205 degrees and -155 degrees, are equivalent for a slew angle limit of 200 degrees
                    // limitHitX = LimitStatus.AtLowerLimitAxisX || LimitStatus.AtUpperLimitAxisX;
                    // The secondary (Alt / Dec) axis must be in the range LowerLimitAxisY to UpperLimitAxisY
                    // limitHitY = LimitStatus.AtLowerLimitAxisY || LimitStatus.AtUpperLimitAxisY;
                    // meridianLimit = limitHitX;
                    // limitHit = LimitStatus.AtLimit;
                    break;
                // Check the ranges of the primary axis and -hourAngleLimit to 180 + hourAngleLimit for german polar
                case AlignmentMode.GermanPolar:
                    {
                        // the primary axis needs to be in the range -180 to +180 to correspond with hour angles of -12 to 12.
                        // check if we have hit the hour angle limit
                        var appX = _defaultInstance?._appAxes.X ?? 0.0;
                        if (_settings!.Latitude < 0)
                        {
                            if (appX >= _settings!.HourAngleLimit ||
                                appX <= -_settings!.HourAngleLimit - 180)
                            {
                                limitHit = true;
                            }

                            // Check tracking limit
                            if (appX >= totLimit || appX <= -totLimit - 180)
                            {
                                meridianLimit = true;
                            }
                        }
                        else
                        {
                            if (appX >= _settings!.HourAngleLimit + 180 ||
                                appX <= -_settings!.HourAngleLimit)
                            {
                                limitHit = true;
                            }

                            //Check Tracking Limit
                            if (appX >= totLimit + 180 || appX <= -totLimit)
                            {
                                meridianLimit = true;
                            }
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Horizon Limit Test
            if (_settings!.HzLimitPark || _settings!.HzLimitTracking) // Skip all if set to do nothing
            {
                switch (_settings!.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        if ((Altitude <= _settings!.AxisHzTrackingLimit
                             || Altitude <= _settings!.AxisLowerLimitY
                             || Altitude >= _settings!.AxisUpperLimitY) && Tracking)
                        {
                            limitHit = true;
                            horizonLimit = true;
                        }
                        break;
                    case AlignmentMode.Polar:
                        // AWW ToDo fix this after debug
                        //if (Altitude <= _settings!.AxisHzTrackingLimit && Tracking)
                        //{
                        //    limitHit = true;
                        //    horizonLimit = true;
                        //}
                        break;
                    case AlignmentMode.GermanPolar:
                        if (SideOfPier == PointingState.Normal && Altitude <= _settings!.AxisHzTrackingLimit && Tracking)
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
                    $"Meridian Limit Alarm: Park: {_settings!.LimitPark} | Position: {_settings!.ParkLimitName} | Stop Tracking: {_settings!.LimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);

                if (Tracking && _settings!.LimitTracking)
                {
                    Tracking = false;
                } // turn off tracking

                if (_settings!.LimitPark && (_defaultInstance?._slewState ?? SlewType.SlewNone) != SlewType.SlewPark) // only hit this once while in limit
                {
                    var found = _settings!.ParkPositions.Find(x => x.Name == _settings!.ParkLimitName);
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
                    $"Horizon Limit Alarm: Park: {_settings!.HzLimitPark} | Position:{_settings!.ParkHzLimitName} | Stop Tracking:{_settings!.HzLimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);

                if (Tracking && _settings!.HzLimitTracking)
                {
                    Tracking = false;
                } // turn off tracking

                if (_settings!.HzLimitPark && (_defaultInstance?._slewState ?? SlewType.SlewNone) != SlewType.SlewPark) // only hit this once while in limit
                {
                    var found = _settings!.ParkPositions.Find(x => x.Name == _settings!.ParkHzLimitName);
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
