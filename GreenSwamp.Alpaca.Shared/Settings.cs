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
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using GreenSwamp.Alpaca.Settings.Services;

namespace GreenSwamp.Alpaca.Shared
{
    public static class Settings
    {
        #region Events

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        #endregion

        #region Fields

        private static IVersionedSettingsService? _settingsService;

        #endregion

        #region Properties Device

        private static bool _serverDevice;
        public static bool ServerDevice
        {
            get => _serverDevice;
            set
            {
                if (_serverDevice == value) return;
                _serverDevice = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _telescope;
        public static bool Telescope
        {
            get => _telescope;
            set
            {
                if (_telescope == value) return;
                _telescope = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _uI;
        public static bool Ui
        {
            get => _uI;
            set
            {
                if (_uI == value) return;
                _uI = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region  Properties Category

        private static bool _other;
        public static bool Other
        {
            get => _other;
            set
            {
                if (_other == value) return;
                _other = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _driver;
        public static bool Driver
        {
            get => _driver;
            set
            {
                if (_driver == value) return;
                _driver = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _interface;
        public static bool Interface
        {
            get => _interface;
            set
            {
                if (_interface == value) return;
                _interface = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _server;
        public static bool Server
        {
            get => _server;
            set
            {
                if (_server == value) return;
                _server = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _mount;
        public static bool Mount
        {
            get => _mount;
            set
            {
                if (_mount == value) return;
                _mount = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _alignment;
        public static bool Alignment
        {
            get => _alignment;
            set
            {
                if (_alignment == value) return;
                _alignment = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Properties Types

        private static bool _information;
        public static bool Information
        {
            get => _information;
            set
            {
                if (_information == value) return;
                _information = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _data;
        public static bool Data
        {
            get => _data;
            set
            {
                if (_data == value) return;
                _data = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _warning;
        public static bool Warning
        {
            get => _warning;
            set
            {
                if (_warning == value) return;
                _warning = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _error;
        public static bool Error
        {
            get => _error;
            set
            {
                if (_error == value) return;
                _error = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _debug;
        public static bool Debug
        {
            get => _debug;
            set
            {
                if (_debug == value) return;
                _debug = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Properties

        private static string _language;
        public static string Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _logMonitor;
        public static bool LogMonitor
        {
            get => _logMonitor;
            set
            {
                if (_logMonitor == value) return;
                _logMonitor = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _logCharting;
        public static bool LogCharting
        {
            get => _logCharting;
            set
            {
                if (_logCharting == value) return;
                _logCharting = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _logPath;
        public static string LogPath
        {
            get => _logPath;
            set
            {
                if (_logPath == value) return;
                _logPath = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// starts sending entries to a file in my documents
        /// </summary>
        private static bool _logSession;
        public static bool LogSession
        {
            get => _logSession;
            set
            {
                if (_logSession == value) return;
                _logSession = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
            }
        }

        private static bool _startMonitor;
        public static bool StartMonitor
        {
            get => _startMonitor;
            set
            {
                if (_startMonitor == value) return;
                _startMonitor = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initialize the settings with the versioned settings service
        /// </summary>
        public static void Initialize(IVersionedSettingsService service)
        {
            _settingsService = service ?? throw new ArgumentNullException(nameof(service));
            
            // Subscribe to settings changes from service
            _settingsService.MonitorSettingsChanged += (sender, settings) =>
            {
                // Settings were changed externally, reload
                LoadFromService(settings);
            };
            
            LogSetting("Initialize", "Settings service initialized");
        }

        /// <summary>
        /// Load settings from the versioned settings service
        /// </summary>
        public static void Load()
        {
            if (_settingsService == null)
            {
                throw new InvalidOperationException("Settings service not initialized. Call Initialize() first.");
            }

            var settings = _settingsService.GetMonitorSettings();
            LoadFromService(settings);
            
            LogSetting("Load", "Settings loaded from service");
        }

        /// <summary>
        /// Internal method to load from MonitorSettings model
        /// </summary>
        private static void LoadFromService(GreenSwamp.Alpaca.Settings.Models.MonitorSettings settings)
        {
            // MonitorDevice
            ServerDevice = settings.ServerDevice;
            Telescope = settings.Telescope;
            Ui = settings.Ui;
            
            // MonitorCategory
            Other = settings.Other;
            Driver = settings.Driver;
            Interface = settings.Interface;
            Server = settings.Server;
            Mount = settings.Mount;
            Alignment = settings.Alignment;
            
            // MonitorType
            Information = settings.Information;
            Data = settings.Data;
            Warning = settings.Warning;
            Error = settings.Error;
            Debug = settings.Debug;
            
            // Logging Options
            LogMonitor = settings.LogMonitor;
            LogSession = settings.LogSession;
            LogCharting = settings.LogCharting;
            StartMonitor = settings.StartMonitor;
            
            // Miscellaneous
            Language = settings.Language;
            LogPath = settings.LogPath;
        }

        /// <summary>
        /// Save and reload settings through the versioned settings service
        /// </summary>
        public static void Save()
        {
            if (_settingsService == null)
            {
                throw new InvalidOperationException("Settings service not initialized. Call Initialize() first.");
            }

            var settings = new GreenSwamp.Alpaca.Settings.Models.MonitorSettings
            {
                // MonitorDevice
                ServerDevice = ServerDevice,
                Telescope = Telescope,
                Ui = Ui,
                
                // MonitorCategory
                Other = Other,
                Driver = Driver,
                Interface = Interface,
                Server = Server,
                Mount = Mount,
                Alignment = Alignment,
                
                // MonitorType
                Information = Information,
                Data = Data,
                Warning = Warning,
                Error = Error,
                Debug = Debug,
                
                // Logging Options
                LogMonitor = LogMonitor,
                LogSession = LogSession,
                LogCharting = LogCharting,
                StartMonitor = StartMonitor,
                
                // Miscellaneous
                Language = Language,
                LogPath = LogPath,
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0"
            };

            _settingsService.SaveMonitorSettingsAsync(settings).Wait();
            
            LogSetting("Save", "Settings saved through service");
        }

        /// <summary>
        /// output to session log
        /// </summary>
        /// <param name="method"></param>
        /// <param name="value"></param>
        private static void LogSetting(string method, string value)
        {
            var monitorItem = new MonitorEntry
            { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = $"{method}", Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// property event notification
        /// </summary>
        /// <param name="propertyName"></param>
        private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
