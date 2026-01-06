# Multi-Telescope Architecture Analysis
**Date**: 2025-01-XX  
**Project**: GreenSwamp Alpaca Server  
**Phase**: Post-Phase 3B - Planning Multi-Telescope Support  

---

## Executive Summary

The GreenSwamp Alpaca Server currently uses a **static `SkyServer` class** with **188+ static state variables** to manage mount control. This architecture creates a **critical blocker** for multi-telescope support, as all `Telescope` driver instances share the same static state.

**Current Status**: ? Phase 3B Complete - Internal code uses `_settings` instance  
**Next Goal**: ?? Enable true multi-telescope support (multiple independent mounts)  
**Blocker**: Static `SkyServer` class prevents independent telescope instances  

---

## Problem Statement

### Current Architecture Issues

```csharp
// Program.cs - Attempts to create multiple telescope instances
DeviceManager.LoadTelescope(0, new Telescope(), "Mount 1", ...); // Device 0
DeviceManager.LoadTelescope(1, new Telescope(), "Mount 2", ...); // Device 1

// ? PROBLEM: All Telescope instances call static SkyServer
public class Telescope : ITelescopeV4
{
    public double Altitude => SkyServer.Altitude;  // Static - shared by ALL
    public bool Connected
    {
        get => SkyServer.IsMountRunning;          // Static - shared by ALL
        set => SkyServer.IsMountRunning = value;
    }
}

// ? PROBLEM: SkyServer has 188+ static state variables
public static partial class SkyServer
{
    private static double _actualAxisX;      // Shared by ALL telescopes
    private static double _actualAxisY;      // Shared by ALL telescopes
    private static bool _tracking;           // Shared by ALL telescopes
    private static bool _mountRunning;       // Shared by ALL telescopes
    private static Vector _appAxes;          // Shared by ALL telescopes
    private static Vector _rateMoveAxes;     // Shared by ALL telescopes
    private static double[] _steps;          // Shared by ALL telescopes
    // ... 181 more static fields that should be per-instance
}
```

**Consequence**: 
- ? Multiple telescopes **cannot operate independently**
- ? Connecting to Mount 1 affects Mount 2's state
- ? Slewing Mount 1 updates Mount 2's position
- ? Tracking state is shared across all mounts

---

## Static State Analysis

### Categories of Static State (188 Total Fields)

#### **1. Position & Coordinate State (25 fields)**
```csharp
private static double _actualAxisX;
private static double _actualAxisY;
private static double _appAxisX;
private static double _appAxisY;
private static Vector _appAxes;
private static Vector _raDec;
private static double _rightAscensionXForm;
private static double _declinationXForm;
private static double[] _steps;
private static double _siderealTime;
private static double _lha;
// ... etc
```
**Per-Instance?**: ? **YES** - Each telescope has unique position

---

#### **2. Mount Control State (35 fields)**
```csharp
private static bool _mountRunning;
private static bool _tracking;
private static bool _isHome;
private static bool _isPulseGuidingRa;
private static bool _isPulseGuidingDec;
private static Vector _rateMoveAxes;
private static bool _moveAxisActive;
private static PointingState _isSideOfPier;
private static Exception _mountError;
private static SlewType _slewState;
private static Vector _targetRaDec;
// ... etc
```
**Per-Instance?**: ? **YES** - Each telescope operates independently

---

#### **3. Hardware State (15 fields)**
```csharp
private static bool _canHomeSensor;
private static bool _canPPec;
private static bool _canPolarLed;
private static bool _canAdvancedCmdSupport;
private static string _capabilities;
private static string _mountName;
private static string _mountVersion;
private static long[] _stepsPerRevolution;
private static double[] _factorStep;
// ... etc
```
**Per-Instance?**: ? **YES** - Each mount has different hardware capabilities

---

#### **4. PEC (Periodic Error Correction) State (15 fields)**
```csharp
private static bool _pecShow;
private static bool _pPecTraining;
private static bool _pPecTrainInProgress;
private static Tuple<int, double, int> _pecBinNow;
private static SortedList<int, Tuple<double, int>> Pec360Master;
private static SortedList<int, Tuple<double, int>> PecWormMaster;
private static SortedList<int, Tuple<double, int>> PecBinsSubs;
// ... etc
```
**Per-Instance?**: ? **YES** - Each mount has unique PEC data

---

#### **5. Timers & Threading Resources (10 fields)**
```csharp
private static MediaTimer _mediaTimer;
private static MediaTimer _altAzTrackingTimer;
private static CancellationTokenSource _ctsGoTo;
private static CancellationTokenSource _ctsPulseGuideRa;
private static CancellationTokenSource _ctsPulseGuideDec;
private static CancellationTokenSource _ctsHcPulseGuide;
private static readonly object TimerLock;
private static readonly object MountPositionUpdatedLock;
// ... etc
```
**Per-Instance?**: ? **YES** - Each mount needs independent timers/cancellation

---

#### **6. UI/Monitor State (12 fields)**
```csharp
private static int _autoHomeProgressBar;
private static bool _autoHomeStop;
private static bool _isAutoHomeRunning;
private static bool _monitorPulse;
private static bool _lowVoltageEventState;
private static ulong _loopCounter;
// ... etc
```
**Per-Instance?**: ? **YES** - Each telescope has independent UI state

---

#### **7. Limit & Park State (8 fields)**
```csharp
private static ParkPosition _parkSelected;
private static LimitStatusType _limitStatus;
private static double _slewSettleTime;
private static bool _snapPort1Result;
private static bool _snapPort2Result;
// ... etc
```
**Per-Instance?**: ? **YES** - Each mount has unique park positions/limits

---

### **Conclusion**: All 188 static fields should be **per-instance state**

---

## Architectural Solutions

### **Option 1: Instance-Based SkyServer (RECOMMENDED)**

#### **Strategy**
Convert `SkyServer` from static class to instance class. Each `Telescope` driver receives its own `SkyServerInstance`.

#### **Architecture Diagram**
```
???????????????????????????????????????????????????????????????
?                      Program.cs (DI)                        ?
???????????????????????????????????????????????????????????????
?  ???????????????????????  ???????????????????????          ?
?  ?SettingsService     ?  ?SettingsService     ?          ?
?  ?  (mount1.json)      ?  ?  (mount2.json)      ?          ?
?  ???????????????????????  ???????????????????????          ?
?         ?                         ?                          ?
?         ?                         ?                          ?
?  ???????????????????????  ???????????????????????          ?
?  ?SkySettingsInstance1?  ?SkySettingsInstance2?          ?
?  ???????????????????????  ???????????????????????          ?
?         ?                         ?                          ?
?         ?                         ?                          ?
?  ???????????????????????  ???????????????????????          ?
?  ?SkyServerInstance1  ?  ?SkyServerInstance2  ?          ?
?  ?  - _actualAxisX     ?  ?  - _actualAxisX     ?          ?
?  ?  - _tracking        ?  ?  - _tracking        ?          ?
?  ?  - _mountRunning    ?  ?  - _mountRunning    ?          ?
?  ?  - _mediaTimer      ?  ?  - _mediaTimer      ?          ?
?  ?  - (88 more fields) ?  ?  - (88 more fields) ?          ?
?  ???????????????????????  ???????????????????????          ?
?         ?                         ?                          ?
?         ?                         ?                          ?
?  ???????????????????????  ???????????????????????          ?
?  ?Telescope1 (Device0)?  ?Telescope2 (Device1)?          ?
?  ?  (ASCOM Driver)     ?  ?  (ASCOM Driver)     ?          ?
?  ???????????????????????  ???????????????????????          ?
???????????????????????????????????????????????????????????????

Static Utilities (Stateless):
???????????????????????????????????????????????????????????????
?  Axes.cs         - Coordinate transformations              ?
?  Transforms.cs   - Coordinate type conversions             ?
?  SkySettings     - Static facade (delegates to _instance)  ?
???????????????????????????????????????????????????????????????
```

---

#### **Implementation Overview**

##### **Step 1: Create `SkyServerInstance.cs`**

```csharp
namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Instance-based mount server - supports multiple independent telescopes
    /// Replaces static SkyServer for multi-telescope support
    /// </summary>
    public class SkyServerInstance : IDisposable, INotifyPropertyChanged
    {
        #region Instance Fields (formerly static)
        
        // Settings
        private readonly SkySettingsInstance _settings;
        private readonly string _instanceId;
        
        // Position state (25 fields)
        private double _actualAxisX;
        private double _actualAxisY;
        private double _appAxisX;
        private double _appAxisY;
        private Vector _appAxes;
        private Vector _raDec;
        private double _rightAscensionXForm;
        private double _declinationXForm;
        private double[] _steps = { 0.0, 0.0 };
        private double _siderealTime;
        private double _lha;
        // ... 14 more position fields
        
        // Mount control state (35 fields)
        private bool _mountRunning;
        private bool _tracking;
        private bool _isHome;
        private bool _isPulseGuidingRa;
        private bool _isPulseGuidingDec;
        private Vector _rateMoveAxes;
        private bool _moveAxisActive;
        private PointingState _isSideOfPier;
        private Exception? _mountError;
        private SlewType _slewState;
        // ... 25 more control fields
        
        // Hardware state (15 fields)
        private bool _canHomeSensor;
        private bool _canPPec;
        private string? _capabilities;
        private long[] _stepsPerRevolution = { 0, 0 };
        private double[] _factorStep = { 0.0, 0.0 };
        // ... 10 more hardware fields
        
        // PEC state (15 fields)
        private bool _pecShow;
        private Tuple<int, double, int>? _pecBinNow;
        private SortedList<int, Tuple<double, int>>? _pec360Master;
        // ... 12 more PEC fields
        
        // Timers & resources (10 fields)
        private MediaTimer? _mediaTimer;
        private MediaTimer? _altAzTrackingTimer;
        private CancellationTokenSource? _ctsGoTo;
        private CancellationTokenSource? _ctsPulseGuideRa;
        private CancellationTokenSource? _ctsPulseGuideDec;
        private readonly object _timerLock = new();
        // ... 4 more resource fields
        
        // UI/Monitor state (12 fields)
        private int _autoHomeProgressBar;
        private bool _autoHomeStop;
        private bool _isAutoHomeRunning;
        private ulong _loopCounter;
        // ... 8 more UI fields
        
        // Limit/Park state (8 fields)
        private ParkPosition? _parkSelected;
        private LimitStatusType _limitStatus;
        private double _slewSettleTime;
        // ... 5 more limit/park fields
        
        #endregion
        
        #region Constructor & Initialization
        
        public SkyServerInstance(SkySettingsInstance settings, string instanceId = "default")
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _instanceId = instanceId;
            
            // Initialize instance state
            _appAxes = new Vector(double.NaN, double.NaN);
            _rateMoveAxes = new Vector(0, 0);
            _raDec = new Vector(0, 0);
            
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"SkyServerInstance created | ID:{_instanceId} | Mount:{_settings.Mount} | Port:{_settings.Port}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }
        
        #endregion
        
        #region Public Properties (formerly static)
        
        /// <summary>
        /// Unique identifier for this mount instance
        /// </summary>
        public string InstanceId => _instanceId;
        
        /// <summary>
        /// Settings instance for this mount
        /// </summary>
        public SkySettingsInstance Settings => _settings;
        
        /// <summary>
        /// Actual axis X position in degrees
        /// </summary>
        public double ActualAxisX
        {
            get => _actualAxisX;
            set
            {
                _actualAxisX = value;
                OnPropertyChanged();
            }
        }
        
        /// <summary>
        /// Actual axis Y position in degrees
        /// </summary>
        public double ActualAxisY
        {
            get => _actualAxisY;
            set
            {
                _actualAxisY = value;
                OnPropertyChanged();
            }
        }
        
        /// <summary>
        /// Mount altitude (degrees)
        /// </summary>
        public double Altitude { get; private set; }
        
        /// <summary>
        /// Mount azimuth (degrees)
        /// </summary>
        public double Azimuth { get; private set; }
        
        /// <summary>
        /// Right ascension (hours)
        /// </summary>
        public double RightAscension { get; private set; }
        
        /// <summary>
        /// Declination (degrees)
        /// </summary>
        public double Declination { get; private set; }
        
        /// <summary>
        /// Is mount running/connected
        /// </summary>
        public bool IsMountRunning
        {
            get => _mountRunning;
            set
            {
                if (_mountRunning == value) return;
                _mountRunning = value;
                _loopCounter = 0;
                
                if (value)
                    MountStart();
                else
                    MountStop();
                
                OnPropertyChanged();
            }
        }
        
        /// <summary>
        /// Is mount tracking
        /// </summary>
        public bool Tracking
        {
            get => _tracking;
            set
            {
                if (_tracking == value) return;
                _tracking = value;
                
                if (value)
                    SetTracking();
                else
                    StopTracking();
                
                OnPropertyChanged();
            }
        }
        
        /// <summary>
        /// Current mount position steps
        /// </summary>
        public double[] Steps
        {
            get => _steps;
            set
            {
                _steps = value;
                UpdatePositionFromSteps(value);
                OnPropertyChanged();
            }
        }
        
        // ... 170+ more public properties (converted from static)
        
        #endregion
        
        #region Core Operations (formerly static methods)
        
        /// <summary>
        /// Start mount operations (timers, queues, etc.)
        /// </summary>
        public void MountStart()
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Starting mount instance | ID:{_instanceId} | Mount:{_settings.Mount}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            
            // Start mount-specific resources
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    MountQueue.Start();
                    break;
                case MountType.SkyWatcher:
                    SkyQueue.Start(SkySystem.Serial, ...);
                    break;
            }
            
            // Start instance timers
            _mediaTimer = new MediaTimer 
            { 
                Period = _settings.DisplayInterval, 
                Resolution = 5 
            };
            _mediaTimer.Tick += UpdateServerEvent;
            _mediaTimer.Start();
            
            _altAzTrackingTimer = new MediaTimer 
            { 
                Period = _settings.AltAzTrackingUpdateInterval, 
                Resolution = 5 
            };
            _altAzTrackingTimer.Tick += AltAzTrackingTimerEvent;
        }
        
        /// <summary>
        /// Stop mount operations and cleanup resources
        /// </summary>
        public void MountStop()
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Stopping mount instance | ID:{_instanceId}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            
            // Stop tracking and cancel all operations
            Tracking = false;
            CancelAllAsync();
            
            // Stop timers
            if (_mediaTimer != null)
            {
                _mediaTimer.Tick -= UpdateServerEvent;
                _mediaTimer.Stop();
                _mediaTimer.Dispose();
                _mediaTimer = null;
            }
            
            if (_altAzTrackingTimer != null)
            {
                _altAzTrackingTimer.Tick -= AltAzTrackingTimerEvent;
                _altAzTrackingTimer.Stop();
                _altAzTrackingTimer.Dispose();
                _altAzTrackingTimer = null;
            }
            
            // Stop queues
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    MountQueue.Stop();
                    break;
                case MountType.SkyWatcher:
                    SkyQueue.Stop();
                    SkySystem.ConnectSerial = false;
                    break;
            }
        }
        
        /// <summary>
        /// Slew mount to target position
        /// </summary>
        public void SlewMount(Vector targetPosition, SlewType slewState, 
            bool tracking = false, bool async = true)
        {
            // Instance-specific slew logic
            // All state updates affect THIS instance only
        }
        
        /// <summary>
        /// Cancel all async operations (goto, pulse guide, etc.)
        /// </summary>
        private void CancelAllAsync()
        {
            _ctsGoTo?.Cancel();
            _ctsPulseGuideRa?.Cancel();
            _ctsPulseGuideDec?.Cancel();
            _ctsHcPulseGuide?.Cancel();
        }
        
        /// <summary>
        /// Set tracking rate
        /// </summary>
        private void SetTracking()
        {
            // Instance-specific tracking logic
        }
        
        /// <summary>
        /// Stop tracking
        /// </summary>
        private void StopTracking()
        {
            // Instance-specific stop logic
        }
        
        /// <summary>
        /// Update position from steps
        /// </summary>
        private void UpdatePositionFromSteps(double[] steps)
        {
            // Convert steps to degrees
            var rawPositions = new[] 
            { 
                ConvertStepsToDegrees(steps[0], 0), 
                ConvertStepsToDegrees(steps[1], 1) 
            };
            
            // Update limit status
            UpdateMountLimitStatus(rawPositions);
            
            // Convert to axis position
            rawPositions = GetUnsyncedAxes(rawPositions);
            
            // Update UI diagnostics
            ActualAxisX = rawPositions[0];
            ActualAxisY = rawPositions[1];
            
            // Convert to app axes
            var axes = Axes.AxesMountToApp(rawPositions);
            _appAxes.X = axes[0];
            _appAxes.Y = axes[1];
            
            // Calculate Alt/Az
            var altAz = Axes.AxesXyToAzAlt(axes);
            Azimuth = altAz[0];
            Altitude = altAz[1];
            
            // Calculate Ra/Dec
            var raDec = Axes.AxesXyToRaDec(axes);
            RightAscension = raDec[0];
            Declination = raDec[1];
        }
        
        // ... 50+ more methods (converted from static)
        
        #endregion
        
        #region Event Handlers
        
        private void UpdateServerEvent(object? sender, EventArgs e)
        {
            // Update THIS instance's position and state
            lock (_timerLock)
            {
                _loopCounter++;
                
                // Get steps from mount
                UpdateSteps();
                
                // Update sidereal time
                _siderealTime = GetLocalSiderealTime();
                
                // Check slew state
                CheckSlewState();
                
                // Check limits
                CheckAxisLimits();
            }
        }
        
        private void AltAzTrackingTimerEvent(object? sender, EventArgs e)
        {
            // Update AltAz tracking for THIS instance
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            MountStop();
            
            _ctsGoTo?.Dispose();
            _ctsPulseGuideRa?.Dispose();
            _ctsPulseGuideDec?.Dispose();
            _ctsHcPulseGuide?.Dispose();
        }
        
        #endregion
        
        #region INotifyPropertyChanged
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        #endregion
    }
}
```

---

##### **Step 2: Update `Telescope.cs` Driver**

```csharp
namespace GreenSwamp.Alpaca.Server.TelescopeDriver
{
    /// <summary>
    /// ASCOM Telescope driver - instance-based for multi-telescope support
    /// </summary>
    public class Telescope : ITelescopeV4, IDisposable
    {
        #region Fields
        
        // Instance-specific mount server
        private readonly SkyServerInstance _mountServer;
        private readonly int _deviceId;
        private readonly string _deviceName;
        
        // Rate collections (per driver instance)
        private AxisRates[] _mAxisRates;
        private TrackingRates _mTrackingRates;
        private TrackingRatesSimple _mTrackingRatesSimple;
        
        #endregion
        
        #region Constructor
        
        /// <summary>
        /// Create telescope driver with mount server instance
        /// </summary>
        /// <param name="mountServer">Instance of mount server for THIS telescope</param>
        /// <param name="deviceId">ASCOM device number (0, 1, 2, etc.)</param>
        /// <param name="deviceName">Human-readable device name</param>
        public Telescope(SkyServerInstance mountServer, int deviceId = 0, 
            string deviceName = "Telescope")
        {
            _mountServer = mountServer ?? throw new ArgumentNullException(nameof(mountServer));
            _deviceId = deviceId;
            _deviceName = deviceName;
            
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Telescope driver created | DeviceId:{deviceId} | Name:{deviceName} | MountInstance:{_mountServer.InstanceId}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            
            // Initialize rate collections
            _mAxisRates = new AxisRates[3];
            _mAxisRates[0] = new AxisRates(TelescopeAxis.Primary);
            _mAxisRates[1] = new AxisRates(TelescopeAxis.Secondary);
            _mAxisRates[2] = new AxisRates(TelescopeAxis.Tertiary);
            _mTrackingRates = new TrackingRates();
            _mTrackingRatesSimple = new TrackingRatesSimple();
        }
        
        #endregion
        
        #region ASCOM Properties (ITelescopeV4)
        
        public AlignmentMode AlignmentMode
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanAlignMode, "AlignmentMode");
                var r = _mountServer.Settings.AlignmentMode;
                
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Telescope,
                    Category = MonitorCategory.Driver,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"[{_deviceId}] {r}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                
                return r;
            }
        }
        
        public double Altitude
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanAltAz, "Altitude", false);
                var r = _mountServer.Altitude;  // ? Instance-specific
                
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Telescope,
                    Category = MonitorCategory.Driver,
                    Type = MonitorType.Data,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"[{_deviceId}] {r}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                
                return r;
            }
        }
        
        public double Azimuth
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanAltAz, "Azimuth", false);
                return _mountServer.Azimuth;  // ? Instance-specific
            }
        }
        
        public bool Connected
        {
            get => _mountServer.IsMountRunning;  // ? Instance-specific
            set
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Telescope,
                    Category = MonitorCategory.Driver,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"[{_deviceId}] Setting Connected = {value}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                
                _mountServer.IsMountRunning = value;  // ? Instance-specific
            }
        }
        
        public double Declination
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanEquatorial, "Declination", false);
                return _mountServer.Declination;  // ? Instance-specific
            }
        }
        
        public double RightAscension
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanEquatorial, "RightAscension", false);
                return _mountServer.RightAscension;  // ? Instance-specific
            }
        }
        
        public bool Tracking
        {
            get
            {
                CheckCapability(_mountServer.Settings.CanSetTracking, "Tracking", false);
                return _mountServer.Tracking;  // ? Instance-specific
            }
            set
            {
                CheckCapability(_mountServer.Settings.CanSetTracking, "Tracking");
                _mountServer.Tracking = value;  // ? Instance-specific
            }
        }
        
        // ... all other ASCOM properties use _mountServer instance
        
        #endregion
        
        #region ASCOM Methods (ITelescopeV4)
        
        public void SlewToCoordinates(double ra, double dec)
        {
            CheckCapability(_mountServer.Settings.CanSlew, "SlewToCoordinates");
            
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"[{_deviceId}] RA:{ra} Dec:{dec}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            
            // Use instance-specific slew
            _mountServer.SlewMount(new Vector(ra, dec), SlewType.SlewRaDec, async: false);
        }
        
        public void AbortSlew()
        {
            CheckCapability(_mountServer.Settings.CanSlew, "AbortSlew");
            _mountServer.AbortSlew();  // ? Instance-specific
        }
        
        // ... all other ASCOM methods use _mountServer instance
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"[{_deviceId}] Telescope driver disposing"
            };
            MonitorLog.LogToMonitor(monitorItem);
            
            // Note: Don't dispose _mountServer - it may be shared or managed by DI
        }
        
        #endregion
    }
}
```

---

##### **Step 3: Update `Program.cs` for Multi-Telescope**

```csharp
// GreenSwamp.Alpaca.Server/Program.cs
public static async Task Main(string[] args)
{
    // ... existing WebApplication setup ...
    
    var app = builder.Build();
    
    try
    {
        var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
        
        // Multi-telescope configuration
        var telescopeConfigs = new[]
        {
            new 
            { 
                DeviceId = 0, 
                Name = "SkyWatcher EQ6-R", 
                SettingsFile = "mount1.json",
                InstanceId = "mount_primary"
            },
            // Uncomment to enable second telescope
            // new 
            // { 
            //     DeviceId = 1, 
            //     Name = "Simulator Mount", 
            //     SettingsFile = "mount2.json",
            //     InstanceId = "mount_secondary"
            // },
        };
        
        foreach (var config in telescopeConfigs)
        {
            Logger.LogInformation($"Initializing telescope device {config.DeviceId}: {config.Name}");
            
            // Create settings instance for this mount
            var settingsInstance = new SkySettingsInstance(
                settingsService, 
                config.SettingsFile
            );
            Logger.LogInformation($"? Settings instance created: {config.SettingsFile}");
            
            // Create mount server instance
            var mountServer = new SkyServerInstance(
                settingsInstance, 
                config.InstanceId
            );
            Logger.LogInformation($"? Mount server instance created: {config.InstanceId}");
            
            // Create telescope driver with mount instance
            var telescope = new Telescope(
                mountServer, 
                config.DeviceId, 
                config.Name
            );
            Logger.LogInformation($"? Telescope driver created: {config.Name}");
            
            // Register with ASCOM device manager
            DeviceManager.LoadTelescope(
                config.DeviceId, 
                telescope, 
                config.Name,
                ServerSettings.GetDeviceUniqueId("Telescope", config.DeviceId)
            );
            
            Logger.LogInformation($"? Loaded telescope device {config.DeviceId}: {config.Name}");
        }
        
        Logger.LogInformation($"? All telescope devices initialized ({telescopeConfigs.Length} total)");
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to initialize telescopes: {ex.Message}");
        throw;
    }
    
    // ... rest of Program.cs ...
}
```

---

##### **Step 4: Keep Static `SkyServer` as Utility Facade**

```csharp
// GreenSwamp.Alpaca.MountControl/SkyServer.cs (simplified)
namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Static facade for backward compatibility and utilities
    /// For multi-telescope, use SkyServerInstance directly
    /// </summary>
    public static partial class SkyServer
    {
        // Static utilities (no state)
        public static double DegToRad(double degree) 
            => degree * (Math.PI / 180.0);
        
        public static double RadToDeg(double rad) 
            => rad * (180.0 / Math.PI);
        
        // Default instance for backward compatibility
        private static SkyServerInstance? _defaultInstance;
        
        /// <summary>
        /// Initialize default instance for backward compatibility
        /// New code should use SkyServerInstance directly
        /// </summary>
        public static void Initialize(SkyServerInstance defaultInstance)
        {
            _defaultInstance = defaultInstance;
            
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Static SkyServer facade initialized (default instance: {defaultInstance.InstanceId})"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }
        
        // Legacy properties forward to default instance
        public static double Altitude => _defaultInstance?.Altitude ?? double.NaN;
        public static double Azimuth => _defaultInstance?.Azimuth ?? double.NaN;
        public static bool IsMountRunning
        {
            get => _defaultInstance?.IsMountRunning ?? false;
            set { if (_defaultInstance != null) _defaultInstance.IsMountRunning = value; }
        }
        
        // ... other legacy properties forward to _defaultInstance
    }
}
```

---

### **Option 2: Service Locator Pattern (NOT RECOMMENDED)**

#### **Strategy**
Keep `SkyServer` static, use `Dictionary<int, MountState>` to route calls based on device ID.

#### **Why NOT Recommended**:
- ? Thread-safety complexity
- ? Hidden dependencies (context-based routing)
- ? Difficult to test
- ? Error-prone (forget to set device context)

#### **Example** (for reference only):
```csharp
public static partial class SkyServer
{
    private static readonly Dictionary<int, MountState> _mountStates = new();
    
    [ThreadStatic]
    private static int _currentDeviceId;
    
    public static void RegisterMount(int deviceId, SkySettingsInstance settings)
    {
        _mountStates[deviceId] = new MountState(settings);
    }
    
    public static void SetCurrentDevice(int deviceId)
    {
        _currentDeviceId = deviceId;
    }
    
    private static MountState CurrentMount => _mountStates[_currentDeviceId];
    
    public static double Altitude => CurrentMount.Altitude;
}

// Telescope.cs
public double Altitude
{
    get
    {
        SkyServer.SetCurrentDevice(_deviceId);  // ?? FRAGILE
        return SkyServer.Altitude;
    }
}
```

---

## Implementation Roadmap

### **Phase 4A: Create SkyServerInstance** (Week 1-2)

| Task | Effort | Priority |
|------|--------|----------|
| Create `SkyServerInstance.cs` skeleton | 4 hours | ?? HIGH |
| Move 25 position fields to instance | 4 hours | ?? HIGH |
| Move 35 control fields to instance | 8 hours | ?? HIGH |
| Move 15 hardware fields to instance | 4 hours | ?? MEDIUM |
| Move 15 PEC fields to instance | 4 hours | ?? MEDIUM |
| Move 10 timer/resource fields to instance | 4 hours | ?? HIGH |
| Move 12 UI fields to instance | 2 hours | ?? LOW |
| Move 8 limit/park fields to instance | 2 hours | ?? LOW |
| Convert static methods to instance | 16 hours | ?? HIGH |
| Implement IDisposable | 2 hours | ?? HIGH |
| **Total** | **50 hours** | |

---

### **Phase 4B: Update Telescope Driver** (Week 2)

| Task | Effort | Priority |
|------|--------|----------|
| Add `SkyServerInstance` constructor parameter | 1 hour | ?? HIGH |
| Replace all `SkyServer.Property` with `_mountServer.Property` | 8 hours | ?? HIGH |
| Test single telescope with instance | 4 hours | ?? HIGH |
| **Total** | **13 hours** | |

---

### **Phase 4C: Update Program.cs** (Week 3)

| Task | Effort | Priority |
|------|--------|----------|
| Implement multi-telescope configuration | 2 hours | ?? HIGH |
| Test with 1 telescope (regression) | 2 hours | ?? HIGH |
| Test with 2 telescopes (real multi) | 4 hours | ?? HIGH |
| Update documentation | 4 hours | ?? MEDIUM |
| **Total** | **12 hours** | |

---

### **Phase 4D: Utility Classes** (Week 3-4)

| Task | Effort | Priority |
|------|--------|----------|
| Keep `Axes.cs` using static facade | 1 hour | ?? LOW |
| Keep `Transforms.cs` using static facade | 1 hour | ?? LOW |
| Update `SkyServer` static facade | 4 hours | ?? MEDIUM |
| **Total** | **6 hours** | |

---

### **Total Estimated Effort: 81 hours (~10 working days)**

---

## Migration Strategy

### **Approach: Incremental Migration**

#### **Step 1: Proof of Concept** (2 days)
- Create `SkyServerInstance` with 10 critical fields only:
  - `_mountRunning`, `_tracking`, `_actualAxisX`, `_actualAxisY`, `_steps`
- Update `Telescope` to accept instance (optional parameter)
- Test single telescope with instance
- **Goal**: Validate architecture works

#### **Step 2: Full Field Migration** (5 days)
- Migrate remaining 178 fields to instance
- Convert all static methods to instance methods
- Keep static `SkyServer` as facade forwarding to default instance
- **Goal**: Complete instance implementation

#### **Step 3: Multi-Telescope Testing** (2 days)
- Update `Program.cs` for multi-telescope config
- Test with 2 simulators
- Test with 1 real + 1 simulator
- Fix any state leakage issues
- **Goal**: Verify true independence

#### **Step 4: Documentation & Cleanup** (1 day)
- Document multi-telescope configuration
- Update README with examples
- Remove obsolete code
- **Goal**: Production-ready

---

## Testing Strategy

### **Unit Tests**

```csharp
[TestClass]
public class SkyServerInstanceTests
{
    [TestMethod]
    public void TwoInstances_HaveIndependentState()
    {
        // Arrange
        var settings1 = new SkySettingsInstance(...);
        var settings2 = new SkySettingsInstance(...);
        
        var mount1 = new SkyServerInstance(settings1, "mount1");
        var mount2 = new SkyServerInstance(settings2, "mount2");
        
        // Act
        mount1.ActualAxisX = 10.0;
        mount2.ActualAxisX = 20.0;
        
        // Assert
        Assert.AreEqual(10.0, mount1.ActualAxisX);
        Assert.AreEqual(20.0, mount2.ActualAxisX);  // ? Independent!
    }
    
    [TestMethod]
    public void TwoInstances_CanRunConcurrently()
    {
        // Arrange
        var mount1 = new SkyServerInstance(..., "mount1");
        var mount2 = new SkyServerInstance(..., "mount2");
        
        // Act
        mount1.IsMountRunning = true;
        mount2.IsMountRunning = true;
        
        // Assert
        Assert.IsTrue(mount1.IsMountRunning);
        Assert.IsTrue(mount2.IsMountRunning);
        
        // Act - Stop mount1
        mount1.IsMountRunning = false;
        
        // Assert - mount2 unaffected
        Assert.IsFalse(mount1.IsMountRunning);
        Assert.IsTrue(mount2.IsMountRunning);  // ? Independent!
    }
}
```

---

### **Integration Tests**

```csharp
[TestClass]
public class MultiTelescopeIntegrationTests
{
    [TestMethod]
    public async Task TwoTelescopes_CanSlewIndependently()
    {
        // Arrange
        var tel1 = CreateTelescope(deviceId: 0, "Mount1");
        var tel2 = CreateTelescope(deviceId: 1, "Mount2");
        
        tel1.Connected = true;
        tel2.Connected = true;
        
        // Act - Slew mount1 to target A
        tel1.SlewToCoordinates(ra: 5.0, dec: 30.0);
        await Task.Delay(100);
        
        // Act - Slew mount2 to target B
        tel2.SlewToCoordinates(ra: 10.0, dec: -20.0);
        await Task.Delay(100);
        
        // Assert - Different positions
        Assert.AreNotEqual(tel1.RightAscension, tel2.RightAscension);
        Assert.AreNotEqual(tel1.Declination, tel2.Declination);
        
        // Assert - Independent slewing state
        Assert.IsTrue(tel1.Slewing);
        Assert.IsTrue(tel2.Slewing);
    }
}
```

---

## Backward Compatibility

### **Static Facade Preservation**

For code that currently uses static `SkyServer`, maintain backward compatibility:

```csharp
// Old code (still works via facade)
var altitude = SkyServer.Altitude;  // ? Works - delegates to default instance

// New code (preferred for multi-telescope)
var altitude = mountServer.Altitude;  // ? Direct instance access
```

### **Migration Path**

1. **Phase 4A-4B**: Static facade forwards to default instance
2. **Phase 4C**: New telescopes use instance directly
3. **Phase 5** (future): Gradually migrate UI/Blazor to use instances via DI
4. **Phase 6** (future): Remove static facade when all code migrated

---

## Benefits of Instance-Based Architecture

### **1. True Multi-Telescope Support**
- ? Each telescope has completely independent state
- ? No shared static variables causing conflicts
- ? Can connect/disconnect mounts independently

### **2. Thread Safety**
- ? No static state = no thread synchronization issues
- ? Each instance has its own locks/resources
- ? Concurrent operations safe by design

### **3. Testability**
- ? Easy to create isolated test instances
- ? No global state pollution between tests
- ? Can mock/stub individual instances

### **4. Scalability**
- ? Support unlimited number of telescopes
- ? Easy to add/remove telescopes at runtime
- ? Resource cleanup per instance (IDisposable)

### **5. Clarity**
- ? Explicit dependencies (constructor injection)
- ? No hidden context or thread-local storage
- ? Easy to trace state ownership

---

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Large refactoring breaks existing code** | ?? HIGH | Incremental migration with static facade for backward compatibility |
| **Performance overhead from instances** | ?? MEDIUM | Instances are lightweight; no significant overhead |
| **Utility classes need refactoring** | ?? LOW | Keep using static facade for utilities (`Axes`, `Transforms`) |
| **Testing complexity** | ?? MEDIUM | Comprehensive test suite before/during migration |
| **UI/Blazor needs DI updates** | ?? MEDIUM | Phase 5 (future) - UI continues using static facade initially |

---

## Next Steps

### **Immediate Actions** (Week 1)

1. ? Review this architectural analysis with team
2. ? Create `SkyServerInstance.cs` skeleton
3. ? Implement proof-of-concept with 10 fields
4. ? Update `Telescope.cs` to accept instance (optional)
5. ? Test single telescope with instance

### **Short Term** (Weeks 2-3)

6. ? Migrate all 188 fields to instance
7. ? Convert all static methods to instance
8. ? Update `Program.cs` for multi-telescope
9. ? Test with 2 telescopes

### **Medium Term** (Weeks 4-6)

10. ? Update Blazor UI to use DI for instances
11. ? Remove static facade (if all code migrated)
12. ? Production deployment

---

## Conclusion

The current static `SkyServer` architecture is a **critical blocker** for multi-telescope support. The **instance-based architecture (Option 1)** provides:

- ? **True multi-telescope support** (unlimited independent mounts)
- ? **Clean architecture** (no static state, proper DI)
- ? **Thread safety** (no shared mutable state)
- ? **Testability** (isolated instances)
- ? **Backward compatibility** (static facade during migration)

**Recommended Path**: **Option 1 - Full instance migration over 3-4 weeks**

**Estimated Effort**: 81 hours (~10 working days)  
**Risk Level**: Medium (mitigated by incremental approach)  
**Reward**: Foundation for true multi-telescope observatory support  

---

## References

- **Current Implementation**: `GreenSwamp.Alpaca.MountControl/SkyServer.cs`
- **ASCOM Driver**: `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`
- **Settings System**: `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`
- **Related Work**: Phase 3B (completed) - Settings instance migration

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-XX  
**Status**: Planning / Awaiting Approval
