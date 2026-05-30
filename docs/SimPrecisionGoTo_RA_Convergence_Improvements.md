# SimPrecisionGoTo RA Axis Convergence — Improvement Analysis

**Report generated:** 2026-05-27 15:39 | **Last updated:** 2026-05-30 19:00
**Author:** GitHub Copilot (analysis requested by Andy)  
**Scope:** GEM and Polar alignment modes, RaDec slews only  
**Files under analysis:** `GreenSwamp.Alpaca.MountControl\Mount.Motion.cs` — `SimGoTo()`, `SimPrecisionGoto()`, `SkyGoTo()`, `SkyPrecisionGoto()`  
**Simulator project:** `GreenSwamp.Alpaca.Simulator\Controllers.cs`

---

## 1. Current Implementation Summary

```
SimPrecisionGoto() — key behaviour (GEM / Polar, RaDec slew)
```

| Aspect | Current behaviour |
|--------|-------------------|
| **Prediction horizon** | Fixed `deltaTime = 75 ms` (initial), then replaced by `loopTimer.Elapsed.Milliseconds` each iteration |
| **RA axis target** | `simTarget[0] + 0.125 × deltaDegree[0]` — a constant fractional step toward target |
| **Dec axis target** | `simTarget[1] + 0.05  × deltaDegree[1]` — a constant fractional step toward target |
| **Target drift during slew** | **Not modelled** for GEM/Polar — the sky moves at the sidereal rate (~15.041″/s) while the axis is in motion, but `simTarget` is computed once at the start of each loop iteration and is not adjusted for sidereal drift over the slew execution window |
| **Sidereal rate access** | `SkySettings.SiderealRate = 15.0410671786691 °/hr` available in `SkySettings`; also exposed via `SkyPredictor._trackingRateProvider()` |
| **Max iterations** | 5 (hard limit) |
| **Loop settle wait** | Up to 3 000 ms per iteration |

### What the 0.125 scalar does

The factor `0.125` damps the commanded step to 1/8th of the measured error. This is a form of **proportional-only closed-loop control** with a fixed gain of 0.125. It provides stability but:

- Does not account for the fact that the sky **keeps moving** during the slew execution — so each iteration arrives at a point that is already stale by the time the axis settles.
- `deltaTime` (used as the next-iteration prediction horizon) is fed back as `loopTimer.Elapsed.Milliseconds`, which is the **measured past duration**, not a projection of the next iteration's duration. The two are identical only if iteration times are perfectly consistent.

---

## 2. Root Cause of RA Convergence Lag

For GEM and Polar modes the RA axis tracks sidereal motion. During the precision slew loop:

```
Elapsed per precision iteration ≈ 20–3 000 ms (poll interval + settle)
Sidereal RA drift per second    ≈ 15.041 arcseconds/s  = 0.004178°/s
Drift over a 100 ms iteration   ≈ 0.000418° ≈ 1.5″
Drift over a 500 ms iteration   ≈ 0.00209°  ≈ 7.5″
```

Precision of 2 encoder steps at 9 024 000 steps/rev ≈ `0.000080°` ≈ `0.29″`

So if the iteration takes just 200 ms the sky has drifted ~3″, which is already ~10× the target precision. The mount must then execute a second or third corrective iteration purely to catch up with sidereal drift — iterations that would be unnecessary if the commanded target had been projected forward.

---

## 3. Proposed Algorithms (RA axis, GEM/Polar only)

### Algorithm A — Sidereal-Drift Feedforward on Axis Position (Recommended — minimal change)

**Concept:** Before issuing the `CmdAxisGoToTarget` for Axis 1, advance the target by the expected sidereal drift over the estimated slew duration.

```
raTargetDeg_corrected = simTarget[0]
					  + SiderealRate_deg_per_ms × estimatedSlew_ms
```

where `SiderealRate_deg_per_ms = 15.0410671786691 / 3600000.0`.

The estimated slew duration can be seeded from `deltaTime` (the previous measured iteration time).

**Why it converges faster:**  
The axis is commanded to where the sky *will be* when the axis arrives, rather than where the sky *was* when the command was issued. The residual error on settling is then dominated by mechanical backlash and quantisation, not sidereal drift.

**Asymmetry consideration for GEM:**  
In GEM mode the RA axis direction (East vs West of meridian) determines whether drift adds or subtracts. The sign of the required correction is:

```
sign = (mountSide == MountSide.West) ? +1.0 : -1.0;
```

For Polar (fork) mount the sign is constant (+1 for Northern hemisphere, assuming standard mount orientation).

**Code sketch:**

```csharp
// GEM / Polar sidereal drift correction for RA axis
if (Settings.AlignmentMode != AlignmentMode.AltAz
	&& slewType == SlewType.SlewRaDec
	&& !axis1AtTarget)
{
	const double siderealDegPerMs = 15.0410671786691 / 3_600_000.0;
	double driftDeg = siderealDegPerMs * deltaTime;           // deltaTime now in ms
	double driftSign = (Settings.SideOfPier == PierSide.East) ? -1.0 : +1.0; // verify convention
	double raTargetCorrected = simTarget[0] + driftSign * driftDeg + 0.125 * deltaDegree[0];
	_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, raTargetCorrected);
}
```

**Expected gain:** Should reduce from typically 3–4 iterations to 1–2 iterations for targets at low-to-mid declinations.

---

### Algorithm B — Exponential Moving Average (EMA) of Iteration Duration

**Concept:** Replace the naive `deltaTime = loopTimer.Elapsed.Milliseconds` feedback with a smoothed estimate:

```
deltaTime_next = α × loopTimer.Elapsed.Milliseconds + (1 - α) × deltaTime_prev
```

where `α ∈ [0.3, 0.5]` is recommended (faster adaptation than a simple average, less noisy than raw measurement).

**Why it matters:**  
`loopTimer.Elapsed.Milliseconds` returns the *integer millisecond* portion of the elapsed time (`0–999`). This is not the same as `TotalMilliseconds`. If an iteration takes 1 050 ms, `Elapsed.Milliseconds` returns `50`, not `1050`. This is a latent bug that causes the predictor to badly underestimate slew time and therefore undercompensate for drift.

**Fix:** Replace:
```csharp
deltaTime = loopTimer.Elapsed.Milliseconds;
```
with:
```csharp
deltaTime = loopTimer.Elapsed.TotalMilliseconds;
```
and apply EMA smoothing.

**Expected gain:** More accurate sidereal drift compensation, especially for long-settling iterations (>1 s). Also fixes the integer truncation latent bug.

---

### Algorithm C — Adaptive Proportional Gain (Variable P Gain)

**Concept:** Rather than a fixed scalar of `0.125` on Axis 1, use a gain that reduces as the residual error shrinks:

```
gain = Clamp(|deltaDegree[0]| / threshold, g_min, g_max)
```

For example:
- When `|deltaDegree[0]| > 1.0°` → gain = 1.0 (large move, go straight to target)
- When `|deltaDegree[0]| < 0.01°` → gain = 0.25 (fine correction, gentle approach)

This avoids the 0.125 factor under-driving large residuals and over-damping tiny ones. Combined with Algorithm A (sidereal feedforward) the gain becomes responsible purely for removing the mechanical error component.

**Code sketch:**

```csharp
double gain1 = Math.Clamp(Math.Abs(deltaDegree[0]) / gotoPrecision[0], 0.25, 1.0);
_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1,
		simTarget[0] + driftCorrection + gain1 * deltaDegree[0]);
```

---

### Algorithm D — Measured Slew-Time Model (Learning Feedforward)

**Concept:** Maintain a running estimate of how long Axis 1 takes to settle for a given commanded displacement. After each iteration record:

```
(|commanded displacement in degrees|, observed settle time in ms)
```

Use a simple linear fit: `settleMs ≈ k₀ + k₁ × |displacement_deg|`.

On the next iteration use the fitted model to predict settle time → drive the sidereal drift correction in Algorithm A more accurately than raw `deltaTime` feedback.

**Scope:** This is most valuable when the precision loop is called many times in a session (e.g. tracking with periodic corrections). For a one-off precision slew it adds no value. However, if `SimPulseGoto` is also modified to share the model, it pays dividends there too.

---

### Algorithm E — Single-Shot Prediction with Sidereal Integration (Most Aggressive)

**Concept:** Replace the iterative approach with a single well-predicted command. At the moment the command is issued, integrate the sidereal rate forward by the expected settle time:

```
t_arrive = t_now + estimatedSettleMs
ra_arrive = ra_now + SiderealRate_deg_per_ms × (t_arrive - t_now)
```

Issue `CmdAxisGoToTarget(Axis.Axis1, ra_arrive)` once. If the mount's settle time model is accurate (Algorithm D), this can converge in a **single iteration** for tracking targets at sidereal rate.

The residual second iteration then only deals with mechanical scatter (backlash, resonance), not sidereal drift.

---

## 4. Comparison Matrix

| Algorithm | Complexity | Iterations saved (est.) | Fixes latent bug | Notes |
|-----------|-----------|------------------------|-----------------|-------|
| **A — Sidereal drift feedforward** | Low | 1–2 | No (standalone) | ✅ Implemented (commit ccc5e67) |
| **B — EMA + TotalMilliseconds fix** | Very Low | 0–1 | **Yes** | ✅ Implemented (commit ccc5e67) |
| **C — Adaptive P gain** | Low | 0–1 | No | Complements A |
| **D — Learned settle-time model** | Medium | 0–1 (accumulated) | No | Valuable for session-long accuracy |
| **E — Single-shot integration** | Medium | 1–2 | No (needs D first) | Best long-term solution |

---

## 5. Recommended Implementation Order

1. ✅ **DONE — Algorithm B bug fix:** `loopTimer.Elapsed.Milliseconds` → `loopTimer.Elapsed.TotalMilliseconds`. Also fixed `SimPulseGoto` with the same bug. Also fixed the `deltaTime` seed from `75 * 0.001 = 0.075` ms to `75.0` ms, and added EMA smoothing (`α = 0.4`).

2. ✅ **DONE — Algorithm A feedforward in `SimPrecisionGoto`:** Sidereal drift feedforward added to `Axis1` command for GEM and Polar RaDec slews. Uses `Settings.SiderealRate / 3_600_000.0` (degrees/ms). Sign is hemisphere-only (`+1` NH, `−1` SH). See Section 8 for the sign derivation.

3. ✅ **DONE — Pre-correction in `SimGoTo`:** First-slew RA pre-correction added to `SimGoTo` using `Settings.MaxSlewRate` (default `3.4 °/s`) as the speed model. See Section 9 for the full design.

4. **Medium term (Algorithm C):** Replace the fixed `0.125` gain with an adaptive one once A and B are proven stable.

5. **Long term (Algorithms D + E):** Introduce a lightweight per-session settle-time model so single-shot prediction becomes viable.

---

## 6. Key Values from Codebase

| Symbol | Value | Source |
|--------|-------|--------|
| `SiderealRate` | `15.0410671786691` arcsec/s | `SkySettings.cs`, `TrackingCommandProcessor.cs`, `SkyPredictor.cs` |
| `SiderealRate_deg_per_ms` | `15.0410671786691 / 3_600_000.0 = 4.178×10⁻⁶ °/ms` | derived |
| `MaxSlewRate` (default) | `3.4 °/s` | `SkySettings.cs:107` (`_maxSlewRate`) |
| Precision threshold (2 steps at 9 024 000 steps/rev) | `≈ 0.000080°` ≈ `0.29″` | `Mount.Motion.cs` |
| `SimPrecisionGoto` initial `deltaTime` | `75.0 ms` | `Mount.Motion.cs` |
| `SimGoTo` minimum slew floor | `4000 ms` | `Mount.Motion.cs` (empirically derived) |
| `SimGoTo` dominant-axis time estimate | `Math.Max(axis1Distance, axis2Distance) / MaxSlewRate × 1000` | `Mount.Motion.cs` |
| RA precision-loop gain scalar | `0.125` | `Mount.Motion.cs` |
| Dec precision-loop gain scalar | `0.05` | `Mount.Motion.cs` |
| EMA smoothing factor (α) | `0.4` | `Mount.Motion.cs` |
| Max precision iterations | `5` | `Mount.Motion.cs` |
| Max iteration wait | `3 000 ms` | `Mount.Motion.cs` |

---

## 7. Notes on GEM vs Polar Differences

- **GEM (GermanPolar):** ~~Sidereal drift direction is pier-side-dependent.~~ **Corrected 2026-05-27 16:27:** The sign is **hemisphere-only** (`+1` NH, `−1` SH), identical to the Polar case. The East/West pier position changes the through-pole offset applied to `axis0` in `RaDecToAxesXyCore`, but does not invert the *rate of change* of `axis0` with respect to HA (sidereal time). `AxesAppToMount` applies the same hemisphere-dependent transform regardless of pier side. See Section 8 for the full derivation (Polar case; GEM follows the same logic).

- **Polar (Fork):** See detailed analysis in Section 8 below.

- **AltAz:** Already handled by `SkyPredictor.GetRaDecAtTime()` in the existing code — not in scope for this report.

---

## 8. Polar Mount — Normal vs ThroughPole Sign Analysis

**Reviewed 2026-05-27 15:39** following challenge that a Polar mount can occupy two distinct physical positions (`PierSideUI.Normal` and `PierSideUI.ThroughPole`) pointing at the same celestial coordinates, and the correction sign between these positions must be verified.

### The two physical positions

In app code, both positions arrive via the same path through `RaDecToAxesXyCore` (`Axes.cs`):

```csharp
// Convert RA to Hour Angle in degrees
axes[0] = 15.0 * (lst - axes[0]);     // HA in degrees
axes[0] = Range.Range360(axes[0]);

// Through-pole detection and adjustment
if (axes[0] > 180.0)
{
    axes[0] += 180;          // ThroughPole branch
    axes[1] = 180 - axes[1]; // Dec axis flipped
}
axes = Range.RangeAxesXy(axes); // axis0 → [0, 180)
```

This means:

| Physical position | HA range | App-space axis0 (before AxesAppToMount) |
|---|---|---|
| **Normal** | HA ∈ [0°, 180°] | `axis0 = HA` |
| **ThroughPole** | HA ∈ (180°, 360°] | `axis0 = HA + 180` mod 360, ranged → `HA − 180` |

In both cases, as time advances and HA increases at the sidereal rate, **app-space axis0 increases at the same rate** (δ axis0 / δ HA = +1). The ThroughPole branch adds a fixed 180° offset to HA before ranging — it does not invert the axis0 derivative with respect to time.

### Through AxesAppToMount

`AxesAppToMount` (`Axes.cs:79`) receives only the already-ranged app-space value and has no visibility of whether the original HA was in the Normal or ThroughPole half. The transforms it applies are:

| Mount / Hemisphere / PolarMode | Transform on axis0 | Encoder sign vs sidereal drift |
|---|---|---|
| Simulator, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| Simulator, SH | `a[0] = -a[0]` | **−** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Right, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Right, SH | `a[0] = -a[0]` | **−** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Left, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Left, SH | `a[0] = 180 − a[0]` | **−** — same for Normal & ThroughPole |

### Conclusion (confirmed correct)

The original report conclusion stands: **the correction sign does not change between Normal and ThroughPole positions for a Polar mount.**

The sign of the sidereal drift correction in mount encoder space is determined solely by:
1. **Hemisphere** (Latitude ≥ 0 vs < 0) — the primary determinant.
2. **PolarMode** (Left vs Right) for SkyWatcher SH only — affects the specific transform applied but still produces a consistent sign per configuration.

It is **not** affected by which of the two physical positions (Normal / ThroughPole) the mount is currently occupying, because the ThroughPole path in `RaDecToAxesXyCore` shifts the axis0 value by a constant 180° offset but does not invert the rate of change of axis0 with respect to sidereal time.

For the feedforward implementation, the sign determination logic is therefore:

```csharp
// True for NH simulator, SkyWatcher PolarMode.Right NH, SkyWatcher PolarMode.Left NH
// False (negate) for all SH configurations
bool positiveSign = Settings.Latitude >= 0;
double driftSign = positiveSign ? +1.0 : -1.0;
```

No check against `PierSideUI.Normal` / `PierSideUI.ThroughPole` is required.

---

## 9. Final Design — `SimGoTo` RA Pre-Correction

### Purpose

`SimGoTo` issues the first (coarse) slew command before `SimPrecisionGoto` runs the fine-correction loop. Without pre-correction the first slew lands behind the true sky position by the sidereal drift accumulated during the slew — forcing the precision loop to spend its first iteration just catching up, rather than converging on the residual mechanical error.

### Implementation (`Mount.Motion.cs`)

```csharp
var axis1SlewTarget = simTarget[0];
if (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
{
    var rawPos = GetRawDegrees();
    if (rawPos != null && !double.IsNaN(rawPos[0]))
    {
        var axis1Distance = Math.Abs(simTarget[0] - rawPos[0]);
        var axis2Distance = Math.Abs(simTarget[1] - rawPos[1]);

        // Dominant-axis distance: mount won't stop until both axes settle,
        // so use whichever axis has the longer journey.
        // Apply 4000 ms minimum floor for acceleration-dominated short slews.
        const double minSlewMs = 4000.0;
        var estimatedSlewMs = Settings.MaxSlewRate > 0
            ? Math.Max(minSlewMs, (Math.Max(axis1Distance, axis2Distance) / Settings.MaxSlewRate) * 1000.0)
            : minSlewMs;

        var driftSign = Settings.Latitude >= 0 ? +1.0 : -1.0;
        var raCorrection = driftSign * (Settings.SiderealRate / 3_600_000.0) * estimatedSlewMs;
        axis1SlewTarget = simTarget[0] + raCorrection;
    }
}
_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, axis1SlewTarget);
_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis2, simTarget[1]);
```

### Parameter values (production)

| Parameter | Value | Notes |
|-----------|-------|-------|
| `minSlewMs` | `4000 ms` | Empirically derived from test logs; accounts for simulator accel profile |
| Speed model | `MaxSlewRate` default `3.4 °/s` | Read from `SkySettings.MaxSlewRate` |
| Distance model | `Math.Max(axis1Distance, axis2Distance)` | Dominant-axis: prevents underestimate for Dec-heavy slews |
| Sidereal rate | `Settings.SiderealRate / 3_600_000.0` °/ms | Same constant as the precision loop |
| Sign | `Latitude >= 0 ? +1 : -1` | Hemisphere only; same as precision loop |

### Telemetry

Each `SimGoTo` pre-correction logs at `MonitorType.Information`:

```
Mount:{id}|GoToRaCorrection|{raCorrection:F6}|EstSlewMs|{estimatedSlewMs:F1}|Dist|{axis1Distance:F4}|Dist2|{axis2Distance:F4}
```

---

## 10. Simulator Non-Determinism

### Overview

The simulator (`GreenSwamp.Alpaca.Simulator\Controllers.cs`) uses wall-clock elapsed time in a background polling loop to integrate axis motion. This means slew duration and precision-loop iteration count vary between otherwise identical runs. This is the primary reason `SimPrecisionGoto` shows run-to-run variation in `GoToSeconds` and iteration counts.

### Sources of non-determinism (in order of impact)

#### 1. `Thread.Sleep(20)` delivers variable tick intervals — primary cause

**Location:** `Controllers.cs` — `MoveAxes()`

```csharp
Thread.Sleep(20);   // Windows does NOT guarantee 20ms
var now = HiResDateTime.UtcNow;
var seconds = (now - _lastUpdateTime).TotalSeconds;
_lastUpdateTime = now;
...
changeX += GoTo(Axis.Axis1, seconds);   // movement = speed × actual_interval
```

On Windows, `Thread.Sleep(20)` sleeps for **15–35 ms** depending on the system timer resolution (default 15.625 ms quantum). The actual `seconds` is measured from the real wall clock, so axis movement per tick is `speed × variable_interval`. A slew that nominally takes 150 ticks at exactly 20 ms = 3000 ms can range from ~2800 ms to ~3300 ms. **This alone explains 10–20% variation in actual slew duration.**

#### 2. No thread synchronisation between command thread and simulation thread — data race

**Location:** `Controllers.cs` — `Command()` vs `MoveAxes()`

`Command()` is called from `IoSerial.Send()` on the command-queue thread and writes fields like `_gotoX`, `_isGotoSlewingX`, `DegreesX` directly. `MoveAxes()` runs on the dedicated `MountSimLoop` background thread and reads/writes the same fields. There is **no `lock`, `Interlocked`, or `volatile`** on any of these fields. This can cause:

- A `gototarget` command arriving mid-tick to partially update state while `MoveAxes()` is computing
- `_isGotoSlewingX` being cleared on the sim thread at the same moment a new goto arrives from the command thread
- Inconsistent axis X/Y snapshot in `CheckStopped` / `CheckSlewing`

#### 3. Speed band threshold crossings are sensitive to tick jitter

**Location:** `Controllers.cs` — `GoTo(Axis axis, double interval)` lines ~426–458

```csharp
if      (delta < .01)  { /* snap to target */   _isGotoSlewingX = false; }
else if (delta < .2)   { change = SlewSpeedOne * sign;  }  // ~0.04 °/s
else if (delta < .6)   { change = SlewSpeedFour * sign; }  //  ~6.0 °/s
else if (delta < 1)    { change = SlewSpeedSix * sign;  }  // ~10.0 °/s
else                   { change = SlewSpeedEight * sign; }  // ~13   °/s
```

The `SlewSpeedSix → SlewSpeedOne` transition at `delta = 0.2°` is particularly sensitive. At 10 °/s, one 22 ms tick advances the axis `0.22°` — the axis **jumps over the 0.2° threshold without entering the slow band**. At 19 ms the same axis moves `0.19°` and enters the slow band correctly. This produces **systematic half-second variation** in slew duration depending purely on tick timing near that boundary.

#### 4. Non-atomic position read in `Actions.AxesDegrees()`

**Location:** `Actions.cs` — `AxesDegrees()`

```csharp
var x = Convert.ToDouble(_ioSerial.Send($"degrees|{Axis.Axis1}"));
// MoveAxes() can tick HERE and update both axes
var y = Convert.ToDouble(_ioSerial.Send($"degrees|{Axis.Axis2}"));
```

Axis1 and Axis2 positions are read in **two separate calls** with no atomic snapshot. The simulation loop can run between them, so the X/Y pair delivered to `SimPrecisionGoto` may come from different simulator ticks. For a large slew this introduces an embedded time-skew error in the starting position that varies every read.

### Summary table

| Source | Location | Effect on `GoToSeconds` / iterations |
|--------|----------|--------------------------------------|
| `Thread.Sleep(20)` jitter | `Controllers.cs` `MoveAxes()` | ±5–15 ms per tick; cumulative 10–20% slew-time variation |
| No thread synchronisation | `Controllers.cs` `Command()` vs `MoveAxes()` | Race on `_gotoX/Y`, `_isGotoSlewing`, `DegreesX/Y` |
| Speed band threshold overshoot | `Controllers.cs` `GoTo()` | One tick near 0.2° skips the slow-down band; ~0.5 s variation |
| Non-atomic position read | `Actions.cs` `AxesDegrees()` | X/Y read from different ticks; skewed starting position |

### Impact on convergence tuning

The `minSlewMs = 4000 ms` floor and the dominant-axis estimate in `SimGoTo` were derived empirically from observed slew-time distributions in test logs. Simulator non-determinism means there is a practical lower bound on how tightly the pre-correction can be tuned: a run that completes in 3800 ms will slightly over-correct relative to one that completes in 5200 ms, but the pre-correction still reduces the precision-loop residual in both cases compared to no correction at all.

Improving simulator determinism (fixed-step integration, proper locking, or atomic position snapshot) would allow tighter tuning but is a separate concern from the mount-control convergence work.

---

## 11. SkyWatcher Path — Current State Assessment (`SkyGoTo` / `SkyPrecisionGoto`)

### 11.1 Architecture differences vs the Simulator path

| Aspect | Simulator path | SkyWatcher path |
|--------|---------------|-----------------|
| **First-slew command** | `CmdAxisGoToTarget` via `SimQueue` | `SkyAxisGoToTarget` via `SkyQueue` |
| **Settle detection** | Poll `CmdAxisStatus.Stopped` (50 ms intervals) | Poll `SkyIsAxisFullStop` (125 ms `WaitHandle` intervals) |
| **Position feedback source** | `GetRawDegrees()` → `IoSerial` → `Controllers.DegreesX/Y` | Event-based `WaitUpdateMountPosition()` — waits for `_mountPositionUpdatedEvent` set by `ReceiveSteps()` |
| **Position update rate** | Controlled by simulator loop (~20 ms) | Driven by SkyWatcher hardware step-count polling; update arrives asynchronously |
| **Precision threshold** | `ConvertStepsToDegrees(2, 0/1)` ≈ `0.000080°` (2 encoder steps) | `Settings.GotoPrecision` = `0.001°` (configurable; default ≈ 3.6″) |
| **Iteration wait budget** | 3 000 ms per axis (poll loop) | 3 000 ms shared between both axes per iteration |
| **Initial `deltaTime`** | `75.0 ms` (seeded), then EMA α=0.4 | `800 ms` (seeded); no EMA — raw `loopTimer.ElapsedMilliseconds` |
| **RA gain (Axis1)** | `0.125` | `0.25` for RaDec slew |
| **Dec gain (Axis2)** | `0.05` | `0.0` for GEM/Polar RaDec (no correction applied); `0.1` for AltAz |
| **Max iterations** | `5` | `5` |

### 11.2 Known bugs in the SkyWatcher path

#### Bug 1 — `ElapsedMilliseconds` truncation in `SkyPrecisionGoto` (line 689)

```csharp
loopTimer.Stop();
deltaTime = loopTimer.ElapsedMilliseconds;   // BUG: integer ms, not TotalMilliseconds
```

`Stopwatch.ElapsedMilliseconds` is a `long` returning the integer millisecond count and is **correct** for durations under 1000 ms. However, when a precision iteration takes more than 1 second (which is normal — the 3000 ms budget is the ceiling), it returns the full count correctly. **This is not the same truncation bug as `Elapsed.Milliseconds`**, but the variable is typed `long` and used as a `long` in the AltAz prediction path only. For GEM/Polar the feedforward computation has not been implemented yet, so this value is currently unused in those modes. It will matter when the feedforward is added.

#### Bug 2 — `ElapsedMilliseconds` truncation in `SkyPulseGoto` (line 818)

```csharp
loopTimer.Stop();
deltaTime = loopTimer.ElapsedMilliseconds;   // same pattern as above
```

Same situation in `SkyPulseGoto` — the AltAz path uses `deltaTime` to predict the next position; GEM/Polar does not yet use it. Should be consistent with the simulator path fix when feedforward is introduced.

#### Bug 3 — No sidereal feedforward in `SkyPrecisionGoto` for GEM/Polar

`SkyPrecisionGoto` handles GEM/Polar and AltAz differently:

```csharp
// AltAz: uses SkyPredictor to project target forward by deltaTime ✅
if (Settings.AlignmentMode == AlignmentMode.AltAz && slewType == SlewType.SlewRaDec)
{
    var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
    var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
    ...
}

// GEM/Polar Axis1 correction (line 632–638):
var predictor = (slewType != SlewType.SlewRaDec)
    ? 0
    : 0.25;   // 0.25 × delta for RaDec, 0 for axis slew
_ = new SkyAxisGoToTarget(..., skyTarget[0] + predictor * deltaDegree[0]);

// GEM/Polar Axis2 correction (line 660–666):
var predictor = (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
    ? 0       // ← No Dec correction at all for GEM/Polar RaDec ← by design
    : 0.1;
```

There is **no sidereal drift feedforward** for GEM/Polar RaDec, equivalent to the pre-Algorithm A state of `SimPrecisionGoto`. Each iteration commands `skyTarget[0] + 0.25 × deltaDegree[0]` — a proportional step toward the *current measured* error — without advancing the target for how far the sky will move during the next settling period.

#### Bug 4 — No pre-correction in `SkyGoTo` for GEM/Polar

```csharp
// SkyGoTo first slew (lines 422–423):
_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0]);  // no RA correction
_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
```

There is no equivalent of the `SimGoTo` RA pre-correction. The first slew always targets the sky position at the moment the command is issued, so the precision loop must absorb the full sidereal drift accumulated during the (typically 5–15 second) first slew.

### 11.3 Comparison with the Simulator path after changes

| Feature | Sim path (current) | Sky path (current) | Gap |
|---------|--------------------|--------------------|-----|
| `TotalMilliseconds` (not `Milliseconds`) in precision loop | ✅ Fixed | ⚠️ `ElapsedMilliseconds` (correct for `long`, but inconsistent) | Minor |
| EMA smoothing of `deltaTime` | ✅ α = 0.4 | ❌ None | Yes |
| Sidereal feedforward in precision loop (GEM/Polar) | ✅ `RaFwd` | ❌ None | **Yes** |
| Pre-correction in first slew | ✅ `SimGoTo` correction | ❌ None | **Yes** |
| Dominant-axis time estimate | ✅ `Math.Max(axis1, axis2)` | N/A (not implemented) | N/A |
| Minimum slew floor | ✅ 4000 ms | N/A | N/A |

### 11.4 Key differences that affect algorithm design for the SkyWatcher path

**1. Real hardware latency is higher and more variable than the simulator.**  
The SkyWatcher mount communicates over serial at typically 9600 baud. Each `SkyAxisGoToTarget` command round-trip is ~20–100 ms. `SkyIsAxisFullStop` polling uses 125 ms `WaitHandle` intervals. The iteration duration for real hardware is therefore dominated by mount mechanics and serial latency, not thread scheduling — it will be larger and more stable than the simulator.

**2. Position feedback uses an event-driven model.**  
`WaitUpdateMountPosition(5000)` blocks until `ReceiveSteps()` fires the event. This is more reliable than the simulator's two-call `AxesDegrees()` read but still introduces an unknown lag between the physical axis stopping and the event being received.

**3. The existing `deltaTime = 800 ms` seed is likely already better calibrated for hardware.**  
For a real mount with ~500–1000 ms serial + settling overhead, 800 ms is a reasonable first guess. The simulator seed of 75 ms was far too small for that platform. The optimal floor for hardware may differ from the simulator's 4000 ms.

**4. The Dec (Axis2) correction is explicitly suppressed for GEM/Polar RaDec.**  
`predictor = 0` for Dec in GEM/Polar mode is correct — Dec does not drift siderally. The RA feedforward is the only addition needed for this mode.

**5. `Settings.GotoPrecision` default (`0.001°` ≈ 3.6″) is coarser than the simulator threshold (`0.000080°` ≈ 0.29″).**  
This means `SkyPrecisionGoto` will exit earlier in each iteration. The sidereal feedforward must still be within the 3.6″ precision window to avoid adding iterations rather than removing them.

### 11.5 Proposed changes for the SkyWatcher path

#### Change S1 — Sidereal feedforward in `SkyPrecisionGoto` (equivalent to Algorithm A)

Apply a sidereal drift correction to the Axis1 command in exactly the same way as `SimPrecisionGoto`, using `deltaTime` (the EMA-smoothed previous iteration duration) as the prediction horizon.

```csharp
// Proposed code sketch (GEM/Polar, RaDec only):
if (!axis1AtTarget && slewType == SlewType.SlewRaDec
    && Settings.AlignmentMode != AlignmentMode.AltAz)
{
    var driftSign = Settings.Latitude >= 0 ? +1.0 : -1.0;
    var raFeedforward = driftSign * (Settings.SiderealRate / 3_600_000.0) * deltaTime;
    _ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1,
        skyTarget[0] + 0.25 * deltaDegree[0] + raFeedforward);
}
```

**Risk:** Real hardware iteration times are more variable than the simulator's. If `deltaTime` overshoots, the feedforward may over-correct. EMA smoothing (Change S2) mitigates this.

#### Change S2 — EMA smoothing of `deltaTime` in `SkyPrecisionGoto` and `SkyPulseGoto` (equivalent to Algorithm B)

Replace the raw assignment with EMA smoothing using the same `α = 0.4`:

```csharp
// Current (line 689):
deltaTime = loopTimer.ElapsedMilliseconds;

// Proposed:
deltaTime = (long)(0.4 * loopTimer.Elapsed.TotalMilliseconds + 0.6 * deltaTime);
```

Note: `deltaTime` is currently `long` in the Sky path; it should remain `long` (or be changed to `double` consistently) when this is implemented.

#### Change S3 — RA pre-correction in `SkyGoTo` (equivalent to `SimGoTo` pre-correction)

Apply a first-slew RA correction using `MaxSlewRate` and dominant-axis distance, identical in structure to the simulator version:

```csharp
// Proposed addition in SkyGoTo, before the first SkyAxisGoToTarget pair:
var axis1SlewTarget = skyTarget[0];
if (slewType == SlewType.SlewRaDec && Settings.AlignmentMode != AlignmentMode.AltAz)
{
    var rawPos = GetRawDegrees();
    if (rawPos != null && !double.IsNaN(rawPos[0]))
    {
        var axis1Distance = Math.Abs(skyTarget[0] - rawPos[0]);
        var axis2Distance = Math.Abs(skyTarget[1] - rawPos[1]);
        const double minSlewMs = 4000.0;  // needs validation against real hardware
        var estimatedSlewMs = Settings.MaxSlewRate > 0
            ? Math.Max(minSlewMs, (Math.Max(axis1Distance, axis2Distance) / Settings.MaxSlewRate) * 1000.0)
            : minSlewMs;
        var driftSign = Settings.Latitude >= 0 ? +1.0 : -1.0;
        axis1SlewTarget = skyTarget[0] + driftSign * (Settings.SiderealRate / 3_600_000.0) * estimatedSlewMs;
    }
}
_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, axis1SlewTarget);
_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
```

**Important caveat for `minSlewMs`:** The simulator floor of `4000 ms` was empirically derived from simulator test data. For real SkyWatcher hardware the slew profile is different (hardware acceleration ramps, different serial latency). The minimum floor **must be validated against real hardware logs** before being hardened. A safe starting point would be the same `4000 ms`, but this may need adjustment.

### 11.6 Implementation risk assessment

| Change | Risk level | Notes |
|--------|-----------|-------|
| **S2 — EMA smoothing** | Low | No behavioural change if hardware iterations are consistent; only smooths noise |
| **S1 — Sidereal feedforward in precision loop** | Medium | Depends on `deltaTime` quality; EMA (S2) should be done first. If `deltaTime` estimate is poor, can add iterations. **Needs real hardware test log to validate.** |
| **S3 — Pre-correction in `SkyGoTo`** | Medium–High | `minSlewMs` floor value is simulator-derived and unvalidated for hardware. Real hardware slew times are longer and more consistent than the simulator, so the floor may be less critical — but the `MaxSlewRate` speed model needs verification. **Do not implement before S1 + S2 are validated on hardware.** |

### 11.7 Recommended implementation order for SkyWatcher path

1. **S2 first** — EMA smoothing of `deltaTime` in `SkyPrecisionGoto` and `SkyPulseGoto`. Low risk, enables S1.
2. **S1 second** — Sidereal feedforward in `SkyPrecisionGoto`. Validate with real hardware log before S3.
3. **S3 last** — `SkyGoTo` pre-correction. Requires hardware log evidence that the precision loop still requires multiple iterations after S1 + S2.

---

*End of report*
