# Multi-Telescope Implementation Plan Review & First Steps

## Date: Current Session
## Status: READY TO BEGIN

---

## Current State Assessment

### ? Already Completed (Phase 3.2)

1. **MountInstance Class Created** ?
   - Location: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`
   - Instance-based controller with delegation to static
   - Core methods migrated: Connect, Start, Stop, Reset
   - Position methods: GetRawDegrees, GetRawSteps, ConvertStepsToDegrees, MapSlewTargetToAxes

2. **Instance State Fields** ? (Partial)
   - Basic fields: `_isMountRunning`, `_homeAxes`, `_appAxes`, `_targetRaDec`
   - Timers: `_mediaTimer`, `_altAzTrackingTimer`
   - Tracking: `_tracking`, `_trackingRate`, `_atPark`
   - PEC: `_wormTeethCount`, `_pecBinSteps`
   - Rates: `_skyHcRate`, `_skyTrackingRate`, `_guideRate`

3. **Properties Read from Static** ?? (Temporary Solution)
   ```csharp
   private double[] _factorStep => SkyServer.FactorStep;
   private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;
   private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;
   ```
   - **Status**: Working for single telescope
   - **Issue**: Will break with multiple telescopes

4. **SkySettingsInstance** ? (Partial)
   - Created but still reads from static `SkySettings`
   - Registered in DI container (Program.cs line 156)
   - Initialized after app.Build() (line 223)

5. **TelescopeStateService** ? (Just Added)
   - Real-time state updates synchronized with SkyServer loop
   - Registered as singleton (Program.cs line 200)
   - UI page displaying 26 properties

---

## Critical Issues That Must Be Fixed First

### ?? BLOCKING: Park Function Bugs (from PARK_FUNCTION_ANALYSIS.md)

**These must be fixed BEFORE multi-telescope work to avoid compounding issues:**

1. **Issue #1**: Duplicate CancellationTokenSource creation (lines 703 & 709)
2. **Issue #2**: Infinite wait loop with no timeout (line 665)
3. **Issue #3**: Early return prevents park completion (line 684)
4. **Issue #4**: AtPark timing issues (lines 2657, 785)
5. **Issue #5**: Missing lock protection (lines 662-663)
6. **Issue #6**: CancelAllAsync logic flaw (line 2954)

**Recommendation**: Fix Issues #1, #2, and #3 before starting Phase 4.1.

---

## Plan Validation

### ? Phase 4.1: Instance-Owned State
**Status**: VALID - Ready to begin  
**Prerequisites**: Park function bugs fixed  
**Estimated Time**: 11-16 hours

**Key Changes**:
```csharp
// Current (lines 48-50 in MountInstance.cs):
private double[] _factorStep => SkyServer.FactorStep;
private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;
private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;

// Phase 4.1 Target:
private double[] _factorStep = new double[2];
private long[] _stepsPerRevolution = new long[2];
private double[] _stepsWormPerRevolution = new double[2];

// Populate in MountConnect() after SimTasks/SkyTasks:
Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
Array.Copy(SkyServer.FactorStep, _factorStep, 2);
```

### ? Phase 4.2: Per-Instance Settings
**Status**: VALID - Depends on 4.1  
**Prerequisites**: Phase 4.1 complete  
**Estimated Time**: 12-17 hours

**Key Changes**:
- Extend `SkySettingsInstance` to load from file parameter
- Create per-telescope settings files in AppData
- Remove dependency on static `SkySettings`

### ?? Phase 4.3: Per-Instance Queues
**Status**: VALID but COMPLEX  
**Prerequisites**: Phase 4.1, 4.2 complete  
**Estimated Time**: 30-38 hours

**Key Changes**:
- Convert `MountQueue` from static class to instance class
- Convert `SkyQueue` from static class to instance class
- Update all command calls throughout codebase

**Warning**: This is the most complex and risky phase.

### ? Phases 4.4-4.6
**Status**: VALID - Sequential dependencies clear

---

## Recommended Approach: Phased with Validation

### Option A: Full Phase-by-Phase (Recommended)
**Advantages**: Lower risk, easier to debug, can stop at any phase  
**Timeline**: 12 weeks  
**Approach**: Complete 4.1, test, commit ? Complete 4.2, test, commit ? etc.

### Option B: Combined 4.1 + 4.2
**Advantages**: Faster initial progress  
**Timeline**: 10 weeks  
**Approach**: Do instance state AND settings together  
**Risk**: Medium - harder to isolate issues

### Option C: Skip to 4.3 (Not Recommended)
**Advantages**: None  
**Timeline**: Unknown  
**Risk**: HIGH - will likely need to redo work

**Recommendation**: **Option A** - Phase-by-phase with validation

---

## First Steps for Phase 4.1

### Prerequisites Checklist

Before starting Phase 4.1, complete these tasks:

- [ ] **CRITICAL**: Fix Park function bugs (Issues #1, #2, #3)
  - Location: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`
  - Lines: 665, 684, 703, 709
  - Estimated time: 2-3 hours
  - Test: Park command works reliably

- [ ] **Review**: Ensure Phase 3.2 is stable
  - Single telescope connects and operates correctly
  - Position updates work
  - Tracking works
  - All ASCOM commands function

- [ ] **Create**: Feature branch
  ```bash
  git checkout -b feature/phase-4.1-instance-state
  ```

- [ ] **Backup**: Create snapshot
  ```bash
  git commit -am "Phase 3.2 stable - before Phase 4.1"
  git tag phase-3.2-stable
  ```

---

## Phase 4.1 Implementation Steps

### Step 1: Convert Properties to Fields (2-3 hours)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Current** (lines 47-50):
```csharp
// Phase 3.2: Factor steps (conversion ratios)
private double[] _factorStep => SkyServer.FactorStep;  // Read from static
private long[] _stepsPerRevolution => SkyServer.StepsPerRevolution;  // Read from static
private double[] _stepsWormPerRevolution => SkyServer.StepsWormPerRevolution;  // Read from static
```

**Change to**:
```csharp
// Phase 4.1: Factor steps (conversion ratios) - instance-owned
private double[] _factorStep = new double[2];
private long[] _stepsPerRevolution = new long[2];
private double[] _stepsWormPerRevolution = new double[2];
```

**Why**: These will become truly instance-owned instead of reading from static.

---

### Step 2: Populate Fields in MountConnect() (2-3 hours)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Location**: After line 157 (Simulator) and after line 265 (SkyWatcher)

**After Simulator tasks** (after line 157):
```csharp
SkyServer.SimTasks(MountTaskName.GetFactorStep);
SkyServer.SimTasks(MountTaskName.Capabilities);

// Phase 4.1: Copy static values to instance fields
Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
Array.Copy(SkyServer.FactorStep, _factorStep, 2);

raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
```

**After SkyWatcher tasks** (after line 265):
```csharp
if (SkyServer.CanPPec) SkyServer.SkyTasks(MountTaskName.Pec);

// Phase 4.1: Copy static values to instance fields
Array.Copy(SkyServer.StepsPerRevolution, _stepsPerRevolution, 2);
Array.Copy(SkyServer.StepsWormPerRevolution, _stepsWormPerRevolution, 2);
Array.Copy(SkyServer.FactorStep, _factorStep, 2);

raWormTeeth = (int)(_stepsPerRevolution[0] / _stepsWormPerRevolution[0]);
```

**Why**: Values from SimTasks/SkyTasks populate static properties; we immediately copy to instance.

---

### Step 3: Add Capabilities Fields (1-2 hours)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Add after line 62** (after PEC fields):
```csharp
// Phase 4.1: Mount capabilities (instance-owned)
private bool _canPPec;
private bool _canHomeSensor;
private bool _canPolarLed;
private bool _canAdvancedCmdSupport;
private string _mountName = string.Empty;
private string _mountVersion = string.Empty;
private string _capabilities = string.Empty;
```

**Populate after tasks** (both Simulator and SkyWatcher sections):
```csharp
// Phase 4.1: Copy capabilities from static
_canPPec = SkyServer.CanPPec;
_canHomeSensor = SkyServer.CanHomeSensor;
_canPolarLed = SkyServer.CanPolarLed;
_canAdvancedCmdSupport = SkyServer.CanAdvancedCmdSupport;
_mountName = SkyServer.MountName;
_mountVersion = SkyServer.MountVersion;
_capabilities = SkyServer.Capabilities;
```

---

### Step 4: Add Position State Fields (1-2 hours)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Add after capabilities fields**:
```csharp
// Phase 4.1: Position state (instance-owned)
private double[] _steps = new double[2];
private double _actualAxisX;
private double _actualAxisY;
private double _rightAscensionXForm;
private double _declinationXForm;
private double _altitude;
private double _azimuth;
private double _declination;
private double _rightAscension;
```

**Note**: These will be populated by UpdateSteps() method (migrated later).

---

### Step 5: Add Tracking State Fields (1-2 hours)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Add after position fields**:
```csharp
// Phase 4.1: Tracking state (instance-owned)
private bool _isSlewing;
private SlewType _slewState = SlewType.SlewNone;
private bool _isPulseGuidingRa;
private bool _isPulseGuidingDec;
```

**Note**: Already have `_tracking` (line 53) and `_atPark` (line 65).

---

### Step 6: Add Logging to Verify Values (1 hour)

**File**: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**After copying values in MountConnect()**:
```csharp
Array.Copy(SkyServer.FactorStep, _factorStep, 2);

// Phase 4.1: Log copied values for verification
var monitorItem = new MonitorEntry
{
    Datetime = HiResDateTime.UtcNow,
    Device = MonitorDevice.Server,
    Category = MonitorCategory.Mount,
    Type = MonitorType.Information,
    Method = MethodBase.GetCurrentMethod()?.Name,
    Thread = Thread.CurrentThread.ManagedThreadId,
    Message = $"Phase4.1|Instance:{_id}|StepsPerRev:{_stepsPerRevolution[0]}|FactorStep:{_factorStep[0]}"
};
MonitorLog.LogToMonitor(monitorItem);
```

---

### Step 7: Build and Test (1-2 hours)

**Tests**:
1. Build solution (should succeed)
2. Start application with simulator
3. Connect mount
4. Verify log shows copied values
5. Check calculations:
   - `_wormTeethCount` should be valid (not 0)
   - `_pecBinSteps` should be valid (not 0 or NaN)
6. Test position updates
7. Test tracking
8. Test park command (after bug fixes)

---

### Step 8: Update Static Facade (Optional - 1 hour)

**File**: `GreenSwamp.Alpaca.MountControl/SkyServer.cs`

**Add comments to static properties**:
```csharp
/// <summary>
/// Factor to convert steps, Sky Watcher in rad
/// Phase 4.1: Static for backward compatibility only
/// Use MountInstance._factorStep for new code
/// </summary>
internal static double[] FactorStep { get; set; } = { 0.0, 0.0 };
```

---

## Validation Criteria for Phase 4.1

### Must Pass:
- [ ] Build succeeds with no errors
- [ ] Single telescope connects successfully
- [ ] Worm teeth calculated correctly (log shows non-zero values)
- [ ] PEC bin steps calculated correctly (not NaN or 0)
- [ ] Position updates work
- [ ] Tracking works
- [ ] Park command works (after bug fixes)
- [ ] No regression in existing functionality

### Success Metrics:
- [ ] All instance fields populated with correct values
- [ ] Log messages show instance ID and copied values
- [ ] No division by zero errors
- [ ] Mount operates identically to Phase 3.2

---

## Risk Assessment

### Low Risk Changes:
? Converting properties to fields  
? Adding new fields  
? Adding logging  

### Medium Risk Changes:
?? Copying values in MountConnect()  
?? Order of operations (tasks ? copy ? calculations)  

### High Risk Changes:
None in Phase 4.1 (this is the safe phase)

---

## Rollback Plan

If issues arise:

1. **Immediate Rollback**:
   ```bash
   git reset --hard phase-3.2-stable
   ```

2. **Partial Rollback**:
   ```bash
   git revert <commit-hash>
   ```

3. **Keep Changes but Disable**:
   - Comment out Array.Copy lines
   - Change fields back to properties reading from static

---

## Timeline Estimate

| Task | Time | Critical? |
|------|------|-----------|
| Fix Park bugs | 2-3 hours | ? YES |
| Step 1: Convert properties | 2-3 hours | No |
| Step 2: Populate fields | 2-3 hours | ?? YES |
| Step 3: Capabilities | 1-2 hours | No |
| Step 4: Position fields | 1-2 hours | No |
| Step 5: Tracking fields | 1-2 hours | No |
| Step 6: Logging | 1 hour | No |
| Step 7: Testing | 1-2 hours | ? YES |
| Step 8: Documentation | 1 hour | No |
| **TOTAL** | **12-18 hours** | |

---

## Next Steps After Phase 4.1

Once Phase 4.1 is complete and validated:

1. **Commit and tag**:
   ```bash
   git commit -am "Phase 4.1 complete: Instance-owned state"
   git tag phase-4.1-complete
   ```

2. **Review before Phase 4.2**:
   - Ensure all tests pass
   - Check performance (no degradation)
   - Review code for cleanup opportunities

3. **Plan Phase 4.2**:
   - File-based settings per telescope
   - Settings file structure
   - UI changes for settings management

---

## Recommendations

### DO:
? Fix Park bugs FIRST  
? Follow steps sequentially  
? Test after each major change  
? Log values for verification  
? Commit frequently with descriptive messages  
? Keep static facade for backward compatibility  

### DON'T:
? Skip Park bug fixes  
? Rush through steps  
? Make multiple changes without testing  
? Remove static properties (keep for Phase 5)  
? Change queue management yet (Phase 4.3)  

---

## Summary

**Current Status**: Phase 3.2 complete, ready for Phase 4.1  
**Blocking Issue**: Park function bugs must be fixed first  
**First Phase**: Phase 4.1 - Instance-owned state (12-18 hours)  
**Validation**: Plan is sound, prerequisites clear, steps actionable  
**Risk Level**: LOW (Phase 4.1 is the safest phase)  
**Recommendation**: Proceed with Phase 4.1 after fixing Park bugs

**Ready to begin**: YES ?

