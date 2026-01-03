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

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.MountControl.Interfaces;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase 3.1: Instance-based mount controller that initially delegates to static SkyServer.
    /// This class will gradually take over implementation from static methods in Phase 3.2+.
    /// </summary>
    public class MountInstance : IMountController
    {
        private readonly string _id;
        private readonly SkySettingsInstance _settings;

        // Phase 3.2: Instance state fields (migrated from static)
        private bool _isMountRunning;
        private MediaTimer? _mediaTimer;
        private MediaTimer? _altAzTrackingTimer;
        private Vector _homeAxes;
        private Vector _appAxes;
        private Vector _targetRaDec;
        private Exception? _mountError;

        // Phase 3.2: Factor steps (conversion ratios)
        private readonly double[] _factorStep = new double[2];
        private readonly long[] _stepsPerRevolution = new long[2];

        // Phase 3.2: Tracking state
        private bool _tracking;
        private DriveRate _trackingRate = DriveRate.Sidereal;

        // Phase 3.2: SkyWatcher tracking rates
        private Vector _skyHcRate;
        private Vector _skyTrackingRate;

        // Phase 3.2: PEC fields
        private double[] _stepsWormPerRevolution = new double[2];
        private int[] _wormTeethCount = new int[2];
        private double _pecBinSteps;

        // Phase 3.2: Mount state
        private bool _atPark;

        // Phase 3.2: UpdateSteps fields
        private DateTime _lastUpdateStepsTime = DateTime.MinValue;
        private readonly object _lastUpdateLock = new object();

        // Phase 3.2: Slew speed fields
        private double _slewSpeedOne;
        private double _slewSpeedTwo;
        private double _slewSpeedThree;
        private double _slewSpeedFour;
        private double _slewSpeedFive;
        private double _slewSpeedSix;
        private double _slewSpeedSeven;
        private double _slewSpeedEight;

        // Phase 3.2: Guide rate field
        private Vector _guideRate;
        
        public MountInstance(string id, SkySettingsInstance settings)
        {
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Phase 3.2: Initialize instance state
            _isMountRunning = false;
            _appAxes = new Vector(double.NaN, double.NaN);
            _targetRaDec = new Vector(double.NaN, double.NaN);
            _skyHcRate = new Vector(0, 0);
            _skyTrackingRate = new Vector(0, 0);
            _atPark = false;

            LogMount($"MountInstance created: {_id}");
        }
        #region IMountController Implementation (Phase 3.1: Delegation)

        /// <summary>
        /// Gets the unique identifier for this mount instance
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Gets whether the mount is currently connected
        /// Phase 3.1: Delegates to static SkyServer
        /// </summary>
        public bool IsConnected => SkyServer.IsMountRunning;

        /// <summary>
        /// Gets whether the mount is currently running
        /// Phase 3.1: Delegates to static SkyServer
        /// </summary>
        public bool IsRunning => SkyServer.IsMountRunning;

        /// <summary>
        /// Connect to mount hardware
        /// Phase 3.2: Implemented (was stub in 3.1)
        /// </summary>
        public bool Connect()
        {
            LogMount($"Connect() called on instance {_id}");

            // Phase 3.2: Call the actual connect implementation
            // This will be MountConnect() migrated from static
            return MountConnect();
        }

        /// <summary>
        /// Sets up defaults after an established connection
        /// Phase 3.2: Migrated from SkyServer.MountConnect()
        /// </summary>
        private bool MountConnect()
        {
            _targetRaDec = new Vector(double.NaN, double.NaN);
            var positions = SkyServer.GetDefaultPositions_Internal();
            double[]? rawPositions = null;
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
                    SkyServer.SimTasks(MountTaskName.MountName);
                    SkyServer.SimTasks(MountTaskName.MountVersion);
                    SkyServer.SimTasks(MountTaskName.StepsPerRevolution);
                    SkyServer.SimTasks(MountTaskName.StepsWormPerRevolution);
                    SkyServer.SimTasks(MountTaskName.CanHomeSensor);
                    SkyServer.SimTasks(MountTaskName.GetFactorStep);
                    SkyServer.SimTasks(MountTaskName.Capabilities);

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / SkyServer.PecBinCount;

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
                        if (_atPark)
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
                    SkyServer.ConnectAlignmentModel();

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
                        SkyServer.SkyErrorHandler(init.Exception);
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
                        SkyServer.SkyTasks(MountTaskName.AllowAdvancedCommandSet);
                    }
                    SkyServer.SkyTasks(MountTaskName.LoadDefaults);
                    SkyServer.SkyTasks(MountTaskName.StepsPerRevolution);
                    SkyServer.SkyTasks(MountTaskName.StepsWormPerRevolution);
                    SkyServer.SkyTasks(MountTaskName.StopAxes);
                    SkyServer.SkyTasks(MountTaskName.Encoders);
                    SkyServer.SkyTasks(MountTaskName.FullCurrent);
                    SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate);
                    SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere);
                    SkyServer.SkyTasks(MountTaskName.MountName);
                    SkyServer.SkyTasks(MountTaskName.MountVersion);
                    SkyServer.SkyTasks(MountTaskName.StepTimeFreq);
                    SkyServer.SkyTasks(MountTaskName.CanPpec);
                    SkyServer.SkyTasks(MountTaskName.CanPolarLed);
                    SkyServer.SkyTasks(MountTaskName.PolarLedLevel);
                    SkyServer.SkyTasks(MountTaskName.CanHomeSensor);
                    SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo);
                    SkyServer.SkyTasks(MountTaskName.AlternatingPpec);
                    SkyServer.SkyTasks(MountTaskName.MinPulseDec);
                    SkyServer.SkyTasks(MountTaskName.MinPulseRa);
                    SkyServer.SkyTasks(MountTaskName.GetFactorStep);
                    SkyServer.SkyTasks(MountTaskName.Capabilities);
                    SkyServer.SkyTasks(MountTaskName.CanAdvancedCmdSupport);
                    if (SkyServer.CanPPec) SkyServer.SkyTasks(MountTaskName.Pec);

                    //CanHomeSensor = true; //test auto home

                    raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
                    decWormTeeth = (int)(_stepsPerRevolution[1] / _stepsWormPerRevolution[1]);
                    _wormTeethCount = new[] { raWormTeeth, decWormTeeth };
                    _pecBinSteps = _stepsPerRevolution[0] / (_wormTeethCount[0] * 1.0) / SkyServer.PecBinCount;

                    SkyServer.CalcCustomTrackingOffset();  //generates rates for the custom gearing offsets

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
                        if (_atPark)
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
                    SkyServer.ConnectAlignmentModel();

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            msg = positionsSet ? $"SetPositions|{positions[0]}|{positions[1]}" : $"PositionsNotSet";
            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = msg };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"MountAxes|{_appAxes.X}|{_appAxes.Y}|Actual|{SkyServer.ActualAxisX}|{SkyServer.ActualAxisY}" };
            MonitorLog.LogToMonitor(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"StepsPerRevolution|{SkyServer.StepsPerRevolution[0]}|{SkyServer.StepsPerRevolution[1]}" };
            MonitorLog.LogToMonitor(monitorItem);

            //Load Pec Files
            var pecmsg = string.Empty;
            SkyServer.PecOn = SkySettings.PecOn;
            if (File.Exists(SkySettings.PecWormFile))
            {
                SkyServer.LoadPecFile(SkySettings.PecWormFile);
                pecmsg += SkySettings.PecWormFile;
            }

            if (File.Exists(SkySettings.Pec360File))
            {
                SkyServer.LoadPecFile(SkySettings.Pec360File);
                pecmsg += ", " + SkySettings.PecWormFile;
            }

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Pec: {pecmsg}" };
            MonitorLog.LogToMonitor(monitorItem);

            try
            {
                // Get path to current version's appsettings.user.json file
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                // Get version from assembly (matches VersionedSettingsService logic)
                var infoVersionAttr = assembly
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as AssemblyInformationalVersionAttribute;

                var version = infoVersionAttr?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString()
                    ?? "1.0.0";

                // Remove build metadata (e.g., +commitHash)
                var plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                {
                    version = version.Substring(0, plusIndex);
                }

                var userSettingsPath = Path.Combine(appData, "GreenSwampAlpaca", version, "appsettings.user.json");
                var logDirectoryPath = GsFile.GetLogPath();

                if (File.Exists(userSettingsPath))
                {
                    // Copy the appsettings.user.json file to the log directory
                    var destinationPath = Path.Combine(logDirectoryPath, "appsettings.user.json");
                    File.Copy(userSettingsPath, destinationPath, true);

                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Copied appsettings.user.json to {logDirectoryPath}" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                else
                {
                    // Settings file doesn't exist yet - log info (it will be created later by the settings service)
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"appsettings.user.json not found at {userSettingsPath} - will be created on first settings save" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Cannot copy appsettings.user.json. {e.Message}" };
                MonitorLog.LogToMonitor(monitorItem);
            }

            _isMountRunning = true;
            return true;
        }
        
        /// <summary>
        /// Disconnect from mount hardware
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Disconnect()
        {
            LogMount($"Disconnect() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Disconnect_Stub();
        }

        /// <summary>
        /// Start mount operations
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Start()
        {
            LogMount($"Start() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Start_Stub();
        }

        /// <summary>
        /// Stop mount operations
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Stop()
        {
            LogMount($"Stop() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.Stop_Stub();
        }

        /// <summary>
        /// Reset mount to home position
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void Reset()
        {
            LogMount($"Reset() called on instance {_id}");

            // Phase 3.2: Call instance method directly
            MountReset();
        }
        /// <summary>
        /// Emergency stop - halt all motion immediately
        /// Phase 3.1: Delegates to static method
        /// </summary>
        public void EmergencyStop()
        {
            LogMount($"EmergencyStop() called on instance {_id}");
            
            // Phase 3.1: Delegate to static
            SkyServer.AbortSlewAsync(speak: false);
        }

        /// <summary>
        /// Get last error from mount
        /// Phase 3.1: Delegates to static property
        /// </summary>
        public Exception? GetLastError()
        {
            return SkyServer.MountError;
        }

        #endregion

        #region Phase 3.2: Position Methods (Migrated from static)

        /// <summary>
        /// Maps a slew target to the corresponding axes based on the specified slew type.
        /// Phase 3.2: Migrated from SkyServer.MapSlewTargetToAxes()
        /// </summary>
        /// <remarks>The mapping behavior depends on the specified slew type:
        /// - For SlewRaDec: target is converted to RA/Dec axes and synchronized
        /// - For SlewAltAz: target is converted to Alt/Az axes
        /// - For SlewPark, SlewHome, SlewMoveAxis: target is converted to mount-specific axes
        /// </remarks>
        /// <param name="target">Target coordinates to be mapped</param>
        /// <param name="slewType">Type of slew operation</param>
        /// <returns>Target coordinates mapped to appropriate axes</returns>        
        public double[] MapSlewTargetToAxes(double[] target, SlewType slewType)
        {
            // Convert target to axes based on slew type
            switch (slewType)
            {
                case SlewType.SlewRaDec:
                    // convert target to axis for Ra / Dec slew
                    target = Axes.RaDecToAxesXy(target);
                    // Convert to synced axes
                    target = SkyServer.GetSyncedAxes(target);
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
        /// Gets current converted positions from the mount in degrees
        /// Phase 3.2: Migrated from SkyServer.GetRawDegrees()
        /// </summary>
        internal double[]? GetRawDegrees()
        {
            var actualDegrees = new[] { double.NaN, double.NaN };
            if (!_isMountRunning) { return actualDegrees; }

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    actualDegrees = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    break;

                case MountType.SkyWatcher:
                    var skyPositions = new SkyGetPositionsInDegrees(SkyQueue.NewId);
                    actualDegrees = (double[])SkyQueue.GetCommandResult(skyPositions).Result;
                    if (!skyPositions.Successful || skyPositions.Exception != null)
                        return null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return actualDegrees;
        }

        /// <summary>
        /// Convert steps to degrees
        /// Phase 3.2: Migrated from SkyServer.ConvertStepsToDegrees()
        /// </summary>
        internal double ConvertStepsToDegrees(double steps, int axis)
        {
            double degrees;
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    degrees = steps / _factorStep[axis];
                    break;

                case MountType.SkyWatcher:
                    degrees = Principles.Units.Rad2Deg1(steps * _factorStep[axis]);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            return degrees;
        }

        /// <summary>
        /// Get steps from the mount
        /// Phase 3.2: Migrated from SkyServer.GetRawSteps()
        /// </summary>
        internal double[]? GetRawSteps()
        {
            var steps = new[] { double.NaN, double.NaN };
            if (!_isMountRunning) { return steps; }

            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    var simPositions = new CmdAxesDegrees(MountQueue.NewId);
                    steps = (double[])MountQueue.GetCommandResult(simPositions).Result;
                    steps[0] *= _factorStep[0];
                    steps[1] *= _factorStep[1];
                    break;

                case MountType.SkyWatcher:
                    var skySteps = new SkyGetSteps(SkyQueue.NewId);
                    steps = (double[])SkyQueue.GetCommandResult(skySteps).Result;
                    if (!skySteps.Successful || skySteps.Exception != null)
                        return null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return steps;
        }

        /// <summary>
        /// Gets current positions from the mount in steps for a specific axis
        /// Phase 3.2: Migrated from SkyServer.GetRawSteps(int axis)
        /// </summary>
        /// <param name="axis">Axis index (0 = RA/Az, 1 = Dec/Alt)</param>
        /// <returns>Position in steps, or null if not available</returns>
        internal double? GetRawSteps(int axis)
        {
            if (!_isMountRunning) { return null; }

            switch (_settings.Mount)
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

        /// <summary>
        /// Main get for the Steps
        /// Phase 3.2: Migrated from SkyServer.UpdateSteps()
        /// </summary>
        internal void UpdateSteps()
        {
            lock (_lastUpdateLock)
            {
                if (_isMountRunning || (_lastUpdateStepsTime.AddMilliseconds(100) < HiResDateTime.UtcNow))
                {
                    switch (_settings.Mount)
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

                    _lastUpdateStepsTime = HiResDateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Get home axes adjusted for angle offset
        /// Phase 3.2: Migrated from SkyServer.GetHomeAxes()
        /// </summary>
        /// <param name="xAxis">X axis position</param>
        /// <param name="yAxis">Y axis position</param>
        /// <returns>Home axes vector adjusted for alignment mode and hemisphere</returns>
        internal Vector GetHomeAxes(double xAxis, double yAxis)
        {
            var home = new[] { xAxis, yAxis };
            if (_settings.AlignmentMode != AlignmentMode.Polar)
            {
                home = Axes.AxesAppToMount(new[] { xAxis, yAxis });
            }
            else
            {
                var angleOffset = SkyServer.SouthernHemisphere ? 180.0 : 0.0;
                home[0] -= angleOffset;
                home = Axes.AzAltToAxesXy(home);
            }
            return new Vector(home[0], home[1]);
        }

        #endregion

        #region Phase 3.2: Core Operations (Migrated from static)

        /// <summary>
        /// Load default settings and slew rates
        /// Phase 3.2: Migrated from SkyServer.Defaults()
        /// </summary>
        internal void Defaults()
        {
            SkyServer.SlewSettleTime = 0;

            // Initialize FactorStep array (already initialized in constructor, but keep for compatibility)
            // _factorStep is already initialized as new double[2]

            // home axes
            _homeAxes = GetHomeAxes(_settings.HomeAxisX, _settings.HomeAxisY);

            // set the slew speeds, the longest distance is using the higher speed for longer
            _slewSpeedOne = Principles.Units.Deg2Rad1((int)_settings.HcSpeed * (15.0 / 3600)); //1x 15"/s
            _slewSpeedTwo = _slewSpeedOne * 2; //2x
            _slewSpeedThree = _slewSpeedOne * 8; //8x
            _slewSpeedFour = _slewSpeedOne * 16; //16x
            _slewSpeedFive = _slewSpeedOne * 32; //32x
            _slewSpeedSix = _slewSpeedOne * 64; //64x
            _slewSpeedSeven = _slewSpeedOne * 600; //600x
            _slewSpeedEight = _slewSpeedOne * 800; //800x

            var maxSlew = Principles.Units.Deg2Rad1(_settings.MaxSlewRate);
            SkyServer.SetSlewRates(maxSlew);

            // set the guiderates
            _guideRate = new Vector(_settings.GuideRateOffsetY, _settings.GuideRateOffsetX);
            SkyServer.SetGuideRates();
        }

        /// <summary>
        /// Reset mount to home position
        /// Phase 3.2: Migrated from SkyServer.MountReset()
        /// </summary>
        internal void MountReset()
        {
            // Phase A.6: Settings already loaded from JSON via bridge
            // Bridge keeps settings current - no need to reload from user.config
            // All 121 properties are synced bidirectionally by the bridge

            // Set home positions using current settings (already loaded)
            _homeAxes = GetHomeAxes(_settings.HomeAxisX, _settings.HomeAxisY);

            // Set axis positions
            _appAxes = new Vector(_homeAxes.X, _homeAxes.Y);
        }

        // Phase 3.2: Expose internal state for static facade backward compatibility
        internal Vector HomeAxes => _homeAxes;
        internal Vector AppAxes => _appAxes;
        
        #endregion

        #region Logging

        private void LogMount(string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = "MountInstance",
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = message
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch
            {
                // Fail silently if logging fails
            }
        }

        #endregion
    }
}
