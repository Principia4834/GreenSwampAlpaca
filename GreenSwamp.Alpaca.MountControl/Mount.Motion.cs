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
using System.Diagnostics;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    public partial class Mount : IMountController
    {
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

            // Pre-correct Axis1 for sidereal drift during the slew (GEM/Polar RaDec only).
            // Estimate slew duration from angular distance and MaxSlewRate, then offset the
            // target by how far the sky will move in that time so the mount lands near the
            // final position in one go, minimising precision-loop iterations.
            var axis1SlewTarget = simTarget[0];
            if (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
            {
                var rawPos = GetRawDegrees();
                if (rawPos != null && !double.IsNaN(rawPos[0]))
                {
                    var axis1Distance = Math.Abs(simTarget[0] - rawPos[0]);
                    var axis2Distance = Math.Abs(simTarget[1] - rawPos[1]);
                    // Use the dominant axis distance so that Dec-heavy slews are correctly
                    // accounted for — the mount won't stop until both axes have settled.
                    // Apply a minimum floor to account for the simulator's acceleration profile:
                    // short slews are accel-dominated and take far longer than distance/rate predicts.
                    // 4000ms is empirically derived from observed minimum settle times in test data.
                    const double minSlewMs = 4000.0;
                    var estimatedSlewMs = Settings.MaxSlewRate > 0
                        ? Math.Max(minSlewMs, (Math.Max(axis1Distance, axis2Distance) / Settings.MaxSlewRate) * 1000.0)
                        : minSlewMs;
                    var driftSign = Settings.Latitude >= 0 ? +1.0 : -1.0;
                    var raCorrection = driftSign * (Settings.SiderealRate / 3_600_000.0) * estimatedSlewMs;
                    axis1SlewTarget = simTarget[0] + raCorrection;

                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{_mountId}|GoToRaCorrection|{raCorrection:F6}|EstSlewMs|{estimatedSlewMs:F1}|Dist|{axis1Distance:F4}|Dist2|{axis2Distance:F4}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                }
            }

            _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, axis1SlewTarget);
            _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis2, simTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested();

                var axis1Stopped = false;
                var axis2Stopped = false;
                try
                {
                    var statusX = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                    var resultX = SimQueue.GetCommandResult(statusX);
                    if (!resultX.Successful) break;
                    axis1Stopped = ((Alpaca.Mount.Simulator.AxisStatus)resultX.Result).Stopped;

                    Thread.Sleep(50);
                    token.ThrowIfCancellationRequested();

                    var statusY = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                    var resultY = SimQueue.GetCommandResult(statusY);
                    if (!resultY.Successful) break;
                    axis2Stopped = ((Alpaca.Mount.Simulator.AxisStatus)resultY.Result).Stopped;
                }
                catch (InvalidOperationException) { break; }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { break; }

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
            double[] deltaDegree = [0.0, 0.0];
            double[] gotoPrecision = [ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1)];
            double deltaTime = 75.0; // ms — initial seed for first-iteration drift estimate

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
                // Algorithm A: sidereal drift feedforward for GEM and Polar RaDec slews.
                // Commands Axis1 to where the sky will be when the axis settles (deltaTime ms from now),
                // eliminating drift catch-up iterations. Sign: +1 NH, -1 SH (hemisphere-only, not pier-side-dependent).
                var raFeedforward = 0.0;
                if (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
                {
                    var driftSign = Settings.Latitude >= 0 ? +1.0 : -1.0;
                    raFeedforward = driftSign * (Settings.SiderealRate / 3_600_000.0) * deltaTime;
                }
                if (!axis1AtTarget)
                    _ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, simTarget[0] + raFeedforward + 0.125 * deltaDegree[0]);
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
                        try
                        {
                            var status1 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                            var result1 = SimQueue.GetCommandResult(status1);
                            if (!result1.Successful) break;
                            axis1Stopped = ((Alpaca.Mount.Simulator.AxisStatus)result1.Result).Stopped;
                        }
                        catch (InvalidOperationException) { break; }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { break; }
                    }

                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Stopped)
                    {
                        try
                        {
                            var status2 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                            var result2 = SimQueue.GetCommandResult(status2);
                            if (!result2.Successful) break;
                            axis2Stopped = ((Alpaca.Mount.Simulator.AxisStatus)result2.Result).Stopped;
                        }
                        catch (InvalidOperationException) { break; }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { break; }
                    }

                    if (axis1Stopped && axis2Stopped) { break; }
                }
                loopTimer.Stop();
                // EMA smoothing (α=0.4): reduces noise while adapting quickly to iteration time changes
                deltaTime = 0.4 * loopTimer.Elapsed.TotalMilliseconds + 0.6 * deltaTime;

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"Mount:{_mountId}|Delta|({deltaDegree[0]},{deltaDegree[1]})|RaFwd|{raFeedforward:F6}|Seconds|{loopTimer.Elapsed.TotalSeconds}"
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
                            try
                            {
                                var status1 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis1);
                                var result1 = SimQueue.GetCommandResult(status1);
                                if (!result1.Successful) break;
                                axis1Stopped = ((Alpaca.Mount.Simulator.AxisStatus)result1.Result).Stopped;
                            }
                            catch (InvalidOperationException) { break; }
                            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { break; }
                        }

                        Thread.Sleep(100);

                        if (!axis2Stopped)
                        {
                            try
                            {
                                var status2 = new CmdAxisStatus(SimQueue.NewId, SimQueue, Axis.Axis2);
                                var result2 = SimQueue.GetCommandResult(status2);
                                if (!result2.Successful) break;
                                axis2Stopped = ((Alpaca.Mount.Simulator.AxisStatus)result2.Result).Stopped;
                            }
                            catch (InvalidOperationException) { break; }
                            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { break; }
                        }

                        if (axis1Stopped && axis2Stopped) { break; }
                    }
                    stopwatch1.Stop();
                    deltaTime = (long)stopwatch1.Elapsed.TotalMilliseconds;
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

                    try
                    {
                        var statusX = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                        var x = SkyQueue.GetCommandResult(statusX);
                        if (!x.Successful) break;
                        axis1Stopped = Convert.ToBoolean(x.Result);
                    }
                    catch (InvalidOperationException) { break; }
                }

                if (!axis2Stopped)
                {
                    // When Axis 1 is already done its WaitOne(250) is skipped; add a small
                    // delay so we do not read Axis 2 status with no preamble, which risks
                    // catching a transient FullStop during the advanced-command deceleration ramp.
                    if (axis1Stopped) Thread.Sleep(50);
                    token.WaitHandle.WaitOne(250);
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var statusY = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                        var y = SkyQueue.GetCommandResult(statusY);
                        if (!y.Successful) break;
                        axis2Stopped = Convert.ToBoolean(y.Result);
                    }
                    catch (InvalidOperationException) { break; }
                }

                if (!axis1Stopped || !axis2Stopped) { continue; }

                if (_slewSettleTime > 0)
                    Thread.Sleep((int)TimeSpan.FromSeconds(_slewSettleTime).TotalMilliseconds);
                break;
            }
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            stopwatch.Stop();

            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Mount:{_mountId}|Seconds|{elapsedSeconds}|Target|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested();
            if (elapsedSeconds <= timer)
                SkyPrecisionGoto(target, slewType, token);
            #endregion

            SkyTasks(MountTaskName.StopAxes);
            return success;
        }

        /// <summary>
        /// Performs a high-precision goto operation by iteratively correcting mount position until target coordinates
        /// are reached within the configured precision tolerance.
        /// </summary>
        /// <remarks>Uses an iterative correction approach with up to 5 attempts, applying fractional
        /// corrections (0.25x for axis 1, 0.1x for axis 2) to converge on target. For Alt/Az mounts slewing to RA/Dec
        /// coordinates, compensates for sky rotation by predicting future positions. Axes that reach target precision
        /// are marked complete and not commanded further.</remarks>
        /// <param name="target">Target coordinates as a two-element array [axis1, axis2] in degrees.</param>
        /// <param name="slewType">Type of slew operation determining coordinate interpretation and prediction behavior.</param>
        /// <param name="token">Cancellation token to abort the precision goto operation.</param>
        /// <returns>Status code indicating completion (always returns 0).</returns>
        /// <exception cref="TimeoutException">Thrown when mount position update does not arrive within 5 seconds.</exception>
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
                Message = $"Mount:{_mountId}|from|({_actualAxisX},{_actualAxisY})|to|({target[0]},{target[1]})"
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

                // Event-based position update waiting
                if (!WaitUpdateMountPosition(5000))
                {
                    var errorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Error,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{_mountId}|Timeout waiting for position update|Try:{maxTries}"
                    };
                    MonitorLog.LogToMonitor(errorItem);
                    throw new TimeoutException($"Mount position update timeout in precision goto (Mount: {_mountId})");
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
                if (Math.Abs(deltaDegree[0]) > 5.0 || Math.Abs(deltaDegree[1]) > 5.0)
                {
                    // Snapshot log file for large error
                    MonitorQueue.WriteBuffer();
                }

                axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested();
                if (!axis1AtTarget)
                {
                    var predictor = (slewType != SlewType.SlewRaDec)
                        ? 0
                        : 0.25;
                    _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0] + predictor * deltaDegree[0]);
                }
                var axis1Done = axis1AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();

                    if (!axis1Done)
                    {
                        try
                        {
                            var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                            var result1 = SkyQueue.GetCommandResult(status1);
                            if (!result1.Successful) break;
                            axis1Done = Convert.ToBoolean(result1.Result);
                        }
                        catch (InvalidOperationException) { break; }
                    }
                    if (axis1Done) { break; }
                }

                token.ThrowIfCancellationRequested();
                if (!axis2AtTarget)
                {
                    var predictor = (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
                        ? 0
                        : 0.1;
                    _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1] + predictor * deltaDegree[1]);
                }

                var axis2Done = axis2AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();

                    if (!axis2Done)
                    {
                        try
                        {
                            var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                            var result2 = SkyQueue.GetCommandResult(status2);
                            if (!result2.Successful) break;
                            axis2Done = Convert.ToBoolean(result2.Result);
                        }
                        catch (InvalidOperationException) { break; }
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
                    Message = $"Mount:{_mountId}|Delta|{deltaDegree[0]}|{deltaDegree[1]}|Seconds|{loopTimer.Elapsed.TotalSeconds}"
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
                    if (!WaitUpdateMountPosition(5000)) throw new TimeoutException($"Mount position update timeout in pulse goto (Mount: {_mountId})");

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
                            try
                            {
                                var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                                var result1 = SkyQueue.GetCommandResult(status1);
                                if (!result1.Successful) break;
                                axis1Done = Convert.ToBoolean(result1.Result);
                            }
                            catch (InvalidOperationException) { break; }
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
                            try
                            {
                                var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                                var result2 = SkyQueue.GetCommandResult(status2);
                                if (!result2.Successful) break;
                                axis2Done = Convert.ToBoolean(result2.Result);
                            }
                            catch (InvalidOperationException) { break; }
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

        /// <summary>
        /// Ensures the SlewController is initialized.
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
            // Capture offset rates now
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
    }
}
