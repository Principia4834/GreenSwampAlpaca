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
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Serial/UDP connection management for Mount.
    /// Handles port opening/closing, queue initialization, and connection lifecycle.
    /// </summary>
    public partial class Mount
    {
        #region Connection Lifecycle

        /// <summary>
        /// Adds or removes the given client ID from the connected-client set.
        /// On first connect, starts the mount hardware in a background task (ASCOM async pattern).
        /// Returns within 1 second per ASCOM specification. Connection completes when Connecting becomes false.
        /// On last disconnect, the hardware continues running until explicitly stopped.
        /// </summary>
        public void SetConnected(long id, bool value)
        {
            if (value)
            {
                if (_connectStates.IsEmpty)
                {
                    // Reset connection error so a repeated failure always triggers a new toast.
                    _lastConnectionError = null;
                    Connecting = true;
                }

                if (!IsMountRunning)
                {
                    // Phase 1: Start mount initialization in background to comply with ASCOM <1 second return requirement
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            MountStart();

                            // Hardware succeeded — add to connected set before clearing Connecting so the
                            // 200 ms TelescopeStateService tick always sees IsConnected = true on the same
                            // tick that Connecting transitions to false. This ensures the "Mount Connected"
                            // voice announcement fires on the correct state transition.
                            _connectStates.TryAdd(id, true);
                            _hasEverBeenConnected = true;
                            _lastConnectionError = null;
                            Connecting = false;

                            var completionItem = new MonitorEntry
                            {
                                Datetime = HiResDateTime.UtcNow,
                                Device = MonitorDevice.Server,
                                Category = MonitorCategory.Server,
                                Type = MonitorType.Information,
                                Method = nameof(SetConnected),
                                Thread = Environment.CurrentManagedThreadId,
                                Message = $"Connection complete|Mount:{Id}|LoopCount:{_loopCounter}"
                            };
                            LogMount(completionItem);
                        }
                        catch (Exception ex)
                        {
                            // TryAdd was never called — IsConnected is already false.
                            _lastConnectionError = ex.Message;

                            // MountErrorHandler calls MountStop() and stores _mountError.
                            MountErrorHandler(ex);

                            Connecting = false;

                            var errorItem = new MonitorEntry
                            {
                                Datetime = HiResDateTime.UtcNow,
                                Device = MonitorDevice.Server,
                                Category = MonitorCategory.Server,
                                Type = MonitorType.Error,
                                Method = nameof(SetConnected),
                                Thread = Environment.CurrentManagedThreadId,
                                Message = $"Connection failed|Mount:{Id}|Error:{ex.Message}"
                            };
                            LogMount(errorItem);
                        }
                    });

                    // Return immediately - connection proceeds in background
                    var startItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Add|{id}|StartingAsync|Connecting:{Connecting}"
                    };
                    LogMount(startItem);
                    return; // Early return - Connecting remains true until background task completes
                }

                // Mount already running — add this client immediately; no hardware init needed.
                _connectStates.TryAdd(id, true);
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Add|{id}|AlreadyRunning" };
                LogMount(monitorItem);
            }
            else
            {
                if (_connectStates.Count == 1) { Connecting = true; }
                var successfullyRemoved = _connectStates.TryRemove(id, out _);
                if (_connectStates.IsEmpty)
                {
                    MountStop();
                }
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Remove|{id}|{successfullyRemoved}" };
                LogMount(monitorItem);
            }
            Connecting = false;
        }

        /// <summary>
        /// Opens the serial or UDP port defined in settings. Equivalent to SkySystem.ConnectSerial = true.
        /// </summary>
        private void OpenSerial()
        {
            _serialError = null;
            try
            {
                _serial?.Dispose();
                _serial = null;
                _connectType = ConnectType.None;

                var readTimeout = TimeSpan.FromMilliseconds(Settings.ReadTimeout);
                if (Settings.Port.StartsWith("COM") ||              // Windows
                    Settings.Port.StartsWith("/dev/ttyUSB") ||      // Linux USB serial
                    Settings.Port.StartsWith("/dev/ttyS"))          // Linux serial
                {
                    var options = SerialOptions.DiscardNull
                        | (Settings.DtrEnable ? SerialOptions.DtrEnable : SerialOptions.None)
                        | (Settings.RtsEnable ? SerialOptions.RtsEnable : SerialOptions.None);

                    _serial = new GsSerialPort(
                        Settings.Port,
                        (int)Settings.BaudRate,
                        readTimeout,
                        Settings.HandShake,
                        Parity.None,
                        StopBits.One,
                        Settings.DataBits,
                        options);
                    _connectType = ConnectType.Com;
                }
                else
                {
                    var endpoint = CreateIpEndPoint(Settings.Port);
                    _serial = new SerialOverUdpPort(endpoint, readTimeout);
                    _connectType = ConnectType.Wifi;
                }
                _serial?.Open();
            }
            catch (Exception ex)
            {
                _serialError = ex;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{ex.Message}|{ex.InnerException?.Message}" };
                LogMount(monitorItem);
                _serial = null;
                _connectType = ConnectType.None;
            }
        }

        /// <summary>
        /// Closes and disposes the serial port. Equivalent to SkySystem.ConnectSerial = false.
        /// </summary>
        private void CloseSerial()
        {
            _serial?.Dispose();
            _serial = null;
            _connectType = ConnectType.None;
        }

        /// <summary>
        /// Parses a "host:port" string into an IPEndPoint. Handles IPv4 and IPv6.
        /// </summary>
        private static IPEndPoint CreateIpEndPoint(string endPoint)
        {
            var ep = endPoint.Split(':');
            if (ep.Length < 2) { throw new FormatException("Invalid endpoint format"); }
            IPAddress ip;
            if (ep.Length > 2)
            {
                if (!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip))
                { throw new FormatException("Invalid ip-address"); }
            }
            else
            {
                if (!IPAddress.TryParse(ep[0], out ip))
                { throw new FormatException("Invalid ip-address"); }
            }
            return !int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out var port)
                ? throw new FormatException("Invalid port")
                : new IPEndPoint(ip, port);
        }

        #endregion

        #region Mount Initialization and Shutdown

        /// <summary>
        /// Sets up defaults after an established connection.
        /// Migrated from SkyServer.MountConnect()
        /// </summary>
        public bool MountConnect()
        {
            _targetRaDec = new Vector(double.NaN, double.NaN);
            var positions = this.GetDefaultPositions();
            double[]? rawPositions = null;
            var counter = 0;
            bool positionsSet = false;
            MonitorEntry monitorItem;
            string msg;

            switch (Settings.Mount)
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


                    // Log instance values for verification
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{Id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{CanPPec}|MountName:{MountName}"
                    };
                    LogMount(monitorItem);

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            monitorItem = new MonitorEntry
                            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Counter exceeded:{positions[0]}|{positions[1]}" };
                            LogMount(monitorItem);
                            break;
                        }
                        counter++;

                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetRawDegrees:{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
                        LogMount(monitorItem);

                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (_atPark)
                        {
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                            _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis1, positions[0]);
                        _ = new CmdAxisToDegrees(SimQueue!.NewId, SimQueue, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    break;
                case MountType.SkyWatcher:
                    SkyHcRate = new Vector(0, 0);
                    SkyTrackingRate = new Vector(0, 0);

                    // create a command and put in queue to test connection
                    var init = new SkyGetMotorCardVersion(SkyQueue!.NewId, SkyQueue, Axis.Axis1);
                    _ = (string)SkyQueue.GetCommandResult(init).Result;
                    if (!init.Successful)
                    {
                        // Assign _mountError directly — MountErrorHandler will be called once
                        // by SetConnected()'s catch block, avoiding a double MountStop().
                        _mountError = init.Exception != null
                            ? new Exception($"CheckMount{Environment.NewLine}{init.Exception.Message}", init.Exception)
                            : new SkyServerException(ErrorCode.ErrSerialFailed, "No response from mount controller — check port and power");
                        return false;
                    }

                    // Warm Axis2 version cache so SkyTasks(MountVersion) can read both axes from cache.
                    // Axis version never changes at runtime; this is the only time it needs to be queried.
                    var initAxis2 = new SkyGetMotorCardVersion(SkyQueue.NewId, SkyQueue, Axis.Axis2);
                    _ = (string)SkyQueue.GetCommandResult(initAxis2).Result; var controllerVoltage = double.NaN;

                    try
                    {
                        var vs = new SkyGetControllerVoltage(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                        controllerVoltage = (double)SkyQueue.GetCommandResult(vs).Result;
                    }
                    catch { }
                    ControllerVoltage = controllerVoltage;
                    monitorItem = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Voltage|{controllerVoltage:F2} V"
                    };
                    LogMount(monitorItem);
                    // defaults
                    SkyTasks(MountTaskName.AllowAdvancedCommandSet);
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


                    // Log instance values for verification
                    var monitorItemSky = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"Mount:{Id}|StepsPerRev:{_stepsPerRevolution[0]},{_stepsPerRevolution[1]}|" +
                                  $"FactorStep:{_factorStep[0]:F10},{_factorStep[1]:F10}|" +
                                  $"WormSteps:{_stepsWormPerRevolution[0]:F2},{_stepsWormPerRevolution[1]:F2}|" +
                                  $"CanPPec:{CanPPec}|MountName:{MountName}"
                    };
                    LogMount(monitorItemSky);

                    //CanHomeSensor = true; //test auto home

                    this.CalcCustomTrackingOffset();

                    // Initialize slew speeds
                    this.SetSlewRates(Settings.MaxSlewRate);

                    //log current positions
                    var steps = GetRawSteps();
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"GetSteps:{steps[0]}|{steps[1]}" };
                    LogMount(monitorItem);

                    // checks if the mount is close enough to home position to set default position. If not use the positions from the mount
                    while (rawPositions == null)
                    {
                        if (counter > 5)
                        {
                            // All position reads failed — no mount is responding.
                            var error = new SkyServerException(ErrorCode.ErrMount,
                                "Mount not responding: failed to read axis positions after 5 retries");
                            MountErrorHandler(error);
                            return false;
                        }
                        counter++;

                        //get positions and log them
                        rawPositions = GetRawDegrees();
                        msg = rawPositions != null ? $"GetDegrees|{rawPositions[0]}|{rawPositions[1]}" : $"NULL";
                        monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
                        LogMount(monitorItem);

                        //if an error getting positions then stay in while loop and try again
                        if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
                        {
                            rawPositions = null;
                            continue;
                        }

                        //is mount parked, if so set to the default position
                        if (_atPark)
                        {
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis1, positions[0]);
                            _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis2, positions[1]);
                            positionsSet = true;
                            break;
                        }

                        //was mount powered and at 0,0  are both axes close to home?  if not then don't change current mount positions 
                        if (!rawPositions[0].IsBetween(-.1, .1) || !rawPositions[1].IsBetween(-.1, .1)) { continue; }

                        //Mount is close to home 0,0 so set the default position
                        _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis1, positions[0]);
                        _ = new SkySetAxisPosition(SkyQueue!.NewId, SkyQueue, Axis.Axis2, positions[1]);
                        positionsSet = true;

                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            msg = positionsSet ? $"SetPositions|{positions[0]}|{positions[1]}" : $"PositionsNotSet";
            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = msg };
            LogMount(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"MountAxes|{_appAxes.X}|{_appAxes.Y}|Actual|{_actualAxisX}|{_actualAxisY}" };
            LogMount(monitorItem);

            monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"StepsPerRevolution|{_stepsPerRevolution[0]}|{_stepsPerRevolution[1]}" };
            LogMount(monitorItem);

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

                var userSettingsPath = Path.Combine(appData, "GreenSwampAlpaca", version, "monitor.settings.user.json");
                var logDirectoryPath = GsFile.GetLogPath();

                if (File.Exists(userSettingsPath))
                {
                    // Copy the monitor.settings.user.json file to the log directory
                    var destinationPath = Path.Combine(logDirectoryPath, "monitor.settings.user.json");
                    File.Copy(userSettingsPath, destinationPath, true);

                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Copied monitor.settings.user.json to {logDirectoryPath}" };
                    LogMount(monitorItem);
                }
                else
                {
                    // Settings file doesn't exist yet - log info (it will be created later by the settings service)
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"monitor.settings.user.json not found at {userSettingsPath} - will be created on first settings save" };
                    LogMount(monitorItem);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Cannot copy appsettings.user.json. {e.Message}" };
                LogMount(monitorItem);
            }

            return true;
        }

        /// <summary>
        /// Start connection, queues, and events
        /// Migrated from SkyServer.MountStart()
        /// </summary>
        public void MountStart()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, DeviceNumber = DeviceNumber, Device = MonitorDevice.Server, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Settings.Mount}" };
            LogMount(monitorItem);

            // setup server defaults, stop auto-discovery, connect serial port, start queues
            Defaults();
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    Alpaca.Mount.Simulator.Settings.AutoHomeAxisX = (int)Settings.AutoHomeAxisX;
                    Alpaca.Mount.Simulator.Settings.AutoHomeAxisY = (int)Settings.AutoHomeAxisY;
                    var mqImpl = new GreenSwamp.Alpaca.Mount.Simulator.MountQueueImplementation();
                    mqImpl.DeviceNumber = DeviceNumber;
                    mqImpl.SetupCallbacks(
                        steps => ReceiveSteps(steps),
                        v => { IsPulseGuidingRa = v; },
                        v => { IsPulseGuidingDec = v; });
                    // Start the instance-owned simulator queue directly (no static facade)
                    mqImpl.Start();
                    SimQueue = mqImpl;
                    if (!mqImpl.IsRunning)
                    {
                        throw new Exception("Failed to start simulator queue");
                    }

                    break;
                case MountType.SkyWatcher:
                    // open serial port
                    CloseSerial();
                    OpenSerial();
                    if (_serial?.IsOpen != true)
                    {
                        throw new SkyServerException(ErrorCode.ErrSerialFailed,
                            $"Connection Failed: {_serialError}");
                    }
                    // Start up, pass custom mount gearing if needed
                    var custom360Steps = new[] { 0, 0 };
                    var customWormSteps = new[] { 0.0, 0.0 };
                    if (Settings.CustomGearing)
                    {
                        custom360Steps = [Settings.CustomRa360Steps, Settings.CustomDec360Steps];
                        customWormSteps = [(double)Settings.CustomRa360Steps / Settings.CustomRaWormTeeth, (double)Settings.CustomDec360Steps / Settings.CustomDecWormTeeth
                        ];
                    }

                    // Create queue for SkyWatcher.
                    var sqImpl = new GreenSwamp.Alpaca.Mount.SkyWatcher.SkyQueueImplementation();
                    sqImpl.DeviceNumber = DeviceNumber;
                    sqImpl.SetupCallbacks(
                        steps => ReceiveSteps(steps),
                        v => { IsPulseGuidingRa = v; },
                        v => { IsPulseGuidingDec = v; });
                    sqImpl.Start(_serial, custom360Steps, customWormSteps, this.OnLowVoltageEvent);
                    SkyQueue = sqImpl;
                    if (!sqImpl.IsRunning)
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
                // Start the tracking command processor (queue-based AltAz serialisation)
                _trackingProcessor = new TrackingCommandProcessor(this);
                _trackingProcessor.Start(CancellationToken.None);

                // start with a stop
                AxesStopValidate();

                // Event to get mount positions and update UI
                // Ensure CheckInterval is valid for MediaTimer (must be > 0)
                var checkInterval = Settings.CheckInterval > 0 ? Settings.CheckInterval : 2000;
                _mediaTimer = MediaTimerFactory.Create();
                _mediaTimer.Period = checkInterval;
                _mediaTimer.Resolution = 5;
                _mediaTimer.Tick += OnUpdateServerEvent;
                _mediaTimer.Start();
            }
            else
            {
                // MountConnect() set _mountError directly — throw it so SetConnected()'s catch
                // block calls MountErrorHandler() once and sets _lastConnectionError.
                throw _mountError
                    ?? new SkyServerException(ErrorCode.ErrMount, "Mount failed during initialisation");
            }
        }

        /// <summary>
        /// Stop queues and events
        /// Migrated from SkyServer.MountStop()
        /// </summary>
        public void MountStop()
        {
            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Settings.Mount}" };
            LogMount(monitorItem);

            // Stop all asynchronous operations
            TrackingMode = TrackingMode.Off;
            Tracking = false;
            _ctsGoTo?.Cancel();
            _ctsPulseGuideRa?.Cancel();
            _ctsPulseGuideDec?.Cancel();
            _ctsHcPulseGuide?.Cancel();
            // Complete the tracking command processor before stopping the timer
            _trackingProcessor?.StopAsync().GetAwaiter().GetResult();
            _trackingProcessor = null;
            // N6: Stop timers BEFORE AxesStopValidate
            //     after the stop commands, which would leave the motor running after Disconnect.
            if (_altAzTrackingTimer != null) { _altAzTrackingTimer.Tick -= AltAzTrackingTimerTick; } // J4 / N6: moved before AxesStopValidate
            _altAzTrackingTimer?.Stop();
            _altAzTrackingTimer?.Dispose();
            _altAzTrackingTimer = null;  // N6: null the field — was missing, causing IsRunning check on disposed timer after reconnect
            if (_mediaTimer != null)
            {
                _mediaTimer.Tick -= OnUpdateServerEvent;  // No more subscriptions
                _mediaTimer?.Stop();                       // Stop generating ticks
                _mediaTimer?.Dispose();                    // Dispose resources

                // Acquire lock to ensure any pending OnUpdateServerEvent completes
                lock (_timerLock)
                {
                    _mediaTimer = null;  // Safe to null now
                }
            }
            AxesStopValidate(); // Now safe — no timer can race and re-queue motion

            if (SimQueue?.IsRunning == true) { SimQueue.Stop(); }

            if (SkyQueue?.IsRunning == true)
            {
                SkyQueue.Stop();
                CloseSerial();
            }

            SimQueue = null;
            SkyQueue = null;

            // ToDo - fix cleanup
            // Dispose SlewController
            _slewController?.Dispose();
            _slewController = null;

        }

        /// <summary>
        /// Forcibly clears all ASCOM client connections and stops the mount.
        /// Called when a breaking settings change (Port, BaudRate, AlignmentMode, Mount type)
        /// is applied while clients are connected. Clients must reconnect after this.
        /// </summary>
        public void ClearAllConnections()
        {
            var count = _connectStates.Count;
            LogMount(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Forced disconnect of {count} client(s) for breaking settings change | Mount:{Id}"
            });
            _connectStates.Clear();
            MountStop();
        }

        #endregion

        /// <summary>
        /// Wires per-device settings change notifications.
        /// </summary>
        public void InitializeSettings()
        {
            Settings.PropertyChanged -= OnPropertyChangedSkySettings; // Prevent double-wiring
            Settings.PropertyChanged += OnPropertyChangedSkySettings;
            LogMount(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Settings listener wired | Mount:{Settings.Mount} | Port:{Settings.Port}"
            });
        }

        /// <summary>
        /// Per-tick update loop.
        /// Replaces static UpdateServerEvent body — per-instance lock prevents cross-device re-entrancy.
        /// </summary>
        private void OnUpdateServerEvent(object sender, EventArgs e)
        {
            var hasLock = false;
            try
            {
                Monitor.TryEnter(_timerLock, ref hasLock);
                if (!hasLock)
                {
                    _timerOverruns++;
                    return;
                }

                _loopCounter++;
                CheckAxisLimits();
                CheckPecTraining();

                // Refresh controller voltage on every tick for SkyWatcher mounts
                if (Settings.Mount == MountType.SkyWatcher && SkyQueue != null)
                {
                    try
                    {
                        var vs = new SkyGetControllerVoltage(SkyQueue.NewId, SkyQueue, Axis.Axis1);
                        ControllerVoltage = (double)SkyQueue.GetCommandResult(vs).Result;
                    }
                    catch { /* serial errors are non-fatal; keep last known value */ }
                }
            }
            catch (Exception ex)
            {
                MountErrorHandler(ex);
            }
            finally
            {
                if (hasLock) { Monitor.Exit(_timerLock); }
            }
        }

        private void OnPropertyChangedSkySettings(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AtPark":
                    if (AtPark != Settings.AtPark) AtPark = Settings.AtPark;
                    break;
                case "AlignmentMode":
                    SetTracking(false);
                    SkyPredictor.Reset();
                    break;
            }
        }
    }
}