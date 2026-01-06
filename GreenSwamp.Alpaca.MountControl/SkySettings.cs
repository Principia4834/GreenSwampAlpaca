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
using GreenSwamp.Alpaca.MountControl.Pulses;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Configuration;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Handshake = System.IO.Ports.Handshake;

namespace GreenSwamp.Alpaca.MountControl
{
    public static class SkySettings
    {
        #region Phase 3: Static Facade for ASCOM Driver Compatibility

        private static SkySettingsInstance? _instance;

        /// <summary>
        /// Initialize static facade with settings instance.
        /// Required for ASCOM driver compatibility - multiple driver instances must share state.
        /// </summary>
        /// <param name="instance">The settings instance to delegate all static property access to</param>
        public static void Initialize(SkySettingsInstance instance)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));

            // Forward property changed events from instance to static listeners
            _instance.PropertyChanged += (sender, args) =>
            {
                OnStaticPropertyChanged(args?.PropertyName);
            };

            var monitorItem = new MonitorEntry
            {
                Datetime = Principles.HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Static SkySettings facade initialized - delegates to instance | Port:{_instance.Port} | Mount:{_instance.Mount}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion

        #region Events

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        #endregion

        #region Capabilities

        public static bool CanAlignMode
        {
            get => _instance?.CanAlignMode ?? false;
        }
        public static bool CanAltAz
        {
            get => _instance?.CanAltAz ?? false;
        }

        public static bool CanEquatorial
        {
            get => _instance?.CanEquatorial ?? false;
        }

        public static bool CanFindHome
        {
            get => _instance?.CanFindHome ?? false;
        }

        public static bool CanLatLongElev
        {
            get => _instance?.CanLatLongElev ?? false;
        }

        public static bool CanOptics
        {
            get => _instance?.CanOptics ?? false;
        }

        public static bool CanPark
        {
            get => _instance?.CanPark ?? false;
        }

        public static bool CanPulseGuide
        {
            get => _instance?.CanPulseGuide ?? false;
        }

        public static bool CanSetEquRates
        {
            get => _instance?.CanSetEquRates ?? false;
        }

        public static bool CanSetDeclinationRate
        {
            get => _instance?.CanSetDeclinationRate ?? false;
        }

        public static bool CanSetGuideRates
        {
            get => _instance?.CanSetGuideRates ?? false;
        }

        public static bool CanSetPark
        {
            get => _instance?.CanSetPark ?? false;
        }

        public static bool CanSetPierSide
        {
            get => _instance?.CanSetPierSide ?? false;
        }

        public static bool CanSetRightAscensionRate
        {
            get => _instance?.CanSetRightAscensionRate ?? false;
        }

        public static bool CanSetTracking
        {
            get => _instance?.CanSetTracking ?? false;
        }

        public static bool CanSiderealTime
        {
            get => _instance?.CanSiderealTime ?? false;
        }

        public static bool CanSlew
        {
            get => _instance?.CanSlew ?? false;
        }

        public static bool CanSlewAltAz
        {
            get => _instance?.CanSlewAltAz ?? false;
        }
        public static bool CanSlewAltAzAsync
        {
            get => _instance?.CanSlewAltAzAsync ?? false;
        }
        public static bool CanSlewAsync
        {
            get => _instance?.CanSlewAsync ?? false;
        }

        public static bool CanSync
        {
            get => _instance?.CanSync ?? false;
        }

        public static bool CanSyncAltAz
        {
            get => _instance?.CanSyncAltAz ?? false;
        }

        public static bool CanTrackingRates
        {
            get => _instance?.CanTrackingRates ?? false;
        }

        public static bool CanUnPark
        {
            get => _instance?.CanUnPark ?? false;
        }

        public static bool NoSyncPastMeridian
        {
            get => _instance?.NoSyncPastMeridian ?? false;
        }

        public static int NumMoveAxis
        {
            get => _instance?.NumMoveAxis ?? 2;
        }


        public static bool VersionOne
        {
            get => _instance?.VersionOne ?? false;
        }
        #endregion

        #region Server Settings

        public static AlignmentMode AlignmentMode
        {
            get => _instance?.AlignmentMode ?? default;
            set { if (_instance != null) _instance.AlignmentMode = value; }
        }

        public static Vector3 AxisModelOffsets => _instance?.AxisModelOffsets ?? Vector3.Zero;

        public static SerialSpeed BaudRate
        {
            get => _instance?.BaudRate ?? default;
            set { if (_instance != null) _instance.BaudRate = value; }
        }

        public static bool AllowAdvancedCommandSet
        {
            get => _instance?.AllowAdvancedCommandSet ?? false;
            set { if (_instance != null) _instance.AllowAdvancedCommandSet = value; }
        }

        public static int CustomDec360Steps
        {
            get => _instance?.CustomDec360Steps ?? 0;
            set { if (_instance != null) _instance.CustomDec360Steps = value; }
        }

        public static int CustomDecTrackingOffset
        {
            get => _instance?.CustomDecTrackingOffset ?? 0;
            set { if (_instance != null) _instance.CustomDecTrackingOffset = value; }
        }
        
        public static int CustomDecWormTeeth
        {
            get => _instance?.CustomDecWormTeeth ?? 0;
            set { if (_instance != null) _instance.CustomDecWormTeeth = value; }
        }

        public static int CustomRa360Steps
        {
            get => _instance?.CustomRa360Steps ?? 0;
            set { if (_instance != null) _instance.CustomRa360Steps = value; }
        }

        public static int CustomRaTrackingOffset
        {
            get => _instance?.CustomRaTrackingOffset ?? 0;
            set { if (_instance != null) _instance.CustomRaTrackingOffset = value; }
        }

        public static int CustomRaWormTeeth
        {
            get => _instance?.CustomRaWormTeeth ?? 0;
            set { if (_instance != null) _instance.CustomRaWormTeeth = value; }
        }


        public static bool CustomGearing
        {
            get => _instance?.CustomGearing ?? false;
            set { if (_instance != null) _instance.CustomGearing = value; }
        }

        public static string GpsComPort
        {
            get => _instance?.GpsComPort ?? "COM3";
            set { if (_instance != null) _instance.GpsComPort = value; }
        }

        public static SerialSpeed GpsBaudRate
        {
            get => _instance?.GpsBaudRate ?? default;
            set { if (_instance != null) _instance.GpsBaudRate = value; }
        }

        public static EquatorialCoordinateType EquatorialCoordinateType
        {
            get => _instance?.EquatorialCoordinateType ?? default;
            set { if (_instance != null) _instance.EquatorialCoordinateType = value; }
        }

        public static DriveRate TrackingRate
        {
            get => _instance?.TrackingRate ?? DriveRate.Sidereal;
            set { if (_instance != null) _instance.TrackingRate = value; }
        }
        // Note: Side effect is handled in SkySettingsInstance.TrackingRate setter

        public static SlewSpeed HcSpeed
        {
            get => _instance?.HcSpeed ?? default;
            set { if (_instance != null) _instance.HcSpeed = value; }
        }

        public static Handshake HandShake
        {
            get => _instance?.HandShake ?? default;
        }

        public static HcMode HcMode
        {
            get => _instance?.HcMode ?? default;
            set { if (_instance != null) _instance.HcMode = value; }
        }

        public static MountType Mount
        {
            get => _instance?.Mount ?? default;
            set { if (_instance != null) _instance.Mount = value; }
        }

        public static PecMode PecMode
        {
            get => _instance?.PecMode ?? default;
            set { if (_instance != null) _instance.PecMode = value; }
        }

        public static bool AlternatingPPec
        {
            get => _instance?.AlternatingPPec ?? default;
            set { if (_instance != null) _instance.AlternatingPPec = value; }
        }

        public static double ApertureArea => _instance?.ApertureArea ?? 0.0;

        public static double ApertureDiameter => _instance?.ApertureDiameter ?? 0.0;

        public static bool AtPark
        {
            get => _instance?.AtPark ?? false;
            set { if (_instance != null) _instance.AtPark = value; }
        }

        public static double AutoHomeAxisX
        {
            get => _instance?.AutoHomeAxisX ?? 0.0;
            set { if (_instance != null) _instance.AutoHomeAxisX = value; }
        }


        public static double AutoHomeAxisY
        {
            get => _instance?.AutoHomeAxisY ?? 0.0;
            set { if (_instance != null) _instance.AutoHomeAxisY = value; }
        }

        public static bool AutoTrack
        {
            get => _instance?.AutoTrack ?? false;
        }
        public static double AxisTrackingLimit
        {
            get => _instance?.AxisTrackingLimit ?? 0.0;
            set { if (_instance != null) _instance.AxisTrackingLimit = value; }
        }

        public static double AxisHzTrackingLimit
        {
            get => _instance?.AxisHzTrackingLimit ?? 0.0;
            set { if (_instance != null) _instance.AxisHzTrackingLimit = value; }
        }

        public static double CameraHeight
        {
            get => _instance?.CameraHeight ?? 0.0;
            set { if (_instance != null) _instance.CameraHeight = value; }
        }

        public static double CameraWidth
        {
            get => _instance?.CameraWidth ?? 0.0;
            set { if (_instance != null) _instance.CameraWidth = value; }
        }

        public static string Port
        {
            get => _instance?.Port ?? "COM3";
            set { if (_instance != null) _instance.Port = value; }
        }

        public static int DataBits
        {
            get => _instance?.DataBits ?? 0;
        }

        public static int DecBacklash
        {
            get => _instance?.DecBacklash ?? 0;
            set { if (_instance != null) _instance.DecBacklash = value; }
        }

        public static bool DecPulseToGoTo
        {
            get => _instance?.DecPulseToGoTo ?? false;
            set { if (_instance != null) _instance.DecPulseToGoTo = value; }
        }

        public static bool DisableKeysOnGoTo
        {
            get => _instance?.DisableKeysOnGoTo ?? false;
            set { if (_instance != null) _instance.DisableKeysOnGoTo = value; }
        }

        public static bool DtrEnable
        {
            get => _instance?.DtrEnable ?? false;
        }

        public static double Elevation
        {
            get => _instance?.Elevation ?? 0.0;
            set { if (_instance != null) _instance.Elevation = value; }
        }

        public static bool Encoders
        {
            get => _instance?.Encoders ?? false;
            set { if (_instance != null) _instance.Encoders = value; }
        }

        public static double EyepieceFs
        {
            get => _instance?.EyepieceFs ?? 0.0;
            set { if (_instance != null) _instance.EyepieceFs = value; }
        }

        public static bool FullCurrent
        {
            get => _instance?.FullCurrent ?? false;
            set { if (_instance != null) _instance.FullCurrent = value; }
        }

        public static double FocalLength
        {
            get => _instance?.FocalLength ?? 0.0;
            set { if (_instance != null) _instance.FocalLength = value; }
        }

        public static bool GlobalStopOn
        {
            get => _instance?.GlobalStopOn ?? false;
            set { if (_instance != null) _instance.GlobalStopOn = value; }
        }

        public static double GotoPrecision => _instance?.GotoPrecision ?? 0.001;

        public static double GuideRateOffsetX
        {
            get => _instance?.GuideRateOffsetX ?? 0.0;
            set { if (_instance != null) _instance.GuideRateOffsetX = value; }
        }

        public static double GuideRateOffsetY
        {
            get => _instance?.GuideRateOffsetY ?? 0.0;
            set { if (_instance != null) _instance.GuideRateOffsetY = value; }
        }

        public static bool HcAntiDec
        {
            get => _instance?.HcAntiDec ?? false;
            set { if (_instance != null) _instance.HcAntiDec = value; }
        }

        public static bool HcAntiRa
        {
            get => _instance?.HcAntiRa ?? false;
            set { if (_instance != null) _instance.HcAntiRa = value; }
        }

        public static bool HcFlipEw
        {
            get => _instance?.HcFlipEw ?? false;
            set { if (_instance != null) _instance.HcFlipEw = value; }
        }

        public static bool HcFlipNs
        {
            get => _instance?.HcFlipNs ?? false;
            set { if (_instance != null) _instance.HcFlipNs = value; }
        }

        public static List<HcPulseGuide> HcPulseGuides
        {
            get => _instance?.HcPulseGuides ?? new List<HcPulseGuide>();
            set { if (_instance != null) _instance.HcPulseGuides = value; }
        }

        public static double HomeAxisX
        {
            get => _instance?.HomeAxisX ?? 0.0;
            set { if (_instance != null) _instance.HomeAxisX = value; }
        }

        public static double HomeAxisY
        {
            get => _instance?.HomeAxisY ?? 0.0;
            set { if (_instance != null) _instance.HomeAxisY = value; }
        }

        public static double HourAngleLimit
        {
            get => _instance?.HourAngleLimit ?? 0.0;
            set { if (_instance != null) _instance.HourAngleLimit = value; }
        }

        public static bool HzLimitTracking
        {
            get => _instance?.HzLimitTracking ?? false;
            set { if (_instance != null) _instance.HzLimitTracking = value; }
        }

        public static bool HzLimitPark
        {
            get => _instance?.HzLimitPark ?? false;
            set { if (_instance != null) _instance.HzLimitPark = value; }
        }

        public static string InstrumentDescription
        {
            get => _instance?.InstrumentDescription ?? string.Empty;
        }

        public static string InstrumentName
        {
            get => _instance?.InstrumentName ?? string.Empty;
        }

        public static double KingRate
        {
            get => _instance?.KingRate ?? 0.0;
            set { if (_instance != null) _instance.KingRate = value; }
        }

        public static double Latitude
        {
            get => _instance?.Latitude ?? 0.0;
            set { if (_instance != null) _instance.Latitude = value; }
        }
        // Note: Side effect handled in SkySettingsInstance.Latitude setter

        public static bool LimitTracking
        {
            get => _instance?.LimitTracking ?? false;
            set { if (_instance != null) _instance.LimitTracking = value; }
        }

        public static bool LimitPark
        {
            get => _instance?.LimitPark ?? false;
            set { if (_instance != null) _instance.LimitPark = value; }
        }

        public static double Longitude
        {
            get => _instance?.Longitude ?? 0.0;
            set { if (_instance != null) _instance.Longitude = value; }
        }

        public static double LunarRate
        {
            get => _instance?.LunarRate ?? 0.0;
            set { if (_instance != null) _instance.LunarRate = value; }
        }

        public static double MaxSlewRate
        {
            get => _instance?.MaxSlewRate ?? 0.0;
            set { if (_instance != null) _instance.MaxSlewRate = value; }
        }
        // Note: Side effect handled in SkySettingsInstance.MaxSlewRate setter

        public static int MinPulseDec
        {
            get => _instance?.MinPulseDec ?? 0;
            set { if (_instance != null) _instance.MinPulseDec = value; }
        }

        public static int MinPulseRa
        {
            get => _instance?.MinPulseRa ?? 0;
            set { if (_instance != null) _instance.MinPulseRa = value; }
        }

       /// <summary>
        /// Park axes position in mount axes values
        /// </summary>
        public static double[] ParkAxes
        {
            get => _instance?.ParkAxes ?? new[] { double.NaN, double.NaN };
            set { if (_instance != null) _instance.ParkAxes = value; }
        }

        public static bool PecOn
        {
            get => _instance?.PecOn ?? false;
            set { if (_instance != null) _instance.PecOn = value; }
        }

        public static int PecOffSet
        {
            get => _instance?.PecOffSet ?? 0;
            set { if (_instance != null) _instance.PecOffSet = value; }
        }

        public static string PecWormFile
        {
            get => _instance?.PecWormFile ?? string.Empty;
            set { if (_instance != null) _instance.PecWormFile = value; }
        }

        public static string Pec360File
        {
            get => _instance?.Pec360File ?? string.Empty;
            set { if (_instance != null) _instance.Pec360File = value; }
        }

        public static bool PPecOn
        {
            get => _instance?.PPecOn ?? false;
            set { if (_instance != null) _instance.PPecOn = value; }
        }

        public static int PolarLedLevel
        {
            get => _instance?.PolarLedLevel ?? 0;
            set { if (_instance != null) _instance.PolarLedLevel = value; }
        }

        public static PolarMode PolarMode
        {
            get => _instance?.PolarMode ?? PolarMode.Left;
            set { if (_instance != null) _instance.PolarMode = value; }
        }

        public static int RaBacklash
        {
            get => _instance?.RaBacklash ?? 0;
            set { if (_instance != null) _instance.RaBacklash = value; }
        }

        public static int ReadTimeout
        {
            get => _instance?.ReadTimeout ?? 0;
        }

        public static bool Refraction
        {
            get => _instance?.Refraction ?? false;
            set { if (_instance != null) _instance.Refraction = value; }
        }

        public static int RaTrackingOffset
        {
            get => _instance?.RaTrackingOffset ?? 0;
        }


        public static bool RtsEnable
        {
            get => _instance?.RtsEnable ?? false;
        }

        public static double AxisLimitX
        {
            get => _instance?.AxisLimitX ?? 0.0;
            set { if (_instance != null) _instance.AxisLimitX = value; }
        }

        public static double SiderealRate
        {
            get => _instance?.SiderealRate ?? 0.0;
            set { if (_instance != null) _instance.SiderealRate = value; }
        }
        
        public static int DisplayInterval
        {
            get => _instance?.DisplayInterval ?? 0;
            set { if (_instance != null) _instance.DisplayInterval = value; }
        }

        public static int St4GuideRate
        {
            get => _instance?.St4GuideRate ?? 0;
            set { if (_instance != null) _instance.St4GuideRate = value; }
        }

        public static double SolarRate
        {
            get => _instance?.SolarRate ?? 0.0;
            set { if (_instance != null) _instance.SolarRate = value; }
        }

        public static int SyncLimit
        {
            get => _instance?.SyncLimit ?? 0;
        }

        public static bool SyncLimitOn
        {
            get => _instance?.SyncLimitOn ?? false;
            set { if (_instance != null) _instance.SyncLimitOn = value; }
        }

        public static double Temperature
        {
            get => _instance?.Temperature ?? 0.0;
            set { if (_instance != null) _instance.Temperature = value; }
        }

        /// <summary>
        /// Park positions in mount axes values
        /// Polar values are stored as Az / Alt
        /// </summary>
        public static List<ParkPosition> ParkPositions
        {
            get => _instance?.ParkPositions ?? new List<ParkPosition>();
            set { if (_instance != null) _instance.ParkPositions = value; }
        }

        public static string ParkName
        {
            get => _instance?.ParkName ?? string.Empty;
            set { if (_instance != null) _instance.ParkName = value; }
        }

        public static string ParkLimitName
        {
            get => _instance?.ParkLimitName ?? string.Empty;
            set { if (_instance != null) _instance.ParkLimitName = value; }
        }

        public static string ParkHzLimitName
        {
            get => _instance?.ParkHzLimitName ?? string.Empty;
            set { if (_instance != null) _instance.ParkHzLimitName = value; }
        }

        public static int AltAzTrackingUpdateInterval
        {
            get => _instance?.AltAzTrackingUpdateInterval ?? 0;
            set { if (_instance != null) _instance.AltAzTrackingUpdateInterval = value; }
        }

        public static double AxisLowerLimitY
        {
            get => _instance?.AxisLowerLimitY ?? 0.0;
            set { if (_instance != null) _instance.AxisLowerLimitY = value; }
        }

        public static double AxisUpperLimitY
        {
            get
            {
                var baseValue = _instance?.AxisUpperLimitY ?? 90.0;
                return baseValue + (AlignmentMode == AlignmentMode.Polar ? Math.Abs(Latitude) : 0);
            }
            set { if (_instance != null) _instance.AxisUpperLimitY = value; }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Resets the park positions to their default values based on the current latitude setting.
        /// </summary>
        /// <remarks>This method adjusts the Y-coordinates of the park positions to account for the
        /// absolute value of the latitude  and sets the park state to its default configuration. The updated park
        /// positions are serialized and saved  to the application's settings.</remarks>
        public static void ResetParkPositions()
        {
            // Phase A.5: Updated to use static properties and hardcoded defaults instead of app.config
            // Default park positions for Polar mount: (0, 0) and (0, 5) adjusted for latitude
            
            var parkPositions = new List<ParkPosition>
            {
                new ParkPosition
                {
                    Name = "Default",
                    X = 0.0,
                    Y = Math.Round(Math.Abs(Latitude) - 90.0, 6)
                },
                new ParkPosition
                {
                    Name = "Home",
                    X = 0.0,
                    Y = Math.Round(Math.Abs(Latitude) - 85.0, 6) // 5 degrees from default
                }
            };
            
            // Update static properties directly (bridge will handle JSON persistence)
            ParkPositions = parkPositions;
            AtPark = false;
            ParkName = "Default";
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
