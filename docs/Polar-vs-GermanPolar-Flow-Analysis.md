# Polar vs GermanPolar Flow Analysis - SlewToCoordinatesAsync

## Issue Summary
Error "SlewToAltAz outside hardware limits" is thrown during `SlewToCoordinatesAsync` in Polar mode, even though the target should be reachable.

## Root Causes Identified

### 1. **BUG: Swapped Error Messages in CheckReachable (Telescope.cs:1996-2001)**
```csharp
// INCORRECT CODE (lines 1996-2001):
switch (slewType)
{
    case SlewType.SlewAltAz:
        method = "SlewToCoordinates";  // ❌ WRONG
        break;
    case SlewType.SlewRaDec:
        method = "SlewToAltAz";        // ❌ WRONG
        break;
    default:
        method = "Unknown Slew Type";
        break;
}
```

**The method names are backwards!** When slewing RaDec, it reports "SlewToAltAz" error.

### 2. **CheckReachable Only Applies to Polar Mode (line 2007)**
```csharp
// Only check for polar alignment mode
if (SkySettings.AlignmentMode != AlignmentMode.Polar ||
    SkyServer.IsTargetReachable(new[] { axisX, axisY }, slewType)) return;
```

GermanPolar bypasses the check entirely, so it never throws this error.

## Complete Flow Comparison

### **1. ASCOM Entry Point (Telescope.cs:1517)**
Both modes follow the same path:

```
SlewToCoordinatesAsync(RA, Dec)
  ↓
  Line 1535: CheckReachable(RA, Dec, SlewType.SlewRaDec)
    ↓
    Line 2007: if (AlignmentMode != Polar) → RETURN (GermanPolar exits here)
    Line 2007: if (AlignmentMode == Polar) → Check IsTargetReachable
      ↓
      If NOT reachable → throw "SlewToAltAz outside hardware limits" ❌ WRONG MESSAGE
  ↓
  Line 1539: raDec = Transforms.CoordTypeToInternal(RA, Dec)
  ↓
  Line 1549: await SkyServer.SlewRaDecAsync(raDec.X, raDec.Y, tracking: true)
```

**Difference:** GermanPolar skips the hardware limit check entirely.

---

### **2. SkyServer Layer (SkyServer.TelescopeAPI.cs:670)**
Both modes follow the same path:

```
SlewRaDecAsync(RA, Dec, tracking)
  ↓
  Line 685: target = new[] { RA, Dec }
  ↓
  Line 686: return await SlewAsync(target, SlewType.SlewRaDec, tracking)
```

**No difference between modes at this layer.**

---

### **3. SlewController (SlewController.cs:119)**
Both modes follow the same path:

```
ExecuteSlewAsync(operation)
  ↓
  Line 147: SetupPhaseAsync(operation)
    ↓
    Line 298: CancelCurrentSlewAsync() - Stop previous slew
    ↓
    Line 324: operation.Prepare()
  ↓
  Line 155: IsSlewing = true
  ↓
  Line 160: _movementTask = ExecuteMovementAndCompletionAsync(operation)
```

**No difference between modes at this layer.**

---

### **4. SlewOperation Movement (SlewController.cs:621)**
Both modes follow the same path:

```
ExecuteMovementAsync(CancellationToken)
  ↓
  Line 631-638: Mount type switch
    ↓
    If SkyWatcher: SkyServer.SkyGoTo(Target, TrackingAfterSlew, SlewType.SlewRaDec, token)
```

**No difference between modes at this layer.**

---

### **5. Mount Instance SkyGoTo (MountInstance.cs:1246 → 1498)**
Both modes follow the same path through the static wrapper to instance method:

```
SkyGoTo(target, trackingState, slewType, token)
  ↓
  Line 1503: skyTarget = MapSlewTargetToAxes(target, slewType)
    ↓
    THIS IS WHERE POLAR AND GERMAPOLAR DIVERGE!
```

---

### **6. CRITICAL DIVERGENCE: MapSlewTargetToAxes (MountInstance.cs:798)**

Both modes reach the same code:

```csharp
Line 811: switch (slewType)
{
    case SlewType.SlewRaDec:
        Line 815: target = Axes.RaDecToAxesXy(target, context)
        Line 817: target = SkyServer.GetSyncedAxes(target)
        break;
    ...
}
```

Both call `Axes.RaDecToAxesXy()`.

---

### **7. KEY METHOD: Axes.RaDecToAxesXy (Axes.cs:445)**

```csharp
RaDecToAxesXy(raDec, context)
  ↓
  Line 448: double lst = context.GetLst()
  ↓
  Line 449: return RaDecToAxesXyCore(raDec, useLst: true, lst, context)
```

Both modes call the same core conversion.

---

### **8. CORE CONVERSION: RaDecToAxesXyCore (Axes.cs:472)**

**THIS IS WHERE THE COORDINATE CONVERSION HAPPENS:**

```csharp
Line 482: switch (context.AlignmentMode)
{
    case AlignmentMode.Polar:
    case AlignmentMode.GermanPolar:
        // ✅ SAME CODE PATH FOR BOTH MODES
        
        // Convert RA to Hour Angle (HA)
        Line 500: axes[0] = useLst ? 15.0 * (lst - axes[0]) : 15.0 * axes[0]
        Line 501: axes[0] = Range.Range360(axes[0])
        
        // Southern hemisphere dec inversion
        Line 504-505: if (SouthernHemisphere) axes[1] = -axes[1]
        
        // Adjust axes to be through the pole if needed
        Line 508-512:
        if (axes[0] > 180.0)
        {
            axes[0] += 180;
            axes[1] = 180 - axes[1];
        }
        
        // Normalize axes ranges
        Line 515: axes = Range.RangeAxesXy(axes)
        
        // Check for alternative position within flip angle and hardware limits
        Line 518: axes = AxesAppToMount(axes, context)
        Line 519: alt = SkyServer.GetAlternatePosition(axes)
        Line 520: return (alt is null) ? axes : alt
}
```

**OBSERVATION:** Polar and GermanPolar use **IDENTICAL** code in the coordinate conversion!

---

### **9. Alternative Position Check: GetAlternatePosition (SkyServer.cs)**

```csharp
GetAlternatePosition(axes)
  ↓
  Checks if alternate mount position (via meridian flip) is within hardware limits
  ↓
  Returns alternate position if valid, otherwise null
```

**Both modes check for alternate positions the same way.**

---

### **10. Hardware Limit Checking**

#### **At CheckReachable (Telescope.cs:1991):**

**GermanPolar:**
```
Line 2007: if (AlignmentMode != Polar) return; // ✅ EXITS HERE - NO CHECK
```

**Polar:**
```
Line 2007: if (AlignmentMode == Polar) 
  → Call IsTargetReachable(target, slewType)
    ↓
    If NOT reachable → throw "SlewToAltAz outside hardware limits" ❌ WRONG MESSAGE
```

---

## Summary of Differences

| Step | Polar Mode | GermanPolar Mode | Difference? |
|------|------------|------------------|-------------|
| ASCOM Entry (Telescope.cs) | CheckReachable called | CheckReachable called | ❌ NO |
| CheckReachable Logic | **Checks IsTargetReachable** | **Skips check (line 2007)** | ✅ **YES** |
| Error Message | "SlewToAltAz outside hardware limits" | N/A (no check) | ✅ **YES** |
| SkyServer.SlewRaDecAsync | Same code | Same code | ❌ NO |
| SlewController | Same code | Same code | ❌ NO |
| SlewOperation | Same code | Same code | ❌ NO |
| MapSlewTargetToAxes | Same code | Same code | ❌ NO |
| RaDecToAxesXyCore | **IDENTICAL** | **IDENTICAL** | ❌ NO |
| GetAlternatePosition | Same code | Same code | ❌ NO |

---

## Conclusions

1. **Polar and GermanPolar use IDENTICAL coordinate conversion logic** in `Axes.cs`.

2. **The ONLY difference** is in hardware limit checking:
   - **GermanPolar:** Can ALWAYS reach any target via flipping (line 2973 in SkyServer.TelescopeAPI.cs: `return true`)
   - **Polar:** Has limited flip capability, must check `IsTargetReachable`

3. **BUG #1: Error message is WRONG** (Telescope.cs lines 1996-2001)
   - When `slewType = SlewType.SlewRaDec`, it reports "**SlewToAltAz** outside hardware limits" ❌
   - Should report "**SlewToCoordinates** outside hardware limits" ✅
   - **ROOT CAUSE:** The switch case labels were backwards!

4. **Design Decision (NOT a bug):** `CheckReachable` only applies to `Polar` mode
   - Line 2007 in Telescope.cs: `if (AlignmentMode != Polar ... return;`
   - This is INTENTIONAL because GermanPolar can always flip to reach targets
   - Polar mounts have more limited motion (see `IsWithinFlipLimits` line 3000)

5. **Your Error:** "SlewToAltAz outside hardware limits" during `SlewToCoordinatesAsync` in Polar mode
   - **Misleading error message** (fixed by Bug #1 correction)
   - **Actual error:** "SlewToCoordinates outside hardware limits"
   - **Root cause:** The target RA/Dec is genuinely unreachable in Polar mode with current flip limits
   - **Solution:** Either adjust your flip angle/slew limits OR the target is truly unreachable

---

## Recommended Fixes

### **Fix #1: Correct the error messages (APPLIED ✅)**
File: `Telescope.cs` lines 1994-2005

```csharp
// CORRECTED VERSION (Applied):
switch (slewType)
{
    case SlewType.SlewAltAz:
        method = "SlewToAltAz";           // ✅ FIXED
        break;
    case SlewType.SlewRaDec:
        method = "SlewToCoordinates";     // ✅ FIXED
        break;
    default:
        method = "Unknown Slew Type";
        break;
}
```

### **Fix #2: Do NOT apply check to GermanPolar (NO CHANGE NEEDED)**
The current behavior is CORRECT:
- GermanPolar always returns true in `IsTargetReachable` (line 2973)
- This is because GermanPolar can flip to reach any target
- Polar mode has stricter limits due to limited flip capability

**Current code (CORRECT):**
```csharp
// In Telescope.cs line 2007:
if (SkySettings.AlignmentMode != AlignmentMode.Polar ||
    SkyServer.IsTargetReachable(new[] { axisX, axisY }, slewType)) return;

// In SkyServer.TelescopeAPI.cs line 2973:
public static bool IsTargetReachable(double[] target, SlewType slewState)
{
    if (_settings!.AlignmentMode == AlignmentMode.GermanPolar) return true;
    // ... Polar mode checks limits ...
}
```

---

## Questions for Investigation ✅ ANSWERED

1. **Why was GermanPolar excluded from the hardware limit check?**
   - ✅ **ANSWER:** Because GermanPolar can ALWAYS flip to reach any target
   - See `IsTargetReachable` line 2973: `if (AlignmentMode == GermanPolar) return true;`
   - This is by design - GermanPolar has full 360° motion via flipping

2. **What does `IsTargetReachable` actually check?**
   - ✅ **ANSWER:** It checks if target is within flip limits for the current alignment mode
   - For **GermanPolar:** Always returns `true`
   - For **Polar:** Checks `IsWithinFlipLimits` (line 3000) based on `AxisLimitX` setting
   - For **AltAz:** Checks azimuth limits

3. **Should the fix be:**
   - ✅ **ANSWER: A. Just fix the error message**
   - The current logic is CORRECT - only Polar mode needs the check
   - GermanPolar intentionally bypasses because it can always flip

## Next Steps for User

Since the error is now correctly labeled as "SlewToCoordinates outside hardware limits":

1. **Check your Polar mode settings:**
   - Look at `AxisLimitX` (flip angle limit)
   - Look at `HourAngleLimit` settings
   - These define the reachable range for Polar mounts

2. **Verify the target coordinates:**
   - Log the RA/Dec you're trying to slew to
   - Check if it's truly reachable given your mount's flip limits

3. **Consider switching to GermanPolar mode for testing:**
   - GermanPolar has no such restrictions
   - If it works in GermanPolar but not Polar, it confirms the issue is the flip limits

4. **Check the simulator's Polar mode configuration:**
   - The simulator may have stricter Polar limits than expected
   - Compare with GermanPolar settings
