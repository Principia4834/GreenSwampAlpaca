# SkyServer Architecture Refactoring Plan

**Document Version**: 1.0  
**Last Updated**: 2025-01-03  
**Project**: GreenSwamp Alpaca Mount Control  
**Status**: Proposed

---

## Executive Summary

The current `SkyServer` static class architecture presents significant maintainability and scalability challenges. This document outlines a comprehensive plan to migrate to an instance-based architecture that supports multiple independent mount instances while maintaining backward compatibility during the transition.

### Key Goals
- Enable multiple mount instances to run simultaneously
- Improve testability through dependency injection
- Enhance maintainability by applying SOLID principles
- Maintain backward compatibility during migration
- Reduce technical debt

---

## Current Architecture Problems

### 1. Static State Issues

**Problem**: The entire `SkyServer` class is static with shared state across the application.

**Impact**:
- ? Only one mount can be controlled at a time
- ? Global mutable state creates hidden dependencies
- ? Thread safety risks with async operations
- ? Memory leaks from static references preventing garbage collection
- ? Difficult to isolate and test

**Example of Current Issue**:
```csharp
// Current static design - only ONE mount possible
SkyServer.SlewRaDec(12.5, 45.0, true);
SkyServer.Tracking = true;
// All state is global and shared
```

### 2. God Class Anti-Pattern

The `SkyServer` class violates the Single Responsibility Principle by handling:

- **Position Tracking**: RA/Dec, Alt/Az, coordinate transformations
- **Slew Operations**: RA/Dec slews, Alt/Az slews, parking, homing
- **Tracking Control**: Tracking modes, rates, guide rates
- **Pulse Guiding**: Guide pulses, pulse rates
- **Mount Communication**: Direct hardware I/O
- **Settings Management**: Configuration and state
- **Limit Checking**: Hour angle, altitude, axis limits
- **Auto-Home**: Sensor detection and positioning

**Impact**:
- ?? **File Size**: Over 5000 lines of code
- ?? **Maintenance**: Difficult to modify without breaking changes
- ?? **Testing**: Nearly impossible to unit test
- ?? **Understanding**: High cognitive load for developers

### 3. Tight Coupling

**Current Dependencies**:
```csharp
// Direct static dependencies throughout the code
SkySettings.TrackingRate = DriveRate.Sidereal;
SkyServer.SkyTasks(MountTaskName.AlternatingPpec);
Axes.AxisPosition(Axis.Axis1);
```

**Problems**:
- Cannot mock dependencies for testing
- Changes ripple across the codebase
- Circular dependencies between static classes
- Hard to understand data flow

### 4. Lack of Separation of Concerns

**Mixed Responsibilities**:
- Business logic mixed with hardware communication
- UI concerns (property changed events) mixed with domain logic
- No clear boundaries between components

---

## Proposed Architecture

### Phase 1: Extract Interfaces (Weeks 1-2)

Create clear contracts without changing existing implementation.

#### Core Interfaces

```csharp
// IMountController.cs
public interface IMountController
{
    string InstanceId { get; }
    bool IsMountRunning { get; set; }
    bool IsSlewing { get; }
    bool Tracking { get; set; }
    bool AtPark { get; }
    bool AtHome { get; }
    
    void SlewRaDec(double ra, double dec, bool tracking = false);
    void SlewAltAz(double altitude, double azimuth);
    void AbortSlew(bool speak, EventWaitHandle abortSlewStarted = null);
    void StopAxes();
    
    event PropertyChangedEventHandler PropertyChanged;
}

// IPositionProvider.cs
public interface IPositionProvider
{
    double RightAscension { get; }
    double Declination { get; }
    double Altitude { get; }
    double Azimuth { get; }
    double SiderealTime { get; }
    PointingState SideOfPier { get; }
    
    void UpdatePosition(double[] mountAxisPositions);
}

// ITrackingController.cs
public interface ITrackingController
{
    bool Tracking { get; set; }
    DriveRate TrackingRate { get; set; }
    double RateRa { get; set; }
    double RateDec { get; set; }
    
    void SetGuideRates();
    void SetTrackingMode();
}

// IParkingController.cs
public interface IParkingController
{
    bool AtPark { get; }
    ParkPosition ParkSelected { get; set; }
    
    void GoToPark();
    void GoToHome();
    void UnPark();
}
```

### Phase 2: Create Instance-Based Classes (Weeks 3-4)

#### Core Mount Instance

```csharp
// MountInstance.cs
public class MountInstance : IMountController, IDisposable
{
    private readonly string _instanceId;
    private readonly IMountSettings _settings;
    private readonly IMonitorLog _logger;
    private readonly IMountHardware _hardware;
    
    // Specialized controllers (composition over inheritance)
    private readonly IPositionProvider _positionTracker;
    private readonly ISlewController _slewController;
    private readonly IParkingController _parkingController;
    private readonly ITrackingController _trackingController;
    
    public string InstanceId => _instanceId;
    
    public MountInstance(
        string instanceId,
        IMountSettings settings,
        IMonitorLog logger,
        IMountHardwareFactory hardwareFactory)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Create hardware based on settings
        _hardware = hardwareFactory.Create(settings.Mount, settings.Port, settings.BaudRate);
        
        // Initialize specialized controllers
        _positionTracker = new PositionTracker(this, settings, logger);
        _slewController = new SlewController(this, _hardware, _positionTracker, settings, logger);
        _parkingController = new ParkingController(this, _hardware, _positionTracker, settings, logger);
        _trackingController = new TrackingController(this, _hardware, _positionTracker, settings, logger);
        
        _logger.LogInformation($"Mount instance '{_instanceId}' created");
    }
    
    public bool Tracking 
    { 
        get => _trackingController.Tracking;
        set => _trackingController.Tracking = value;
    }
    
    public void SlewRaDec(double ra, double dec, bool tracking = false)
        => _slewController.SlewRaDec(ra, dec, tracking);
    
    public event PropertyChangedEventHandler PropertyChanged;
    
    public void Dispose()
    {
        _hardware?.Dispose();
        (_slewController as IDisposable)?.Dispose();
        (_trackingController as IDisposable)?.Dispose();
    }
}
```

### Phase 3: Dependency Injection & Instance Management (Weeks 5-6)

#### Mount Instance Manager

```csharp
// IMountInstanceManager.cs
public interface IMountInstanceManager
{
    MountInstance GetOrCreateInstance(string instanceId);
    MountInstance GetInstance(string instanceId);
    IEnumerable<MountInstance> GetAllInstances();
    bool RemoveInstance(string instanceId);
}

// MountInstanceManager.cs
public class MountInstanceManager : IMountInstanceManager, IDisposable
{
    private readonly ConcurrentDictionary<string, MountInstance> _instances = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MountInstanceManager> _logger;
    
    public MountInstance GetOrCreateInstance(string instanceId)
    {
        return _instances.GetOrAdd(instanceId, id => CreateInstance(id));
    }
    
    private MountInstance CreateInstance(string instanceId)
    {
        _logger.LogInformation($"Creating mount instance: {instanceId}");
        
        var settingsProvider = _serviceProvider.GetRequiredService<IMountSettingsProvider>();
        var settings = settingsProvider.GetSettings(instanceId);
        
        var logger = _serviceProvider.GetRequiredService<IMonitorLog>();
        var hardwareFactory = _serviceProvider.GetRequiredService<IMountHardwareFactory>();
        
        return new MountInstance(instanceId, settings, logger, hardwareFactory);
    }
}
```

#### Service Registration

```csharp
// MountServiceExtensions.cs
public static class MountServiceExtensions
{
    public static IServiceCollection AddMountServices(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IMountInstanceManager, MountInstanceManager>();
        services.AddSingleton<IMountSettingsProvider, MountSettingsProvider>();
        services.AddSingleton<IMountHardwareFactory, MountHardwareFactory>();
        
        // Keyed services for multiple instances (.NET 8 feature)
        services.AddKeyedScoped<IMountController>("primary", (sp, key) =>
        {
            var manager = sp.GetRequiredService<IMountInstanceManager>();
            return manager.GetOrCreateInstance("primary");
        });
        
        return services;
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMountServices();
```

### Phase 4: Backward Compatibility Facade (Weeks 7-8)

```csharp
// SkyServer.cs (modified to become a facade)
public static class SkyServer
{
    private static IMountInstanceManager _manager;
    private static readonly string DefaultInstanceId = "primary";
    
    public static void Initialize(IMountInstanceManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }
    
    private static MountInstance DefaultInstance => 
        _manager?.GetOrCreateInstance(DefaultInstanceId) 
        ?? throw new InvalidOperationException("SkyServer not initialized");
    
    // Delegate all properties to default instance
    public static bool Tracking
    {
        get => DefaultInstance.Tracking;
        set => DefaultInstance.Tracking = value;
    }
    
    public static double RightAscension => DefaultInstance.RightAscension;
    
    // Delegate all methods to default instance
    public static void SlewRaDec(double ra, double dec, bool tracking = false)
        => DefaultInstance.SlewRaDec(ra, dec, tracking);
}
```

### Phase 5: Update Consumers (Weeks 9-10)

#### Before: Using Static SkyServer

```csharp
public class TelescopeController : ControllerBase
{
    [HttpGet("api/telescope/position")]
    public IActionResult GetPosition()
    {
        return Ok(new
        {
            RA = SkyServer.RightAscension,
            Dec = SkyServer.Declination
        });
    }
}
```

#### After: Using Dependency Injection

```csharp
public class TelescopeController : ControllerBase
{
    private readonly IMountController _mount;
    private readonly IPositionProvider _positionProvider;
    
    public TelescopeController(
        [FromKeyedServices("primary")] IMountController mount,
        [FromKeyedServices("primary")] IPositionProvider positionProvider)
    {
        _mount = mount;
        _positionProvider = positionProvider;
    }
    
    [HttpGet("api/telescope/position")]
    public IActionResult GetPosition()
    {
        return Ok(new
        {
            RA = _positionProvider.RightAscension,
            Dec = _positionProvider.Declination
        });
    }
}
```

#### Multiple Instances Example

```csharp
public class DualMountController : ControllerBase
{
    private readonly IMountController _primaryMount;
    private readonly IMountController _secondaryMount;
    
    public DualMountController(
        [FromKeyedServices("primary")] IMountController primaryMount,
        [FromKeyedServices("secondary")] IMountController secondaryMount)
    {
        _primaryMount = primaryMount;
        _secondaryMount = secondaryMount;
    }
    
    [HttpPost("api/mounts/slew-both")]
    public async Task<IActionResult> SlewBothMounts([FromBody] SlewRequest request)
    {
        var tasks = new[]
        {
            Task.Run(() => _primaryMount.SlewRaDec(request.RA, request.Dec, true)),
            Task.Run(() => _secondaryMount.SlewRaDec(request.RA, request.Dec, true))
        };
        
        await Task.WhenAll(tasks);
        return Ok();
    }
}
```

---

## Migration Timeline

### Overview

Total estimated time: **13+ weeks**

### Detailed Phase Breakdown

| Phase | Duration | Risk | Key Deliverables |
|-------|----------|------|------------------|
| 1. Interface Extraction | 2 weeks | ?? Low | 8 interface definitions, documentation |
| 2. Instance Classes | 2 weeks | ?? Medium | MountInstance + 8 controller classes |
| 3. DI Implementation | 2 weeks | ?? Medium | Instance manager, service registration |
| 4. Backward Compatibility | 2 weeks | ?? High | Static facade, regression tests |
| 5. Update Consumers | 2 weeks | ?? Medium | Updated controllers & components |
| 6. Specialized Controllers | 2 weeks | ?? Low | Utility classes, optimizations |
| 7. Cleanup | 1+ weeks | ?? Low | Documentation, remove legacy code |

### Phase 1: Interface Extraction (Weeks 1-2)

**Goals**: Define clear contracts without changing implementation

**Tasks**:
- [ ] Define `IMountController` interface
- [ ] Define `IPositionProvider` interface
- [ ] Define `IParkingController` interface
- [ ] Define `ITrackingController` interface
- [ ] Define `IPulseGuideController` interface
- [ ] Define `ISyncController` interface
- [ ] Define `IAutoHomeController` interface
- [ ] Create unit test skeletons
- [ ] Document interface contracts

### Phase 2: Instance Class Creation (Weeks 3-4)

**Goals**: Create instance-based implementations

**Tasks**:
- [ ] Create `MountInstance` class
- [ ] Create `SlewController` class
- [ ] Create `TrackingController` class
- [ ] Create `ParkingController` class
- [ ] Create `PositionTracker` class
- [ ] Add unit tests for each class
- [ ] Verify instance isolation

### Phase 3: DI Implementation (Weeks 5-6)

**Goals**: Set up dependency injection infrastructure

**Tasks**:
- [ ] Create `IMountInstanceManager` interface
- [ ] Implement `MountInstanceManager` class
- [ ] Create service registration extensions
- [ ] Update `Program.cs`
- [ ] Create hardware factory
- [ ] Add integration tests

### Phase 4: Backward Compatibility (Weeks 7-8)

**Goals**: Maintain existing functionality with static facade

**Tasks**:
- [ ] Modify static `SkyServer` to facade
- [ ] Implement `Initialize()` method
- [ ] Delegate all properties/methods
- [ ] Run full regression test suite
- [ ] Document migration path

### Phase 5: Update Consumers (Weeks 9-10)

**Goals**: Migrate controllers and components to use DI

**Tasks**:
- [ ] Update ASCOM API controllers
- [ ] Update Blazor components
- [ ] Add error handling
- [ ] Performance testing
- [ ] Update documentation

---

## Testing Strategy

### Unit Tests

```csharp
[TestClass]
public class MountInstanceTests
{
    private Mock<IMountSettings> _mockSettings;
    private Mock<IMountHardware> _mockHardware;
    private MountInstance _mount;
    
    [TestInitialize]
    public void Setup()
    {
        _mockSettings = new Mock<IMountSettings>();
        _mockHardware = new Mock<IMountHardware>();
        
        _mount = new MountInstance("test", _mockSettings.Object, 
            Mock.Of<IMonitorLog>(), Mock.Of<IMountHardwareFactory>());
    }
    
    [TestMethod]
    public void SlewRaDec_ValidCoordinates_StartsSlew()
    {
        // Arrange
        _mount.IsMountRunning = true;
        
        // Act
        _mount.SlewRaDec(12.5, 45.0, true);
        
        // Assert
        Assert.IsTrue(_mount.IsSlewing);
    }
}
```

### Integration Tests

```csharp
[TestClass]
public class MountInstanceIntegrationTests
{
    [TestMethod]
    public async Task MultipleInstances_CanOperateIndependently()
    {
        // Arrange
        var manager = new MountInstanceManager();
        var mount1 = manager.GetOrCreateInstance("mount1");
        var mount2 = manager.GetOrCreateInstance("mount2");
        
        // Act
        var task1 = Task.Run(() => mount1.SlewRaDec(10.0, 30.0, true));
        var task2 = Task.Run(() => mount2.SlewRaDec(15.0, 45.0, true));
        
        await Task.WhenAll(task1, task2);
        
        // Assert
        Assert.AreNotSame(mount1, mount2);
    }
}
```

---

## Benefits of New Architecture

### 1. Multiple Instance Support ?
- Run multiple mounts simultaneously
- Independent configuration per instance
- Isolated state prevents interference

### 2. Improved Testability ?
- Mock dependencies easily
- Test components in isolation
- Faster test execution

### 3. Better Maintainability ?
- Single Responsibility Principle
- Clear separation of concerns
- Smaller, focused classes

### 4. Enhanced Thread Safety ?
- Instance-level locking
- No shared static state
- Proper async/await patterns

### 5. Flexible Configuration ?
- Per-instance settings
- Runtime configuration changes
- Support for different mount types

### 6. Proper Lifecycle Management ?
- IDisposable implementation
- Proper resource cleanup
- Memory leak prevention

---

## Risks & Mitigation

### Risk 1: Breaking Changes
**Impact**: High  
**Probability**: Medium  
**Mitigation**: 
- Maintain backward compatibility facade
- Incremental migration
- Comprehensive regression testing

### Risk 2: Performance Regression
**Impact**: Medium  
**Probability**: Low  
**Mitigation**:
- Performance benchmarks at each phase
- Profile before and after
- Optimize hot paths

### Risk 3: State Management Issues
**Impact**: High  
**Probability**: Medium  
**Mitigation**:
- Careful state migration
- Extensive integration tests
- Parallel running during transition

### Risk 4: Learning Curve
**Impact**: Low  
**Probability**: High  
**Mitigation**:
- Comprehensive documentation
- Code examples
- Team training sessions

---

## Success Criteria

1. ? **Functional Parity**: All existing functionality works identically
2. ? **No Performance Regression**: Performance metrics match or exceed current
3. ? **All Tests Pass**: 100% test pass rate
4. ? **Multiple Instances Work**: Can run 2+ mounts simultaneously
5. ? **Code Coverage**: >80% unit test coverage on new code
6. ? **Documentation Complete**: All interfaces and classes documented

---

## Implementation Checklist

### Pre-Migration
- [ ] Review and approve refactoring plan
- [ ] Set up project tracking
- [ ] Create feature branch
- [ ] Baseline performance metrics

### During Migration
- [ ] Weekly progress reviews
- [ ] Continuous integration testing
- [ ] Document decisions and changes
- [ ] Update architecture diagrams

### Post-Migration
- [ ] Final regression testing
- [ ] Performance validation
- [ ] Documentation review
- [ ] Team retrospective
- [ ] Knowledge transfer

---

## References & Resources

### Design Patterns Used
- **Dependency Injection**: Loose coupling, testability
- **Facade Pattern**: Backward compatibility
- **Strategy Pattern**: Different mount implementations
- **Factory Pattern**: Hardware creation
- **Composite Pattern**: Controller composition

### SOLID Principles Applied
- **S**ingle Responsibility: Each class has one job
- **O**pen/Closed: Extensible without modification
- **L**iskov Substitution: Interfaces are substitutable
- **I**nterface Segregation: Small, focused interfaces
- **D**ependency Inversion: Depend on abstractions

### Related Documentation
- ASCOM Alpaca Specification
- .NET 8 Dependency Injection Guidelines
- Blazor Server Architecture Patterns

---

## Appendix A: Code Samples

### Example: Creating a Mount Instance

```csharp
// Using DI in a controller
public class MountController : ControllerBase
{
    private readonly IMountController _mount;
    
    public MountController([FromKeyedServices("primary")] IMountController mount)
    {
        _mount = mount;
    }
    
    public IActionResult Connect()
    {
        _mount.IsMountRunning = true;
        return Ok();
    }
}
```

### Example: Blazor Component

```razor
@inject IMountInstanceManager MountManager

<div class="mount-control">
    <button @onclick="ToggleTracking">
        @(_mount?.Tracking == true ? "Stop" : "Start") Tracking
    </button>
</div>

@code {
    [Parameter]
    public string InstanceId { get; set; } = "primary";
    
    private IMountController _mount;
    
    protected override void OnInitialized()
    {
        _mount = MountManager.GetOrCreateInstance(InstanceId);
        _mount.PropertyChanged += OnMountPropertyChanged;
    }
    
    private void ToggleTracking()
    {
        _mount.Tracking = !_mount.Tracking;
    }
}
```

---

## Appendix B: Migration Checklist by File

### Phase 1: New Files to Create
- [ ] `IMountController.cs`
- [ ] `IPositionProvider.cs`
- [ ] `ITrackingController.cs`
- [ ] `IParkingController.cs`
- [ ] `IMountInstanceManager.cs`

### Phase 2: Files to Modify
- [ ] `SkyServer.cs` ? Convert to facade
- [ ] `Program.cs` ? Add DI registration
- [ ] All controllers ? Add DI constructors
- [ ] All Blazor components ? Inject services

### Phase 3: Files to Eventually Remove
- [ ] Legacy static implementations (after migration complete)
- [ ] Temporary compatibility code

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-03 | Architecture Team | Initial draft |

**Approval Status**: Pending Review

**Next Review Date**: TBD

---

*This document is a living guide and will be updated as the refactoring progresses.*
