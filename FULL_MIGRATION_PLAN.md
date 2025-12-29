# Full Migration Plan: Remove Old Settings System

**Goal:** Complete removal of old static `SkySettings` class  
**Status:** ?? IN PROGRESS  
**Estimated Time:** 15-20 days  
**Risk Level:** ???? HIGH (Hardware operations)

---

## Current State Analysis

### Usage Statistics
- **Total SkySettings usages:** 541
- **SkyServer.Core.cs:** ~300 usages
- **SkySettings.cs (old):** ~150 usages (property implementations)
- **Other services:** ~91 usages
- **Controllers:** 0 usages ? (already clean)
- **Blazor pages:** 1 file (MountSettings.razor - already migrated)

### Files Affected
1. **OLD FILES TO DELETE:**
   - `GreenSwamp.Alpaca.MountControl/SkySettings.cs` (2,800+ lines)
   - `GreenSwamp.Alpaca.MountControl/SkySettingsBridge.cs` (once migration complete)
   - `Properties.SkyTelescope.Default` usage

2. **FILES TO MIGRATE:**
   - `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs` (~300 usages)
   - `GreenSwamp.Alpaca.MountControl/SkySystem.cs` (~20 usages)
   - Other mount control services
   - Telescope driver
   - Monitor services

---

## Migration Phases

### Phase 1: Prepare Infrastructure (Days 1-2) ? COMPLETE
- [x] Create new settings models
- [x] Create IVersionedSettingsService
- [x] Register in DI
- [x] Create bridge
- [x] Verify build

### Phase 2: Create Settings Wrapper Service (Day 3)
**Goal:** Create a service that provides instance-based access to settings

**Create:** `GreenSwamp.Alpaca.Settings/Services/SkySettingsAccessor.cs`

```csharp
public interface ISkySettingsAccessor
{
    SkySettings Get();
    Task SaveAsync(SkySettings settings);
    event EventHandler<SkySettings> Changed;
}

public class SkySettingsAccessor : ISkySettingsAccessor
{
    private readonly IVersionedSettingsService _service;
    
    public SkySettingsAccessor(IVersionedSettingsService service)
    {
        _service = service;
        _service.SettingsChanged += (s, settings) => Changed?.Invoke(this, settings);
    }
    
    public SkySettings Get() => _service.GetSettings();
    public Task SaveAsync(SkySettings settings) => _service.SaveSettingsAsync(settings);
    public event EventHandler<SkySettings> Changed;
}
```

**Register in DI:** `Program.cs`
```csharp
builder.Services.AddSingleton<ISkySettingsAccessor, SkySettingsAccessor>();
```

### Phase 3: Migrate SkyServer (Days 4-10) ?? MOST COMPLEX
**Goal:** Replace all static SkySettings calls in SkyServer

#### Step 3.1: Add DI to SkyServer
**Problem:** SkyServer is static  
**Solution:** Convert to instance-based with singleton pattern

**Before:**
```csharp
public static partial class SkyServer
{
    static SkyServer()
    {
        SkySettings.Load();
    }
}
```

**After:**
```csharp
public partial class SkyServer
{
    private readonly ISkySettingsAccessor _settings;
    private static SkyServer? _instance;
    
    public SkyServer(ISkySettingsAccessor settings)
    {
        _settings = settings;
        _instance = this;
    }
    
    public static SkyServer Instance => _instance 
        ?? throw new InvalidOperationException("SkyServer not initialized");
}
```

#### Step 3.2: Replace SkySettings Calls (Systematic)

**Pattern to find:**
```csharp
SkySettings.PropertyName
```

**Replace with:**
```csharp
_settings.Get().PropertyName
```

**For side-effect properties, extract to methods:**
```csharp
// OLD:
SkySettings.MaxSlewRate = value;  // Triggers SkyServer.SetSlewRates()

// NEW:
private void SetMaxSlewRate(double value)
{
    var settings = _settings.Get();
    settings.MaximumSlewRate = value;
    await _settings.SaveAsync(settings);
    SetSlewRates(value);  // Explicit side effect
}
```

#### Step 3.3: Handle Side Effects (13 Properties)

| Property | Side Effect | Migration Strategy |
|----------|-------------|-------------------|
| `AlternatingPPec` | `SkyServer.SkyTasks()` | Extract to `SetAlternatingPPec(bool value)` |
| `DecPulseToGoTo` | `SkyServer.SkyTasks()` | Extract to `SetDecPulseToGoTo(bool value)` |
| `Encoders` | `SkyServer.SkyTasks()` | Extract to `SetEncoders(bool value)` |
| `FullCurrent` | `SkyServer.SkyTasks()` | Extract to `SetFullCurrent(bool value)` |
| `GuideRateOffsetX` | `SkyServer.SetGuideRates()` | Extract to `SetGuideRateOffsetX(double value)` |
| `GuideRateOffsetY` | `SkyServer.SetGuideRates()` | Extract to `SetGuideRateOffsetY(double value)` |
| `Latitude` | `SkyServer.SkyTasks()` | Extract to `SetLatitude(double value)` |
| `MaxSlewRate` | `SkyServer.SetSlewRates()` | Extract to `SetMaxSlewRate(double value)` |
| `MinPulseDec` | `SkyServer.SkyTasks()` | Extract to `SetMinPulseDec(int value)` |
| `MinPulseRa` | `SkyServer.SkyTasks()` | Extract to `SetMinPulseRa(int value)` |
| `Mount` | `IsMountRunning = false` | Extract to `SetMount(MountType value)` |
| `St4GuideRate` | `SkyServer.SkyTasks()` | Extract to `SetSt4GuideRate(int value)` |
| `TrackingRate` | Resets rates | Extract to `SetTrackingRate(DriveRate value)` |

**Example Migration:**
```csharp
// OLD:
public static void SetGuideRates()
{
    _guideRate = new Vector(SkySettings.GuideRateOffsetY, SkySettings.GuideRateOffsetX);
    // ... rest of implementation
}

// NEW:
public void SetGuideRateOffsetX(double value)
{
    var settings = _settings.Get();
    settings.GuideRateOffsetX = value;
    _settings.SaveAsync(settings).Wait();  // Or make method async
    SetGuideRates();  // Trigger side effect
}

private void SetGuideRates()
{
    var settings = _settings.Get();
    _guideRate = new Vector(settings.GuideRateOffsetY, settings.GuideRateOffsetX);
    // ... rest of implementation
}
```

###Phase 4: Migrate SkySystem (Day 11)
**File:** `GreenSwamp.Alpaca.MountControl/SkySystem.cs`  
**Usages:** ~20

**Add DI:**
```csharp
public static class SkySystem
{
    private static ISkySettingsAccessor? _settings;
    
    public static void Initialize(ISkySettingsAccessor settings)
    {
        _settings = settings;
    }
    
    // Replace: SkySettings.Port
    // With: _settings.Get().Port
}
```

### Phase 5: Migrate Other Services (Days 12-13)
**Files:** Telescope driver, Monitor, etc.

**Strategy:** Add ISkySettingsAccessor via constructor injection

### Phase 6: Remove Old Code (Day 14)
**Delete files:**
1. `GreenSwamp.Alpaca.MountControl/SkySettings.cs`
2. `GreenSwamp.Alpaca.MountControl/SkySettingsBridge.cs`
3. Remove `Properties.SkyTelescope` project references

### Phase 7: Testing & Verification (Days 15-20)
- [ ] Build successful
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Simulator testing
- [ ] Hardware testing (if available)
- [ ] Regression testing

---

## Risks & Mitigation

### Risk 1: Side Effects Broken
**Probability:** HIGH  
**Impact:** CRITICAL (Hardware operations fail)

**Mitigation:**
- Test each side-effect property individually
- Use simulator first
- Document expected behavior
- Have rollback plan ready

### Risk 2: Initialization Order
**Probability:** MEDIUM  
**Impact:** HIGH (Null reference exceptions)

**Mitigation:**
- Use dependency injection properly
- Initialize services in correct order
- Add null checks where needed

### Risk 3: Static to Instance Complexity
**Probability:** HIGH  
**Impact:** MEDIUM (Refactoring errors)

**Mitigation:**
- Make SkyServer singleton initially
- Keep static facade temporarily
- Migrate callers gradually

### Risk 4: Performance Impact
**Probability:** LOW  
**Impact:** MEDIUM (Settings access slower)

**Mitigation:**
- Cache settings object locally where needed
- Don't call `Get()` in loops
- Profile hot paths

---

## Rollback Strategy

### Automatic Rollback Points
1. After Phase 2: Can revert, bridge still works
2. After Phase 3: Git branch rollback
3. After Phase 4: Git branch rollback

### Manual Rollback
```bash
git checkout phase-3.2-backup
git push --force-with-lease
```

---

## Testing Checklist

### Unit Tests
- [ ] Settings service tests
- [ ] Side-effect method tests
- [ ] Initialization tests

### Integration Tests  
- [ ] Mount operations
- [ ] Tracking operations
- [ ] Slewing operations
- [ ] Settings persistence

### Hardware Tests
- [ ] Connect to mount
- [ ] Initialize positions
- [ ] Change settings
- [ ] Verify hardware responds
- [ ] Test all side-effect properties

---

## Success Criteria

- [ ] Zero references to old `SkySettings` static class
- [ ] Zero references to `Properties.SkyTelescope.Default`
- [ ] All tests passing
- [ ] Build successful (0 errors, 0 warnings)
- [ ] Mount operations work correctly
- [ ] Settings persist correctly
- [ ] Side effects execute correctly
- [ ] No performance degradation

---

## Time Estimate Breakdown

| Phase | Days | Risk |
|-------|------|------|
| 1. Infrastructure | 2 | ? LOW (Done) |
| 2. Wrapper Service | 1 | ?? LOW |
| 3. SkyServer Migration | 6-7 | ?????? HIGH |
| 4. SkySystem Migration | 1 | ?? MEDIUM |
| 5. Other Services | 2 | ?? MEDIUM |
| 6. Cleanup | 1 | ?? LOW |
| 7. Testing | 5-6 | ???? HIGH |
| **TOTAL** | **15-20** | ???? HIGH |

---

## Decision Point

**READY TO PROCEED?**

This is a **high-risk, high-effort** migration. You should proceed if:
- ? You have comprehensive tests
- ? You have simulator/hardware for testing
- ? You have 2-3 weeks available
- ? You're willing to handle regression issues
- ? You value architecture purity over pragmatism

**Alternative:** Keep the enhanced bridge (already working, zero risk)

---

## Next Steps

**To proceed with full migration:**

1. **Create backup branch**
   ```bash
   git checkout -b full-migration-backup
   git push origin full-migration-backup
   ```

2. **Create work branch**
   ```bash
   git checkout -b full-migration
   ```

3. **Start Phase 2** (Create wrapper service)

**Type "PROCEED" to continue with Phase 2**  
**Type "KEEP BRIDGE" to abandon full migration**
