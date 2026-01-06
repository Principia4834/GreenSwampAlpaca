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
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GreenSwamp.Alpaca.MountControl
{
    public static class SkySystem
    {
        public static event PropertyChangedEventHandler StaticPropertyChanged;
        private static long _idCount;
        // Phase 2: Instance-based settings reference
        private static SkySettingsInstance? _settings;
        private static ConcurrentDictionary<long, bool> _connectStates;
        private static bool _initialized;
        private static readonly ConcurrentDictionary<long, bool> _pendingConnections = new ConcurrentDictionary<long, bool>();

        // Remove readonly modifier to allow lazy initialization
        public static ConcurrentDictionary<long, bool> ConnectStates
        {
            get
            {
                if (!_initialized)
                {
                    return new ConcurrentDictionary<long, bool>();  // ✅ Return empty collection
                }
                return _connectStates;
            }
        }

        public static ISerialPort Serial { get; private set; }
        private static IList<string> _devices;

        /// <summary>
        /// com and remote ip ports
        /// </summary>
        public static IList<string> Devices
        {
            get => _devices;
            private set
            {
                _devices = value;
                OnStaticPropertyChanged();
            }
        }

        public static bool Connected
        {
            get
            {
                if (!_initialized)
                {
                    // ✅ Check if there's a pending connection
                    return _pendingConnections.Values.Any(v => v);
                }
                return _connectStates?.Count > 0;
            }
        }
        public static bool Connecting { get; set; }

        public static Exception Error { get; private set; }

        public static ConnectType ConnType { get; private set; }

        /// <summary>
        /// Initializes the SkySystem. Must be called after SkySettings are loaded.
        /// Thread-safe and idempotent - safe to call multiple times.
        /// </summary>
        public static void Initialize(SkySettingsInstance settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (_initialized) return;

            lock (typeof(SkySystem))
            {
                if (_initialized) return;

                // Validate that settings have been loaded from instance
                ValidateSettingsLoaded();

                // Initialize fields that were in static constructor
                _connectStates = new ConcurrentDictionary<long, bool>();
                _idCount = 0;
                Connecting = false;

                // Discover serial devices (now safe because settings are loaded)
                DiscoverSerialDevices();

                _initialized = true;

                // Process any pending connections that were queued before initialization
                foreach (var pending in _pendingConnections)
                {
                    SetConnected(pending.Key, pending.Value);
                }
                _pendingConnections.Clear();

                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "SkySystem initialized successfully"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Validates that SkySettings have been properly loaded before using them.
        /// Throws InvalidOperationException if settings are not initialized.
        /// </summary>
        private static void ValidateSettingsLoaded()
        {
            if (_settings == null)
            {
                throw new InvalidOperationException(
                    "SkySystem.Initialize() called with null settings instance. " +
                    "Ensure valid SkySettingsInstance is passed to Initialize().");
            }

            // Check critical settings that must be loaded
            if (string.IsNullOrEmpty(_settings.Port))
            {
                throw new InvalidOperationException(
                    "SkySystem.Initialize() called with invalid settings. " +
                    "Settings.Port is null or empty. " +
                    "Ensure settings are loaded from JSON before calling Initialize().");
            }

            if (_settings.BaudRate == 0 || (int)_settings.BaudRate == 0)
            {
                throw new InvalidOperationException(
                    "SkySystem.Initialize() called with invalid settings. " +
                    "Settings.BaudRate is zero (default enum value). " +
                    "Ensure settings are loaded from JSON before calling Initialize().");
            }

            // Log successful validation
            var monitorItem = new MonitorEntry
            {
                Datetime = Principles.HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Settings validation passed: Port={_settings.Port}, BaudRate={(int)_settings.BaudRate}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// Ensures SkySystem is initialized before use.
        /// Throws InvalidOperationException if not initialized.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "SkySystem not initialized. Call SkySystem.Initialize() after " +
                    "SkySettingsBridge.Initialize() in Program.cs startup sequence.");
            }
        }
        public static void SetConnected(long id, bool value)
        {
            // If not initialized yet, queue the connection request
            if (!_initialized)
            {
                _pendingConnections[id] = value;
                
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"SetConnected queued before initialization - ID:{id}, Value:{value} - will process after Initialize()"
                };
                MonitorLog.LogToMonitor(monitorItem);
                return; // ✅ Queue instead of ignoring
            }

            // add or remove the instance, this is done once regardless of the number of calls
            if (value)
            {
                if (!Connected) {Connecting = true;}

                var notAlreadyPresent = ConnectStates.TryAdd(id, true);

                if (Connected)
                {
                    if (!SkyServer.IsMountRunning)
                    {
                        SkyServer.IsMountRunning = true;
                        var connectionTimer = Stopwatch.StartNew();
                        // Wait for two server event loop updates or 5 second timeout
                        while (SkyServer.LoopCounter < 2 && connectionTimer.ElapsedMilliseconds < 5000)
                            Thread.Sleep(100);
                    }
                }

                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Add|{id}|{notAlreadyPresent}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            else
            {
                if (ConnectStates.Count == 1) {Connecting = true;}

                var successfullyRemoved = ConnectStates.TryRemove(id, out _);

                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Remove|{id}|{successfullyRemoved}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            Connecting = false;
        }

        public static void DiscoverSerialDevices()
        {
            // Allow this method to be called during initialization (don't guard)
            // But validate settings if we're using them
            if (_initialized && _settings != null && string.IsNullOrEmpty(_settings.Port))
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Settings.Port is null or empty during DiscoverSerialDevices"
                };
                MonitorLog.LogToMonitor(monitorItem);
                Devices = new List<string>();
                return;
            }

            var list = new List<string>();
            var allPorts = SerialPort.GetPortNames();
            foreach (var port in allPorts)
            {
                if (string.IsNullOrEmpty(port)) continue;
                var portNumber = Strings.GetNumberFromString(port);
                if (!(portNumber >= 1)) continue;
                if (!list.Contains(port))
                {
                    list.Add(port);
                }
            }

            if (_settings != null && !string.IsNullOrEmpty(_settings.Port) && !list.Contains(_settings.Port))
            {
                list.Add(_settings.Port);
            }
            Devices = list;
        }

        public static void AddRemoteIp(string ip)
        {
            // If not initialized, we can't safely add remote IPs yet
            if (!_initialized)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"AddRemoteIp called before initialization - IP:{ip} - ignoring until initialized"
                };
                MonitorLog.LogToMonitor(monitorItem);
                return;
            }

            var list = Devices;
            if (list.Contains(ip)) return;
            list.Add(ip);
            Devices = list;
            if (_settings != null)
            {
                _settings.Port = ip;
            }
        }

        public static bool ConnectSerial
        {
            get => Serial?.IsOpen == true;
            internal set
            {
                // If not initialized, we can't connect yet
                if (!_initialized)
                {
                    var monitorItem = new MonitorEntry
                    {
                        Datetime = Principles.HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"ConnectSerial setter called before initialization - Value:{value} - ignoring until initialized"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    return;
                }

                try
                {
                    Serial?.Dispose();
                    Serial = null;
                    if(ConnType != ConnectType.None){ConnType = ConnectType.None;}

                    if (value)
                    {
                        var readTimeout = TimeSpan.FromMilliseconds(_settings!.ReadTimeout);
                        if (_settings.Port.Contains("COM"))
                        {
                            var options = SerialOptions.DiscardNull
                                | (_settings.DtrEnable ? SerialOptions.DtrEnable : SerialOptions.None)
                                | (_settings.RtsEnable ? SerialOptions.RtsEnable : SerialOptions.None);

                            Serial = new GsSerialPort(
                                _settings.Port,
                                (int)_settings.BaudRate,
                                readTimeout,
                                _settings.HandShake,
                                Parity.None,
                                StopBits.One,
                                _settings.DataBits,
                                options);
                            ConnType = ConnectType.Com;
                        }
                        else
                        {
                            var endpoint = CreateIpEndPoint(_settings.Port);
                            Serial = new SerialOverUdpPort(endpoint, readTimeout);
                            ConnType = ConnectType.Wifi;
                        }
                        Serial?.Open();
                    }
                    OnStaticPropertyChanged();
                }
                catch (Exception ex)
                {
                    Error = ex;
                    var monitorItem = new MonitorEntry
                    {
                        Datetime = Principles.HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"{ex.Message}|{ex.InnerException?.Message}"
                    };
                    MonitorLog.LogToMonitor(monitorItem);
                    Serial = null;
                    ConnType = ConnectType.None;
                }
            }
        }

        /// <summary>
        /// Handles IPv4 and IPv6 notation.
        /// </summary>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        private static IPEndPoint CreateIpEndPoint(string endPoint)
        {
            var ep = endPoint.Split(':');
            if (ep.Length < 2) {throw new FormatException("Invalid endpoint format");}
            IPAddress ip;
            if (ep.Length > 2)
            {
                if (!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip))
                {
                    throw new FormatException("Invalid ip-address");
                }
            }
            else
            {
                if (!IPAddress.TryParse(ep[0], out ip))
                {
                    throw new FormatException("Invalid ip-address");
                }
            }

            return !int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out var port)
                ? throw new FormatException("Invalid port")
                : new IPEndPoint(ip, port);
        }

        /// <summary>
        /// Get a thread-safe, unique ID.
        /// </summary>
        /// <returns></returns>
        public static long GetId() => Interlocked.Increment(ref _idCount);
        private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }
    }
}
