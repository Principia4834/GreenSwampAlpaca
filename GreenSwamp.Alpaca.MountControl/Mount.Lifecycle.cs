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
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Mount lifecycle management: tracking state, park/home transitions, axis limits, and async operation cancellation.
    /// Handles state transitions during mount operation.
    /// </summary>
    public partial class Mount
    {
        /// <summary>
        /// Per Axis-limit check — replaces static SkyServer.CheckAxisLimits().
        /// Reads _limitStatus.
        /// J7: calls StopAxes/GoToPark to halt the correct physical device.
        /// </summary>
        private void CheckAxisLimits()
        {
            if (!Settings.LimitsOn)
            {
                ClearLimitWarningState();
                return;
            }

            var totalMeridianLimit = Settings.HourAngleLimit + Settings.AxisTrackingLimit;
            var sh = Settings.Latitude < 0;
            Dictionary<LimitType, bool> limitState = new()
            {
                { LimitType.Hardware, _limitStatus.AtLowerLimitAxisX || _limitStatus.AtUpperLimitAxisX
                                        || _limitStatus.AtLowerLimitAxisY || _limitStatus.AtUpperLimitAxisY },

                { LimitType.Meridian, sh ? _appAxes.X >= totalMeridianLimit
                                        || _appAxes.X <= -totalMeridianLimit - 180 :
                                        _appAxes.X >= totalMeridianLimit + 180
                                        || _appAxes.X <= -totalMeridianLimit},

                { LimitType.Horizon, SideOfPier == PointingState.Normal
                                        && _altAzm.Y <= Settings.AxisHzTrackingLimit }
            };

            if (Settings.AlignmentMode == AlignmentMode.GermanPolar)
            {
                limitState[LimitType.Hardware] = false;
            }
            else
            {
                limitState[LimitType.Meridian] = false;
                limitState[LimitType.Horizon] = false;
            }

            bool activeLimit = limitState.Values.Contains(true);
            bool primaryLimit = Settings.AlignmentMode == AlignmentMode.GermanPolar ? limitState[LimitType.Meridian] : limitState[LimitType.Hardware];

            if (!activeLimit)
            {
                ClearLimitWarningState();
                return;
            }

            if (LimitTriggered)
            {
                return;
            }

            LimitTriggered = true;

            if (primaryLimit)
            {
                var source = Settings.AlignmentMode == AlignmentMode.GermanPolar ? "Meridian Limit" : "Hardware Limit";
                HandleLimitAction(source, Settings.MeridianTrackingAction, Settings.ParkLimitName);
                return;
            }

            HandleLimitAction("Horizon Limit", Settings.HorizonTrackingAction, Settings.ParkHzLimitName);
        }

        private void ClearLimitWarningState()
        {
            LimitWarningActive = false;
            LimitWarningMessage = string.Empty;
            LimitTriggered = false;
        }

        private void HandleLimitAction(string source, LimitAction limitAction, string parkPositionName)
        {
            var action = "Warning only";
            var warningStaysOn = true;

            if (limitAction == LimitAction.StopTracking && TrackingMode != TrackingMode.Off)
            {
                TrackingMode = TrackingMode.Off;
                ApplyTracking(false);
                action = "Stopped tracking";
            }

            if (limitAction == LimitAction.Park && _slewState != SlewType.SlewPark)
            {
                var found = Settings.ParkPositions.Find(x => x.Name == parkPositionName);
                if (found == null)
                {
                    action = $"Park target '{parkPositionName}' not found; stopped axes";
                    MountStopAxes();
                }
                else
                {
                    action = $"Started park '{found.Name}'";
                    _parkSelected = found;
                    StartGoToParkAsync();
                    warningStaysOn = false;
                }
            }

            var message = $"{source}: {action}";
            LogMount(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = message
            });

            LimitWarningMessage = message;
            LimitWarningActive = warningStaysOn;
            LimitWarningSequence++;
        }

        /// <summary>
        /// Stops all mount axes and cancels any active GoTo or pulse guide operations.
        /// </summary>
        internal void MountStopAxes()
        {
            _ctsGoTo?.Cancel();
            PrepareMoveAxisStopChecks();
            _moveAxisActive = false;
            _rateMoveAxes = new Vector(0, 0);
            _rateRaDec = new Vector(0, 0);
            if (!AxesStopValidate())
            {
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        SimTasks(MountTaskName.StopAxes);
                        break;
                    case MountType.SkyWatcher:
                        SkyTasks(MountTaskName.StopAxes);
                        break;
                }
            }
            _slewState = SlewType.SlewNone;
            Tracking = false;
            TrackingMode = TrackingMode.Off;
            StartMoveAxisStopChecks();
        }

        /// <summary>
        /// Start an asynchronous park (slew-to-park) operation using the instance's currently selected park position.
        /// If no valid park position is set the method returns immediately. Disables tracking, writes the selected
        /// park coordinates and name into settings, and starts the park slew by invoking SlewAsync(double[], SlewType, bool) 
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
        /// Resets SkyPredictor, sets tracking mode, and applies hardware.
        /// Early-exits if already in requested state (mirrors SkyServer.Tracking early-exit).
        /// </summary>
        public void ApplyTracking(bool tracking)
        {
            ApplyTrackingCore(tracking, waitForQueueCompletion: false);
        }

        /// <summary>
        /// Blocking variant of ApplyTracking that waits for queued tracking commands to complete.
        /// </summary>
        public void ApplyTrackingAndWait(bool tracking)
        {
            ApplyTrackingCore(tracking, waitForQueueCompletion: true);
        }

        private void ApplyTrackingCore(bool tracking, bool waitForQueueCompletion)
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
                IsPulseGuidingRa = false;
                IsPulseGuidingDec = false;
                TrackingMode = TrackingMode.Off;
            }
            this.SetTracking(waitForQueueCompletion: waitForQueueCompletion);
        }

        /// <summary>
        /// Sets tracking state and mode without resetting SkyPredictor.
        /// </summary>
        internal void ApplyTrackingDirect(bool tracking, TrackingMode mode)
        {
            Tracking = tracking;
            TrackingMode = mode;
            this.SetTracking();
        }

        /// <summary>
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
        /// Cancel all async operations.
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

        /// <summary>Retains MoveAxis slew state until requested motion and measured stopping are complete.</summary>
        private void SetRateMoveSlewState()
        {
            lock (_moveAxisStopLock)
            {
                _moveAxisActive = _rateMoveAxes.X != 0.0 || _rateMoveAxes.Y != 0.0
                    || _moveAxisStopPending[0] || _moveAxisStopPending[1];
                if (_slewState == SlewType.SlewNone || _slewState == SlewType.SlewMoveAxis)
                {
                    _slewState = _moveAxisActive ? SlewType.SlewMoveAxis : SlewType.SlewNone;
                    if (_rateMoveAxes.X == 0.0 && _rateMoveAxes.Y == 0.0 && Tracking)
                        SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
                }
            }
        }

        private bool MoveAxisStopPending
        {
            get
            {
                lock (_moveAxisStopLock)
                    return _moveAxisStopPending[0] || _moveAxisStopPending[1];
            }
        }

        private int PrepareMoveAxisRateChange(int axis, double rate)
        {
            lock (_moveAxisStopLock)
            {
                _moveAxisStopPending[axis] = rate == 0.0;
                _moveAxisStopRate[axis] = rate;
                return ++_moveAxisStopVersion[axis];
            }
        }

        private void PrepareMoveAxisStopChecks()
        {
            lock (_moveAxisStopLock)
            {
                if (_rateMoveAxes.X != 0.0 || _moveAxisStopPending[0]) PrepareMoveAxisRateChange(0, 0.0);
                if (_rateMoveAxes.Y != 0.0 || _moveAxisStopPending[1]) PrepareMoveAxisRateChange(1, 0.0);
            }
        }

        private void StartMoveAxisStopChecks()
        {
            lock (_moveAxisStopLock)
            {
                for (var axis = 0; axis < 2; axis++)
                    if (_moveAxisStopPending[axis]) StartMoveAxisStopCheck(axis, _moveAxisStopVersion[axis]);
            }
        }

        private void ResetMoveAxisStopChecks()
        {
            lock (_moveAxisStopLock)
            {
                for (var axis = 0; axis < 2; axis++)
                {
                    _moveAxisStopVersion[axis]++;
                    _moveAxisStopPending[axis] = false;
                }
                _rateMoveAxes = new Vector(0, 0);
                _moveAxisActive = false;
                if (_slewState == SlewType.SlewMoveAxis) _slewState = SlewType.SlewNone;
            }
        }

        private bool IsMoveAxisStopCheckCurrent(int axis, int version)
        {
            lock (_moveAxisStopLock)
                return _moveAxisStopPending[axis] && _moveAxisStopVersion[axis] == version;
        }

        private void StartMoveAxisStopCheck(int axis, int version) =>
            _ = Task.Run(() => ConfirmMoveAxisStopAsync(axis, version));

        private async Task ConfirmMoveAxisStopAsync(int axis, int version)
        {
            try
            {
                var timer = Stopwatch.StartNew();
                (double Position, double Seconds)? previous = null;
                double previousRate = 0;
                var stableSamples = 0;
                while (timer.Elapsed.TotalSeconds < 15)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                    if (!IsMoveAxisStopCheckCurrent(axis, version)) return;
                    if (!IsMountRunning)
                        throw new InvalidOperationException($"Mount stopped before MoveAxis velocity could be confirmed for axis {axis + 1}.");
                    var sample = ReadMoveAxisPosition(axis);
                    if (timer.Elapsed.TotalSeconds >= 15) break;
                    var expectedRate = GetMoveAxisStopRate(axis);
                    if (!double.IsFinite(sample.Position) || !double.IsFinite(expectedRate) || _stepsPerRevolution[axis] <= 0)
                        throw new InvalidOperationException($"Invalid MoveAxis stop position, rate or axis resolution for axis {axis + 1}.");

                    if (previous.HasValue)
                    {
                        var elapsed = sample.Seconds - previous.Value.Seconds;
                        if (elapsed <= 0)
                            throw new InvalidOperationException($"Non-increasing MoveAxis position timestamp for axis {axis + 1}.");
                        // One position step of quantisation plus 1 arcsecond/second of rate tolerance.
                        var tolerance = 360.0 / _stepsPerRevolution[axis] / elapsed + 1.0 / 3600;
                        var velocity = Range.Range180(sample.Position - previous.Value.Position) / elapsed;
                        var guiding = axis == 0 ? IsPulseGuidingRa : IsPulseGuidingDec;
                        stableSamples = !guiding && Math.Abs(expectedRate - previousRate) <= tolerance
                            && Math.Abs(velocity - expectedRate) <= tolerance ? stableSamples + 1 : 0;
                        if (stableSamples >= 3)
                        {
                            lock (_moveAxisStopLock)
                            {
                                if (!IsMoveAxisStopCheckCurrent(axis, version)) return;
                                if ((axis == 0 ? _rateMoveAxes.X : _rateMoveAxes.Y) != 0.0) return;
                                if (_moveAxisStopRate[axis] != expectedRate || (axis == 0 ? IsPulseGuidingRa : IsPulseGuidingDec))
                                {
                                    stableSamples = 0;
                                    previous = sample;
                                    previousRate = expectedRate;
                                    continue;
                                }
                                _moveAxisStopPending[axis] = false;
                                SetRateMoveSlewState();
                            }
                            LogMount($"MoveAxis stopped|axis:{axis + 1}|velocity:{velocity}|expected:{expectedRate}|tolerance:{tolerance}");
                            return;
                        }
                    }
                    previous = sample;
                    previousRate = expectedRate;
                }
                throw new TimeoutException($"MoveAxis stop velocity was not confirmed within 15 seconds for axis {axis + 1}.");
            }
            catch (Exception ex)
            {
                lock (_moveAxisStopLock)
                {
                    if (!IsMoveAxisStopCheckCurrent(axis, version)) return;
                    // Keep stopping pending: missing evidence must not report successful completion.
                    _slewException = ex;
                }
                LogMount(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server, Type = MonitorType.Error,
                    Method = nameof(ConfirmMoveAxisStopAsync), Thread = Environment.CurrentManagedThreadId,
                    Message = $"Axis:{axis + 1}|{ex}"
                });
            }
        }

        private (double Position, double Seconds) ReadMoveAxisPosition(int axis)
        {
            if (Settings.Mount == MountType.SkyWatcher)
            {
                var queue = SkyQueue;
                var command = new SkyGetAxisPositionDate(queue.NewId, queue, axis == 0 ? Axis.Axis1 : Axis.Axis2);
                var result = queue.GetCommandResult(command);
                if (!result.Successful || result.Exception != null
                    || result.Result is not Tuple<double?, DateTime> sample || !sample.Item1.HasValue
                    || !double.IsFinite(sample.Item1.Value) || sample.Item2 == DateTime.MinValue)
                    throw result.Exception ?? new InvalidOperationException($"Could not read a fresh MoveAxis position for axis {axis + 1}.");
                return (ConvertStepsToDegrees(sample.Item1.Value, axis), (double)sample.Item2.Ticks / TimeSpan.TicksPerSecond);
            }

            var positions = GetRawDegrees();
            if (positions == null || positions.Length < 2 || !double.IsFinite(positions[axis]))
                throw new InvalidOperationException($"Could not read a fresh MoveAxis position for axis {axis + 1}.");
            return (positions[axis], (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        }

        private double GetMoveAxisStopRate(int axis)
        {
            lock (_moveAxisStopLock)
                return _moveAxisStopRate[axis];
        }

        private void RecordMoveAxisStopRate(int axis, double rate)
        {
            lock (_moveAxisStopLock)
                _moveAxisStopRate[axis] = rate;
        }

        /// <summary>
        ///Ra/Dec rate action — updates this device's predictor and applies hardware tracking rate.
        /// Mirrors SkyServer.ActionRateRaDec using this device's own fields.
        /// </summary>
        /// <param name="changedAxis">The axis that was changed. When specified, only that axis is updated (eliminating redundant commands).</param>
        private void ActionRateRaDec(TelescopeAxis? changedAxis = null, bool waitForQueueCompletion = false)
        {
            if (Tracking)
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    var raDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                    SkyPredictor.Set(raDec[0], raDec[1], _rateRaDec.X, _rateRaDec.Y);
                }
                this.SetTracking(changedAxis, waitForQueueCompletion);
            }
            else
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, _rateRaDec.X, _rateRaDec.Y);
                }
            }
        }
    }
}
