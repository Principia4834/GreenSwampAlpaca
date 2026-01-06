# SkyServer Modular Architecture Analysis
**Date**: 2025-01-XX  
**Project**: GreenSwamp Alpaca Server  
**Phase**: Post-Phase 3B - Architectural Refactoring Planning  
**Related**: [Multi-Telescope Architecture Analysis](Multi-Telescope-Architecture-Analysis.md)

---

## Executive Summary

The current `SkyServer` class is a **monolithic static class** with:
- **188+ static state variables** across 7 partial files
- **100+ methods** handling diverse concerns
- **Violations of SOLID principles** throughout

This document proposes **modular architecture patterns** to:
1. ? Break down monolithic class into manageable modules
2. ? Enable dependency injection for testability
3. ? Support multi-telescope instances
4. ? Improve maintainability and extensibility

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [SOLID Principle Violations](#solid-principle-violations)
3. [Proposed Architectures](#proposed-architectures)
   - [Option A: DI-Based Capability Modules](#option-a-di-based-capability-modules)
   - [Option B: Pipeline Architecture](#option-b-pipeline-architecture)
   - [Option C: Event-Driven Architecture](#option-c-event-driven-architecture)
   - [Option D: Actor Model](#option-d-actor-model)
   - [Option E: Domain-Driven Design](#option-e-domain-driven-design)
   - [**RECOMMENDED: Hybrid Approach**](#recommended-hybrid-approach)
4. [Architecture Comparison](#architecture-comparison)
5. [Implementation Plan](#implementation-plan)
6. [Migration Strategy](#migration-strategy)
7. [Code Examples](#code-examples)
8. [Conclusion](#conclusion)

---

## Problem Statement

### Current Architecture Issues

#### **1. Monolithic God Object**

```csharp
// SkyServer.cs + 6 partial files = ~8000 lines of code
public static partial class SkyServer
{
    // 188+ static fields across 7 categories
    private static double _actualAxisX;
    private static double _actualAxisY;
    private static bool _tracking;
    private static bool _mountRunning;
    private static Vector _appAxes;
    private static Vector _rateMoveAxes;
    private static MediaTimer _mediaTimer;
    private static CancellationTokenSource _ctsGoTo;
    private static SortedList<int, Tuple<double, int>> _pecWormMaster;
    private static ParkPosition _parkSelected;
    private static Exception _mountError;
    // ... 177 more static fields
    
    // 100+ methods across multiple concerns
    public static void MountStart() { /* ... */ }
    public static void SlewMount() { /* ... */ }
    public static void UpdatePositionFromSteps() { /* ... */ }
    public static void ApplyPecCorrection() { /* ... */ }
    public static void CheckLimits() { /* ... */ }
    public static void UpdateUI() { /* ... */ }
    public static void HandleErrors() { /* ... */ }
    // ... 93 more methods
}
```

#### **2. Multiple Responsibilities**

| Responsibility | Lines of Code | Files |
|---------------|---------------|-------|
| Position Tracking | ~800 | SkyServer.Core.cs |
| Mount Control | ~1200 | SkyServer.Core.cs |
| Slewing Logic | ~600 | SkyServer.Core.cs |
| PEC Management | ~500 | SkyServer.cs |
| Limit Checking | ~300 | SkyServer.TelescopeAPI.cs |
| UI Updates | ~400 | Multiple |
| Error Handling | ~200 | SkyServer.Core.cs |
| **Total** | **~8000** | **7 partial files** |

---

## SOLID Principle Violations

### **Single Responsibility Principle** ?

**Violation**: SkyServer handles 7+ distinct concerns:

```csharp
public static partial class SkyServer
{
    // Concern 1: Position Tracking
    public static double Altitude { get; }
    public static double RightAscension { get; }
    
    // Concern 2: Mount Control
    public static void MountStart() { }
    public static void MountStop() { }
    
    // Concern 3: Slewing
    public static void SlewToCoordinates() { }
    public static void AbortSlew() { }
    
    // Concern 4: PEC
    public static void LoadPecFile() { }
    public static void ApplyPecCorrection() { }
    
    // Concern 5: Limit Checking
    public static void CheckAxisLimits() { }
    
    // Concern 6: UI Updates
    public static void UpdateServerEvent() { }
    
    // Concern 7: Error Handling
    public static void SkyErrorHandler() { }
}
```

**Impact**:
- Changes to position tracking may break PEC
- Testing slewing requires mocking UI updates
- Error in one concern affects all others

---

### **Open/Closed Principle** ?

**Violation**: Adding new mount types requires modifying core class:

```csharp
public static void MountStart()
{
    switch (_settings!.Mount)
    {
        case MountType.Simulator:
            MountQueue.Start();
            break;
        case MountType.SkyWatcher:
            SkyQueue.Start(...);
            break;
        // ? Adding new mount type requires editing this method
        // case MountType.Meade:
        //     MeadeQueue.Start();
        //     break;
    }
}
```

**Better Approach**: Strategy pattern with mount-specific implementations.

---

### **Liskov Substitution Principle** ?

**Violation**: Static methods prevent polymorphism:

```csharp
// ? Cannot substitute different implementations
public static void SlewToCoordinates(double ra, double dec)
{
    // Hard-coded logic
}

// ? Would enable substitution
public interface ISlewController
{
    void SlewToCoordinates(double ra, double dec);
}

public class PrecisionSlewController : ISlewController { }
public class FastSlewController : ISlewController { }
```

---

### **Interface Segregation Principle** ?

**Violation**: Massive class forces clients to depend on methods they don't use:

```csharp
// Telescope driver only needs position properties
public class Telescope : ITelescopeV4
{
    public double Altitude => SkyServer.Altitude;  // Uses 1 property
    
    // But SkyServer exposes 188 fields + 100 methods
    // Telescope is coupled to ALL of them!
}
```

---

### **Dependency Inversion Principle** ?

**Violation**: Direct dependencies on concrete implementations:

```csharp
public static void UpdateSteps()
{
    // ? Direct dependency on concrete SkyQueue
    var steps = new SkyGetSteps(SkyQueue.NewId);
    _steps = (double[])SkyQueue.GetCommandResult(steps).Result;
    
    // ? Should depend on abstraction
    // var steps = _mountCommunicator.GetSteps();
}
```

---

## Proposed Architectures

### **Option A: DI-Based Capability Modules**

#### **Concept**

Break `SkyServer` into **focused capability modules**, each with a single responsibility. Use **dependency injection** to compose them.

#### **Architecture Diagram**

```
???????????????????????????????????????????????????????????????
?              SkyServerInstance (Coordinator)                ?
?  - Minimal state (_settings, _instanceId)                  ?
?  - Delegates to capability modules                          ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  Injected Capability Modules:                              ?
?                                                             ?
?  ????????????????????  ????????????????????               ?
?  ? IPositionTracker ?  ? IMountController ?               ?
?  ?  - Altitude      ?  ?  - Start()       ?               ?
?  ?  - Azimuth       ?  ?  - Stop()        ?               ?
?  ?  - RightAsc      ?  ?  - IsRunning     ?               ?
?  ?  - Declination   ?  ?                  ?               ?
?  ????????????????????  ????????????????????               ?
?                                                             ?
?  ????????????????????  ????????????????????               ?
?  ? ISlewController  ?  ? IPecManager      ?               ?
?  ?  - SlewTo()      ?  ?  - LoadFile()    ?               ?
?  ?  - AbortSlew()   ?  ?  - ApplyCorrect()?               ?
?  ?  - IsSlewng      ?  ?  - PecOn         ?               ?
?  ????????????????????  ????????????????????               ?
?                                                             ?
?  ????????????????????  ????????????????????               ?
?  ? ITrackingEngine  ?  ? ILimitChecker    ?               ?
?  ?  - SetTracking() ?  ?  - CheckLimits() ?               ?
?  ?  - TrackingMode  ?  ?  - AtLimit       ?               ?
?  ????????????????????  ????????????????????               ?
???????????????????????????????????????????????????????????????
```

#### **Module Definitions**

##### **1. IPositionTracker**

```csharp
/// <summary>
/// Manages position calculations and coordinate transformations
/// Responsibility: Track mount position in various coordinate systems
/// </summary>
public interface IPositionTracker
{
    // Current position
    double Altitude { get; }
    double Azimuth { get; }
    double RightAscension { get; }
    double Declination { get; }
    double ActualAxisX { get; }
    double ActualAxisY { get; }
    
    // Update position from mount
    Task UpdateFromStepsAsync(double[] steps);
    
    // Coordinate conversions
    double[] ConvertStepsToDegrees(double[] steps);
    (double ra, double dec) CalculateRaDec(double[] axisPositions);
    (double alt, double az) CalculateAltAz(double[] axisPositions);
    
    // Events
    event EventHandler<PositionChangedEventArgs> PositionChanged;
}

public class PositionTracker : IPositionTracker
{
    private readonly SkySettingsInstance _settings;
    private double _altitude;
    private double _azimuth;
    private double _rightAscension;
    private double _declination;
    
    public PositionTracker(SkySettingsInstance settings)
    {
        _settings = settings;
    }
    
    public double Altitude => _altitude;
    public double Azimuth => _azimuth;
    
    public async Task UpdateFromStepsAsync(double[] steps)
    {
        // Convert steps to degrees
        var degrees = ConvertStepsToDegrees(steps);
        
        // Calculate coordinates
        var (alt, az) = CalculateAltAz(degrees);
        var (ra, dec) = CalculateRaDec(degrees);
        
        // Update state
        _altitude = alt;
        _azimuth = az;
        _rightAscension = ra;
        _declination = dec;
        
        // Raise event
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(steps, alt, az, ra, dec));
    }
    
    public double[] ConvertStepsToDegrees(double[] steps)
    {
        // Implementation from static SkyServer
        return new[] 
        { 
            steps[0] * _settings.FactorStep[0], 
            steps[1] * _settings.FactorStep[1] 
        };
    }
    
    // ... other methods
}
```

##### **2. IMountController**

```csharp
/// <summary>
/// Manages mount connection and lifecycle
/// Responsibility: Start/stop mount, manage queues and timers
/// </summary>
public interface IMountController
{
    bool IsRunning { get; }
    string MountName { get; }
    string MountVersion { get; }
    
    Task StartAsync();
    Task StopAsync();
    void Reset();
    
    event EventHandler<MountStateChangedEventArgs> StateChanged;
}

public class MountController : IMountController
{
    private readonly SkySettingsInstance _settings;
    private readonly IPositionTracker _positionTracker;
    private bool _isRunning;
    private MediaTimer? _updateTimer;
    
    public MountController(
        SkySettingsInstance settings,
        IPositionTracker positionTracker)
    {
        _settings = settings;
        _positionTracker = positionTracker;
    }
    
    public bool IsRunning => _isRunning;
    
    public async Task StartAsync()
    {
        if (_isRunning) return;
        
        // Start queues based on mount type
        switch (_settings.Mount)
        {
            case MountType.Simulator:
                MountQueue.Start();
                break;
            case MountType.SkyWatcher:
                SkyQueue.Start(SkySystem.Serial, ...);
                break;
        }
        
        // Start update timer
        _updateTimer = new MediaTimer 
        { 
            Period = _settings.DisplayInterval, 
            Resolution = 5 
        };
        _updateTimer.Tick += async (s, e) => await UpdatePositionAsync();
        _updateTimer.Start();
        
        _isRunning = true;
        StateChanged?.Invoke(this, new MountStateChangedEventArgs(true));
    }
    
    public async Task StopAsync()
    {
        if (!_isRunning) return;
        
        // Stop timer
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _updateTimer = null;
        
        // Stop queues
        switch (_settings.Mount)
        {
            case MountType.Simulator:
                MountQueue.Stop();
                break;
            case MountType.SkyWatcher:
                SkyQueue.Stop();
                break;
        }
        
        _isRunning = false;
        StateChanged?.Invoke(this, new MountStateChangedEventArgs(false));
    }
    
    private async Task UpdatePositionAsync()
    {
        var steps = await GetStepsFromMountAsync();
        await _positionTracker.UpdateFromStepsAsync(steps);
    }
    
    // ... other methods
}
```

##### **3. ISlewController**

```csharp
/// <summary>
/// Manages slewing operations
/// Responsibility: GoTo, precision slew, abort slew
/// </summary>
public interface ISlewController
{
    bool IsSlewing { get; }
    SlewType SlewState { get; }
    
    Task SlewToCoordinatesAsync(double ra, double dec, bool tracking = false);
    Task SlewToAltAzAsync(double alt, double az);
    Task AbortSlewAsync();
    
    event EventHandler<SlewStateChangedEventArgs> SlewStateChanged;
}

public class SlewController : ISlewController
{
    private readonly SkySettingsInstance _settings;
    private readonly IPositionTracker _positionTracker;
    private bool _isSlewing;
    private SlewType _slewState;
    private CancellationTokenSource? _ctsSlew;
    
    public SlewController(
        SkySettingsInstance settings,
        IPositionTracker positionTracker)
    {
        _settings = settings;
        _positionTracker = positionTracker;
    }
    
    public bool IsSlewing => _isSlewing;
    public SlewType SlewState => _slewState;
    
    public async Task SlewToCoordinatesAsync(double ra, double dec, bool tracking = false)
    {
        _ctsSlew = new CancellationTokenSource();
        _isSlewing = true;
        _slewState = SlewType.SlewRaDec;
        
        SlewStateChanged?.Invoke(this, new SlewStateChangedEventArgs(true, SlewType.SlewRaDec));
        
        try
        {
            // Convert to target axes
            var target = ConvertRaDecToAxes(ra, dec);
            
            // Perform slew (implementation from static SkyServer)
            await PerformSlewAsync(target, _ctsSlew.Token);
            
            // Precision slew if needed
            await PrecisionSlewAsync(target, _ctsSlew.Token);
        }
        finally
        {
            _isSlewing = false;
            _slewState = SlewType.SlewNone;
            SlewStateChanged?.Invoke(this, new SlewStateChangedEventArgs(false, SlewType.SlewNone));
        }
    }
    
    public async Task AbortSlewAsync()
    {
        _ctsSlew?.Cancel();
        _isSlewing = false;
        _slewState = SlewType.SlewNone;
    }
    
    // ... other methods
}
```

##### **4. IPecManager**

```csharp
/// <summary>
/// Manages Periodic Error Correction
/// Responsibility: Load PEC files, apply corrections
/// </summary>
public interface IPecManager
{
    bool PecOn { get; set; }
    bool PecTraining { get; set; }
    Tuple<int, double, int>? CurrentBin { get; }
    
    void LoadPecFile(string fileName);
    Task ApplyCorrectionAsync(double[] steps);
    
    event EventHandler<PecStateChangedEventArgs> PecStateChanged;
}

public class PecManager : IPecManager
{
    private readonly SkySettingsInstance _settings;
    private bool _pecOn;
    private SortedList<int, Tuple<double, int>>? _pecWormMaster;
    private Tuple<int, double, int>? _currentBin;
    
    public PecManager(SkySettingsInstance settings)
    {
        _settings = settings;
    }
    
    public bool PecOn
    {
        get => _pecOn;
        set
        {
            if (_pecOn == value) return;
            _pecOn = value;
            PecStateChanged?.Invoke(this, new PecStateChangedEventArgs(_pecOn));
        }
    }
    
    public void LoadPecFile(string fileName)
    {
        // Implementation from static SkyServer.LoadPecFile
        var bins = ParsePecFile(fileName);
        _pecWormMaster = CreateWormMaster(bins);
    }
    
    public async Task ApplyCorrectionAsync(double[] steps)
    {
        if (!_pecOn || _pecWormMaster == null) return;
        
        // Calculate current bin
        var binNo = CalculateBinNumber(steps[0]);
        
        // Get correction factor
        if (_pecWormMaster.TryGetValue(binNo, out var bin))
        {
            _currentBin = new Tuple<int, double, int>(binNo, bin.Item1, bin.Item2);
            // Apply correction (implementation from static SkyServer)
        }
    }
    
    // ... other methods
}
```

##### **5. ITrackingEngine**

```csharp
/// <summary>
/// Manages tracking operations
/// Responsibility: Set tracking rates, handle tracking modes
/// </summary>
public interface ITrackingEngine
{
    bool Tracking { get; set; }
    TrackingMode TrackingMode { get; set; }
    double TrackingRate { get; }
    
    Task SetTrackingAsync(bool enabled);
    void SetTrackingRate(double rate);
    
    event EventHandler<TrackingStateChangedEventArgs> TrackingStateChanged;
}

public class TrackingEngine : ITrackingEngine
{
    private readonly SkySettingsInstance _settings;
    private readonly IMountController _mountController;
    private bool _tracking;
    private TrackingMode _trackingMode;
    
    public TrackingEngine(
        SkySettingsInstance settings,
        IMountController mountController)
    {
        _settings = settings;
        _mountController = mountController;
    }
    
    public bool Tracking
    {
        get => _tracking;
        set => _ = SetTrackingAsync(value);
    }
    
    public async Task SetTrackingAsync(bool enabled)
    {
        if (_tracking == enabled) return;
        
        _tracking = enabled;
        
        if (enabled)
        {
            // Start tracking (implementation from static SkyServer)
            await StartTrackingAsync();
        }
        else
        {
            // Stop tracking
            await StopTrackingAsync();
        }
        
        TrackingStateChanged?.Invoke(this, new TrackingStateChangedEventArgs(_tracking));
    }
    
    // ... other methods
}
```

##### **6. ILimitChecker**

```csharp
/// <summary>
/// Manages axis limits and safety checks
/// Responsibility: Check limits, prevent unsafe movements
/// </summary>
public interface ILimitChecker
{
    bool AtLimit { get; }
    LimitStatusType LimitStatus { get; }
    
    bool CheckLimits(double[] position);
    bool IsPositionSafe(double[] targetPosition);
    
    event EventHandler<LimitStatusChangedEventArgs> LimitStatusChanged;
}

public class LimitChecker : ILimitChecker
{
    private readonly SkySettingsInstance _settings;
    private LimitStatusType _limitStatus;
    
    public LimitChecker(SkySettingsInstance settings)
    {
        _settings = settings;
    }
    
    public bool AtLimit => _limitStatus.AtLimit;
    public LimitStatusType LimitStatus => _limitStatus;
    
    public bool CheckLimits(double[] position)
    {
        // Implementation from static SkyServer.CheckAxisLimits
        var newStatus = new LimitStatusType
        {
            AtLowerLimitAxisX = position[0] <= -_settings.AxisLimitX,
            AtUpperLimitAxisX = position[0] >= _settings.AxisLimitX,
            AtLowerLimitAxisY = position[1] <= _settings.AxisLowerLimitY,
            AtUpperLimitAxisY = position[1] >= _settings.AxisUpperLimitY
        };
        
        if (!_limitStatus.Equals(newStatus))
        {
            _limitStatus = newStatus;
            LimitStatusChanged?.Invoke(this, new LimitStatusChangedEventArgs(_limitStatus));
        }
        
        return !_limitStatus.AtLimit;
    }
    
    public bool IsPositionSafe(double[] targetPosition)
    {
        return CheckLimits(targetPosition);
    }
}
```

#### **Coordinator: SkyServerInstance**

```csharp
/// <summary>
/// Coordinates capability modules - lightweight facade
/// </summary>
public class SkyServerInstance : IDisposable
{
    private readonly SkySettingsInstance _settings;
    private readonly string _instanceId;
    
    // Injected capabilities
    private readonly IPositionTracker _positionTracker;
    private readonly IMountController _mountController;
    private readonly ISlewController _slewController;
    private readonly IPecManager _pecManager;
    private readonly ITrackingEngine _trackingEngine;
    private readonly ILimitChecker _limitChecker;
    
    public SkyServerInstance(
        SkySettingsInstance settings,
        IPositionTracker positionTracker,
        IMountController mountController,
        ISlewController slewController,
        IPecManager pecManager,
        ITrackingEngine trackingEngine,
        ILimitChecker limitChecker,
        string instanceId = "default")
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _mountController = mountController ?? throw new ArgumentNullException(nameof(mountController));
        _slewController = slewController ?? throw new ArgumentNullException(nameof(slewController));
        _pecManager = pecManager ?? throw new ArgumentNullException(nameof(pecManager));
        _trackingEngine = trackingEngine ?? throw new ArgumentNullException(nameof(trackingEngine));
        _limitChecker = limitChecker ?? throw new ArgumentNullException(nameof(limitChecker));
        _instanceId = instanceId;
    }
    
    // Facade properties delegate to modules
    public string InstanceId => _instanceId;
    public SkySettingsInstance Settings => _settings;
    
    // Position
    public double Altitude => _positionTracker.Altitude;
    public double Azimuth => _positionTracker.Azimuth;
    public double RightAscension => _positionTracker.RightAscension;
    public double Declination => _positionTracker.Declination;
    
    // Mount control
    public bool IsMountRunning
    {
        get => _mountController.IsRunning;
        set => _ = value ? _mountController.StartAsync() : _mountController.StopAsync();
    }
    
    // Slewing
    public bool IsSlewing => _slewController.IsSlewing;
    public Task SlewToCoordinatesAsync(double ra, double dec, bool tracking = false)
        => _slewController.SlewToCoordinatesAsync(ra, dec, tracking);
    
    // Tracking
    public bool Tracking
    {
        get => _trackingEngine.Tracking;
        set => _trackingEngine.Tracking = value;
    }
    
    // PEC
    public bool PecOn
    {
        get => _pecManager.PecOn;
        set => _pecManager.PecOn = value;
    }
    
    // Limits
    public bool AtLimit => _limitChecker.AtLimit;
    
    public void Dispose()
    {
        _mountController.StopAsync().Wait();
        
        if (_positionTracker is IDisposable disposablePos)
            disposablePos.Dispose();
        if (_mountController is IDisposable disposableMount)
            disposableMount.Dispose();
        if (_slewController is IDisposable disposableSlew)
            disposableSlew.Dispose();
    }
}
```

#### **Dependency Injection Configuration**

```csharp
// Program.cs
public static void ConfigureSkyServerServices(IServiceCollection services)
{
    // Register capability modules as scoped (one per mount instance)
    services.AddScoped<IPositionTracker, PositionTracker>();
    services.AddScoped<IMountController, MountController>();
    services.AddScoped<ISlewController, SlewController>();
    services.AddScoped<IPecManager, PecManager>();
    services.AddScoped<ITrackingEngine, TrackingEngine>();
    services.AddScoped<ILimitChecker, LimitChecker>();
    
    // Register SkyServerInstance factory
    services.AddScoped<SkyServerInstance>((sp) =>
    {
        var settings = sp.GetRequiredService<SkySettingsInstance>();
        var positionTracker = sp.GetRequiredService<IPositionTracker>();
        var mountController = sp.GetRequiredService<IMountController>();
        var slewController = sp.GetRequiredService<ISlewController>();
        var pecManager = sp.GetRequiredService<IPecManager>();
        var trackingEngine = sp.GetRequiredService<ITrackingEngine>();
        var limitChecker = sp.GetRequiredService<ILimitChecker>();
        
        return new SkyServerInstance(
            settings,
            positionTracker,
            mountController,
            slewController,
            pecManager,
            trackingEngine,
            limitChecker,
            $"mount_{Guid.NewGuid()}"
        );
    });
}
```

#### **Pros & Cons**

| Aspect | Assessment |
|--------|------------|
| ? **Testability** | Each module can be tested in isolation with mocks |
| ? **Maintainability** | Changes to position tracking don't affect PEC |
| ? **Extensibility** | Easy to add new capabilities (e.g., IGuideController) |
| ? **SOLID Compliance** | Each module has single responsibility |
| ? **DI-Friendly** | Standard .NET dependency injection patterns |
| ?? **Coordination** | Modules may need to communicate (via events) |
| ?? **Shared State** | Need to define ownership of shared data |
| ?? **Learning Curve** | Team needs to understand module boundaries |

---

### **Option B: Pipeline Architecture**

#### **Concept**

Process mount operations as a **chain of independent steps** (pipeline). Each step performs a specific transformation or validation.

#### **Architecture Diagram**

```
???????????????????????????????????????????????????????????????
?                Position Update Pipeline                     ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  Input: Steps from Mount                                    ?
?     ?                                                        ?
?  ????????????????????                                       ?
?  ? ReadStepsStep    ? ? Get steps from SkyQueue/MountQueue ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? ConvertStepsStep ? ? Convert steps to degrees           ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? AlignmentStep    ? ? Apply alignment model              ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? CoordinatesStep  ? ? Calculate Alt/Az, Ra/Dec           ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? LimitCheckStep   ? ? Check axis limits                  ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? PecCorrectionStep? ? Apply PEC if enabled               ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  ????????????????????                                       ?
?  ? UpdateUIStep     ? ? Notify UI components               ?
?  ????????????????????                                       ?
?           ?                                                  ?
?  Output: Updated Position Data                              ?
???????????????????????????????????????????????????????????????
```

#### **Implementation**

```csharp
// Pipeline context - carries data between steps
public class MountContext
{
    public SkySettingsInstance Settings { get; }
    public Dictionary<string, object> Data { get; } = new();
    public CancellationToken CancellationToken { get; }
    public bool ShouldTerminate { get; set; }
    
    public MountContext(SkySettingsInstance settings, CancellationToken ct)
    {
        Settings = settings;
        CancellationToken = ct;
    }
    
    // Helper methods
    public T GetData<T>(string key) => (T)Data[key];
    public void SetData(string key, object value) => Data[key] = value;
}

// Pipeline step interface
public interface IPipelineStep
{
    string Name { get; }
    Task<StepResult> ExecuteAsync(MountContext context);
}

public class StepResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public bool ShouldTerminate { get; }
    
    public static StepResult Ok() => new StepResult(true, null, false);
    public static StepResult Fail(string message) => new StepResult(false, message, true);
    public static StepResult Terminate() => new StepResult(true, null, true);
}

// Example pipeline step
public class ReadStepsStep : IPipelineStep
{
    public string Name => "ReadStepsFromMount";
    
    public async Task<StepResult> ExecuteAsync(MountContext context)
    {
        try
        {
            // Read from mount queue based on mount type
            double[] steps = context.Settings.Mount switch
            {
                MountType.Simulator => await ReadSimulatorStepsAsync(),
                MountType.SkyWatcher => await ReadSkyWatcherStepsAsync(),
                _ => throw new NotSupportedException()
            };
            
            context.SetData("RawSteps", steps);
            return StepResult.Ok();
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Failed to read steps: {ex.Message}");
        }
    }
    
    private async Task<double[]> ReadSimulatorStepsAsync()
    {
        var cmd = new CmdAxesDegrees(MountQueue.NewId);
        return (double[])await MountQueue.GetCommandResult(cmd).Result;
    }
    
    private async Task<double[]> ReadSkyWatcherStepsAsync()
    {
        var cmd = new SkyGetSteps(SkyQueue.NewId);
        return (double[])await SkyQueue.GetCommandResult(cmd).Result;
    }
}

// Pipeline executor
public class PipelineExecutor
{
    private readonly List<IPipelineStep> _steps;
    
    public PipelineExecutor(IEnumerable<IPipelineStep> steps)
    {
        _steps = steps.ToList();
    }
    
    public async Task<PipelineResult> ExecuteAsync(MountContext context)
    {
        foreach (var step in _steps)
        {
            var result = await step.ExecuteAsync(context);
            
            if (!result.Success)
            {
                return PipelineResult.Failed(step.Name, result.ErrorMessage);
            }
            
            if (result.ShouldTerminate || context.ShouldTerminate)
            {
                return PipelineResult.Terminated(step.Name);
            }
        }
        
        return PipelineResult.Success();
    }
}

// Usage: Position update pipeline
public class PositionUpdatePipeline
{
    private readonly PipelineExecutor _executor;
    
    public PositionUpdatePipeline()
    {
        _executor = new PipelineExecutor(new IPipelineStep[]
        {
            new ReadStepsStep(),
            new ConvertStepsToDegrees Step(),
            new ApplyAlignmentModelStep(),
            new CalculateCoordinatesStep(),
            new CheckLimitsStep(),
            new ApplyPecCorrectionStep(),
            new UpdateUIStep()
        });
    }
    
    public async Task ExecuteAsync(MountContext context)
    {
        await _executor.ExecuteAsync(context);
    }
}
```

#### **Pros & Cons**

| Aspect | Assessment |
|--------|------------|
| ? **Flexible** | Easy to reorder or skip steps |
| ? **Extensible** | Add new steps without modifying existing code |
| ? **Testable** | Test individual steps in isolation |
| ? **Auditable** | Log each step's execution |
| ?? **Context Passing** | All data travels through context dictionary |
| ?? **Debugging** | Harder to trace execution flow |
| ?? **Performance** | Async overhead for each step |

---

### **Option C: Event-Driven Architecture**

#### **Concept**

Use **MediatR** (or similar event bus) for **loose coupling** between components. Publish events, handle them independently.

#### **Architecture Diagram**

```
???????????????????????????????????????????????????????????????
?                     Event Bus (MediatR)                     ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  Publishers:                   Handlers:                    ?
?                                                             ?
?  ????????????????             ????????????????            ?
?  ? MountQueue   ???StepsRead?>? Position     ?            ?
?  ?              ?             ? Tracker      ?            ?
?  ????????????????             ????????????????            ?
?                                      ?                      ?
?                               PositionUpdated               ?
?                                      ?                      ?
?  ????????????????             ????????????????            ?
?  ? SlewController??SlewStarted>? UI Updater   ?            ?
?  ?              ?             ?              ?            ?
?  ????????????????             ????????????????            ?
?                                      ?                      ?
?                               PositionUpdated               ?
?                                      ?                      ?
?  ????????????????             ????????????????            ?
?  ? TrackingEngine??TrackingOn?>? PEC Manager  ?            ?
?  ?              ?             ?              ?            ?
?  ????????????????             ????????????????            ?
???????????????????????????????????????????????????????????????
```

#### **Implementation**

```csharp
// Events (MediatR notifications)
public record StepsReadEvent(double[] Steps) : INotification;
public record PositionUpdatedEvent(double Alt, double Az, double Ra, double Dec) : INotification;
public record SlewStartedEvent(double TargetRa, double TargetDec) : INotification;
public record SlewCompletedEvent() : INotification;
public record TrackingStateChangedEvent(bool IsTracking) : INotification;

// Event handlers
public class PositionTrackerHandler : INotificationHandler<StepsReadEvent>
{
    private readonly IPositionCalculator _calculator;
    private readonly IMediator _mediator;
    
    public async Task Handle(StepsReadEvent evt, CancellationToken ct)
    {
        // Calculate position
        var (alt, az, ra, dec) = _calculator.CalculatePosition(evt.Steps);
        
        // Publish updated position
        await _mediator.Publish(new PositionUpdatedEvent(alt, az, ra, dec), ct);
    }
}

public class UIUpdaterHandler : INotificationHandler<PositionUpdatedEvent>
{
    public async Task Handle(PositionUpdatedEvent evt, CancellationToken ct)
    {
        // Update UI components
        await UpdateUIAsync(evt);
    }
}

public class PecManagerHandler : INotificationHandler<PositionUpdatedEvent>
{
    private readonly IPecManager _pecManager;
    
    public async Task Handle(PositionUpdatedEvent evt, CancellationToken ct)
    {
        if (_pecManager.PecOn)
        {
            await _pecManager.ApplyCorrectionAsync();
        }
    }
}

// Usage
public class SkyServerInstance
{
    private readonly IMediator _mediator;
    
    public async Task UpdateStepsAsync(double[] steps)
    {
        // Publish event - all handlers will execute
        await _mediator.Publish(new StepsReadEvent(steps));
    }
}
```

#### **Pros & Cons**

| Aspect | Assessment |
|--------|------------|
| ? **Loose Coupling** | Publishers don't know about handlers |
| ? **Extensible** | Add handlers without modifying publishers |
| ? **Async** | Built-in async/await support |
| ? **Cross-Cutting** | Easy to add logging, validation behaviors |
| ?? **Hidden Dependencies** | Hard to see what happens when event fires |
| ?? **Ordering** | Handler execution order not guaranteed |
| ?? **Performance** | Reflection overhead |

---

### **Option D: Actor Model**

#### **Concept**

Use **Orleans** or **Akka.NET** for **actor-based concurrency**. Each actor encapsulates state and processes messages.

**Pros**:
- ? True concurrency
- ? Location transparent
- ? Fault tolerance

**Cons**:
- ?? High complexity
- ?? Overkill for single-machine
- ?? Steep learning curve

**Recommendation**: **Not suitable** for this project (too complex for desktop astronomy app).

---

### **Option E: Domain-Driven Design**

#### **Concept**

Model **business domain** with **aggregates**, **entities**, and **value objects**. Encapsulate business rules.

```csharp
// Aggregate root
public class MountAggregate
{
    public MountId Id { get; }
    public Position CurrentPosition { get; private set; }
    public SlewState SlewState { get; private set; }
    
    // Business logic methods (not just getters/setters)
    public Result SlewTo(Coordinates target)
    {
        // Validate invariants
        if (!CanSlew())
            return Result.Failure("Cannot slew in current state");
        
        // Apply business logic
        SlewState = SlewState.StartSlew(target);
        
        // Raise domain event
        RaiseDomainEvent(new SlewStartedEvent(Id, target));
        
        return Result.Success();
    }
}
```

**Pros**:
- ? Rich domain model
- ? Business rules encapsulated
- ? Event sourcing ready

**Cons**:
- ?? Learning curve
- ?? May be overkill
- ?? Extra layers

---

### **RECOMMENDED: Hybrid Approach**

#### **Best of Multiple Worlds**

Combine **Option A (DI Modules)** + **Option C (Events)** + Light **Option E (DDD)**

#### **Architecture**

```
???????????????????????????????????????????????????????????????
?                  SkyServerInstance                          ?
?              (Coordinator + DI + Events)                    ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  Core Modules (DI):                                         ?
?  ????????????????????  ????????????????????               ?
?  ? PositionModule   ?  ? SlewModule       ?               ?
?  ? - IPositionTracker?  ? - ISlewController?               ?
?  ????????????????????  ????????????????????               ?
?           ?                      ?                          ?
?           ?  Publishes Events    ?                          ?
?           ????????????????????????                          ?
?                   ?                                          ?
?           ????????????????????                              ?
?           ?  Event Bus       ?                              ?
?           ?  (MediatR)       ?                              ?
?           ????????????????????                              ?
?                    ?                                         ?
?      ?????????????????????????????                          ?
?      ?             ?             ?                          ?
?  ??????????  ??????????  ??????????                        ?
?  ?PEC     ?  ?UI      ?  ?Logging ?                        ?
?  ?Handler ?  ?Updater ?  ?Handler ?                        ?
?  ??????????  ??????????  ??????????                        ?
?                                                             ?
?  Business Rules: Light DDD (aggregate methods)              ?
?  - SlewTo() validates can slew before executing            ?
?  - UpdatePosition() checks limits before updating           ?
???????????????????????????????????????????????????????????????
```

#### **Why This Works**

1. **DI Modules** = Clear separation of concerns, testable
2. **Events** = Loose coupling for cross-cutting concerns (PEC, UI, logging)
3. **Light DDD** = Business rules in methods, not scattered
4. **Backward Compatible** = Can keep static facade during migration

---

## Architecture Comparison

| Criteria | Monolithic | Option A (DI) | Option B (Pipeline) | Option C (Events) | **Hybrid** |
|----------|-----------|---------------|---------------------|-------------------|------------|
| **Maintainability** | ? Poor | ? Good | ? Good | ? Good | ? **Excellent** |
| **Testability** | ? Hard | ? Easy | ? Easy | ? Easy | ? **Easy** |
| **Performance** | ? Fast | ? Fast | ?? Medium | ?? Medium | ? **Fast** |
| **Extensibility** | ? Hard | ? Good | ? Excellent | ? Excellent | ? **Excellent** |
| **Learning Curve** | ? Low | ? Low | ?? Medium | ?? Medium | ?? **Medium** |
| **SOLID Compliance** | ? Poor | ? Good | ? Good | ? Good | ? **Excellent** |
| **Migration Effort** | N/A | ?? Medium | ?? High | ?? Medium | ?? **Medium** |
| **Multi-Telescope** | ? No | ? Yes | ? Yes | ? Yes | ? **Yes** |

---

## Implementation Plan

### **Phase 1: Extract First Module** (Week 1)

**Goal**: Prove the concept with one module

1. Create `IPositionTracker` interface
2. Implement `PositionTracker` class
3. Move position calculation logic
4. Update `SkyServerInstance` to use module
5. Test with single telescope

**Estimated Effort**: 16 hours

---

### **Phase 2: Extract Core Modules** (Weeks 2-3)

**Goal**: Extract remaining core modules

1. Extract `IMountController` + implementation (8 hours)
2. Extract `ISlewController` + implementation (12 hours)
3. Extract `ITrackingEngine` + implementation (8 hours)
4. Wire with DI in `Program.cs` (4 hours)
5. Test multi-telescope (8 hours)

**Estimated Effort**: 40 hours

---

### **Phase 3: Extract Specialized Modules** (Week 4)

**Goal**: Extract PEC, limits, etc.

1. Extract `IPecManager` + implementation (8 hours)
2. Extract `ILimitChecker` + implementation (4 hours)
3. Add event bus (MediatR) (4 hours)
4. Wire cross-cutting handlers (4 hours)

**Estimated Effort**: 20 hours

---

### **Phase 4: Cleanup & Documentation** (Week 5)

**Goal**: Remove obsolete code, document

1. Remove static `SkyServer` methods (8 hours)
2. Update documentation (4 hours)
3. Integration testing (8 hours)

**Estimated Effort**: 20 hours

---

### **Total Estimated Effort: 96 hours (~12 working days)**

---

## Migration Strategy

### **Approach: Incremental Extraction**

#### **Step 1: Create Module Interfaces** (Days 1-2)
```csharp
// Define contracts first
IPositionTracker
ISlewController
ITrackingEngine
IPecManager
ILimitChecker
IMountController
```

#### **Step 2: Implement First Module** (Days 3-4)
```csharp
// Start with simplest: PositionTracker
public class PositionTracker : IPositionTracker
{
    // Move position calculation logic from static SkyServer
}
```

#### **Step 3: Wire with DI** (Day 5)
```csharp
// Program.cs
services.AddScoped<IPositionTracker, PositionTracker>();
```

#### **Step 4: Update SkyServerInstance** (Day 5)
```csharp
public class SkyServerInstance
{
    private readonly IPositionTracker _positionTracker;
    
    public double Altitude => _positionTracker.Altitude;
}
```

#### **Step 5: Test** (Day 6)
- Unit test PositionTracker in isolation
- Integration test with SkyServerInstance
- Regression test with Telescope driver

#### **Step 6: Repeat for Other Modules** (Days 7-12)
- Extract one module at a time
- Test after each extraction
- Keep static facade working during migration

---

## Code Examples

### **Before (Monolithic)**

```csharp
// SkyServer.cs - Everything in one place
public static partial class SkyServer
{
    // State
    private static double _altitude;
    private static bool _tracking;
    private static bool _pecOn;
    
    // Position calculation
    public static void UpdatePosition(double[] steps)
    {
        var degrees = ConvertSteps(steps);
        _altitude = CalculateAltitude(degrees);
        CheckLimits(_altitude);
        if (_pecOn) ApplyPec();
        UpdateUI();
    }
    
    // Tracking
    public static void SetTracking(bool enabled)
    {
        _tracking = enabled;
        // ... 50 lines of tracking logic
    }
    
    // PEC
    public static void ApplyPec()
    {
        // ... 100 lines of PEC logic
    }
    
    // ... 80+ more methods
}
```

### **After (Modular)**

```csharp
// IPositionTracker.cs
public interface IPositionTracker
{
    double Altitude { get; }
    Task UpdateFromStepsAsync(double[] steps);
}

// PositionTracker.cs
public class PositionTracker : IPositionTracker
{
    private readonly IMediator _mediator;
    private double _altitude;
    
    public double Altitude => _altitude;
    
    public async Task UpdateFromStepsAsync(double[] steps)
    {
        var degrees = ConvertSteps(steps);
        _altitude = CalculateAltitude(degrees);
        
        // Publish event for other modules
        await _mediator.Publish(new PositionUpdatedEvent(_altitude));
    }
}

// PecManager.cs - Independent module
public class PecManager : INotificationHandler<PositionUpdatedEvent>
{
    public async Task Handle(PositionUpdatedEvent evt, CancellationToken ct)
    {
        if (PecOn)
            await ApplyPecCorrectionAsync(evt.Altitude);
    }
}

// SkyServerInstance.cs - Thin coordinator
public class SkyServerInstance
{
    private readonly IPositionTracker _positionTracker;
    
    public double Altitude => _positionTracker.Altitude;
    
    public async Task UpdateStepsAsync(double[] steps)
    {
        await _positionTracker.UpdateFromStepsAsync(steps);
    }
}
```

---

## Conclusion

### **Recommendation: Hybrid Approach**

**Rationale**:
1. ? **Clear module boundaries** (DI) - Easy to maintain and test
2. ? **Loose coupling** (Events) - Cross-cutting concerns isolated
3. ? **Business rules encapsulated** (Light DDD) - Domain logic in one place
4. ? **Incremental migration** - Extract one module at a time
5. ? **Multi-telescope ready** - Each instance has own modules

**Estimated Effort**: 96 hours (~12 working days)

**Risk Level**: Medium (mitigated by incremental approach)

**Reward**: 
- Clean architecture
- Easy testing
- Multi-telescope support
- Future extensibility

---

## Next Steps

### **Immediate Actions**

1. ? Review this analysis with team
2. ? Decide on architecture (recommend Hybrid)
3. ? Create `IPositionTracker` interface
4. ? Implement `PositionTracker` class
5. ? Proof-of-concept: Extract position module

### **Short Term**

6. ? Extract remaining core modules
7. ? Add MediatR for events
8. ? Wire with DI container
9. ? Test multi-telescope

### **Long Term**

10. ? Remove static `SkyServer`
11. ? Full integration testing
12. ? Production deployment

---

## References

- **Related Document**: [Multi-Telescope Architecture Analysis](Multi-Telescope-Architecture-Analysis.md)
- **Current Implementation**: `GreenSwamp.Alpaca.MountControl/SkyServer.cs`
- **ASCOM Driver**: `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`
- **Settings System**: `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-XX  
**Status**: Planning / Awaiting Approval  
**Author**: AI Architecture Assistant
