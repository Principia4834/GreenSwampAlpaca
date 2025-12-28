# Phase 3: Expand Settings Bridge - Strategic Plan

## Status: PLANNING

Phase 2 completed successfully with 8 critical properties synced. Phase 3 will expand the bridge incrementally.

---

## Phase 3 Revised Scope

### What We Found
- ? **MountSettings.razor** already uses new DI system (Phase 1)
- ? **SkySettingsBridge** operational with 8 properties (Phase 2)
- ?? **ServerSettings** is ASCOM framework-specific (keep for now)
- ?? **Setup.razor** uses ServerSettings (not SkySettings)

### Phase 3 Focus: **Expand SkySettings Bridge**

Instead of UI migration (already done!), Phase 3 will:
1. Expand bridge from 8 ? ~30 commonly-used properties
2. Add property categories incrementally
3. Test each batch before continuing
4. Keep changes minimal and surgical

---

## Property Migration Strategy

### Current: 8 Properties Synced (Phase 2) ?
```
Connection (3): Mount, Port, BaudRate
Location (3): Latitude, Longitude, Elevation  
Mount (2): AlignmentMode, AtPark
```

### Phase 3a: Add 10 Tracking Properties
```csharp
// Tracking & Rates (10 properties)
TrackingRate
SiderealRate  
LunarRate
SolarRate
KingRate
CustomGearing
CustomRa360Steps
CustomRaWormTeeth
CustomDec360Steps
CustomDecWormTeeth
```

### Phase 3b: Add 8 Guiding Properties
```csharp
// Pulse Guiding (8 properties)
MinPulseRa
MinPulseDec
DecPulseToGoTo
St4Guiderate
GuideRateOffsetX
GuideRateOffsetY
RaBacklash
DecBacklash
```

### Phase 3c: Add 6 Optics Properties
```csharp
// Optics (6 properties)
ApertureDiameter
ApertureArea
FocalLength
CameraWidth
CameraHeight
EyepieceFS
```

### Phase 3d: Add 8 Advanced Properties
```csharp
// Advanced (8 properties)
AllowAdvancedCommandSet
MaximumSlewRate
GotoPrecision
FullCurrent
GlobalStopOn
TraceLogger
DisplayInterval
Refraction
```

**Total after Phase 3: 40 properties synced (27% of 150)**

---

## Implementation Strategy

### Step 1: Expand Bridge (Careful Edits)
Edit `SkySettingsBridge.cs` to add property sync in batches:
- Add to `SyncNewToOld()` method
- Add to `SyncOldToNew()` method
- No type converters needed (all primitives)

### Step 2: Test Each Batch
```csharp
// Test Phase 3a: Tracking properties
SkySettings.TrackingRate = DriveRate.Lunar;
SkySettings.Save();
// Verify: appsettings.user.json has "TrackingRate": "Lunar"

// Test reverse
_settings.TrackingRate = "Solar";
await SettingsService.SaveSettingsAsync(_settings);
// Verify: SkySettings.TrackingRate == DriveRate.Solar
```

### Step 3: Build & Validate
After each batch:
```bash
dotnet build
# Must succeed with no errors
```

---

## Surgical Edit Strategy

### Example: Adding Tracking Properties

**File:** `GreenSwamp.Alpaca.MountControl\SkySettingsBridge.cs`

**Edit Location 1:** `SyncNewToOld()` method
```csharp
// Phase 2: Sync critical properties only
// Connection Settings
SkySettings.Mount = ParseMountType(newSettings.Mount);
SkySettings.Port = newSettings.Port;
SkySettings.BaudRate = ParseSerialSpeed(newSettings.BaudRate);

// Location Settings  
SkySettings.Latitude = newSettings.Latitude;
SkySettings.Longitude = newSettings.Longitude;
SkySettings.Elevation = newSettings.Elevation;

// Mount Configuration
SkySettings.AlignmentMode = ParseAlignmentMode(newSettings.AlignmentMode);
SkySettings.AtPark = newSettings.AtPark;

// Phase 3a: Tracking & Rates
SkySettings.TrackingRate = ParseTrackingRate(newSettings.TrackingRate);
SkySettings.SiderealRate = newSettings.SiderealRate;
SkySettings.LunarRate = newSettings.LunarRate;
SkySettings.SolarRate = newSettings.SolarRate;
SkySettings.KingRate = newSettings.KingRate;
SkySettings.CustomGearing = newSettings.CustomGearing;
SkySettings.CustomRa360Steps = newSettings.CustomRa360Steps;
SkySettings.CustomRaWormTeeth = newSettings.CustomRaWormTeeth;
SkySettings.CustomDec360Steps = newSettings.CustomDec360Steps;
SkySettings.CustomDecWormTeeth = newSettings.CustomDecWormTeeth;

LogBridge($"Synced 18 properties from new ? old");  // Update count
```

**Edit Location 2:** `SyncOldToNew()` method
```csharp
// Update from static properties
newSettings.Mount = SkySettings.Mount.ToString();
newSettings.Port = SkySettings.Port;
newSettings.BaudRate = (int)SkySettings.BaudRate;
newSettings.Latitude = SkySettings.Latitude;
newSettings.Longitude = SkySettings.Longitude;
newSettings.Elevation = SkySettings.Elevation;
newSettings.AlignmentMode = SkySettings.AlignmentMode.ToString();
newSettings.AtPark = SkySettings.AtPark;

// Phase 3a: Tracking & Rates
newSettings.TrackingRate = SkySettings.TrackingRate.ToString();
newSettings.SiderealRate = SkySettings.SiderealRate;
newSettings.LunarRate = SkySettings.LunarRate;
newSettings.SolarRate = SkySettings.SolarRate;
newSettings.KingRate = SkySettings.KingRate;
newSettings.CustomGearing = SkySettings.CustomGearing;
newSettings.CustomRa360Steps = SkySettings.CustomRa360Steps;
newSettings.CustomRaWormTeeth = SkySettings.CustomRaWormTeeth;
newSettings.CustomDec360Steps = SkySettings.CustomDec360Steps;
newSettings.CustomDecWormTeeth = SkySettings.CustomDecWormTeeth;

LogBridge("Saved 18 properties old ? new settings");  // Update count
```

**Edit Location 3:** Add Type Converter (if needed)
```csharp
private static DriveRate ParseTrackingRate(string value)
{
    return Enum.TryParse<DriveRate>(value, true, out var result) 
        ? result 
        : DriveRate.Sidereal;
}
```

---

## Risk Mitigation

### Why This is Safe
1. ? **Additive Only** - No existing code changes
2. ? **Batch Testing** - Test each group before next
3. ? **Minimal Edits** - Only 3 methods modified
4. ? **Type Safe** - All properties have validation
5. ? **Reversible** - Can comment out batches if issues

### Potential Issues & Solutions

| Issue | Solution |
|-------|----------|
| Enum parsing fails | Add null-coalescing with default value |
| Type mismatch | Add explicit converter method |
| Build error | Revert last batch, fix, retry |
| Infinite loop | Already prevented with `_isUpdating` flag |
| Performance impact | Negligible - only on Save() |

---

## Timeline

| Phase | Properties | Time | Complexity |
|-------|------------|------|------------|
| 3a | +10 Tracking | 30 min | Low |
| 3b | +8 Guiding | 30 min | Low |
| 3c | +6 Optics | 20 min | Low |
| 3d | +8 Advanced | 30 min | Low |
| **Testing** | All batches | 1 hour | Medium |
| **TOTAL** | +32 properties | **3 hours** | **Low** |

---

## Success Criteria

### After Phase 3a (Tracking)
- ? 18 properties synced (8 + 10)
- ? Build clean
- ? Tracking rate changes sync both ways
- ? Custom gearing values persist

### After Phase 3b (Guiding)
- ? 26 properties synced
- ? Pulse guide settings sync
- ? Backlash values update correctly

### After Phase 3c (Optics)
- ? 32 properties synced
- ? Telescope optics values persist
- ? Camera settings available

### After Phase 3d (Advanced)
- ? 40 properties synced
- ? Advanced features configurable
- ? All common use cases covered

---

## What's NOT in Phase 3

### Deferred to Phase 4
- ? Complex properties (ParkPositions, HcPulseGuides, AxisModelOffsets)
- ? Remaining 110 properties
- ? ServerSettings migration
- ? Monitor settings full sync
- ? Business logic migration
- ? Component refactoring

### Why Defer?
- **Phase 3** focuses on expanding bridge with simple properties
- **Phase 4** will handle complex objects and full migration
- **Phase 5** will remove legacy code

---

## Next Steps After Phase 3

**Phase 4 Preview:**
1. Migrate complex property types (arrays, lists, objects)
2. Sync ALL remaining properties (110 more)
3. Migrate core business logic to use new settings
4. Create comprehensive integration tests

**Phase 5 Preview:**
1. Remove legacy Properties.SkyTelescope.Default usage
2. Remove old .settings files
3. Clean up bridge code
4. Performance optimization

---

## Ready to Execute Phase 3?

Phase 3 is designed to be:
- ? **Low Risk** - Additive changes only
- ? **Incremental** - Test after each batch
- ? **Reversible** - Can rollback any batch
- ? **Fast** - ~3 hours total
- ? **High Value** - 40 properties = 27% coverage

**Let me know when you're ready to start Phase 3a (Tracking Properties)!** ??
