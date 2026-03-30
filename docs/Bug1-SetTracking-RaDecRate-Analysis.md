# Bug 1 — `SetTracking()` Unconditionally Re-Queues `CmdRaDecRate` on Tracking-Off

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`  
**Method:** `SetTracking()` (static), lines ~2377–2500  
**Symptom:** ASCOM conformance `RightAscensionRate Write +3.0` reports ~50 % ΔRA over-report intermittently (~10 % of runs). Actual ≈ +0.0503 h, Expected ≈ +0.0333 h.

---

## 1. Background — The Normal (Passing) Path

### Conversion chain (verified)

```
Telescope.RightAscensionRate.set(3.0)
  → inst.SetRateRa( ArcSec2Deg( SideSec2ArcSec(3.0) ) )
  → SkyServer.RateRa = 0.012534 deg/s
  → ActionRateRaDec()          [only if Tracking == true]
  → SetTracking()
```

`SideSec2ArcSec(3.0) = 3.0 × 1.0027304323 × 15 = 45.123 arcsec/s`  
`ArcSec2Deg(45.123)  = 45.123 / 3600           = 0.012534 deg/s`

So `RateRa = +0.012534 deg/s` in all subsequent code.

### `SetTracking()` — normal (EqN) execution

```csharp
// rateChange calculation
var currentTrackingMode = _defaultInstance?.TrackingMode ?? TrackingMode.Off;
switch (currentTrackingMode)
{
    case TrackingMode.EqN:
        rateChange = CurrentTrackingRate();   // SiderealRate / 3600 ≈ +0.004178 deg/s
        break;
    ...
}

// Simulator / GermanPolar dispatch
var mq = _defaultInstance!.MountQueueInstance!;

if (!MovePrimaryAxisActive)
    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
                                          // → _trackingX = +0.004178 deg/s  ✓

_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1,
        GetRaRateDirection(_defaultInstance?.RateRa ?? 0.0));
                                          // GetRaRateDirection(+0.012534)
                                          //   Northern hemisphere, east=true → return -0.012534
                                          // → _raDecRateX = -0.012534 deg/s  ✓
```

### Physics in `MoveAxes()` — normal path

```
trkX = Tracking(Axis1, s)  = _trackingX  × s = +0.004178 × s
rdrX = RaDecRate(Axis1, s) = _raDecRateX × s = -0.012534 × s
marX = 0  (no MoveAxis active)

changeX = rdrX + trkX = (-0.012534 + 0.004178) × s = -0.008356 × s
```

`DegreesX` decreases at 0.008356 deg/s.

### Resulting RA drift — normal path

```
RA = LST − DegreesX / 15

dRA/dt = dLST/dt − d(DegreesX)/dt / 15
       = +0.000279 − (−0.008356) / 15
       = +0.000279 + 0.000557
       = +0.000836 h/s

ΔRA over the ~40 s measurement window = 0.000836 × 40 = 0.03344 h ≈ Expected 0.0333 h  ✓
```

> **Why ~40 s, not 10 s?**  
> The "Test duration: 10 seconds" label in the conformance output is the *minimum threshold* parameter, not the actual elapsed time. Working backwards: `ΔRA_expected / (dRA/dt_normal) = 0.0333 / 0.000836 ≈ 40 s`. All arithmetic below uses T = 40 s.

---

## 2. The Bug — `TrackingMode.Off` Code Path

### When does `TrackingMode.Off` appear?

Whenever the ASCOM driver property `Tracking` is set to `false`, the mount instance's `TrackingMode` is changed to `TrackingMode.Off` and `SetTracking()` is called. This occurs at several points during the conformance test sequence:

- The conformance checker toggling `Tracking = false` between test sections.
- `CheckAxisLimits()` (called every ~200 ms from `OnUpdateServerEvent`) setting `Tracking = false` if the axis drifts near a meridian or horizon limit.
- Any prior test that ends by disabling tracking.

### `SetTracking()` — Off path

```csharp
var currentTrackingMode = _defaultInstance?.TrackingMode ?? TrackingMode.Off;
switch (currentTrackingMode)
{
    case TrackingMode.Off:
        break;                            // rateChange stays 0  ✓
    ...
}

// Simulator / GermanPolar dispatch  ← EXECUTED REGARDLESS OF rateChange
var mq = _defaultInstance!.MountQueueInstance!;

if (!MovePrimaryAxisActive)
    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
                                          // rateChange = 0
                                          // → _trackingX = 0  ✓  (tracking stops)

_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1,
        GetRaRateDirection(_defaultInstance?.RateRa ?? 0.0));
                                          // RateRa = 0.012534 (unchanged from +3.0 test setup)
                                          // → GetRaRateDirection(+0.012534) = -0.012534
                                          // → _raDecRateX = -0.012534  ← BUG: not cleared!
```

`CmdRaDecRate` has **no guard on `currentTrackingMode`**. It always re-applies whatever `RateRa` is set to — even when the intent is to stop all motion.

### Physics in `MoveAxes()` — Off/bug path

```
_trackingX  = 0           (correctly cleared by CmdAxisTracking)
_raDecRateX = -0.012534   (INCORRECTLY re-applied by CmdRaDecRate)

trkX = Tracking(Axis1, s)  = 0 × s          = 0
rdrX = RaDecRate(Axis1, s) = -0.012534 × s

changeX = rdrX + trkX = -0.012534 × s + 0 = -0.012534 × s
```

`DegreesX` now decreases at the **full RA rate** with **no sidereal tracking component** to partially cancel it.

### Resulting RA drift — bug path

```
dRA/dt = dLST/dt − (−0.012534) / 15
       = +0.000279 + 0.000836
       = +0.001115 h/s          ← 33.3 % faster than normal 0.000836 h/s
```

---

## 3. Quantitative Effect

| State | `_trackingX` | `_raDecRateX` | `dRA/dt` | ΔRA (40 s) | Excess |
|---|---|---|---|---|---|
| Normal (EqN, rate set) | +0.004178 | −0.012534 | 0.000836 h/s | **0.0334 h** | 0 % |
| Bug (Off, rate persists) | 0 | −0.012534 | 0.001115 h/s | **0.0446 h** | **+33.5 %** |
| Bug (Off, full 40 s) | 0 | −0.012534 | 0.001115 h/s | **0.0446 h** | +33.5 % |

### Mixed-window scenario (intermittent failure)

The conformance checker reads `RA_start`, waits T ≈ 40 s, reads `RA_end`. The bug state (off, rate persists) does not need to last the full 40 s to affect the result — it only needs to be active during **part** of the measurement window.

Let `t_bug` = seconds spent in the bug state; `t_normal` = 40 − t_bug seconds in normal state.

```
ΔRA = 0.001115 × t_bug + 0.000836 × (40 − t_bug)
    = 0.03344 + 0.000279 × t_bug
```

| t_bug (s) | ΔRA (h) | Deviation from 0.0333 h |
|---|---|---|
| 0 | 0.03344 | +0.3 % (pass) |
| 10 | 0.03623 | +8.8 % (pass, within 5 %? borderline) |
| 20 | 0.03902 | +17.2 % (fail) |
| 40 | 0.04460 | +33.9 % (max from this bug alone) |

> **Ceiling observation:** Bug 1 alone produces a **maximum excess of ~33.5 %** when the off-state persists for the entire 40 s window. The observed failure of **+50.97 %** exceeds this ceiling. Bug 1 is therefore a necessary but **not sufficient** explanation for the full observed deviation on its own. A compounding mechanism (see §5) is required to reach 50.97 %.

---

## 4. Exact Code Sequence That Triggers the Bug

```
[ASCOM Conformance Checker]
  │
  ├─ PUT Tracking = false
  │     ↓
  │   SkyServer.Tracking setter
  │     → _defaultInstance.TrackingMode = TrackingMode.Off
  │     → SetTracking()
  │         rateChange = 0
  │         CmdAxisTracking(Axis1, 0)     ← queued  → _trackingX = 0
  │         CmdRaDecRate(Axis1, −0.012534) ← queued  → _raDecRateX = −0.012534  ← BUG
  │
  ├─ PUT Tracking = true           (or RightAscensionRate = 3.0 triggers ActionRateRaDec)
  │     ↓
  │   SetTracking()
  │         rateChange = +0.004178
  │         CmdAxisTracking(Axis1, +0.004178)  ← queued
  │         CmdRaDecRate(Axis1, −0.012534)      ← queued (same value, but correct path)
  │
  ├─ GET RightAscension  →  RA_start  (reads _rightAscensionXForm cache)
  │
  │   [Physics loop ticks — MoveAxes() every ~20 ms]
  │   RACE: if _trackingX is still 0 when RA_start is sampled
  │         → drift rate = 0.001115 h/s  (bug state)
  │         → drift rate = 0.000836 h/s  (normal state, once CmdAxisTracking processed)
  │
  ├─ [Wait ~40 s]
  │
  └─ GET RightAscension  →  RA_end
        ΔRA = RA_end − RA_start
```

The **race window** is between the queue processing `CmdAxisTracking(0)` + `CmdRaDecRate(−0.012534)` (Off path) and then `CmdAxisTracking(+0.004178)` (re-enable path). During that window, the physics loop has `_trackingX = 0` but `_raDecRateX = −0.012534`, producing the elevated drift rate.

The width of this window depends on MountQueue backpressure and thread scheduling. Typical queue processing is fast (< 5 ms), so for this alone the effect on a 40 s window would be tiny. The failure must therefore involve the bug state persisting for a **significant fraction of the measurement window** — which means the trigger is not a simple toggle but a sustained condition.

---

## 5. Why the Bug State Can Persist — `CheckAxisLimits()` as the Sustained Trigger

`OnUpdateServerEvent()` fires every ~200 ms and calls `CheckAxisLimits()`. If the axis position is at or near a configured meridian or horizon limit, `CheckAxisLimits()` calls:

```csharp
Tracking = false;
```

This causes the full Off-path `SetTracking()` sequence described in §4 **every ~200 ms for as long as the limit condition holds**, continuously re-applying:

```
CmdAxisTracking(Axis1, 0)       → _trackingX = 0       (every 200 ms)
CmdRaDecRate(Axis1, −0.012534)  → _raDecRateX = −0.012534  (every 200 ms, BUG)
```

Meanwhile, the `RightAscensionRate = 3.0` test's `SetTracking()` call (which would restore `_trackingX = sidereal`) is continuously **overwritten** by the limit-triggered Off calls in the queue.

The net physics is `_trackingX ≈ 0` for extended periods, giving sustained `dRA/dt = 0.001115 h/s`.

### Why only 10 % of the time?

The trigger is `DegreesX` (or `DegreesY`) drifting close to `Settings.AutoHomeAxisX/Y` during the 40 s window. This is position-dependent: whether it occurs depends on the exact sky position at test time, which varies across individual conformance runs. Most runs the axis is well clear of limits; ~10 % of the time the position is near enough to a threshold that `CheckAxisLimits()` fires repeatedly.

---

## 6. Why Bug 1 Alone Falls Short of 50.97 %

From §3:
- Bug 1 alone caps at **+33.5 %** (sustained Off state, full 40 s)
- Observed deviation: **+50.97 %**

The gap of ~17 percentage points implies a concurrent additive effect. The most consistent candidate (excluding pulse guide) is a transient `TrackingMode.EqS` condition.

### EqS compounding scenario

If `SetTracking()` is called while `TrackingMode = EqS` (Southern hemisphere):

```
rateChange = −CurrentTrackingRate() = −0.004178 deg/s   (negated)
CmdAxisTracking(Axis1, −0.004178) → _trackingX = −0.004178

GetRaRateDirection(+0.012534) with SouthernHemisphere = false:
  east = true; if (east) { rate = -rate; } → returns −0.012534
CmdRaDecRate(Axis1, −0.012534)  → _raDecRateX = −0.012534
```

Physics:
```
changeX = (−0.004178 + (−0.012534)) × s = −0.016712 × s
dRA/dt  = 0.000279 + 0.016712 / 15 = 0.001393 h/s
```

Mixed window with EqS for t_EqS seconds, normal for the rest:
```
ΔRA = 0.001393 × t_EqS + 0.000836 × (40 − t_EqS)
    = 0.03344 + 0.000557 × t_EqS
```

For ΔRA = 0.0503 h:
```
0.000557 × t_EqS = 0.01686  →  t_EqS ≈ 30.3 s
```

So if `TrackingMode.EqS` were transiently active for ~30 of the 40 s window, the exact observed 50.97 % would result. This could be caused by `SetTrackingDirect()` being called with a wrong `TrackingMode` from a prior slew-completion path.

> **This document focuses on the confirmed Bug 1 mechanism.** The EqS compounding scenario requires additional investigation of `SetTrackingDirect()` call sites in `SlewController.cs` and related code.

---

## 7. Fix Applied

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

```csharp
// Before (lines ~2427–2441, Simulator / GermanPolar):
_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1,
        GetRaRateDirection(_defaultInstance?.RateRa ?? 0.0));   // always uses stale RateRa

if (!MoveSecondaryAxisActive)
    _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2,
            GetDecRateDirection(_defaultInstance?.RateDec ?? 0.0));

// After:
var raRate = currentTrackingMode != TrackingMode.Off
    ? GetRaRateDirection(_defaultInstance?.RateRa ?? 0.0)
    : 0.0;                                                       // clear rate when tracking off
_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, raRate);

if (!MoveSecondaryAxisActive)
{
    var decRate = currentTrackingMode != TrackingMode.Off
        ? GetDecRateDirection(_defaultInstance?.RateDec ?? 0.0)
        : 0.0;
    _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, decRate);
}
```

**Effect of fix:** When `TrackingMode.Off`, both `_raDecRateX` and `_raDecRateY` are set to `0` in the physics simulator. `dRA/dt` in the off state reverts to `dLST/dt` only (`0.000279 h/s`), preventing any rate-induced drift contaminating the RA measurement.

---

## 8. Residual Investigation Items

| Item | Location | Status |
|---|---|---|
| Source of `TrackingMode.EqS` transient for Northern hemisphere | `SlewController.cs`, `SetTrackingDirect()` call sites | Not yet investigated |
| Missing `volatile` / `lock` on `_trackingX`, `_raDecRateX` in `Controllers.cs` | `Controllers.cs` all physics fields | Noted, not fixed |
| `_pulseX` never set by pulse path (always 0) | `Controllers.cs` `Pulse()` function | Noted, not fixed |
| `_ctsMount` static across all instances | `Controllers.cs` line 28 | Noted, not fixed |
