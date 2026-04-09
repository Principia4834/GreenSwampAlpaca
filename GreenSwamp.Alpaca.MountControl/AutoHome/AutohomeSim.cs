using System.Reflection;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.MountControl.AutoHome
{
    public class AutoHomeSim
    {
        private const int MaxSteps = Int32.MaxValue;
        private const int MinSteps = Int32.MinValue;
        private int TripPosition { get; set; }
        private bool HasHomeSensor { get; set; }
        private readonly SkySettings _settings;
        private readonly ICommandQueue<Actions> _mountQueue;
        private readonly MountControl.Mount _mount;

        /// <summary>
        /// auto home for the simulator
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="mountQueue"></param>
        /// <param name="mount"></param>
        public AutoHomeSim(SkySettings settings, ICommandQueue<Actions> mountQueue, MountControl.Mount mount)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = "Start"
            };
            MonitorLog.LogToMonitor(monitorItem);
            this._settings = settings;
            _mountQueue = mountQueue;
            _mount = mount;
        }

        /// <summary>
        /// Check for home sensor capability
        /// </summary>
        private void HomeSensorCapabilityCheck()
        {
            HasHomeSensor = false;
            var canHomeCmdA = new GetHomeSensorCapability(_mountQueue.NewId, _mountQueue);
            bool.TryParse(Convert.ToString(_mountQueue.GetCommandResult(canHomeCmdA).Result), out bool hasHome);
            HasHomeSensor = hasHome;
        }

        /// <summary>
        /// Gets the direction to home sensor or if null then TripPosition was set
        /// </summary>
        /// <param name="axis"></param>
        /// <returns></returns>
        private bool? GetHomeSensorStatus(Axis axis)
        {
            var sensorStatusCmd = new CmdHomeSensor(_mountQueue.NewId, _mountQueue, axis);
            var sensorStatus = (int)_mountQueue.GetCommandResult(sensorStatusCmd).Result;
            switch (sensorStatus)
            {
                case MaxSteps:
                    return false;
                case MinSteps:
                    return true;
                default:
                    TripPosition = sensorStatus;
                    return null;
            }
        }

        /// <summary>
        /// Checks for valid home sensor status after a reset
        /// </summary>
        /// <param name="axis"></param>
        /// <returns></returns>
        private bool? GetValidStatus(Axis axis)
        {
            for (var i = 0; i < 2; i++)
            {
                ResetHomeSensor(axis);
                var status = GetHomeSensorStatus(axis);
                // If status is valid, return it
                if (status == true || status == false)
                    return status;
                // If not, slew a small amount and try again
                SlewAxis(1, axis);
            }
            return null;
        }

        /// <summary>
        /// Reset home sensor :Wx080000[0D]
        /// </summary>
        /// <param name="axis"></param>
        private void ResetHomeSensor(Axis axis)
        {
            var reset = new CmdHomeSensorReset(_mountQueue.NewId, _mountQueue, axis);

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{reset.Successful}|{axis}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Start auto home process per axis with max degrees default at 90
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="offSetDec"></param>
        /// <param name="maxMove"></param>
        /// <returns></returns>
        public AutoHomeResult StartAutoHome(Axis axis, int maxMove = 100, int offSetDec = 0)
        {
            HomeSensorCapabilityCheck();
            if (!HasHomeSensor) return AutoHomeResult.HomeCapabilityCheckFailed;
            _ = new CmdAxisStop(_mountQueue.NewId, _mountQueue, axis);
            if (_mount.Tracking) _mount.ApplyTracking(false);
            //StartCount = GetEncoderCount(axis);
            var totalMove = 0.0;
            // ReSharper disable once RedundantAssignment
            var clockwise = false;
            var startOvers = 0;
            bool? status;
            bool? loopStatus = null;
            _mount._autoHomeProgressBar += 5;
            Alpaca.Mount.Simulator.Settings.AutoHomeAxisX = (int) _settings.AutoHomeAxisX;
            Alpaca.Mount.Simulator.Settings.AutoHomeAxisY = (int) _settings.AutoHomeAxisY;

            // slew away from those that start at home position
            var slewResult = SlewAxis(3.3, axis);
            totalMove += 3.3;
            if (slewResult != AutoHomeResult.Success) return slewResult;

            // 5 degree loops to look for sensor
            for (var i = 0; i <= (maxMove / 5); i++)
            {
                if (_mount._autoHomeStop) return AutoHomeResult.StopRequested;
                if (totalMove >= maxMove) return AutoHomeResult.HomeSensorNotFound;
                if (startOvers >= 2) return AutoHomeResult.TooManyRestarts;

                status = GetValidStatus(axis);
                var lastStatus = status;

                // check status last loop vs this loop and see if status changed
                if (status != null && loopStatus != null && status != loopStatus)
                {
                    // status changed but no detection of home
                    slewResult = SlewAxis(2.7, axis, clockwise);
                    if (slewResult != AutoHomeResult.Success) return slewResult;
                    status = GetHomeSensorStatus(axis);
                    if (status != null)
                    {
                        // should be far enough from the dead zone to start over.
                        i = 0;
                        startOvers++;
                        continue; //start over
                    }
                    break; //found home
                }

                if (status == null)
                    return _mount._autoHomeStop ? AutoHomeResult.StopRequested : AutoHomeResult.FailedHomeSensorReset;

                clockwise = (status == true);
                _mount._autoHomeProgressBar += 1;

                slewResult = SlewAxis(5.0, axis, clockwise);
                if (slewResult != AutoHomeResult.Success) return slewResult;
                totalMove += 5.0;
                status = GetHomeSensorStatus(axis);
                loopStatus = status;
                if (status != null)
                {
                    if (status == lastStatus) continue;
                    slewResult = SlewAxis(7.5, axis, clockwise);
                    if (slewResult != AutoHomeResult.Success) return slewResult;
                    status = GetHomeSensorStatus(axis);
                    loopStatus = status;
                    if (status != null)
                    {
                        i = 0;
                        startOvers++;
                        continue; //start over
                    }
                }
                break;//found home
            }
            if (_mount._autoHomeStop) return AutoHomeResult.StopRequested;
            if (totalMove >= maxMove) return AutoHomeResult.HomeSensorNotFound;
            if (startOvers >= 2) return AutoHomeResult.TooManyRestarts;

            // slew to detected home
            slewResult = SlewToHome(axis);
            if (slewResult != AutoHomeResult.Success) return slewResult;

            _mount._autoHomeProgressBar += 5;

            // 3.7 degree slew away from home for a validation move
            slewResult = SlewAxis(3.7, axis);
            if (slewResult != AutoHomeResult.Success) return slewResult;
            status = GetValidStatus(axis);
            switch (status)
            {
                case null:
                    return _mount._autoHomeStop ? AutoHomeResult.StopRequested : AutoHomeResult.FailedHomeSensorReset;
                case true:
                case false:
                    clockwise = (bool)status;
                    break;
            }

            _mount._autoHomeProgressBar += 5;

            // slew back over home to validate home position
            slewResult = SlewAxis(5, axis, clockwise);
            if (slewResult != AutoHomeResult.Success) return slewResult;
            status = GetHomeSensorStatus(axis);
            switch (status)
            {
                case null:
                    // home found
                    break;
                case true:
                case false:
                    return AutoHomeResult.HomeSensorNotFound;
            }

            _mount._autoHomeProgressBar += 5;

            // slew back to remove backlash
            slewResult = SlewAxis(3, axis, !clockwise);
            if (slewResult != AutoHomeResult.Success) return slewResult;

            _mount._autoHomeProgressBar += 5;

            // slew to home
            slewResult = SlewToHome(axis);
            if (slewResult != AutoHomeResult.Success) return slewResult;

            // Dec offset for side saddles
            if (Math.Abs(offSetDec) > 0 && axis == Axis.Axis2)
            {
                slewResult = SlewAxis(Math.Abs(offSetDec), axis, offSetDec < 0);
                if (slewResult != AutoHomeResult.Success) return slewResult;
            }

            return AutoHomeResult.Success;
        }

        /// <summary>
        /// Slew to home based on TripPosition already being set
        /// </summary>
        /// <param name="axis"></param>
        private AutoHomeResult SlewToHome(Axis axis)
        {
            if (_mount._autoHomeStop) return AutoHomeResult.StopRequested;

            var a = TripPosition / 36000;
            // ToDo AWW replace with proper context - needs change to autohome signature, may need updates for each invocation
            var positions = Axes.MountAxis2Mount(_settings, _mount.AppAxes.X, _mount.AppAxes.Y);
            switch (axis)
            {
                case Axis.Axis1:
                    _mount.SlewSync([(double)a, positions[1]], SlewType.SlewMoveAxis);
                    break;
                case Axis.Axis2:
                    _mount.SlewSync([positions[0], (double)a], SlewType.SlewMoveAxis);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
            }

            _ = new CmdAxisStop(_mountQueue.NewId, _mountQueue, axis);
            //Thread.Sleep(1000);
            return AutoHomeResult.Success;
        }

        /// <summary>
        /// Slews degrees from current position using the goto from the server
        /// </summary>
        /// <param name="degrees"></param>
        /// <param name="direction"></param>
        /// <param name="axis"></param>
        private AutoHomeResult SlewAxis(double degrees, Axis axis, bool direction = false)
        {
            if (_mount._autoHomeStop) return AutoHomeResult.StopRequested;

            if (_mount.Tracking)
            {
                // ToDo - implement if needed
                // SkyServer.TrackingSpeak = false;
                _mount.ApplyTracking(false);
            }

            // ToDo AWW replace with proper context - needs change to autohome signature, may need updates for each invocation
            var positions = Axes.MountAxis2Mount(_settings, _mount.AppAxes.X, _mount.AppAxes.Y);

            switch (axis)
            {
                case Axis.Axis1:
                    degrees = direction ? -Math.Abs(degrees) : Math.Abs(degrees);
                    if (_settings.Latitude < 0) degrees = direction ? Math.Abs(degrees) : -Math.Abs(degrees);
                    _mount.SlewSync([positions[0] + degrees, positions[1]], SlewType.SlewMoveAxis);
                    break;
                case Axis.Axis2:
                    degrees = direction ? -Math.Abs(degrees) : Math.Abs(degrees);
                    _mount.SlewSync([positions[0], positions[1] + degrees], SlewType.SlewMoveAxis);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
            }

            _ = new CmdAxisStop(_mountQueue.NewId, _mountQueue, axis);
            return AutoHomeResult.Success;
        }
    }
}