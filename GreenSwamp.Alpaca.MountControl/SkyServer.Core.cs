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
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        // Phase 6: AltAz timer delegates to default instance (per-device isolation)
        private static MediaTimer? _altAzTrackingTimer
        {
            get => _defaultInstance?._altAzTrackingTimer;
            set { if (_defaultInstance != null) _defaultInstance._altAzTrackingTimer = value; }
        }
        // _altAzTrackingLock removed from static — use _defaultInstance._altAzTrackingLock directly (ref semantics required by Interlocked)
        // Default mount instance — computed from registry slot 0 (Step 9: Bridge B0 removed)
        private static MountInstance? _defaultInstance => MountInstanceRegistry.GetInstance(0);
        // Option C: _settings is now a computed property — always reads from the registered slot 0 instance
        private static SkySettingsInstance? _settings => _defaultInstance?.Settings;
        internal static SkySettingsInstance? Settings => _settings;

        // Phase 4.2: Slew speed properties (delegate to default instance)
        public static double SlewSpeedOne => _defaultInstance?.SlewSpeedOne ?? 0.0;
        public static double SlewSpeedTwo => _defaultInstance?.SlewSpeedTwo ?? 0.0;
        public static double SlewSpeedThree => _defaultInstance?.SlewSpeedThree ?? 0.0;
        public static double SlewSpeedFour => _defaultInstance?.SlewSpeedFour ?? 0.0;
        public static double SlewSpeedFive => _defaultInstance?.SlewSpeedFive ?? 0.0;
        public static double SlewSpeedSix => _defaultInstance?.SlewSpeedSix ?? 0.0;
        public static double SlewSpeedSeven => _defaultInstance?.SlewSpeedSeven ?? 0.0;
        public static double SlewSpeedEight => _defaultInstance?.SlewSpeedEight ?? 4.0;

        // Phase 4.3: SkyWatcher tracking rates (internal - delegates to instance)
        internal static Vector SkyTrackingRate
        {
            get => _defaultInstance?._skyTrackingRate ?? new Vector(0, 0);
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance._skyTrackingRate = value;
            }
        }

        internal static Vector SkyHcRate
        {
            get => _defaultInstance?._skyHcRate ?? new Vector(0, 0);
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance._skyHcRate = value;
            }
        }

        // HC Anti-Backlash
        private static HcPrevMove? _hcPrevMoveRa
        {
            get => _defaultInstance?._hcPrevMoveRa;
            set { if (_defaultInstance != null) _defaultInstance._hcPrevMoveRa = value; }
        }

        private static HcPrevMove? _hcPrevMoveDec
        {
            get => _defaultInstance?._hcPrevMoveDec;
            set { if (_defaultInstance != null) _defaultInstance._hcPrevMoveDec = value; }
        }

        private static IList<double> HcPrevMovesDec
            => _defaultInstance?._hcPrevMovesDec ?? new List<double>();

        // Phase 4.1: Converted to delegating properties (fields removed, see properties below)

        // ToDo: Remove if not needed
        // public static readonly List<SpiralPoint> SpiralCollection;

        // AlignmentModel
        // ToDo: Remove if not needed
        // public static readonly AlignmentModel AlignmentModel;

        // Phase 5.3: CancellationTokenSources — delegate to default instance to prevent cross-device cancellation
        private static CancellationTokenSource? _ctsGoTo
        {
            get => _defaultInstance?._ctsGoTo;
            set { if (_defaultInstance != null) _defaultInstance._ctsGoTo = value; }
        }
        private static CancellationTokenSource? _ctsPulseGuideRa
        {
            get => _defaultInstance?._ctsPulseGuideRa;
            set { if (_defaultInstance != null) _defaultInstance._ctsPulseGuideRa = value; }
        }
        private static CancellationTokenSource? _ctsPulseGuideDec
        {
            get => _defaultInstance?._ctsPulseGuideDec;
            set { if (_defaultInstance != null) _defaultInstance._ctsPulseGuideDec = value; }
        }
        private static CancellationTokenSource? _ctsHcPulseGuide
        {
            get => _defaultInstance?._ctsHcPulseGuide;
            set { if (_defaultInstance != null) _defaultInstance._ctsHcPulseGuide = value; }
        }

        // Phase 4.1: Delegating properties for instance state
        /// <summary>
        /// Home axes position (mount coordinates)
        /// Delegates to default instance
        /// </summary>
        private static Vector HomeAxes
        {
            get => _defaultInstance?.HomeAxes ?? new Vector(0, 0);
        }

        /// <summary>
        /// Application axes position (derived from mount axes by AxesMountToApp)
        /// Delegates to default instance
        /// </summary>
        private static Vector AppAxes
        {
            get => _defaultInstance?.AppAxes ?? new Vector(0, 0);
        }

        /// <summary>
        /// Alt/Az sync position for Alt/Az mode syncing
        /// Delegates to default instance
        /// </summary>
        private static Vector AltAzSync
        {
            get => _defaultInstance?.AltAzSync ?? new Vector(0, 0);
            set
            {
                if (_defaultInstance != null)
                    _defaultInstance.AltAzSync = value;
            }
        }
        
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
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Loading SkyServer"
                };
                MonitorLog.LogToMonitor(monitorItem);

                // load some things
                Defaults();
                
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
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{ex.Message}|{ex.StackTrace}"
                };
                MonitorLog.LogToMonitor(monitorItem);

                // ToDo: improve exception handling
                // AlertState = true;
                throw;
            }
        }

        #endregion

        #region Core Mount Operations
        // Contains: MountStart, MountStop, MountReset, GetHomeAxes, ShutdownServer

        /// <summary>
        /// Start connection, queues, and events
        /// Delegated to instance
        /// </summary>
        private static void MountStart()
        {
            // If _defaultInstance is null, this is a no-op (safe)
            _defaultInstance?.MountStart();
        }

        /// <summary>
        /// Stop queues and events
        /// Delegated to instance
        /// </summary>
        private static void MountStop()
        {
            // If _defaultInstance is null, this is a no-op (safe)
            _defaultInstance?.MountStop();
        }

        /// <summary>
        /// Get home axes adjusted for angle offset
        /// Delegated to instance
        /// </summary>
        public static Vector GetHomeAxes(double xAxis, double yAxis)
        {
            return _defaultInstance?.GetHomeAxes(xAxis, yAxis) ?? new Vector(0, 0);
        }

        /// <summary>
        /// Shuts down everything and exists
        /// </summary>
        public static void ShutdownServer()
        {
            IsMountRunning = false;

            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "MainWindow Closing" };
            MonitorLog.LogToMonitor(monitorItem);

            // ToDo - fix reference counting on shutdown
            //for (var intCounter = MediaTypeNames.Application.Current.Windows.Count - 1; intCounter >= 0; intCounter--)
            //{
            //    MediaTypeNames.Application.Current.Windows[intCounter]?.Close();
            //}
        }

        /// <summary>
        /// Initialize SkyServer. Must be called after MountInstanceRegistry slot 0 has been registered.
        /// Settings are read directly from the registered slot 0 instance via the computed _settings property.
        /// </summary>
        public static void Initialize()
        {
            // Wire settings change notifications now that slot 0 is registered
            if (_settings != null)
                _settings.PropertyChanged += PropertyChangedSkySettings;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"SkyServer initialized | Mount:{_settings?.Mount} | Port:{_settings?.Port}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion

        #region Position Update Methods
        // Contains: GetRawDegrees, ConvertStepsToDegrees, GetRawSteps (3 overloads), UpdateSteps, GetRawStepsDt,
        //           MapSlewTargetToAxes, GetDefaultPositions

        /// <summary>
        /// Gets current converted positions from the mount in degrees
        /// Renamed to _Internal, delegated to instance
        /// </summary>
        internal static double[]? GetRawDegrees()
        {
            // Delegate to default instance
            return _defaultInstance?.GetRawDegrees() ?? new[] { double.NaN, double.NaN };
        }
        
        /// <summary>
        /// Convert steps to degrees
        /// Renamed to _Internal, delegated to instance
        /// </summary>
        internal static double ConvertStepsToDegrees(double steps, int axis)
        {
            // Delegate to default instance
            return _defaultInstance?.ConvertStepsToDegrees(steps, axis) ?? double.NaN;
        }

        /// <summary>
        /// Get steps from the mount
        /// Renamed to _Internal, delegated to instance
        /// </summary>
        internal static double[]? GetRawSteps()
        {
            // Delegate to default instance
            return _defaultInstance?.GetRawSteps() ?? new[] { double.NaN, double.NaN };
        }

        /// <summary>
        /// Main get for the Steps
        /// Delegated to instance
        /// </summary>
        internal static void UpdateSteps()
        {
            _defaultInstance?.UpdateSteps();
        }

        /// <summary>
        /// Maps a slew target to the corresponding axes based on the specified slew type.
        /// Delegated to instance
        /// </summary>
        public static double[] MapSlewTargetToAxes(double[] target, SlewType slewType)
        {
            return _defaultInstance?.MapSlewTargetToAxes(target, slewType) ?? new[] { double.NaN, double.NaN };
        }

        /// <summary>
        /// Used when the mount is first turned on and the instance is created
        /// </summary>
        internal static double[] GetDefaultPositions_Internal()
        {
            // set default home position or get home override from the settings 
            double[] positions = { 0, 0 };
            string name = String.Empty;
            // home axes are mount values
            var homeAxes = GetHomeAxes(_settings!.HomeAxisX, _settings!.HomeAxisY);

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Home position,{name}|{homeAxes.X}|{homeAxes.Y}|{_settings!.HomeAxisX}|{_settings!.HomeAxisY}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            if (AtPark)
            {
                if (_settings!.AutoTrack)
                {
                    AtPark = false;
                    Tracking = _settings!.AutoTrack;
                }
                // Create context from current settings
                var context = AxesContext.FromSettings(_settings);
                positions = Axes.AxesAppToMount(_settings!.ParkAxes, context);
                ParkSelected = GetStoredParkPosition();

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Parked,{_settings!.ParkName}|{_settings!.ParkAxes[0]}|{_settings!.ParkAxes[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);

            }
            else
            {
                positions = new[] { homeAxes.X, homeAxes.Y };
            }

            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Load:{positions[0]}|{positions[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            return positions;
        }

        #endregion

        #region Coordinate Transformations
        // Contains: GetSyncedAxes, GetUnsyncedAxes (currently in Alignment region - will be moved here)
        // Note: These methods are alignment-model-aware coordinate transformations
        // TODO: Move GetSyncedAxes and GetUnsyncedAxes from Alignment region to here for better organization

        #endregion

        #region Error Handling
        // Contains: SkyErrorHandler, CheckSkyErrors

        /// <summary>
        /// Handles MountControlException and SkyServerException
        /// </summary>
        /// <param name="ex"></param>
        public static void SkyErrorHandler(Exception ex)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Error,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{ex.Message}|{ex.StackTrace}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // ToDo: improve exception handling
            // AlertState = true;
            var extype = ex.GetType().ToString().Trim();
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
                            IsMountRunning = false;
                            MountError = mounterr;
                            break;
                        default:
                            IsMountRunning = false;
                            MountError = mounterr;
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
                            IsMountRunning = false;
                            MountError = skyerr;
                            break;
                        default:
                            IsMountRunning = false;
                            MountError = skyerr;
                            break;
                    }

                    break;
                default:
                    MountError = ex;
                    IsMountRunning = false;
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
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{command.Successful}|{command.Exception.Message}|{command.Exception.StackTrace}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return !command.Successful || command.Exception != null;
        }

        #endregion

        #region Internal Utility Methods
        // Contains: Defaults, OnStaticPropertyChanged, DegToRad, RadToDeg

        /// <summary>
        /// Load default settings and slew rates
        /// Delegated to instance
        /// </summary>
        private static void Defaults()
        {
            _defaultInstance?.Defaults();
            // Fallback not needed - this is internal initialization only
        }

        /// <summary>
        /// Property change notification for static properties
        /// </summary>
        /// <param name="propertyName"></param>
        private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

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
        // Contains: PropertyChangedSkySettings, PropertyChangedAlignmentSettings, PropertyChangedSkyQueue,
        //           PropertyChangedMountQueue, UpdateServerEvent, LowVoltageEventSet, GetLocalSiderealTime (2 overloads)

        /// <summary>
        /// Property changes from SkySettings
        /// </summary>
        private static void PropertyChangedSkySettings(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AtPark":
                    if (AtPark != _settings!.AtPark) AtPark = _settings!.AtPark;
                    break;
                case "Latitude":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteLatitude = _settings!.Latitude;
                    break;
                case "Longitude":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteLongitude = _settings!.Longitude;
                    break;
                case "Elevation":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteElevation = _settings!.Elevation;
                    break;
                case "AlignmentMode":
                    Tracking = false;
                    _defaultInstance.SkyPredictor.Reset();
                    break;
            }
        }

        // ToDo: Remove if not needed
        /// <summary>
        /// Property changes from AlignmentSettings
        /// </summary>
        private static void PropertyChangedAlignmentSettings(object sender, PropertyChangedEventArgs e)
        {
            //switch (e.PropertyName)
            //{
            //    case "IsAlignmentOn":
            //        AlignmentModel.IsAlignmentOn = AlignmentSettings.IsAlignmentOn;
            //        break;
            //    case "ProximityLimit":
            //        AlignmentModel.ProximityLimit = AlignmentSettings.ProximityLimit;
            //        break;
            //    case "AlignmentBehaviour":
            //        AlignmentModel.AlignmentBehaviour = AlignmentSettings.AlignmentBehaviour;
            //        break;
            //    case "ActivePoints":
            //        AlignmentModel.ActivePoints = AlignmentSettings.ActivePoints;
            //        break;
            //    case "ThreePointAlgorithm":
            //        AlignmentModel.ThreePointAlgorithm = AlignmentSettings.ThreePointAlgorithm;
            //        break;
            //    case "AlignmentWarningThreshold":
            //        AlignmentModel.AlignmentWarningThreshold = AlignmentSettings.AlignmentWarningThreshold;
            //        break;
            //}
        }

        /// <summary>
        /// Property changes from SkyQueue
        /// </summary>
        private static void PropertyChangedSkyQueue(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsPulseGuidingRa":
                    IsPulseGuidingRa = SkyQueue.IsPulseGuidingRa;
                    break;
                case "IsPulseGuidingDec":
                    IsPulseGuidingDec = SkyQueue.IsPulseGuidingDec;
                    break;
                case "Steps":
                    // Steps setter delegates to _defaultInstance?.ReceiveSteps() which signals
                    // _mountPositionUpdatedEvent on the owning instance — no direct signal needed.
                    Steps = SkyQueue.Steps;
                    break;
            }
        }

        /// <summary>
        /// Property changes from MountQueue
        /// </summary>
        private static void PropertyChangedMountQueue(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsPulseGuidingRa":
                    IsPulseGuidingRa = MountQueue.IsPulseGuidingRa;
                    break;
                case "IsPulseGuidingDec":
                    IsPulseGuidingDec = MountQueue.IsPulseGuidingDec;
                    break;
                case "Steps":
                    // Steps setter delegates to _defaultInstance?.ReceiveSteps() which signals
                    // _mountPositionUpdatedEvent on the owning instance — no direct signal needed.
                    Steps = MountQueue.Steps;
                    break;
            }
        }

        /// <summary>
        /// Update the Server and UI from the axis positions
        /// Phase 5.4: Delegates to instance-owned OnUpdateServerEvent for per-device lock isolation.
        /// </summary>
        internal static void UpdateServerEvent(object sender, EventArgs e)
        {
            _defaultInstance?.OnUpdateServerEvent(sender, e);
        }

        /// <summary>
        /// Wait for mount position to be updated using event signalling.
        /// Delegates to the default instance's per-instance event (Step 6).
        /// </summary>
        /// <exception cref="TimeoutException"></exception>
        public static void WaitMountPositionUpdated()
        {
            var evt = _defaultInstance?._mountPositionUpdatedEvent;
            if (evt is null) return;
            evt.Reset();
            UpdateSteps();  // Immediate position for tight control
            if (!evt.Wait(5000))
                throw new TimeoutException();
        }

        /// <summary>
        /// Handles the event triggered when a low voltage condition is detected.
        /// </summary>
        internal static void LowVoltageEventSet(object sender, EventArgs e)
        {
            LowVoltageEventState = true;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Error,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Mount detected low voltage: check power supply and wiring"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Get current local sidereal time
        /// </summary>
        internal static double GetLocalSiderealTime()
        {
            return GetLocalSiderealTime(HiResDateTime.UtcNow);
        }

        /// <summary>
        /// Get local sidereal time for specific UTC time
        /// </summary>
        internal static double GetLocalSiderealTime(DateTime utcNow)
        {
            var gsjd = JDate.Ole2Jd(utcNow);
            return Time.Lst(JDate.Epoch2000Days(), gsjd, false, _settings!.Longitude);
        }

        #endregion

        #region Alignment
        // Contains: ConnectAlignmentModel, AlignmentModel_Notification, AddAlignmentPoint, 
        //           GetSyncedAxes, GetUnsyncedAxes
        // Note: GetSyncedAxes and GetUnsyncedAxes should ideally be in Coordinate Transformations region
        //       but are kept here due to their tight coupling with AlignmentModel

        /// <summary>
        /// Initialize alignment model connection with current mount parameters
        /// </summary>
        internal static void ConnectAlignmentModel()
        {
            // ToDo: Remove if not needed
            // AlignmentModel.Connect(_homeAxes.X, _homeAxes.Y, StepsPerRevolution, AlignmentSettings.ClearModelOnStartup);
        }

        // ToDo: Remove if not needed
        /// <summary>
        /// Event handler for alignment model notifications
        /// </summary>
        //private static void AlignmentModel_Notification(object sender, NotificationEventArgs e)
        //{
        //    // Luckily the NotificationType enum and mimics MonitorType enum.
        //    var monitorItem = new MonitorEntry
        //    {
        //        Datetime = HiResDateTime.UtcNow,
        //        Device = MonitorDevice.Server,
        //        Category = MonitorCategory.Alignment,
        //        Type = (MonitorType)e.NotificationType,
        //        Method = e.Method,
        //        Thread = e.Thread,
        //        Message = e.Message
        //    };
        //    MonitorLog.LogToMonitor(monitorItem);
        //}

        /// <summary>
        /// Add an alignment point to the alignment model
        /// </summary>
        private static void AddAlignmentPoint()
        {
            // At this point:
            //      SkyServer.Steps contains the current encoder positions.
            //      SkyServer.FactorStep contains the conversion from radians to steps
            // To get the target steps
            // Set context from current settings
            var context = AxesContext.FromSettings(_settings);
            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec);
            var xy = Axes.RaDecToAxesXy(new[] { a.X, a.Y }, context);
            var unSynced = Axes.AxesAppToMount(new[] { xy[0], xy[1] }, context);
            var rawSteps = GetRawSteps();
            var synced = new[] { ConvertStepsToDegrees(rawSteps[0], 0), ConvertStepsToDegrees(rawSteps[1], 1) };
            // ToDo: Remove if not needed
            //if (AlignmentModel.SyncToRaDec(
            //        unSynced,
            //        synced,
            //        DateTime.Now))
            //{
            //    var monitorItem = new MonitorEntry
            //    {
            //        Datetime = HiResDateTime.UtcNow,
            //        Device = MonitorDevice.Server,
            //        Category = MonitorCategory.Alignment,
            //        Type = MonitorType.Information,
            //        Method = MethodBase.GetCurrentMethod()?.Name,
            //        Thread = Thread.CurrentThread.ManagedThreadId,
            //        Message = $"Alignment point added: Un-synced axis = {unSynced[0]}/{unSynced[1]}, RA/Dec = {a.X}/{a.Y}, Synched axis = {synced[0]}/{synced[1]}"
            //    };
            //    MonitorLog.LogToMonitor(monitorItem);
            //}
            //else
            //{
            //    var monitorItem = new MonitorEntry
            //    {
            //        Datetime = HiResDateTime.UtcNow,
            //        Device = MonitorDevice.Server,
            //        Category = MonitorCategory.Alignment,
            //        Type = MonitorType.Error,
            //        Method = MethodBase.GetCurrentMethod()?.Name,
            //        Thread = Thread.CurrentThread.ManagedThreadId,
            //        Message = $"Alignment point added: Un-synced axis = {unSynced[0]}/{unSynced[1]}, RA/Dec = {a.X}/{a.Y}, Synched axis = {synced[0]}/{synced[1]}"
            //    };
            //    MonitorLog.LogToMonitor(monitorItem);
            //}
        }

        /// <summary>
        /// Gets the alignment model corrected target (physical) axis positions for a given calculated axis position.
        /// </summary>
        /// <param name="unsynced">Calculated axis position</param>
        /// <returns>Physical axis position corrected by alignment model</returns>
        public static double[] GetSyncedAxes(double[] unsynced)
        {
            // ToDo: Remove if not needed
            //if (AlignmentModel.IsAlignmentOn && SkyServer.SlewState == SlewType.SlewRaDec && !SkyServer.IsHome && !SkyServer.AtPark)
            //{
            //    double[] synced = AlignmentModel.GetSyncedValue(unsynced);
            //    var monitorItem = new MonitorEntry
            //    {
            //        Datetime = HiResDateTime.UtcNow,
            //        Device = MonitorDevice.Server,
            //        Category = MonitorCategory.Alignment,
            //        Type = MonitorType.Information,
            //        Method = MethodBase.GetCurrentMethod()?.Name,
            //        Thread = Thread.CurrentThread.ManagedThreadId,
            //        Message = $"Mapped un-synced axis angles: {unsynced[0]}/{unsynced[1]} to {synced[0]}/{synced[1]}"
            //    };
            //    MonitorLog.LogToMonitor(monitorItem);

            //    // For safety, check the difference is within the max unsynced/synched difference found in the alignment model.
            //    var a = Math.Abs(unsynced[0] - synced[0]);
            //    var b = Math.Abs(unsynced[1] - synced[1]);
            //    double[] maxDelta = AlignmentModel.MaxDelta;
            //    if (Math.Abs(a) > maxDelta[0] * AlignmentModel.AlignmentWarningThreshold || Math.Abs(b) > maxDelta[1] * AlignmentModel.AlignmentWarningThreshold)
            //    {
            //        // Log a warning message, switch off the alignment model and return the original calculated position.
            //        monitorItem = new MonitorEntry
            //        {
            //            Datetime = HiResDateTime.UtcNow,
            //            Device = MonitorDevice.Server,
            //            Category = MonitorCategory.Alignment,
            //            Type = MonitorType.Warning,
            //            Method = MethodBase.GetCurrentMethod()?.Name,
            //            Thread = Thread.CurrentThread.ManagedThreadId,
            //            Message = $"Large delta: {unsynced[0]}|{unsynced[1]}|{synced[0]}|{synced[1]}|{maxDelta[0]}|{maxDelta[1]}"
            //        };
            //        MonitorLog.LogToMonitor(monitorItem);
            //        AlignmentSettings.IsAlertOn = true;
            //        return unsynced;
            //    }
            //    else
            //    {
            //        return synced;
            //    }
            //}
            //else
            //{
                return unsynced;
            //}
        }

        /// <summary>
        /// Get the axis positions to report for a given physical axis position.
        /// </summary>
        /// <param name="synced">Physical axis position</param>
        /// <returns>Calculated axis position</returns>
        private static double[] GetUnsyncedAxes(double[] synced)
        {
            // ToDo: Remove if not needed
            //if (AlignmentModel.IsAlignmentOn && SkyServer.SlewState != SlewType.SlewPark && SkyServer.SlewState != SlewType.SlewHome
            //    && !SkyServer.IsHome && !SkyServer.AtPark)
            //{
            //    var monitorItem = new MonitorEntry
            //    {
            //        Datetime = HiResDateTime.UtcNow,
            //        Device = MonitorDevice.Server,
            //        Category = MonitorCategory.Alignment,
            //        Type = MonitorType.Data,
            //        Method = MethodBase.GetCurrentMethod()?.Name,
            //        Thread = Thread.CurrentThread.ManagedThreadId,
            //        Message = $"Mapped synced axis angles: {synced[0]}/{synced[1]} to {unsynced[0]}/{unsynced[1]}"
            //    };
            //    MonitorLog.LogToMonitor(monitorItem);
            //    return AlignmentModel.GetUnsyncedValue(synced);
            //}

            return synced;
        }

        #endregion

        #region Mount-Specific Core Operations
        // Contains mount-specific implementations for Simulator and SkyWatcher

        #region Simulator Items
        // Contains: SimGoTo, SimPrecisionGoto, SimPulseGoto, SimTasks

        /// <summary>
        /// Sim GOTO slew (static wrapper - delegates to default instance)
        /// </summary>
        internal static int SimGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            return _defaultInstance?.SimGoTo(target, trackingState, slewType, token) ?? -1;
        }

        /// <summary>
        /// Sim precision GOTO (static wrapper - delegates to default instance)
        /// </summary>
        private static int SimPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            return _defaultInstance?.SimPrecisionGoto(target, slewType, token) ?? -1;
        }

        /// <summary>
        /// Sim pulse GOTO (static wrapper - delegates to default instance)
        /// </summary>
        private static void SimPulseGoto(CancellationToken token)
        {
            _defaultInstance?.SimPulseGoto(token);
        }



                                /// <summary>
                                /// Instance-aware SimTasks: routes commands and capability writes to the given MountInstance.
        /// </summary>
        public static void SimTasks(MountTaskName taskName, MountInstance instance)
        {
            if (!instance.IsMountRunning) return;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{taskName}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var context = AxesContext.FromSettings(instance.Settings);
            var q = instance.MountQueueInstance!;

            switch (instance.Settings.Mount)
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
                            instance._canAdvancedCmdSupport = false;
                            break;
                        case MountTaskName.CanPpec:
                            instance._canPPec = false;
                            break;
                        case MountTaskName.CanPolarLed:
                            instance._canPolarLed = false;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeCmd = new GetHomeSensorCapability(q.NewId, q);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(canHomeCmd).Result), out bool hasHome);
                            instance._canHomeSensor = hasHome;
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
                            var appAxes = instance.AppAxes;
                            var sync = Axes.AxesAppToMount(new[] { appAxes.X, appAxes.Y }, context);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, sync[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, sync[1]);
                            break;
                        case MountTaskName.SyncTarget:
                            var a = Transforms.CoordTypeToInternal(instance.TargetRa, instance.TargetDec);
                            var targetR = Axes.RaDecToAxesXy(new[] { a.X, a.Y }, context);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetR[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetR[1]);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSync = instance._altAzSync;
                            var targetA = new[] { altAzSync.Y, altAzSync.X };
                            targetA = Axes.AzAltToAxesXy(targetA, context);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, targetA[0]);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, targetA[1]);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new CmdSetMonitorPulse(0, q, instance._monitorPulse);
                            break;
                        case MountTaskName.Pec:
                            break;
                        case MountTaskName.PecTraining:
                            break;
                        case MountTaskName.Capabilities:
                            instance._capabilities = @"N/A";
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            break;
                        case MountTaskName.SetSnapPort1:
                            _ = new CmdSnapPort(0, q, 1, SnapPort1);
                            instance._snapPort1Result = false;
                            break;
                        case MountTaskName.SetSnapPort2:
                            _ = new CmdSnapPort(0, q, 2, SnapPort2);
                            instance._snapPort2Result = true;
                            break;
                        case MountTaskName.MountName:
                            var mountNameCmd = new CmdMountName(q.NewId, q);
                            instance._mountName = (string)q.GetCommandResult(mountNameCmd).Result;
                            break;
                        case MountTaskName.GetAxisVersions:
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            break;
                        case MountTaskName.MountVersion:
                            var mountVersionCmd = new CmdMountVersion(q.NewId, q);
                            instance._mountVersion = (string)q.GetCommandResult(mountVersionCmd).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var spr = new CmdSpr(q.NewId, q);
                            var sprnum = (long)q.GetCommandResult(spr).Result;
                            instance._stepsPerRevolution = new[] { sprnum, sprnum };
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var spw = new CmdSpw(q.NewId, q);
                            var spwnum = (double)q.GetCommandResult(spw).Result;
                            instance._stepsWormPerRevolution = new[] { spwnum, spwnum };
                            break;
                        case MountTaskName.SetHomePositions:
                            var homeAxesSim = instance.HomeAxes;
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis1, homeAxesSim.X);
                            _ = new CmdAxisToDegrees(0, q, Axis.Axis2, homeAxesSim.Y);
                            break;
                        case MountTaskName.GetFactorStep:
                            var factorStepCmd = new CmdFactorSteps(q.NewId, q);
                            instance._factorStep[0] = (double)q.GetCommandResult(factorStepCmd).Result;
                            instance._factorStep[1] = instance._factorStep[0];
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
        // Contains: TrackingOffsetRaRate, TrackingOffsetDecRate, CalcCustomTrackingOffset, SkyGetRate,
        //           SkyGoTo, SkyPrecisionGoto, SkyPulseGoto, SkyTasks

        // _trackingOffsetRate field moved to MountInstance._trackingOffsetRate (Step 2)

        /// <summary>
        /// Custom Tracking Offset for RA calculate into arc seconds per sec
        /// </summary>
        public static double TrackingOffsetRaRate
        {
            get => _defaultInstance?._trackingOffsetRate.X ?? 0.0;
            private set { if (_defaultInstance != null) _defaultInstance._trackingOffsetRate.X = value; }
        }

        /// <summary>
        /// Custom Tracking Offset for Dec calculate into arc seconds per sec
        /// </summary>
        public static double TrackingOffsetDecRate
        {
            get => _defaultInstance?._trackingOffsetRate.Y ?? 0.0;
            private set { if (_defaultInstance != null) _defaultInstance._trackingOffsetRate.Y = value; }
        }

        /// <summary>
        /// Adjust tracking rate for Custom Mount Gearing Offset settings
        /// </summary>
        /// <returns>difference in rates</returns>
        internal static void CalcCustomTrackingOffset()
        {
            if (_defaultInstance != null) _defaultInstance._trackingOffsetRate = new Vector(0.0, 0.0);

            //calculate mount sidereal :I, add offset to :I, Calculate new rate, Add rate difference to rate
            if (_settings!.Mount != MountType.SkyWatcher) { return; } //only use for sky watcher mounts

            if (_settings!.CustomGearing == false) { return; }

            var ratioFactor = (double)StepsTimeFreq[0] / StepsPerRevolution[0] * 1296000.0;  //generic factor for calc
            var siderealI = ratioFactor / SiderealRate;
            siderealI += _settings!.CustomRaTrackingOffset;  //calc :I and add offset
            var newRate = ratioFactor / siderealI; //calc new rate from offset
            TrackingOffsetRaRate = SiderealRate - newRate;

            ratioFactor = (double)StepsTimeFreq[1] / StepsPerRevolution[1] * 1296000.0;  //generic factor for calc
            siderealI = ratioFactor / SiderealRate;
            siderealI += _settings!.CustomDecTrackingOffset;  //calc :I and add offset
            newRate = ratioFactor / siderealI; //calc new rate from offset
            TrackingOffsetDecRate = SiderealRate - newRate;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{TrackingOffsetRaRate}|{TrackingOffsetDecRate}" };
            MonitorLog.LogToMonitor(monitorItem);

        }

        // used to combine multiple sources for a single slew rate
        // include tracking, hand controller, etc..
        private static int[] SkyTrackingOffset => _defaultInstance?._skyTrackingOffset ?? new[] { 0, 0 }; // Store for custom mount :I offset

        /// <summary>
        /// combines multiple Ra and Dec rates for a single slew rate
        /// </summary>
        /// <returns></returns>
        private static Vector SkyGetRate()
        {
            var change = new Vector();

            change += SkyTrackingRate; // Tracking
            change += SkyHcRate; // Hand controller
            // Primary axis
            change.X += RateMovePrimaryAxis;
            change.X += _settings!.AlignmentMode != AlignmentMode.AltAz ? GetRaRateDirection(RateRa) : 0;
            // Secondary axis
            change.Y += RateMoveSecondaryAxis;
            change.Y += _settings!.AlignmentMode != AlignmentMode.AltAz ? GetDecRateDirection(RateDec) : 0;

            CheckAxisLimits();

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{change}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return change;
        }

        /// <summary>
        /// SkyWatcher GOTO slew (static wrapper - delegates to default instance)
        /// </summary>
        internal static int SkyGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            return _defaultInstance?.SkyGoTo(target, trackingState, slewType, token) ?? -1;
        }

        /// <summary>
        /// SkyWatcher precision GOTO (static wrapper - delegates to default instance)
        /// </summary>
        private static int SkyPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            return _defaultInstance?.SkyPrecisionGoto(target, slewType, token) ?? -1;
        }

        /// <summary>
        /// SkyWatcher pulse GOTO (static wrapper - delegates to default instance)
        /// </summary>
        private static void SkyPulseGoto(CancellationToken token)
        {
            _defaultInstance?.SkyPulseGoto(token);
        }

        /// <summary>
        /// Instance-aware SkyTasks: routes commands and capability writes to the given MountInstance.
        /// </summary>
        public static void SkyTasks(MountTaskName taskName, MountInstance instance)
        {
            if (!instance.IsMountRunning) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{taskName}"
            };

            var context = AxesContext.FromSettings(instance.Settings);
            var q = instance.SkyQueueInstance!;

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
                            _ = new SkySetPPecTrain(0, q, Axis.Axis1, PecTraining);
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
                            var sp1 = new SkySetSnapPort(q.NewId, q, 1, SnapPort1);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp1).Result), out bool port1Result);
                            instance._snapPort1Result = port1Result;
                            break;
                        case MountTaskName.SetSnapPort2:
                            var sp2 = new SkySetSnapPort(q.NewId, q, 2, SnapPort2);
                            bool.TryParse(Convert.ToString(q.GetCommandResult(sp2).Result), out bool port2Result);
                            instance._snapPort2Result = port2Result;
                            break;
                        case MountTaskName.SyncAxes:
                            var appAxesSync = instance.AppAxes;
                            var sync = Axes.AxesAppToMount(new[] { appAxesSync.X, appAxesSync.Y }, context);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, sync[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, sync[1]);
                            monitorItem.Message += $",{appAxesSync.X}|{appAxesSync.Y}|{sync[0]}|{sync[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncTarget:
                            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec);
                            var targetR = Axes.RaDecToAxesXy(new[] { a.X, a.Y }, context);
                            _ = new SkySyncAxis(0, q, Axis.Axis1, targetR[0]);
                            _ = new SkySyncAxis(0, q, Axis.Axis2, targetR[1]);
                            monitorItem.Message += $",{Utilities.HoursToHMS(a.X, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(a.Y, " ", ":", "", 2)}|{targetR[0]}|{targetR[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncAltAz:
                            var altAzSyncPos = instance._altAzSync;
                            var targetA = new[] { altAzSyncPos.Y, altAzSyncPos.X };
                            targetA = Axes.AzAltToAxesXy(targetA, context);
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
                            StepsTimeFreq = (long[])q.GetCommandResult(skyStepTimeFreq).Result;
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
        /// Initiates a slew operation to move the mount to the specified target position.
        /// </summary>
        /// <remarks>This method will only execute if the mount is running. If <paramref
        /// name="slewAsync"/> is <see langword="false"/>,  the method will block until the slew operation is complete.
        /// Otherwise, the operation will proceed asynchronously.</remarks>
        /// <param name="targetPosition">The target position to which the mount should slew, specified as a <see cref="Vector"/> with X and Y
        /// coordinates.</param>
        /// <param name="slewState">The type of slew operation to perform, specified as a <see cref="SlewType"/>.</param>
        /// <param name="tracking">A value indicating whether tracking should be enabled during the slew operation. <see langword="true"/> to
        /// enable tracking; otherwise, <see langword="false"/>. The default is <see langword="false"/>.</param>
        /// <param name="slewAsync">A value indicating whether the slew operation should be performed asynchronously. <see langword="true"/> to
        /// perform the operation asynchronously; otherwise, <see langword="false"/>. The default is <see
        /// langword="true"/>.</param>
        private static void SlewMount(Vector targetPosition, SlewType slewState, bool tracking = false, bool slewAsync = true)
        {
            if (!IsMountRunning) { return; }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"from|{ActualAxisX}|{ActualAxisY}|to|{targetPosition.X}|{targetPosition.Y}|SlewType|{slewState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            HcResetPrevMove(MountAxis.Ra);
            HcResetPrevMove(MountAxis.Dec);

            AtPark = false;
            // ToDo reimplement later
            // SpeakSlewStart(slewState);
            // Set up event handle and task for checking slew started
            EventWaitHandle goToStartedEvent = new ManualResetEvent(false);
            Action goTo = () =>
                GoToAsync(new[] { targetPosition.X, targetPosition.Y }, slewState, goToStartedEvent, tracking);
            Task goToTask = new Task(goTo);
            // Start the go to and wait for the started event - IsSlewing will be set
            goToTask.Start();
            goToStartedEvent.WaitOne(5000); // Timeout for the event to be set
            if (!slewAsync) goToTask.Wait();
            goToStartedEvent = null;
        }

        /// <summary>
        /// Sets slew rates for all speed levels (1-8) based on maximum slew rate.
        /// All speeds are stored in degrees/second for ASCOM AxisRates compliance.
        /// </summary>
        /// <param name="maxRate">Maximum slew rate in degrees/second</param>
        /// <remarks>
        /// Values stored in degrees/second. Hardware layer (SkyWatcher/Simulator)
        /// converts to radians when sending commands to mount.
        /// Called during initialization and when MaxSlewRate setting changes.
        /// </remarks>
        internal static void SetSlewRates(double maxRate)
        {
            // Sky Speeds
                if (_defaultInstance == null) return;

                _defaultInstance._slewSpeedOne = Math.Round(maxRate * 0.0034, 3);
                _defaultInstance._slewSpeedTwo = Math.Round(maxRate * 0.0068, 3);
                _defaultInstance._slewSpeedThree = Math.Round(maxRate * 0.047, 3);
                _defaultInstance._slewSpeedFour = Math.Round(maxRate * 0.068, 3);
                _defaultInstance._slewSpeedFive = Math.Round(maxRate * 0.2, 3);
                _defaultInstance._slewSpeedSix = Math.Round(maxRate * 0.4, 3);
                _defaultInstance._slewSpeedSeven = Math.Round(maxRate * 0.8, 3);
                _defaultInstance._slewSpeedEight = Math.Round(maxRate * 1.0, 3);

            // Log (same as before)

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message =
                    $"{SlewSpeedOne}|{SlewSpeedTwo}|{SlewSpeedThree}|{SlewSpeedFour}|{SlewSpeedFive}|{SlewSpeedSix}|{SlewSpeedSeven}|{SlewSpeedEight}"
            };
            MonitorLog.LogToMonitor(monitorItem);

        }

        /// <summary>
        /// Instance-aware AxesStopValidate: routes stop commands and status queries to the given MountInstance's queues.
        /// </summary>
        internal static bool AxesStopValidate(MountInstance instance)
        {
            if (!instance.IsMountRunning) { return true; }
            Stopwatch stopwatch;
            bool axis2Stopped;
            bool axis1Stopped;
            switch (instance.Settings.Mount)
            {
                case MountType.Simulator:
                    var mq = instance.MountQueueInstance!;
                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SimTasks(MountTaskName.StopAxes, instance);
                        Thread.Sleep(100);
                        var statusX = new CmdAxisStatus(mq.NewId, mq, Axis.Axis1);
                        var axis1Status = (Mount.Simulator.AxisStatus)mq.GetCommandResult(statusX).Result;
                        axis1Stopped = axis1Status.Stopped;

                        var statusY = new CmdAxisStatus(mq.NewId, mq, Axis.Axis2);
                        var axis2Status = (Mount.Simulator.AxisStatus)mq.GetCommandResult(statusY).Result;
                        axis2Stopped = axis2Status.Stopped;

                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                case MountType.SkyWatcher:
                    var sq = instance.SkyQueueInstance!;
                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SkyTasks(MountTaskName.StopAxes, instance);
                        Thread.Sleep(100);
                        var statusx = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis1);
                        axis1Stopped = Convert.ToBoolean(sq.GetCommandResult(statusx).Result);

                        var statusy = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis2);
                        axis2Stopped = Convert.ToBoolean(sq.GetCommandResult(statusy).Result);

                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Asynchronously aborts the current slew operation of the mount.
        /// Telescope V4 requires asynchronous operation for aborting a slew.
        /// </summary>
        /// <param name="speak">A value indicating whether a verbal notification should be provided upon aborting the slew.  <see
        /// langword="true"/> to enable verbal notification; otherwise, <see langword="false"/>.</param>
        public static void AbortSlewAsync(bool speak)
        {
            // Set up event handle and task for checking slew started
            EventWaitHandle abortSlewStartedEvent = new ManualResetEvent(false);
            Action abortSlew = () => AbortSlew(speak, abortSlewStartedEvent);
            Task abortSlewTask = new Task(abortSlew);
            // Start the Abort Slew and wait for the started event
            abortSlewTask.Start();
            abortSlewStartedEvent.WaitOne(5000); // Timeout for the event to be set
            abortSlewStartedEvent = null;
        }

        #endregion

        #region Tracking & Rate Management

        /// <summary>
        /// Set/ reset tracking and slewing state whilst MoveAxis is active
        /// </summary>
        private static void SetRateMoveSlewState()
        {
            if (MovePrimaryAxisActive || MoveSecondaryAxisActive)
            {
                MoveAxisActive = true;
                IsSlewing = true;
                SlewState = SlewType.SlewMoveAxis;
            }
            if (!MovePrimaryAxisActive && !MoveSecondaryAxisActive)
            {
                MoveAxisActive = false;
                IsSlewing = false;
                SlewState = SlewType.SlewNone;
                if (Tracking) _defaultInstance.SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
        }

        /// <summary>
        /// Action Ra and Dec tracking rate offsets
        /// </summary>
        private static void ActionRateRaDec()
        {
            // If tracking is on then change the mount tracking rate
            if (Tracking)
            {
                if (_settings!.AlignmentMode == AlignmentMode.AltAz)
                {
                    // get tracking target at time now
                    var raDec = _defaultInstance.SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                    // set predictor parameters ready for tracking
                    _defaultInstance.SkyPredictor.Set(raDec[0], raDec[1], _defaultInstance?.RateRa ?? 0.0, _defaultInstance?.RateDec ?? 0.0);
                }
                SetTracking();
            }
            else
            {
                if (_settings!.AlignmentMode == AlignmentMode.AltAz)
                {
                    // no tracking target so set to current position 
                    _defaultInstance.SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, _defaultInstance?.RateRa ?? 0.0, _defaultInstance?.RateDec ?? 0.0);
                }
            }
        }

        #endregion

        #region Internal State & Calculations

        /// <summary>
        /// Updates axis upper and lower limit states using raw position in degrees
        /// X and Y axis limit values from SkySettings
        /// </summary>
        /// <param name="RawPositions">Raw position (X,Y) in degrees</param>
        public static void UpdateMountLimitStatus(double[] RawPositions)
        {
            const double oneArcSec = 1.0 / 3600;
            LimitStatus.AtLowerLimitAxisX = RawPositions[0] <= -_settings!.AxisLimitX - oneArcSec;
            LimitStatus.AtUpperLimitAxisX = RawPositions[0] >= _settings!.AxisLimitX + oneArcSec;
            var axisUpperLimitY = _settings!.AxisUpperLimitY;
            var axisLowerLimitY = _settings!.AxisLowerLimitY;
            if (_settings!.AlignmentMode == AlignmentMode.Polar && PolarMode == PolarMode.Left)
            {
                axisLowerLimitY = 180 - _settings!.AxisUpperLimitY;
                axisUpperLimitY = 180 - _settings!.AxisLowerLimitY;
            }
            LimitStatus.AtLowerLimitAxisY = RawPositions[1] <= axisLowerLimitY - oneArcSec;
            LimitStatus.AtUpperLimitAxisY = RawPositions[1] >= axisUpperLimitY + oneArcSec;
        }


        #endregion
    }
}
