# Batch 11: Horizon & Alt-Az Tracking Settings - COMPLETE ?

## Summary
Successfully implemented synchronization for **7 properties** covering horizon limits, alt-az tracking configuration, and instrument information.

---

## Properties Synced (7 total)

### Horizon Limit Properties (4 bidirectional)

#### 1. **HzLimitPark** (bool)
- **Old**: `SkySettings.HzLimitPark` { get; set; }
- **New**: `newSettings.HzLimitPark` (bool)
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Enable parking when horizon limit is reached

#### 2. **ParkHzLimitName** (string)
- **Old**: `SkySettings.ParkHzLimitName` { get; set; }
- **New**: `newSettings.ParkHzLimitName` (string)
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Name of park position to use when horizon limit triggered

#### 3. **HzLimitTracking** (bool)
- **Old**: `SkySettings.HzLimitTracking` { get; set; }
- **New**: `newSettings.HzLimitTracking` (bool)
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Enable horizon limit for tracking

#### 4. **AxisHzTrackingLimit** (double)
- **Old**: `SkySettings.AxisHzTrackingLimit` { get; set; }
- **New**: `newSettings.AxisHzTrackingLimit` (double)
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Horizon altitude limit in degrees

### Alt-Az Tracking Property (1 bidirectional)

#### 5. **AltAzTrackingUpdateInterval** (int)
- **Old**: `SkySettings.AltAzTrackingUpdateInterval` { get; set; }
- **New**: `newSettings.AltAzTrackingUpdateInterval` (int)
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Update interval in milliseconds for alt-az tracking predictor

### Instrument Information (2 properties - OLD ? NEW only)

#### 6. **InstrumentDescription** (string - read-only in old)
- **Old**: `SkySettings.InstrumentDescription` { get; **private set**; }
- **New**: `newSettings.InstrumentDescription` (string)
- **Sync Direction**: OLD ? NEW only
- **Reason**: Read-only in old system (private setter)
- **Purpose**: Telescope description for ASCOM

#### 7. **InstrumentName** (string - read-only in old)
- **Old**: `SkySettings.InstrumentName` { get; **private set**; }
- **New**: `newSettings.InstrumentName` (string)
- **Sync Direction**: OLD ? NEW only
- **Reason**: Read-only in old system (private setter)
- **Purpose**: Telescope name for ASCOM

---

## Synchronization Flow

### NEW ? OLD (SyncNewToOld)
```csharp
// Phase 4 Batch 11: Horizon & Alt-Az Tracking Settings (5 properties)
SkySettings.HzLimitPark = newSettings.HzLimitPark;
SkySettings.ParkHzLimitName = newSettings.ParkHzLimitName;
SkySettings.HzLimitTracking = newSettings.HzLimitTracking;
SkySettings.AxisHzTrackingLimit = newSettings.AxisHzTrackingLimit;
SkySettings.AltAzTrackingUpdateInterval = newSettings.AltAzTrackingUpdateInterval;

// Note: InstrumentDescription and InstrumentName are read-only in old system
// They should only be synced from old ? new, not new ? old
```

### OLD ? NEW (SyncOldToNew)
```csharp
// Phase 4 Batch 11: Horizon & Alt-Az Tracking Settings (7 properties)
newSettings.HzLimitPark = SkySettings.HzLimitPark;
newSettings.ParkHzLimitName = SkySettings.ParkHzLimitName;
newSettings.HzLimitTracking = SkySettings.HzLimitTracking;
newSettings.AxisHzTrackingLimit = SkySettings.AxisHzTrackingLimit;
newSettings.AltAzTrackingUpdateInterval = SkySettings.AltAzTrackingUpdateInterval;

// InstrumentDescription and InstrumentName can be synced FROM old system
// (they're read-only in old but can be written in new)
newSettings.InstrumentDescription = SkySettings.InstrumentDescription;
newSettings.InstrumentName = SkySettings.InstrumentName;
```

---

## Keys Added to Constants
```csharp
// Phase 4 Batch 11: Horizon & Alt-Az Tracking Settings
public const string HzLimitPark = "HzLimitPark";
public const string ParkHzLimitName = "ParkHzLimitName";
public const string HzLimitTracking = "HzLimitTracking";
public const string AxisHzTrackingLimit = "AxisHzTrackingLimit";
public const string AltAzTrackingUpdateInterval = "AltAzTrackingUpdateInterval";
public const string InstrumentDescription = "InstrumentDescription";
public const string InstrumentName = "InstrumentName";
```

---

## Testing Checklist

### ? Verify Horizon Limit Synchronization
- [ ] HzLimitPark syncs correctly in both directions
- [ ] ParkHzLimitName syncs correctly in both directions
- [ ] HzLimitTracking syncs correctly in both directions
- [ ] AxisHzTrackingLimit syncs correctly in both directions
- [ ] When horizon limit is triggered, correct park position is used

### ? Verify Alt-Az Tracking
- [ ] AltAzTrackingUpdateInterval syncs correctly in both directions
- [ ] Changing interval affects tracking update frequency
- [ ] Value persists across application restarts

### ? Verify Instrument Information
- [ ] InstrumentDescription syncs from old ? new
- [ ] InstrumentName syncs from old ? new
- [ ] Values appear correctly in ASCOM driver properties
- [ ] NEW ? OLD does NOT overwrite these read-only properties

---

## Property Analysis

| Property | Old System | New System | Read/Write | Synced | Notes |
|----------|-----------|-----------|------------|--------|-------|
| HzLimitPark | bool | bool | Read/Write | ? Both | Enable horizon park |
| ParkHzLimitName | string | string | Read/Write | ? Both | Park position name |
| HzLimitTracking | bool | bool | Read/Write | ? Both | Enable horizon tracking |
| AxisHzTrackingLimit | double | double | Read/Write | ? Both | Horizon altitude limit |
| AltAzTrackingUpdateInterval | int | int | Read/Write | ? Both | Update interval in ms |
| InstrumentDescription | string (private set) | string | Read-only | ? OLD?NEW | ASCOM description |
| InstrumentName | string (private set) | string | Read-only | ? OLD?NEW | ASCOM telescope name |

---

## Build Status
? **Build Successful** - No errors or warnings

---

## Progress Summary
- **Total Properties Synced**: 93 (8 Phase 2 + 18 Phase 3 + 67 Phase 4 Batches 1-11)
- **Phase 4 Batches Complete**: 11 of ~12
- **Properties Remaining**: ~5-10 (final batch for complex objects + misc properties)

---

## Design Decisions

### Why Instrument Properties Are Old ? New Only

**InstrumentDescription** and **InstrumentName** have `private set` in the old system because they are:
- **ASCOM driver properties**: Exposed to external applications via ASCOM interface
- **Configuration metadata**: Set during initialization, not changed dynamically
- **Read-only by design**: Users configure these in settings, drivers read them

These properties can be synced FROM old system TO new system (where they're read/write), but cannot be synced back because the old system marks them as read-only. This is the correct behavior - these are configuration values that should be set once and read many times.

### Horizon Limit Behavior

The horizon limit system works in two modes:

1. **HzLimitTracking = true**: Stops tracking when altitude drops below `AxisHzTrackingLimit`
2. **HzLimitPark = true**: Automatically parks mount at `ParkHzLimitName` position when limit reached

This prevents the telescope from:
- Tracking objects too close to the horizon
- Hitting physical obstructions (trees, buildings, ground)
- Continuing tracking through unsafe altitudes

### Alt-Az Tracking Update Interval

For Alt-Az mounts, the tracking rates must be continuously updated because:
- Alt-Az coordinates change as objects move across the sky
- The mount needs to adjust both axes simultaneously
- The `AltAzTrackingUpdateInterval` controls how often these rates are recalculated

**Typical values**:
- **Fast**: 50-100 ms (high precision, more CPU)
- **Medium**: 200-500 ms (balanced)
- **Slow**: 1000+ ms (low precision, less CPU)

Higher resolution mounts may need shorter intervals for accurate tracking.

---

## Functional Context

### How Horizon Limits Are Checked

From `SkyServer.cs` (CheckAxisLimits method):
```csharp
// Horizon Limit Test
if (SkySettings.HzLimitPark || SkySettings.HzLimitTracking)
{
    switch (SkySettings.AlignmentMode)
    {
        case AlignmentMode.AltAz:
            if ((Altitude <= SkySettings.AxisHzTrackingLimit || ...) && Tracking)
            {
                limitHit = true;
                horizonLimit = true;
            }
            break;
        case AlignmentMode.Polar:
            if (Altitude <= SkySettings.AxisHzTrackingLimit && Tracking)
            {
                limitHit = true;
                horizonLimit = true;
            }
            break;
        // ...
    }
}

// Horizon Triggers
if (horizonLimit)
{
    if (Tracking && SkySettings.HzLimitTracking)
        Tracking = false; // Stop tracking
    
    if (SkySettings.HzLimitPark && SlewState != SlewType.SlewPark)
    {
        var found = SkySettings.ParkPositions.Find(x => x.Name == SkySettings.ParkHzLimitName);
        if (found != null)
        {
            ParkSelected = found;
            GoToPark();
        }
    }
}
```

### How Alt-Az Tracking Works

From `SkyServer.cs` (AltAzTrackingTimerEvent and SetTracking methods):
```csharp
private static void AltAzTrackingTimerEvent(object sender, EventArgs e)
{
    if (_altAzTrackingTimer?.IsRunning == true)
    {
        SetTracking(); // Update tracking rates
    }
}

private static void StartAltAzTrackingTimer()
{
    _altAzTrackingTimer = new MediaTimer
    {
        Period = SkySettings.AltAzTrackingUpdateInterval // Use interval from settings
    };
    _altAzTrackingTimer.Tick += AltAzTrackingTimerEvent;
    _altAzTrackingTimer.Start();
}
```

The timer fires every `AltAzTrackingUpdateInterval` milliseconds, recalculating the required tracking rates for both axes based on the target's changing Alt-Az position.

---

## Next Batch Suggestions

### **Batch 12: Final Simple Properties & Integration (~5-8 properties)**
Remaining simple properties not yet synced:
- **CdCip** (string) - Cartes du Ciel IP address integration
- **CdCport** (int) - Cartes du Ciel port integration
- **EquatorialCoordinateType** (string) - Verify if already synced
- **RATrackingOffset** (int) - Note: different casing from `RaTrackingOffset`
- **SyncLimit** (int) - Check if already synced
- **CustomRaTrackingOffset** (int) - Check if already synced
- **CustomDecTrackingOffset** (int) - Check if already synced
- **AxisTrackingLimit** (double) - Check if already synced

### **Final: Complex Objects (Phase 5)**
Remaining complex types that need special handling:
- **ParkPositions** (`List<ParkPosition>`) - Complex object list
- **HcPulseGuides** (`List<HcPulseGuide>`) - Complex object list  
- **AxisModelOffsets** (`AxisModelOffset` object) - Complex nested object
- **ParkAxes** (`double[]` array) - Simple array

---

## Commit Message Suggestion
```
Phase 4 Batch 11: Horizon & Alt-Az tracking settings synchronized

- Added 5 bidirectional properties for horizon limits and tracking:
  * HzLimitPark, ParkHzLimitName, HzLimitTracking
  * AxisHzTrackingLimit, AltAzTrackingUpdateInterval
- Added 2 old?new properties for ASCOM instrument info:
  * InstrumentDescription, InstrumentName (read-only in old system)
- Documented horizon limit safety system behavior
- Explained Alt-Az tracking update interval purpose

Total properties synced: 93/~100
Phase 4 Batch 11: COMPLETE ?
```

---

**Status**: Ready for testing and commit ?

**Estimated Remaining Work**: 
- 1 batch for final simple properties (~5-8 properties)
- 1 batch for complex objects (Phase 5)
- Total remaining time: 1-2 hours

**Key Achievement**: Successfully synchronized horizon safety limits and Alt-Az tracking configuration, which are critical for safe telescope operation near the horizon and accurate tracking for Alt-Az mounted telescopes.

---

## Safety Note

The horizon limit system is a **safety feature** that prevents:
1. **Physical damage**: Hitting ground, tripod, or obstructions
2. **Poor tracking**: Objects too low have excessive atmospheric refraction
3. **Loss of target**: Objects disappearing below horizon during long exposures

Always configure `AxisHzTrackingLimit` to a safe value above your local horizon (typically 5-15 degrees) to account for:
- Terrain irregularities
- Buildings or trees
- Mount-specific mechanical limits
- Atmospheric distortion near horizon

This batch ensures these critical safety settings are properly synchronized between old and new systems.
