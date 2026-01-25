# Full Multi-Telescope Architecture Implementation Plan

## Executive Summary

**Goal**: Support multiple simultaneous telescopes with per-telescope settings and independent operation.

**Current State**: Phase 3.2 - Instance-based architecture with static dependencies  
**Target State**: Phase 4+ - Fully independent multi-telescope support  
**Estimated Effort**: 40-60 hours of development + testing

---

## Architecture Requirements

### 1. Per-Telescope Settings ? (Partially Done)

**Current**: `SkySettingsInstance` exists but reads from static `SkySettings`  
**Required**: Each telescope has completely independent settings

```csharp
// Current (Phase 3.2):
var settingsA = new SkySettingsInstance();  // Reads from static SkySettings
var settingsB = new SkySettingsInstance();  // SAME settings as A!

// Required (Phase 4):
var settingsA = new SkySettingsInstance("telescope-a.json");
var settingsB = new SkySettingsInstance("telescope-b.json");  // Different settings!
```

### 2. Per-Telescope Queues ?? (Not Started)

**Current**: Single static `MountQueue` and `SkyQueue`  
**Required**: Each telescope has its own command queue

```csharp
// Current problem:
instanceA.Connect();  // Uses static MountQueue
instanceB.Connect();  // Uses SAME MountQueue - commands get mixed!

// Required:
instanceA.Connect();  // Uses instanceA._mountQueue
instanceB.Connect();  // Uses instanceB._mountQueue - independent!
```

### 3. Per-Telescope State ?? (Partially Done)

**Current**: Some instance state, many static properties  
**Required**: All state is instance-based

**Currently Static (need to migrate)**:
- `SkyServer.FactorStep` ??
- `SkyServer.StepsPerRevolution` ??
- `SkyServer.StepsWormPerRevolution` ??
- `SkyServer.Tracking` ??
- `SkyServer.AtPark` ??
- `SkyServer.IsSlewing` ??
- `SkyServer.ActualAxisX/Y` ??
- Many more...

### 4. Per-Telescope Serial Port ? (Not Started)

**Current**: Single static `SkySystem.ConnectSerial`  
**Required**: Each telescope connects to different COM port

```csharp
// Required:
telescopeA ? COM3
telescopeB ? COM5
```

### 5. Per-Telescope UI Updates ? (Not Started)

**Current**: Single static UI update timer  
**Required**: Each telescope updates its own UI independently

---

## Implementation Phases

### **Phase 4.1: Instance-Owned State** (Week 1-2)
**Goal**: Remove all static property dependencies from instance

#### Tasks:

1. **Convert properties to fields in MountInstance** (4-6 hours)
```csharp
// In MountInstance.cs

// FROM (current):
private double[] _factorStep => SkyServer.FactorStep;
private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;
private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;

// TO (Phase 4.1):
private double[] _factorStep = new double[2];
private long[] _stepsPerRevolution = new long[2];
private double[] _stepsWormPerRevolution = new double[2];

// In MountConnect(), AFTER tasks:
Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
Array.Copy(SkyServer.FactorStep, _factorStep, 2);
```

2. **Migrate mount capabilities to instance** (3-4 hours)
```csharp
// Add to MountInstance:
private bool _canPPec;
private bool _canHomeSensor;
private bool _canPolarLed;
private bool _canAdvancedCmdSupport;
private string _mountName = string.Empty;
private string _mountVersion = string.Empty;
private string _capabilities = string.Empty;

// Populate in MountConnect() after tasks
```

3. **Migrate tracking state to instance** (2-3 hours)
```csharp
// Add to MountInstance:
private bool _tracking;
private bool _atPark;
private bool _isSlewing;
private SlewType _slewState = SlewType.SlewNone;
private bool _isPulseGuidingRa;
private bool _isPulseGuidingDec;
```

4. **Migrate position state to instance** (2-3 hours)
```csharp
// Add to MountInstance:
private double[] _steps = new double[2];
private double _actualAxisX;
private double _actualAxisY;
private double _rightAscensionXForm;
private double _declinationXForm;
```

**Total Phase 4.1**: 11-16 hours

---

### **Phase 4.2: Per-Instance Settings** (Week 2-3)
**Goal**: Each instance has completely independent settings

#### Tasks:

1. **Extend SkySettingsInstance to load from file** (4-6 hours)
```csharp
// In SkySettingsInstance.cs

public class SkySettingsInstance
{
    private readonly string _settingsFile;
    private readonly Dictionary<string, object> _settings = new();
    
    public SkySettingsInstance(string settingsFile)
    {
        _settingsFile = settingsFile;
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        // Load from _settingsFile instead of static bridge
        var json = File.ReadAllText(_settingsFile);
        _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
    }
    
    public void SaveSettings()
    {
        // Save to _settingsFile
        var json = JsonSerializer.Serialize(_settings);
        File.WriteAllText(_settingsFile, json);
    }
}
```

2. **Create per-telescope settings files** (2-3 hours)
```
%AppData%/GreenSwampAlpaca/{version}/
??? appsettings.user.json           # Default telescope
??? telescope-1.settings.json        # Telescope 1
??? telescope-2.settings.json        # Telescope 2
??? telescope-3.settings.json        # Telescope 3
```

3. **Update UI to support multiple settings files** (6-8 hours)
- Add telescope selector dropdown
- Load/save per-telescope settings
- Show multiple telescopes in UI simultaneously

**Total Phase 4.2**: 12-17 hours

---

### **Phase 4.3: Per-Instance Queues** (Week 3-4)
**Goal**: Each telescope has its own command queue

#### Tasks:

1. **Make MountQueue instance-based** (8-10 hours)
```csharp
// Current (static):
public static class MountQueue
{
    private static CommandQueue _queue;
    public static void Start() { ... }
}

// Required (instance):
public class MountQueueInstance
{
    private readonly CommandQueue _queue;
    private readonly string _id;
    
    public MountQueueInstance(string id)
    {
        _id = id;
        _queue = new CommandQueue();
    }
    
    public void Start() { ... }
    public long NewId() { ... }
    public Task<dynamic> GetCommandResult(ICommand cmd) { ... }
}
```

2. **Make SkyQueue instance-based** (8-10 hours)
```csharp
public class SkyQueueInstance
{
    private readonly CommandQueue _queue;
    private readonly string _id;
    private readonly SerialPort _serialPort;  // Per-instance!
    
    public SkyQueueInstance(string id, string comPort)
    {
        _id = id;
        _serialPort = new SerialPort(comPort);
        _queue = new CommandQueue();
    }
}
```

3. **Update MountInstance to use instance queues** (4-6 hours)
```csharp
public class MountInstance
{
    private MountQueueInstance? _mountQueue;
    private SkyQueueInstance? _skyQueue;
    
    internal void MountStart()
    {
        switch (_settings.Mount)
        {
            case MountType.Simulator:
                _mountQueue = new MountQueueInstance(_id);
                _mountQueue.Start();
                break;
                
            case MountType.SkyWatcher:
                _skyQueue = new SkyQueueInstance(_id, _settings.ComPort);
                _skyQueue.Start(_settings.CustomGearing, ...);
                break;
        }
    }
}
```

4. **Update all command calls** (10-12 hours)
```csharp
// FROM (current):
var cmd = new CmdAxisSteps(MountQueue.NewId);
var result = MountQueue.GetCommandResult(cmd).Result;

// TO (Phase 4.3):
var cmd = new CmdAxisSteps(_mountQueue.NewId());
var result = _mountQueue.GetCommandResult(cmd).Result;
```

**Total Phase 4.3**: 30-38 hours

---

### **Phase 4.4: Per-Instance Tasks** (Week 5)
**Goal**: SimTasks/SkyTasks work with instance queues

#### Tasks:

1. **Convert SimTasks to instance method** (4-6 hours)
```csharp
// In MountInstance.cs

private void SimTasks(MountTaskName taskName)
{
    if (!_isMountRunning) return;
    
    switch (taskName)
    {
        case MountTaskName.GetFactorStep:
            var factorStep = new CmdFactorSteps(_mountQueue.NewId());
            _factorStep[0] = (double)_mountQueue.GetCommandResult(factorStep).Result;
            _factorStep[1] = _factorStep[0];
            break;
            
        case MountTaskName.StepsPerRevolution:
            var spr = new CmdSpr(_mountQueue.NewId());
            var sprnum = (long)_mountQueue.GetCommandResult(spr).Result;
            _stepsPerRevolution = new[] { sprnum, sprnum };
            break;
        
        // ... all other tasks
    }
}
```

2. **Convert SkyTasks to instance method** (4-6 hours)
```csharp
private void SkyTasks(MountTaskName taskName)
{
    if (!_isMountRunning) return;
    
    switch (taskName)
    {
        case MountTaskName.GetFactorStep:
            var skyFactor = new SkyGetFactorStepToRad(_skyQueue.NewId());
            _factorStep = (double[])_skyQueue.GetCommandResult(skyFactor).Result;
            break;
            
        // ... all other tasks
    }
}
```

3. **Update MountConnect to use instance tasks** (2-3 hours)
```csharp
private bool MountConnect()
{
    switch (_settings.Mount)
    {
        case MountType.Simulator:
            SimTasks(MountTaskName.MountName);
            SimTasks(MountTaskName.GetFactorStep);
            // ... use instance method
            break;
    }
}
```

**Total Phase 4.4**: 10-15 hours

---

### **Phase 4.5: Multi-Instance Service** (Week 6)
**Goal**: Service manages multiple telescope instances

#### Tasks:

1. **Create MountControlService** (6-8 hours)
```csharp
public class MountControlService
{
    private readonly Dictionary<string, MountInstance> _instances = new();
    
    public MountInstance CreateInstance(string id, string settingsFile)
    {
        var settings = new SkySettingsInstance(settingsFile);
        var instance = new MountInstance(id, settings);
        _instances[id] = instance;
        return instance;
    }
    
    public MountInstance? GetInstance(string id)
    {
        return _instances.TryGetValue(id, out var instance) ? instance : null;
    }
    
    public IReadOnlyList<MountInstance> GetAllInstances()
    {
        return _instances.Values.ToList();
    }
    
    public void RemoveInstance(string id)
    {
        if (_instances.TryGetValue(id, out var instance))
        {
            instance.Stop();
            instance.Disconnect();
            _instances.Remove(id);
        }
    }
}
```

2. **Register service in DI** (1-2 hours)
```csharp
// In Program.cs
builder.Services.AddSingleton<MountControlService>();
```

3. **Update TelescopeDriver to use service** (4-6 hours)
```csharp
public class TelescopeDriver
{
    private readonly MountControlService _mountService;
    private readonly string _deviceId;
    
    public TelescopeDriver(MountControlService mountService, int deviceNumber)
    {
        _mountService = mountService;
        _deviceId = $"telescope-{deviceNumber}";
    }
    
    public void Connect()
    {
        var settingsFile = GetSettingsFile(_deviceId);
        var instance = _mountService.CreateInstance(_deviceId, settingsFile);
        instance.Connect();
    }
}
```

**Total Phase 4.5**: 11-16 hours

---

### **Phase 4.6: UI Updates** (Week 7)
**Goal**: UI supports multiple simultaneous telescopes

#### Tasks:

1. **Add telescope selector component** (4-6 hours)
```razor
@* TelescopeSelector.razor *@
<div class="telescope-selector">
    <select @bind="SelectedTelescopeId">
        @foreach (var telescope in Telescopes)
        {
            <option value="@telescope.Id">@telescope.Name</option>
        }
    </select>
    <button @onclick="AddTelescope">Add Telescope</button>
</div>
```

2. **Update mount control page** (6-8 hours)
```razor
@* MountControl.razor *@
<div class="telescope-panels">
    @foreach (var telescope in ActiveTelescopes)
    {
        <TelescopePanel TelescopeId="@telescope.Id" />
    }
</div>
```

3. **Per-telescope status display** (4-6 hours)
```razor
@* TelescopePanel.razor *@
<div class="telescope-panel">
    <h3>@TelescopeName</h3>
    <div>RA: @RightAscension</div>
    <div>Dec: @Declination</div>
    <div>Status: @Status</div>
    <button @onclick="Connect">Connect</button>
    <button @onclick="Disconnect">Disconnect</button>
</div>
```

4. **Per-telescope settings page** (6-8 hours)
- Settings UI per telescope
- Save/Load per-telescope settings
- Import/Export telescope configurations

**Total Phase 4.6**: 20-28 hours

---

## File Changes Required

### New Files to Create

1. **MountQueueInstance.cs** - Instance-based mount queue
2. **SkyQueueInstance.cs** - Instance-based sky queue
3. **MountControlService.cs** - Multi-instance manager
4. **TelescopeSelector.razor** - UI telescope selector
5. **TelescopePanel.razor** - Per-telescope UI panel
6. **TelescopeConfiguration.cs** - Telescope config model

### Files to Modify

#### **High Impact** (Major Changes)

1. **MountInstance.cs** ??????
   - Add instance fields for all state
   - Add instance queues
   - Add instance tasks methods
   - Remove static property dependencies
   - **Lines affected**: ~500+

2. **SkySettingsInstance.cs** ????
   - Add file-based loading
   - Remove static bridge dependency
   - **Lines affected**: ~200+

3. **MountQueue.cs** ? **MountQueueInstance.cs** ??????
   - Convert from static to instance
   - **Lines affected**: All (complete rewrite)

4. **SkyQueue.cs** ? **SkyQueueInstance.cs** ??????
   - Convert from static to instance
   - Add per-instance serial port
   - **Lines affected**: All (complete rewrite)

#### **Medium Impact** (Moderate Changes)

5. **SkyServer.cs** ????
   - Keep static facade for backward compatibility
   - Delegate to default instance
   - **Lines affected**: ~100-200

6. **SkyServer.Core.cs** ????
   - Update static methods to work with instances
   - **Lines affected**: ~100-200

7. **TelescopeDriver.cs** ????
   - Use MountControlService
   - Support multiple device numbers
   - **Lines affected**: ~50-100

#### **Low Impact** (Minor Changes)

8. **Program.cs** ??
   - Register MountControlService
   - **Lines affected**: ~5-10

9. **All Blazor pages** ??
   - Inject MountControlService
   - Update bindings
   - **Lines affected**: ~10-20 per page

---

## Testing Strategy

### Unit Tests

1. **MountInstance Tests**
   - Test instance isolation
   - Test independent state
   - Test concurrent operations

2. **Queue Tests**
   - Test instance queue isolation
   - Test command routing
   - Test error handling

3. **Settings Tests**
   - Test per-telescope settings
   - Test settings persistence
   - Test settings independence

### Integration Tests

1. **Multi-Instance Connection**
```csharp
[Test]
public void TwoTelescopesCanConnectSimultaneously()
{
    var service = new MountControlService();
    
    var instanceA = service.CreateInstance("telescope-a", "settings-a.json");
    var instanceB = service.CreateInstance("telescope-b", "settings-b.json");
    
    Assert.IsTrue(instanceA.Connect());
    Assert.IsTrue(instanceB.Connect());
    
    Assert.IsTrue(instanceA.IsConnected);
    Assert.IsTrue(instanceB.IsConnected);
}
```

2. **Concurrent Operations**
```csharp
[Test]
public void TelescopesOperateIndependently()
{
    var instanceA = service.GetInstance("telescope-a");
    var instanceB = service.GetInstance("telescope-b");
    
    // Start simultaneous slews
    Task.Run(() => instanceA.SlewToTarget(ra1, dec1));
    Task.Run(() => instanceB.SlewToTarget(ra2, dec2));
    
    // Verify independence
    Assert.AreNotEqual(instanceA.RightAscension, instanceB.RightAscension);
}
```

3. **State Isolation**
```csharp
[Test]
public void TelescopeStateIsIsolated()
{
    var instanceA = service.GetInstance("telescope-a");
    var instanceB = service.GetInstance("telescope-b");
    
    instanceA.Tracking = true;
    instanceB.Tracking = false;
    
    Assert.IsTrue(instanceA.Tracking);
    Assert.IsFalse(instanceB.Tracking);
}
```

### Manual Testing

1. **Physical Hardware Test**
   - Connect 2+ SkyWatcher mounts simultaneously
   - Verify independent operation
   - Test concurrent slews
   - Test settings isolation

2. **Simulator Test**
   - Create 3+ simulator instances
   - Verify UI shows all instances
   - Test simultaneous operations

---

## Migration Path for Existing Users

### Backward Compatibility

**Phase 4.0**: Keep static facade
```csharp
// SkyServer.cs (static)
public static bool IsMountRunning
{
    get => _defaultInstance?.IsRunning ?? false;
    set
    {
        if (value)
            _defaultInstance?.Start();
        else
            _defaultInstance?.Stop();
    }
}
```

This ensures existing code continues to work:
```csharp
// Old code still works:
SkyServer.IsMountRunning = true;
SkyServer.SlewToTarget(ra, dec);
```

### Migration Guide for Users

**Single Telescope Users**: No changes required  
**Multi-Telescope Users**: Follow migration guide

```
1. Save your current settings
2. Update to new version
3. Create per-telescope settings files:
   - telescope-1.settings.json (copy from appsettings.user.json)
   - telescope-2.settings.json (create new)
4. Configure COM ports per telescope
5. Start application
6. Select telescope from dropdown
7. Connect to each telescope
```

---

## Risk Assessment

### High Risk Areas

1. **Queue Isolation** ??????
   - Risk: Commands go to wrong telescope
   - Mitigation: Extensive testing, command ID per instance

2. **Serial Port Management** ??????
   - Risk: Port conflicts, access violations
   - Mitigation: Port locking, proper disposal

3. **State Synchronization** ????
   - Risk: UI shows wrong telescope state
   - Mitigation: Clear instance binding in UI

### Medium Risk Areas

4. **Settings Persistence** ????
   - Risk: Settings get mixed up
   - Mitigation: File-based isolation, validation

5. **Timer Management** ????
   - Risk: Too many timers, performance issues
   - Mitigation: Single timer with per-instance callbacks

### Low Risk Areas

6. **UI Complexity** ??
   - Risk: Confusing UI with multiple telescopes
   - Mitigation: Clear labeling, color coding

---

## Performance Considerations

### Memory Usage

**Per Telescope Overhead**:
- MountInstance: ~500 bytes
- Queue: ~10KB
- Settings: ~5KB
- Timers: ~1KB
- **Total**: ~16KB per telescope

**Maximum Telescopes**: 50+ (limited by COM ports, not memory)

### CPU Usage

**Current**: Single telescope ~5% CPU  
**With 5 Telescopes**: ~15-20% CPU (3-4% per telescope)

### Threading

Each telescope will have:
- Command queue thread
- Update timer callback
- Serial port thread (SkyWatcher only)

**Total threads**: 3-4 per telescope

---

## Timeline Estimate

| Phase | Duration | Effort | Dependencies |
|-------|----------|--------|--------------|
| 4.1 - Instance State | 2 weeks | 16 hours | None |
| 4.2 - Per-Instance Settings | 2 weeks | 17 hours | 4.1 |
| 4.3 - Per-Instance Queues | 2 weeks | 38 hours | 4.1, 4.2 |
| 4.4 - Per-Instance Tasks | 1 week | 15 hours | 4.3 |
| 4.5 - Multi-Instance Service | 1 week | 16 hours | 4.4 |
| 4.6 - UI Updates | 1 week | 28 hours | 4.5 |
| **Testing** | 2 weeks | 20 hours | All |
| **TOTAL** | **12 weeks** | **150 hours** | |

---

## Immediate Next Steps

### Step 1: Start Phase 4.1 (This Week)

1. Create feature branch:
```bash
git checkout -b feature/multi-telescope-phase4
```

2. Modify `MountInstance.cs`:
   - Convert properties to fields
   - Add copy logic in `MountConnect()`
   - Test with single telescope

3. Run tests:
```bash
dotnet test
```

4. Commit:
```bash
git commit -m "Phase 4.1: Convert static properties to instance fields"
```

### Step 2: Create Test Infrastructure (Next Week)

1. Create `MountInstanceTests.cs`
2. Create `MultiTelescopeTests.cs`
3. Write initial tests

### Step 3: Phase 4.2 (Week 3-4)

1. Extend `SkySettingsInstance`
2. Implement file-based settings
3. Test settings isolation

---

## Success Criteria

### Phase 4 Complete When:

? Multiple telescope instances can connect simultaneously  
? Each telescope has independent settings  
? Each telescope has independent state  
? Each telescope has independent queue  
? UI shows multiple telescopes  
? All existing tests pass  
? New multi-telescope tests pass  
? Documentation updated  
? Migration guide published

---

## Conclusion

**Full multi-telescope support requires significant refactoring** but the architecture is already moving in the right direction with Phase 3.2's instance-based foundation.

**Current fix (Phase 3.2)** is appropriate for single telescope use.

**Begin Phase 4.1 when**:
- Feature is prioritized
- Team capacity available
- Testing infrastructure ready

**Estimated completion**: 3 months with 1 developer, 1.5 months with 2 developers

