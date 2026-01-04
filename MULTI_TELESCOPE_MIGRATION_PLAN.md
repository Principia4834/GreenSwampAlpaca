# Multi-Telescope Architecture: Variable Initialization Strategy

## Current Implementation (Phase 3.2)

### What We Just Fixed

```csharp
// In MountInstance.cs (lines 48-50)
private double[] _factorStep => SkyServer.FactorStep;  // ? Reads from static
private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;  // ? Reads from static
private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;  // ? Reads from static
```

**Current Architecture**:
- Static `SkyServer` holds ONE set of values (single telescope)
- All `MountInstance` objects read from the SAME static properties
- Works fine for single telescope

---

## Problem with Multi-Telescope

### Scenario: Two Telescopes

```
Telescope A (EQ6-R):
- StepsPerRevolution: [9024000, 9024000]
- FactorStep: [0.0001234, 0.0001234]

Telescope B (HEQ5):
- StepsPerRevolution: [8640000, 8640000]
- FactorStep: [0.0001296, 0.0001296]
```

### Current Broken Flow:

```csharp
// User connects Telescope A
instanceA.Connect();
  ? SimTasks(GetFactorStep)
  ? SkyServer.FactorStep = [0.0001234, 0.0001234]  // Static!

// User connects Telescope B
instanceB.Connect();
  ? SimTasks(GetFactorStep)
  ? SkyServer.FactorStep = [0.0001296, 0.0001296]  // Overwrites Telescope A!

// Now Telescope A is BROKEN:
instanceA._factorStep[0]  // Returns 0.0001296 (WRONG! Should be 0.0001234)
```

**Result**: Last telescope to connect overwrites static values, breaking all other instances!

---

## Required Changes for Multi-Telescope

### Phase 4: Instance-Owned State

#### Option 1: Instance Fields Populated After Tasks (Recommended)

```csharp
// In MountInstance.cs

// Change from properties to fields:
private double[] _factorStep = new double[2];
private long[] _stepsPerRevolution = new long[2];
private double[] _stepsWormPerRevolution = new double[2];

// In MountConnect(), AFTER running tasks:
private bool MountConnect()
{
    // ... existing code ...
    
    switch (SkySettings.Mount)
    {
        case MountType.Simulator:
            // Run tasks that populate TEMPORARY static values
            SkyServer.SimTasks(MountTaskName.StepsPerRevolution);
            SkyServer.SimTasks(MountTaskName.StepsWormPerRevolution);
            SkyServer.SimTasks(MountTaskName.GetFactorStep);
            
            // NEW: Copy static to instance IMMEDIATELY after tasks
            Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
            Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
            Array.Copy(SkyServer.FactorStep, _factorStep, 2);
            
            // Now use instance values
            raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
            // ...
            break;
            
        case MountType.SkyWatcher:
            // Same pattern
            SkyServer.SkyTasks(MountTaskName.StepsPerRevolution);
            SkyServer.SkyTasks(MountTaskName.StepsWormPerRevolution);
            SkyServer.SkyTasks(MountTaskName.GetFactorStep);
            
            // Copy to instance
            Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
            Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
            Array.Copy(SkyServer.FactorStep, _factorStep, 2);
            
            raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
            // ...
            break;
    }
}
```

**Pros**:
- ? Each instance has its own values
- ? No interference between telescopes
- ? Static values are temporary (overwritten is OK)

**Cons**:
- ?? Requires modifying `MountConnect()` to copy values
- ?? More memory per instance (negligible)

---

#### Option 2: Direct Query Methods (Future Phase 5)

```csharp
// Create instance-specific task methods that return values directly

private bool MountConnect()
{
    switch (SkySettings.Mount)
    {
        case MountType.Simulator:
            // Query directly into instance fields
            _stepsPerRevolution = QuerySimulatorStepsPerRevolution();
            _stepsWormPerRevolution = QuerySimulatorStepsWormPerRevolution();
            _factorStep = QuerySimulatorFactorStep();
            
            raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
            break;
    }
}

// New methods:
private long[] QuerySimulatorStepsPerRevolution()
{
    var spr = new CmdSpr(MountQueue.NewId);
    var sprnum = (long)MountQueue.GetCommandResult(spr).Result;
    return new[] { sprnum, sprnum };
}

private double[] QuerySimulatorFactorStep()
{
    var factorStep = new CmdFactorSteps(MountQueue.NewId);
    var factor = (double)MountQueue.GetCommandResult(factorStep).Result;
    return new[] { factor, factor };
}
```

**Pros**:
- ? No dependency on static properties
- ? Cleaner architecture
- ? Each instance queries its own values

**Cons**:
- ?? Duplicates logic from `SimTasks()` and `SkyTasks()`
- ?? More refactoring required

---

## Current State Analysis

### What Properties Read from Static?

```csharp
// MountInstance.cs - Line 48-50
private double[] _factorStep => SkyServer.FactorStep;  // ?? SHARED
private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;  // ?? SHARED
private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;  // ?? SHARED
```

### What Else Reads from Static?

Let me check...

#### In MountInstance:
- ? `_settings` - Instance-specific (good!)
- ?? `_factorStep` - Static (needs fixing)
- ?? `_stepsPerRevolution` - Static (needs fixing)
- ?? `_stepsWormPerRevolution` - Static (needs fixing)
- ? `_wormTeethCount` - Calculated from above (will be good once fixed)
- ? `_pecBinSteps` - Calculated from above (will be good once fixed)

#### Also Uses Static SkyServer:
- `SkyServer.PecBinCount` (line 162, 272) - ?? Should be instance
- `SkyServer.CanPPec` (line 265) - ?? Should be instance
- `SkyServer.ConnectAlignmentModel()` (line 211, 336) - ?? Should be instance
- `SkyServer.ActualAxisX/Y` (line 343) - ?? Should be instance
- `SkyServer.CalcCustomTrackingOffset()` (line 274) - ?? Should be instance

---

## Migration Roadmap

### Phase 3.2 (CURRENT - ? DONE)
**Goal**: Fix immediate bugs while maintaining single telescope

**Status**: Properties read from static
- ? Prevents division by zero
- ? Works for single telescope
- ?? NOT multi-telescope safe

---

### Phase 4 (NEXT - Required for Multi-Telescope)
**Goal**: Instance-owned state

**Changes Required**:

1. **Convert properties to fields**:
```csharp
// From:
private double[] _factorStep => SkyServer.FactorStep;

// To:
private double[] _factorStep = new double[2];
```

2. **Copy values after tasks run**:
```csharp
// In MountConnect() after SimTasks/SkyTasks:
Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
Array.Copy(SkyServer.FactorStep, _factorStep, 2);
```

3. **Add per-instance PEC count**:
```csharp
private const int _pecBinCount = 100;  // Or read from settings
```

4. **Migrate capabilities to instance**:
```csharp
private bool _canPPec;
private bool _canHomeSensor;
// etc.
```

---

### Phase 5 (FUTURE - Full Decoupling)
**Goal**: Remove all static dependencies

**Changes Required**:

1. **Instance-specific task methods**
2. **Instance-specific queues** (or queue routing)
3. **Instance-specific settings**
4. **Instance-specific alignment model**

---

## Immediate Action Required?

### For Current Single-Telescope Use: **NO**
- Current fix works perfectly ?
- No changes needed for now ?

### Before Multi-Telescope Support: **YES**
- Must implement Phase 4 changes ??
- Timeline: Before adding multi-telescope feature

---

## Testing Multi-Telescope Readiness

### Test Case 1: Sequential Connections
```csharp
var instanceA = new MountInstance("telescope-a", settingsA);
instanceA.Connect();
var factorA = instanceA._factorStep[0];  // Should be unique to A

var instanceB = new MountInstance("telescope-b", settingsB);
instanceB.Connect();
var factorB = instanceB._factorStep[0];  // Should be unique to B

// VERIFY: factorA should NOT change after connecting B
Assert.AreEqual(originalFactorA, instanceA._factorStep[0]);
```

### Test Case 2: Concurrent Operations
```csharp
var instanceA = new MountInstance("telescope-a", settingsA);
var instanceB = new MountInstance("telescope-b", settingsB);

Task.Run(() => instanceA.UpdateSteps());
Task.Run(() => instanceB.UpdateSteps());

// VERIFY: No race conditions, no value corruption
```

---

## Recommended Approach

### For Now (Phase 3.2): ? DONE
Keep current property-based approach:
```csharp
private double[] _factorStep => SkyServer.FactorStep;
```

**Rationale**:
- Works for current single-telescope use
- Simple and maintainable
- Easy to change later

### Before Multi-Telescope (Phase 4):
Switch to field-based with copy:
```csharp
private double[] _factorStep = new double[2];

// In MountConnect():
Array.Copy(SkyServer.FactorStep, _factorStep, 2);
```

**Rationale**:
- Each instance owns its values
- No interference between telescopes
- Minimal code changes from Phase 3.2

---

## Files That Will Need Changes

### Phase 4 Changes:

1. **MountInstance.cs** (PRIMARY):
   - Change properties to fields (lines 48-50)
   - Add copy logic in `MountConnect()` (after line 157 and 265)
   - Add instance-specific `_pecBinCount` field

2. **SkyServer.Core.cs** (MINOR):
   - Keep static properties for backward compatibility
   - Document that they're for single-telescope only

3. **Tests** (NEW):
   - Add multi-instance connection tests
   - Add concurrent operation tests
   - Verify instance isolation

---

## Decision Matrix

| Use Case | Current (Phase 3.2) | Phase 4 (Copy) | Phase 5 (Query) |
|----------|---------------------|----------------|-----------------|
| Single telescope | ? Perfect | ? Works | ? Works |
| Multi telescope | ? Broken | ? Works | ? Works |
| Complexity | ? Low | ? Medium | ?? High |
| Migration effort | ? Done | ?? Medium | ? High |
| Memory per instance | ? Minimal | ? Small | ? Small |
| **Recommended for** | **NOW** | **Multi-telescope** | **Future** |

---

## Summary

### Current Status (Phase 3.2): ? ACCEPTABLE

**Properties read from static - works for single telescope:**
```csharp
private double[] _factorStep => SkyServer.FactorStep;
```

**Pros**:
- ? Fixes immediate division-by-zero bug
- ? Simple and maintainable
- ? Easy to change later
- ? Sufficient for current single-telescope architecture

**Cons**:
- ?? NOT multi-telescope safe
- ?? Last connected telescope overwrites values

---

### Required for Multi-Telescope (Phase 4): ?? CHANGES NEEDED

**Must convert to instance-owned fields:**
```csharp
private double[] _factorStep = new double[2];

// Copy after tasks:
Array.Copy(SkyServer.FactorStep, _factorStep, 2);
```

**Timeline**: Before implementing multi-telescope feature

**Effort**: Medium (2-4 hours of work + testing)

---

## Recommendation

**Keep the current implementation** for now because:
1. ? It works correctly for current single-telescope use
2. ? Easy to change when multi-telescope is needed
3. ? Clear migration path exists (well-documented above)
4. ? No wasted effort - change only when required

**When to change**: Before starting multi-telescope work, follow Phase 4 plan above.

