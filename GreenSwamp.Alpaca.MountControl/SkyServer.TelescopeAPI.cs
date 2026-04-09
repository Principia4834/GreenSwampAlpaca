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

        #region ASCOM State Properties

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
            var cl = ChooseClosestPosition(context.AppAxisX ?? 0.0, position, alt);
            if (context.FlipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                    Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{context.AppAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                });
            }
            if (cl != "b") { return null; }
            return alt;
        }

        private static double[] GetAlternatePositionAltAz(double[] position, AxesContext context)
        {
            if (!context.IsWithinFlipLimits(position)) { return null; }
            var alt = Axes.GetAltAxisPosition(position, context);
            var cl = ChooseClosestPosition(context.AppAxisX ?? 0.0, position, alt);
            if (context.FlipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                    Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{context.AppAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
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
                var cl = ChooseClosestPositionPolar([context.AppAxisX ?? 0.0, context.AppAxisY ?? 0.0], position, alt);
                if (context.FlipOnNextGoto)
                {
                    cl = cl == "a" ? "b" : "a";
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"flip|{cl}|{context.AppAxisX ?? 0.0}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
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
        public static void PulseGuide(GuideDirection direction, int duration, double altRate, Mount? instance = null) // J1: instance-aware
        {
            var inst = instance;
            var settings = instance?.Settings;
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
                            var mq = inst!.SimQueue!; // J1: per-instance
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
                            var sq = inst!.SkyQueue!; // J1: per-instance
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
                                var mq = inst!.SimQueue!; // J1: per-instance
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
                                var sq = inst!.SkyQueue!; // J1: per-instance
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
        /// Calculates the current RA tracking rate for a specific instance (arc seconds per second).
        /// Used by per-instance SkyPredictor to avoid reading from _defaultInstance.
        /// </summary>
        public static double CurrentTrackingRate(Mount inst)
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
        /// Update AltAz tracking rates including delta for tracking error
        /// </summary>
        internal static void SetAltAzTrackingRates(AltAzTrackingType altAzTrackingType, Mount inst)
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
        /// Set mechanical direction for dec rate
        /// Positive direction mean go mechanical north
        /// </summary>
        /// <returns></returns>
        internal static double GetDecRateDirection(double rate, Mount inst)
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
        internal static double GetRaRateDirection(double rate, SkySettings settings)
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

        #endregion


            }
        }
