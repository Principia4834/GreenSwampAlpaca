# The IsPulseGuiding Combined Property - Why It's Critical

## Quick Reference

### The Three-Tier System

```csharp
// TIER 1: Internal (per-axis with CTS disposal)
public static bool IsPulseGuidingRa { get; set; }
public static bool IsPulseGuidingDec { get; set; }

// TIER 2: ASCOM Interface (combined OR)
public static bool IsPulseGuiding => (IsPulseGuidingDec || IsPulseGuidingRa);

// TIER 3: Driver Wrapper (validation)
public bool IsPulseGuiding => SkyServer.IsPulseGuiding;
```

## Why ASCOM Clients ONLY See Combined Property

**ASCOM ITelescopeV4 Interface Definition:**
```csharp
public interface ITelescopeV4
{
    void PulseGuide(GuideDirection Direction, int Duration);
    bool IsPulseGuiding { get; }  // ✅ Single combined property
    
    // ❌ NOT defined:
    // bool IsPulseGuidingRa { get; }
    // bool IsPulseGuidingDec { get; }
}
```

**Client Code (PHD2, NINA, ConformU):**
```csharp
telescope.PulseGuide(GuideDirection.East, 1000);

// Client can ONLY access:
while (telescope.IsPulseGuiding)  // ✅ Combined property
{
    Thread.Sleep(10);
}

// Client CANNOT access:
// while (telescope.IsPulseGuidingRa)  // ❌ Not in ASCOM interface
```

## OR Logic Truth Table

| IsPulseGuidingRa | IsPulseGuidingDec | IsPulseGuiding | Meaning |
|------------------|-------------------|----------------|---------|
| `false` | `false` | `false` | Idle - can start new pulse |
| `true` | `false` | `true` | RA pulsing - must wait |
| `false` | `true` | `true` | Dec pulsing - must wait |
| `true` | `true` | `true` | Both pulsing - must wait |

**Critical Case: One Axis Finishes First**
```
T=0   : Start RA pulse (800ms) + Dec pulse (600ms)
        IsPulseGuiding = (true || true) = true

T=600 : Dec finishes
        IsPulseGuidingDec = false
        IsPulseGuiding = (true || false) = true ✅ STILL TRUE!
        
T=800 : RA finishes
        IsPulseGuidingRa = false
        IsPulseGuiding = (false || false) = false ✅ NOW FALSE
```

## What Clients See

### PHD2 Sequential Pulses
```csharp
telescope.PulseGuide(East, 500);
while (telescope.IsPulseGuiding) { }  // Waits until false

telescope.PulseGuide(North, 500);
while (telescope.IsPulseGuiding) { }  // Waits until false
```

### NINA Simultaneous Dithering
```csharp
telescope.PulseGuide(East, 1000);   // RA starts
telescope.PulseGuide(North, 800);   // Dec starts

// Single wait for BOTH to complete
while (telescope.IsPulseGuiding) { }  // Returns when BOTH done
```

### ConformU Conformance Test
```csharp
// Test verifies:
// 1. Property exists (ITelescopeV4.IsPulseGuiding)
// 2. Returns true immediately after PulseGuide()
// 3. Returns false when pulse complete
// 4. Works with simultaneous pulses
```

## Why Refactoring Must Preserve This

**If You Break The Combined Property:**
- ❌ ASCOM interface violated
- ❌ ConformU tests fail
- ❌ PHD2 won't work (can't poll status)
- ❌ NINA dithering breaks (can't detect completion)
- ❌ Clients see implementation details (Ra/Dec split exposed)

**Original Implementation:**
```csharp
// ✅ Simple, elegant, ASCOM-compliant
public static bool IsPulseGuiding => (IsPulseGuidingDec || IsPulseGuidingRa);
```

**Current Broken Refactor:**
```csharp
// In PulseGuideController:
public bool IsPulseGuiding => IsPulseGuidingRa || IsPulseGuidingDec;

// Problem: Controller's flags can be stale due to wait loops in finally blocks
// Result: Returns false while hardware still running, or true after hardware done
```

## Key Takeaway

**The combined property is not a convenience - it's an ASCOM specification requirement.**

- ASCOM defines **single** property
- Clients depend on **single** property
- Internal Ra/Dec split is **implementation detail**
- OR logic must be **exact** (not approximate)
- Must reflect **actual hardware state**, not controller state

Any refactoring that doesn't preserve this three-tier system will break ASCOM compliance.
