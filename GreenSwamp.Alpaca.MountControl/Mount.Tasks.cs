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

// ============================================================================
// Mount.Tasks.cs - Mount task dispatch (formerly SkyServer.SimTasks / SkyTasks)
// ============================================================================

using ASCOM.Tools;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;
using SkyWatcherErrorCode = GreenSwamp.Alpaca.Mount.SkyWatcher.ErrorCode;

namespace GreenSwamp.Alpaca.MountControl
{
    public partial class Mount
    {
        #region Error Handling

        /// <summary>
        /// Handles MountControlException and SkyServerException for this mount instance.
        /// </summary>
internal void MountErrorHandler(Exception ex)
{
    var monitorItem = new MonitorEntry
    {
        Datetime = HiResDateTime.UtcNow,
        Device = MonitorDevice.Server,
        Category = MonitorCategory.Server,
        Type = MonitorType.Error,
        Method = MethodBase.GetCurrentMethod()?.Name,
        Thread = Environment.CurrentManagedThreadId,
        Message = $"{ex.Message}|{ex.StackTrace}"
    };
    LogMount(monitorItem);

    switch (ex)
    {
        case MountControlException mounterr:
            switch (mounterr.ErrorCode)
            {
                case SkyWatcherErrorCode.ErrInvalidId:
                case SkyWatcherErrorCode.ErrAlreadyConnected:
                case SkyWatcherErrorCode.ErrNotConnected:
                case SkyWatcherErrorCode.ErrInvalidData:
                case SkyWatcherErrorCode.ErrSerialPortBusy:
                case SkyWatcherErrorCode.ErrMountNotFound:
                case SkyWatcherErrorCode.ErrNoResponseAxis1:
                case SkyWatcherErrorCode.ErrNoResponseAxis2:
                case SkyWatcherErrorCode.ErrAxisBusy:
                case SkyWatcherErrorCode.ErrMaxPitch:
                case SkyWatcherErrorCode.ErrMinPitch:
                case SkyWatcherErrorCode.ErrUserInterrupt:
                case SkyWatcherErrorCode.ErrAlignFailed:
                case SkyWatcherErrorCode.ErrUnimplemented:
                case SkyWatcherErrorCode.ErrWrongAlignmentData:
                case SkyWatcherErrorCode.ErrQueueFailed:
                case SkyWatcherErrorCode.ErrTooManyRetries:
                default:
                    MountStop();
                    _mountError = mounterr;
                    break;
            }
            break;

        case SkyServerException skyerr:
            switch (skyerr.ErrorCode)
            {
                case ErrorCode.ErrMount:
                case ErrorCode.ErrExecutingCommand:
                case ErrorCode.ErrUnableToDeqeue:
                case ErrorCode.ErrSerialFailed:
                default:
                    MountStop();
                    _mountError = skyerr;
                    break;
            }
            break;

        default:
            _mountError = ex;
            MountStop();
            break;
    }
}

        /// <summary>Checks command object for errors and unsuccessful execution.</summary>
        /// <returns>true if errors found or not successful</returns>
        private static bool CheckSkyErrors(ISkyCommand command)
        {
            if (command.Exception != null)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"{command.Successful}|{command.Exception.Message}|{command.Exception.StackTrace}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return !command.Successful || command.Exception != null;
        }

        #endregion

        #region Simulator Tasks

        /// <summary>
        /// Routes task commands to this mount's Simulator queue.
        /// </summary>
        public void SimTasks(MountTaskName taskName)
        {
            if (!IsMountRunning) return;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{taskName}"
            };
            LogMount(monitorItem);

            var settings = Settings;
            var q = SimQueue!;

            switch (Settings.Mount)
            {
                case MountType.SkyWatcher:
                    break;
                case MountType.Simulator:
                    switch (taskName)
                    {
                        case MountTaskName.AllowAdvancedCommandSet:
                            break;
                        case MountTaskName.AlternatingPpec:
                            break;
                        case MountTaskName.CanAdvancedCmdSupport:
                            _canAdvancedCmdSupport = false;
                            break;
                        case MountTaskName.CanPpec:
                            _canPPec = false;
                            break;
                        case MountTaskName.CanPolarLed:
                            _canPolarLed = false;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeCmd = new GetHomeSensorCapability(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(canHomeCmd).Result), out bool hasHome);
                            _canHomeSensor = hasHome;
                            break;
                        case MountTaskName.DecPulseToGoTo:
                            break;
                        case MountTaskName.Encoders:
                            break;
                        case MountTaskName.FullCurrent:
                            break;
                        case MountTaskName.LoadDefaults:
                            break;
                        case MountTaskName.StopAxes:
                            _ = new CmdAxisStop(0, q, Axis.Axis1);
                            _ = new CmdAxisStop(0, q, Axis.Axis2);
                            break;
                        case MountTaskName.InstantStopAxes:
                            break;
                        case MountTaskName.SetSouthernHemisphere:
                            break;
                        case MountTaskName.SyncAxes:
                            var appAxes = AppAxes;
                            var sync = Axes.AxesAppToMount([appAxes.X, appAxes.Y], settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, sync[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, sync[1]);
                            break;
                        case MountTaskName.SyncTarget:
                            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec, settings: settings);
                            var targetR = Axes.RaDecToAxesXy([a.X, a.Y], settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetR[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetR[1]);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSync = _altAzSync;
                            var targetA = new[] { altAzSync.Y, altAzSync.X };
                            targetA = Axes.AzAltToAxesXy(targetA, settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetA[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetA[1]);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new CmdSetMonitorPulse(0, q, _monitorPulse);
                            break;
                        case MountTaskName.Pec:
                            break;
                        case MountTaskName.PecTraining:
                            break;
                        case MountTaskName.Capabilities:
                            _capabilities = @"N/A";
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            break;
                        case MountTaskName.SetSnapPort1:
                            _ = new CmdSnapPort(0, q, 1, _snapPort1);
                            _snapPort1Result = false;
                            break;
                        case MountTaskName.SetSnapPort2:
                            _ = new CmdSnapPort(0, q, 2, _snapPort2);
                            _snapPort2Result = true;
                            break;
                        case MountTaskName.MountName:
                            var mountNameCmd = new CmdMountName(q.NewId, q);
                            _mountName = (string)q.GetCommandResult(mountNameCmd).Result;
                            break;
                        case MountTaskName.GetAxisVersions:
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            break;
                        case MountTaskName.MountVersion:
                            var mountVersionCmd = new CmdMountVersion(q.NewId, q, Axis.Axis1);
                            _mountVersion[0] = (string)q.GetCommandResult(mountVersionCmd).Result;
                            mountVersionCmd = new CmdMountVersion(q.NewId, q, Axis.Axis2);
                            _mountVersion[1] = (string)q.GetCommandResult(mountVersionCmd).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var spr = new CmdSpr(q.NewId, q);
                            var sprnum = (long)q.GetCommandResult(spr).Result;
                            _stepsPerRevolution = [sprnum, sprnum];
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var spw = new CmdSpw(q.NewId, q);
                            var spwnum = (double)q.GetCommandResult(spw).Result;
                            _stepsWormPerRevolution = [spwnum, spwnum];
                            break;
                        case MountTaskName.SetHomePositions:
                            var homeAxesSim = HomeAxes;
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, homeAxesSim.X);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, homeAxesSim.Y);
                            break;
                        case MountTaskName.GetFactorStep:
                            var factorStepCmd = new CmdFactorSteps(q.NewId, q);
                            _factorStep[0] = (double)q.GetCommandResult(factorStepCmd).Result;
                            _factorStep[1] = _factorStep[0];
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(taskName), taskName, null);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        #region SkyWatcher Tasks

        /// <summary>
        /// Routes task commands to this mount's SkyWatcher queue.
        /// </summary>
        public void SkyTasks(MountTaskName taskName)
        {
            if (!IsMountRunning) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{taskName}"
            };

            var settings = Settings;
            var q = SkyQueue!;

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    switch (taskName)
                    {
                        case MountTaskName.AllowAdvancedCommandSet:
                            _ = new SkyAllowAdvancedCommandSet(0, q, Settings.AllowAdvancedCommandSet);
                            break;
                        case MountTaskName.AlternatingPpec:
                            _ = new SkySetAlternatingPPec(0, q, Settings.AlternatingPPec);
                            break;
                        case MountTaskName.DecPulseToGoTo:
                            _ = new SkySetDecPulseToGoTo(0, q, Settings.DecPulseToGoTo);
                            break;
                        case MountTaskName.CanAdvancedCmdSupport:
                            var skyCanAdvanced = new SkyGetAdvancedCmdSupport(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyCanAdvanced).Result), out bool pAdvancedResult);
                            _canAdvancedCmdSupport = pAdvancedResult;
                            break;
                        case MountTaskName.CanPpec:
                            var skyMountCanPpec = new SkyCanPPec(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyMountCanPpec).Result), out bool pPecResult);
                            _canPPec = pPecResult;
                            break;
                        case MountTaskName.CanPolarLed:
                            var skyCanPolarLed = new SkyCanPolarLed(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyCanPolarLed).Result), out bool polarLedResult);
                            _canPolarLed = polarLedResult;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeSky = new SkyCanHomeSensors(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(canHomeSky).Result), out bool homeSensorResult);
                            _canHomeSensor = homeSensorResult;
                            break;
                        case MountTaskName.Capabilities:
                            var skyCap = new SkyGetCapabilities(q.NewId, q);
                            _capabilities = (string)q.GetCommandResult(skyCap).Result;
                            break;
                        case MountTaskName.Encoders:
                            _ = new SkySetEncoder(0, q, Axis.Axis1, Settings.Encoders);
                            _ = new SkySetEncoder(0, q, Axis.Axis2, Settings.Encoders);
                            break;
                        case MountTaskName.FullCurrent:
                            _ = new SkySetFullCurrent(0, q, Axis.Axis1, Settings.FullCurrent);
                            _ = new SkySetFullCurrent(0, q, Axis.Axis2, Settings.FullCurrent);
                            break;
                        case MountTaskName.GetFactorStep:
                            var skyFactor = new SkyGetFactorStepToRad(q.NewId, q);
                            _factorStep = (double[])q.GetCommandResult(skyFactor).Result;
                            break;
                        case MountTaskName.LoadDefaults:
                            _ = new SkyLoadDefaultMountSettings(0, q);
                            break;
                        case MountTaskName.InstantStopAxes:
                            _ = new SkyAxisStopInstant(0, q, Axis.Axis1);
                            _ = new SkyAxisStopInstant(0, q, Axis.Axis2);
                            break;
                        case MountTaskName.MinPulseRa:
                            _ = new SkySetMinPulseDuration(0, q, Axis.Axis1, Settings.MinPulseRa);
                            break;
                        case MountTaskName.MinPulseDec:
                            _ = new SkySetMinPulseDuration(0, q, Axis.Axis2, Settings.MinPulseDec);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new SkySetMonitorPulse(0, q, _monitorPulse);
                            break;
                        case MountTaskName.PecTraining:
                            _ = new SkySetPPecTrain(0, q, Axis.Axis1, _pPecTraining);
                            break;
                        case MountTaskName.Pec:
                            var ppeOcn = new SkySetPPec(q.NewId, q, Axis.Axis1, Settings.PPecOn);
                            var pPecOnStr = (string)q.GetCommandResult(ppeOcn).Result;
                            if (string.IsNullOrEmpty(pPecOnStr))
                            {
                                Settings.PPecOn = false;
                                break;
                            }
                            if (pPecOnStr.Contains("!")) { Settings.PPecOn = false; }
                            break;
                        case MountTaskName.PolarLedLevel:
                            if (Settings.PolarLedLevel < 0 || Settings.PolarLedLevel > 255) { return; }
                            _ = new SkySetPolarLedLevel(0, q, Axis.Axis1, Settings.PolarLedLevel);
                            break;
                        case MountTaskName.StopAxes:
                            _ = new SkyAxisStop(0, q, Axis.Axis1);
                            _ = new SkyAxisStop(0, q, Axis.Axis2);
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            _ = new SkySetSt4GuideRate(0, q, Settings.St4GuideRate);
                            break;
                        case MountTaskName.SetSouthernHemisphere:
                            _ = new SkySetSouthernHemisphere(q.NewId, q, Settings.Latitude < 0);
                            break;
                        case MountTaskName.SetSnapPort1:
                            var sp1 = new SkySetSnapPort(q.NewId, q, 1, _snapPort1);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp1).Result), out bool port1Result);
                            _snapPort1Result = port1Result;
                            break;
                        case MountTaskName.SetSnapPort2:
                            var sp2 = new SkySetSnapPort(q.NewId, q, 2, _snapPort2);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp2).Result), out bool port2Result);
                            _snapPort2Result = port2Result;
                            break;
                        case MountTaskName.SyncAxes:
                            var appAxesSync = AppAxes;
                            var sync = Axes.AxesAppToMount([appAxesSync.X, appAxesSync.Y], settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, sync[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, sync[1]);
                            monitorItem.Message += $",{appAxesSync.X}|{appAxesSync.Y}|{sync[0]}|{sync[1]}";
                            LogMount(monitorItem);
                            break;
                        case MountTaskName.SyncTarget:
                            var at = Transforms.CoordTypeToInternal(TargetRa, TargetDec, settings: settings);
                            var targetR = Axes.RaDecToAxesXy([at.X, at.Y], settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, targetR[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, targetR[1]);
                            monitorItem.Message += $",{Utilities.HoursToHMS(at.X, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(at.Y, " ", ":", "", 2)}|{targetR[0]}|{targetR[1]}";
                            LogMount(monitorItem);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSyncPos = _altAzSync;
                            var targetAz = new[] { altAzSyncPos.Y, altAzSyncPos.X };
                            targetAz = Axes.AzAltToAxesXy(targetAz, settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, targetAz[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, targetAz[1]);
                            monitorItem.Message += $",{altAzSyncPos.Y}|{altAzSyncPos.X}|{targetAz[0]}|{targetAz[1]}";
                            LogMount(monitorItem);
                            break;
                        case MountTaskName.GetAxisVersions:
                            var skyAxisVersions = new SkyGetAxisStringVersions(q.NewId, q);
                            _ = (long[])q.GetCommandResult(skyAxisVersions).Result;
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            var skyAxisStrVersions = new SkyGetAxisStringVersions(q.NewId, q);
                            _ = (string)q.GetCommandResult(skyAxisStrVersions).Result;
                            break;
                        case MountTaskName.MountName:
                            var skyMountType = new SkyMountType(q.NewId, q);
                            _mountName = (string)q.GetCommandResult(skyMountType).Result;
                            break;
                        case MountTaskName.MountVersion:
                            var skyMountVersion1 = new SkyMountVersion(q.NewId, q, Axis.Axis1);
                            _mountVersion[0] = (string)q.GetCommandResult(skyMountVersion1).Result;
                            var skyMountVersion2 = new SkyMountVersion(q.NewId, q, Axis.Axis2);
                            _mountVersion[1] = (string)q.GetCommandResult(skyMountVersion2  ).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var skyMountRevolutions = new SkyGetStepsPerRevolution(q.NewId, q);
                            _stepsPerRevolution = (long[])q.GetCommandResult(skyMountRevolutions).Result;
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var skyWormRevolutions1 = new SkyGetPecPeriod(q.NewId, q, Axis.Axis1);
                            _stepsWormPerRevolution[0] = (double)q.GetCommandResult(skyWormRevolutions1).Result;
                            var skyWormRevolutions2 = new SkyGetPecPeriod(q.NewId, q, Axis.Axis2);
                            _stepsWormPerRevolution[1] = (double)q.GetCommandResult(skyWormRevolutions2).Result;
                            break;
                        case MountTaskName.StepTimeFreq:
                            var skyStepTimeFreq = new SkyGetStepTimeFreq(q.NewId, q);
                            _stepsTimeFreq = (long[])q.GetCommandResult(skyStepTimeFreq).Result;
                            break;
                        case MountTaskName.SetHomePositions:
                            var homeAxesSky = HomeAxes;
                            _ = new SkySetAxisPosition(0, q, Axis.Axis1, homeAxesSky.X);
                            _ = new SkySetAxisPosition(0, q, Axis.Axis2, homeAxesSky.Y);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        #region Axes Stop Validate

        /// <summary>
        /// Waits up to 5 s for a single axis to reach a full stop.
        /// Used by MoveAxis(rate=0) to block until the specific axis has physically stopped,
        /// without affecting the other axis which may still be moving.
        /// </summary>
        public bool AxisStopValidate(Axis axis)
        {
            if (!IsMountRunning) { return true; }
            var stopwatch = Stopwatch.StartNew();
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var mq = SimQueue!;
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        Thread.Sleep(100);
                        try
                        {
                            var status = new CmdAxisStatus(mq.NewId, mq, axis);
                            var result = mq.GetCommandResult(status);
                            if (!result.Successful) return false;
                            if (((Alpaca.Mount.Simulator.AxisStatus)result.Result).Stopped) return true;
                        }
                        catch (InvalidOperationException) { return false; }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return false; }
                    }
                    return false;
                case MountType.SkyWatcher:
                    var sq = SkyQueue!;
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        Thread.Sleep(100);
                        try
                        {
                            var statusCmd = new SkyIsAxisFullStop(sq.NewId, sq, axis);
                            var result = sq.GetCommandResult(statusCmd);
                            if (!result.Successful) return false;
                            if (Convert.ToBoolean(result.Result)) return true;
                        }
                        catch (InvalidOperationException) { return false; }
                    }
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Stops axes and waits up to 5 s for them to reach a full stop.
        /// </summary>
        internal bool AxesStopValidate()
        {
            if (!IsMountRunning) { return true; }
            Stopwatch stopwatch;
            bool axis2Stopped = false;
            bool axis1Stopped = false;
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    var mq = SimQueue!;
                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SimTasks(MountTaskName.StopAxes);
                        Thread.Sleep(100);
                        try
                        {
                            var statusX = new CmdAxisStatus(mq.NewId, mq, Axis.Axis1);
                            var resultX = mq.GetCommandResult(statusX);
                            if (!resultX.Successful) return false;
                            axis1Stopped = ((Alpaca.Mount.Simulator.AxisStatus)resultX.Result).Stopped;

                            var statusY = new CmdAxisStatus(mq.NewId, mq, Axis.Axis2);
                            var resultY = mq.GetCommandResult(statusY);
                            if (!resultY.Successful) return false;
                            axis2Stopped = ((Alpaca.Mount.Simulator.AxisStatus)resultY.Result).Stopped;
                        }
                        catch (InvalidOperationException) { return false; }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return false; }

                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                case MountType.SkyWatcher:
                    var sq = SkyQueue!;
                    // If the mount never responded (e.g. failed connect), IsMountConnected is false.
                    // There are no running axes to stop — return immediately so GetCommandResult
                    // is never called and the 40 s CompletionTimeoutMs is never incurred.
                    if (!sq.IsMountConnected) return false;
                    stopwatch = Stopwatch.StartNew();
                    SkyTasks(MountTaskName.StopAxes);
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        Thread.Sleep(100);
                        if (!axis1Stopped)
                        {
                            try
                            {
                                var statusX = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis1);
                                var resultX = sq.GetCommandResult(statusX);
                                if (!resultX.Successful) return false;
                                axis1Stopped = Convert.ToBoolean(resultX.Result);
                            }
                            catch (InvalidOperationException) { return false; }
                        }
                        if (!axis2Stopped)
                        {
                            try
                            {
                                var statusY = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis2);
                                var resultY = sq.GetCommandResult(statusY);
                                if (!resultY.Successful) return false;
                                axis2Stopped = Convert.ToBoolean(resultY.Result);
                            }
                            catch (InvalidOperationException) { return false; }
                        }
                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion
    }
}
