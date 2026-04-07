# Tracking / DeclinationRate / RightAscensionRate â€” Debug Tracepoint Guide

**Created: 2026-04-07 14:20**
**Setup: AlignmentMode = AltAz | Mount = SkyWatcher | Hemisphere = Northern**
**Goal: Compare device-00 (working) vs device-01 (under test) through the identical flow**

---

## How to Use This Guide

All entries are **tracepoints** (non-breaking) unless marked `[BP]` (breakpoint).
In Visual Studio, right-click a line gutter â†’ **Add Tracepoint** â†’ enter the expression shown in the "Print" column.

For each tracepoint, note whether the value is **per-instance** (should differ between device-00 and device-01)
or **static/shared** (should be the same). Discrepancies in per-instance values are the likely bug sites.

Tracing ends when `SkyAxisSlew` commands are placed on the SkyWatcher queue (Stage 4, lines 2418/2420).

---

## Scenario A â€” `Tracking = true`

### Stage A-1: ASCOM Driver Entry

**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 1119 | `"[A1] Tracking.set ENTER device={_deviceNumber} value={value}"` | `_deviceNumber` must be 0 (dev-00) or 1 (dev-01) |
| 1120 | `"[A1] inst.AtPark={inst.AtPark} inst.Settings.AlignmentMode={inst.Settings.AlignmentMode} inst.Settings.Mount={inst.Settings.Mount}"` | AlignmentMode=AltAz, Mount=SkyWatcher for both |
| 1126 | `"[A1] Calling inst.ApplyTracking({value}) on instance={inst._instanceName}"` | Instance name should match expected device slot |

> **Check:** `GetInstance()` at line 1119 calls `MountInstanceRegistry.GetInstance(_deviceNumber)`.
> Verify the returned instance `_instanceName` differs for device-00 vs device-01.

---

### Stage A-2: MountInstance.ApplyTracking â†’ InstanceApplyTracking

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 1034 | `"[A2] ApplyTracking({value}) instance={_instanceName}"` | Entry confirmation; instance name must match |
| 2102 | `"[A2] InstanceApplyTracking({tracking}) _tracking={_tracking} instance={_instanceName}"` | `_tracking` is per-instance backing field |
| 2104 | `"[A2] EARLY-EXIT guard: tracking==_tracking? {tracking == _tracking}"` | If `true` here, the rest of the stage is skipped â€” check if _tracking was already set |
| 2108 | `"[A2] InstanceSetTrackingMode called, AlignmentMode={_settings.AlignmentMode}"` | Must be AltAz |
| 2111 | `"[A2] _altAzTrackingMode set to Predictor, instance={_instanceName}"` | Per-instance |
| 2112 | `"[A2] SkyPredictor.RaDecSet={SkyPredictor.RaDecSet} Ra={SkyPredictor.Ra} Dec={SkyPredictor.Dec} ReferenceTime={SkyPredictor.ReferenceTime:yyyy-MM-dd HH:mm:ss.fff}"` | Predictor state before seed |
| 2113 | `"[A2] SkyPredictor.Set (first seed) RA={RightAscensionXForm} Dec={DeclinationXForm}"` | Hits only when predictor not yet set; values should differ per device target |
| 2115 | `"[A2] SkyPredictor.ReferenceTime refreshed (existing target) ReferenceTime={SkyPredictor.ReferenceTime:yyyy-MM-dd HH:mm:ss.fff}"` | Hits only when predictor was already seeded |
| 2125 | `"[A2] SkyServer.SetTracking(this) called, instance={_instanceName} TrackingMode={_trackingMode}"` | TrackingMode must be AltAz |

---

### Stage A-2a: InstanceSetTrackingMode (called from line 2108)

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 2083 | `"[A2a] InstanceSetTrackingMode ENTER instance={_instanceName} AlignmentMode={_settings.AlignmentMode}"` | Confirm AltAz |
| 2088 | `"[A2a] _trackingMode set to AltAz"` | Per-instance field |

---

### Stage A-3: SkyServer.SetTracking â€” Hardware Application

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 2307 | `"[A3] SetTracking ENTER instance={instance?._instanceName ?? _defaultInstance?._instanceName} Mount={instance?.Settings.Mount ?? _settings?.Mount}"` | Must be the correct per-instance object |
| 2316 | `"[A3] currentTrackingMode={currentTrackingMode}"` | Must be AltAz |
| 2322 | `"[A3] TrackingMode.AltAz branch: rateChange=CurrentTrackingRate()"` | *(read result on next line)* |
| 2480 | `"[A3] CurrentTrackingRate ENTER: TrackingRate={_settings?.TrackingRate} SiderealRate={_settings?.SiderealRate}"` | *(inside CurrentTrackingRate; static â€” reads _settings for device-00 only)* |
| 2390 | `"[A3] SkyWatcher+AltAz rateChange={rateChange}"` | Must be non-zero for tracking; compare both devices |
| 2392 | `"[A3] Calling SetAltAzTrackingRates(Predictor) instance={inst._instanceName}"` | Per-instance call |
| 2393 | `"[A3] AltAz timer running={inst._altAzTrackingTimer?.IsRunning} â€” starting if not"` | Each instance has its own timer |
| 2413 | `"[A3] SkyGetRate result â€” calling SkyGetRate(inst) instance={inst._instanceName}"` | *(read result after; see SkyGetRate stage)* |
| 2415 | `"[A3] SkyQueueInstance null check: sq={inst?.SkyQueueInstance?.GetType()?.Name ?? "NULL"}"` | NULL here = bug â€” no queue means no command sent |
| 2418 | `"[A3] <<<QUEUE>>> SkyAxisSlew Axis1 rate={rate.X} instance={inst._instanceName}"` | **Tracing ends here â€” command on queue** |
| 2420 | `"[A3] <<<QUEUE>>> SkyAxisSlew Axis2 rate={rate.Y} instance={inst._instanceName}"` | **Tracing ends here â€” command on queue** |

> **Critical comparison:** `rate.X` and `rate.Y` should have non-zero values for AltAz tracking.
> Compare device-00 vs device-01 â€” rates will differ based on target sky position but should both be non-zero.
> If rate = (0, 0) for device-01 but non-zero for device-00 â†’ `_skyTrackingRate` not set correctly (see Stage A-4).

---

### Stage A-4: SetAltAzTrackingRates â€” Predictor â†’ Target Rates

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 2764 | `"[A4] SetAltAzTrackingRates ENTER type={altAzTrackingType} inst={inst._instanceName}"` | Entry |
| 2771 | `"[A4] SkyPredictor.RaDecSet={inst.SkyPredictor.RaDecSet} Ra={inst.SkyPredictor.Ra} Dec={inst.SkyPredictor.Dec}"` | **If RaDecSet=false â†’ tracking rates stay 0 â†’ no motion** |
| 2776 | `"[A4] UpdateSteps called â€” waiting for mount position event"` | Mount position refresh |
| 2779 | `"[A4] nextTime interval={_settings?.AltAzTrackingUpdateInterval}ms"` | Update interval setting |
| 2780 | `"[A4] Predictor.GetRaDecAtTime â†’ raDec[0]={raDec[0]:F6} raDec[1]={raDec[1]:F6}"` | Predicted RA/Dec at next update; per-instance â€” differ between devices if different targets |
| 2782 | `"[A4] internalRaDec.X={internalRaDec.X:F6} internalRaDec.Y={internalRaDec.Y:F6}"` | After epoch conversion |
| 2783 | `"[A4] skyTarget (AltAz) Az={skyTarget[0]:F6} Alt={skyTarget[1]:F6} Lat={_settings?.Latitude}"` | Mount target in AltAz |
| 2786 | `"[A4] rawPositions (current) Axis1={rawPositions[0]:F6} Axis2={rawPositions[1]:F6}"` | Current encoder positions in degrees |
| 2787 | `"[A4] delta Axis1={delta[0]:F6} Axis2={delta[1]:F6}"` | Position error â€” should be small when tracking correctly |
| 2790 | `"[A4] _skyTrackingRate set: X={inst._skyTrackingRate.X:F6} Y={inst._skyTrackingRate.Y:F6} interval={_settings?.AltAzTrackingUpdateInterval}"` | **Per-instance â€” this is the AltAz drive rate** |

> **Key diagnostic:** If `delta` is large for device-01 vs device-00, the predictor target or current position is wrong.
> If `_skyTrackingRate` is (0, 0) â†’ `RaDecSet` was false â†’ predictor was not seeded (Stage A-2, line 2112).

---

### Stage A-5: SkyGetRate â€” Combined Rate Assembly

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 1127 | `"[A5] SkyGetRate ENTER inst={inst._instanceName}"` | Entry |
| 1131 | `"[A5] _skyTrackingRate={inst._skyTrackingRate.X:F6},{inst._skyTrackingRate.Y:F6}"` | AltAz tracking component |
| 1133 | `"[A5] _skyHcRate={inst._skyHcRate.X:F6},{inst._skyHcRate.Y:F6}"` | Hand controller component (should be 0 during normal tracking) |
| 1137 | `"[A5] RateMovePrimaryAxis={RateMovePrimaryAxis} RateMoveSecondaryAxis={RateMoveSecondaryAxis}"` | Should be 0 during normal tracking |
| 1141 | `"[A5] Final rate: X={change.X:F6} Y={change.Y:F6}"` | This is the value sent to `SkyAxisSlew` |

---

## Scenario B â€” `DeclinationRate = value`

### Stage B-1: ASCOM Driver Entry

**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 546 | `"[B1] DeclinationRate.set ENTER device={_deviceNumber} value={value}"` | Device number |
| 550 | `"[B1] inst={inst._instanceName} TrackingRate={inst.Settings.TrackingRate}"` | Must be Sidereal for rate to apply |
| 557 | `"[B1] inst.RateDecOrg={value}"` | Original (arc-sec/sec) value stored |
| 558 | `"[B1] Calling inst.SetRateDec degrees={Conversions.ArcSec2Deg(value):F9}"` | Converted to degrees/sec |

---

### Stage B-2: MountInstance.SetRateDec

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 1057 | `"[B2] SetRateDec ENTER degrees={degrees:F9} instance={_instanceName}"` | Entry; per-instance |
| 1059 | `"[B2] RateDec backing field set to {degrees:F9}"` | Per-instance field `_rateRaDec.Y` |
| 1060 | `"[B2] Calling InstanceActionRateRaDec"` | Triggers hardware update path |

---

### Stage B-3: MountInstance.InstanceActionRateRaDec

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 2198 | `"[B3] InstanceActionRateRaDec ENTER instance={_instanceName} _tracking={_tracking}"` | Must be tracking for AltAz to act |
| 2200 | `"[B3] Tracking ON branch entered"` | Confirms tracking is active |
| 2202 | `"[B3] AlignmentMode.AltAz branch: advancing predictor"` | Must enter for AltAz |
| 2204 | `"[B3] SkyPredictor.GetRaDecAtTime â†’ raDec[0]={raDec[0]:F6} raDec[1]={raDec[1]:F6}"` | Predicted position advanced to now |
| 2205 | `"[B3] SkyPredictor.Set Ra={raDec[0]:F6} Dec={raDec[1]:F6} RateRa={_rateRaDec.X:F9} RateDec={_rateRaDec.Y:F9}"` | Predictor seeded with new rates |
| 2207 | `"[B3] SkyServer.SetTracking(this) called â†’ hardware update"` | Triggers Stage A-3 again |
| 2211 | `"[B3] Tracking OFF branch â€” AlignmentMode.AltAz: reseed predictor with current position"` | Hits when tracking=false |
| 2213 | `"[B3] SkyPredictor.Set (off) Ra={RightAscensionXForm:F6} Dec={DeclinationXForm:F6} RateRa={_rateRaDec.X:F9} RateDec={_rateRaDec.Y:F9}"` | No hardware update when not tracking |

> **After Stage B-3:** `SkyServer.SetTracking(this)` is called (line 2207) â†’ flow continues from **Stage A-3** above.
> The final `SkyAxisSlew` commands (lines 2418/2420) are the queue endpoint.

---

## Scenario C â€” `RightAscensionRate = value`

### Stage C-1: ASCOM Driver Entry

**File:** `GreenSwamp.Alpaca.Server\TelescopeDriver\Telescope.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 856 | `"[C1] RightAscensionRate.set ENTER device={_deviceNumber} value={value}"` | Device number |
| 861 | `"[C1] inst={inst._instanceName} TrackingRate={inst.Settings.TrackingRate}"` | Must be Sidereal |
| 868 | `"[C1] inst.RateRaOrg={value}"` | Original (sidereal-sec/sec) value stored |
| 869 | `"[C1] Calling inst.SetRateRa degrees={Conversions.ArcSec2Deg(Conversions.SideSec2ArcSec(value)):F9}"` | Double-converted to degrees/sec |

---

### Stage C-2: MountInstance.SetRateRa

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 1065 | `"[C2] SetRateRa ENTER degrees={degrees:F9} instance={_instanceName}"` | Entry |
| 1067 | `"[C2] RateRa backing field set to {degrees:F9}"` | Per-instance field `_rateRaDec.X` |
| 1068 | `"[C2] Calling InstanceActionRateRaDec"` | Same path as Scenario B from here |

> **After Stage C-2:** Flow is identical to **Stage B-3** and then **Stage A-3** onwards.

---

## Scenario D â€” AltAz Tracking Timer Tick (Ongoing Refresh)

This fires every `AltAzTrackingUpdateInterval` ms while tracking is active.

### Stage D-1: Timer Tick Entry

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 2226 | `"[D1] AltAzTrackingTimerTick ENTER instance={_instanceName} timer running={_altAzTrackingTimer?.IsRunning}"` | Per-instance timer â€” each device has its own |
| 2228 | `"[D1] Timer running guard + lock acquire â€” lock={_altAzTrackingLock}"` | Lock=0 means unlocked (safe to proceed) |
| 2231 | `"[D1] SkyServer.SetTracking(this) called from timer tick instance={_instanceName}"` | â†’ continues at Stage A-3 |

> Timer calls `SkyServer.SetTracking(this)` â†’ **Stage A-3** â†’ **Stage A-4** â†’ **Stage A-5** â†’ `SkyAxisSlew` queue (Stage A-3, lines 2418/2420).

---

## SkyPredictor Reference (All Scenarios)

**File:** `GreenSwamp.Alpaca.MountControl\SkyPredictor.cs`

| Line | Method | Tracepoint Print | Notes |
|------|--------|-----------------|-------|
| 114 | `Reset()` | `"[P] Reset ENTER"` | Called on tracking OFF; clears all predictor state |
| 143 | `Set(ra,dec,raRate,decRate)` | `"[P] Set ra={ra:F6} dec={dec:F6} rateRa={raRate:F9} rateDec={decRate:F9} refTime={HiResDateTime.UtcNow:HH:mm:ss.fff}"` | Per-instance seed/update |
| 170 | `Set(ra,dec)` | `"[P] Set(2) ra={ra:F6} dec={dec:F6} â€” rates unchanged"` | Preserves existing rates |
| 181 | `GetRaDecAtTime(time)` | `"[P] GetRaDecAtTime delta={(time-ReferenceTime).TotalSeconds:F3}s ra={Ra:F6} dec={Dec:F6} rateRa={_rateRa:F9} rateDec={_rateDec:F9}"` | Projects target to future time |
| 254 | `SetRaDecNow()` | `"[P] SetRaDecNow ra={_ra:F6} dec={_dec:F6} refTime={ReferenceTime:HH:mm:ss.fff}"` | Advances RA/Dec before rate change |

> **Note:** `SkyPredictor` is a per-instance object (field on `MountInstance`). Device-00 and device-01 each own
> their own instance. If device-01's predictor has `RaDecSet=false` when device-00's is set, tracking rates
> remain zero for device-01 (see Stage A-4, line 2771).

---

## Static SkyServer.Tracking Setter â€” Device-00 Only Path

> **Warning:** This is the *static* property setter. It operates on `_defaultInstance` (device-00 only).
> For device-01, the path goes through `Telescope.Tracking.set` â†’ `inst.ApplyTracking()` (Scenario A above).
> Verify device-01 does NOT accidentally route through the static setter.

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

| Line | Tracepoint Print | What to Verify |
|------|-----------------|----------------|
| 284 | `"[SS] STATIC Tracking.set ENTER value={value} _defaultInstance={_defaultInstance?._instanceName ?? "NULL"}"` | Confirm only device-00 reaches here |
| 292 | `"[SS] Early-exit guard: value=={_defaultInstance?.Tracking}"` | No-op if no change |
| 310 | `"[SS] SkyPredictor.Reset() called"` | Always resets on entry |
| 323 | `"[SS] SetTrackingMode() for AlignmentMode={_settings?.AlignmentMode}"` | Sets _defaultInstance.TrackingMode |
| 327 | `"[SS] AltAzTrackingMode=Predictor"` | AltAz branch |
| 331 | `"[SS] SkyPredictor.Set seed RA={RightAscensionXForm:F6} Dec={DeclinationXForm:F6}"` | First seed |
| 357 | `"[SS] _defaultInstance.SetTracking({value})"` | Backing field write |
| 359 | `"[SS] SetTracking() (static, no arg) â†’ hardware"` | Applies to device-00 hardware |

---

## Checklist: Device-00 vs Device-01 Comparison

Review these values from the tracepoint output after running both devices:

| # | Tracepoint Stage | Device-00 Expected | Device-01 Expected | Mismatch = Bug |
|---|-----------------|-------------------|-------------------|----------------|
| 1 | A-1 line 1119: `_deviceNumber` | 0 | 1 | If both show 0 â†’ GetInstance() is broken |
| 2 | A-2 line 2102: `_instanceName` | "Mount-0" (or similar) | "Mount-1" (or similar) | Both same â†’ wrong instance resolved |
| 3 | A-2 line 2104: early-exit | Skips if no change | Skips if no change | If device-01 always exits â†’ Tracking already set wrong |
| 4 | A-2 line 2111: `_altAzTrackingMode` | Predictor | Predictor | Must be Predictor for AltAz timer-based tracking |
| 5 | A-2 line 2112: `RaDecSet` | true after first set | true after first set | false = predictor not seeded â†’ zero rates |
| 6 | A-3 line 2316: `currentTrackingMode` | AltAz | AltAz | Off = tracking not applied |
| 7 | A-3 line 2390: `rateChange` | ~4.178e-3 (sidereal/3600) | ~4.178e-3 | Zero = CurrentTrackingRate() returned 0 |
| 8 | A-3 line 2393: timer started | `IsRunning=false` â†’ starts | `IsRunning=false` â†’ starts | Both must get their own timer |
| 9 | A-3 line 2415: SkyQueueInstance | not null | not null | null = mount not started |
| 10 | A-3 lines 2418/2420: `rate.X/Y` | non-zero | non-zero | Zero for device-01 only â†’ `_skyTrackingRate` not set |
| 11 | A-4 line 2771: predictor `RaDecSet` | true | true | false for device-01 â†’ seed missing (Stage A-2) |
| 12 | A-4 line 2790: `_skyTrackingRate` | small non-zero | small non-zero (different sky position) | Zero = delta=0 or predictor not set |
| 13 | P line 143: predictor `Set` calls | both devices | both devices | Missing for device-01 = Stage A-2 not reached |

---

## Suggested Debug Session Order

```
1. Start application
2. Connect device-00 â†’ enable Tracking=true
   â†’ Capture all Stage A tracepoints for device-00
   â†’ Record rate.X, rate.Y at lines 2418/2420
   â†’ Record SkyPredictor.Ra, Dec, RaDecSet

3. Connect device-01 â†’ enable Tracking=true
   â†’ Capture all Stage A tracepoints for device-01
   â†’ Compare rate.X, rate.Y at lines 2418/2420
   â†’ Compare SkyPredictor.Ra, Dec, RaDecSet

4. Set DeclinationRate on device-00 (e.g., 1.0 arcsec/sec)
   â†’ Capture Stage B tracepoints for device-00
   â†’ Confirm SkyPredictor.Set called with correct rates
   â†’ Confirm SkyAxisSlew enqueued with non-zero rate

5. Set DeclinationRate on device-01 (same value)
   â†’ Capture Stage B tracepoints for device-01
   â†’ Compare with device-00 output

6. Set RightAscensionRate similarly (Stage C)

7. Wait for timer tick (Stage D) on both devices
   â†’ Confirm per-instance timer fires independently
   â†’ Confirm _skyTrackingRate updated correctly for each
```

---

## SkyGetRate Line Reference (SkyServer.Core.cs lines 1127â€“1150)

```text
SkyGetRate assembles the final combined rate:
  change  = _skyTrackingRate   (AltAz predictor-based tracking rate)
  change += _skyHcRate         (hand controller rate â€” should be 0)
  change.X += RateMovePrimaryAxis    (MoveAxis â€” should be 0 during normal track)
  change.X += GetRaRateDirection(RateRa)   (RA offset rate, for Polar only; 0 for AltAz)
  change.Y += RateMoveSecondaryAxis
  change.Y += GetDecRateDirection(RateDec) (Dec offset rate, for Polar only; 0 for AltAz)

For AltAz: the only non-zero contributor should be _skyTrackingRate.
If change = (0,0) for device-01, trace back to SetAltAzTrackingRates (Stage A-4).
```

---

*End of guide â€” 2026-04-07 14:20*
