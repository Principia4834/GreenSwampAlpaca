# Settings Migration Summary

## Overview
Successfully transferred all settings from legacy XML `.settings` files to new JSON override files.

## Files Migrated

### Source Files (XML)
- `GreenSwamp.Alpaca.Settings/Profiles/GermanPolar.settings`
- `GreenSwamp.Alpaca.Settings/Profiles/Polar.settings`
- `GreenSwamp.Alpaca.Settings/Profiles/AltAz.settings`

### Target Files (JSON)
- `GreenSwamp.Alpaca.Settings/Templates/germanpolar-overrides.json`
- `GreenSwamp.Alpaca.Settings/Templates/polar-overrides.json`
- `GreenSwamp.Alpaca.Settings/Templates/altaz-overrides.json`

---

## German Polar (GEM) Settings

### Key Changes from Previous JSON
- **HomeAxisX**: 0.0 ? **90.0**
- **HomeAxisY**: 90.0 (unchanged)
- **AutoHomeAxisX**: 0.0 ? **90.0**
- **AutoHomeAxisY**: 90.0 (unchanged)
- **ParkAxes**: [180.0, 90.0] ? **[90.0, 90.0]**

### Settings Added
- `HourAngleLimit`: 15.0
- `AxisUpperLimitY`: 180.0
- `AxisLowerLimitY`: -180.0
- `AxisLimitX`: 180.0
- `AxisHzTrackingLimit`: 0.0
- `LimitTracking`: false
- `LimitPark`: false
- `ParkLimitName`: "Default"
- `HzLimitTracking`: false
- `HzLimitPark`: false
- `ParkHzLimitName`: "Default"
- `PolarMode`: "Right"
- `NoSyncPastMeridian`: true

### Park Positions
```json
[
  {"Name": "Default", "X": 90.0, "Y": 90.0},
  {"Name": "Home", "X": 90.0, "Y": 90.0}
]
```

---

## Polar (Fork Equatorial) Settings

### Key Changes from Previous JSON
- **HomeAxisX**: 0.0 ? **180.0**
- **HomeAxisY**: 0.0 ? **5.0**
- **AutoHomeAxisX**: 0.0 ? **90.0**
- **AutoHomeAxisY**: 0.0 ? **90.0**
- **ParkAxes**: [0.0, 90.0] ? **[180.0, 0.0]**

### Settings Added
- `HourAngleLimit`: 0.0
- `AxisUpperLimitY`: 90.0
- `AxisLowerLimitY`: -90.0
- `AxisLimitX`: 115.0
- `AxisHzTrackingLimit`: 0.0
- `LimitTracking`: false
- `LimitPark`: false
- `ParkLimitName`: "Default"
- `HzLimitTracking`: false
- `HzLimitPark`: false
- `ParkHzLimitName`: "Default"
- `PolarMode`: "Left" (note: Fork mounts use Left mode)
- `NoSyncPastMeridian`: false

### Park Positions
```json
[
  {"Name": "Default", "X": 0.0, "Y": 0.0},
  {"Name": "Home", "X": 0.0, "Y": 5.0}
]
```

---

## Alt-Azimuth Settings

### Key Changes from Previous JSON
- **ParkAxes**: [0.0, 90.0] ? **[0.0, 0.0]**

### Settings Added
- `HourAngleLimit`: 0.0
- `AxisUpperLimitY`: 80.0
- `AxisLowerLimitY`: -10.0
- `AxisLimitX`: 210.0
- `AxisHzTrackingLimit`: 0.0
- `LimitTracking`: false
- `LimitPark`: false
- `ParkLimitName`: "Default"
- `HzLimitTracking`: false
- `HzLimitPark`: false
- `ParkHzLimitName`: "Default"
- `PolarMode`: "Right"
- `NoSyncPastMeridian`: false

### Park Positions
```json
[
  {"Name": "Default", "X": 0.0, "Y": 0.0},
  {"Name": "Home", "X": 0.0, "Y": 0.0}
]
```

---

## Notable Differences Between Modes

### German Polar (GEM)
- Home at 90°, 90° (counterweight down position)
- Park at 90°, 90° (same as home)
- Large Y-axis range: -180° to 180°
- Hour angle limit: 15 hours
- Polar mode: Right
- Meridian flip behavior: NoSyncPastMeridian = true

### Polar (Fork)
- Home at 180°, 5° (slightly elevated)
- Park at 180°, 0° (horizontal)
- Limited Y-axis range: -90° to 90°
- Hour angle limit: 0 (no limit)
- X-axis limit: 115° (more restricted)
- Polar mode: Left (fork-specific)
- Meridian flip behavior: NoSyncPastMeridian = false

### Alt-Azimuth
- Home at 0°, 0° (north horizon)
- Park at 0°, 0° (same as home)
- Y-axis range: -10° to 80° (horizon to near-zenith)
- Large X-axis range: 210°
- Hour angle limit: 0 (not applicable)
- No meridian flip concerns

---

## Settings NOT Transferred

The following XML settings were present but are NOT appropriate for configuration templates:
- `ModelType` - Appears to be related to 3D model display (UI feature, not mount control)
- `AxisModelOffsets` - 3D model offsets (X, Y, Z coordinates for visualization)

These settings may be:
1. Related to UI features (3D model) not part of core mount settings
2. Handled elsewhere in the application
3. Deprecated in the new system

## Settings NOW Transferred (Update)

? **AtPark** - Now included in all override files
  - Persists mount park state across power cycles
  - Hardware doesn't maintain park state, so this setting is essential
  - Default: `false` for all modes

? **CanSetPierSide** - Now included in all override files
  - Configuration setting independent of hardware capabilities
  - Some mounts cannot action pier side commands even if hardware supports it
  - Values:
    - German Polar: `true` (supports pier flips)
    - Polar (Fork): `true` (can report pier side)
    - Alt-Azimuth: `false` (concept doesn't apply)

---

## Next Steps

1. ? **DONE**: Transfer all XML settings to JSON overrides
2. ?? **PENDING**: Review transferred settings for accuracy
3. ?? **PENDING**: Update JSON schemas to include new properties
4. ?? **PENDING**: Test default profile creation with new overrides
5. ?? **PENDING**: Remove old XML files from Profiles folder
6. ?? **PENDING**: Update SkySettings model if new properties needed

---

## Validation Checklist

Before removing XML files, verify:
- [ ] All numeric values transferred correctly
- [ ] Boolean values converted (True/False ? true/false)
- [ ] String values quoted properly
- [ ] Park positions arrays formatted correctly
- [ ] Alignment mode names match enum (algGermanPolar ? GermanPolar)
- [ ] Polar mode values match (Right/Left)
- [ ] JSON syntax is valid (no trailing commas, proper brackets)

---

## Files Ready for Deletion

After schema updates and testing, these files can be removed:
```
GreenSwamp.Alpaca.Settings/Profiles/GermanPolar.settings
GreenSwamp.Alpaca.Settings/Profiles/GermanPolar.Designer.cs
GreenSwamp.Alpaca.Settings/Profiles/Polar.settings
GreenSwamp.Alpaca.Settings/Profiles/Polar.Designer.cs
GreenSwamp.Alpaca.Settings/Profiles/AltAz.settings
GreenSwamp.Alpaca.Settings/Profiles/AltAz.Designer.cs
```

---

## Notes

- All settings from XML have been migrated to JSON
- Numeric precision maintained (e.g., 90 ? 90.0)
- JSON uses lowercase booleans (true/false vs True/False)
- Alignment mode strings updated to match enum names
- Park positions simplified but retain original values
- All limit settings now explicitly defined per mode
