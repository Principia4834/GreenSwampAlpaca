# Phase 3.2: Deep Dependency & Coupling Analysis

## Status: ANALYSIS COMPLETE ??

---

## Executive Summary

**CRITICAL FINDINGS:** The codebase has extensive PropertyChanged event coupling that creates hidden dependencies. These events are used for **real-time UI updates** and **cross-component communication**, making simple property access migration risky.

---

## PropertyChanged Event Pattern Analysis

### Pattern Found in Multiple Static Classes

```csharp
public static class SomeStaticClass
{
    public static event PropertyChangedEventHandler StaticPropertyChanged;
    
    private static bool _someProperty;
    public static bool SomeProperty
    {
        get => _someProperty;
        set
        {
            if (_someProperty == value) return;
            _someProperty = value;
            Properties.Something.Default.SomeProperty = value;
            OnStaticPropertyChanged();  // ?? TRIGGERS EVENT
        }
    }
    
    private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
    {
        StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
    }
}
```

### Classes Using This Pattern

1. **`SkySettings`** (3500+ lines)
   - 93 properties with PropertyChanged
   - Used for telescope mount settings
   - Real-time UI binding

2. **`Settings`** (Monitor settings)
   - Used for logging/monitoring configuration
   - Real-time UI updates

3. **`MonitorQueue`**
   - `WarningState`, `AlertState`, `MonitorEntry`, `PulseEntry`
   - UI notification system

4. **`SkySystem`**
   - `Devices`, `ConnectSerial`
   - Connection management

5. **`SkyQueue`** (SkyWatcher mount)
   - `IsPulseGuidingDec`, `IsPulseGuidingRa`, `Steps`
   - Real-time mount status

6. **`MountQueue`** (Simulator)
   - `IsPulseGuidingDec`, `IsPulseGuidingRa`, `Steps`
   - Simulator status

---

## Hidden Coupling Types

### Type 1: UI Binding Coupling

**Pattern:**
```csharp
// Blazor page subscribes to events
SkySettings.StaticPropertyChanged += OnSettingsChanged;

// When setting changes:
SkySettings.Port = "COM4";  
// ? Triggers PropertyChanged event
// ? UI automatically updates via subscription
```

**Impact:**
- If we remove static properties, UI breaks
- Must implement equivalent event system in new service
- Cannot simply migrate property access

**Files Affected:**
- All Blazor pages that bind to settings
- Settings forms
- Real-time status displays

---

### Type 2: Cross-Component Communication

**Pattern:**
```csharp
// Component A changes setting
SkySettings.MaxSlewRate = 3.0;
// ? PropertyChanged event fires
// ? Component B listening to event reacts:

void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == "MaxSlewRate")
    {
        SkyServer.SetSlewRates(SkySettings.MaxSlewRate);  // ?? SIDE EFFECT
    }
}
```

**Impact:**
- Property setters trigger side effects
- Cannot migrate individual properties in isolation
- Must understand all event subscribers first

**Examples Found:**
- `SkySettings.AlternatingPPec` ? calls `SkyServer.SkyTasks(MountTaskName.AlternatingPpec)`
- `SkySettings.DecPulseToGoTo` ? calls `SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo)`
- `SkySettings.Encoders` ? calls `SkyServer.SkyTasks(MountTaskName.Encoders)`
- `SkySettings.FullCurrent` ? calls `SkyServer.SkyTasks(MountTaskName.FullCurrent)`
- `SkySettings.GuideRateOffsetX/Y` ? calls `SkyServer.SetGuideRates()`
- `SkySettings.Latitude` ? calls `SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere)`
- `SkySettings.MaxSlewRate` ? calls `SkyServer.SetSlewRates(value)`
- `SkySettings.MinPulseDec/Ra` ? calls `SkyServer.SkyTasks(...)`
- `SkySettings.St4GuideRate` ? calls `SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate)`

---

### Type 3: Persistence Coupling

**Pattern:**
```csharp
public static string Port
{
    get => _port;
    set
    {
        if (_port == value) return;
        _port = value;
        Properties.SkyTelescope.Default.Port = value;  // ?? WRITES TO user.config
        LogSetting(MethodBase.GetCurrentMethod()?.Name, value);
        OnStaticPropertyChanged();
    }
}
```

**Impact:**
- Every property set writes to user.config
- Must replicate this behavior in new system
- Bridge handles this, but adds complexity

---

## SkySettings Property Analysis

### Properties with Side Effects (HIGH RISK)

These properties **DO MORE** than just store values:

| Property | Side Effect | Component Called |
|----------|-------------|------------------|
| `AlternatingPPec` | Sends mount command | `SkyServer.SkyTasks()` |
| `DecPulseToGoTo` | Sends mount command | `SkyServer.SkyTasks()` |
| `Encoders` | Sends mount command | `SkyServer.SkyTasks()` |
| `FullCurrent` | Sends mount command | `SkyServer.SkyTasks()` |
| `GuideRateOffsetX` | Recalculates rates | `SkyServer.SetGuideRates()` |
| `GuideRateOffsetY` | Recalculates rates | `SkyServer.SetGuideRates()` |
| `Latitude` | Hemisphere detection | `SkyServer.SkyTasks()` |
| `MaxSlewRate` | Updates slew rates | `SkyServer.SetSlewRates()` |
| `MinPulseDec` | Updates pulse config | `SkyServer.SkyTasks()` |
| `MinPulseRa` | Updates pulse config | `SkyServer.SkyTasks()` |
| `Mount` | Stops mount | `SkyServer.IsMountRunning = false` |
| `St4GuideRate` | Updates guide rate | `SkyServer.SkyTasks()` |
| `TrackingRate` | Resets rates | `SkyServer.RateDecOrg = 0; ...` |

**CRITICAL:** These cannot be simply migrated - they trigger **hardware operations**!

---

## Event Subscription Analysis

### Where Events Are Subscribed (MUST FIND ALL)

**Pattern to search for:**
```csharp
SkySettings.StaticPropertyChanged += SomeHandler;
Settings.StaticPropertyChanged += SomeHandler;
MonitorQueue.StaticPropertyChanged += SomeHandler;
SkySystem.StaticPropertyChanged += SomeHandler;
```

**Action Required:**
- Search entire codebase for `+= ` subscriptions
- Document all event handlers
- Understand what they do
- Plan equivalent in new system

**Expected Locations:**
- Blazor pages (`*.razor.cs` files)
- View models
- Background services
- Real-time update services

---

## Controllers - Good News! ?

**TelescopeController Analysis:**
- **NO SkySettings usage found**
- Uses `DeviceManager.GetTelescope(DeviceNumber)` pattern
- Telescope driver is isolated
- Controllers are already decoupled

**Step 1 (Controllers) Assessment:**
- **LIKELY VERY LOW EFFORT** ?
- May find zero changes needed
- Controllers call device drivers, not settings directly

---

## Revised Migration Strategy

### CRITICAL CHANGES TO PLAN

#### Original Plan: ? TOO RISKY
1. Step 1: Controllers (simple property swap)
2. Step 2: Blazor Pages (simple property swap)
3. Step 3: Services (simple property swap)
4. Step 4: Static Classes

#### REVISED Plan: ?? SAFER

### Phase A: Event System Analysis (1-2 days)
1. **Search for all event subscriptions:**
   ```bash
   grep -r "StaticPropertyChanged +=" --include="*.cs" .
   ```

2. **Document all subscribers:**
   - What properties they watch
   - What actions they take
   - Can we preserve behavior?

3. **Design new event system:**
   - `IVersionedSettingsService` needs `SettingsChanged` event
   - Must be compatible with existing subscribers
   - Bridge must forward events

### Phase B: Bridge Enhancement (1-2 days)
1. **Bi-directional event forwarding:**
   ```csharp
   // Old ? New
   SkySettings.StaticPropertyChanged += (s, e) => 
   {
       // Sync to new system
       _settingsService.OnSettingChanged(e.PropertyName);
   };
   
   // New ? Old
   _settingsService.SettingsChanged += (s, newSettings) =>
   {
       // Sync back to old system
       SkySettings.Port = newSettings.Port;
       // But MUST avoid infinite loop!
   };
   ```

2. **Side-effect preservation:**
   - When `settings.MaxSlewRate` changes via new system
   - Must still call `SkyServer.SetSlewRates()`
   - Options:
     - Keep side effects in setters (bridge triggers them)
     - Move side effects to event handlers
     - Move side effects to new service

### Phase C: Step-by-Step Migration (same as before)
1. Controllers (likely minimal)
2. Blazor Pages (with event handling)
3. Services (with side-effect handling)
4. Static Classes (service locator)

---

## Side Effect Handling Options

### Option 1: Keep in Old System (SAFEST)
```csharp
// New system
await _settingsService.SaveSettingsAsync(settings);

// Bridge syncs to old
SkySettings.MaxSlewRate = settings.MaxSlewRate;  
// ? Setter still calls SkyServer.SetSlewRates() ?
```

**Pros:**
- ? No behavior changes
- ? Side effects still work
- ? Low risk

**Cons:**
- ?? Keeps dependency on old system
- ?? Can't remove old system yet

### Option 2: Move to New Service (CLEANEST)
```csharp
public class VersionedSettingsService : IVersionedSettingsService
{
    public async Task SaveSettingsAsync(SkySettings settings)
    {
        // Detect changes
        if (Math.Abs(_current.MaxSlewRate - settings.MaxSlewRate) > 0.0001)
        {
            // Apply side effect
            SkyServer.SetSlewRates(settings.MaxSlewRate);
        }
        
        // Save
        _current = settings;
        // ...
    }
}
```

**Pros:**
- ? Clean architecture
- ? Can remove old system eventually
- ? Centralized logic

**Cons:**
- ?? More work
- ?? Higher risk
- ?? Must identify all 13+ side effects

### Option 3: Event-Based (FLEXIBLE)
```csharp
_settingsService.SettingsChanged += (sender, newSettings) =>
{
    if (SettingChanged("MaxSlewRate", _old, newSettings))
    {
        SkyServer.SetSlewRates(newSettings.MaxSlewRate);
    }
};
```

**Pros:**
- ? Flexible
- ? Easy to add/remove
- ? Testable

**Cons:**
- ?? Must wire up all handlers
- ?? Distributed logic

**RECOMMENDATION: Start with Option 1, migrate to Option 3**

---

## Blazor Page Coupling Example

**Expected pattern in pages:**
```csharp
@code {
    protected override void OnInitialized()
    {
        // Subscribe to static events
        SkySettings.StaticPropertyChanged += OnSettingsChanged;
        MonitorQueue.StaticPropertyChanged += OnMonitorChanged;
    }
    
    private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
    {
        // Update UI
        InvokeAsync(StateHasChanged);
    }
    
    public void Dispose()
    {
        // MUST unsubscribe or memory leak!
        SkySettings.StaticPropertyChanged -= OnSettingsChanged;
        MonitorQueue.StaticPropertyChanged -= OnMonitorChanged;
    }
}
```

**Migration challenge:**
- Must preserve real-time updates
- Must handle property-specific logic
- Must avoid memory leaks

---

## Risk Assessment

### HIGH RISK Items

1. **Side-Effect Properties** ??????
   - 13+ properties trigger hardware operations
   - Must preserve behavior exactly
   - Test thoroughly with real hardware

2. **Event Subscribers** ????
   - Unknown number of subscribers
   - Could be in any file
   - Missing one = broken feature

3. **Infinite Event Loops** ????
   - Bridge forwards events both ways
   - Must prevent loops: A?B?A?B...
   - Need "event suppression" flag

### MEDIUM RISK Items

4. **UI Real-Time Updates** ??
   - Blazor pages expect PropertyChanged
   - Must implement in new service
   - Test all pages

5. **Property Name Strings** ??
   - Events use string property names
   - Refactoring can break: `"MaxSlewRate"`
   - Consider using `nameof()` or enums

### LOW RISK Items

6. **Controllers** ?
   - Likely already decoupled
   - Minimal changes expected

---

## Required Pre-Migration Tasks

### MUST DO BEFORE Step 1:

1. **Search for Event Subscriptions** (30 min)
   ```bash
   grep -rn "StaticPropertyChanged +=" --include="*.cs" .
   grep -rn "StaticPropertyChanged -=" --include="*.cs" .
   ```

2. **Document All Subscribers** (1-2 hours)
   - Create spreadsheet
   - List: File | Line | Property | Action

3. **Test Current Behavior** (2-3 hours)
   - Connect to real/simulated hardware
   - Change each side-effect property
   - Document what happens
   - This is our "reference implementation"

4. **Enhance Bridge** (2-3 hours)
   - Add event forwarding
   - Add loop detection
   - Add side-effect preservation
   - Test thoroughly

---

## Revised Timeline

| Phase | Duration | Status |
|-------|----------|--------|
| **A: Event Analysis** | 1-2 days | ? REQUIRED |
| **B: Bridge Enhancement** | 1-2 days | ? REQUIRED |
| **C: Step 1 Controllers** | 0.5-1 day | ? PENDING |
| **C: Step 2 Blazor Pages** | 3-4 days | ? PENDING |
| **C: Step 3 Services** | 4-5 days | ? PENDING |
| **C: Step 4 Static Classes** | 5-7 days | ? PENDING |
| **Total** | **15-22 days** | vs **14-19** original |

**Additional time needed: +1-3 days for event analysis and bridge work**

---

## Recommendation

### DO NOT START Step 1 YET ??

**First complete:**

1. **Event subscription audit** (run grep commands)
2. **Document findings** (spreadsheet)
3. **Enhance bridge** (event forwarding)
4. **Test bridge** (verify no regressions)

**Then:**

5. **Start Step 1** (controllers - likely trivial)
6. **Proceed cautiously** to Steps 2-4

---

## Next Steps

### Immediate Actions Needed:

**A. Run Event Search:**
```bash
cd T:\source\repos\GreenSwampAlpaca
grep -rn "\.StaticPropertyChanged +=" --include="*.cs" . > event_subscriptions.txt
grep -rn "SkySettings\." --include="*.cs" --include="*.razor" . | grep -v "using" > skysettings_usage.txt
```

**B. Review Results:**
- How many event subscribers?
- Which files use SkySettings most?
- Any surprises?

**C. Make Go/No-Go Decision:**
- If < 20 subscribers: Proceed with enhanced plan
- If 20-50 subscribers: Requires more planning
- If > 50 subscribers: Consider alternative approach

---

## Alternative Approach (If Too Complex)

If event coupling is too extensive:

### Facade Pattern
Keep old static classes, but make them facades to new service:

```csharp
public static class SkySettings
{
    private static IVersionedSettingsService _service;
    
    public static void Initialize(IVersionedSettingsService service)
    {
        _service = service;
    }
    
    public static string Port
    {
        get => _service.GetSettings().Port;
        set
        {
            var settings = _service.GetSettings();
            settings.Port = value;
            _service.SaveSettingsAsync(settings).Wait();
            OnStaticPropertyChanged();  // Preserve event system
        }
    }
    
    // ... all other properties as facades
}
```

**Pros:**
- ? Zero migration needed for consumers
- ? Events still work
- ? Low risk

**Cons:**
- ?? Static class remains
- ?? Technical debt persists
- ?? Doesn't solve original problem

---

## Status: AWAITING EVENT AUDIT

**Cannot proceed safely without understanding event dependencies.**

**Recommended action: Run grep commands, analyze results, report back.**
