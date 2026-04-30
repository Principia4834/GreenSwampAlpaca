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
