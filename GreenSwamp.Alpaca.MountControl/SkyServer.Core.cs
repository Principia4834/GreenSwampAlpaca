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
// SkyServer.Core.cs - Core/Common Functionality
// ============================================================================
// This partial class file contains:
// - Static constructor and initialization
// - Core fields (private/internal state)
// - Core mount operations (connect, start, stop)
// - Position update methods (GetRawDegrees, GetRawSteps, UpdateSteps, etc.)
// - Coordinate transformations (GetSyncedAxes, GetUnsyncedAxes)
// - Error handling (SkyErrorHandler, CheckSkyErrors)
// - Internal utility methods
// - Event handlers (PropertyChanged, UpdateServerEvent, etc.)
// - Threading & timing (MediaTimer events)
// - Alignment model integration
// Dependencies: Used by both TelescopeAPI and UI partial classes
// ============================================================================

using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System;
using System.Diagnostics;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;
using SkyWatcherErrorCode = GreenSwamp.Alpaca.Mount.SkyWatcher.ErrorCode;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Core functionality for SkyServer - handles mount communication, position tracking, and internal state
    /// </summary>
    public static partial class SkyServer
    {
        #region Core Fields

        private const double SiderealRate = 15.0410671786691;

        #endregion

        #region Static Constructor

        static SkyServer()
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = "Loading SkyServer"
                };
                MonitorLog.LogToMonitor(monitorItem);

                // ToDo: Remove if not needed
                // initialise the alignment model
                //AlignmentSettings.Load();
                //AlignmentModel = new AlignmentModel(
                //    _settings!.Latitude,
                //    _settings!.Longitude,
                //    _settings!.Elevation)
                //{
                //    IsAlignmentOn = AlignmentSettings.IsAlignmentOn,
                //    ThreePointAlgorithm = ThreePointAlgorithmEnum.BestCentre
                //};
                //AlignmentModel.Notification += AlignmentModel_Notification;

                }
            catch (Exception ex)
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
                MonitorLog.LogToMonitor(monitorItem);

                // ToDo: improve exception handling
                // AlertState = true;
                throw;
            }
        }

        #endregion

        #region Error Handling
        // Contains: SkyErrorHandler, CheckSkyErrors

        /// <summary>
        /// Handles MountControlException and SkyServerException
        /// </summary>
        /// <param name="ex"></param>
        public static void SkyErrorHandler(Exception ex, Mount? mount = null)
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
            MonitorLog.LogToMonitor(monitorItem);

            // ToDo: improve exception handling
            // AlertState = true;
            var extype = ex.GetType().ToString().Trim();
            var effectiveMount = mount;
            switch (extype)
            {
                case "GS.SkyWatcher.MountControlException":
                    var mounterr = (MountControlException)ex;
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
                            effectiveMount?.MountStop();
                            if (effectiveMount != null) effectiveMount._mountError = mounterr;
                            break;
                        default:
                            effectiveMount?.MountStop();
                            if (effectiveMount != null) effectiveMount._mountError = mounterr;
                            break;
                    }

                    break;
                case "GS.Server.SkyTelescope.SkyServerException":
                    var skyerr = (SkyServerException)ex;
                    switch (skyerr.ErrorCode)
                    {
                        case ErrorCode.ErrMount:
                        case ErrorCode.ErrExecutingCommand:
                        case ErrorCode.ErrUnableToDeqeue:
                        case ErrorCode.ErrSerialFailed:
                            effectiveMount?.MountStop();
                            if (effectiveMount != null) effectiveMount._mountError = skyerr;
                            break;
                        default:
                            effectiveMount?.MountStop();
                            if (effectiveMount != null) effectiveMount._mountError = skyerr;
                            break;
                    }

                    break;
                default:
                    if (effectiveMount != null) effectiveMount._mountError = ex;
                    effectiveMount?.MountStop();
                    break;
            }
        }

        /// <summary>
        /// Checks command object for errors and unsuccessful execution
        /// </summary>
        /// <param name="command"></param>
        /// <returns>true for errors found and not successful</returns>
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

        #region Internal Utility Methods

        /// <summary>
        /// Convert degrees to radians
        /// </summary>
        /// <param name="degree">Angle in degrees</param>
        /// <returns>Angle in radians</returns>
        internal static double DegToRad(double degree)
        {
            return degree * (Math.PI / 180.0);
        }

        /// <summary>
        /// Convert radians to degrees
        /// </summary>
        /// <param name="rad">Angle in radians</param>
        /// <returns>Angle in degrees</returns>
        internal static double RadToDeg(double rad)
        {
            return rad * (180.0 / Math.PI);
        }

        #endregion

        #region Event Handlers
        // Contains: GetLocalSiderealTime (longitude overload)
        // M4: PropertyChangedSkySettings, PropertyChangedAlignmentSettings, UpdateServerEvent,
        //     LowVoltageEventSet moved to Mount (OnPropertyChangedSkySettings, OnLowVoltageEvent)

        /// <summary>
        /// Get local sidereal time for the current UTC time using an explicit longitude.
        /// Use this overload from per-instance code to avoid reading device-00's longitude.
        /// </summary>
        internal static double GetLocalSiderealTime(double longitude)
        {
            var gsjd = JDate.Ole2Jd(HiResDateTime.UtcNow);
            return Time.Lst(JDate.Epoch2000Days(), gsjd, false, longitude);
        }

        #endregion

        #region Alignment

        /// <summary>
        /// Gets the alignment model corrected target (physical) axis positions for a given calculated axis position.
        /// </summary>
        /// <param name="unsynced">Calculated axis position</param>
        /// <returns>Physical axis position corrected by alignment model</returns>
        public static double[] GetSyncedAxes(double[] unsynced)
        {
                return unsynced;
        }

        #endregion

        #region Mount-Specific Core Operations
        // Contains mount-specific implementations for Simulator and SkyWatcher

        #region Simulator Items
        // Contains: SimTasks 

        /// <summary>
        /// Instance-aware SimTasks: routes commands and capability writes to the given Mount.
        /// </summary>
        public static void SimTasks(MountTaskName taskName, Mount mount)
        {
            if (!mount.IsMountRunning) return;

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
            MonitorLog.LogToMonitor(monitorItem);

            var settings = mount.Settings;
            var q = mount.SimQueue!;

            switch (mount.Settings.Mount)
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
                            mount._canAdvancedCmdSupport = false;
                            break;
                        case MountTaskName.CanPpec:
                            mount._canPPec = false;
                            break;
                        case MountTaskName.CanPolarLed:
                            mount._canPolarLed = false;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeCmd = new GetHomeSensorCapability(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(canHomeCmd).Result), out bool hasHome);
                            mount._canHomeSensor = hasHome;
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
                            var appAxes = mount.AppAxes;
                            var sync = Axes.AxesAppToMount([appAxes.X, appAxes.Y], settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, sync[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, sync[1]);
                            break;
                        case MountTaskName.SyncTarget:
                            var a = Transforms.CoordTypeToInternal(mount.TargetRa, mount.TargetDec);
                            var targetR = Axes.RaDecToAxesXy([a.X, a.Y], settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetR[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetR[1]);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSync = mount._altAzSync;
                            var targetA = new[] { altAzSync.Y, altAzSync.X };
                            targetA = Axes.AzAltToAxesXy(targetA, settings);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetA[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetA[1]);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new CmdSetMonitorPulse(0, q, mount._monitorPulse);
                            break;
                        case MountTaskName.Pec:
                            break;
                        case MountTaskName.PecTraining:
                            break;
                        case MountTaskName.Capabilities:
                            mount._capabilities = @"N/A";
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            break;
                        case MountTaskName.SetSnapPort1:
                            _ = new CmdSnapPort(0, q, 1, mount._snapPort1);
                            mount._snapPort1Result = false;
                            break;
                        case MountTaskName.SetSnapPort2:
                            _ = new CmdSnapPort(0, q, 2, mount._snapPort2);
                            mount._snapPort2Result = true;
                            break;
                        case MountTaskName.MountName:
                            var mountNameCmd = new CmdMountName(q.NewId, q);
                            mount._mountName = (string)q.GetCommandResult(mountNameCmd).Result;
                            break;
                        case MountTaskName.GetAxisVersions:
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            break;
                        case MountTaskName.MountVersion:
                            var mountVersionCmd = new CmdMountVersion(q.NewId, q);
                            mount._mountVersion = (string)q.GetCommandResult(mountVersionCmd).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var spr = new CmdSpr(q.NewId, q);
                            var sprnum = (long)q.GetCommandResult(spr).Result;
                            mount._stepsPerRevolution = [sprnum, sprnum];
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var spw = new CmdSpw(q.NewId, q);
                            var spwnum = (double)q.GetCommandResult(spw).Result;
                            mount._stepsWormPerRevolution = [spwnum, spwnum];
                            break;
                        case MountTaskName.SetHomePositions:
                            var homeAxesSim = mount.HomeAxes;
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, homeAxesSim.X);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, homeAxesSim.Y);
                            break;
                        case MountTaskName.GetFactorStep:
                            var factorStepCmd = new CmdFactorSteps(q.NewId, q);
                            mount._factorStep[0] = (double)q.GetCommandResult(factorStepCmd).Result;
                            mount._factorStep[1] = mount._factorStep[0];
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

        #region SkyWatcher Items
        // Contains: SkyTasks

        /// <summary>
        /// Instance-aware SkyTasks: routes commands and capability writes to the given Mount.
        /// </summary>
        public static void SkyTasks(MountTaskName taskName, Mount instance)
        {
            if (!instance.IsMountRunning) { return; }

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

            var settings = instance.Settings;
            var q = instance.SkyQueue!;

            switch (instance.Settings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    switch (taskName)
                    {
                        case MountTaskName.AllowAdvancedCommandSet:
                            _ = new SkyAllowAdvancedCommandSet(0, q, instance.Settings.AllowAdvancedCommandSet);
                            break;
                        case MountTaskName.AlternatingPpec:
                            _ = new SkySetAlternatingPPec(0, q, instance.Settings.AlternatingPPec);
                            break;
                        case MountTaskName.DecPulseToGoTo:
                            _ = new SkySetDecPulseToGoTo(0, q, instance.Settings.DecPulseToGoTo);
                            break;
                        case MountTaskName.CanAdvancedCmdSupport:
                            var skyCanAdvanced = new SkyGetAdvancedCmdSupport(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyCanAdvanced).Result), out bool pAdvancedResult);
                            instance._canAdvancedCmdSupport = pAdvancedResult;
                            break;
                        case MountTaskName.CanPpec:
                            var skyMountCanPpec = new SkyCanPPec(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyMountCanPpec).Result), out bool pPecResult);
                            instance._canPPec = pPecResult;
                            break;
                        case MountTaskName.CanPolarLed:
                            var skyCanPolarLed = new SkyCanPolarLed(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(skyCanPolarLed).Result), out bool polarLedResult);
                            instance._canPolarLed = polarLedResult;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeSky = new SkyCanHomeSensors(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(canHomeSky).Result), out bool homeSensorResult);
                            instance._canHomeSensor = homeSensorResult;
                            break;
                        case MountTaskName.Capabilities:
                            var skyCap = new SkyGetCapabilities(q.NewId, q);
                            instance._capabilities = (string)q.GetCommandResult(skyCap).Result;
                            break;
                        case MountTaskName.Encoders:
                            _ = new SkySetEncoder(0, q, Axis.Axis1, instance.Settings.Encoders);
                            _ = new SkySetEncoder(0, q, Axis.Axis2, instance.Settings.Encoders);
                            break;
                        case MountTaskName.FullCurrent:
                            _ = new SkySetFullCurrent(0, q, Axis.Axis1, instance.Settings.FullCurrent);
                            _ = new SkySetFullCurrent(0, q, Axis.Axis2, instance.Settings.FullCurrent);
                            break;
                        case MountTaskName.GetFactorStep:
                            var skyFactor = new SkyGetFactorStepToRad(q.NewId, q);
                            instance._factorStep = (double[])q.GetCommandResult(skyFactor).Result;
                            break;
                        case MountTaskName.LoadDefaults:
                            _ = new SkyLoadDefaultMountSettings(0, q);
                            break;
                        case MountTaskName.InstantStopAxes:
                            _ = new SkyAxisStopInstant(0, q, Axis.Axis1);
                            _ = new SkyAxisStopInstant(0, q, Axis.Axis2);
                            break;
                        case MountTaskName.MinPulseRa:
                            _ = new SkySetMinPulseDuration(0, q, Axis.Axis1, instance.Settings.MinPulseRa);
                            break;
                        case MountTaskName.MinPulseDec:
                            _ = new SkySetMinPulseDuration(0, q, Axis.Axis2, instance.Settings.MinPulseDec);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new SkySetMonitorPulse(0, q, instance._monitorPulse);
                            break;
                        case MountTaskName.PecTraining:
                            _ = new SkySetPPecTrain(0, q, Axis.Axis1, instance._pPecTraining);
                            break;
                        case MountTaskName.Pec:
                            var ppeOcn = new SkySetPPec(q.NewId, q, Axis.Axis1, instance.Settings.PPecOn);
                            var pPecOnStr = (string)q.GetCommandResult(ppeOcn).Result;
                            if (string.IsNullOrEmpty(pPecOnStr))
                            {
                                instance.Settings.PPecOn = false;
                                break;
                            }
                            if (pPecOnStr.Contains("!")) { instance.Settings.PPecOn = false; }
                            break;
                        case MountTaskName.PolarLedLevel:
                            if (instance.Settings.PolarLedLevel < 0 || instance.Settings.PolarLedLevel > 255) { return; }
                            _ = new SkySetPolarLedLevel(0, q, Axis.Axis1, instance.Settings.PolarLedLevel);
                            break;
                        case MountTaskName.StopAxes:
                            _ = new SkyAxisStop(0, q, Axis.Axis1);
                            _ = new SkyAxisStop(0, q, Axis.Axis2);
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            _ = new SkySetSt4GuideRate(0, q, instance.Settings.St4GuideRate);
                            break;
                        case MountTaskName.SetSouthernHemisphere:
                            _ = new SkySetSouthernHemisphere(q.NewId, q, instance.Settings.Latitude < 0);
                            break;
                        case MountTaskName.SetSnapPort1:
                            var sp1 = new SkySetSnapPort(q.NewId, q, 1, instance._snapPort1);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp1).Result), out bool port1Result);
                            instance._snapPort1Result = port1Result;
                            break;
                        case MountTaskName.SetSnapPort2:
                            var sp2 = new SkySetSnapPort(q.NewId, q, 2, instance._snapPort2);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp2).Result), out bool port2Result);
                            instance._snapPort2Result = port2Result;
                            break;
                        case MountTaskName.SyncAxes:
                            var appAxesSync = instance.AppAxes;
                            var sync = Axes.AxesAppToMount([appAxesSync.X, appAxesSync.Y], settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, sync[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, sync[1]);
                            monitorItem.Message += $",{appAxesSync.X}|{appAxesSync.Y}|{sync[0]}|{sync[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncTarget:
                            var a = Transforms.CoordTypeToInternal(instance?.TargetRa ?? double.NaN, instance?.TargetDec ?? double.NaN);
                            var targetR = Axes.RaDecToAxesXy([a.X, a.Y], settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, targetR[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, targetR[1]);
                            monitorItem.Message += $",{Utilities.HoursToHMS(a.X, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(a.Y, " ", ":", "", 2)}|{targetR[0]}|{targetR[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSyncPos = instance._altAzSync;
                            var targetA = new[] { altAzSyncPos.Y, altAzSyncPos.X };
                            targetA = Axes.AzAltToAxesXy(targetA, settings);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, targetA[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, targetA[1]);
                            monitorItem.Message += $",{altAzSyncPos.Y}|{altAzSyncPos.X}|{targetA[0]}|{targetA[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
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
                            instance._mountName = (string)q.GetCommandResult(skyMountType).Result;
                            break;
                        case MountTaskName.MountVersion:
                            var skyMountVersion = new SkyMountVersion(q.NewId, q);
                            instance._mountVersion = (string)q.GetCommandResult(skyMountVersion).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var skyMountRevolutions = new SkyGetStepsPerRevolution(q.NewId, q);
                            instance._stepsPerRevolution = (long[])q.GetCommandResult(skyMountRevolutions).Result;
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var skyWormRevolutions1 = new SkyGetPecPeriod(q.NewId, q, Axis.Axis1);
                            instance._stepsWormPerRevolution[0] = (double)q.GetCommandResult(skyWormRevolutions1).Result;
                            var skyWormRevolutions2 = new SkyGetPecPeriod(q.NewId, q, Axis.Axis2);
                            instance._stepsWormPerRevolution[1] = (double)q.GetCommandResult(skyWormRevolutions2).Result;
                            break;
                        case MountTaskName.StepTimeFreq:
                            var skyStepTimeFreq = new SkyGetStepTimeFreq(q.NewId, q);
                            instance._stepsTimeFreq = (long[])q.GetCommandResult(skyStepTimeFreq).Result;
                            break;
                        case MountTaskName.SetHomePositions:
                            var homeAxesSky = instance.HomeAxes;
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

        #endregion

        #region Slewing & Movement Core

        /// <summary>
        /// Routes stop commands and status queries to the given Mount's queues.
        /// </summary>
        internal static bool AxesStopValidate(Mount mount)
        {
            if (!mount.IsMountRunning) { return true; }
            Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V1: AxesStopValidate ENTRY | mount:{mount.Id}");
            Stopwatch stopwatch;
            bool axis2Stopped = false;
            bool axis1Stopped = false;
            switch (mount.Settings.Mount)
            {
                case MountType.Simulator:
                    var mq = mount.SimQueue!;
                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SimTasks(MountTaskName.StopAxes, mount);
                        Thread.Sleep(100);
                        var statusX = new CmdAxisStatus(mq.NewId, mq, Axis.Axis1);
                        var axis1Status = (Alpaca.Mount.Simulator.AxisStatus)mq.GetCommandResult(statusX).Result;
                        axis1Stopped = axis1Status.Stopped;

                        var statusY = new CmdAxisStatus(mq.NewId, mq, Axis.Axis2);
                        var axis2Status = (Alpaca.Mount.Simulator.AxisStatus)mq.GetCommandResult(statusY).Result;
                        axis2Stopped = axis2Status.Stopped;

                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                case MountType.SkyWatcher:
                    var sq = mount.SkyQueue!;
                    stopwatch = Stopwatch.StartNew();
                    SkyTasks(MountTaskName.StopAxes, mount);
                    Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V2: StopAxes command queued | elapsed:{stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                    var _iter = 0;
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V3: iter:{++_iter} loop start | elapsed:{stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                        Thread.Sleep(100);
                        if (!axis1Stopped)
                        {
                            var statusX = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis1);
                            axis1Stopped = Convert.ToBoolean(sq.GetCommandResult(statusX).Result);
                            Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V4: axis1 query done | stopped:{axis1Stopped} | elapsed:{stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                        }
                        if (!axis2Stopped)
                        {
                            var statusY = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis2);
                            axis2Stopped = Convert.ToBoolean(sq.GetCommandResult(statusY).Result);
                            Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V5: axis2 query done | stopped:{axis2Stopped} | elapsed:{stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                        }
                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V6: AxesStopValidate TRUE | iters:{_iter} | total:{stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                        return true;
                    }
                    Debug.WriteLine($"[ABORT|{HiResDateTime.UtcNow:HH:mm:ss.fff}|tid:{Environment.CurrentManagedThreadId}] V7: AxesStopValidate TIMEOUT after {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion
    }
}
