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

// Per-instance hand-controller move logic.
// Migrated from static SkyServer.HcMoves(), HcPulseMoveAsync(), and HcPulseMove().
// Simplified API: HcMode and per-call backlash/anti-ra args are read from Settings.

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.MountControl.Pulses;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;
using SimAxisStatus = GreenSwamp.Alpaca.Mount.Simulator.AxisStatus;

namespace GreenSwamp.Alpaca.MountControl
{
    public partial class Mount
    {
        /// <summary>Whether an HC pulse-guide cycle is mid-pulse (between PulseGuide call and sleep).</summary>
        internal volatile bool HcPulseDone;

        /// <summary>
        /// Applies a hand-controller button press to the mount axes.
        /// Mirrors the former static SkyServer.HcMoves(), converted to instance-based.
        /// Anti-backlash and mode settings are read from <see cref="SkySettings"/>.
        /// </summary>
        /// <param name="speed">HC speed (1–8)</param>
        /// <param name="direction">Direction of move</param>
        public void HcMoves(SlewSpeed speed, SlewDirection direction)
        {
            if (!IsMountRunning) return;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Settings.HcSpeed}|{Settings.HcMode}|{direction}|{_actualAxisX}|{_actualAxisY}"
            };
            LogMount(monitorItem);

            bool altAzMode = Settings.AlignmentMode == AlignmentMode.AltAz;
            bool southernHemisphere = Settings.Latitude < 0;

            direction = ApplyFlip(direction);

            double delta = speed switch
            {
                SlewSpeed.One   => _slewSpeedOne,
                SlewSpeed.Two   => _slewSpeedTwo,
                SlewSpeed.Three => _slewSpeedThree,
                SlewSpeed.Four  => _slewSpeedFour,
                SlewSpeed.Five  => _slewSpeedFive,
                SlewSpeed.Six   => _slewSpeedSix,
                SlewSpeed.Seven => _slewSpeedSeven,
                SlewSpeed.Eight => _slewSpeedEight,
                _               => 0
            };

            var change = new double[] { 0, 0 };

            switch (Settings.HcMode)
            {
                case HcMode.Axes:
                    ApplyAxesChange(direction, delta, altAzMode, southernHemisphere, change);
                    break;
                case HcMode.Guiding:
                    ApplyGuidingChange(direction, delta, altAzMode, southernHemisphere, change);
                    break;
                case HcMode.Pulse:
                    HcPulseMoveAsync(speed, direction);
                    return;
                default:
                    return;
            }

            // Polar Left: primary OTA is flipped, so reverse both axes
            if (Settings.AlignmentMode == AlignmentMode.Polar && Settings.PolarMode == PolarMode.Left)
            {
                change[0] = -change[0];
                change[1] = -change[1];
            }

            if (Math.Abs(change[0]) > 0 || Math.Abs(change[1]) > 0)
            {
                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{Settings.HcSpeed}|{direction}|{change[0]}|{change[1]}"
                };
                LogMount(monitorItem);
            }

            _slewState = Math.Abs(change[0]) + Math.Abs(change[1]) > 0 ? SlewType.SlewHandpad : SlewType.SlewNone;

            // Anti-backlash — Dec axis
            long stepsNeededDec = 0;
            bool hcAntiDec = Settings.HcAntiDec;
            int decBacklash = Settings.DecBacklash;
            if (hcAntiDec && decBacklash > 0 && _hcPrevMoveDec != null)
            {
                switch (direction)
                {
                    case SlewDirection.SlewNorth:
                    case SlewDirection.SlewUp:
                    case SlewDirection.SlewSouth:
                    case SlewDirection.SlewDown:
                    case SlewDirection.SlewNorthEast:
                    case SlewDirection.SlewNorthWest:
                    case SlewDirection.SlewSouthEast:
                    case SlewDirection.SlewSouthWest:
                        if (Math.Abs(_hcPrevMoveDec.Delta) > 0.000000 &&
                            Math.Sign(_hcPrevMoveDec.Delta) != Math.Sign(change[1]))
                        {
                            stepsNeededDec = Convert.ToInt64(_hcPrevMovesDec.Sum());
                            if (stepsNeededDec >= decBacklash) stepsNeededDec = decBacklash;
                            if (change[1] < 0) stepsNeededDec = -stepsNeededDec;
                        }
                        break;
                }
            }

            // Anti-backlash — RA axis
            long stepsNeededRa = 0;
            bool hcAntiRa = Settings.HcAntiRa;
            int raBacklash = Settings.RaBacklash;
            if (hcAntiRa && Tracking && raBacklash > 0 && _hcPrevMoveRa != null)
            {
                if (direction == SlewDirection.SlewNoneRa)
                {
                    if (_hcPrevMoveRa.StepEnd.HasValue && _hcPrevMoveRa.StepStart.HasValue)
                    {
                        if (southernHemisphere)
                        {
                            if (_hcPrevMoveRa.StepEnd.Value > _hcPrevMoveRa.StepStart.Value)
                            {
                                stepsNeededRa = Convert.ToInt64(_hcPrevMoveRa.StepDiff);
                                if (stepsNeededRa >= raBacklash) stepsNeededRa = raBacklash;
                                stepsNeededRa = -Math.Abs(stepsNeededRa);
                            }
                        }
                        else
                        {
                            if (_hcPrevMoveRa.StepEnd.Value < _hcPrevMoveRa.StepStart.Value)
                            {
                                stepsNeededRa = Convert.ToInt64(_hcPrevMoveRa.StepDiff);
                                if (stepsNeededRa >= raBacklash) stepsNeededRa = raBacklash;
                            }
                        }
                    }
                }
            }

            // Log anti-lash moves
            if (Math.Abs(stepsNeededDec) > 0 && _hcPrevMoveDec != null)
            {
                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{_hcPrevMoveDec.Delta}|{_hcPrevMovesDec.Sum()},Anti-Lash,{stepsNeededDec} of {decBacklash}"
                };
                LogMount(monitorItem);
            }
            if (Math.Abs(stepsNeededRa) > 0 && _hcPrevMoveRa != null)
            {
                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{_hcPrevMoveRa.Direction}|{_hcPrevMoveRa.StepDiff},Anti-Lash,{stepsNeededRa} of {raBacklash}"
                };
                LogMount(monitorItem);
            }

            // Track previous direction for backlash next time
            switch (direction)
            {
                case SlewDirection.SlewNorth:
                case SlewDirection.SlewUp:
                case SlewDirection.SlewSouth:
                case SlewDirection.SlewDown:
                    _hcPrevMoveDec = new HcPrevMove
                    {
                        Direction = direction,
                        StartDate = HiResDateTime.UtcNow,
                        Delta = change[1],
                        StepStart = GetRawSteps(1),
                    };
                    break;
                case SlewDirection.SlewEast:
                case SlewDirection.SlewLeft:
                case SlewDirection.SlewWest:
                case SlewDirection.SlewRight:
                    _hcPrevMoveRa = new HcPrevMove
                    {
                        Direction = direction,
                        StartDate = HiResDateTime.UtcNow,
                        Delta = change[0],
                        StepStart = GetRawSteps(0),
                    };
                    break;
                case SlewDirection.SlewNoneRa:
                case SlewDirection.SlewNoneDec:
                    break;
                case SlewDirection.SlewNorthEast:
                case SlewDirection.SlewNorthWest:
                case SlewDirection.SlewSouthEast:
                case SlewDirection.SlewSouthWest:
                    _hcPrevMoveDec = new HcPrevMove
                    {
                        Direction = direction,
                        StartDate = HiResDateTime.UtcNow,
                        Delta = change[1],
                        StepStart = GetRawSteps(1),
                    };
                    _hcPrevMoveRa = new HcPrevMove
                    {
                        Direction = direction,
                        StartDate = HiResDateTime.UtcNow,
                        Delta = change[0],
                        StepStart = GetRawSteps(0),
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }

            // AltAz mode: pause predictor while axes are moving
            if (altAzMode && (change[0] != 0.0 || change[1] != 0.0) && Tracking)
                _trackingProcessor?.Post(new HcAltAzPauseCommand());

            // Send commands to mount hardware
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SendHcMoveSimulator(stepsNeededDec, stepsNeededRa, change);
                    break;
                case MountType.SkyWatcher:
                    SendHcMoveSkyWatcher(stepsNeededDec, stepsNeededRa, change);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // AltAz mode: when motion stops, re-seed predictor and resume tracking
            if (altAzMode && change[0] == 0.0 && change[1] == 0.0 && Tracking)
            {
                var capturedRa = RightAscensionXForm;
                var capturedDec = DeclinationXForm;
                Task.Run(() =>
                {
                    var trackingRate = SkyGetRate();
                    AxesRateOfChange.Reset();
                    do
                    {
                        _mountPositionUpdatedEvent.Reset();
                        UpdateSteps();
                        _mountPositionUpdatedEvent.Wait(TimeSpan.FromMilliseconds(200));
                        AxesRateOfChange.Update(_actualAxisX, _actualAxisY, HiResDateTime.UtcNow);
                    } while ((AxesRateOfChange.AxisVelocity - trackingRate).Length > 1.1 * CurrentTrackingRate());

                    capturedRa = RightAscensionXForm;
                    capturedDec = DeclinationXForm;

                    _trackingProcessor?.Post(new HcAltAzResumeCommand(capturedRa, capturedDec));

                    var info = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = "HcMoves",
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"|RaDec SlewNone tracking|{capturedRa}|{capturedDec}"
                    };
                    LogMount(info);
                });
            }
        }

        /// <summary>
        /// Applies HcFlipEw / HcFlipNs settings to reverse axis directions before
        /// the direction is dispatched to hardware. Mirrors the legacy SkyTelescopeVm
        /// call-site flip applied before StartSlew().
        /// </summary>
        private SlewDirection ApplyFlip(SlewDirection direction)
        {
            var ew = Settings.HcFlipEw;
            var ns = Settings.HcFlipNs;
            if (!ew && !ns) return direction;
            return direction switch
            {
                SlewDirection.SlewNorth     when ns       => SlewDirection.SlewSouth,
                SlewDirection.SlewSouth     when ns       => SlewDirection.SlewNorth,
                SlewDirection.SlewEast      when ew       => SlewDirection.SlewWest,
                SlewDirection.SlewWest      when ew       => SlewDirection.SlewEast,
                SlewDirection.SlewNorthEast when ns && ew => SlewDirection.SlewSouthWest,
                SlewDirection.SlewNorthEast when ns       => SlewDirection.SlewSouthEast,
                SlewDirection.SlewNorthEast when ew       => SlewDirection.SlewNorthWest,
                SlewDirection.SlewNorthWest when ns && ew => SlewDirection.SlewSouthEast,
                SlewDirection.SlewNorthWest when ns       => SlewDirection.SlewSouthWest,
                SlewDirection.SlewNorthWest when ew       => SlewDirection.SlewNorthEast,
                SlewDirection.SlewSouthEast when ns && ew => SlewDirection.SlewNorthWest,
                SlewDirection.SlewSouthEast when ns       => SlewDirection.SlewNorthEast,
                SlewDirection.SlewSouthEast when ew       => SlewDirection.SlewSouthWest,
                SlewDirection.SlewSouthWest when ns && ew => SlewDirection.SlewNorthEast,
                SlewDirection.SlewSouthWest when ns       => SlewDirection.SlewNorthWest,
                SlewDirection.SlewSouthWest when ew       => SlewDirection.SlewSouthEast,
                _ => direction
            };
        }

        /// <summary>
        /// Starts an async pulse-guide loop from the hand controller.
        /// Continues firing pulses until the corresponding SlewNone direction is received.
        /// </summary>
        private async void HcPulseMoveAsync(SlewSpeed speed, SlewDirection direction)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{speed}|{direction}"
                };
                LogMount(monitorItem);

                switch (direction)
                {
                    case SlewDirection.SlewNoneRa:
                    case SlewDirection.SlewNoneDec:
                        _ctsHcPulseGuide?.Cancel();
                        return;
                    case SlewDirection.SlewNorthEast:
                    case SlewDirection.SlewNorthWest:
                    case SlewDirection.SlewSouthEast:
                    case SlewDirection.SlewSouthWest:
                        return; // diagonal directions not supported in pulse mode
                    case SlewDirection.SlewNorth:
                    case SlewDirection.SlewSouth:
                    case SlewDirection.SlewEast:
                    case SlewDirection.SlewWest:
                    case SlewDirection.SlewUp:
                    case SlewDirection.SlewDown:
                    case SlewDirection.SlewLeft:
                    case SlewDirection.SlewRight:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
                }

                var hpGs = Settings.HcPulseGuides;
                if (hpGs == null || hpGs.Count == 0) return;

                int hcSpeed = (int)speed;
                if (!hpGs.Any(x => x.Speed == hcSpeed)) return;

                var hcPulseGuide = hpGs.Find(x => x.Speed == hcSpeed);
                if (hcPulseGuide == null) return;

                GuideDirection pulseDirection = direction switch
                {
                    SlewDirection.SlewNorth or SlewDirection.SlewUp    => GuideDirection.North,
                    SlewDirection.SlewSouth or SlewDirection.SlewDown  => GuideDirection.South,
                    SlewDirection.SlewEast  or SlewDirection.SlewLeft  => GuideDirection.East,
                    SlewDirection.SlewWest  or SlewDirection.SlewRight => GuideDirection.West,
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
                };

                _ctsHcPulseGuide = new CancellationTokenSource();
                int returnCode = await Task.Run(() => HcPulseMove(hcPulseGuide, pulseDirection, _ctsHcPulseGuide.Token));

                if (returnCode > 0)
                {
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Warning,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"ReturnCode:{returnCode}|{hcPulseGuide.Speed}|{hcPulseGuide.Duration}|{hcPulseGuide.Interval}|{hcPulseGuide.Rate}"
                    };
                    LogMount(monitorItem);
                }
            }
            catch (Exception ex)
            {
                bool cancelled = ex is OperationCanceledException || ex.GetBaseException() is OperationCanceledException;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = cancelled ? "HcPulseGuide cancelled by command" : "HcPulseGuides failed"
                };
                LogMount(monitorItem);
            }
        }

        /// <summary>
        /// Synchronous pulse loop — fires one pulse per iteration until the token is cancelled.
        /// Returns 0 on clean cancellation, 2 on invalid settings, 3 on error.
        /// </summary>
        private int HcPulseMove(HcPulseGuide hcPulseGuide, GuideDirection pulseDirection, CancellationToken token)
        {
            try
            {
                int duration = hcPulseGuide.Duration;
                int interval = hcPulseGuide.Interval;
                if (duration <= 0) return 2;
                if (interval < 0) return 2;

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    PulseGuide(pulseDirection, duration, hcPulseGuide.Rate);
                    token.ThrowIfCancellationRequested();
                    Thread.Sleep(duration);
                    HcPulseDone = true;
                    Thread.Sleep(interval);
                    HcPulseDone = false;
                }
            }
            catch (OperationCanceledException)
            {
                HcPulseDone = false;
                return 0;
            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Error,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{ex}"
                };
                LogMount(monitorItem);
                HcPulseDone = false;
                _ctsPulseGuideDec?.Cancel();
                _ctsPulseGuideRa?.Cancel();
                return 3;
            }
        }

        // ----------------------------------------------------------------
        // Change computation helpers
        // ----------------------------------------------------------------

        private void ApplyAxesChange(SlewDirection direction, double delta, bool altAzMode, bool southernHemisphere, double[] change)
        {
            switch (direction)
            {
                case SlewDirection.SlewNorth:
                case SlewDirection.SlewUp:
                    change[1] = delta;
                    break;
                case SlewDirection.SlewSouth:
                case SlewDirection.SlewDown:
                    change[1] = -delta;
                    break;
                case SlewDirection.SlewEast:
                case SlewDirection.SlewLeft:
                    change[0] = southernHemisphere && !altAzMode ? -delta : delta;
                    break;
                case SlewDirection.SlewWest:
                case SlewDirection.SlewRight:
                    change[0] = southernHemisphere && !altAzMode ? delta : -delta;
                    break;
                case SlewDirection.SlewNoneRa:
                    if (_hcPrevMoveRa != null)
                    {
                        _hcPrevMoveRa.StepEnd = GetRawSteps(0);
                        if (_hcPrevMoveRa.StepEnd.HasValue && _hcPrevMoveRa.StepStart.HasValue)
                            _hcPrevMoveRa.StepDiff = Math.Abs(_hcPrevMoveRa.StepEnd.Value - _hcPrevMoveRa.StepStart.Value);
                    }
                    break;
                case SlewDirection.SlewNoneDec:
                    if (_hcPrevMoveDec != null)
                    {
                        _hcPrevMoveDec.StepEnd = GetRawSteps(1);
                        if (_hcPrevMoveDec.StepEnd.HasValue && _hcPrevMoveDec.StepStart.HasValue)
                        {
                            _hcPrevMoveDec.StepDiff = Math.Abs(_hcPrevMoveDec.StepEnd.Value - _hcPrevMoveDec.StepStart.Value);
                            _hcPrevMovesDec.Add(_hcPrevMoveDec.StepDiff);
                        }
                    }
                    break;
                case SlewDirection.SlewNorthEast:
                    change[1] = delta;
                    change[0] = southernHemisphere && !altAzMode ? -delta : delta;
                    break;
                case SlewDirection.SlewNorthWest:
                    change[1] = delta;
                    change[0] = southernHemisphere && !altAzMode ? delta : -delta;
                    break;
                case SlewDirection.SlewSouthEast:
                    change[1] = -delta;
                    change[0] = southernHemisphere && !altAzMode ? -delta : delta;
                    break;
                case SlewDirection.SlewSouthWest:
                    change[1] = -delta;
                    change[0] = southernHemisphere && !altAzMode ? delta : -delta;
                    break;
                default:
                    change[0] = 0;
                    change[1] = 0;
                    break;
            }
        }

        private void ApplyGuidingChange(SlewDirection direction, double delta, bool altAzMode, bool southernHemisphere, double[] change)
        {
            bool isEast = SideOfPier == PointingState.Normal;
            bool isWest = SideOfPier == PointingState.ThroughThePole;

            switch (direction)
            {
                case SlewDirection.SlewNorth:
                case SlewDirection.SlewUp:
                    if (!altAzMode)
                        change[1] = Settings.Mount == MountType.Simulator
                            ? (isEast ? delta : -delta)
                            : (isEast ? -delta : delta);
                    else
                        change[1] = delta;
                    break;

                case SlewDirection.SlewSouth:
                case SlewDirection.SlewDown:
                    if (!altAzMode)
                        change[1] = Settings.Mount == MountType.Simulator
                            ? (isWest ? delta : -delta)
                            : (isWest ? -delta : delta);
                    else
                        change[1] = -delta;
                    break;

                case SlewDirection.SlewEast:
                case SlewDirection.SlewLeft:
                    change[0] = !altAzMode
                        ? (southernHemisphere ? delta : -delta)
                        : delta;
                    break;

                case SlewDirection.SlewWest:
                case SlewDirection.SlewRight:
                    change[0] = !altAzMode
                        ? (southernHemisphere ? -delta : delta)
                        : -delta;
                    break;

                case SlewDirection.SlewNoneRa:
                    if (_hcPrevMoveRa != null)
                    {
                        _hcPrevMoveRa.StepEnd = GetRawSteps(0);
                        if (_hcPrevMoveRa.StepEnd.HasValue && _hcPrevMoveRa.StepStart.HasValue)
                            _hcPrevMoveRa.StepDiff = Math.Abs(_hcPrevMoveRa.StepEnd.Value - _hcPrevMoveRa.StepStart.Value);
                    }
                    break;

                case SlewDirection.SlewNoneDec:
                    if (_hcPrevMoveDec != null)
                    {
                        _hcPrevMoveDec.StepEnd = GetRawSteps(1);
                        if (_hcPrevMoveDec.StepEnd.HasValue && _hcPrevMoveDec.StepStart.HasValue)
                        {
                            _hcPrevMoveDec.StepDiff = Math.Abs(_hcPrevMoveDec.StepEnd.Value - _hcPrevMoveDec.StepStart.Value);
                            _hcPrevMovesDec.Add(_hcPrevMoveDec.StepDiff);
                        }
                    }
                    break;

                case SlewDirection.SlewNorthEast:
                    if (!altAzMode)
                    {
                        change[1] = Settings.Mount == MountType.Simulator ? (isEast ? delta : -delta) : (isEast ? -delta : delta);
                        change[0] = southernHemisphere ? delta : -delta;
                    }
                    else { change[1] = delta; change[0] = delta; }
                    break;

                case SlewDirection.SlewNorthWest:
                    if (!altAzMode)
                    {
                        change[1] = Settings.Mount == MountType.Simulator ? (isEast ? delta : -delta) : (isEast ? -delta : delta);
                        change[0] = southernHemisphere ? -delta : delta;
                    }
                    else { change[1] = delta; change[0] = -delta; }
                    break;

                case SlewDirection.SlewSouthEast:
                    if (!altAzMode)
                    {
                        change[1] = Settings.Mount == MountType.Simulator ? (isWest ? delta : -delta) : (isWest ? -delta : delta);
                        change[0] = southernHemisphere ? delta : -delta;
                    }
                    else { change[1] = -delta; change[0] = delta; }
                    break;

                case SlewDirection.SlewSouthWest:
                    if (!altAzMode)
                    {
                        change[1] = Settings.Mount == MountType.Simulator ? (isWest ? delta : -delta) : (isWest ? -delta : delta);
                        change[0] = southernHemisphere ? -delta : delta;
                    }
                    else { change[1] = -delta; change[0] = -delta; }
                    break;

                default:
                    change[0] = 0;
                    change[1] = 0;
                    break;
            }
        }

        // ----------------------------------------------------------------
        // Hardware dispatch helpers
        // ----------------------------------------------------------------

        private void SendHcMoveSimulator(long stepsNeededDec, long stepsNeededRa, double[] change)
        {
            var mq = SimQueue!;

            // Dec anti-lash
            if (Math.Abs(stepsNeededDec) > 0)
            {
                _hcPrevMovesDec.Clear();
                var a = new CmdAxesDegrees(mq.NewId, mq);
                var b = (double[])mq.GetCommandResult(a).Result;
                var arcSecs = Conversions.StepPerArcSec(_stepsPerRevolution[1]);
                var d = Conversions.ArcSec2Deg(stepsNeededDec / arcSecs);
                _ = new CmdAxisGoToTarget(mq.NewId, mq, Axis.Axis2, b[1] + d);

                var sw1 = Stopwatch.StartNew();
                while (sw1.Elapsed.TotalSeconds <= 2)
                {
                    var statusCmd = new CmdAxisStatus(mq.NewId, mq, Axis.Axis2);
                    var axis2Status = (SimAxisStatus)mq.GetCommandResult(statusCmd).Result;
                    if (!axis2Status.Slewing) break;
                }
            }

            if (_hcPrevMoveDec != null) _hcPrevMoveDec.StepStart = GetRawSteps(1);

            // RA anti-lash
            if (Math.Abs(stepsNeededRa) > 0)
            {
                var a = new CmdAxesDegrees(mq.NewId, mq);
                var b = (double[])mq.GetCommandResult(a).Result;
                var arcSecs = Conversions.StepPerArcSec(_stepsPerRevolution[0]);
                var d = Conversions.ArcSec2Deg(stepsNeededRa / arcSecs);
                _ = new CmdAxisGoToTarget(mq.NewId, mq, Axis.Axis1, b[0] + d);

                var sw1 = Stopwatch.StartNew();
                while (sw1.Elapsed.TotalSeconds <= 2)
                {
                    var statusCmd = new CmdAxisStatus(mq.NewId, mq, Axis.Axis1);
                    var axis1Status = (SimAxisStatus)mq.GetCommandResult(statusCmd).Result;
                    if (!axis1Status.Slewing) break;
                }

                GetRawDegrees();
            }

            _ = new CmdHcSlew(mq.NewId, mq, Axis.Axis1, change[0]);
            _ = new CmdHcSlew(mq.NewId, mq, Axis.Axis2, change[1]);
        }

        private void SendHcMoveSkyWatcher(long stepsNeededDec, long stepsNeededRa, double[] change)
        {
            var sq = SkyQueue!;

            // Dec anti-lash
            if (Math.Abs(stepsNeededDec) > 0)
            {
                _hcPrevMovesDec.Clear();
                _ = new SkyAxisMoveSteps(sq.NewId, sq, Axis.Axis2, stepsNeededDec);

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds <= 2)
                {
                    var statusCmd = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis2);
                    bool stopped = Convert.ToBoolean(sq.GetCommandResult(statusCmd).Result);
                    if (stopped) break;
                }
            }

            if (_hcPrevMoveDec != null) _hcPrevMoveDec.StepStart = GetRawSteps(1);

            // RA anti-lash
            if (Math.Abs(stepsNeededRa) > 0)
            {
                _ = new SkyAxisMoveSteps(sq.NewId, sq, Axis.Axis1, stepsNeededRa);
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds <= 2)
                {
                    var statusCmd = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis1);
                    bool stopped = Convert.ToBoolean(sq.GetCommandResult(statusCmd).Result);
                    if (stopped) break;
                }
            }

            // Apply HC slew rate and update mount
            SkyHcRate = new Vector(change[0], change[1]);
            var rate = SkyGetRate();
            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);
            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);
        }
    }
}
