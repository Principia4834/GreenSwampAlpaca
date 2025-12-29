# Phase A Progress Tracker

**Start Date:** $(Get-Date)  
**Status:** ?? IN PROGRESS - Step A.1

---

## Step A.1: Create Instance Wrapper ? IN PROGRESS

### File to Create
`GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

### Properties Count Analysis
From `SkySettings.cs` analysis:
- **Capabilities (read-only):** 28 properties
- **Server Settings (writable):** 65+ properties  
- **Side-effect properties:** 13 properties
- **Methods:** Load(), Save(), ResetParkPositions()
- **Events:** StaticPropertyChanged

**Total Properties to Wrap:** ~93

### Strategy
Due to file size risk:
1. ? Create stub file with structure
2. ? Add properties in batches of 20
3. ? Test build after each batch
4. ? Verify side effects preserved

### Batches

**Batch 1: Connection & Mount (20 properties)**
- Mount, Port, BaudRate
- HandShake, DataBits, ReadTimeout
- DtrEnable, RtsEnable
- AlignmentMode, EquatorialCoordinateType
- AtPark, TrackingRate
- GpsComPort, GpsBaudRate
- FrontGraphic, HcSpeed, HcMode
- PecMode, PolarMode

**Batch 2: Location & Custom Gearing (15 properties)**
- Latitude ?? SIDE EFFECT
- Longitude, Elevation
- CustomGearing
- CustomRa360Steps, CustomRaWormTeeth
- CustomDec360Steps, CustomDecWormTeeth
- CustomRaTrackingOffset, CustomDecTrackingOffset
- AllowAdvancedCommandSet

**Batch 3: Tracking Rates (8 properties)**
- SiderealRate, LunarRate, SolarRate, KingRate
- AxisTrackingLimit, AxisHzTrackingLimit
- DisplayInterval, AltAzTrackingUpdateInterval

**Batch 4: Guiding (8 properties)** ?? MULTIPLE SIDE EFFECTS
- MinPulseRa ?? SIDE EFFECT
- MinPulseDec ?? SIDE EFFECT
- DecPulseToGoTo ?? SIDE EFFECT
- St4GuideRate ?? SIDE EFFECT
- GuideRateOffsetX ?? SIDE EFFECT  
- GuideRateOffsetY ?? SIDE EFFECT
- RaBacklash, DecBacklash

**Batch 5: Optics & Camera (6 properties)**
- FocalLength, CameraWidth, CameraHeight
- EyepieceFs, ApertureArea, ApertureDiameter

**Batch 6: Advanced Settings (6 properties)** ?? SIDE EFFECTS
- MaxSlewRate ?? SIDE EFFECT
- FullCurrent ?? SIDE EFFECT
- Encoders ?? SIDE EFFECT
- AlternatingPPec ?? SIDE EFFECT
- GlobalStopOn, Refraction

**Batch 7: Home & Park (12 properties)**
- HomeAxisX, HomeAxisY
- AutoHomeAxisX, AutoHomeAxisY
- HomeWarning, HomeDialog
- ParkName, ParkDialog
- ParkAxes, ParkPositions
- LimitPark, ParkLimitName

**Batch 8: Limits (8 properties)**
- HourAngleLimit, AxisLimitX
- AxisUpperLimitY, AxisLowerLimitY
- LimitTracking, SyncLimitOn
- HzLimitTracking, HzLimitPark, ParkHzLimitName

**Batch 9: PEC (8 properties)**
- PecOn, PPecOn
- PecOffSet, PecWormFile, Pec360File
- PolarLedLevel

**Batch 10: Hand Controller (10 properties)**
- HcAntiRa, HcAntiDec
- HcFlipEw, HcFlipNs
- HcPulseGuides
- DisableKeysOnGoTo

**Batch 11: Misc (5 properties)**
- Temperature
- InstrumentDescription, InstrumentName
- AxisModelOffsets
- RaGaugeFlip

**Batch 12: Capabilities (28 read-only properties)**
- All Can* properties (delegates only, no setters)

---

## Risk Mitigation

### Before Each Batch
- [ ] Save git commit
- [ ] Build successfully

### After Each Batch
- [ ] Build successful
- [ ] No errors
- [ ] Git commit with message

### If Build Fails
1. Revert last change
2. Review error
3. Fix issue
4. Try again

---

## Current Status

**Current Batch:** Batch 1 (Connection & Mount)  
**Properties Added:** 0 / 20  
**Build Status:** Not tested  
**Last Commit:** N/A

---

## Next Steps

1. Create stub file with file header and class structure
2. Add Batch 1 properties (20)
3. Build and test
4. Commit
5. Continue with Batch 2...

---

## Notes

- File is 2800+ lines - need to be extra careful
- 13 properties have side effects - must preserve
- Using delegation pattern: all properties call static SkySettings
- Side effects automatically preserved because we call the static setters

