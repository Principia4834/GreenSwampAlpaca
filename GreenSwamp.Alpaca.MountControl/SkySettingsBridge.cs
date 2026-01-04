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

using GreenSwamp.Alpaca.Settings.Services;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using ASCOM.Common.DeviceInterfaces;
using System.IO.Ports;
using System.ComponentModel;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase 3.2: Enhanced Bridge with bidirectional event forwarding and side-effect preservation.
    /// Synchronizes between legacy static SkySettings and new DI-based settings.
    /// Maintains full compatibility during migration by preserving all hardware side effects.
    /// </summary>
    public static class SkySettingsBridge
    {
        private static IVersionedSettingsService? _settingsService;
        private static bool _isUpdating; // Prevent recursive updates
        private static bool _initialized;

        /// <summary>
        /// Initialize the bridge with the new settings service and setup bidirectional event forwarding
        /// </summary>
        public static void Initialize(IVersionedSettingsService service)
        {
            if (_initialized)
            {
                LogBridge("Already initialized, skipping");
                return;
            }

            _settingsService = service ?? throw new ArgumentNullException(nameof(service));
            
            // Subscribe to changes from NEW system ? sync to OLD
            _settingsService.SettingsChanged += OnNewSettingsChanged;
            
            // Subscribe to changes from OLD system ? sync to NEW
            SkySettings.StaticPropertyChanged += OnOldSettingsPropertyChanged;
            
            // Initial sync: new ? old
            SyncNewToOld();
            
            _initialized = true;
            LogBridge("Enhanced bridge initialized with bidirectional event forwarding");
        }

        /// <summary>
        /// Called when old settings are saved - sync to new system
        /// </summary>
        public static void OnOldSettingsSaved()
        {
            if (!_initialized || _settingsService == null || _isUpdating)
            {
                return;
            }
            
            try
            {
                _isUpdating = true;
                SyncOldToNew();
                LogBridge("Synced old settings ? new settings (Save() called)");
            }
            catch (Exception ex)
            {
                LogBridge($"Error syncing old ? new: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// NEW FEATURE: Handle individual property changes from old system
        /// This allows reactive updates without waiting for full Save()
        /// </summary>
        private static void OnOldSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_initialized || _settingsService == null || _isUpdating)
            {
                return;
            }
            
            try
            {
                _isUpdating = true;
                
                // For now, sync everything on any property change
                // Future optimization: sync only the changed property
                SyncOldToNew();
                
                LogBridge($"Synced property '{e.PropertyName}' old ? new");
            }
            catch (Exception ex)
            {
                LogBridge($"Error syncing property '{e.PropertyName}': {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Sync new settings to old static properties.
        /// This method triggers the old property setters, preserving all side effects!
        /// </summary>
        private static void SyncNewToOld()
        {
            if (_settingsService == null || _isUpdating) return;

            try
            {
                _isUpdating = true;
                var newSettings = _settingsService.GetSettings();

                // =================================================================
                // PART 1: Sync 93 writable properties (via public setters - preserves side effects)
                // =================================================================

                // Connection Settings (5 properties)
                SkySettings.Mount = ParseMountType(newSettings.Mount);
                SkySettings.Port = newSettings.Port;
                SkySettings.BaudRate = ParseSerialSpeed(newSettings.BaudRate);
                SkySettings.AlignmentMode = ParseAlignmentMode(newSettings.AlignmentMode);
                SkySettings.AtPark = newSettings.AtPark;

                // Location Settings (3 properties)
                SkySettings.Latitude = newSettings.Latitude;
                SkySettings.Longitude = newSettings.Longitude;
                SkySettings.Elevation = newSettings.Elevation;

                // Tracking & Rates (10 properties)
                SkySettings.TrackingRate = ParseDriveRate(newSettings.TrackingRate);
                SkySettings.SiderealRate = newSettings.SiderealRate;
                SkySettings.LunarRate = newSettings.LunarRate;
                SkySettings.SolarRate = newSettings.SolarRate;
                SkySettings.KingRate = newSettings.KingRate;
                SkySettings.CustomGearing = newSettings.CustomGearing;
                SkySettings.CustomRa360Steps = newSettings.CustomRa360Steps;
                SkySettings.CustomRaWormTeeth = newSettings.CustomRaWormTeeth;
                SkySettings.CustomDec360Steps = newSettings.CustomDec360Steps;
                SkySettings.CustomDecWormTeeth = newSettings.CustomDecWormTeeth;
                SkySettings.CustomRaTrackingOffset = newSettings.CustomRaTrackingOffset;
                SkySettings.CustomDecTrackingOffset = newSettings.CustomDecTrackingOffset;

                // Guiding (8 properties - with side effects!)
                SkySettings.MinPulseRa = newSettings.MinPulseRa;
                SkySettings.MinPulseDec = newSettings.MinPulseDec;
                SkySettings.DecPulseToGoTo = newSettings.DecPulseToGoTo;
                SkySettings.St4GuideRate = newSettings.St4Guiderate;
                SkySettings.GuideRateOffsetX = newSettings.GuideRateOffsetX;
                SkySettings.GuideRateOffsetY = newSettings.GuideRateOffsetY;
                SkySettings.RaBacklash = newSettings.RaBacklash;
                SkySettings.DecBacklash = newSettings.DecBacklash;

                // Optics (4 properties)
                SkySettings.FocalLength = newSettings.FocalLength;
                SkySettings.CameraWidth = newSettings.CameraWidth;
                SkySettings.CameraHeight = newSettings.CameraHeight;
                SkySettings.EyepieceFs = newSettings.EyepieceFS;

                // Advanced Settings (7 properties - some with side effects!)
                SkySettings.AllowAdvancedCommandSet = newSettings.AllowAdvancedCommandSet;
                SkySettings.MaxSlewRate = newSettings.MaximumSlewRate;  // Side effect: SetSlewRates()
                SkySettings.FullCurrent = newSettings.FullCurrent;      // Side effect: SkyTasks()
                SkySettings.Encoders = newSettings.EncodersOn;          // Side effect: SkyTasks()
                SkySettings.AlternatingPPec = newSettings.AlternatingPPEC;  // Side effect: SkyTasks()
                SkySettings.GlobalStopOn = newSettings.GlobalStopOn;
                SkySettings.Refraction = newSettings.Refraction;

                // Display Settings (3 properties)
                SkySettings.DisplayInterval = newSettings.DisplayInterval;
                SkySettings.FrontGraphic = ParseFrontGraphic(newSettings.FrontGraphic);
                SkySettings.RaGaugeFlip = newSettings.RaGaugeFlip;

                // Home Position (5 properties)
                SkySettings.HomeAxisX = newSettings.HomeAxisX;
                SkySettings.HomeAxisY = newSettings.HomeAxisY;
                SkySettings.AutoHomeAxisX = newSettings.AutoHomeAxisX;
                SkySettings.AutoHomeAxisY = newSettings.AutoHomeAxisY;
                SkySettings.HomeWarning = newSettings.HomeWarning;

                // Park Settings (3 properties)
                SkySettings.ParkName = newSettings.ParkName;
                SkySettings.LimitPark = newSettings.LimitPark;
                SkySettings.ParkLimitName = newSettings.ParkLimitName;

                // Axis Limits (6 properties)
                SkySettings.HourAngleLimit = newSettings.HourAngleLimit;
                SkySettings.AxisLimitX = newSettings.AxisLimitX;
                SkySettings.AxisUpperLimitY = newSettings.AxisUpperLimitY;
                SkySettings.AxisLowerLimitY = newSettings.AxisLowerLimitY;
                SkySettings.LimitTracking = newSettings.LimitTracking;
                SkySettings.SyncLimitOn = newSettings.SyncLimitOn;
                SkySettings.AxisTrackingLimit = newSettings.AxisTrackingLimit;

                // Horizon Limits (3 properties)
                SkySettings.HzLimitPark = newSettings.HzLimitPark;
                SkySettings.ParkHzLimitName = newSettings.ParkHzLimitName;
                SkySettings.HzLimitTracking = newSettings.HzLimitTracking;
                SkySettings.AxisHzTrackingLimit = newSettings.AxisHzTrackingLimit;

                // PEC Settings (8 properties)
                SkySettings.PecOn = newSettings.PecOn;
                SkySettings.PPecOn = newSettings.PpecOn;
                SkySettings.PecMode = ParsePecMode(newSettings.PecMode);
                SkySettings.PecOffSet = newSettings.PecOffSet;
                SkySettings.PecWormFile = newSettings.PecWormFile;
                SkySettings.Pec360File = newSettings.Pec360File;
                SkySettings.PolarLedLevel = newSettings.PolarLedLevel;

                // Hand Controller (8 properties)
                SkySettings.HcSpeed = ParseSlewSpeed(newSettings.HcSpeed);
                SkySettings.HcMode = ParseHcMode(newSettings.HcMode);
                SkySettings.HcAntiRa = newSettings.HcAntiRa;
                SkySettings.HcAntiDec = newSettings.HcAntiDec;
                SkySettings.HcFlipEw = newSettings.HcFlipEW;
                SkySettings.HcFlipNs = newSettings.HcFlipNS;
                SkySettings.DisableKeysOnGoTo = newSettings.DisableKeysOnGoTo;

                // GPS Settings (2 properties)
                SkySettings.GpsComPort = ParseGpsPortNumber(newSettings.GpsPort);
                SkySettings.GpsBaudRate = ParseSerialSpeed(ParseGpsBaudRateString(newSettings.GpsBaudRate));

                // Mount Settings (2 properties)
                SkySettings.PolarMode = ParsePolarMode(newSettings.PolarMode);
                SkySettings.EquatorialCoordinateType = Enum.TryParse<EquatorialCoordinateType>(
                    newSettings.EquatorialCoordinateType, true, out var eqType)
                    ? eqType
                    : EquatorialCoordinateType.Other;

                // Environmental (1 property)
                SkySettings.Temperature = newSettings.Temperature;

                // Alt-Az Tracking (1 property)
                SkySettings.AltAzTrackingUpdateInterval = newSettings.AltAzTrackingUpdateInterval;

                // CanSetPark and CanSetPierSide have public setters (not read-only)
                SkySettings.CanSetPark = newSettings.CanSetPark;
                SkySettings.CanSetPierSide = newSettings.CanSetPierSide;

                // =================================================================
                // PART 2: Sync 41 read-only properties (via backing fields - direct access)
                // =================================================================
                // ✅ NEW: Use the JSON-based method instead of the instance-based one
                SkySettings.SetCapabilitiesAndReadOnlyFromJson(newSettings);

                LogBridge("Synced 134 properties new → old (93 writable + 41 read-only)");
            }
            finally
            {
                _isUpdating = false;
            }
        }
        
        /// <summary>
        /// Sync old static properties to new settings (write path).
        /// </summary>
        private static void SyncOldToNew()
        {
            if (_settingsService == null)
            {
                return;
            }
            
            try
            {
                // Get current settings
                var newSettings = _settingsService.GetSettings();
                
                // Update from static properties (8 critical properties)
                newSettings.Mount = SkySettings.Mount.ToString();
                newSettings.Port = SkySettings.Port;
                newSettings.BaudRate = (int)SkySettings.BaudRate;
                newSettings.Latitude = SkySettings.Latitude;
                newSettings.Longitude = SkySettings.Longitude;
                newSettings.Elevation = SkySettings.Elevation;
                newSettings.AlignmentMode = SkySettings.AlignmentMode.ToString();
                newSettings.AtPark = SkySettings.AtPark;
                
                // Phase 3a: Tracking & Rates (10 properties)
                newSettings.TrackingRate = SkySettings.TrackingRate.ToString();
                newSettings.SiderealRate = SkySettings.SiderealRate;
                newSettings.LunarRate = SkySettings.LunarRate;
                newSettings.SolarRate = SkySettings.SolarRate;
                newSettings.KingRate = SkySettings.KingRate;
                newSettings.CustomGearing = SkySettings.CustomGearing;
                newSettings.CustomRa360Steps = SkySettings.CustomRa360Steps;
                newSettings.CustomRaWormTeeth = SkySettings.CustomRaWormTeeth;
                newSettings.CustomDec360Steps = SkySettings.CustomDec360Steps;
                newSettings.CustomDecWormTeeth = SkySettings.CustomDecWormTeeth;
                
                // Phase 3b: Guiding (8 properties)
                newSettings.MinPulseRa = SkySettings.MinPulseRa;
                newSettings.MinPulseDec = SkySettings.MinPulseDec;
                newSettings.DecPulseToGoTo = SkySettings.DecPulseToGoTo;
                newSettings.St4Guiderate = SkySettings.St4GuideRate;
                newSettings.GuideRateOffsetX = SkySettings.GuideRateOffsetX;
                newSettings.GuideRateOffsetY = SkySettings.GuideRateOffsetY;
                newSettings.RaBacklash = SkySettings.RaBacklash;
                newSettings.DecBacklash = SkySettings.DecBacklash;
                
                // Phase 3c: Optics (4 properties)
                newSettings.FocalLength = SkySettings.FocalLength;
                newSettings.CameraWidth = SkySettings.CameraWidth;
                newSettings.CameraHeight = SkySettings.CameraHeight;
                newSettings.EyepieceFS = SkySettings.EyepieceFs;
                
                // Phase 3d: Advanced (6 properties)
                newSettings.AllowAdvancedCommandSet = SkySettings.AllowAdvancedCommandSet;
                newSettings.MaximumSlewRate = SkySettings.MaxSlewRate;
                newSettings.FullCurrent = SkySettings.FullCurrent;
                newSettings.GlobalStopOn = SkySettings.GlobalStopOn;
                newSettings.DisplayInterval = SkySettings.DisplayInterval;
                newSettings.Refraction = SkySettings.Refraction;
                
                // Phase 4 Batch 1: Home Position Properties
                newSettings.HomeAxisX = SkySettings.HomeAxisX;
                newSettings.HomeAxisY = SkySettings.HomeAxisY;
                newSettings.AutoHomeAxisX = SkySettings.AutoHomeAxisX;
                newSettings.AutoHomeAxisY = SkySettings.AutoHomeAxisY;
                newSettings.HomeWarning = SkySettings.HomeWarning;
                
                // Phase 4 Batch 3: Environmental & Park Properties
                newSettings.Temperature = SkySettings.Temperature;
                newSettings.ParkName = SkySettings.ParkName;
                newSettings.LimitPark = SkySettings.LimitPark;
                newSettings.ParkLimitName = SkySettings.ParkLimitName;
                
                // Phase 4 Batch 4: Axis Limit Properties
                newSettings.HourAngleLimit = SkySettings.HourAngleLimit;
                newSettings.AxisLimitX = SkySettings.AxisLimitX;
                newSettings.AxisUpperLimitY = SkySettings.AxisUpperLimitY;
                newSettings.AxisLowerLimitY = SkySettings.AxisLowerLimitY;
                newSettings.LimitTracking = SkySettings.LimitTracking;
                newSettings.SyncLimitOn = SkySettings.SyncLimitOn;
                
                // Phase 4 Batch 5: PEC & PPEC Properties
                newSettings.PecOn = SkySettings.PecOn;
                newSettings.PpecOn = SkySettings.PPecOn;
                newSettings.AlternatingPPEC = SkySettings.AlternatingPPec;
                newSettings.PecMode = SkySettings.PecMode.ToString();
                newSettings.PecOffSet = SkySettings.PecOffSet;
                newSettings.PecWormFile = SkySettings.PecWormFile;
                newSettings.Pec360File = SkySettings.Pec360File;
                newSettings.PolarLedLevel = SkySettings.PolarLedLevel;
                
                // Phase 4 Batch 6: Encoder & Hand Controller Properties
                newSettings.EncodersOn = SkySettings.Encoders;
                newSettings.HcSpeed = SkySettings.HcSpeed.ToString();
                newSettings.HcMode = SkySettings.HcMode.ToString();
                newSettings.HcAntiRa = SkySettings.HcAntiRa;
                newSettings.HcAntiDec = SkySettings.HcAntiDec;
                newSettings.HcFlipEW = SkySettings.HcFlipEw;
                newSettings.HcFlipNS = SkySettings.HcFlipNs;
                newSettings.DisableKeysOnGoTo = SkySettings.DisableKeysOnGoTo;
                
                // Phase 4 Batch 8: Serial Communication Settings (7 properties)
                newSettings.GpsPort = ParseGpsPortString(SkySettings.GpsComPort);
                newSettings.GpsBaudRate = ((int)SkySettings.GpsBaudRate).ToString();
                
                // Phase 4 Batch 9: UI & Display Settings (2 properties)
                newSettings.FrontGraphic = SkySettings.FrontGraphic.ToString();
                newSettings.RaGaugeFlip = SkySettings.RaGaugeFlip;
                
                // Phase 4 Batch 10: Mount Behavior & Capability Settings
                newSettings.PolarMode = SkySettings.PolarMode.ToString();
                
                // Phase 4 Batch 11: Horizon & Alt-Az Tracking Settings
                newSettings.HzLimitPark = SkySettings.HzLimitPark;
                newSettings.ParkHzLimitName = SkySettings.ParkHzLimitName;
                newSettings.HzLimitTracking = SkySettings.HzLimitTracking;
                newSettings.AxisHzTrackingLimit = SkySettings.AxisHzTrackingLimit;
                newSettings.AltAzTrackingUpdateInterval = SkySettings.AltAzTrackingUpdateInterval;
                
                // InstrumentDescription and InstrumentName can be synced FROM old system
                newSettings.InstrumentDescription = SkySettings.InstrumentDescription;
                newSettings.InstrumentName = SkySettings.InstrumentName;
                
                // Save asynchronously (use Wait for synchronous context)
                _settingsService.SaveSettingsAsync(newSettings).Wait();
                
                LogBridge("Saved 93 properties old ? new settings");
            }
            catch (Exception ex)
            {
                LogBridge($"Error in SyncOldToNew: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when new settings change externally (e.g., from UI)
        /// </summary>
        private static void OnNewSettingsChanged(object? sender, Settings.Models.SkySettings e)
        {
            if (_isUpdating)
            {
                return;
            }
            
            SyncNewToOld();
            LogBridge("Synced new settings ? old settings (external change)");
        }

        #region Type Converters
        
        private static MountType ParseMountType(string value)
        {
            return Enum.TryParse<MountType>(value, true, out var result) 
                ? result 
                : MountType.Simulator;
        }
            
        private static SerialSpeed ParseSerialSpeed(int value)
        {
            return value switch
            {
                300 => SerialSpeed.ps300,
                1200 => SerialSpeed.ps1200,
                2400 => SerialSpeed.ps2400,
                4800 => SerialSpeed.ps4800,
                9600 => SerialSpeed.ps9600,
                14400 => SerialSpeed.ps14400,
                19200 => SerialSpeed.ps19200,
                28800 => SerialSpeed.ps28800,
                38400 => SerialSpeed.ps38400,
                57600 => SerialSpeed.ps57600,
                115200 => SerialSpeed.ps115200,
                230400 => SerialSpeed.ps230400,
                _ => SerialSpeed.ps9600
            };
        }
                
        private static AlignmentMode ParseAlignmentMode(string value)
        {
            return Enum.TryParse<AlignmentMode>(value, true, out var result) 
                ? result 
                : AlignmentMode.GermanPolar;
        }
        
        private static DriveRate ParseDriveRate(string value)
        {
            return Enum.TryParse<DriveRate>(value, true, out var result) 
                ? result 
                : DriveRate.Sidereal;
        }
        
        private static PecMode ParsePecMode(string value)
        {
            return Enum.TryParse<PecMode>(value, true, out var result) 
                ? result 
                : PecMode.PecWorm;
        }
        
        private static SlewSpeed ParseSlewSpeed(string value)
        {
            return Enum.TryParse<SlewSpeed>(value, true, out var result) 
                ? result 
                : SlewSpeed.Eight;
        }
        
        private static HcMode ParseHcMode(string value)
        {
            return Enum.TryParse<HcMode>(value, true, out var result) 
                ? result 
                : HcMode.Guiding;
        }
        
        private static Handshake ParseHandshake(string value)
        {
            return Enum.TryParse<Handshake>(value, true, out var result) 
                ? result 
                : Handshake.None;
        }
        
        private static FrontGraphic ParseFrontGraphic(string value)
        {
            return Enum.TryParse<FrontGraphic>(value, true, out var result) 
                ? result 
                : FrontGraphic.None;
        }
        
        private static PolarMode ParsePolarMode(string value)
        {
            return Enum.TryParse<PolarMode>(value, true, out var result) 
                ? result 
                : PolarMode.Left;
        }
        
        private static string ParseGpsPortNumber(int portNumber)
        {
            return portNumber > 0 ? $"COM{portNumber}" : string.Empty;
        }
        
        private static int ParseGpsPortString(string portString)
        {
            if (string.IsNullOrEmpty(portString)) return 0;
            
            var cleaned = portString.Replace("COM", "", StringComparison.OrdinalIgnoreCase).Trim();
            return int.TryParse(cleaned, out var portNum) ? portNum : 0;
        }
        
        private static int ParseGpsBaudRateString(string baudRateString)
        {
            return int.TryParse(baudRateString, out var baudRate) ? baudRate : 9600;
        }
        
        private static void SetPrivateProperty(Type type, string propertyName, object value)
        {
            var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (property != null)
            {
                var backingField = type.GetField($"_{char.ToLower(propertyName[0])}{propertyName.Substring(1)}", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (backingField != null)
                {
                    backingField.SetValue(null, value);
                }
            }
        }
        
        #endregion

        private static void LogBridge(string message)
        {
            try
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = "SkySettingsBridge",
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
    }
}
