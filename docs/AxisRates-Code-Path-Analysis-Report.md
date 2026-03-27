# AxisRates Code Path Analysis Report
**Generated:** 2025-01-XX  
**Purpose:** Comprehensive analysis of AxisRates calculation paths and settings dependencies

---

## Executive Summary

This report documents all code paths involved in calculating and returning AxisRates values for the ASCOM telescope driver. The analysis traces values from JSON settings through initialization, calculation, and final return to ASCOM clients.

**Key Finding:** AxisRates values are derived from `SlewSpeedEight`, which has TWO independent calculation paths that may produce different values depending on initialization order.

---

## 1. ASCOM API Entry Point

### Location
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`  
**Method:** `AxisRates(TelescopeAxis axis)`  
**Lines:** 1307-1324

### Implementation
```csharp
public IAxisRates AxisRates(TelescopeAxis Axis)
{
    var monitorItem = new MonitorEntry
    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, 
      Category = MonitorCategory.Driver, Type = MonitorType.Data, 
      Method = MethodBase.GetCurrentMethod()?.Name, 
      Thread = Thread.CurrentThread.ManagedThreadId, 
      Message = $"   {Axis}" };
    MonitorLog.LogToMonitor(monitorItem);

    switch (Axis)
    {
        case TelescopeAxis.Primary:
            return new AxisRates(TelescopeAxis.Primary);
        case TelescopeAxis.Secondary:
            return new AxisRates(TelescopeAxis.Secondary);
        case TelescopeAxis.Tertiary:
            return new AxisRates(TelescopeAxis.Tertiary);
        default:
            return null;
    }
}
```

**Behavior:** Creates a new `AxisRates` object for each request. Does NOT cache values.

---

## 2. AxisRates Constructor

### Location
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Rates.cs`  
**Class:** `AxisRates`  
**Constructor Lines:** 95-133

### Implementation
```csharp
public AxisRates(TelescopeAxis axis)
{
    // Safety check: Use settings fallback if SlewSpeedEight not yet initialized
    // (e.g., if AxisRates is queried before mount connects)
    var maxSlewRate = SkyServer.SlewSpeedEight > 0
        ? SkyServer.SlewSpeedEight
        : 4.0;

    switch (axis)
    {
        case TelescopeAxis.Primary:
            _mRates = new[] { new Rate(0.0, maxSlewRate) };
            break;
        case TelescopeAxis.Secondary:
            _mRates = new[] { new Rate(0.0, maxSlewRate) };
            break;
        case TelescopeAxis.Tertiary:
            _mRates = new[] { new Rate(0.0, maxSlewRate) };
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
    }
    _pos = -1;
}
```

### Key Points
1. **Retrieves `SkyServer.SlewSpeedEight`** - a static property
2. **Fallback value:** 4.0 (used if `SlewSpeedEight <= 0`)
3. **All three axes use the same rate range:** 0.0 to `maxSlewRate`
4. **No conversion applied** - value used directly from `SlewSpeedEight`

---

## 3. SkyServer.SlewSpeedEight Property

### Location
**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 82

### Implementation
```csharp
public static double SlewSpeedEight => _defaultInstance?.SlewSpeedEight ?? 4.0;
```

### Behavior
- **Static property** - accessible before mount connection
- **Delegates** to the default `MountInstance`
- **Fallback:** Returns 4.0 if no default instance exists
- **Thread-safe:** Read-only access pattern

---

## 4. MountInstance.SlewSpeedEight Property

### Location
**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 474

### Implementation
```csharp
public double SlewSpeedEight => _slewSpeedEight;
```

### Storage
- **Private field:** `_slewSpeedEight` (type: `double`)
- **Read-only** from outside MountInstance
- **Set in two places:** Initial load and runtime recalculation

---

## 5. Two Calculation Paths for SlewSpeedEight

### Path A: Initial Load (MountInstance Constructor Region)

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Method:** Constructor / Initialization region  
**Lines:** 1346-1358

#### Calculation
```csharp
// set the slew speeds, the longest distance is using the higher speed for longer
_slewSpeedOne = Principles.Units.Deg2Rad1((int)_settings.HcSpeed * (15.0 / 3600)); //1x 15"/s
_slewSpeedTwo = _slewSpeedOne * 2; //2x
_slewSpeedThree = _slewSpeedOne * 8; //8x
_slewSpeedFour = _slewSpeedOne * 16; //16x
_slewSpeedFive = _slewSpeedOne * 32; //32x
_slewSpeedSix = _slewSpeedOne * 64; //64x
_slewSpeedSeven = _slewSpeedOne * 600; //600x
_slewSpeedEight = _slewSpeedOne * 800; //800x

var maxSlew = Principles.Units.Deg2Rad1(_settings.MaxSlewRate);
SkyServer.SetSlewRates(maxSlew);
```

#### Dependency Chain (Path A)
```
HcSpeed (from JSON)
  ↓
(int)_settings.HcSpeed  [cast enum to int]
  ↓
× (15.0 / 3600)  [15 arcseconds per second]
  ↓
Principles.Units.Deg2Rad1()  [convert to radians]
  ↓
× 800  [multiply by 800 for speed level 8]
  ↓
_slewSpeedEight
```

#### Example Calculation (Path A)
If `HcSpeed = "8"` (enum value 8):
```
_slewSpeedOne = Deg2Rad1(8 * (15.0 / 3600))
              = Deg2Rad1(8 * 0.004166667)
              = Deg2Rad1(0.033333333)
              = 0.033333333 × (π / 180)
              = 0.0005817764
              
_slewSpeedEight = 0.0005817764 × 800
                = 0.46542112 radians
```

---

### Path B: Runtime Recalculation (SetSlewRates)

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Method:** `SetSlewRates(double maxRate)`  
**Lines:** 1686-1715

#### When Called
1. **During initial load:** After Path A calculations
2. **During settings changes:** When `MaxSlewRate` property is modified

#### Calculation
```csharp
internal static void SetSlewRates(double maxRate)
{
    if (_defaultInstance == null) return;

    _defaultInstance._slewSpeedOne = Math.Round(maxRate * 0.0034, 3);
    _defaultInstance._slewSpeedTwo = Math.Round(maxRate * 0.0068, 3);
    _defaultInstance._slewSpeedThree = Math.Round(maxRate * 0.047, 3);
    _defaultInstance._slewSpeedFour = Math.Round(maxRate * 0.068, 3);
    _defaultInstance._slewSpeedFive = Math.Round(maxRate * 0.2, 3);
    _defaultInstance._slewSpeedSix = Math.Round(maxRate * 0.4, 3);
    _defaultInstance._slewSpeedSeven = Math.Round(maxRate * 0.8, 3);
    _defaultInstance._slewSpeedEight = Math.Round(maxRate * 1.0, 3);

    var monitorItem = new MonitorEntry { ... };
    MonitorLog.LogToMonitor(monitorItem);
}
```

#### Dependency Chain (Path B)
```
MaximumSlewRate (from JSON)
  ↓
_settings.MaxSlewRate
  ↓
Principles.Units.Deg2Rad1()  [convert to radians]
  ↓
SkyServer.SetSlewRates(maxSlew)
  ↓
maxRate × 1.0  [100% of max rate]
  ↓
Math.Round(..., 3)  [round to 3 decimals]
  ↓
_slewSpeedEight
```

#### Example Calculation (Path B)
If `MaximumSlewRate = 8.0` (degrees/second):
```
maxSlew = Deg2Rad1(8.0)
        = 8.0 × (π / 180)
        = 0.139626340159546
        
_slewSpeedEight = Math.Round(0.139626340159546 × 1.0, 3)
                = Math.Round(0.139626340159546, 3)
                = 0.140 radians
```

---

## 6. Critical Observation: Dual Calculation Paths

### The Problem
**SlewSpeedEight is calculated TWICE during initialization:**

1. **First (Path A):** Based on `HcSpeed`, produces ~0.465 radians (for HcSpeed=8)
2. **Then immediately (Path B):** Overwritten by `MaximumSlewRate`, produces ~0.140 radians (for MaxSlewRate=8.0)

### Which Value Wins?
**Path B always overwrites Path A** because:
```csharp
// In MountInstance initialization (lines 1346-1358):
_slewSpeedEight = _slewSpeedOne * 800;        // Path A calculates first
var maxSlew = Principles.Units.Deg2Rad1(_settings.MaxSlewRate);
SkyServer.SetSlewRates(maxSlew);              // Path B immediately overwrites
```

**Result:** The initial Path A calculation is discarded. Only Path B's value is ever returned by AxisRates.

---

## 7. JSON Settings Properties

### Primary Settings File
**File:** `GreenSwamp.Alpaca.Server\appsettings.json`

### Relevant Properties

| Property | Type | Default Value | Used In | Purpose |
|----------|------|---------------|---------|---------|
| `HcSpeed` | string (enum) | `"8"` | Path A (discarded) | Hand controller speed level (1-8) |
| `MaximumSlewRate` | double | `8.0` | Path B (final) | Maximum slew rate in degrees/second |

### Default Values from Template
**File:** `GreenSwamp.Alpaca.Server\appsettings.json` (lines 99, 136)
```json
{
  "HcSpeed": "8",
  "MaximumSlewRate": 8.0
}
```

---

## 8. Settings Model Definition

### Location
**File:** `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`

### Property Definitions
```csharp
// Line 143 - Used in Path A (but overwritten)
public string HcSpeed { get; set; } = null!;

// Line 186 - Used in Path B (final value)
public double MaximumSlewRate { get; set; }
```

### No Validation Attributes
Neither property has range validation or JsonSchema attributes that would enforce consistency.

---

## 9. Settings Loading Path

### Entry Point
**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs`  
**Method:** `ApplySettings(Settings.Models.SkySettings settings)`  
**Lines:** 1807-1900+

### Loading Sequence
```csharp
// Line 1829-1830 - HcSpeed loaded as enum
if (Enum.TryParse<SlewSpeed>(settings.HcSpeed, true, out var hcSpd))
    _hcSpeed = hcSpd;

// Line 1880 - MaximumSlewRate loaded as double
_maxSlewRate = settings.MaximumSlewRate;
```

### Enum Definition
**File:** `GreenSwamp.Alpaca.MountControl\Enums.cs`  
**Lines:** 40-51
```csharp
[Flags]
public enum SlewSpeed
{
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8
}
```

**Note:** `[Flags]` attribute is present but inappropriate (these are discrete levels, not bitwise flags).

---

## 10. Runtime Property Change Path

### MaxSlewRate Property Setter
**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs`  
**Lines:** 922-938

```csharp
public double MaxSlewRate
{
    get => _maxSlewRate;
    set
    {
        if (Math.Abs(_maxSlewRate - value) > 0.001)
        {
            _maxSlewRate = value;
            OnPropertyChanged();

            // SIDE EFFECT: Update slew rates
            if (SkyServer.IsMountRunning)
            {
                SkyServer.SetSlewRates(value);
            }
        }
    }
}
```

### Side Effect Behavior
When `MaxSlewRate` is changed at runtime:
1. Value stored in `_maxSlewRate`
2. Property change notification fired
3. **If mount is running:** `SetSlewRates()` called immediately
4. All eight slew speeds recalculated
5. **AxisRates calls will see new values immediately** (no cache)

---

## 11. Unit Conversion Functions

### Deg2Rad1 Function
**File:** `GreenSwamp.Alpaca.Principles\Units.cs`  
**Lines:** 119-124

```csharp
public static double Deg2Rad1(double degrees)
{
    const double a = Math.PI / 180.0;
    var b = degrees * a;
    return b;
}
```

**Precision:** Uses `Math.PI` constant (15-17 significant digits)  
**Formula:** `degrees × (π / 180)`

---

## 12. Complete Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     JSON Settings File                           │
│                   (appsettings.json)                             │
│                                                                   │
│  HcSpeed: "8"                MaximumSlewRate: 8.0               │
└────────┬─────────────────────────────────┬────────────────────────┘
         │                                 │
         │ Loaded by                       │ Loaded by
         │ ApplySettings()                 │ ApplySettings()
         ↓                                 ↓
┌────────────────────┐          ┌────────────────────┐
│ _hcSpeed (enum)    │          │ _maxSlewRate       │
│   = SlewSpeed.Eight│          │   = 8.0            │
└────────┬───────────┘          └─────────┬──────────┘
         │                                │
         │ Used in                        │ Used in
         │ MountInstance init             │ MountInstance init
         ↓                                ↓
┌────────────────────────────────────────────────────┐
│           MountInstance Initialization              │
│                                                     │
│  [Path A - OVERWRITTEN]                            │
│  _slewSpeedOne = Deg2Rad1((int)HcSpeed * 15"/3600)│
│    = Deg2Rad1(8 × 0.00416667)                     │
│    = 0.000581776 rad                               │
│                                                     │
│  _slewSpeedEight = _slewSpeedOne × 800             │
│    = 0.465421 rad                                  │
│                                                     │
│  [Path B - FINAL VALUE]                            │
│  maxSlew = Deg2Rad1(MaximumSlewRate)               │
│    = Deg2Rad1(8.0)                                 │
│    = 0.139626 rad                                  │
│                                                     │
│  SetSlewRates(maxSlew)                             │
│    _slewSpeedEight = Round(0.139626 × 1.0, 3)     │
│    = 0.140 rad       ← FINAL VALUE                 │
└────────────────────────┬───────────────────────────┘
                         │
                         │ Exposed via
                         ↓
            ┌────────────────────────────┐
            │  SkyServer.SlewSpeedEight  │
            │  (static property)          │
            │  Returns: 0.140 rad         │
            └────────────┬───────────────┘
                         │
                         │ Read by
                         ↓
            ┌────────────────────────────┐
            │   AxisRates Constructor     │
            │                             │
            │  maxSlewRate =              │
            │    SkyServer.SlewSpeedEight │
            │    > 0 ? value : 4.0        │
            │                             │
            │  Creates Rate(0.0, 0.140)   │
            └────────────┬───────────────┘
                         │
                         │ Returned to
                         ↓
            ┌────────────────────────────┐
            │   ASCOM Client              │
            │   AxisRates Property        │
            │                             │
            │   Min: 0.0 rad/sec          │
            │   Max: 0.140 rad/sec        │
            └─────────────────────────────┘
```

---

## 13. Unit Conversions and Expected Values

### Default Configuration Analysis
Given default values: `HcSpeed = "8"`, `MaximumSlewRate = 8.0`

#### Path A Calculation (Discarded)
```
Input: HcSpeed = 8
Step 1: 8 × (15.0 / 3600) = 8 × 0.00416667 = 0.0333333 degrees/sec
Step 2: Deg2Rad1(0.0333333) = 0.0333333 × 0.0174533 = 0.000581776 rad/sec
Step 3: 0.000581776 × 800 = 0.465421 rad/sec
```

#### Path B Calculation (Final)
```
Input: MaximumSlewRate = 8.0 degrees/sec
Step 1: Deg2Rad1(8.0) = 8.0 × 0.0174533 = 0.139626 rad/sec
Step 2: 0.139626 × 1.0 = 0.139626 rad/sec
Step 3: Math.Round(0.139626, 3) = 0.140 rad/sec
```

**RESULT:** `AxisRates` returns maximum rate of **0.140 rad/sec**

### Conversion to Degrees/Second
```
0.140 rad/sec ÷ (π/180) = 0.140 ÷ 0.0174533 = 8.018 degrees/sec
```
(Rounding introduces slight increase from 8.0)

### Conversion to Arcseconds/Second
```
8.018 degrees/sec × 3600 arcsec/degree = 28,865 arcsec/sec
```

---

## 14. Potential Issues Identified

### Issue 1: Dual Calculation Paths
**Symptom:** HcSpeed calculation is performed but immediately discarded  
**Impact:** Confusing code; Path A appears to set values but has no effect  
**Code Location:** MountInstance initialization (lines 1346-1358)

### Issue 2: Inconsistent Units in Path A
**Observation:** Path A multiplies by 800 but Path B multiplies by 1.0  
**Question:** Why does Path A use 800x multiplier?  
**Possible Explanation:** Path A may be designed for internal step rates, not ASCOM rates

### Issue 3: Enum Casting Without Validation
**Code Location:** Line 1347 - `(int)_settings.HcSpeed`  
**Risk:** If HcSpeed enum doesn't parse correctly, cast to int could produce 0

### Issue 4: Fallback Value May Not Match
**Code Location:** Rates.cs line 107 - `SlewSpeedEight > 0 ? SlewSpeedEight : 4.0`  
**Issue:** Fallback is `4.0` radians/sec = 229 degrees/sec (seems too high)  
**Actual:** Should probably be `Deg2Rad1(4.0)` = 0.0698 rad/sec

### Issue 5: No Range Validation
**Observation:** Neither `HcSpeed` nor `MaximumSlewRate` have validation attributes  
**Risk:** Invalid values could propagate through calculations

### Issue 6: Static Property Access Pattern
**Code Location:** Rates.cs accesses `SkyServer.SlewSpeedEight` (static)  
**Issue:** AxisRates doesn't reference specific mount instance  
**Impact:** Multi-device scenarios may share rates incorrectly

---

## 15. Related Settings Not Directly Used in AxisRates

The following settings affect slew behavior but do NOT directly affect AxisRates values:

| Setting | Purpose | File |
|---------|---------|------|
| `SlewSpeedOne` through `SlewSpeedSeven` | Internal slew speed levels | Calculated, not from JSON |
| `GotoPrecision` | When to stop slew | appsettings.json line 137 |
| `NumMoveAxis` | Number of axes | appsettings.json line 139 |
| `FullCurrent` | Motor current setting | appsettings.json line 138 |

---

## 16. Call Stack Summary

### Normal Operation Flow
```
1. ASCOM Client calls Telescope.AxisRates(axis)
   ↓
2. Telescope.AxisRates() creates new AxisRates(axis)
   ↓
3. AxisRates constructor reads SkyServer.SlewSpeedEight
   ↓
4. SkyServer.SlewSpeedEight returns _defaultInstance?.SlewSpeedEight ?? 4.0
   ↓
5. MountInstance._slewSpeedEight field (set during initialization)
   ↓
6. Value comes from SetSlewRates(MaximumSlewRate) [Path B]
```

### Initialization Flow
```
1. Application startup
   ↓
2. Settings loaded from appsettings.json
   ↓
3. SkySettingsInstance.ApplySettings() called
   ↓
4. MountInstance created with settings
   ↓
5. MountInstance initialization calculates slew speeds (Path A)
   ↓
6. MountInstance initialization calls SetSlewRates() (Path B overwrites)
   ↓
7. Values ready for AxisRates queries
```

---

## 17. Files Involved

### Core Implementation Files
1. `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs` - ASCOM API
2. `GreenSwamp.Alpaca.Server\TelescopeDriver\Rates.cs` - AxisRates class
3. `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs` - Static access layer
4. `GreenSwamp.Alpaca.MountControl\MountInstance.cs` - Instance calculations
5. `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs` - Settings management
6. `GreenSwamp.Alpaca.Principles\Units.cs` - Unit conversions

### Settings Files
7. `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs` - Settings model
8. `GreenSwamp.Alpaca.Server\appsettings.json` - Default configuration
9. `GreenSwamp.Alpaca.MountControl\Enums.cs` - Enum definitions

---

## 18. Test Scenarios

### To Verify Current Behavior

#### Test 1: Default Configuration
- **Settings:** `HcSpeed = "8"`, `MaximumSlewRate = 8.0`
- **Expected AxisRates.Maximum:** 0.140 rad/sec
- **Method:** Call AxisRates after mount connected

#### Test 2: Change MaximumSlewRate at Runtime
- **Action:** Set `MaximumSlewRate = 4.0` while mount running
- **Expected Result:** AxisRates immediately returns 0.070 rad/sec
- **Verifies:** Runtime recalculation works

#### Test 3: Query Before Mount Connected
- **Action:** Call AxisRates before any mount initialization
- **Expected Result:** Falls back to 4.0 rad/sec
- **Verifies:** Fallback mechanism (may reveal fallback bug)

#### Test 4: Change HcSpeed at Runtime
- **Action:** Set `HcSpeed = 4` while mount running
- **Expected Result:** NO CHANGE to AxisRates (Path A not re-executed)
- **Verifies:** HcSpeed doesn't affect AxisRates after initialization

---

## 19. Questions for Investigation

1. **Why two calculation paths?**
   - Is Path A intended for different purpose (internal motor commands)?
   - Should Path A be removed?

2. **Is MaximumSlewRate the correct source?**
   - Should AxisRates reflect physical mount capabilities?
   - Or should it reflect user-configured limits?

3. **Is the 800x multiplier in Path A intentional?**
   - Path A: HcSpeed-based calculation × 800
   - Path B: MaximumSlewRate × 1.0
   - These produce vastly different results (0.465 vs 0.140)

4. **Should HcSpeed affect AxisRates?**
   - Currently only affects initial calculation (then overwritten)
   - Is this a bug or intentional?

5. **Is the fallback value correct?**
   - Current: 4.0 rad/sec (229 deg/sec - very fast!)
   - Should be: 0.0698 rad/sec (4 deg/sec)?

6. **Multi-device scenario handling?**
   - Static SkyServer.SlewSpeedEight shared across devices
   - Each device may have different MaximumSlewRate
   - Is this a problem?

---

## 20. Recommendations for Further Analysis

### Code Review Focus Areas
1. Verify Path A is truly unnecessary
2. Confirm fallback value units and magnitude
3. Review multi-device rate sharing implications
4. Check if AxisRates should be cached

### Testing Priorities
1. Run Test Scenarios 1-4 above
2. Test multi-device configuration
3. Test settings file migration scenarios
4. Validate with actual mount hardware

### Documentation Needed
1. Clarify relationship between HcSpeed and MaximumSlewRate
2. Document units expected by ASCOM clients
3. Explain Path A vs Path B design decisions
4. Define valid ranges for rate settings

---

## Appendix A: Key Formulas

### Path A (Discarded)
```
_slewSpeedEight = Deg2Rad1((int)HcSpeed × (15.0 / 3600)) × 800
```

### Path B (Final)
```
_slewSpeedEight = Math.Round(Deg2Rad1(MaximumSlewRate) × 1.0, 3)
```

### Deg2Rad1 Conversion
```
Deg2Rad1(degrees) = degrees × (π / 180)
                  = degrees × 0.0174532925199433
```

### Rate Object Created
```
Rate(minimum: 0.0, maximum: _slewSpeedEight)
```
Units: radians per second

---

## Appendix B: Default Values Table

| Setting | JSON Value | Parsed As | Unit | Used In Path |
|---------|------------|-----------|------|--------------|
| HcSpeed | "8" | SlewSpeed.Eight (enum 8) | Dimensionless | Path A only |
| MaximumSlewRate | 8.0 | 8.0 (double) | degrees/second | Path B (final) |
| (calculated) _slewSpeedOne | N/A | 0.000582 | radians/second | Path A |
| (calculated) _slewSpeedEight (Path A) | N/A | 0.465421 | radians/second | OVERWRITTEN |
| (calculated) _slewSpeedEight (Path B) | N/A | 0.140 | radians/second | RETURNED |

---

## Appendix C: ASCOM Units Reference

According to ASCOM Platform documentation:
- **AxisRates.Minimum/Maximum:** Rate in units of axis degrees/second
- **MoveAxis rate parameter:** Rate in units of axis degrees/second

**Critical Note:** The values returned by AxisRates should be in **degrees/second**, not radians/second!

### Current Implementation
- Returns: `0.140 rad/sec` 
- ASCOM expects: `8.018 deg/sec`
- **POTENTIAL BUG:** Units mismatch! AxisRates may be returning radians when degrees expected.

---

## End of Report

**Report Status:** FACTUAL ANALYSIS COMPLETE  
**Next Steps:** Code review and testing per Section 19 & 20  
**Version:** 1.0  
**Last Updated:** 2025-01-XX
