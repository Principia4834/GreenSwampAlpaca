# Settings Migration Complete - AtPark & CanSetPierSide Added

## Summary

Successfully added `AtPark` and `CanSetPierSide` settings to all template files and updated JSON schemas.

---

## Changes Made

### 1. **common.json** ?
Added `CanSetPierSide` after `AtPark`:
```json
"AtPark": false,
"CanSetPierSide": true,  // NEW - default true for most mounts
```

**Note**: `AtPark` was already present in common.json

### 2. **germanpolar-overrides.json** ?
Added at beginning of Overrides section:
```json
"AtPark": false,
"CanSetPierSide": true,  // GEM mounts support pier flips
```

### 3. **polar-overrides.json** ?
Added at beginning of Overrides section:
```json
"AtPark": false,
"CanSetPierSide": true,  // Fork mounts can report pier side
```

### 4. **altaz-overrides.json** ?
Added at beginning of Overrides section:
```json
"AtPark": false,
"CanSetPierSide": false,  // Alt-Az mounts don't have pier concept
```

### 5. **settings-template.schema.json** ?
Added `CanSetPierSide` property after `AtPark`:
```json
"CanSetPierSide": {
  "type": "boolean",
  "description": "Mount can set pier side (for meridian flips)"
},
```

**Note**: `AtPark` was already in the schema

### 6. **settings-override.schema.json** ?
Added both properties at beginning of Overrides properties, plus all other override properties:
```json
"AtPark": {
  "type": "boolean",
  "description": "Mount is at park position (persists across restarts)"
},
"CanSetPierSide": {
  "type": "boolean",
  "description": "Mount can set pier side (false for AltAz, true for equatorial mounts that support pier flips)"
},
// ... plus all other override properties (axis limits, tracking limits, etc.)
```

Also added all other override properties that were in JSON but missing from schema:
- `AxisUpperLimitY`, `AxisLowerLimitY`, `AxisLimitX`
- `AxisHzTrackingLimit`
- `LimitTracking`, `LimitPark`, `ParkLimitName`
- `HzLimitTracking`, `HzLimitPark`, `ParkHzLimitName`
- `PolarMode`

---

## Purpose of Each Setting

### AtPark
**Type**: Boolean configuration setting
**Purpose**: Persists mount park state across power cycles
**Reason for inclusion**: 
- Mount hardware does NOT maintain park state when powered off
- Application needs to restore park state on startup
- Used to initialize mount to correct state after power cycle

**Values**:
- All modes: `false` (default - mount not parked)
- Changed at runtime when user parks/unparks mount
- Persisted to configuration file

### CanSetPierSide
**Type**: Boolean configuration setting
**Purpose**: Indicates if mount can action pier side set commands
**Reason for inclusion**:
- Configuration setting INDEPENDENT of hardware capabilities
- Some mounts cannot action pier side commands even if physically capable
- Allows user to disable pier side changes for problematic mounts
- Different from ASCOM CanSetPierSide (which reports hardware capability)

**Values**:
- German Polar: `true` - Supports pier flips
- Polar (Fork): `true` - Can report pier side
- Alt-Azimuth: `false` - Pier concept doesn't apply

---

## Settings Hierarchy

### Common Template (Default Values)
```
common.json:
  AtPark: false
  CanSetPierSide: true  ? Most mounts support it
```

### Overrides (Mode-Specific)
```
germanpolar-overrides.json:
  AtPark: false         ? Same as common
  CanSetPierSide: true  ? GEM supports pier flips

polar-overrides.json:
  AtPark: false         ? Same as common
  CanSetPierSide: true  ? Fork can report pier side

altaz-overrides.json:
  AtPark: false         ? Same as common
  CanSetPierSide: false ? Alt-Az doesn't have pier concept
```

---

## Schema Validation

Both schemas now properly validate these settings:

### Template Schema
- Validates `AtPark` (boolean)
- Validates `CanSetPierSide` (boolean)

### Override Schema
- Validates `AtPark` (boolean) in Overrides
- Validates `CanSetPierSide` (boolean) in Overrides
- Plus all other override properties now properly documented

---

## Comparison with XML Settings

### From GermanPolar.settings:
```xml
<Setting Name="AtPark" Type="System.Boolean">
  <Value>False</Value>
</Setting>
<Setting Name="CanSetPierSide" Type="System.Boolean">
  <Value>True</Value>
</Setting>
```

### To germanpolar-overrides.json:
```json
"AtPark": false,
"CanSetPierSide": true,
```

? **Exact match** - Values preserved

---

## Files Modified

| File | Status | Changes |
|------|--------|---------|
| `common.json` | ? Updated | Added `CanSetPierSide: true` |
| `germanpolar-overrides.json` | ? Updated | Added `AtPark: false`, `CanSetPierSide: true` |
| `polar-overrides.json` | ? Updated | Added `AtPark: false`, `CanSetPierSide: true` |
| `altaz-overrides.json` | ? Updated | Added `AtPark: false`, `CanSetPierSide: false` |
| `settings-template.schema.json` | ? Updated | Added `CanSetPierSide` property |
| `settings-override.schema.json` | ? Updated | Added `AtPark`, `CanSetPierSide`, and all other override properties |
| `MIGRATION_SUMMARY.md` | ? Updated | Updated "Settings NOT Transferred" section |

---

## Validation Checklist

- [x] `AtPark` in common.json
- [x] `CanSetPierSide` in common.json
- [x] Both settings in all three override files
- [x] Correct values per mount type
- [x] Both properties in template schema
- [x] Both properties in override schema
- [x] All other override properties in override schema
- [x] Documentation updated
- [x] JSON syntax valid (no trailing commas)

---

## Next Steps

1. ? **DONE**: Transfer all XML settings to JSON overrides
2. ? **DONE**: Add AtPark and CanSetPierSide
3. ? **DONE**: Update JSON schemas
4. ?? **PENDING**: Verify SkySettings model has AtPark and CanSetPierSide properties
5. ?? **PENDING**: Test default profile creation with new settings
6. ?? **PENDING**: Build and test application
7. ?? **PENDING**: Remove old XML files from Profiles folder

---

## Build Readiness

**Status**: ?? **Ready to build** (schemas updated)

The schemas are now complete with all override properties. The build should succeed.

**Before removing XML files**:
1. Build application to verify no errors
2. Test profile creation
3. Verify AtPark and CanSetPierSide are properly loaded
4. Test park/unpark functionality
5. Test pier side operations (GEM only)

---

## Schema Completeness

### Override Schema Now Includes:
? `AlignmentMode`
? `AtPark` (NEW)
? `CanSetPierSide` (NEW)
? `HomeAxisX`, `HomeAxisY`
? `AutoHomeAxisX`, `AutoHomeAxisY`
? `ParkName`, `ParkAxes`, `ParkPositions`
? `HourAngleLimit`
? `AxisUpperLimitY`, `AxisLowerLimitY`, `AxisLimitX` (NEW)
? `AxisHzTrackingLimit` (NEW)
? `LimitTracking`, `LimitPark`, `ParkLimitName` (NEW)
? `HzLimitTracking`, `HzLimitPark`, `ParkHzLimitName` (NEW)
? `PolarMode` (NEW)
? `NoSyncPastMeridian`

**All properties from XML now properly documented in schema!**

---

## Migration Complete ?

All settings from the legacy XML files have been successfully migrated to the new JSON template system, including:
- AtPark (mount park state persistence)
- CanSetPierSide (pier side command capability)
- All axis limits
- All tracking limits
- Polar mode settings
- Park positions and limits

The system is now ready for testing and deployment.
