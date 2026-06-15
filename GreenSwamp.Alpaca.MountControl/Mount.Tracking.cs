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

// Per-instance tracking and rate methods.
// Replaces SkyServer.SetTracking(this), SetGuideRates(this), CalcCustomTrackingOffset(this),
// SetSlewRates(rate, this). SkyGetRate() fixes the device-0 AlignmentMode bias.

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Represents a per-device instance of a telescope mount controller, implementing the
    /// <see cref="IMountController"/> interface for both SkyWatcher hardware and the built-in simulator.
    /// <para>
    /// Each <see cref="Mount"/> owns its full lifecycle — serial/UDP connection, hardware command
    /// queues (<see cref="SkyQueue"/> / <see cref="SimQueue"/>), coordinate pipeline,
    /// tracking timers, slew controller, and all associated cancellation tokens — so that multiple
    /// physical devices can operate concurrently without shared state.
    /// </para>
    /// <para>
    /// Key responsibilities:
    /// <list type="bullet">
    ///   <item><description>Connects to and disconnects from mount hardware (COM port or UDP/WiFi).</description></item>
    ///   <item><description>Runs the per-tick update loop (<see cref="OnUpdateServerEvent"/>) that converts
    ///   raw hardware step counts to topocentric RA/Dec, Alt/Az, and app-axis coordinates.</description></item>
    ///   <item><description>Manages sidereal, AltAz, and custom tracking modes via
    ///   <see cref="ApplyTracking"/> and the per-instance AltAz tracking timer.</description></item>
    ///   <item><description>Executes GoTo slews (coarse + precision pass) for RA/Dec, Alt/Az,
    ///   Home, and Park targets through <see cref="SlewAsync"/> / <see cref="SlewSync"/>.</description></item>
    ///   <item><description>Handles pulse guiding (equatorial and AltAz predictor-based) with
    ///   cancellation tokens to prevent cross-device interference.</description></item>
    ///   <item><description>Enforces meridian and horizon axis limits and reacts with configurable
    ///   stop-tracking or auto-park responses.</description></item>
    ///   <item><description>Exposes ASCOM-compliant properties and bridge methods consumed by
    ///   <c>Telescope.cs</c> and the Blazor UI without routing through the static <c>SkyServer</c> façade.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This is a partial class; additional members are defined in
    /// <c>Mount.Tracking.cs</c> and related sibling files.
    /// </para>
    /// </summary>
    public partial class Mount
    {
        #region Tracking and Rate Methods (Phase M1)

        private const double SiderealRate = 15.0410671786691;

        /// <summary>
        /// Updates tracking rates for mount axes. When changedAxis is specified, only that axis is updated.
        /// When null, both axes are updated (full tracking update). This eliminates redundant hardware commands.
        /// </summary>
        /// <param name="changedAxis">The axis that changed (Primary=RA, Secondary=Dec). Null for full update.</param>
        /// <param name="waitForQueueCompletion">When true, waits until queued tracking commands complete.</param>
        internal void SetTracking(TelescopeAxis? changedAxis = null, bool waitForQueueCompletion = false)
        {
            if (!IsMountRunning) return;

            double rateChange = 0;
            Vector rate = default;
            var currentTrackingMode = TrackingMode;
            switch (currentTrackingMode)
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

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var simCommands = new List<ICommand<Actions>>();
                    switch (Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor);
                                if (_altAzTrackingTimer?.IsRunning != true) StartAltAzTrackingTimerInternal();
                            }
                            else
                            {
                                if (_altAzTrackingTimer?.IsRunning == true) StopAltAzTrackingTimerInternal();
                                _skyTrackingRate = new Vector(0, 0);
                            }
                            rate = SkyGetRate();
                            {
                                var mq = SimQueue;
                                if (mq == null) return;
                                // Only queue Axis1 if: no specific axis requested OR the changed axis is Primary (RA)
                                if (_rateMoveAxes.X == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Primary))
                                {
                                    var command = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rate.X);
                                    if (waitForQueueCompletion) simCommands.Add(command);
                                }
                                // Only queue Axis2 if: no specific axis requested OR the changed axis is Secondary (Dec)
                                if (_rateMoveAxes.Y == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Secondary))
                                {
                                    var command = new CmdAxisTracking(mq.NewId, mq, Axis.Axis2, rate.Y);
                                    if (waitForQueueCompletion) simCommands.Add(command);
                                }
                            }
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            {
                                var mq = SimQueue!;
                                // Only queue Axis1 for base tracking if: no specific axis requested OR the changed axis is Primary (RA)
                                if (_rateMoveAxes.X == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Primary))
                                {
                                    var command = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
                                    if (waitForQueueCompletion) simCommands.Add(command);
                                }
                                
                                var raRate = currentTrackingMode != TrackingMode.Off
                                    ? GetRaRateDirection(RateRa) : 0.0;
                                {
                                    var command = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, raRate);
                                    if (waitForQueueCompletion) simCommands.Add(command);
                                }
                                
                                // Only queue Axis2 if: no specific axis requested OR the changed axis is Secondary (Dec)
                                if (_rateMoveAxes.Y == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Secondary))
                                {
                                    var decRate = currentTrackingMode != TrackingMode.Off
                                        ? GetDecRateDirection(RateDec) : 0.0;
                                    var command = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, decRate);
                                    if (waitForQueueCompletion) simCommands.Add(command);
                                }
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (waitForQueueCompletion && SimQueue != null)
                    {
                        foreach (var command in simCommands)
                        {
                            var result = SimQueue.GetCommandResult(command);
                            if (!result.Successful)
                                throw result.Exception ?? new InvalidOperationException($"Tracking command {result.Id} failed.");
                        }
                    }
                    break;

                case MountType.SkyWatcher:
                    var skyCommands = new List<ICommand<SkyWatcher>>();
                    switch (Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SetAltAzTrackingRates(AltAzTrackingType.Predictor);
                                if (_altAzTrackingTimer?.IsRunning != true) StartAltAzTrackingTimerInternal();
                            }
                            else
                            {
                                if (_altAzTrackingTimer?.IsRunning == true) StopAltAzTrackingTimerInternal();
                                _skyTrackingRate = new Vector(0, 0);
                            }
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            _skyTrackingRate = new Vector(rateChange, 0);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    rate = SkyGetRate();
                    {
                        var sq = SkyQueue;
                        if (sq == null) return;
                        
                        // Only queue Axis1 if: no specific axis requested (full update) OR the changed axis is Primary (RA)
                        if (_rateMoveAxes.X == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Primary))
                        {
                            var command = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);
                            if (waitForQueueCompletion) skyCommands.Add(command);
                        }
                        
                        // Only queue Axis2 if: no specific axis requested (full update) OR the changed axis is Secondary (Dec)
                        if (_rateMoveAxes.Y == 0.0 && (!changedAxis.HasValue || changedAxis.Value == TelescopeAxis.Secondary))
                        {
                            var command = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);
                            if (waitForQueueCompletion) skyCommands.Add(command);
                        }

                        if (waitForQueueCompletion)
                        {
                            foreach (var command in skyCommands)
                            {
                                var result = sq.GetCommandResult(command);
                                if (!result.Successful)
                                    throw result.Exception ?? new InvalidOperationException($"Tracking command {result.Id} failed.");
                            }
                        }
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{currentTrackingMode}|{rateChange * 3600}|{_skyTrackingOffset[0]}|{_skyTrackingOffset[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        private Vector SkyGetRate()
        {
            var change = new Vector();
            change += _skyTrackingRate;
            change += SkyHcRate;
            change.X += _rateMoveAxes.X;
            change.X += Settings.AlignmentMode != AlignmentMode.AltAz ? GetRaRateDirection(RateRa) : 0;
            change.Y += _rateMoveAxes.Y;
            change.Y += Settings.AlignmentMode != AlignmentMode.AltAz ? GetDecRateDirection(RateDec) : 0;
            CheckAxisLimits();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{change}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return change;
        }

        internal void SetGuideRates()
        {
            var rate = CurrentTrackingRate();
            GuideRateRa = rate * Settings.GuideRateOffsetX;
            GuideRateDec = rate * Settings.GuideRateOffsetY;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{GuideRateRa * 3600}|{GuideRateDec * 3600}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        internal void CalcCustomTrackingOffset()
        {
            _trackingOffsetRate = new Vector(0.0, 0.0);
            if (Settings.Mount != MountType.SkyWatcher) return;
            if (Settings.CustomGearing == false) return;

            var ratioFactor = (double)_stepsTimeFreq[0] / _stepsPerRevolution[0] * 1296000.0;
            var siderealI = ratioFactor / SiderealRate;
            siderealI += Settings.CustomRaTrackingOffset;
            var newRate = ratioFactor / siderealI;
            _trackingOffsetRate.X = SiderealRate - newRate;

            ratioFactor = (double)_stepsTimeFreq[1] / _stepsPerRevolution[1] * 1296000.0;
            siderealI = ratioFactor / SiderealRate;
            siderealI += Settings.CustomDecTrackingOffset;
            newRate = ratioFactor / siderealI;
            _trackingOffsetRate.Y = SiderealRate - newRate;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{_trackingOffsetRate.X}|{_trackingOffsetRate.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        internal void SetSlewRates(double maxRate)
        {
            _slewSpeedOne   = Math.Round(maxRate * 0.0034, 3);
            _slewSpeedTwo   = Math.Round(maxRate * 0.0068, 3);
            _slewSpeedThree = Math.Round(maxRate * 0.047,  3);
            _slewSpeedFour  = Math.Round(maxRate * 0.068,  3);
            _slewSpeedFive  = Math.Round(maxRate * 0.2,    3);
            _slewSpeedSix   = Math.Round(maxRate * 0.4,    3);
            _slewSpeedSeven = Math.Round(maxRate * 0.8,    3);
            _slewSpeedEight = Math.Round(maxRate * 1.0,    3);
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{_slewSpeedOne}|{_slewSpeedTwo}|{_slewSpeedThree}|{_slewSpeedFour}|{_slewSpeedFive}|{_slewSpeedSix}|{_slewSpeedSeven}|{_slewSpeedEight}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion

        #region Tracking Rate Helpers (formerly SkyServer.TelescopeAPI)

        /// <summary>Calculates the current RA tracking rate (arc seconds per second).</summary>
        internal double CurrentTrackingRate()
        {
            double rate = Settings.TrackingRate switch
            {
                DriveRate.Sidereal => Settings.SiderealRate,
                DriveRate.Solar    => Settings.SolarRate,
                DriveRate.Lunar    => Settings.LunarRate,
                DriveRate.King     => Settings.KingRate,
                _                  => throw new ArgumentOutOfRangeException()
            };

            if (rate < SiderealRate * 2 & rate != 0)
                rate += _trackingOffsetRate.X;

            rate /= 3600;
            if (Settings.RaTrackingOffset <= 0) { return rate; }
            var offsetRate = rate * (Convert.ToDouble(Settings.RaTrackingOffset) / 100000);
            rate += offsetRate;
            return rate;
        }

        /// <summary>Update AltAz tracking rates including delta for tracking error.</summary>
        internal void SetAltAzTrackingRates(AltAzTrackingType altAzTrackingType)
        {
            switch (altAzTrackingType)
            {
                case AltAzTrackingType.Predictor:
                    double[] delta = [0.0, 0.0];
                    if (SkyPredictor.RaDecSet)
                    {
                        WaitUpdateMountPosition(500);
                        var steps = _steps;
                        DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(Settings.AltAzTrackingUpdateInterval);
                        var raDec = SkyPredictor.GetRaDecAtTime(nextTime);
                        var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1], settings: Settings);
                        var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, GetLocalSiderealTime(nextTime), Settings.Latitude);
                        Array.Reverse(skyTarget);
                        // GetSyncedAxes is a pass-through; inlined here
                        var rawPositions = new[] { ConvertStepsToDegrees(steps[0], 0), ConvertStepsToDegrees(steps[1], 1) };
                        delta[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                        delta[1] = Range.Range180((skyTarget[1] - rawPositions[1]));
                        const double milliSecond = 0.001;
                        _skyTrackingRate = new Vector(
                            delta[0] / (Settings.AltAzTrackingUpdateInterval * milliSecond),
                            delta[1] / (Settings.AltAzTrackingUpdateInterval * milliSecond)
                        );
                        var monitorItem = new MonitorEntry
                        {
                            Datetime = HiResDateTime.UtcNow,
                            Device = MonitorDevice.Server,
                            Category = MonitorCategory.Server,
                            Type = MonitorType.Data,
                            Method = MethodBase.GetCurrentMethod()?.Name,
                            Thread = Environment.CurrentManagedThreadId,
                            Message = $"Ra:{internalRaDec.X}|Dec:{internalRaDec.Y}|Azimuth delta:{delta[0]}|Altitude delta:{delta[1]}"
                        };
                        MonitorLog.LogToMonitor(monitorItem);
                    }
                    break;
            }
        }

        /// <summary>Set mechanical direction for dec rate. Positive direction means go mechanical north.</summary>
        internal double GetDecRateDirection(double rate)
        {
            bool moveNorth = rate > 0;
            bool isEast = SideOfPier == PointingState.Normal;
            bool isWest = SideOfPier == PointingState.ThroughThePole;
            bool invert = false;
            rate = Math.Abs(rate);

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                case MountType.SkyWatcher:
                    switch (Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            break;
                        case AlignmentMode.Polar:
                            if (isEast || isWest)
                            {
                                if (Settings.Mount == MountType.Simulator)
                                {
                                    if (Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else
                                {
                                    if (Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                    if (Settings.PolarMode == PolarMode.Left) invert = !invert;
                                }
                            }
                            break;
                        case AlignmentMode.GermanPolar:
                            if (isEast || isWest)
                            {
                                if (Settings.Mount == MountType.Simulator)
                                {
                                    if (Settings.Latitude < 0)
                                        invert = (isEast && moveNorth) || (isWest && !moveNorth);
                                    else
                                        invert = (isEast && !moveNorth) || (isWest && moveNorth);
                                }
                                else
                                {
                                    if (Settings.Latitude < 0)
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

        /// <summary>Set mechanical direction for ra rate. Positive direction means go mechanical east.</summary>
        internal double GetRaRateDirection(double rate)
        {
            var east = rate > 0;
            rate = Math.Abs(rate);

            if (Settings.Latitude < 0)
            {
                if (!east) { rate = -rate; }
            }
            else
            {
                if (east) { rate = -rate; }
            }

            return rate;
        }

        /// <summary>Pulse guide command on this mount instance.</summary>
        public void PulseGuide(GuideDirection direction, int duration, double altRate)
        {
            if (!IsMountRunning) throw new Exception("Mount not running");

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{direction}|{duration}" };
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
                    _hcPrevMoveDec = null;
                    var decGuideRate = useAltRate ? altRate : Math.Abs(GuideRateDec);
                    switch (Settings.AlignmentMode)
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
                            if (Settings.PolarMode == PolarMode.Left) decGuideRate = -decGuideRate; // Swap direction because primary OTA is flipped
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
                    if (direction != _lastDecDirection) decBacklashAmount = Settings.DecBacklash;
                    _lastDecDirection = direction;
                    _ctsPulseGuideDec?.Cancel();
                    _ctsPulseGuideDec?.Dispose();
                    _ctsPulseGuideDec = new CancellationTokenSource();

                    switch (Settings.Mount)
                    {
                        case MountType.Simulator:
                        {
                            var mq = SimQueue!;
                            switch (Settings.AlignmentMode)
                            {
                                case AlignmentMode.AltAz:
                                    PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, SimPulseGoto, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.Polar:
                                    if (!(Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis2, decGuideRate, duration, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    if (!(Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis2, decGuideRate, duration, _ctsPulseGuideDec.Token);
                                    break;
                                default:
                                    break;
                            }
                            break;
                        }
                        case MountType.SkyWatcher:
                        {
                            var sq = SkyQueue!;
                            switch (Settings.AlignmentMode)
                            {
                                case AlignmentMode.AltAz:
                                    PulseGuideAltAz((int)Axis.Axis2, decGuideRate, duration, SkyPulseGoto, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.Polar:
                                    if (!(Settings.Latitude < 0)) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
                                    _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis2, decGuideRate, duration, decBacklashAmount, _ctsPulseGuideDec.Token);
                                    break;
                                case AlignmentMode.GermanPolar:
                                    _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis2, decGuideRate, duration, decBacklashAmount, _ctsPulseGuideDec.Token);
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
                        IsPulseGuidingRa = false;
                        return;
                    }
                    IsPulseGuidingRa = true;
                    _hcPrevMoveRa = null;
                    var raGuideRate = useAltRate ? altRate : Math.Abs(GuideRateRa);
                    if (Settings.AlignmentMode != AlignmentMode.AltAz)
                    {
                        if (Settings.Latitude < 0)
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

                    _ctsPulseGuideRa?.Cancel();
                    _ctsPulseGuideRa?.Dispose();
                    _ctsPulseGuideRa = new CancellationTokenSource();
                    switch (Settings.Mount)
                    {
                        case MountType.Simulator:
                            if (Settings.AlignmentMode == AlignmentMode.AltAz)
                            {
                                PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, SimPulseGoto, _ctsPulseGuideRa.Token);
                            }
                            else
                            {
                                var mq = SimQueue!;
                                _ = new CmdAxisPulse(mq.NewId, mq, Axis.Axis1, raGuideRate, duration, _ctsPulseGuideRa.Token);
                            }
                            break;
                        case MountType.SkyWatcher:
                            if (Settings.AlignmentMode == AlignmentMode.AltAz)
                            {
                                PulseGuideAltAz((int)Axis.Axis1, raGuideRate, duration, SkyPulseGoto, _ctsPulseGuideRa.Token);
                            }
                            else
                            {
                                var sq = SkyQueue!;
                                _ = new SkyAxisPulse(sq.NewId, sq, Axis.Axis1, raGuideRate, duration, 0, _ctsPulseGuideRa.Token);
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
    }
}