# Phase 3b Complete ? - Guiding Properties Added to Bridge

## Status: Phase 3b COMPLETE ?

Successfully added 8 guiding properties to the bidirectional settings bridge.

---

## What Was Done

### Files Modified (1 file)

**GreenSwamp.Alpaca.MountControl\SkySettingsBridge.cs** - 2 surgical edits
1. ? **SyncNewToOld()** - Added 8 guiding property syncs (new ? old)
2. ? **SyncOldToNew()** - Added 8 guiding property syncs (old ? new)

### Properties Added (8 Total)

#### Pulse Guiding
1. **MinPulseRa** - int (minimum pulse duration RA in ms)
2. **MinPulseDec** - int (minimum pulse duration Dec in ms)
3. **DecPulseToGoTo** - bool (use GOTO for Dec pulses)
4. **St4Guiderate** - int (ST4 guide rate 1-8)

#### Guide Rate Offsets
5. **GuideRateOffsetX** - double (X-axis guide rate offset)
6. **GuideRateOffsetY** - double (Y-axis guide rate offset)

#### Backlash Compensation
7. **RaBacklash** - int (RA backlash in steps)
8. **DecBacklash** - int (Dec backlash in steps)

---

## Edit Details

### Edit 1: SyncNewToOld() Method
**Lines Added:** 8 property assignments + 1 log update

```csharp
// Phase 3b: Guiding (8 properties)
SkySettings.MinPulseRa = newSettings.MinPulseRa;
SkySettings.MinPulseDec = newSettings.MinPulseDec;
SkySettings.DecPulseToGoTo = newSettings.DecPulseToGoTo;
SkySettings.St4GuideRate = newSettings.St4Guiderate;
SkySettings.GuideRateOffsetX = newSettings.GuideRateOffsetX;
SkySettings.GuideRateOffsetY = newSettings.GuideRateOffsetY;
SkySettings.RaBacklash = newSettings.RaBacklash;
SkySettings.DecBacklash = newSettings.DecBacklash;

LogBridge($"Synced 26 properties from new ? old (Phase 3b)");
```

### Edit 2: SyncOldToNew() Method
**Lines Added:** 8 property assignments + 1 log update

```csharp
// Phase 3b: Guiding (8 properties)
newSettings.MinPulseRa = SkySettings.MinPulseRa;
newSettings.MinPulseDec = SkySettings.MinPulseDec;
newSettings.DecPulseToGoTo = SkySettings.DecPulseToGoTo;
newSettings.St4Guiderate = SkySettings.St4GuideRate;
newSettings.GuideRateOffsetX = SkySettings.GuideRateOffsetX;
newSettings.GuideRateOffsetY = SkySettings.GuideRateOffsetY;
newSettings.RaBacklash = SkySettings.RaBacklash;
newSettings.DecBacklash = SkySettings.DecBacklash;

LogBridge("Saved 26 properties old ? new settings (Phase 3b)");
```

**Note:** No new type converters needed - all properties are primitive types (int, bool, double)

---

## Current Bridge Status

### Total Properties Synced: **26 properties** (17% of 150)

| Category | Count | Properties |
|----------|-------|------------|
| **Connection** | 3 | Mount, Port, BaudRate |
| **Location** | 3 | Latitude, Longitude, Elevation |
| **Mount Config** | 2 | AlignmentMode, AtPark |
| **Tracking** | 5 | TrackingRate, Sidereal/Lunar/Solar/King rates |
| **Custom Gearing** | 5 | CustomGearing, Ra/Dec 360Steps, Ra/Dec WormTeeth |
| **Pulse Guiding** ? | 4 | MinPulse Ra/Dec, DecPulseToGoTo, St4GuideRate |
| **Guide Offsets** ? | 2 | GuideRateOffset X/Y |
| **Backlash** ? | 2 | RaBacklash, DecBacklash |
| **TOTAL** | **26** | **Phase 2: 8 + Phase 3a: 10 + Phase 3b: 8** |

---

## Build Status

? **Build Successful** - No errors, no warnings  
? **All Properties Primitive Types** - No type converters needed  
? **Phase 2 & 3a Intact** - Previous 18 properties still syncing  
? **Ready for Testing** - Bridge operational with expanded scope

---

## Testing Phase 3b

### Test 1: Pulse Guide Settings (Old ? New)
```csharp
// In existing code
SkySettings.MinPulseRa = 50;
SkySettings.MinPulseDec = 75;
SkySettings.DecPulseToGoTo = true;
SkySettings.St4GuideRate = 5;
SkySettings.Save();

// Verify in appsettings.user.json:
// "MinPulseRa": 50,
// "MinPulseDec": 75,
// "DecPulseToGoTo": true,
// "St4Guiderate": 5
```

### Test 2: Guide Rate Offsets (New ? Old)
```razor
// In MountSettings.razor (Guiding tab)
_settings.GuideRateOffsetX = 0.05;
_settings.GuideRateOffsetY = -0.03;
await SettingsService.SaveSettingsAsync(_settings);

// Verify:
// SkySettings.GuideRateOffsetX == 0.05
// SkySettings.GuideRateOffsetY == -0.03
```

### Test 3: Backlash Compensation
```csharp
// Set backlash values
SkySettings.RaBacklash = 120;
SkySettings.DecBacklash = 80;
SkySettings.Save();

// Verify both directions:
// 1. appsettings.user.json updated
// 2. UI shows correct values in Guiding tab
```

### Test 4: Pulse Guide UI Integration
1. Open MountSettings.razor
2. Navigate to "Guiding" tab
3. Change MinPulseRa to 100ms
4. Change MinPulseDec to 125ms
5. Enable "Dec Pulse uses GOTO"
6. Set ST4 Guide Rate to 6
7. Save settings
8. Verify: All values in `SkySettings` match UI

---

## Verification Checklist

- [x] Both edits applied successfully
- [x] Build succeeds with no errors
- [x] Build succeeds with no warnings
- [x] No new type converters needed (all primitive types)
- [x] SyncNewToOld() updated with 8 properties
- [x] SyncOldToNew() updated with 8 properties
- [x] Log messages updated to show "26 properties"
- [x] Phase 2 & 3a properties still functional
- [x] Settings model has all guiding properties
- [x] MountSettings.razor UI supports guiding settings

---

## Monitor Log Output

When Phase 3b bridge runs, you'll see:

```
SkySettingsBridge: Bridge initialized and synced
SkySettingsBridge: Synced 26 properties from new ? old (Phase 3b)
```

When settings are saved:

```
SkySettingsBridge: Saved 26 properties old ? new settings (Phase 3b)
```

---

## Property Usage in Codebase

### Pulse Guide Properties Used In:
- **Telescope.cs** - `PulseGuide()` method
  ```csharp
  CheckCapability(SkySettings.CanPulseGuide, "PulseGuide");
  CheckRange(Duration, 0, 30000, "PulseGuide", "Duration");
  ```

- **SkyServer.cs** - Pulse guide implementation
  - Uses `MinPulseRa` and `MinPulseDec` to validate minimum durations
  - Uses `DecPulseToGoTo` to determine Dec pulse behavior
  - Uses `St4GuideRate` for ST4 port guiding

### Backlash Properties Used In:
- **SkyWatcher.cs** - `AxisPulse()` method
  ```csharp
  public void AxisPulse(Axis axis, double guideRate, int duration, 
                        int backlashSteps, CancellationToken token)
  ```
  - `RaBacklash` passed as `backlashSteps` for RA axis
  - `DecBacklash` passed as `backlashSteps` for Dec axis

### Guide Rate Offsets Used In:
- **SkyServer.cs** - `SetGuideRates()` method
  - Applies `GuideRateOffsetX` to RA guide rate
  - Applies `GuideRateOffsetY` to Dec guide rate

---

## Next Steps - Phase 3c

**Ready to proceed:** Phase 3c - Add 6 Optics Properties

### Properties to Add (Phase 3c)
1. ApertureDiameter
2. ApertureArea
3. FocalLength
4. CameraWidth
5. CameraHeight
6. EyepieceFS

**After Phase 3c: 32 properties synced (21% of 150)**

---

## Phase 3 Progress Summary

| Sub-Phase | Properties | Total | Coverage | Status |
|-----------|-----------|-------|----------|--------|
| Phase 3a | +10 Tracking | 18 | 12% | ? Complete |
| **Phase 3b** | **+8 Guiding** | **26** | **17%** | ? **Complete** |
| Phase 3c | +6 Optics | 32 | 21% | ?? Next |
| Phase 3d | +8 Advanced | 40 | 27% | ?? Planned |

---

## Summary

? **Phase 3b Complete!**
- **Time Taken:** ~20 minutes
- **Lines Added:** ~18 lines total
- **Properties Added:** 8 (pulse guiding + backlash)
- **Total Synced:** 26 properties
- **Build Status:** ? Clean
- **Risk Level:** ? Low
- **Strategy:** ? Surgical edits successful

**Properties Now Synced:**
- ? 8 Phase 2 (Connection, Location, Mount)
- ? 10 Phase 3a (Tracking, Custom Gearing)
- ? 8 Phase 3b (Guiding, Backlash)
- **Total: 26/150 (17%)**

**Ready for Phase 3c when you are!** ??

---

## Usage Example

```csharp
// Example: Configure pulse guiding
SkySettings.MinPulseRa = 50;        // Minimum 50ms RA pulses
SkySettings.MinPulseDec = 75;       // Minimum 75ms Dec pulses
SkySettings.St4GuideRate = 5;       // ST4 guide rate (1-8 scale)
SkySettings.DecPulseToGoTo = true;  // Use GOTO for Dec pulses

// Example: Set backlash compensation
SkySettings.RaBacklash = 120;       // 120 steps RA backlash
SkySettings.DecBacklash = 80;       // 80 steps Dec backlash

// Example: Apply guide rate offsets (for autoguiding fine-tuning)
SkySettings.GuideRateOffsetX = 0.05;   // +5% RA guide rate
SkySettings.GuideRateOffsetY = -0.03;  // -3% Dec guide rate

// Save to sync all changes
SkySettings.Save();
// ? Bridge automatically syncs to appsettings.user.json
```
