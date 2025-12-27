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
using GreenSwamp.Alpaca.Mount.Commands;

namespace GreenSwamp.Alpaca.Mount.Simulator
{
    /// <summary>
    /// Legacy interface for backward compatibility
    /// </summary>
    public interface IMountCommand : ICommand<Actions>
    {
        // Explicitly redeclare Result for backward compatibility with existing code
        new dynamic Result { get; }
    }

    /// <summary>
    /// Abstract base class for all Mount commands providing common functionality
    /// </summary>
    public abstract class MountCommandBase : CommandBase<Actions>, IMountCommand
    {
        protected MountCommandBase(long id) : base(id, MountQueue.Instance)
        {
        }
    }

    /// <summary>
    /// Abstract base class for Mount commands that return query results
    /// </summary>
    public abstract class MountQueryCommand : QueryCommand<Actions>, IMountCommand
    {
        protected MountQueryCommand(long id) : base(id, MountQueue.Instance)
        {
        }
    }

    /// <summary>
    /// Abstract base class for Mount commands that perform actions without returning results
    /// </summary>
    public abstract class MountActionCommand : ActionCommand<Actions>, IMountCommand
    {
        protected MountActionCommand(long id) : base(id, MountQueue.Instance)
        {
        }
    }

    // Action Commands (no result returned)
    public class CmdRaDecRate : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _rate;

        public CmdRaDecRate(long id, Axis axis, double rate) : base(id)
        {
            _axis = axis;
            _rate = rate;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.RaDecRate(_axis, _rate);
        }
    }

    public class CmdMoveAxisRate : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _rate;

        public CmdMoveAxisRate(long id, Axis axis, double rate) : base(id)
        {
            _axis = axis;
            _rate = rate;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.MoveAxisRate(_axis, _rate);
        }
    }

    public class CmdGotoSpeed : MountQueryCommand
    {
        private readonly int _rate;

        public CmdGotoSpeed(long id, int rate) : base(id)
        {
            _rate = rate;
        }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            actions.GotoRate(_rate);
            return _rate;
        }
    }

    public class CmdAxesSteps : MountActionCommand
    {
        public CmdAxesSteps(long id) : base(id) { }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxesSteps();
        }
    }

    public class CmdAxisStop : MountActionCommand
    {
        private readonly Axis _axis;

        public CmdAxisStop(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxisStop(_axis);
        }
    }

    public class CmdHcSlew : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _rate;

        public CmdHcSlew(long id, Axis axis, double rate) : base(id)
        {
            _axis = axis;
            _rate = rate;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.HcSlew(_axis, _rate);
        }
    }

    public class CmdAxisTracking : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _rate;

        public CmdAxisTracking(long id, Axis axis, double rate) : base(id)
        {
            _axis = axis;
            _rate = rate;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxisTracking(_axis, _rate);
        }
    }

    public class CmdAxisGoToTarget : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _targetPosition;

        public CmdAxisGoToTarget(long id, Axis axis, double targetPosition) : base(id)
        {
            _axis = axis;
            _targetPosition = targetPosition;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxisGoToTarget(_axis, _targetPosition);
        }
    }

    public class CmdAxisToDegrees : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _degrees;

        public CmdAxisToDegrees(long id, Axis axis, double degrees) : base(id)
        {
            _axis = axis;
            _degrees = degrees;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxisToDegrees(_axis, _degrees);
        }
    }

    public class CmdAxisPulse : MountActionCommand
    {
        private readonly Axis _axis;
        private readonly double _guideRate;
        private readonly int _duration;
        private readonly CancellationToken _token;

        public CmdAxisPulse(long id, Axis axis, double guideRate, int duration, CancellationToken token) : base(id)
        {
            _axis = axis;
            _guideRate = guideRate;
            _duration = duration;
            _token = token;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.AxisPulse(_axis, _guideRate, _duration, _token);
        }
    }

    public class CmdHomeSensorReset : MountActionCommand
    {
        private readonly Axis _axis;

        public CmdHomeSensorReset(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.HomeSensorReset(_axis);
        }
    }

    public class CmdSnapPort : MountActionCommand
    {
        private readonly int _port;
        private readonly bool _on;

        public CmdSnapPort(long id, int port, bool on) : base(id)
        {
            _port = port;
            _on = on;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.SnapPort(_port, _on);
        }
    }

    public class CmdSetMonitorPulse : MountActionCommand
    {
        private readonly bool _on;

        public CmdSetMonitorPulse(long id, bool on) : base(id)
        {
            _on = on;
        }

        protected override void ExecuteAction(Actions actions)
        {
            actions.MonitorPulse = _on;
        }
    }

    // Query Commands (return results)
    public class CmdAxesDegrees : MountQueryCommand
    {
        public CmdAxesDegrees(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.AxesDegrees();
        }
    }

    public class CmdAxisSteps : MountQueryCommand
    {
        public CmdAxisSteps(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.AxisSteps();
        }
    }

    public class AxisStepsDt : MountQueryCommand
    {
        private readonly Axis _axis;

        public AxisStepsDt(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.AxisStepsDt(_axis);
        }
    }

    public class CmdAxisStatus : MountQueryCommand
    {
        private readonly Axis _axis;

        public CmdAxisStatus(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.AxisStatus(_axis);
        }
    }

    public class GetHomeSensorCapability : MountQueryCommand
    {
        public GetHomeSensorCapability(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.MountInfo.CanHomeSensors;
        }
    }

    public class CmdHomeSensor : MountQueryCommand
    {
        private readonly Axis _axis;

        public CmdHomeSensor(long id, Axis axis) : base(id)
        {
            _axis = axis;
        }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.HomeSensor(_axis);
        }
    }

    public class CmdFactorSteps : MountQueryCommand
    {
        public CmdFactorSteps(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.FactorSteps();
        }
    }

    public class CmdMountName : MountQueryCommand
    {
        public CmdMountName(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.MountName();
        }
    }

    public class CmdMountVersion : MountQueryCommand
    {
        public CmdMountVersion(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.MountVersion();
        }
    }

    public class CmdSpr : MountQueryCommand
    {
        public CmdSpr(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.Spr();
        }
    }

    public class CmdSpw : MountQueryCommand
    {
        public CmdSpw(long id) : base(id) { }

        protected override dynamic ExecuteQuery(Actions actions)
        {
            return actions.Spw();
        }
    }
}
