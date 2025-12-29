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
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Core functionality for SkyServer - handles mount communication, position tracking, and internal state
    /// </summary>
    public static partial class SkyServer
    {
        #region Core Fields

        private const double SiderealRate = 15.0410671786691;

        private static readonly object TimerLock = new object();
        private static MediaTimer _mediaTimer;
        private static MediaTimer _altAzTrackingTimer;
        private static Int32 _altAzTrackingLock;
        // Phase A: Instance-based settings support
        private static SkySettingsInstance _settings;

        // Slew and HC speeds
        private static double _slewSpeedOne;
        private static double _slewSpeedTwo;
        private static double _slewSpeedThree;
        private static double _slewSpeedFour;
        private static double _slewSpeedFive;
        private static double _slewSpeedSix;
        private static double _slewSpeedSeven;
        public static double SlewSpeedEight;

        // HC Anti-Backlash
        private static HcPrevMove _hcPrevMoveRa;
        private static HcPrevMove _hcPrevMoveDec;
        private static readonly IList<double> HcPrevMovesDec = new List<double>();

        private static Vector _homeAxes;
        // App axes derived from mount axes by AxesMountToApp
        private static Vector _appAxes;
        //private static Vector _targetAxes;
        private static Vector _altAzSync;

        // ToDo: Remove if not needed
        // public static readonly List<SpiralPoint> SpiralCollection;

        // AlignmentModel
        // ToDo: Remove if not needed
        // public static readonly AlignmentModel AlignmentModel;

        // Cancellation token sources for go to and pulse guide async operations
        private static volatile CancellationTokenSource _ctsGoTo;
        private static volatile CancellationTokenSource _ctsPulseGuideRa;
        private static volatile CancellationTokenSource _ctsPulseGuideDec;
        private static volatile CancellationTokenSource _ctsHcPulseGuide;

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

                // load default or user property settings
                // SkySettings.Load();

                // load some things
                Defaults();

                // ToDo: Remove if not needed
                // SpiralCollection = new List<SpiralPoint>();

                // set local to NaN for constructor
                _appAxes = new Vector(double.NaN, double.NaN);

                // ToDo: Remove if not needed
                // initialise the alignment model
                //AlignmentSettings.Load();
                //AlignmentModel = new AlignmentModel(
                //    SkySettings.Latitude,
                //    SkySettings.Longitude,
                //    SkySettings.Elevation)
                //{
                //    IsAlignmentOn = AlignmentSettings.IsAlignmentOn,
                //    ThreePointAlgorithm = ThreePointAlgorithmEnum.BestCentre
                //};
                //AlignmentModel.Notification += AlignmentModel_Notification;

                // attach handler to watch for SkySettings changing.
                SkySettings.StaticPropertyChanged += PropertyChangedSkySettings;

                // ToDo: Remove if not needed
                // attach handler to watch for AlignmentSettings changing;
                // AlignmentSettings.StaticPropertyChanged += PropertyChangedAlignmentSettings;

                // attach handler to watch for pulses changing;
                SkyQueue.StaticPropertyChanged += PropertyChangedSkyQueue;

                // attach handler to watch for pulses changing;
                MountQueue.StaticPropertyChanged += PropertyChangedMountQueue;

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
        // Contains: MountConnect, MountStart, MountStop, MountReset, GetHomeAxes, ShutdownServer
        // Phase 3.1: COMPLETE - All mount lifecycle methods have been moved

        /// <summary>
        /// Sets up defaults after an established connection
        /// </summary>
        private static bool MountConnect()
        {
            _targetRaDec = new Vector(double.NaN, double.NaN); // invalid target position
            var positions = GetDefaultPositions();
            double[] rawPositions = null;
            var counter = 0;
            int raWormTeeth;
            int decWormTeeth;
            bool positionsSet = false;
            MonitorEntry monitorItem;
            string msg;

            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    // defaults
                    SimTasks(MountTaskName.MountName);
                    SimTasks(MountTaskName.MountVersion);
                    SimTasks(MountTaskName.StepsPerRevolution);
                    SimTasks(MountTaskName.StepsWormPerRevolution);
                    SimTasks(MountTaskName.CanHomeSensor);
                    SimTasks(MountTaskName.GetFactorStep);
                    SimTasks(MountTaskName.Capabilities);

                    raWormTeeth = (int)(StepsPerRevolution[0] / StepsWormPerRevolution[0]);
                    decWormTeeth = (int)(StepsPerRevolution[1] / StepsWormPerRevolution[1]);
                    WormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    PecBinSteps = StepsPerRevolution[0] / (WormTeethCount[0] * 1.0) / PecBinCount;

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetRawDegrees:{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
                        MonitorLog.LogToMonitor(monitorItem);

                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (AtPark)
                        {
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        _ = new CmdAxisToDegrees(0, Axis.Axis1, positions[0]);
                        _ = new CmdAxisToDegrees(0, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }
                    // Update AlignmentModel settings.
                    ConnectAlignmentModel();

                    break;
                case MountType.SkyWatcher:
                    _skyHcRate = new Vector(0, 0);
                    _skyTrackingRate = new Vector(0, 0);

                    // create a command and put in queue to test connection
                    var init = new SkyGetMotorCardVersion(SkyQueue.NewId, Axis.Axis1);
                    _ = (string)SkyQueue.GetCommandResult(init).Result;
                    if (!init.Successful && init.Exception != null)
                    {
                        // ToDo: fix string resource
                        init.Exception = new Exception($"CheckMount{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        // init.Exception = new Exception($"{MediaTypeNames.Application.Current.Resources["CheckMount"]}{Environment.NewLine}{init.Exception.Message}", init.Exception);
                        SkyErrorHandler(init.Exception);
                        return false;
                    }

                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Voltage|{SkyServer.ControllerVoltage.ToString("F2") + " V"}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    // defaults
                    if (SkySettings.Mount == MountType.SkyWatcher)
                    {
                        SkyTasks(MountTaskName.AllowAdvancedCommandSet);
                    }
                    SkyTasks(MountTaskName.LoadDefaults);
                    SkyTasks(MountTaskName.StepsPerRevolution);
                    SkyTasks(MountTaskName.StepsWormPerRevolution);
                    SkyTasks(MountTaskName.StopAxes);
                    SkyTasks(MountTaskName.Encoders);
                    SkyTasks(MountTaskName.FullCurrent);
                    SkyTasks(MountTaskName.SetSt4Guiderate);
                    SkyTasks(MountTaskName.SetSouthernHemisphere);
                    SkyTasks(MountTaskName.MountName);
                    SkyTasks(MountTaskName.MountVersion);
                    SkyTasks(MountTaskName.StepTimeFreq);
                    SkyTasks(MountTaskName.CanPpec);
                    SkyTasks(MountTaskName.CanPolarLed);
                    SkyTasks(MountTaskName.PolarLedLevel);
                    SkyTasks(MountTaskName.CanHomeSensor);
                    SkyTasks(MountTaskName.DecPulseToGoTo);
                    SkyTasks(MountTaskName.AlternatingPpec);
                    SkyTasks(MountTaskName.MinPulseDec);
                    SkyTasks(MountTaskName.MinPulseRa);
                    SkyTasks(MountTaskName.GetFactorStep);
                    SkyTasks(MountTaskName.Capabilities);
                    SkyTasks(MountTaskName.CanAdvancedCmdSupport);
                    if (CanPPec) SkyTasks(MountTaskName.Pec);

                    //CanHomeSensor = true; //test auto home

                    raWormTeeth = (int)(StepsPerRevolution[0] / StepsWormPerRevolution[0]);
                    decWormTeeth = (int)(StepsPerRevolution[1] / StepsWormPerRevolution[1]);
                    WormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    PecBinSteps = StepsPerRevolution[0] / (WormTeethCount[0] * 1.0) / PecBinCount;

                    CalcCustomTrackingOffset();  //generates rates for the custom gearing offsets

                    //log current positions
                    var steps = GetRawSteps();
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"GetSteps:{steps[0]}|{steps[1]}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        }
                        counter++;

                        //get positions and log them
                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetDegrees|{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
                        MonitorLog.LogToMonitor(monitorItem);

                        //if an error getting positions then stay in while loop and try again
                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (AtPark)
                        {
                            _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        //was mount powered and at 0,0  are both axes close to home?  if not then don't change current mount positions 
                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        //Mount is close to home 0,0 so set the default position
                        _ = new SkySetAxisPosition(0, Axis.Axis1, positions[0]);
                        _ = new SkySetAxisPosition(0, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    // Update AlignmentModel settings.
                    ConnectAlignmentModel();

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            msg = positionsSet ? $"SetPositions|{positions[0]}|{positions[1]}" : $"PositionsNotSet";
            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"MountAxes|{_appAxes.X}|{_appAxes.Y}|Actual|{ActualAxisX}|{ActualAxisY}" };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"StepsPerRevolution|{StepsPerRevolution[0]}|{StepsPerRevolution[1]}" };
            MonitorLog.LogToMonitor(monitorItem);

            //Load Pec Files
            var pecmsg = string.Empty;
            PecOn = SkySettings.PecOn;
            if (File.Exists(SkySettings.PecWormFile))
            {
                LoadPecFile(SkySettings.PecWormFile);
                pecmsg += SkySettings.PecWormFile;
            }

            if (File.Exists(SkySettings.Pec360File))
            {
                LoadPecFile(SkySettings.Pec360File);
                pecmsg += ", " + SkySettings.PecWormFile;
            }

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Pec: {pecmsg}" };
            MonitorLog.LogToMonitor(monitorItem);

            try
            {
                // Get the app's configuration
                var userConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
                // User config file copy from path
                var userConfigFilepath = userConfig.FilePath;
                // User config file copy to directory path
                var logDirectoryPath = GsFile.GetLogPath();
                // Copy the user config file to the log directory
                File.Copy(userConfigFilepath, Path.Combine(logDirectoryPath, "user.config"), true);

                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Copied user.config to {logDirectoryPath}" };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch (Exception e) when (e is ConfigurationErrorsException || e is ArgumentException) // All other exceptions mean app cannot function
            {
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Cannot copy user.config. {e.Message} " };
                MonitorLog.LogToMonitor(monitorItem);
            }

            return true;
        }

        /// <summary>
        /// Start connection, queues, and events
        /// </summary>
        private static void MountStart()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{SkySettings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // setup server defaults, stop auto-discovery, connect serial port, start queues
            Defaults();
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    Mount.Simulator.Settings.AutoHomeAxisX = (int)SkySettings.AutoHomeAxisX;
                    Mount.Simulator.Settings.AutoHomeAxisY = (int)SkySettings.AutoHomeAxisY;
                    MountQueue.Start();
                    if (MountQueue.IsRunning) { ConnectAlignmentModel(); }
                    else
                    { throw new Exception("Failed to start simulator queue"); }

                    break;
                case MountType.SkyWatcher:
                    // open serial port
                    SkySystem.ConnectSerial = false;
                    SkySystem.ConnectSerial = true;
                    if (!SkySystem.ConnectSerial)
                    {
                        throw new SkyServerException(ErrorCode.ErrSerialFailed,
                            $"Connection Failed: {SkySystem.Error}");
                    }
                    // Start up, pass custom mount gearing if needed
                    var custom360Steps = new[] { 0, 0 };
                    var customWormSteps = new[] { 0.0, 0.0 };
                    if (SkySettings.CustomGearing)
                    {
                        custom360Steps = new[] { SkySettings.CustomRa360Steps, SkySettings.CustomDec360Steps };
                        customWormSteps = new[] { (double)SkySettings.CustomRa360Steps / SkySettings.CustomRaWormTeeth, (double)SkySettings.CustomDec360Steps / SkySettings.CustomDecWormTeeth };
                    }

                    SkyQueue.Start(SkySystem.Serial, custom360Steps, customWormSteps, LowVoltageEventSet);
                    if (!SkyQueue.IsRunning)
                    {
                        throw new SkyServerException(ErrorCode.ErrMount, "Failed to start sky queue");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Run mount default commands and start the UI updates
            if (MountConnect())
            {
                // start with a stop
                AxesStopValidate();

                // Event to get mount positions and update UI
                _mediaTimer = new MediaTimer { Period = SkySettings.DisplayInterval, Resolution = 5 };
                _mediaTimer.Tick += UpdateServerEvent;
                _mediaTimer.Start();

                // Event to update AltAz tracking rate
                _altAzTrackingTimer = new MediaTimer { Period = SkySettings.AltAzTrackingUpdateInterval, Resolution = 5 };
                _altAzTrackingTimer.Tick += AltAzTrackingTimerEvent;
            }
            else
            {
                MountStop();
            }
        }

        /// <summary>
        /// Stop queues and events
        /// </summary>
        private static void MountStop()
        {
            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{SkySettings.Mount}" };
            MonitorLog.LogToMonitor(monitorItem);

            // Stop all asynchronous operations
            Tracking = false;
            CancelAllAsync();
            AxesStopValidate();
            if (_mediaTimer != null) { _mediaTimer.Tick -= UpdateServerEvent; }
            _mediaTimer?.Stop();
            _mediaTimer?.Dispose();
            if (_altAzTrackingTimer != null) { _altAzTrackingTimer.Tick -= AltAzTrackingTimerEvent; }
            _altAzTrackingTimer?.Stop();
            _altAzTrackingTimer?.Dispose();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 1000) { } //change
            sw.Stop();

            if (MountQueue.IsRunning) { MountQueue.Stop(); }

            if (!SkyQueue.IsRunning) return;
            SkyQueue.Stop();
            SkySystem.ConnectSerial = false;
        }

        public static void MountReset()
        {
            // Load all settings
            SkySettings.Load();
            // Set home positions
            _homeAxes = GetHomeAxes(SkySettings.HomeAxisX, SkySettings.HomeAxisY);
            // Set axis positions
            _appAxisX = _homeAxes.X;
            _appAxisY = _homeAxes.Y;
        }

        /// <summary>
        /// Get home axes adjusted for angle offset
        /// </summary>
        /// <param name="xAxis"></param>
        /// <param name="yAxis"></param>
        /// <returns></returns>
        public static Vector GetHomeAxes(double xAxis, double yAxis)
        {
            var home = new[] { xAxis, yAxis };
            if (SkySettings.AlignmentMode != AlignmentMode.Polar)
            {
                home = Axes.AxesAppToMount(new[] { xAxis, yAxis });
            }
            else
            {
                var angleOffset = SouthernHemisphere ? 180.0 : 0.0;
                home[0] -= angleOffset;
                home = Axes.AzAltToAxesXy(home);
            }
            return new Vector(home[0], home[1]);
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
        /// Initialize SkyServer with instance-based settings
        /// </summary>
        public static void Initialize(SkySettingsInstance settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "SkyServer initialized with instance settings"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }       
        
        #endregion

        #region Position Update Methods
        // Contains: GetRawDegrees, ConvertStepsToDegrees, GetRawSteps (3 overloads), UpdateSteps, GetRawStepsDt,
        //           MapSlewTargetToAxes, GetDefaultPositions

        /// <summary>
        /// Gets current converted positions from the mount in degrees
        /// </summary>
        /// <returns></returns>
        private static double[] GetRawDegrees()
        {
            var actualDegrees = new[] { double.NaN, double.NaN };
            if (!IsMountRunning) { return actualDegrees; }
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    actualDegrees = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    break;
                case MountType.SkyWatcher:
                    var skyPositions = new SkyGetPositionsInDegrees(SkyQueue.NewId);
                    actualDegrees = (double[])SkyQueue.GetCommandResult(skyPositions).Result;
                    return CheckSkyErrors(skyPositions) ? null : actualDegrees;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return actualDegrees;
        }

        /// <summary>
        /// Convert steps to degrees
        /// </summary>
        /// <param name="steps"></param>
        /// <param name="axis"></param>
        /// <returns>degrees</returns>
        private static double ConvertStepsToDegrees(double steps, int axis)
        {
            double degrees;
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    degrees = steps / FactorStep[axis];
                    break;
                case MountType.SkyWatcher:
                    degrees = Principles.Units.Rad2Deg1(steps * FactorStep[axis]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return degrees;
        }

        /// <summary>
        /// Get steps from the mount
        /// </summary>
        /// <returns>double array</returns>
        private static double[] GetRawSteps()
        {
            var steps = new[] { double.NaN, double.NaN };
            if (!IsMountRunning) { return steps; }
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    steps = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    steps[0] *= FactorStep[0];
                    steps[1] *= FactorStep[1];
                    break;
                case MountType.SkyWatcher:
                    var skySteps = new SkyGetSteps(SkyQueue.NewId);
                    steps = (double[])SkyQueue.GetCommandResult(skySteps).Result;

                    return CheckSkyErrors(skySteps) ? new[] { double.NaN, double.NaN } : steps;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return steps;
        }

        private static DateTime lastUpdateStepsTime = DateTime.MinValue;
        private static object lastUpdateLock = new object();

        /// <summary>
        /// Main get for the Steps
        /// </summary>
        /// <returns></returns>
        public static void UpdateSteps()
        {
            lock (lastUpdateLock)
            {
                if (IsMountRunning || (lastUpdateStepsTime.AddMilliseconds(100) < HiResDateTime.UtcNow))
                {
                    switch (SkySettings.Mount)
                    {
                        case MountType.Simulator:
                            _ = new CmdAxesSteps(MountQueue.NewId);
                            break;
                        case MountType.SkyWatcher:
                            _ = new SkyUpdateSteps(SkyQueue.NewId);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    lastUpdateStepsTime = HiResDateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Gets current positions from the mount in steps
        /// </summary>
        /// <returns></returns>
        private static double? GetRawSteps(int axis)
        {
            if (!IsMountRunning) { return null; }
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxisSteps(MountQueue.NewId);
                    var a = (int[])MountQueue.GetCommandResult(simPositions).Result;

                    switch (axis)
                    {
                        case 0:
                            return Convert.ToDouble(a[0]);
                        case 1:
                            return Convert.ToDouble(a[1]);
                        default:
                            return null;
                    }
                case MountType.SkyWatcher:
                    switch (axis)
                    {
                        case 0:
                            var b = new SkyGetAxisPositionCounter(SkyQueue.NewId, Axis.Axis1);
                            return Convert.ToDouble(SkyQueue.GetCommandResult(b).Result);
                        case 1:
                            var c = new SkyGetAxisPositionCounter(SkyQueue.NewId, Axis.Axis2);
                            return Convert.ToDouble(SkyQueue.GetCommandResult(c).Result);
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }

        public static Tuple<double?, DateTime> GetRawStepsDt(int axis)
        {
            if (!IsMountRunning) { return null; }
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    switch (axis)
                    {
                        case 0:
                            var a = new AxisStepsDt(MountQueue.NewId, Axis.Axis1);
                            return MountQueue.GetCommandResult(a).Result;
                        case 1:
                            var b = new AxisStepsDt(MountQueue.NewId, Axis.Axis2);
                            return MountQueue.GetCommandResult(b).Result;
                        default:
                            return null;
                    }
                case MountType.SkyWatcher:
                    switch (axis)
                    {
                        case 0:
                            var b = new SkyGetAxisPositionDate(SkyQueue.NewId, Axis.Axis1);
                            return SkyQueue.GetCommandResult(b).Result;
                        case 1:
                            var c = new SkyGetAxisPositionCounter(SkyQueue.NewId, Axis.Axis2);
                            return SkyQueue.GetCommandResult(c).Result;
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Maps a slew target to the corresponding axes based on the specified slew type.
        /// </summary>
        /// <remarks>The mapping behavior depends on the specified <paramref name="slewType">: <list
        /// type="bullet"> <item> <description> For <see cref="SlewType.SlewRaDec"/>, the target is converted to RA/Dec
        /// axes and synchronized. </description> </item> <item> <description> For <see cref="SlewType.SlewAltAz"/>, the
        /// target is converted to Alt/Az axes. </description> </item> <item> <description> For <see
        /// cref="SlewType.SlewPark"/>, <see cref="SlewType.SlewHome"/>, or <see cref="SlewType.SlewMoveAxis"/>,  the
        /// target is converted to mount-specific axes. </description> </item> </list> If the <paramref
        /// name="slewType"/> is not recognized, the method returns the input target unchanged.</remarks>
        /// <param name="target">An array of doubles representing the target coordinates to be mapped.</param>
        /// <param name="slewType">The type of slew operation to perform, which determines how the target is mapped to axes.</param>
        /// <returns>An array of doubles representing the target coordinates mapped to the appropriate axes.</returns>
        public static double[] MapSlewTargetToAxes(double[] target, SlewType slewType)
        {
            // Convert target to axes based on slew type
            switch (slewType)
            {
                case SlewType.SlewRaDec:
                    // convert target to axis for Ra / Dec slew
                    target = Axes.RaDecToAxesXy(target);
                    // Convert to synced axes
                    target = GetSyncedAxes(target);
                    break;
                case SlewType.SlewAltAz:
                    // convert target to axis for Az / Alt slew
                    target = Axes.AzAltToAxesXy(target);
                    break;
                case SlewType.SlewHome:
                    break;
                case SlewType.SlewPark:
                    // convert to mount coordinates for park
                    target = Axes.AxesAppToMount(target);
                    break;
                case SlewType.SlewMoveAxis:
                    target = Axes.AxesAppToMount(target);
                    break;
                default:
                    break;
            }
            return target;
        }

        /// <summary>
        /// Used when the mount is first turned on and the instance is created
        /// </summary>
        private static double[] GetDefaultPositions()
        {
            // set default home position or get home override from the settings 
            double[] positions = { 0, 0 };
            string name = String.Empty;
            // home axes are mount values
            _homeAxes = GetHomeAxes(SkySettings.HomeAxisX, SkySettings.HomeAxisY);

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Home position,{name}|{_homeAxes.X}|{_homeAxes.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            if (AtPark)
            {
                if (SkySettings.AutoTrack)
                {
                    AtPark = false;
                    Tracking = SkySettings.AutoTrack;
                }
                positions = Axes.AxesAppToMount(SkySettings.ParkAxes);
                ParkSelected = GetStoredParkPosition();

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Parked,{SkySettings.ParkName}|{SkySettings.ParkAxes[0]}|{SkySettings.ParkAxes[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);

            }
            else
            {
                positions = new[] { _homeAxes.X, _homeAxes.Y };
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
        // Complete - no Phase 3 additions planned

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
                        case Mount.SkyWatcher.ErrorCode.ErrInvalidId:
                        case Mount.SkyWatcher.ErrorCode.ErrAlreadyConnected:
                        case Mount.SkyWatcher.ErrorCode.ErrNotConnected:
                        case Mount.SkyWatcher.ErrorCode.ErrInvalidData:
                        case Mount.SkyWatcher.ErrorCode.ErrSerialPortBusy:
                        case Mount.SkyWatcher.ErrorCode.ErrMountNotFound:
                        case Mount.SkyWatcher.ErrorCode.ErrNoResponseAxis1:
                        case Mount.SkyWatcher.ErrorCode.ErrNoResponseAxis2:
                        case Mount.SkyWatcher.ErrorCode.ErrAxisBusy:
                        case Mount.SkyWatcher.ErrorCode.ErrMaxPitch:
                        case Mount.SkyWatcher.ErrorCode.ErrMinPitch:
                        case Mount.SkyWatcher.ErrorCode.ErrUserInterrupt:
                        case Mount.SkyWatcher.ErrorCode.ErrAlignFailed:
                        case Mount.SkyWatcher.ErrorCode.ErrUnimplemented:
                        case Mount.SkyWatcher.ErrorCode.ErrWrongAlignmentData:
                        case Mount.SkyWatcher.ErrorCode.ErrQueueFailed:
                        case Mount.SkyWatcher.ErrorCode.ErrTooManyRetries:
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
        // Phase 3 will add various helper methods:
        // - Phase 3.3: SetSlewRates, GetSlewRate
        // - Phase 3.4: SetGuideRates, GetRaRateDirection, GetDecRateDirection
        // - Phase 3.7: SpeakSlewStart, SpeakSlewEnd, ValidateLimits, CheckMount, etc.

        /// <summary>
        /// Load default settings and slew rates
        /// </summary>
        private static void Defaults()
        {
            SlewSettleTime = 0;

            // Initialize FactorStep array
            if (FactorStep == null)
            {
                FactorStep = new double[2];
            }

            // home axes
            _homeAxes = GetHomeAxes(SkySettings.HomeAxisX, SkySettings.HomeAxisY);

            // set the slew speeds, the longest distance is using the higher speed for longer
            _slewSpeedOne = Principles.Units.Deg2Rad1((int)SkySettings.HcSpeed * (15.0 / 3600)); //1x 15"/s
            _slewSpeedTwo = _slewSpeedOne * 2; //2x
            _slewSpeedThree = _slewSpeedOne * 8; //8x
            _slewSpeedFour = _slewSpeedOne * 16; //16x
            _slewSpeedFive = _slewSpeedOne * 32; //32x
            _slewSpeedSix = _slewSpeedOne * 64; //64x
            _slewSpeedSeven = _slewSpeedOne * 600; //600x
            SlewSpeedEight = _slewSpeedOne * 800; //800x

            var maxSlew = Principles.Units.Deg2Rad1(SkySettings.MaxSlewRate);
            SetSlewRates(maxSlew);

            // set the guiderates
            _guideRate = new Vector(SkySettings.GuideRateOffsetY, SkySettings.GuideRateOffsetX);
            SetGuideRates();
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
        // Phase 3.5 will add: AltAzTrackingTimerEvent, CheckPecTraining (from SkyServer.cs)

        /// <summary>
        /// Property changes from SkySettings
        /// </summary>
        private static void PropertyChangedSkySettings(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AtPark":
                    if (AtPark != SkySettings.AtPark) AtPark = SkySettings.AtPark;
                    break;
                case "Latitude":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteLatitude = SkySettings.Latitude;
                    // Reset latitude based park positions for Polar mode
                    if (SkySettings.AlignmentMode == AlignmentMode.Polar) SkySettings.ResetParkPositions();
                    break;
                case "Longitude":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteLongitude = SkySettings.Longitude;
                    break;
                case "Elevation":
                    // ToDo: Remove if not needed
                    // AlignmentModel.SiteElevation = SkySettings.Elevation;
                    break;
                case "AlignmentMode":
                    Tracking = false;
                    SkyPredictor.Reset();
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
                    Steps = SkyQueue.Steps;
                    MountPositionUpdated = true;
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
                    Steps = MountQueue.Steps;
                    MountPositionUpdated = true;
                    break;
            }
        }

        /// <summary>
        /// Update the Server and UI from the axis positions
        /// Main position update loop - runs every display interval
        /// </summary>
        private static void UpdateServerEvent(object sender, EventArgs e)
        {
            var hasLock = false;
            try
            {
                // Stops the overrun of previous event not ended before next one starts
                Monitor.TryEnter(TimerLock, ref hasLock);
                if (!hasLock)
                {
                    TimerOverruns++;
                    return;
                }

                LoopCounter++; // increment counter

                SiderealTime = GetLocalSiderealTime(); // the time is?

                UpdateSteps(); // get step from the mount

                Lha = Coordinate.Ra2Ha12(RightAscensionXForm, SiderealTime);

                CheckSlewState(); // Track slewing state

                CheckAxisLimits(); //used for warning light

                // ToDo: Remove if not needed
                // CheckSpiralLimit(); // reset spiral if moved too far

                // Update UI 
                CheckPecTraining();
                IsHome = AtHome;

                switch (SkySettings.AlignmentMode)
                {
                    case AlignmentMode.AltAz:
                        IsSideOfPier = SideOfPier;
                        break;
                    case AlignmentMode.Polar:
                        IsSideOfPier = SideOfPier;
                        break;
                    case AlignmentMode.GermanPolar:
                        IsSideOfPier = SideOfPier;
                        break;
                    default:
                        break;
                }
                //var t = SkySettings.DisplayInterval; // Event interval time set for UI performance 
                //_mediaTimer.Period = t;

                //if (LoopCounter % (ulong)Settings.Settings.ModelIntFactor == 0)
                //{
                //    Rotate3DModel = true;
                //}
            }
            catch (Exception ex)
            {
                SkyErrorHandler(ex);
            }
            finally
            {
                if (hasLock) { Monitor.Exit(TimerLock); }
            }
        }

        /// <summary>
        /// Handles the event triggered when a low voltage condition is detected.
        /// </summary>
        private static void LowVoltageEventSet(object sender, EventArgs e)
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
        private static double GetLocalSiderealTime()
        {
            return GetLocalSiderealTime(HiResDateTime.UtcNow);
        }

        /// <summary>
        /// Get local sidereal time for specific UTC time
        /// </summary>
        private static double GetLocalSiderealTime(DateTime utcNow)
        {
            var gsjd = JDate.Ole2Jd(utcNow);
            return Time.Lst(JDate.Epoch2000Days(), gsjd, false, SkySettings.Longitude);
        }

        #endregion

        #region Alignment
        // Contains: ConnectAlignmentModel, AlignmentModel_Notification, AddAlignmentPoint, 
        //           GetSyncedAxes, GetUnsyncedAxes
        // Note: GetSyncedAxes and GetUnsyncedAxes should ideally be in Coordinate Transformations region
        //       but are kept here due to their tight coupling with AlignmentModel
        // Complete - no Phase 3 additions planned

        /// <summary>
        /// Initialize alignment model connection with current mount parameters
        /// </summary>
        private static void ConnectAlignmentModel()
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
            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec);
            var xy = Axes.RaDecToAxesXy(new[] { a.X, a.Y });
            var unSynced = Axes.AxesAppToMount(new[] { xy[0], xy[1] });
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
        // Complete - Phase 2 finished moving all mount-specific goto/task methods here

        #region Simulator Items
        // Contains: SimGoTo, SimPrecisionGoto, SimPulseGoto, SimTasks
        // Complete - no Phase 3 additions planned

        /// <summary>
        /// Sim GOTO slew
        /// </summary>
        /// <returns></returns>
        private static int SimGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            const int success = 0;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"from|{ActualAxisX}|{ActualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            token.ThrowIfCancellationRequested(); // check for a stop
            var simTarget = MapSlewTargetToAxes(target, slewType); // map target to axes
            const int timer = 120; //  stop slew after seconds
            var stopwatch = Stopwatch.StartNew();

            SimTasks(MountTaskName.StopAxes);

            #region First Slew
            token.ThrowIfCancellationRequested(); // check for a stop
            // time could be off a bit may need to deal with each axis separate
            _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]);
            _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested(); // check for a stop

                var statusx = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
                var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusx).Result;
                var axis1Stopped = axis1Status.Stopped;

                Thread.Sleep(50);
                token.ThrowIfCancellationRequested(); // check for a stop

                var statusy = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
                var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusy).Result;
                var axis2Stopped = axis2Status.Stopped;

                if (!axis1Stopped || !axis2Stopped) continue;
                if (SlewSettleTime > 0) Tasks.DelayHandler(TimeSpan.FromSeconds(SlewSettleTime).Milliseconds); // post-slew settling time

                break;
            }
            stopwatch.Stop();

            AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"GoToSeconds|{stopwatch.Elapsed.TotalSeconds}|SimTarget|{simTarget[0]}|{simTarget[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested(); // check for a stop
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                Task.Run(() => SimPrecisionGoto(target, slewType, token)).Wait();
            #endregion

            SimTasks(MountTaskName.StopAxes);//make sure all axes are stopped
            return success;
        }

        /// <summary>
        /// Performs a final precision slew of the axes to target if necessary.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="slewType"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private static int SimPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"from|({ActualAxisX},{ActualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            // var gotoPrecision = SkySettings.GotoPrecision;
            var maxTries = 0;
            double[] deltaDegree = { 0.0, 0.0 };
            double[] gotoPrecision = { ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) };
            const double milliSeconds = 0.001;
            var deltaTime = 75 * milliSeconds; // 75mS for simulator slew

            while (true)
            {
                token.ThrowIfCancellationRequested(); // check for a stop
                if (maxTries > 5) { break; }
                maxTries++;
                if (SkySettings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
                    // get predicted RA and Dec at update time
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    // convert to internal Ra and Dec
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                    target = new[] { internalRaDec.X, internalRaDec.Y };
                }
                var simTarget = MapSlewTargetToAxes(target, slewType); // map target to axes

                // Calculate error
                var rawPositions = GetRawDegrees();
                if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1])) { break; }
                deltaDegree[0] = Range.Range180(simTarget[0] - rawPositions[0]);
                deltaDegree[1] = Range.Range180(simTarget[1] - rawPositions[1]);

                var axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0];
                var axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1];
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested(); // check for a stop
                if (!axis1AtTarget)
                {
                    _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]); //move to target RA / Az
                }
                token.ThrowIfCancellationRequested(); // check for a stop
                if (!axis2AtTarget)
                {
                    _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]); //move to target Dec / Alt
                }

                // track movement until axes are stopped
                var stopwatch1 = Stopwatch.StartNew();

                var axis1Stopped = false;
                var axis2Stopped = false;

                while (stopwatch1.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested(); // check for a stop
                    if (!axis1Stopped)
                    {
                        var status1 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
                        var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status1).Result;
                        axis1Stopped = axis1Status.Stopped;
                    }
                    Thread.Sleep(20);
                    token.ThrowIfCancellationRequested(); // check for a stop
                    if (!axis2Stopped)
                    {
                        var status2 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
                        var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status2).Result;
                        axis2Stopped = axis2Status.Stopped;
                    }
                    if (axis1Stopped && axis2Stopped) { break; }
                }
                stopwatch1.Stop();
                deltaTime = stopwatch1.Elapsed.Milliseconds * milliSeconds;

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{Utilities.DegreesToDMS(DeclinationXForm, " ", ":", "", 2)}|Delta|({deltaDegree[0]}, {deltaDegree[1]})|Seconds|{stopwatch1.Elapsed.TotalSeconds}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return returnCode;
        }

        /// <summary>
        /// Performs a precision slew of axes to pulse target defined by RaDec predictor
        /// </summary>
        /// <param name="token"></param>
        private static void SimPulseGoto(CancellationToken token)
        {
            var maxTries = 0;
            double[] deltaDegree = { 0.0, 0.0 };
            var axis1AtTarget = false;
            var axis2AtTarget = false;
            double[] gotoPrecision = { ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) };
            long loopTime = 75; // 75mS for simulator slew
            try
            {
                while (true)
                {
                    if (maxTries > 5) { break; }
                    maxTries++;
                    double[] simTarget = { 0.0, 0.0 };

                    // convert target to axis for Ra / Dec slew and calculate tracking rates
                    if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        var nextTime = HiResDateTime.UtcNow.AddMilliseconds(loopTime);
                        // get predicted RA and Dec at update time
                        var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                        // convert to internal Ra and Dec
                        var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                        // convert to axes for Alt Az slew
                        simTarget = MapSlewTargetToAxes(new[] { internalRaDec.X, internalRaDec.Y }, SlewType.SlewRaDec);
                    }

                    // Calculate error
                    var rawPositions = GetRawDegrees();
                    if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1])) { break; }
                    deltaDegree[0] = Range.Range180(simTarget[0] - rawPositions[0]);
                    deltaDegree[1] = Range.Range180(simTarget[1] - rawPositions[1]);

                    axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                    axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                    if (axis1AtTarget && axis2AtTarget) { break; }
                    if (!axis1AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        object _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]); //move to target RA / Az
                    }
                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        object _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]); //move to target Dec / Alt
                    }

                    // track movement until axes are stopped
                    var stopwatch1 = Stopwatch.StartNew();

                    var axis1Stopped = false;
                    var axis2Stopped = false;

                    while (stopwatch1.Elapsed.TotalMilliseconds < 500)
                    {
                        token.ThrowIfCancellationRequested();
                        Thread.Sleep(100);
                        if (!axis1Stopped)
                        {
                            var status1 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
                            var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status1).Result;
                            axis1Stopped = axis1Status.Stopped;
                        }
                        Thread.Sleep(100);
                        if (!axis2Stopped)
                        {
                            var status2 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
                            var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status2).Result;
                            axis2Stopped = axis2Status.Stopped;
                        }
                        if (axis1Stopped && axis2Stopped) { break; }
                    }
                    stopwatch1.Stop();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Creates tasks that are put in the MountQueue
        /// </summary>
        /// <param name="taskName"></param>
        public static void SimTasks(MountTaskName taskName)
        {
            if (!IsMountRunning) return;

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

            switch (SkySettings.Mount)
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
                            CanAdvancedCmdSupport = false;
                            break;
                        case MountTaskName.CanPpec:
                            CanPPec = false;
                            break;
                        case MountTaskName.CanPolarLed:
                            CanPolarLed = false;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeCmdA = new GetHomeSensorCapability(MountQueue.NewId);
                            bool.TryParse(Convert.ToString(MountQueue.GetCommandResult(canHomeCmdA).Result), out bool hasHome);
                            CanHomeSensor = hasHome;
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
                            _ = new CmdAxisStop(0, Axis.Axis1);
                            _ = new CmdAxisStop(0, Axis.Axis2);
                            break;
                        case MountTaskName.InstantStopAxes:
                            break;
                        case MountTaskName.SetSouthernHemisphere:
                            break;
                        case MountTaskName.SyncAxes:
                            var sync = Axes.AxesAppToMount(new[] { _appAxes.X, _appAxes.Y });
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, sync[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, sync[1]);
                            break;
                        case MountTaskName.SyncTarget:
                            // Convert to internal Ra / Dec
                            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec);
                            // convert target to axis for Ra / Dec sync
                            var targetR = Axes.RaDecToAxesXy(new[] { a.X, a.Y });
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, targetR[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, targetR[1]);
                            break;
                        case MountTaskName.SyncAltAz:
                            var targetA = new[] { _altAzSync.Y, _altAzSync.X };
                            // convert target to axis for Az / Alt sync
                            targetA = Axes.AzAltToAxesXy(targetA);
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, targetA[0]);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, targetA[1]);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new CmdSetMonitorPulse(0, MonitorPulse);
                            break;
                        case MountTaskName.Pec:
                            break;
                        case MountTaskName.PecTraining:
                            break;
                        case MountTaskName.Capabilities:
                            Capabilities = @"N/A";
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            break;
                        case MountTaskName.SetSnapPort1:
                            _ = new CmdSnapPort(0, 1, SnapPort1);
                            SnapPort1Result = false;
                            break;
                        case MountTaskName.SetSnapPort2:
                            _ = new CmdSnapPort(0, 2, SnapPort2);
                            SnapPort2Result = true;
                            break;
                        case MountTaskName.MountName:
                            var mountName = new CmdMountName(MountQueue.NewId);
                            MountName = (string)MountQueue.GetCommandResult(mountName).Result;
                            break;
                        case MountTaskName.GetAxisVersions:
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            break;
                        case MountTaskName.MountVersion:
                            var mountVersion = new CmdMountVersion(MountQueue.NewId);
                            MountVersion = (string)MountQueue.GetCommandResult(mountVersion).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var spr = new CmdSpr(MountQueue.NewId);
                            var sprnum = (long)MountQueue.GetCommandResult(spr).Result;
                            StepsPerRevolution = new[] { sprnum, sprnum };
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var spw = new CmdSpw(MountQueue.NewId);
                            var spwnum = (double)MountQueue.GetCommandResult(spw).Result;
                            StepsWormPerRevolution = new[] { spwnum, spwnum };
                            break;
                        case MountTaskName.SetHomePositions:
                            _ = new CmdAxisToDegrees(0, Axis.Axis1, _homeAxes.X);
                            _ = new CmdAxisToDegrees(0, Axis.Axis2, _homeAxes.Y);
                            break;
                        case MountTaskName.GetFactorStep:
                            var factorStep = new CmdFactorSteps(MountQueue.NewId);
                            FactorStep[0] = (double)MountQueue.GetCommandResult(factorStep).Result;
                            FactorStep[1] = FactorStep[0];
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
        // Complete - no Phase 3 additions planned

        private static Vector _trackingOffsetRate;

        /// <summary>
        /// Custom Tracking Offset for RA calculate into arc seconds per sec
        /// </summary>
        public static double TrackingOffsetRaRate
        {
            get => _trackingOffsetRate.X;
            private set => _trackingOffsetRate.X = value;
        }

        /// <summary>
        /// Custom Tracking Offset for Dec calculate into arc seconds per sec
        /// </summary>
        public static double TrackingOffsetDecRate
        {
            get => _trackingOffsetRate.Y;
            private set => _trackingOffsetRate.Y = value;
        }

        /// <summary>
        /// Adjust tracking rate for Custom Mount Gearing Offset settings
        /// </summary>
        /// <returns>difference in rates</returns>
        private static void CalcCustomTrackingOffset()
        {
            _trackingOffsetRate = new Vector(0.0, 0.0);

            //calculate mount sidereal :I, add offset to :I, Calculate new rate, Add rate difference to rate
            if (SkySettings.Mount != MountType.SkyWatcher) { return; } //only use for sky watcher mounts

            if (SkySettings.CustomGearing == false) { return; }

            var ratioFactor = (double)StepsTimeFreq[0] / StepsPerRevolution[0] * 1296000.0;  //generic factor for calc
            var siderealI = ratioFactor / SiderealRate;
            siderealI += SkySettings.CustomRaTrackingOffset;  //calc :I and add offset
            var newRate = ratioFactor / siderealI; //calc new rate from offset
            TrackingOffsetRaRate = SiderealRate - newRate;

            ratioFactor = (double)StepsTimeFreq[1] / StepsPerRevolution[1] * 1296000.0;  //generic factor for calc
            siderealI = ratioFactor / SiderealRate;
            siderealI += SkySettings.CustomDecTrackingOffset;  //calc :I and add offset
            newRate = ratioFactor / siderealI; //calc new rate from offset
            TrackingOffsetDecRate = SiderealRate - newRate;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{TrackingOffsetRaRate}|{TrackingOffsetDecRate}" };
            MonitorLog.LogToMonitor(monitorItem);

        }

        // used to combine multiple sources for a single slew rate
        // include tracking, hand controller, etc..
        private static Vector _skyHcRate;
        private static Vector _skyTrackingRate;
        private static readonly int[] SkyTrackingOffset = { 0, 0 }; // Store for custom mount :I offset

        /// <summary>
        /// combines multiple Ra and Dec rates for a single slew rate
        /// </summary>
        /// <returns></returns>
        private static Vector SkyGetRate()
        {
            var change = new Vector();

            change += _skyTrackingRate; // Tracking
            change += _skyHcRate; // Hand controller
            // Primary axis
            change.X += RateMovePrimaryAxis;
            change.X += SkySettings.AlignmentMode != AlignmentMode.AltAz ? GetRaRateDirection(RateRa) : 0;
            // Secondary axis
            change.Y += RateMoveSecondaryAxis;
            change.Y += SkySettings.AlignmentMode != AlignmentMode.AltAz ? GetDecRateDirection(RateDec) : 0;

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
        /// SkyWatcher GOTO slew
        /// </summary>
        /// <returns></returns>
        private static int SkyGoTo(double[] target, bool trackingState, SlewType slewType, CancellationToken token)
        {
            const int success = 0;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"from|{ActualAxisX}|{ActualAxisY}|to|{target[0]}|{target[1]}|tracking|{trackingState}|slewing|{slewType}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            token.ThrowIfCancellationRequested(); // check for a stop

            var skyTarget = MapSlewTargetToAxes(target, slewType); // map target to axes
            // var skyTarget = AxesX.SelectPosition(target, slewType, new[] { ActualAxisX, ActualAxisY }, SkyServer.FlipOnNextGoto);
            const int timer = 240; // stop goto after timer
            var stopwatch = Stopwatch.StartNew();

            SkyTasks(MountTaskName.StopAxes);

            #region First Slew
            token.ThrowIfCancellationRequested(); // check for a stop
            // time could be off a bit may need to deal with each axis separate
            _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);
            _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);

            while (stopwatch.Elapsed.TotalSeconds <= timer)
            {
                Thread.Sleep(50);
                token.ThrowIfCancellationRequested(); // check for a stop

                var statusx = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
                var x = SkyQueue.GetCommandResult(statusx);
                var axis1Stopped = Convert.ToBoolean(x.Result);

                Thread.Sleep(50);
                token.ThrowIfCancellationRequested(); // check for a stop

                var statusy = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis2);
                var y = SkyQueue.GetCommandResult(statusy);
                var axis2Stopped = Convert.ToBoolean(y.Result);

                if (!axis1Stopped || !axis2Stopped) { continue; }

                if (SlewSettleTime > 0)
                {
                    Tasks.DelayHandler(TimeSpan.FromSeconds(SlewSettleTime).Milliseconds);// post-slew settling time
                }
                break;
            }
            stopwatch.Stop();
            AxesStopValidate();
            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Current|{Utilities.HoursToHMS(RightAscensionXForm, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(DeclinationXForm, " ", ":", "", 2)}|Seconds|{stopwatch.Elapsed.TotalSeconds}|Target|{target[0]}|{target[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            #endregion

            #region Final precision slew
            token.ThrowIfCancellationRequested(); // check for a stop
            if (stopwatch.Elapsed.TotalSeconds <= timer)
                Task.Run(() => SkyPrecisionGoto(target, slewType, token)).Wait();
            #endregion

            SkyTasks(MountTaskName.StopAxes); //make sure all axes are stopped
            return success;
        }

        /// <summary>
        /// Performs a final precision slew of the axes to target if necessary.
        /// On entry both axes are stopped from SkyGoTo
        /// </summary>
        /// <param name="target"></param>
        /// <param name="slewType"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private static int SkyPrecisionGoto(double[] target, SlewType slewType, CancellationToken token)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"from|({ActualAxisX},{ActualAxisY})|to|({target[0]},{target[1]})"
            };
            MonitorLog.LogToMonitor(monitorItem);

            const int returnCode = 0;
            var maxtries = 0;
            double[] deltaDegree = { 0.0, 0.0 };
            var axis1AtTarget = false;
            var axis2AtTarget = false;

            // double[] gotoPrecision = { ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) };
            double[] gotoPrecision = { SkySettings.GotoPrecision, SkySettings.GotoPrecision };
            long loopTime = 800;
            while (true)
            {
                token.ThrowIfCancellationRequested(); // check for a stop
                // start loop timer
                var loopTimer = Stopwatch.StartNew();
                // Update mount position
                MountPositionUpdated = false;
                UpdateSteps();
                while (!MountPositionUpdated) Thread.Sleep(10);
                // Check for maxtries or no change and exit
                if (maxtries >= 5) { break; }
                maxtries++;

                if (SkySettings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
                {
                    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(loopTime);
                    // get predicted RA and Dec at update time
                    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                    // convert to internal Ra and Dec
                    var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                    target = new[] { internalRaDec.X, internalRaDec.Y };
                }
                var skyTarget = MapSlewTargetToAxes(target, slewType); // map target to axes
                //var skyTarget = AxesX.SelectPosition(target, slewType, new[] { ActualAxisX, ActualAxisY }, SkyServer.FlipOnNextGoto);

                // Calculate error
                var rawPositions = GetRawDegrees();
                deltaDegree[0] = Range.Range180((skyTarget[0] - rawPositions[0]));
                deltaDegree[1] = Range.Range180(skyTarget[1] - rawPositions[1]);

                axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                if (axis1AtTarget && axis2AtTarget) { break; }

                token.ThrowIfCancellationRequested(); // check for a stop
                if (!axis1AtTarget)
                {
                    skyTarget[0] += 0.25 * deltaDegree[0];
                    _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]); //move to target RA / Az
                }
                var axis1Done = axis1AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    if (token.IsCancellationRequested) { break; } // check for a stop
                    if (!axis1Done)
                    {
                        var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
                        axis1Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status1).Result);
                    }
                    if (axis1Done) { break; }
                }

                if (!axis2AtTarget)
                {
                    skyTarget[1] += 0.1 * deltaDegree[1];
                    token.ThrowIfCancellationRequested(); // check for a stop
                    _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]); //move to target Dec / Alt
                }
                var axis2Done = axis2AtTarget;
                while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested(); // check for a stop
                    if (!axis2Done)
                    {
                        var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis2);
                        axis2Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status2).Result);
                    }
                    if (axis2Done) { break; }
                }
                loopTimer.Stop();
                loopTime = loopTimer.ElapsedMilliseconds;

                monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{Utilities.HoursToHMS(RightAscensionXForm, "h ", ":", "", 2)}|" +
                        $"{Utilities.DegreesToDMS(DeclinationXForm, " ", ":", "", 2)}" +
                        $"|Delta|{deltaDegree[0]}|{deltaDegree[1]}" +
                        $"|Seconds|{loopTimer.Elapsed.TotalSeconds}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return returnCode;
        }

        /// <summary>
        /// Performs a precision slew of axes to pulse target defined by RaDec predictor
        /// </summary>
        /// <param name="token"></param>
        private static void SkyPulseGoto(CancellationToken token)
        {
            var maxTries = 0;
            double[] deltaDegree = { 0.0, 0.0 };
            var axis1AtTarget = false;
            var axis2AtTarget = false;

            // double[] gotoPrecision = { ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) };
            double[] gotoPrecision = { SkySettings.GotoPrecision, SkySettings.GotoPrecision };
            long loopTime = 400;
            try
            {
                while (true)
                {
                    // start loop timer
                    var loopTimer = Stopwatch.StartNew();
                    // Update mount position
                    MountPositionUpdated = false;
                    UpdateSteps();
                    while (!MountPositionUpdated) Thread.Sleep(10);
                    // Check for maxtries or no change and exit
                    if (maxTries >= 5) { break; }
                    maxTries++;
                    double[] skyTarget = { 0.0, 0.0 };

                    // convert target to axis for Ra / Dec slew and calculate tracking rates
                    if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                    {
                        var nextTime = HiResDateTime.UtcNow.AddMilliseconds(loopTime);
                        // get predicted RA and Dec at update time
                        var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
                        // convert to internal Ra and Dec
                        var internalRaDec = Transforms.CoordTypeToInternal(predictorRaDec[0], predictorRaDec[1]);
                        // convert to axes for Alt Az slew
                        skyTarget = MapSlewTargetToAxes(new[] { internalRaDec.X, internalRaDec.Y }, SlewType.SlewRaDec);
                    }
                    // Calculate error
                    var rawPositions = GetRawDegrees();
                    if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1])) { break; }
                    deltaDegree[0] = skyTarget[0] - rawPositions[0];
                    deltaDegree[1] = skyTarget[1] - rawPositions[1];

                    axis1AtTarget = Math.Abs(deltaDegree[0]) < gotoPrecision[0] || axis1AtTarget;
                    axis2AtTarget = Math.Abs(deltaDegree[1]) < gotoPrecision[1] || axis2AtTarget;
                    if (axis1AtTarget && axis2AtTarget) { break; }

                    if (!axis1AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        object _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]); //move to target RA / Az
                    }
                    var axis1Done = axis1AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        if (SlewState == SlewType.SlewNone) { break; }

                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis1Done)
                        {
                            var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
                            axis1Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status1).Result);
                        }
                        if (axis1Done) { break; }
                    }

                    if (!axis2AtTarget)
                    {
                        token.ThrowIfCancellationRequested();
                        object _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]); //move to target Dec / Alt // no 0.1*deltaDegree[0]
                    }
                    var axis2Done = axis2AtTarget;
                    while (loopTimer.Elapsed.TotalMilliseconds < 3000)
                    {
                        if (SlewState == SlewType.SlewNone) { break; }

                        Thread.Sleep(30);
                        token.ThrowIfCancellationRequested();

                        if (!axis2Done)
                        {
                            var status2 = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis2);
                            axis2Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status2).Result);
                        }
                        if (axis2Done) { break; }
                    }
                    loopTimer.Stop();
                    loopTime = loopTimer.ElapsedMilliseconds;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Creates tasks that are put in the SkyQueue
        /// </summary>
        /// <param name="taskName"></param>
        public static void SkyTasks(MountTaskName taskName)
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
                Message = $"{taskName}"
            };

            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    switch (taskName)
                    {
                        case MountTaskName.AllowAdvancedCommandSet:
                            _ = new SkyAllowAdvancedCommandSet(0, SkySettings.AllowAdvancedCommandSet);
                            break;
                        case MountTaskName.AlternatingPpec:
                            _ = new SkySetAlternatingPPec(0, SkySettings.AlternatingPPec);
                            break;
                        case MountTaskName.DecPulseToGoTo:
                            _ = new SkySetDecPulseToGoTo(0, SkySettings.DecPulseToGoTo);
                            break;
                        case MountTaskName.CanAdvancedCmdSupport:
                            var skyCanAdvanced = new SkyGetAdvancedCmdSupport(SkyQueue.NewId);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(skyCanAdvanced).Result), out bool pAdvancedResult);
                            CanAdvancedCmdSupport = pAdvancedResult;
                            break;
                        case MountTaskName.CanPpec:
                            var skyMountCanPpec = new SkyCanPPec(SkyQueue.NewId);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(skyMountCanPpec).Result), out bool pPecResult);
                            CanPPec = pPecResult;
                            break;
                        case MountTaskName.CanPolarLed:
                            var skyCanPolarLed = new SkyCanPolarLed(SkyQueue.NewId);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(skyCanPolarLed).Result), out bool polarLedResult);
                            CanPolarLed = polarLedResult;
                            break;
                        case MountTaskName.CanHomeSensor:
                            var canHomeSky = new SkyCanHomeSensors(SkyQueue.NewId);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(canHomeSky).Result), out bool homeSensorResult);
                            CanHomeSensor = homeSensorResult;
                            break;
                        case MountTaskName.Capabilities:
                            var skyCap = new SkyGetCapabilities(SkyQueue.NewId);
                            Capabilities = (string)SkyQueue.GetCommandResult(skyCap).Result;
                            break;
                        case MountTaskName.Encoders:
                            _ = new SkySetEncoder(0, Axis.Axis1, SkySettings.Encoders);
                            _ = new SkySetEncoder(0, Axis.Axis2, SkySettings.Encoders);
                            break;
                        case MountTaskName.FullCurrent:
                            _ = new SkySetFullCurrent(0, Axis.Axis1, SkySettings.FullCurrent);
                            _ = new SkySetFullCurrent(0, Axis.Axis2, SkySettings.FullCurrent);
                            break;
                        case MountTaskName.GetFactorStep:
                            var skyFactor = new SkyGetFactorStepToRad(SkyQueue.NewId);
                            FactorStep = (double[])SkyQueue.GetCommandResult(skyFactor).Result;
                            break;
                        case MountTaskName.LoadDefaults:
                            _ = new SkyLoadDefaultMountSettings(0);
                            break;
                        case MountTaskName.InstantStopAxes:
                            _ = new SkyAxisStopInstant(0, Axis.Axis1);
                            _ = new SkyAxisStopInstant(0, Axis.Axis2);
                            break;
                        case MountTaskName.MinPulseRa:
                            _ = new SkySetMinPulseDuration(0, Axis.Axis1, SkySettings.MinPulseRa);
                            break;
                        case MountTaskName.MinPulseDec:
                            _ = new SkySetMinPulseDuration(0, Axis.Axis2, SkySettings.MinPulseDec);
                            break;
                        case MountTaskName.MonitorPulse:
                            _ = new SkySetMonitorPulse(0, MonitorPulse);
                            break;
                        case MountTaskName.PecTraining:
                            _ = new SkySetPPecTrain(0, Axis.Axis1, PecTraining);
                            break;
                        case MountTaskName.Pec:
                            var ppeOcn = new SkySetPPec(SkyQueue.NewId, Axis.Axis1, SkySettings.PPecOn);
                            var pPecOnStr = (string)SkyQueue.GetCommandResult(ppeOcn).Result;
                            if (string.IsNullOrEmpty(pPecOnStr))
                            {
                                SkySettings.PPecOn = false;
                                break;
                            }
                            if (pPecOnStr.Contains("!")) { SkySettings.PPecOn = false; }
                            break;
                        case MountTaskName.PolarLedLevel:
                            if (SkySettings.PolarLedLevel < 0 || SkySettings.PolarLedLevel > 255) { return; }
                            _ = new SkySetPolarLedLevel(0, Axis.Axis1, SkySettings.PolarLedLevel);
                            break;
                        case MountTaskName.StopAxes:
                            _ = new SkyAxisStop(0, Axis.Axis1);
                            _ = new SkyAxisStop(0, Axis.Axis2);
                            break;
                        case MountTaskName.SetSt4Guiderate:
                            _ = new SkySetSt4GuideRate(0, SkySettings.St4GuideRate);
                            break;
                        case MountTaskName.SetSouthernHemisphere:
                            _ = new SkySetSouthernHemisphere(SkyQueue.NewId, SouthernHemisphere);
                            break;
                        case MountTaskName.SetSnapPort1:
                            var sp1 = new SkySetSnapPort(SkyQueue.NewId, 1, SnapPort1);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(sp1).Result), out bool port1Result);
                            SnapPort1Result = port1Result;
                            break;
                        case MountTaskName.SetSnapPort2:
                            var sp2 = new SkySetSnapPort(SkyQueue.NewId, 2, SnapPort2);
                            bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(sp2).Result), out bool port2Result);
                            SnapPort2Result = port2Result;
                            break;
                        case MountTaskName.SyncAxes:
                            var sync = Axes.AxesAppToMount(new[] { _appAxes.X, _appAxes.Y });
                            _ = new SkySyncAxis(0, Axis.Axis1, sync[0]);
                            _ = new SkySyncAxis(0, Axis.Axis2, sync[1]);
                            monitorItem.Message += $",{_appAxes.X}|{_appAxes.Y}|{sync[0]}|{sync[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncTarget:
                            // convert target to internal Ra / Dec
                            var a = Transforms.CoordTypeToInternal(TargetRa, TargetDec);
                            // convert target to axis for Ra / Dec sync
                            var targetR = Axes.RaDecToAxesXy(new[] { a.X, a.Y });
                            _ = new SkySyncAxis(0, Axis.Axis1, targetR[0]);
                            _ = new SkySyncAxis(0, Axis.Axis2, targetR[1]);
                            monitorItem.Message += $",{Utilities.HoursToHMS(a.X, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(a.Y, " ", ":", "", 2)}|{targetR[0]}|{targetR[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.SyncAltAz:
                            var targetA = new[] { _altAzSync.Y, _altAzSync.X };
                            // convert target to axis for Az / Alt sync
                            targetA = Axes.AzAltToAxesXy(targetA);
                            _ = new SkySyncAxis(0, Axis.Axis1, targetA[0]);
                            _ = new SkySyncAxis(0, Axis.Axis2, targetA[1]);
                            monitorItem.Message += $",{_altAzSync.Y}|{_altAzSync.X}|{targetA[0]}|{targetA[1]}";
                            MonitorLog.LogToMonitor(monitorItem);
                            break;
                        case MountTaskName.GetAxisVersions:
                            var skyAxisVersions = new SkyGetAxisStringVersions(SkyQueue.NewId);
                            // Not used atm
                            _ = (long[])SkyQueue.GetCommandResult(skyAxisVersions).Result;
                            break;
                        case MountTaskName.GetAxisStrVersions:
                            var skyAxisStrVersions = new SkyGetAxisStringVersions(SkyQueue.NewId);
                            // Not used atm
                            _ = (string)SkyQueue.GetCommandResult(skyAxisStrVersions).Result;
                            break;
                        case MountTaskName.MountName:
                            var skyMountType = new SkyMountType(SkyQueue.NewId);
                            MountName = (string)SkyQueue.GetCommandResult(skyMountType).Result;
                            break;
                        case MountTaskName.MountVersion:
                            var skyMountVersion = new SkyMountVersion(SkyQueue.NewId);
                            MountVersion = (string)SkyQueue.GetCommandResult(skyMountVersion).Result;
                            break;
                        case MountTaskName.StepsPerRevolution:
                            var skyMountRevolutions = new SkyGetStepsPerRevolution(SkyQueue.NewId);
                            StepsPerRevolution = (long[])SkyQueue.GetCommandResult(skyMountRevolutions).Result;
                            break;
                        case MountTaskName.StepsWormPerRevolution:
                            var skyWormRevolutions1 = new SkyGetPecPeriod(SkyQueue.NewId, Axis.Axis1);
                            StepsWormPerRevolution[0] = (double)SkyQueue.GetCommandResult(skyWormRevolutions1).Result;
                            var skyWormRevolutions2 = new SkyGetPecPeriod(SkyQueue.NewId, Axis.Axis2);
                            StepsWormPerRevolution[1] = (double)SkyQueue.GetCommandResult(skyWormRevolutions2).Result;
                            break;
                        case MountTaskName.StepTimeFreq:
                            var skyStepTimeFreq = new SkyGetStepTimeFreq(SkyQueue.NewId);
                            StepsTimeFreq = (long[])SkyQueue.GetCommandResult(skyStepTimeFreq).Result;
                            break;
                        case MountTaskName.SetHomePositions:
                            _ = new SkySetAxisPosition(0, Axis.Axis1, _homeAxes.X);
                            _ = new SkySetAxisPosition(0, Axis.Axis2, _homeAxes.Y);
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
        /// Sets speeds for hand controller and slews in simulator
        /// </summary>
        internal static void SetSlewRates(double maxRate)
        {
            // Sky Speeds
            _slewSpeedOne = Math.Round(maxRate * 0.0034, 3);
            _slewSpeedTwo = Math.Round(maxRate * 0.0068, 3);
            _slewSpeedThree = Math.Round(maxRate * 0.047, 3);
            _slewSpeedFour = Math.Round(maxRate * 0.068, 3);
            _slewSpeedFive = Math.Round(maxRate * 0.2, 3);
            _slewSpeedSix = Math.Round(maxRate * 0.4, 3);
            _slewSpeedSeven = Math.Round(maxRate * 0.8, 3);
            SlewSpeedEight = Math.Round(maxRate * 1.0, 3);

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message =
                    $"{_slewSpeedOne}|{_slewSpeedTwo}|{_slewSpeedThree}|{_slewSpeedFour}|{_slewSpeedFive}|{_slewSpeedSix}|{_slewSpeedSeven}|{SlewSpeedEight}"
            };
            MonitorLog.LogToMonitor(monitorItem);

        }

        /// <summary>
        /// Makes sure the axes are at full stop
        /// </summary>
        /// <returns></returns>
        private static bool AxesStopValidate()
        {
            if (!IsMountRunning) { return true; }
            Stopwatch stopwatch;
            bool axis2Stopped;
            bool axis1Stopped;
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:

                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SimTasks(MountTaskName.StopAxes);
                        Thread.Sleep(100);
                        var statusX = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
                        var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusX).Result;
                        axis1Stopped = axis1Status.Stopped;

                        var statusY = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
                        var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusY).Result;
                        axis2Stopped = axis2Status.Stopped;

                        if (!axis1Stopped || !axis2Stopped) { continue; }
                        return true;
                    }
                    return false;
                case MountType.SkyWatcher:
                    stopwatch = Stopwatch.StartNew();
                    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
                    {
                        SkyTasks(MountTaskName.StopAxes);
                        Thread.Sleep(100);
                        var statusx = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
                        axis1Stopped = Convert.ToBoolean(SkyQueue.GetCommandResult(statusx).Result);

                        var statusy = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis2);
                        axis2Stopped = Convert.ToBoolean(SkyQueue.GetCommandResult(statusy).Result);

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
                if (Tracking) SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
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
                if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                {
                    // get tracking target at time now
                    var raDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                    // set predictor parameters ready for tracking
                    SkyPredictor.Set(raDec[0], raDec[1], _rateRaDec.X, _rateRaDec.Y);
                }
                SetTracking();
            }
            else
            {
                if (SkySettings.AlignmentMode == AlignmentMode.AltAz)
                {
                    // no tracking target so set to current position 
                    SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, _rateRaDec.X, _rateRaDec.Y);
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
            LimitStatus.AtLowerLimitAxisX = RawPositions[0] <= -SkySettings.AxisLimitX - oneArcSec;
            LimitStatus.AtUpperLimitAxisX = RawPositions[0] >= SkySettings.AxisLimitX + oneArcSec;
            var axisUpperLimitY = SkySettings.AxisUpperLimitY;
            var axisLowerLimitY = SkySettings.AxisLowerLimitY;
            if (SkySettings.AlignmentMode == AlignmentMode.Polar && PolarMode == PolarMode.Left)
            {
                axisLowerLimitY = 180 - SkySettings.AxisUpperLimitY;
                axisUpperLimitY = 180 - SkySettings.AxisLowerLimitY;
            }
            LimitStatus.AtLowerLimitAxisY = RawPositions[1] <= axisLowerLimitY - oneArcSec;
            LimitStatus.AtUpperLimitAxisY = RawPositions[1] >= axisUpperLimitY + oneArcSec;
        }

        /// <summary>
        /// pPEC Monitors the mount doing pPEC training
        /// </summary>
        private static void CheckPecTraining()
        {
            switch (SkySettings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    if (!PecTraining)
                    {
                        PecTrainInProgress = false;
                        return;
                    }

                    var ppectrain = new SkyIsPPecInTrainingOn(SkyQueue.NewId);
                    if (bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(ppectrain).Result), out bool bTrain))
                    {
                        PecTraining = bTrain;
                        PecTrainInProgress = bTrain;
                        if (!bTrain && PPecOn) //restart pec
                        {
                            PPecOn = false;
                            PPecOn = true;
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Pec Implement
        /// </summary>
        private static void PecCheck()
        {
            try
            {
                if (!PecOn || !Tracking || PecBinCount < 0 || IsSlewing || !PecShow) return;

                // Get axis position and range it
                var position = (int)Range.RangeDouble(Steps[0], Convert.ToDouble(StepsPerRevolution[0]));

                // calc current bin number
                var newBinNo = (int)((position + SkySettings.PecOffSet) / PecBinSteps);

                // Holder for new bin
                Tuple<double, int> pecBin = null;

                switch (SkySettings.PecMode)
                {
                    case PecMode.PecWorm:
                        newBinNo %= 100;
                        // No bin change return
                        if (PecBinNow?.Item1 == newBinNo) return;
                        if (PecWormMaster == null || PecWormMaster?.Count == 0) { return; }
                        PecWormMaster?.TryGetValue(newBinNo, out pecBin);
                        break;
                    case PecMode.Pec360:
                        // No bin change return
                        if (PecBinNow?.Item1 == newBinNo) return;
                        if (Pec360Master == null || Pec360Master?.Count == 0) { return; }
                        if (PecBinsSubs == null) { PecBinsSubs = new SortedList<int, Tuple<double, int>>(); }
                        var count = 0;
                        // search subs for new bin
                        while (PecBinsSubs.TryGetValue(newBinNo, out pecBin) == false && count < 2)
                        {
                            // stay within limits
                            var binStart = newBinNo - 100 < 0 ? 0 : newBinNo - 100;
                            var binEnd = newBinNo + 100 > StepsPerRevolution[0] - 1  //adjust for going over max?
                                ? (int)StepsPerRevolution[0] - 1
                                : newBinNo + 100;

                            // create sub list
                            PecBinsSubs.Clear();
                            for (var i = binStart; i <= binEnd; i++)
                            {
                                var mi = Tuple.Create(0.0, 0);
                                var masterResult = Pec360Master != null && Pec360Master.TryGetValue(i, out mi);
                                if (masterResult) PecBinsSubs.Add(i, mi);
                            }

                            count++;
                        }
                        if (PecBinsSubs.Count == 0)
                        {
                            throw new Exception($"Pec sub not found");
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                // bin must exist or throw error
                if (pecBin == null) { throw new Exception($"Pec not found"); }

                var binNew = new Tuple<int, double, int>(newBinNo, pecBin.Item1, pecBin.Item2);

                // assign new bin info
                PecBinNow = binNew;

                // Send to mount
                SetTracking();
            }
            catch (Exception ex)
            {
                PecOn = false;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Error,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{ex.Message}|{ex.StackTrace}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                MountError = ex;
            }

        }


        #endregion
    }
}