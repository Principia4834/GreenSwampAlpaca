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
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;

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
            var meridianLimit = false;
            var horizonLimit = false;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                Category = MonitorCategory.Server, Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId, Message = string.Empty
            };
            var totLimit = Settings.HourAngleLimit + Settings.AxisTrackingLimit;
            switch (Settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    meridianLimit = _limitStatus.AtLowerLimitAxisY || _limitStatus.AtUpperLimitAxisY; // J5: per-instance
                    break;
                case AlignmentMode.Polar:
                    break;
                case AlignmentMode.GermanPolar:
                    bool sh = Settings.Latitude < 0;
                    if (sh)
                    {
                        if (_appAxes.X >= Settings.HourAngleLimit || _appAxes.X <= -Settings.HourAngleLimit - 180) meridianLimit = true;
                    }
                    else
                    {
                        if (_appAxes.X >= Settings.HourAngleLimit + 180 || _appAxes.X <= -Settings.HourAngleLimit) meridianLimit = true;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (Settings.HzLimitPark || Settings.HzLimitTracking)
            {
                switch (Settings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        if ((_altAzm.Y <= Settings.AxisHzTrackingLimit || _altAzm.Y <= Settings.AxisLowerLimitY || _altAzm.Y >= Settings.AxisUpperLimitY) && TrackingMode != TrackingMode.Off)
                        { horizonLimit = true; }
                        break;
                    case AlignmentMode.Polar:
                        break;
                    case AlignmentMode.GermanPolar:
                        if (this.SideOfPier == PointingState.Normal && _altAzm.Y <= Settings.AxisHzTrackingLimit && TrackingMode != TrackingMode.Off)
                        { horizonLimit = true; }
                        break;
                }
            }
            if (meridianLimit)
            {
                monitorItem.Message = $"Meridian Limit Alarm: Park: {Settings.LimitPark} | Position: {Settings.ParkLimitName} | Stop Tracking: {Settings.LimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);
                if (TrackingMode != TrackingMode.Off && Settings.LimitTracking)
                { TrackingMode = TrackingMode.Off; Tracking = false; }
                if (Settings.LimitPark && _slewState != SlewType.SlewPark)
                {
                    var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkLimitName);
                    if (found == null) InstanceStopAxes(); else { _parkSelected = found; StartGoToParkAsync(); } // J7: per-instance
                }
            }
            if (horizonLimit)
            {
                monitorItem.Message = $"Horizon Limit Alarm: Park: {Settings.HzLimitPark} | Position:{Settings.ParkHzLimitName} | Stop Tracking:{Settings.HzLimitTracking}";
                MonitorLog.LogToMonitor(monitorItem);
                if (TrackingMode != TrackingMode.Off && Settings.HzLimitTracking)
                { TrackingMode = TrackingMode.Off; Tracking = false; }
                if (Settings.HzLimitPark && _slewState != SlewType.SlewPark)
                {
                    var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkHzLimitName);
                    if (found == null) InstanceStopAxes(); else { _parkSelected = found; StartGoToParkAsync(); } // J7: per-instance
                }
            }
        }

        /// <summary>
        /// J7: Per-instance axis stop — halts this device's mount axes without affecting other devices.
        /// Cancels any active GoTo, clears rate moves, then issues hardware stop commands.
        /// </summary>
        internal void InstanceStopAxes()
        {
            _ctsGoTo?.Cancel();
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
                _isPulseGuidingRa = false;
                _isPulseGuidingDec = false;
                TrackingMode = TrackingMode.Off;
            }
            this.SetTracking();
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

        /// <summary>
        /// Sets/reset tracking and slewing state while MoveAxis is active.
        /// Mirrors SkyServer.SetRateMoveSlewState using this device's own fields.
        /// </summary>
        private void SetRateMoveSlewState()
        {
            bool primaryActive = _rateMoveAxes.X != 0.0;
            bool secondaryActive = _rateMoveAxes.Y != 0.0;
            if (primaryActive || secondaryActive)
            {
                _moveAxisActive = true;
                _slewState = SlewType.SlewMoveAxis;
            }
            if (!primaryActive && !secondaryActive)
            {
                _moveAxisActive = false;
                _slewState = SlewType.SlewNone;
                if (Tracking) SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
        }

        /// <summary>
        ///Ra/Dec rate action — updates this device's predictor and applies hardware tracking rate.
        /// Mirrors SkyServer.ActionRateRaDec using this device's own fields.
        /// </summary>
        private void ActionRateRaDec()
        {
            if (Tracking)
            {
                if (Settings.AlignmentMode == AlignmentMode.AltAz)
                {
                    var raDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                    SkyPredictor.Set(raDec[0], raDec[1], _rateRaDec.X, _rateRaDec.Y);
                }
                this.SetTracking();
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
