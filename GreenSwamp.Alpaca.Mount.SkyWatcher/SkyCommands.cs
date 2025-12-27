/* Copyright(C) 2019-2025 Rob  Morgan (robert.morgan.e@gmail.com)

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
using GreenSwamp.Alpaca.Mount.Commands;

namespace GreenSwamp.Alpaca.Mount.SkyWatcher
{
    /// <summary>
    /// Legacy interface for backward compatibility
    /// </summary>
    public interface ISkyCommand : ICommand<SkyWatcher>
    {
        // Explicitly redeclare Result for backward compatibility with existing code
        new dynamic Result { get; }
    }

    /// <summary>
    /// Abstract base class for all Sky commands providing common functionality
    /// </summary>
    public abstract class SkyCommandBase : CommandBase<SkyWatcher>, ISkyCommand
    {
        protected SkyCommandBase(long id) : base(id, SkyQueue.Instance)
        {
        }
    }

    /// <summary>
    /// Abstract base class for Sky commands that return query results
    /// </summary>
    public abstract class SkyQueryCommand : QueryCommand<SkyWatcher>, ISkyCommand
    {
        protected SkyQueryCommand(long id) : base(id, SkyQueue.Instance)
        {
        }
    }

    /// <summary>
    /// Abstract base class for Sky commands that perform actions without returning results
    /// </summary>
    public abstract class SkyActionCommand : ActionCommand<SkyWatcher>, ISkyCommand
    {
        protected SkyActionCommand(long id) : base(id, SkyQueue.Instance)
        {
        }
    }

    // Action Commands (no result returned)
    public class SkyAllowAdvancedCommandSet : SkyActionCommand
    {
        private readonly bool _on;

        public SkyAllowAdvancedCommandSet(long id, bool on) : base(id)
        {
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AllowAdvancedCommandSet(_on);
        }
    }

    public class SkyAxisMoveSteps : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly long _steps;

        public SkyAxisMoveSteps(long id, Axis axis, long steps) : base(id)
        {
            _axis = axis;
            _steps = steps;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisMoveSteps(_axis, _steps);
        }
    }

    public class SkyAxisPulse : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _guideRate;
        private readonly int _duration;
        private readonly int _backlashSteps;
        private readonly CancellationToken _token;

        public SkyAxisPulse(long id, Axis axis, double guideRate, int duration, int backlashSteps, CancellationToken token) : base(id)
        {
            _axis = axis;
            _guideRate = guideRate;
            _duration = duration;
            _backlashSteps = backlashSteps;
            _token = token;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisPulse(_axis, _guideRate, _duration, _backlashSteps, _token);
        }
    }

    public class SkyAxisStop : SkyActionCommand
    {
        private readonly Axis _axis;

        public SkyAxisStop(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisStop(_axis);
        }
    }

    public class SkyAxisStopInstant : SkyActionCommand
    {
        private readonly Axis _axis;

        public SkyAxisStopInstant(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisStopInstant(_axis);
        }
    }

    public class SkyAxisSlew : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _rate;

        public SkyAxisSlew(long id, Axis axis, double rate) : base(id)
        {
            _axis = axis;
            _rate = rate;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisSlew(_axis, _rate);
        }
    }

    public class SkyInitializeAxes : SkyActionCommand
    {
        public SkyInitializeAxes(long id) : base(id) { }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.InitializeAxes();
        }
    }

    public class SkySetAxisPosition : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _position;

        public SkySetAxisPosition(long id, Axis axis, double position) : base(id)
        {
            _axis = axis;
            _position = position;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetAxisPosition(_axis, BasicMath.DegToRad(_position));
        }
    }

    public class SkySetAxisPositionCounter : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly int _position;

        public SkySetAxisPositionCounter(long id, Axis axis, int position) : base(id)
        {
            _axis = axis;
            _position = position;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetAxisPositionCounter(_axis, _position);
        }
    }

    public class SkySetMotionMode : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly int _func;
        private readonly int _direction;

        public SkySetMotionMode(long id, Axis axis, int func, int direction) : base(id)
        {
            _axis = axis;
            _func = func;
            _direction = direction;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetMotionMode(_axis, _func, _direction);
        }
    }

    public class SkyStartMotion : SkyActionCommand
    {
        private readonly Axis _axis;

        public SkyStartMotion(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.StartMotion(_axis);
        }
    }

    public class SkyAxisGoToTarget : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _targetPosition;

        public SkyAxisGoToTarget(long id, Axis axis, double targetPosition) : base(id)
        {
            _axis = axis;
            _targetPosition = targetPosition;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AxisGoToTarget(_axis, BasicMath.DegToRad(_targetPosition));
        }
    }

    public class SkySetAlternatingPPec : SkyActionCommand
    {
        private readonly bool _on;

        public SkySetAlternatingPPec(long id, bool on) : base(id)
        {
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.AlternatingPPec = _on;
        }
    }

    public class SkySetDecPulseToGoTo : SkyActionCommand
    {
        private readonly bool _on;

        public SkySetDecPulseToGoTo(long id, bool on) : base(id)
        {
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.DecPulseGoTo = _on;
        }
    }

    public class SkySetSouthernHemisphere : SkyActionCommand
    {
        private readonly bool _southernHemisphere;

        public SkySetSouthernHemisphere(long id, bool southernHemisphere) : base(id)
        {
            _southernHemisphere = southernHemisphere;
        }

        public override dynamic Result => null;

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SouthernHemisphere = _southernHemisphere;
        }
    }

    public class SkySetEncoder : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly bool _on;

        public SkySetEncoder(long id, Axis axis, bool on) : base(id)
        {
            _axis = axis;
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetEncoder(_axis, _on);
        }
    }

    public class SkySetMonitorPulse : SkyActionCommand
    {
        private readonly bool _on;

        public SkySetMonitorPulse(long id, bool on) : base(id)
        {
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.MonitorPulse = _on;
        }
    }

    public class SkySetMinPulseDuration : SkyActionCommand
    {
        private readonly int _duration;
        private readonly Axis _axis;

        public SkySetMinPulseDuration(long id, Axis axis, int duration) : base(id)
        {
            _axis = axis;
            _duration = duration;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            if (_axis == Axis.Axis1)
            {
                skyWatcher.MinPulseDurationRa = _duration;
            }
            else
            {
                skyWatcher.MinPulseDurationDec = _duration;
            }
        }
    }

    public class SkySetPPecTrain : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly bool _on;

        public SkySetPPecTrain(long id, Axis axis, bool on) : base(id)
        {
            _axis = axis;
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetPPecTrain(_axis, _on);
        }
    }

    public class SkySetPolarLedLevel : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly int _level;

        public SkySetPolarLedLevel(long id, Axis axis, int level) : base(id)
        {
            _axis = axis;
            _level = level;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetPolarLedLevel(_axis, _level);
        }
    }

    public class SkySetFullCurrent : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly bool _on;

        public SkySetFullCurrent(long id, Axis axis, bool on) : base(id)
        {
            _axis = axis;
            _on = on;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetFullCurrent(_axis, _on);
        }
    }

    public class SkySetGotoTargetIncrement : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly long _stepsCount;

        public SkySetGotoTargetIncrement(long id, Axis axis, long stepsCount) : base(id)
        {
            _axis = axis;
            _stepsCount = stepsCount;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetGotoTargetIncrement(_axis, _stepsCount);
        }
    }

    public class SkySetStepSpeed : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly long _stepSpeed;

        public SkySetStepSpeed(long id, Axis axis, long stepSpeed) : base(id)
        {
            _axis = axis;
            _stepSpeed = stepSpeed;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetStepSpeed(_axis, _stepSpeed);
        }
    }

    public class SkySetBreakPointIncrement : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly long _stepsCount;

        public SkySetBreakPointIncrement(long id, Axis axis, long stepsCount) : base(id)
        {
            _axis = axis;
            _stepsCount = stepsCount;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetBreakPointIncrement(_axis, _stepsCount);
        }
    }

    public class SkySetSt4GuideRate : SkyActionCommand
    {
        private readonly int _rate;

        public SkySetSt4GuideRate(long id, int rate) : base(id)
        {
            _rate = rate;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetSt4GuideRate(_rate);
        }
    }

    public class SkySetTargetPosition : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _position;

        public SkySetTargetPosition(long id, Axis axis, double position) : base(id)
        {
            _axis = axis;
            _position = position;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetTargetPosition(_axis, _position);
        }
    }

    public class SkySetHomePositionIndex : SkyActionCommand
    {
        private readonly Axis _axis;

        public SkySetHomePositionIndex(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetHomePositionIndex(_axis);
        }
    }

    public class SkyLoadDefaultMountSettings : SkyActionCommand
    {
        public SkyLoadDefaultMountSettings(long id) : base(id) { }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.LoadDefaultMountSettings();
        }
    }

    public class SkySyncAxis : SkyActionCommand
    {
        private readonly Axis _axis;
        private readonly double _position;

        public SkySyncAxis(long id, Axis axis, double position) : base(id)
        {
            _axis = axis;
            _position = position;
        }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.SetAxisPosition(_axis, BasicMath.DegToRad(_position));
        }
    }

    public class SkyUpdateSteps : SkyActionCommand
    {
        public SkyUpdateSteps(long id) : base(id) { }

        protected override void ExecuteAction(SkyWatcher skyWatcher)
        {
            skyWatcher.UpdateSteps();
        }
    }

    // Query Commands (return results)
    public class SkyCanAxisSlewsIndependent : SkyQueryCommand
    {
        public SkyCanAxisSlewsIndependent(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanAxisSlewsIndependent;
        }
    }

    public class SkyCanAzEq : SkyQueryCommand
    {
        public SkyCanAzEq(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanAzEq;
        }
    }

    public class SkyCanDualEncoders : SkyQueryCommand
    {
        public SkyCanDualEncoders(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanDualEncoders;
        }
    }

    public class SkyCanHalfTrack : SkyQueryCommand
    {
        public SkyCanHalfTrack(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanHalfTrack;
        }
    }

    public class SkyCanHomeSensors : SkyQueryCommand
    {
        public SkyCanHomeSensors(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanHomeSensors;
        }
    }

    public class SkyCanPolarLed : SkyQueryCommand
    {
        public SkyCanPolarLed(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanPolarLed;
        }
    }

    public class SkyCanPPec : SkyQueryCommand
    {
        public SkyCanPPec(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanPPec;
        }
    }

    public class SkyCanWifi : SkyQueryCommand
    {
        public SkyCanWifi(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CanWifi;
        }
    }

    public class SkyCmdToMount : SkyQueryCommand
    {
        private readonly int _axis;
        private readonly string _cmd;
        private readonly string _cmdData;
        private readonly string _ignoreWarnings;

        public SkyCmdToMount(long id, int axis, string cmd, string cmdData, string ignoreWarnings) : base(id)
        {
            _axis = axis;
            _cmd = cmd;
            _cmdData = cmdData;
            _ignoreWarnings = ignoreWarnings;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.CmdToMount(_axis, _cmd, _cmdData, _ignoreWarnings);
        }
    }

    public class SkyGetAdvancedCmdSupport : SkyQueryCommand
    {
        public SkyGetAdvancedCmdSupport(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAdvancedCmdSupport();
        }
    }

    public class SkyGetAngleToStep : SkyQueryCommand
    {
        private readonly Axis _axis;
        private readonly double _angleInRad;

        public SkyGetAngleToStep(long id, Axis axis, double angleInRad) : base(id)
        {
            _axis = axis;
            _angleInRad = angleInRad;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAngleToStep(_axis, _angleInRad);
        }
    }

    public class SkyGetAxisPosition : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetAxisPosition(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisPosition(_axis);
        }
    }

    public class SkyGetAxisPositionCounter : SkyQueryCommand
    {
        private readonly Axis _axis;
        private readonly bool _raw;

        public SkyGetAxisPositionCounter(long id, Axis axis, bool raw = false) : base(id)
        {
            _axis = axis;
            _raw = raw;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisPositionCounter(_axis, _raw);
        }
    }

    public class SkyGetAxisPositionDate : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetAxisPositionDate(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisPositionDate(_axis);
        }
    }

    public class SkyGetControllerVoltage : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetControllerVoltage(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetControllerVoltage(_axis);
        }
    }

    public class SkyGetRampDownRange : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetRampDownRange(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetRampDownRange(_axis);
        }
    }

    public class SkyGetCapabilities : SkyQueryCommand
    {
        public SkyGetCapabilities(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetCapabilities();
        }
    }

    public class SkyGetEncoderCount : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetEncoderCount(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetEncoderCount(_axis);
        }
    }

    public class SkyGetJ : SkyQueryCommand
    {
        private readonly Axis _axis;
        private readonly bool _raw;

        public SkyGetJ(long id, Axis axis, bool raw) : base(id)
        {
            _axis = axis;
            _raw = raw;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.Get_j(_axis, _raw);
        }
    }

    public class SkyGetLastGoToTarget : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetLastGoToTarget(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetLastGoToTarget(_axis);
        }
    }

    public class SkyGetLastSlewSpeed : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetLastSlewSpeed(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetLastSlewSpeed(_axis);
        }
    }

    public class SkyGetHomePosition : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetHomePosition(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetHomePosition(_axis);
        }
    }

    public class SkyGetMotorCardVersion : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetMotorCardVersion(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetMotorCardVersion(_axis);
        }
    }

    public class SkyGetPecPeriod : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetPecPeriod(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetPecPeriod(_axis);
        }
    }

    public class SkyGetPositionsAndTime : SkyQueryCommand
    {
        private readonly bool _raw;

        public SkyGetPositionsAndTime(long id, bool raw) : base(id)
        {
            _raw = raw;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetPositionsAndTime(_raw);
        }
    }

    public class SkyGetPositionsInDegrees : SkyQueryCommand
    {
        public SkyGetPositionsInDegrees(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetPositionsInDegrees();
        }
    }

    public class SkyGetSteps : SkyQueryCommand
    {
        public SkyGetSteps(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetSteps();
        }
    }

    public class SkyGetSiderealRate : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyGetSiderealRate(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetSiderealRate(_axis);
        }
    }

    public class SkyGetStepToAngle : SkyQueryCommand
    {
        private readonly Axis _axis;
        private readonly long _steps;

        public SkyGetStepToAngle(long id, Axis axis, long steps) : base(id)
        {
            _axis = axis;
            _steps = steps;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetStepToAngle(_axis, _steps);
        }
    }

    public class SkyMountType : SkyQueryCommand
    {
        public SkyMountType(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.MountType;
        }
    }

    public class SkyMountVersion : SkyQueryCommand
    {
        public SkyMountVersion(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.MountVersion;
        }
    }

    public class SkyIsAxisFullStop : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyIsAxisFullStop(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStatus(_axis).FullStop;
        }
    }

    public class SkyIsConnected : SkyQueryCommand
    {
        public SkyIsConnected(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.IsConnected;
        }
    }

    public class SkyIsHighSpeed : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyIsHighSpeed(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStatus(_axis).HighSpeed;
        }
    }

    public class SkyIsPPecOn : SkyQueryCommand
    {
        public SkyIsPPecOn(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.IsPPecOn;
        }
    }

    public class SkyIsPPecInTrainingOn : SkyQueryCommand
    {
        public SkyIsPPecInTrainingOn(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.IsPPecInTrainingOn;
        }
    }

    public class SkyIsSlewing : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyIsSlewing(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStatus(_axis).Slewing;
        }
    }

    public class SkyIsSlewingForward : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyIsSlewingForward(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStatus(_axis).SlewingForward;
        }
    }

    public class SkyIsSlewingTo : SkyQueryCommand
    {
        private readonly Axis _axis;

        public SkyIsSlewingTo(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStatus(_axis).SlewingTo;
        }
    }

    public class SkyGetAxisVersions : SkyQueryCommand
    {
        public SkyGetAxisVersions(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisVersions();
        }
    }

    public class SkyGetAxisStringVersions : SkyQueryCommand
    {
        public SkyGetAxisStringVersions(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetAxisStringVersions();
        }
    }

    public class SkySetPPec : SkyQueryCommand
    {
        private readonly Axis _axis;
        private readonly bool _on;

        public SkySetPPec(long id, Axis axis, bool on) : base(id)
        {
            _axis = axis;
            _on = on;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.SetPPec(_axis, _on);
        }
    }

    public class SkySetSnapPort : SkyQueryCommand
    {
        private readonly bool _on;
        private readonly int _port;

        public SkySetSnapPort(long id, int port, bool on) : base(id)
        {
            _on = on;
            _port = port;
        }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.SetSnapPort(_port, _on);
        }
    }

    public class SkyGetStepsPerRevolution : SkyQueryCommand
    {
        public SkyGetStepsPerRevolution(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetStepsPerRevolution();
        }
    }

    public class SkyGetStepTimeFreq : SkyQueryCommand
    {
        public SkyGetStepTimeFreq(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetStepTimeFreq();
        }
    }

    public class SkyGetHighSpeedRatio : SkyQueryCommand
    {
        public SkyGetHighSpeedRatio(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetHighSpeedRatio();
        }
    }

    public class SkyGetLowSpeedGotoMargin : SkyQueryCommand
    {
        public SkyGetLowSpeedGotoMargin(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetLowSpeedGotoMargin();
        }
    }

    public class SkyGetFactorRadRateToInt : SkyQueryCommand
    {
        public SkyGetFactorRadRateToInt(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetFactorRadRateToInt();
        }
    }

    public class SkyGetFactorStepToRad : SkyQueryCommand
    {
        public SkyGetFactorStepToRad(long id) : base(id) { }

        protected override dynamic ExecuteQuery(SkyWatcher skyWatcher)
        {
            return skyWatcher.GetFactorStepToRad();
        }
    }
}

