# Batch 10: Mount Behavior & Capability Settings - COMPLETE ?

## Summary
Successfully implemented synchronization for **1 bidirectional property** and documented **7 properties** that cannot be synced due to architectural constraints (new-system-only or read-only).

---

## Properties Analyzed (8 total)

### ? **Synced Properties (1 bidirectional)**

#### 1. **PolarMode** (string ? enum)
- **Old**: `SkySettings.PolarMode` (PolarMode enum)
- **New**: `newSettings.PolarMode` (string)
- **Enum Values**: "Left", "Right"
- **Conversion**: `ParsePolarMode()` - maps string to enum
- **Bidirectional**: ? NEW ? OLD
- **Purpose**: Determines polar alignment orientation

---

### ? **Non-Synced Properties (7 total)**

#### 2. **DisconnectOnPark** (new system only)
- **Old**: ? Does not exist
- **New**: `newSettings.DisconnectOnPark` (bool)
- **Reason**: New feature in .NET 8 system
- **Action**: Not synced (no old property to map to)

#### 3. **AutoTrack** (read-only in old)
- **Old**: `SkySettings.AutoTrack` { get; **private set**; }
- **New**: `newSettings.AutoTrack` (bool)
- **Reason**: Computed/derived property in old system
- **Action**: Cannot sync FROM new (old setter is private)
- **Note**: NEW can read from OLD, but not vice versa

#### 4. **ModelType** (new system only)
- **Old**: ? Does not exist
- **New**: `newSettings.ModelType` (string)
- **Reason**: New model configuration feature
- **Action**: Not synced (no old property to map to)

#### 5. **Pressure** (new system only)
- **Old**: ? Does not exist
- **New**: `newSettings.Pressure` (double)
- **Reason**: New environmental setting for refraction
- **Action**: Not synced (no old property to map to)

#### 6. **VersionOne** (read-only capability flag)
- **Old**: `SkySettings.VersionOne` { get; **private set**; }
- **New**: `newSettings.VersionOne` (bool)
- **Reason**: Capability flag, computed from mount type
- **Action**: Cannot sync FROM new (old setter is private)
- **Note**: Derived from other settings

#### 7. **NumMoveAxis** (read-only capability flag)
- **Old**: `SkySettings.NumMoveAxis` { get; **private set**; }
- **New**: `newSettings.NumMoveAxis` (int)
- **Reason**: Capability flag, computed from mount type
- **Action**: Cannot sync FROM new (old setter is private)
- **Note**: Derived from mount capabilities

#### 8. **NoSyncPastMeridian** (read-only limit flag)
- **Old**: `SkySettings.NoSyncPastMeridian` { get; **private set**; }
- **New**: `newSettings.NoSyncPastMeridian` (bool)
- **Reason**: Safety limit flag, derived from mount type
- **Action**: Cannot sync FROM new (old setter is private)
- **Note**: Computed from alignment mode and limits

---

## Key Implementation Details

### PolarMode Enum Mapping
```csharp
private static PolarMode ParsePolarMode(string value)
{
    return Enum.TryParse<PolarMode>(value, true, out var result) 
        ? result 
        : PolarMode.Left; // Default to Left if parsing fails
}
```

Maps to `GreenSwamp.Alpaca.MountControl.PolarMode` enum:
- "Left" ? `PolarMode.Left`
- "Right" ? `PolarMode.Right`

### Synchronization Flow

#### NEW ? OLD (SyncNewToOld)
```csharp
// Phase 4 Batch 10: Mount Behavior & Capability Settings (1 property)
// PolarMode - bidirectional sync
SkySettings.PolarMode = ParsePolarMode(newSettings.PolarMode);

// Not synced (documented reasons):
// - DisconnectOnPark (new only)
// - AutoTrack (read-only in old)
// - ModelType (new only)
// - Pressure (new only)
// - VersionOne, NumMoveAxis, NoSyncPastMeridian (all read-only in old)
```

#### OLD ? NEW (SyncOldToNew)
```csharp
// Phase 4 Batch 10: Mount Behavior & Capability Settings
// Bidirectional property:
newSettings.PolarMode = SkySettings.PolarMode.ToString();

// Not synced FROM old:
// - DisconnectOnPark (new only - stays as configured in new)
// - ModelType (new only - stays as configured in new)
// - Pressure (new only - stays as configured in new)
// - AutoTrack, VersionOne, NumMoveAxis, NoSyncPastMeridian (read-only - can't change)
```

---

## Keys Added to Constants
```csharp
// Phase 4 Batch 10: Mount Behavior & Capability Settings
public const string PolarMode = "PolarMode";
// Note: The following are new system only or read-only in old system:
// - DisconnectOnPark (new only)
// - AutoTrack (read-only in old)
// - ModelType (new only)
// - Pressure (new only)
// - VersionOne (read-only in old)
// - NumMoveAxis (read-only in old)
// - NoSyncPastMeridian (read-only in old)
```

---

## Testing Checklist

### ? Verify Enum Conversions
- [ ] PolarMode: "Left" correctly maps to `PolarMode.Left`
- [ ] PolarMode: "Right" correctly maps to `PolarMode.Right`
- [ ] PolarMode: Invalid string defaults to `PolarMode.Left`

### ? Verify Bidirectional Sync
- [ ] PolarMode syncs from new ? old
- [ ] PolarMode syncs from old ? new

### ? Verify Read-Only Properties Behavior
- [ ] AutoTrack in old system stays unchanged (private setter)
- [ ] VersionOne in old system stays unchanged (derived)
- [ ] NumMoveAxis in old system stays unchanged (capability)
- [ ] NoSyncPastMeridian in old system stays unchanged (derived)

### ? Verify New-System-Only Properties
- [ ] DisconnectOnPark works in new system, not synced to old
- [ ] ModelType works in new system, not synced to old
- [ ] Pressure works in new system, not synced to old

---

## Property Analysis

| Property | Old System | New System | Read/Write | Synced | Notes |
|----------|-----------|-----------|------------|--------|-------|
| PolarMode | PolarMode enum | string | Read/Write | ? Both | Enum conversion required |
| DisconnectOnPark | ? N/A | bool | N/A | ? No | New system only |
| AutoTrack | bool (private set) | bool | Read-only | ? No | Can't sync FROM new |
| ModelType | ? N/A | string | N/A | ? No | New system only |
| Pressure | ? N/A | double | N/A | ? No | New system only |
| VersionOne | bool (private set) | bool | Read-only | ? No | Derived/capability flag |
| NumMoveAxis | int (private set) | int | Read-only | ? No | Derived/capability flag |
| NoSyncPastMeridian | bool (private set) | bool | Read-only | ? No | Derived safety flag |

---

## Build Status
? **Build Successful** - No errors or warnings

---

## Progress Summary
- **Total Properties Synced**: 88 (8 Phase 2 + 18 Phase 3 + 62 Phase 4 Batches 1-10)
- **Phase 4 Batches Complete**: 10 of ~11-12
- **Properties Remaining**: ~5-10 (final batches for complex objects)

---

## Design Decisions

### Why 7 Properties Weren't Synced

#### New System Only (3 properties)
**DisconnectOnPark, ModelType, Pressure** are new features in the .NET 8 system that don't exist in the legacy configuration. These represent new functionality:
- **DisconnectOnPark**: New power management feature
- **ModelType**: New pointing model configuration
- **Pressure**: New environmental parameter for advanced refraction

Including them in the bridge would cause errors when trying to access non-existent old properties.

#### Read-Only in Old System (4 properties)
**AutoTrack, VersionOne, NumMoveAxis, NoSyncPastMeridian** have `private set` in the old system because they are:
- **Computed/Derived**: Calculated from other properties
- **Capability Flags**: Determined by mount type and firmware
- **Safety Limits**: Derived from alignment mode and configuration

These cannot be set from the new system because:
1. They're derived values, not configurable settings
2. The old system controls their values internally
3. Attempting to set them would require reflection hacks
4. Their values are authoritative in the old system

### Architectural Principle
**Read-only properties in old system indicate derived or computed values that should NOT be synced FROM new system.** The new system can READ these values, but should not attempt to WRITE them.

---

## Next Batch Suggestions

### **Batch 11: Horizon & Alt-Az Tracking (~3-4 properties)**
Properties for alt-az tracking and horizon limits:
- HzLimitPark (bool - already synced? verify)
- ParkHzLimitName (string - already synced? verify)
- HzLimitTracking (bool - already synced? verify)
- AxisHzTrackingLimit (double - already synced? verify)
- AltAzTrackingUpdateInterval (int)

### **Batch 12: Integration & Remaining Simple Properties (~4-6 properties)**
- CdCip (string - Cartes du Ciel integration)
- CdCport (int - Cartes du Ciel integration)
- InstrumentDescription (string - read-only? check)
- InstrumentName (string - read-only? check)
- EquatorialCoordinateType (string - check if already synced)
- RATrackingOffset (int - note different casing from RaTrackingOffset)

### **Final: Complex Objects (Phase 5)**
- ParkPositions (List<ParkPosition>)
- HcPulseGuides (List<HcPulseGuide>)
- AxisModelOffsets (AxisModelOffset object)
- ParkAxes (double[] array)

---

## Commit Message Suggestion
```
Phase 4 Batch 10: Mount behavior & capability settings synchronized (1 property)

- Added PolarMode (enum/string conversion for polar alignment)
- Documented 7 non-synced properties:
  * 3 new-system-only: DisconnectOnPark, ModelType, Pressure
  * 4 read-only in old: AutoTrack, VersionOne, NumMoveAxis, NoSyncPastMeridian
- Implemented ParsePolarMode() converter for enum mapping
- Added detailed documentation for architectural decisions

Total properties synced: 88/~100
Phase 4 Batch 10: COMPLETE ?
```

---

**Status**: Ready for testing and commit ?

**Estimated Remaining Work**: 
- 1-2 more batches for simple properties (~5-10 properties)
- 1 batch for complex objects (Phase 5)
- Total remaining time: 1-2 hours

**Key Achievement**: Properly identified and documented the difference between:
- **Configurable settings** (can be synced bidirectionally)
- **Derived properties** (read-only, computed from other settings)
- **New features** (exist only in new system)

This architectural clarity will prevent bugs and confusion in future development.
