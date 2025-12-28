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

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Phase 2: Bridge to synchronize between legacy static SkySettings and new DI-based settings.
    /// Provides bidirectional synchronization to maintain compatibility during migration.
    /// Phase 2 syncs only critical properties - full sync happens in Phase 4.
    /// </summary>
    public static class SkySettingsBridge
    {
        private static IVersionedSettingsService? _settingsService;
        private static bool _isUpdating; // Prevent recursive updates
        private static bool _initialized;

        /// <summary>
        /// Initialize the bridge with the new settings service
        /// </summary>
        public static void Initialize(IVersionedSettingsService service)
        {
            if (_initialized)
            {
                LogBridge("Already initialized, skipping");
                return;
            }

            _settingsService = service ?? throw new ArgumentNullException(nameof(service));
            
            // Subscribe to changes from new system
            _settingsService.SettingsChanged += OnNewSettingsChanged;
            
            // Initial sync: new ? old
            SyncNewToOld();
            
            _initialized = true;
            LogBridge("Bridge initialized and synced");
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
                LogBridge("Synced old settings ? new settings");
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
        /// Sync new settings to old static properties.
        /// Phase 2: Only critical properties (8 total) - expanded in Phase 4.
        /// </summary>
        private static void SyncNewToOld()
        {
            if (_settingsService == null || _isUpdating)
            {
                return;
            }
            
            try
            {
                _isUpdating = true;
                var newSettings = _settingsService.GetSettings();
                
                // Phase 2: Sync only critical properties
                // Connection Settings
                SkySettings.Mount = ParseMountType(newSettings.Mount);
                SkySettings.Port = newSettings.Port;
                SkySettings.BaudRate = ParseSerialSpeed(newSettings.BaudRate);
                
                // Location Settings
                SkySettings.Latitude = newSettings.Latitude;
                SkySettings.Longitude = newSettings.Longitude;
                SkySettings.Elevation = newSettings.Elevation;
                
                // Mount Configuration
                SkySettings.AlignmentMode = ParseAlignmentMode(newSettings.AlignmentMode);
                SkySettings.AtPark = newSettings.AtPark;
                
                // Phase 3a: Tracking & Rates (10 properties)
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
                
                // Phase 3b: Guiding (8 properties)
                SkySettings.MinPulseRa = newSettings.MinPulseRa;
                SkySettings.MinPulseDec = newSettings.MinPulseDec;
                SkySettings.DecPulseToGoTo = newSettings.DecPulseToGoTo;
                SkySettings.St4GuideRate = newSettings.St4Guiderate;
                SkySettings.GuideRateOffsetX = newSettings.GuideRateOffsetX;
                SkySettings.GuideRateOffsetY = newSettings.GuideRateOffsetY;
                SkySettings.RaBacklash = newSettings.RaBacklash;
                SkySettings.DecBacklash = newSettings.DecBacklash;
                
                // Phase 3c: Optics (4 properties - ApertureDiameter/Area are read-only in old system)
                SkySettings.FocalLength = newSettings.FocalLength;
                SkySettings.CameraWidth = newSettings.CameraWidth;
                SkySettings.CameraHeight = newSettings.CameraHeight;
                SkySettings.EyepieceFs = newSettings.EyepieceFS;
                
                // Phase 3d: Advanced (6 properties - GotoPrecision read-only, TraceLogger not in old system)
                SkySettings.AllowAdvancedCommandSet = newSettings.AllowAdvancedCommandSet;
                SkySettings.MaxSlewRate = newSettings.MaximumSlewRate;
                SkySettings.FullCurrent = newSettings.FullCurrent;
                SkySettings.GlobalStopOn = newSettings.GlobalStopOn;
                SkySettings.DisplayInterval = newSettings.DisplayInterval;
                SkySettings.Refraction = newSettings.Refraction;
                
                // Phase 4 Batch 1: Home Position Properties
                SkySettings.HomeAxisX = newSettings.HomeAxisX;
                SkySettings.HomeAxisY = newSettings.HomeAxisY;
                SkySettings.AutoHomeAxisX = newSettings.AutoHomeAxisX;
                SkySettings.AutoHomeAxisY = newSettings.AutoHomeAxisY;
                SkySettings.HomeWarning = newSettings.HomeWarning;
                
                // Phase 4 Batch 3: Environmental & Park Properties
                SkySettings.Temperature = newSettings.Temperature;
                SkySettings.ParkName = newSettings.ParkName;
                SkySettings.ParkDialog = newSettings.ParkDialog;
                SkySettings.LimitPark = newSettings.LimitPark;
                SkySettings.ParkLimitName = newSettings.ParkLimitName;
                
                // Phase 4 Batch 4: Axis Limit Properties
                SkySettings.HourAngleLimit = newSettings.HourAngleLimit;
                SkySettings.AxisLimitX = newSettings.AxisLimitX;
                SkySettings.AxisUpperLimitY = newSettings.AxisUpperLimitY;
                SkySettings.AxisLowerLimitY = newSettings.AxisLowerLimitY;
                SkySettings.LimitTracking = newSettings.LimitTracking;
                SkySettings.SyncLimitOn = newSettings.SyncLimitOn;
                
                // Phase 4 Batch 5: PEC & PPEC Properties
                SkySettings.PecOn = newSettings.PecOn;
                SkySettings.PPecOn = newSettings.PpecOn;
                SkySettings.AlternatingPPec = newSettings.AlternatingPPEC;
                SkySettings.PecMode = ParsePecMode(newSettings.PecMode);
                SkySettings.PecOffSet = newSettings.PecOffSet;
                SkySettings.PecWormFile = newSettings.PecWormFile;
                SkySettings.Pec360File = newSettings.Pec360File;
                SkySettings.PolarLedLevel = newSettings.PolarLedLevel;
                
                // Phase 4 Batch 6: Encoder & Hand Controller Properties
                SkySettings.Encoders = newSettings.EncodersOn;
                SkySettings.HcSpeed = ParseSlewSpeed(newSettings.HcSpeed);
                SkySettings.HcMode = ParseHcMode(newSettings.HcMode);
                SkySettings.HcAntiRa = newSettings.HcAntiRa;
                SkySettings.HcAntiDec = newSettings.HcAntiDec;
                SkySettings.HcFlipEw = newSettings.HcFlipEW;
                SkySettings.HcFlipNs = newSettings.HcFlipNS;
                SkySettings.DisableKeysOnGoTo = newSettings.DisableKeysOnGoTo;
                SkySettings.MinPulseRa = newSettings.MinPulseRa;
                SkySettings.MinPulseDec = newSettings.MinPulseDec;
                
                // Phase 4 Batch 7: Tracking Rate & Slew Properties
                SkySettings.SiderealRate = newSettings.SiderealRate;
                SkySettings.LunarRate = newSettings.LunarRate;
                SkySettings.SolarRate = newSettings.SolarRate;
                SkySettings.KingRate = newSettings.KingRate;
                SkySettings.MaxSlewRate = newSettings.MaximumSlewRate;
                SkySettings.DisplayInterval = newSettings.DisplayInterval;
                SkySettings.St4GuideRate = newSettings.St4Guiderate;
                
                // Phase 4 Batch 8: Serial Communication Settings (7 properties)
                // Note: Some have private setters (ReadTimeout, DataBits, DTREnable, RTSEnable, HandShake)
                // These can only be read from new settings, not written back from old
                // Using reflection to set these read-only properties
                try 
                {
                    var handshakeValue = ParseHandshake(newSettings.Handshake);
                    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.HandShake), handshakeValue);
                    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.ReadTimeout), newSettings.ReadTimeout);
                    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.DataBits), newSettings.DataBits);
                    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.DtrEnable), newSettings.DTREnable);
                    SetPrivateProperty(typeof(SkySettings), nameof(SkySettings.RtsEnable), newSettings.RTSEnable);
                }
                catch (Exception ex)
                {
                    LogBridge($"Warning: Could not sync read-only serial properties: {ex.Message}");
                }
                
                // GPS settings - handle port name/number conversion
                SkySettings.GpsComPort = ParseGpsPortNumber(newSettings.GpsPort);
                SkySettings.GpsBaudRate = ParseSerialSpeed(ParseGpsBaudRateString(newSettings.GpsBaudRate));
                
                // Phase 4 Batch 9: UI & Display Settings (2 properties)
                // Note: TraceLogger doesn't exist in old system, skip it
                SkySettings.FrontGraphic = ParseFrontGraphic(newSettings.FrontGraphic);
                SkySettings.RaGaugeFlip = newSettings.RaGaugeFlip;
                
                // Phase 4 Batch 10: Mount Behavior & Capability Settings (3 properties)
                // Note: Several properties only exist in new system or are read-only in old system
                // DisconnectOnPark - new system only, skip
                // AutoTrack - read-only in old, skip (can't sync from new)
                // ModelType - new system only, skip
                // Pressure - new system only, skip
                // VersionOne, NumMoveAxis, NoSyncPastMeridian - all read-only in old, skip
                
                // PolarMode - bidirectional sync
                SkySettings.PolarMode = ParsePolarMode(newSettings.PolarMode);
                
                LogBridge($"Synced 88 properties from new ? old (Phase 4 Batch 1-10)");
            }
            catch (Exception ex)
            {
                LogBridge($"Error in SyncNewToOld: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Sync old static properties to new settings (write path).
        /// Phase 2: Only when explicitly saved via Save() method.
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
                
                // Phase 3c: Optics (4 properties - ApertureDiameter/Area are read-only in old system)
                newSettings.FocalLength = SkySettings.FocalLength;
                newSettings.CameraWidth = SkySettings.CameraWidth;
                newSettings.CameraHeight = SkySettings.CameraHeight;
                newSettings.EyepieceFS = SkySettings.EyepieceFs;
                
                // Phase 3d: Advanced (6 properties - GotoPrecision read-only, TraceLogger not in old system)
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
                
                // Phase 4 Batch 2: Home Dialog Property
                newSettings.HomeDialog = SkySettings.HomeDialog;
                
                // Phase 4 Batch 3: Environmental & Park Properties
                newSettings.Temperature = SkySettings.Temperature;
                newSettings.ParkName = SkySettings.ParkName;
                newSettings.ParkDialog = SkySettings.ParkDialog;
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
                newSettings.MinPulseRa = SkySettings.MinPulseRa;
                newSettings.MinPulseDec = SkySettings.MinPulseDec;
                
                // Phase 4 Batch 7: Tracking Rate & Slew Properties
                newSettings.SiderealRate = SkySettings.SiderealRate;
                newSettings.LunarRate = SkySettings.LunarRate;
                newSettings.SolarRate = SkySettings.SolarRate;
                newSettings.KingRate = SkySettings.KingRate;
                newSettings.MaximumSlewRate = SkySettings.MaxSlewRate;
                newSettings.DisplayInterval = SkySettings.DisplayInterval;
                newSettings.St4Guiderate = SkySettings.St4GuideRate;
                
                // Phase 4 Batch 8: Serial Communication Settings (7 properties)
                // Note: Read-only properties in old system are NOT synced back (they shouldn't change)
                // Only GPS settings can be synced from old ? new
                newSettings.GpsPort = ParseGpsPortString(SkySettings.GpsComPort);
                newSettings.GpsBaudRate = ((int)SkySettings.GpsBaudRate).ToString();
                
                // The following are read-only in old system, so they're not synced from old ? new:
                // HandShake, ReadTimeout, DataBits, DTREnable, RTSEnable
                
                // Phase 4 Batch 9: UI & Display Settings (2 properties)
                // Note: TraceLogger is new system only, doesn't sync from old
                newSettings.FrontGraphic = SkySettings.FrontGraphic.ToString();
                newSettings.RaGaugeFlip = SkySettings.RaGaugeFlip;
                
                // Phase 4 Batch 10: Mount Behavior & Capability Settings
                // New system properties (can't sync FROM old system):
                // - DisconnectOnPark (new only)
                // - ModelType (new only)
                // - Pressure (new only)
                // - TraceLogger (already handled in Batch 9)
                
                // Read-only in old system (can't sync FROM old, they're computed/derived):
                // - AutoTrack (private set in old)
                // - VersionOne (private set in old)
                // - NumMoveAxis (private set in old)
                // - NoSyncPastMeridian (private set in old)
                
                // Bidirectional property:
                newSettings.PolarMode = SkySettings.PolarMode.ToString();
                
                // Save asynchronously (use Wait for synchronous context)
                _settingsService.SaveSettingsAsync(newSettings).Wait();
                
                LogBridge("Saved 88 properties old ? new settings (Phase 4 Batch 1-10)");
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
            // Map baud rate integer to SerialSpeed enum
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
                : PolarMode.Left; // Default to Left if parsing fails
        }
        
        private static string ParseGpsPortNumber(int portNumber)
        {
            // Convert port number to COM port string (e.g., 1 ? "COM1")
            return portNumber > 0 ? $"COM{portNumber}" : string.Empty;
        }
        
        private static int ParseGpsPortString(string portString)
        {
            // Convert COM port string to port number (e.g., "COM1" ? 1)
            if (string.IsNullOrEmpty(portString)) return 0;
            
            var cleaned = portString.Replace("COM", "", StringComparison.OrdinalIgnoreCase).Trim();
            return int.TryParse(cleaned, out var portNum) ? portNum : 0;
        }
        
        private static int ParseGpsBaudRateString(string baudRateString)
        {
            // Parse baud rate from string to integer
            return int.TryParse(baudRateString, out var baudRate) ? baudRate : 9600;
        }
        
        /// <summary>
        /// Set a private property using reflection (for read-only properties in old system)
        /// </summary>
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

        // Phase 4 Batch 1: Home Position Properties
        internal static partial class Keys
        {
            public const string HomeAxisX = "HomeAxisX";
            public const string HomeAxisY = "AutoHomeAxisY";
            public const string AutoHomeAxisX = "AutoHomeAxisX";
            public const string AutoHomeAxisY = "AutoHomeAxisY";
            public const string HomeWarning = "HomeWarning";
            
            // Phase 4 Batch 2: Home Dialog Property
            public const string HomeDialog = "HomeDialog";
            
            // Phase 4 Batch 3: Environmental & Park Properties
            public const string Temperature = "Temperature";
            public const string ParkName = "ParkName";
            public const string ParkDialog = "ParkDialog";
            public const string LimitPark = "LimitPark";
            public const string ParkLimitName = "ParkLimitName";
            
            // Phase 4 Batch 4: Axis Limit Properties
            public const string HourAngleLimit = "HourAngleLimit";
            public const string AxisLimitX = "AxisLimitX";
            public const string AxisUpperLimitY = "AxisUpperLimitY";
            public const string AxisLowerLimitY = "AxisLowerLimitY";
            public const string LimitTracking = "LimitTracking";
            public const string SyncLimitOn = "SyncLimitOn";
            
            // Phase 4 Batch 5: PEC & PPEC Properties
            public const string PecOn = "PecOn";
            public const string PPecOn = "PPecOn";
            public const string AlternatingPPec = "AlternatingPPec";
            public const string PecMode = "PecMode";
            public const string PecOffSet = "PecOffSet";
            public const string PecWormFile = "PecWormFile";
            public const string Pec360File = "Pec360File";
            public const string PolarLedLevel = "PolarLedLevel";
            
            // Phase 4 Batch 6: Encoder & Hand Controller Properties
            public const string Encoders = "Encoders";
            public const string HcSpeed = "HcSpeed";
            public const string HcMode = "HcMode";
            public const string HcAntiRa = "HcAntiRa";
            public const string HcAntiDec = "HcAntiDec";
            public const string HcFlipEw = "HcFlipEw";
            public const string HcFlipNs = "HcFlipNs";
            public const string DisableKeysOnGoTo = "DisableKeysOnGoTo";
            public const string MinPulseRa = "MinPulseRa";
            public const string MinPulseDec = "MinPulseDec";
            
            // Phase 4 Batch 7: Tracking Rate & Slew Properties
            public const string SiderealRate = "SiderealRate";
            public const string LunarRate = "LunarRate";
            public const string SolarRate = "SolarRate";
            public const string KingRate = "KingRate";
            public const string MaxSlewRate = "MaxSlewRate";
            public const string DisplayInterval = "DisplayInterval";
            public const string St4GuideRate = "St4GuideRate";
            
            // Phase 4 Batch 8: Serial Communication Settings
            public const string Handshake = "Handshake";
            public const string ReadTimeout = "ReadTimeout";
            public const string DataBits = "DataBits";
            public const string DtrEnable = "DtrEnable";
            public const string RtsEnable = "RtsEnable";
            public const string GpsComPort = "GpsComPort";
            public const string GpsBaudRate = "GpsBaudRate";
            
            // Phase 4 Batch 9: UI & Display Settings
            public const string FrontGraphic = "FrontGraphic";
            public const string RaGaugeFlip = "RaGaugeFlip";
            
            // Phase 4 Batch 10: Mount Behavior & Capability Settings
            public const string PolarMode = "PolarMode";
            // Note: The following are new system only or read-only in old system:
            // - DisconnectOnPark (new only)
            // - AutoTrack (read-only in old)
            // - ModelType (new only)
            // - Pressure (new only)
            // - VersionOne (read-only in old)
            // - NumMoveAxis (read-only in old)
            // - NoSyncPastMeridian (read-only in old)
        }
        
        // Helper method for setting JSON values safely
        private static void SetJsonSetting<T>(string key, T value)
        {
            if (_settingsService == null || _isUpdating)
            {
                return;
            }

            try
            {
                _isUpdating = true;
                var settings = _settingsService.GetSettings();
                
                // Use reflection to set the property
                var property = typeof(Settings.Models.SkySettings).GetProperty(key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(settings, value);
                    _settingsService.SaveSettingsAsync(settings).Wait();
                    LogBridge($"Updated {key} = {value}");
                }
            }
            catch (Exception ex)
            {
                LogBridge($"Error setting {key}: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }
        
        // Helper method for safe execution
        private static void SafeExecute(Action action)
        {
            if (_settingsService == null || _isUpdating)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogBridge($"Error in SafeExecute: {ex.Message}");
            }
        }
    }
}
