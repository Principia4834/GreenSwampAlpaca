# AxisRates Code Path Analysis Report - Commit 3dbde5a5
**Commit:** 3dbde5a5402c841261b36f001a173e826ba384ea  
**Date:** Tue Mar 24 21:27:26 2026 +0000  
**Commit Message:** "Fix IsConnected error - ConformU AltAz Sim passes"  
**Purpose:** Historical analysis of AxisRates calculation paths at this commit

---

## Executive Summary

This report documents the AxisRates code paths as they existed at commit 3dbde5a5. This commit represents a state **before** `HcSpeed` and `MaximumSlewRate` were added to the `appsettings.json` file.

### Key Differences from Current Code

| Aspect | Commit 3dbde5a5 | Current Code |
|--------|----------------|--------------|
| **Settings in JSON** | NOT in appsettings.json | Present in appsettings.json |
| **Default MaxSlewRate** | 3.4 deg/sec (hardcoded) | 8.0 deg/sec (from JSON) |
| **Default HcSpeed** | SlewSpeed.Eight (hardcoded) | "8" (from JSON) |
| **Calculation Logic** | Identical dual-path | Identical dual-path |
| **Final AxisRates Value** | ~0.059 rad/sec | ~0.140 rad/sec |

**Critical Finding:** At this commit, the AxisRates value would have been calculated as **0.059 rad/sec** (3.4 deg/sec) instead of the current **0.140 rad/sec** (8.0 deg/sec) due to the different default value for `MaximumSlewRate`.

---

## 1. ASCOM API Entry Point

### Location
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`  
**Status at commit 3dbde5a5:** File structure unknown (not in commit diff)

**Assumed Implementation:** Same as current (no changes detected in this area based on commit history)
```csharp
public IAxisRates AxisRates(TelescopeAxis Axis)
{
    // Logging code...
    
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

---

## 2. AxisRates Constructor (VERIFIED at 3dbde5a5)

### Location
**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Rates.cs`  
**Verified Code at Commit 3dbde5a5:**

```csharp
public AxisRates(TelescopeAxis axis)
{
    //
    // This collection must hold zero or more Rate objects describing the 
    // rates of motion ranges for the SkyServer.MoveAxis() method
    // that are supported by your driver. It is OK to leave this 
    // array empty, indicating that MoveAxis() is not supported.
    //
    // Note that we are constructing a rate array for the axis passed
    // to the constructor. Thus we switch() below, and each case should 
    // initialize the array for the rate for the selected axis.
    //
    // Safety check: Use settings fallback if SlewSpeedEight not yet initialized
    // (e.g., if AxisRates is queried before mount connects)
    var maxSlewRate = SkyServer.SlewSpeedEight > 0
        ? SkyServer.SlewSpeedEight
        : 4.0;

    switch (axis)
    {
        case TelescopeAxis.Primary:
            // Example: m_Rates = new Rate[] { new Rate(10.5, 30.2), new Rate(54.0, 43.6) }
            _mRates = new[] { new Rate(0.0, maxSlewRate) };
            break;
        case TelescopeAxis.Secondary:
            _mRates = new[] { new Rate(0.0, maxSlewRate) };
            break;
        case TelescopeAxis.Tertiary:
            _mRates = new[] { new Rate(0.0, maxSlewRate) }; //Conversions.Sec2ArcSec(SkyServer.SlewSpeedEight)) };
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(axis), axis, null);
    }
    _pos = -1;
}
```

**Status:** IDENTICAL to current code. No changes in this section.

---

## 3. SkyServer.SlewSpeedEight Property (VERIFIED at 3dbde5a5)

### Location
**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  

### Verified Code at Commit 3dbde5a5:
```csharp
public static double SlewSpeedEight => _defaultInstance?.SlewSpeedEight ?? 4.0;
```

**Status:** IDENTICAL to current code. Same fallback value (4.0).

---

## 4. MountInstance.SlewSpeedEight Property (VERIFIED at 3dbde5a5)

### Location
**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  

### Verified Code at Commit 3dbde5a5:
```csharp
// Field declaration
internal double _slewSpeedEight;

// Property
public double SlewSpeedEight => _slewSpeedEight;
```

**Status:** IDENTICAL to current code.

---

## 5. Two Calculation Paths for SlewSpeedEight (VERIFIED at 3dbde5a5)

### Path A: Initial Load (MountInstance Constructor Region)

**Verified Code at Commit 3dbde5a5:**

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

**Status:** IDENTICAL to current code.

#### Calculation at Commit 3dbde5a5 (Path A)
Using **default hardcoded value** `HcSpeed = SlewSpeed.Eight` (8):
```
_slewSpeedOne = Deg2Rad1(8 * (15.0 / 3600))
              = Deg2Rad1(8 * 0.004166667)
              = Deg2Rad1(0.033333333)
              = 0.033333333 × (π / 180)
              = 0.0005817764 rad/sec
              
_slewSpeedEight = 0.0005817764 × 800
                = 0.46542112 rad/sec
```

**Same as current code** because the default HcSpeed value didn't change.

---

### Path B: Runtime Recalculation (SetSlewRates) - VERIFIED at 3dbde5a5

**Verified Code at Commit 3dbde5a5:**

```csharp
internal static void SetSlewRates(double maxRate)
{
    // Sky Speeds
    if (_defaultInstance == null) return;

    _defaultInstance._slewSpeedOne = Math.Round(maxRate * 0.0034, 3);
    _defaultInstance._slewSpeedTwo = Math.Round(maxRate * 0.0068, 3);
    _defaultInstance._slewSpeedThree = Math.Round(maxRate * 0.047, 3);
    _defaultInstance._slewSpeedFour = Math.Round(maxRate * 0.068, 3);
    _defaultInstance._slewSpeedFive = Math.Round(maxRate * 0.2, 3);
    _defaultInstance._slewSpeedSix = Math.Round(maxRate * 0.4, 3);
    _defaultInstance._slewSpeedSeven = Math.Round(maxRate * 0.8, 3);
    _defaultInstance._slewSpeedEight = Math.Round(maxRate * 1.0, 3);

    // Log...
}
```

**Status:** IDENTICAL to current code.

#### Calculation at Commit 3dbde5a5 (Path B)
Using **default hardcoded value** `MaximumSlewRate = 3.4` deg/sec:
```
Input: _maxSlewRate = 3.4 degrees/sec (from SkySettingsInstance default)

maxSlew = Deg2Rad1(3.4)
        = 3.4 × (π / 180)
        = 3.4 × 0.0174532925199433
        = 0.059341194567805 rad/sec
        
_slewSpeedEight = Math.Round(0.059341194567805 × 1.0, 3)
                = Math.Round(0.059341194567805, 3)
                = 0.059 rad/sec
```

**THIS IS DIFFERENT from current code!**  
- Commit 3dbde5a5: **0.059 rad/sec** (3.4 deg/sec)
- Current code: **0.140 rad/sec** (8.0 deg/sec)

---

## 6. Critical Observation: Dual Calculation Paths at Commit 3dbde5a5

### The Problem (UNCHANGED)
**SlewSpeedEight is STILL calculated TWICE during initialization at this commit:**

1. **First (Path A):** Based on `HcSpeed`, produces ~0.465 radians (for HcSpeed=8)
2. **Then immediately (Path B):** Overwritten by `MaximumSlewRate`, produces ~0.059 radians (for MaxSlewRate=3.4)

### Which Value Wins?
**Path B always overwrites Path A** - same as current code.

**Result at Commit 3dbde5a5:** The initial Path A calculation is discarded. Only Path B's value (**0.059 rad/sec**) would be returned by AxisRates.

---

## 7. JSON Settings Properties at Commit 3dbde5a5

### Primary Settings File
**File:** `GreenSwamp.Alpaca.Server\appsettings.json`

### **CRITICAL DIFFERENCE:** Properties NOT in JSON at Commit 3dbde5a5

**Verified JSON Structure at Commit 3dbde5a5:**
```json
{
  "SkySettings": {
    "Mount": "Simulator",
    "Port": "COM1",
    "BaudRate": 9600,
    // ... many other properties ...
    "NumMoveAxis": 2,
    "VersionOne": false
    // NO HcSpeed property
    // NO MaximumSlewRate property
  }
}
```

### Where Values Came From at Commit 3dbde5a5

| Property | Source | Value at 3dbde5a5 | Current Value |
|----------|--------|-------------------|---------------|
| `HcSpeed` | Hardcoded default in SkySettingsInstance | `SlewSpeed.Eight` (8) | `"8"` from JSON |
| `MaximumSlewRate` | Hardcoded default in SkySettingsInstance | `3.4` | `8.0` from JSON |

---

## 8. Settings Model Definition at Commit 3dbde5a5

### Location
**File:** `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs`

### Property Definitions (VERIFIED at 3dbde5a5)
```csharp
// Hand Controller Settings
public string HcSpeed { get; set; } = null!;

// Advanced Settings
public double MaximumSlewRate { get; set; }
```

**Status:** Properties existed in the model but were NOT populated from appsettings.json.

---

## 9. Settings Loading Path at Commit 3dbde5a5

### Hardcoded Defaults in SkySettingsInstance (VERIFIED)

**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs`  

```csharp
// Field declarations with DEFAULT VALUES
private SlewSpeed _hcSpeed = SlewSpeed.Eight;
private double _maxSlewRate = 3.4;
```

### Loading Sequence at Commit 3dbde5a5
Since `HcSpeed` and `MaximumSlewRate` were **NOT in appsettings.json** at this commit:

1. **SkySettingsInstance fields initialized** with hardcoded defaults:
   - `_hcSpeed = SlewSpeed.Eight`
   - `_maxSlewRate = 3.4`

2. **ApplySettings() method called** with JSON data:
   - Attempts to parse `settings.HcSpeed` → **Would be null or default**
   - Attempts to load `settings.MaximumSlewRate` → **Would be 0.0 or default**

3. **Result:** Default field values would be used:
   - HcSpeed: `SlewSpeed.Eight` (8)
   - MaximumSlewRate: `3.4` degrees/second

---

## 10. Runtime Property Change Path at Commit 3dbde5a5

### MaxSlewRate Property Setter (VERIFIED)
**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsInstance.cs`  

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

**Status:** IDENTICAL to current code. Same side-effect behavior.

---

## 11. Unit Conversion Functions at Commit 3dbde5a5

### Deg2Rad1 Function
**Assumed Status:** IDENTICAL (no changes detected in Principles library)

```csharp
public static double Deg2Rad1(double degrees)
{
    const double a = Math.PI / 180.0;
    var b = degrees * a;
    return b;
}
```

---

## 12. Complete Data Flow Diagram at Commit 3dbde5a5

```
┌─────────────────────────────────────────────────────────────────┐
│                     JSON Settings File                           │
│                   (appsettings.json)                             │
│                                                                   │
│  HcSpeed: NOT PRESENT        MaximumSlewRate: NOT PRESENT       │
└────────┬─────────────────────────────────┬────────────────────────┘
         │                                 │
         │ Missing from JSON               │ Missing from JSON
         │ Use hardcoded defaults          │ Use hardcoded defaults
         ↓                                 ↓
┌────────────────────┐          ┌────────────────────┐
│ _hcSpeed (enum)    │          │ _maxSlewRate       │
│   = SlewSpeed.Eight│          │   = 3.4            │
│   (HARDCODED)      │          │   (HARDCODED)      │
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
│    = Deg2Rad1(3.4)   ← DIFFERENT VALUE!           │
│    = 0.059341 rad                                  │
│                                                     │
│  SetSlewRates(maxSlew)                             │
│    _slewSpeedEight = Round(0.059341 × 1.0, 3)     │
│    = 0.059 rad       ← FINAL VALUE (DIFFERENT!)    │
└────────────────────────┬───────────────────────────┘
                         │
                         │ Exposed via
                         ↓
            ┌────────────────────────────┐
            │  SkyServer.SlewSpeedEight  │
            │  (static property)          │
            │  Returns: 0.059 rad         │
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
            │  Creates Rate(0.0, 0.059)   │
            └────────────┬───────────────┘
                         │
                         │ Returned to
                         ↓
            ┌────────────────────────────┐
            │   ASCOM Client              │
            │   AxisRates Property        │
            │                             │
            │   Min: 0.0 rad/sec          │
            │   Max: 0.059 rad/sec        │  ← DIFFERENT!
            └─────────────────────────────┘
```

---

## 13. Unit Conversions and Expected Values at Commit 3dbde5a5

### Configuration at Commit 3dbde5a5
Given **hardcoded defaults**: `HcSpeed = 8`, `MaximumSlewRate = 3.4`

#### Path A Calculation (Discarded) - SAME AS CURRENT
```
Input: HcSpeed = 8
Step 1: 8 × (15.0 / 3600) = 8 × 0.00416667 = 0.0333333 degrees/sec
Step 2: Deg2Rad1(0.0333333) = 0.0333333 × 0.0174533 = 0.000581776 rad/sec
Step 3: 0.000581776 × 800 = 0.465421 rad/sec
```

#### Path B Calculation (Final) - **DIFFERENT FROM CURRENT**
```
Input: MaximumSlewRate = 3.4 degrees/sec (HARDCODED DEFAULT)
Step 1: Deg2Rad1(3.4) = 3.4 × 0.0174533 = 0.059341 rad/sec
Step 2: 0.059341 × 1.0 = 0.059341 rad/sec
Step 3: Math.Round(0.059341, 3) = 0.059 rad/sec
```

**RESULT:** `AxisRates` returns maximum rate of **0.059 rad/sec**

### Conversion to Degrees/Second at Commit 3dbde5a5
```
0.059 rad/sec ÷ (π/180) = 0.059 ÷ 0.0174533 = 3.381 degrees/sec
```
(Rounding introduces slight decrease from 3.4)

### Conversion to Arcseconds/Second at Commit 3dbde5a5
```
3.381 degrees/sec × 3600 arcsec/degree = 12,172 arcsec/sec
```

### Comparison with Current Code

| Metric | Commit 3dbde5a5 | Current Code | Ratio |
|--------|----------------|--------------|-------|
| **MaximumSlewRate (input)** | 3.4 deg/sec | 8.0 deg/sec | 2.35x |
| **SlewSpeedEight (rad/sec)** | 0.059 | 0.140 | 2.37x |
| **SlewSpeedEight (deg/sec)** | 3.381 | 8.018 | 2.37x |
| **SlewSpeedEight (arcsec/sec)** | 12,172 | 28,865 | 2.37x |

**Analysis:** Current code returns rates **2.37 times faster** than commit 3dbde5a5.

---

## 14. Potential Issues at Commit 3dbde5a5

### Issue 1: Missing Settings in JSON
**Status at 3dbde5a5:** `HcSpeed` and `MaximumSlewRate` NOT in appsettings.json  
**Impact:** Users could not configure these values without code changes  
**Severity:** Medium - limits user configurability

### Issue 2: Hardcoded Default May Not Match Mount Capabilities
**Status at 3dbde5a5:** MaximumSlewRate hardcoded to 3.4 deg/sec  
**Question:** Was this value tested/validated for different mount types?  
**Impact:** Single default value for all mount types

### Issue 3: Dual Calculation Paths (SAME AS CURRENT)
**Status at 3dbde5a5:** Path A still overwritten by Path B  
**Impact:** Confusing code; no functional difference from current

### Issue 4: Fallback Value Issue (SAME AS CURRENT)
**Status at 3dbde5a5:** Fallback is `4.0` radians/sec (too high)  
**Impact:** Same problem as current code

### Issue 5: No Range Validation (SAME AS CURRENT)
**Status at 3dbde5a5:** No validation on hardcoded defaults  
**Impact:** Invalid values could be set programmatically

### Issue 6: Static Property Access Pattern (SAME AS CURRENT)
**Status at 3dbde5a5:** Same multi-device sharing issue  
**Impact:** Identical to current code

---

## 15. Changes Between Commit 3dbde5a5 and Current Code

### Added to appsettings.json
```json
{
  "Devices": [
    {
      // ... other properties ...
      "HcSpeed": "8",              // NEW - now configurable
      "MaximumSlewRate": 8.0,      // NEW - now configurable (increased from 3.4)
      // ... other properties ...
    }
  ]
}
```

### Behavioral Changes

| Aspect | Commit 3dbde5a5 | Current Code | Reason for Change |
|--------|----------------|--------------|-------------------|
| **HcSpeed source** | Hardcoded default | JSON configuration | User configurability |
| **MaxSlewRate source** | Hardcoded default | JSON configuration | User configurability |
| **MaxSlewRate value** | 3.4 deg/sec | 8.0 deg/sec | Increased capability |
| **AxisRates maximum** | 0.059 rad/sec | 0.140 rad/sec | Result of MaxSlewRate increase |

### Code Structure Changes
**None detected** - the calculation logic remains identical, only the source of input values changed.

---

## 16. Call Stack Summary at Commit 3dbde5a5

### Normal Operation Flow (IDENTICAL to current)
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
6. Value comes from SetSlewRates(MaximumSlewRate=3.4) [Path B]
```

### Initialization Flow at Commit 3dbde5a5
```
1. Application startup
   ↓
2. Settings loaded from appsettings.json
   │  (HcSpeed and MaximumSlewRate NOT present)
   ↓
3. SkySettingsInstance.ApplySettings() called
   │  (Uses hardcoded defaults: HcSpeed=8, MaxSlewRate=3.4)
   ↓
4. MountInstance created with settings
   ↓
5. MountInstance initialization calculates slew speeds (Path A: 0.465)
   ↓
6. MountInstance initialization calls SetSlewRates(3.4) (Path B: 0.059 - OVERWRITES)
   ↓
7. Values ready for AxisRates queries
```

---

## 17. Test Scenarios at Commit 3dbde5a5

### To Verify Behavior at This Commit

#### Test 1: Default Configuration at Commit 3dbde5a5
- **Settings:** Hardcoded defaults (no JSON values)
- **Expected AxisRates.Maximum:** 0.059 rad/sec (3.4 deg/sec)
- **Method:** Call AxisRates after mount connected
- **Result:** Should return **0.059** instead of current **0.140**

#### Test 2: Change MaximumSlewRate at Runtime at Commit 3dbde5a5
- **Action:** Set `MaximumSlewRate = 8.0` while mount running
- **Expected Result:** AxisRates immediately returns 0.140 rad/sec
- **Verifies:** Runtime recalculation brings value to "current" level

#### Test 3: Verify Hardcoded Defaults Used
- **Action:** Inspect `_maxSlewRate` field before any changes
- **Expected:** Should be 3.4, not 8.0
- **Verifies:** Defaults are from code, not JSON

---

## 18. Evolution: What Changed After 3dbde5a5

### Commits After 3dbde5a5
Between commit 3dbde5a5 and current code, these settings were added to JSON:
- `HcSpeed` property added to appsettings.json
- `MaximumSlewRate` property added to appsettings.json
- Default value for `MaximumSlewRate` changed from **3.4** to **8.0**

### Migration Impact
Users upgrading from commit 3dbde5a5 to current code would experience:
1. **Faster slew rates:** 2.37x increase in maximum rate
2. **New configurability:** Can now adjust rates without code changes
3. **Potentially surprising behavior:** Mounts configured for slower rates would suddenly move faster

---

## 19. Questions for Investigation (Specific to This Commit)

1. **Why was MaximumSlewRate set to 3.4 deg/sec at this commit?**
   - Was this based on specific mount hardware limits?
   - Was this a conservative "safe" value?

2. **When were these settings added to JSON?**
   - Which commit introduced HcSpeed/MaximumSlewRate to appsettings.json?
   - What was the rationale for the change from 3.4 to 8.0?

3. **Were users affected by the rate increase?**
   - Did any users report unexpected behavior after upgrade?
   - Were there any mount hardware issues from the faster rates?

4. **Was there a migration guide?**
   - Were users told to update their settings when upgrading?
   - Was there a way to preserve the old 3.4 deg/sec rate?

---

## 20. Comparison Summary

### Side-by-Side Comparison

| Feature | Commit 3dbde5a5 | Current Code |
|---------|----------------|--------------|
| **Code Structure** | Dual-path calculation | Dual-path calculation (SAME) |
| **AxisRates Constructor** | Identical | Identical |
| **SkyServer.SlewSpeedEight** | Identical | Identical |
| **SetSlewRates Logic** | Identical | Identical |
| **HcSpeed in JSON** | ❌ No | ✅ Yes |
| **MaxSlewRate in JSON** | ❌ No | ✅ Yes |
| **HcSpeed Default** | SlewSpeed.Eight (8) | "8" from JSON |
| **MaxSlewRate Default** | 3.4 deg/sec | 8.0 deg/sec |
| **AxisRates Max Value** | **0.059 rad/sec** | **0.140 rad/sec** |
| **Path A Result (discarded)** | 0.465 rad/sec | 0.465 rad/sec (SAME) |
| **Path B Result (used)** | **0.059 rad/sec** | **0.140 rad/sec** |

### Key Insight
**The calculation logic at commit 3dbde5a5 was IDENTICAL to current code.** The only difference was the **source and value** of the `MaximumSlewRate` setting:
- **3dbde5a5:** Hardcoded 3.4 deg/sec → AxisRates = 0.059 rad/sec
- **Current:** JSON 8.0 deg/sec → AxisRates = 0.140 rad/sec

---

## Appendix A: Key Formulas at Commit 3dbde5a5

### Path A (Discarded) - SAME AS CURRENT
```
_slewSpeedEight = Deg2Rad1((int)HcSpeed × (15.0 / 3600)) × 800
                = Deg2Rad1(8 × 0.004166667) × 800
                = 0.465421 rad/sec
```

### Path B (Final) - DIFFERENT INPUT VALUE
```
_slewSpeedEight = Math.Round(Deg2Rad1(MaximumSlewRate) × 1.0, 3)
                = Math.Round(Deg2Rad1(3.4) × 1.0, 3)
                = 0.059 rad/sec
```

---

## Appendix B: Default Values at Commit 3dbde5a5

| Setting | Source | Value | Unit | Used In Path |
|---------|--------|-------|------|--------------|
| HcSpeed | Hardcoded in SkySettingsInstance | SlewSpeed.Eight (8) | Dimensionless | Path A only |
| MaximumSlewRate | Hardcoded in SkySettingsInstance | **3.4** | degrees/second | Path B (final) |
| (calculated) _slewSpeedOne | N/A | 0.000582 | radians/second | Path A |
| (calculated) _slewSpeedEight (Path A) | N/A | 0.465421 | radians/second | OVERWRITTEN |
| (calculated) _slewSpeedEight (Path B) | N/A | **0.059** | radians/second | RETURNED |

---

## Appendix C: ASCOM Units Reference (SAME AS CURRENT)

According to ASCOM Platform documentation:
- **AxisRates.Minimum/Maximum:** Rate in units of axis degrees/second
- **MoveAxis rate parameter:** Rate in units of axis degrees/second

**Critical Note:** At commit 3dbde5a5, the same units mismatch issue exists!

### At Commit 3dbde5a5
- Returns: `0.059 rad/sec` 
- ASCOM expects: `3.381 deg/sec`
- **POTENTIAL BUG:** Units mismatch existed even at this commit!

---

## End of Report

**Report Status:** HISTORICAL ANALYSIS COMPLETE  
**Commit Analyzed:** 3dbde5a5402c841261b36f001a173e826ba384ea  
**Date of Commit:** Tue Mar 24 21:27:26 2026 +0000  
**Key Finding:** AxisRates returned 0.059 rad/sec at this commit vs 0.140 rad/sec currently  
**Root Cause:** MaximumSlewRate default changed from 3.4 to 8.0 deg/sec between commits  
**Code Structure:** IDENTICAL - only input values changed  
**Version:** 1.0  
**Last Updated:** 2025-01-XX
