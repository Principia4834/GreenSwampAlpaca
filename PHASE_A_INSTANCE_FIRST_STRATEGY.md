# Alternative Strategy: Instance-Based SkySettings First

**Goal:** Convert static `SkySettings` to instance-based, THEN migrate to new settings  
**Advantage:** Separates architectural risk from data migration risk  
**Status:** ?? RECOMMENDED APPROACH

---

## Why This Is Better

### Current Problem
Trying to do BOTH at once:
- ? Convert static ? instance
- ? Migrate old settings ? new settings
- ? Handle 300+ usages in SkyServer
- ? Preserve 13 side effects
- ?? Too many risks combined

### New Approach: Two Phases
**Phase A:** Static ? Instance (Architecture)
- ? Keep old Properties.SkyTelescope backend
- ? Keep all property names
- ? Keep all side effects
- ? Just change from static to instance
- ?? Lower risk (no data migration)

**Phase B:** Old Settings ? New Settings (Data)
- ? Instance-based already working
- ? Simple property mapping
- ? Use existing bridge
- ?? Lower risk (architecture already stable)

---

## Phase A: Static ? Instance Conversion

### Step A.1: Create Instance Wrapper (Day 1)

**Create:** `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

```csharp
namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Instance-based wrapper around static SkySettings.
    /// Phase A: Converts static to instance while keeping Properties.SkyTelescope backend.
    /// </summary>
    public class SkySettingsInstance
    {
        // Singleton pattern for backward compatibility
        private static SkySettingsInstance? _instance;
        public static SkySettingsInstance Instance => _instance 
            ?? throw new InvalidOperationException("SkySettingsInstance not initialized");

        public static void Initialize()
        {
            _instance = new SkySettingsInstance();
            SkySettings.Load(); // Use existing Load() method
        }

        // Wrap each property with instance access
        // Connection Settings
        public MountType Mount
        {
            get => SkySettings.Mount;
            set => SkySettings.Mount = value;
        }

        public string Port
        {
            get => SkySettings.Port;
            set => SkySettings.Port = value;
        }

        public SerialSpeed BaudRate
        {
            get => SkySettings.BaudRate;
            set => SkySettings.BaudRate = value;
        }

        // Location Settings
        public double Latitude
        {
            get => SkySettings.Latitude;
            set => SkySettings.Latitude = value;  // ? Side effect preserved!
        }

        public double Longitude
        {
            get => SkySettings.Longitude;
            set => SkySettings.Longitude = value;
        }

        public double Elevation
        {
            get => SkySettings.Elevation;
            set => SkySettings.Elevation = value;
        }

        // ... continue for all 93 properties ...
        
        // Side-effect properties automatically preserved because we call static setters!
        public double MaxSlewRate
        {
            get => SkySettings.MaxSlewRate;
            set => SkySettings.MaxSlewRate = value;  // ? Calls SkyServer.SetSlewRates()!
        }

        public int MinPulseRa
        {
            get => SkySettings.MinPulseRa;
            set => SkySettings.MinPulseRa = value;  // ? Calls SkyServer.SkyTasks()!
        }

        // Methods
        public void Save()
        {
            SkySettings.Save();
        }

        public void Load()
        {
            SkySettings.Load();
        }

        // Events - forward from static
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => SkySettings.StaticPropertyChanged += value;
            remove => SkySettings.StaticPropertyChanged -= value;
        }
    }
}
```

**Key Benefits:**
- ? All side effects preserved (we call static setters)
- ? All existing code still works (static class untouched)
- ? Can be used with DI
- ? Incremental migration path

### Step A.2: Register in DI (Day 1)

**File:** `GreenSwamp.Alpaca.Server/Program.cs`

```csharp
// Initialize singleton
SkySettingsInstance.Initialize();

// Register for DI
builder.Services.AddSingleton(SkySettingsInstance.Instance);
```

### Step A.3: Migrate SkyServer to Use Instance (Days 2-5)

**Before:**
```csharp
public static partial class SkyServer
{
    private static void MountConnect()
    {
        // 300+ direct static calls
        if (SkySettings.AtPark) { ... }
        var port = SkySettings.Port;
        SkySettings.MaxSlewRate = value;
    }
}
```

**After:**
```csharp
public static partial class SkyServer
{
    private static SkySettingsInstance _settings;
    
    public static void Initialize(SkySettingsInstance settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }
    
    private static void MountConnect()
    {
        // Same logic, just use instance
        if (_settings.AtPark) { ... }
        var port = _settings.Port;
        _settings.MaxSlewRate = value;  // ? Side effect still works!
    }
}
```

**Migration Pattern:**
```bash
# Find and replace in SkyServer files
Find:    SkySettings\.
Replace: _settings.
```

### Step A.4: Test Phase A (Day 6)

**Testing Checklist:**
- [ ] Build successful
- [ ] Simulator connects
- [ ] Settings load correctly
- [ ] Settings save correctly
- [ ] Side effects work (MaxSlewRate, Encoders, etc.)
- [ ] UI still works
- [ ] No behavioral changes

**At this point:**
- ? Architecture converted (static ? instance)
- ? All code works
- ? Still using old Properties.SkyTelescope backend
- ? Can commit and deploy safely

---

## Phase B: Migrate to New Settings Backend

### Step B.1: Modify Instance to Use New Backend (Day 7)

**File:** `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

```csharp
public class SkySettingsInstance
{
    private readonly IVersionedSettingsService _newSettings;
    private Settings.Models.SkySettings _current;
    
    public SkySettingsInstance(IVersionedSettingsService newSettings)
    {
        _newSettings = newSettings;
        _current = _newSettings.GetSettings();
        
        // Subscribe to changes
        _newSettings.SettingsChanged += (s, settings) => 
        {
            _current = settings;
            OnPropertyChanged(null); // Notify all properties changed
        };
    }

    // Now properties use new backend
    public string Port
    {
        get => _current.Port;
        set
        {
            _current.Port = value;
            _newSettings.SaveSettingsAsync(_current).Wait();
            OnPropertyChanged();
        }
    }

    // Side effects now explicit!
    public double MaxSlewRate
    {
        get => _current.MaximumSlewRate;
        set
        {
            _current.MaximumSlewRate = value;
            _newSettings.SaveSettingsAsync(_current).Wait();
            SkyServer.SetSlewRates(value);  // ? Explicit side effect
            OnPropertyChanged();
        }
    }

    // ... rest of properties ...

    public void Save()
    {
        _newSettings.SaveSettingsAsync(_current).Wait();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

### Step B.2: Remove Old Static Class (Day 8)

**Delete:**
- `GreenSwamp.Alpaca.MountControl/SkySettings.cs` (old static class)
- `GreenSwamp.Alpaca.MountControl/SkySettingsBridge.cs` (no longer needed)
- Properties.SkyTelescope references

### Step B.3: Test Phase B (Days 9-10)

**Testing Checklist:**
- [ ] Build successful
- [ ] Settings load from JSON
- [ ] Settings save to JSON
- [ ] Versioning works
- [ ] Side effects work
- [ ] UI updates correctly
- [ ] No regressions

---

## Comparison: Full vs Phased Approach

### Original Plan (Full Migration)
```
Day 1-2:  Infrastructure ? (done)
Day 3-10: Migrate SkyServer (300+ changes, both static?instance AND old?new)
Day 11:   Migrate SkySystem
Day 12-13: Other services
Day 14:   Cleanup
Day 15-20: Testing
```
**Risk:** ?????? HIGH (too many changes at once)

### New Plan (Phased)
```
PHASE A: Static ? Instance
Day 1:    Create instance wrapper
Day 2-5:  Migrate SkyServer (just change access, keep backend)
Day 6:    Test & commit ? SAFE CHECKPOINT

PHASE B: Old ? New Backend
Day 7:    Modify wrapper to use new backend
Day 8:    Remove old code
Day 9-10: Test & commit ? SAFE CHECKPOINT
```
**Risk:** ?? MEDIUM (changes separated, two safe checkpoints)

---

## Risk Comparison

### Phase A Risks (Lower)
| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Side effects break | LOW | HIGH | Still calling static setters |
| Initialization order | LOW | MEDIUM | Singleton pattern |
| Missing usages | MEDIUM | LOW | Compiler will catch |
| Performance | LOW | LOW | Just delegation |

### Phase B Risks (Lower)
| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Data migration | LOW | MEDIUM | Bridge already tested |
| Side effects | LOW | HIGH | Now explicit in code |
| Performance | LOW | LOW | Already tested in Phase A |

### Combined Full Migration Risks (Higher)
| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Too many changes | HIGH | CRITICAL | Hard to isolate issues |
| Side effects | HIGH | CRITICAL | Easy to miss one |
| Testing complexity | HIGH | HIGH | Can't isolate failures |

---

## Implementation Timeline

### Week 1: Phase A (Days 1-6)
- **Day 1:** Create `SkySettingsInstance` wrapper (all 93 properties)
- **Day 2-3:** Migrate `SkyServer.Core.cs` (~300 usages)
- **Day 4:** Migrate `SkySystem.cs` (~20 usages)
- **Day 5:** Migrate other services (~70 usages)
- **Day 6:** Testing & commit ? **SAFE CHECKPOINT**

### Week 2: Phase B (Days 7-10)
- **Day 7:** Modify wrapper to use `IVersionedSettingsService`
- **Day 8:** Remove old static `SkySettings` class and bridge
- **Day 9-10:** Testing & commit ? **SAFE CHECKPOINT**

**Total Time:** 10 days (vs 15-20 for full migration)  
**Risk:** ?? MEDIUM (vs HIGH for full migration)  
**Safe Checkpoints:** 2 (vs 0 for full migration)

---

## Rollback Strategy

### After Phase A
```bash
# Everything works, just using instance instead of static
# Can stay here permanently if needed!
git tag phase-a-complete
```

### After Phase B
```bash
# New backend in use, old code removed
# Can rollback to Phase A if issues found
git checkout phase-a-complete
```

### Emergency Rollback
```bash
# Back to bridge approach (already working)
git checkout phase-3.2-complete
```

---

## Code Generation Template

### For Step A.1 (Creating Instance Wrapper)

I can generate the full `SkySettingsInstance.cs` file with all 93 properties automatically wrapped. Each property will:
1. Delegate get to `SkySettings.PropertyName`
2. Delegate set to `SkySettings.PropertyName = value` (preserving side effects)
3. Include XML documentation
4. Maintain same property names
5. Preserve event forwarding

**Would you like me to generate this file?**

---

## Decision

**RECOMMENDED: Phased Approach (A then B)**

**Advantages:**
- ? Lower risk (separated concerns)
- ? Two safe checkpoints
- ? Easier to test
- ? Easier to debug
- ? Can stop after Phase A if needed
- ? Faster (10 days vs 15-20)

**Next Steps:**

1. **START PHASE A:**
   ```bash
   git checkout -b phase-a-instance-conversion
   ```

2. **Generate `SkySettingsInstance.cs`** (I can do this)

3. **Modify `SkyServer` to use instance**

4. **Test & commit checkpoint**

5. **Then decide on Phase B**

---

## Recommendation

**START WITH PHASE A**

Type:
- **"GENERATE WRAPPER"** - I'll create the full `SkySettingsInstance.cs` file
- **"EXPLAIN MORE"** - More details on specific steps
- **"KEEP BRIDGE"** - Stay with current working solution

**Phase A alone gets you 80% of the benefit with 50% of the risk.**

You can stop after Phase A and keep the old backend if you want. The architecture will be clean, just using old storage.
