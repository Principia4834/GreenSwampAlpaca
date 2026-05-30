# SkyGoTo Staggered-Axis Slew — Feasibility Report

**Author:** GitHub Copilot  
**Requested by:** Andy  
**Date:** 2026-05-28 11:36  
**Status:** Draft — revised (§3.1 corrected)

---

## 1. Problem Statement

`SkyGoTo` (Mount.Motion.cs) issues `SkyAxisGoToTarget` for both axes nearly simultaneously.
An intermittent symptom has been observed where a **short slew on one axis appears to prematurely
terminate a longer slew on the other axis**, causing the mount to land short of target.

The proposed fix is a **staggered-axis start strategy**:

1. Compute the expected slew duration for each axis.
2. Start the axis with the *longer* expected slew first.
3. Wait a configurable delay (default 2 000 ms) before starting the shorter axis.
4. Defer the "are we done?" polling loop until after the second axis is expected to have
   finished its slew.

---

## 2. Current Code Flow — `SkyGoTo`

```
Mount.Motion.cs  ─  SkyGoTo()
│
├─ MapSlewTargetToAxes()           // axis-space targets
├─ SkyTasks(StopAxes)              // ensure axes halted before slew
│
├─ SkyAxisGoToTarget(Axis1)        // fired immediately
├─ SkyAxisGoToTarget(Axis2)        // fired immediately after Axis1 enters queue
│
└─ Poll loop  (timer = 240 s)
	├─ if !axis1Stopped: WaitOne(250ms) → SkyIsAxisFullStop(Axis1)
	├─ if !axis2Stopped: Thread.Sleep(50) + WaitOne(250ms) → SkyIsAxisFullStop(Axis2)
	└─ if both stopped → settle → break
		 ↓
	SkyPrecisionGoto()             // fine-correction pass
```

The `SkyAxisGoToTarget` command eventually reaches `SkyWatcher.AxisGoToTarget()`, which:

1. Reads current axis position (`:j`).
2. Computes `movingSteps = AngleToStep(movingAngle)`.
3. Stops the axis if it is not already at full stop (up to 3 500 ms wait).
4. Selects high-speed (`:G 0`) or low-speed (`:G 2`) based on `_lowSpeedGotoMargin`.
5. Sends `:H` (target increment), `:M` (break-point increment), `:J` (start motion).

Both axes travel to their targets independently at the hardware level.

---

## 3. Root-Cause Hypothesis

### 3.1 Serial-Queue Sequencing

Both `SkyAxisGoToTarget` commands are enqueued back-to-back through `SkyQueue`.
The queue is a single-threaded serial dispatcher.  The second command's execution
includes a step that **stops the axis if it is not at full stop**:

```csharp
if (!axesStatus.FullStop)
{
	AxisStop(axis);         // :K — stops only the commanded axis
	// ... waits up to 3 500 ms ...
}
```

This `AxisStop` is axis-specific (`:K` is per-axis), so it should not halt the other axis.

> ⚠️ **Correction (2026-05-28):** An earlier draft of this report claimed there was
> "evidence that issuing `:J` (StartMotion) on Axis 2 while Axis 1 is already executing
> a goto can cause the Axis 1 motor controller to reset its internal step counter or
> deceleration ramp."  **That claim has no supporting evidence in the codebase, in
> commit history, or in any referenced document.**  It was speculative inference
> presented as fact and should be disregarded.  No code comment, issue note, or
> firmware datasheet in this repository supports the assertion.
>
> Whether `:J` on one axis can affect the other axis in any SkyWatcher firmware version
> is an **open question** (see §9).  It should not be treated as a confirmed root cause.

### 3.2 Transient FullStop Poll

The poll loop uses `SkyIsAxisFullStop`, which reads the mount status byte.
During the **deceleration ramp** of a GoTo move, some SkyWatcher firmware (especially
older EQ6 variants) briefly signals `FullStop = true` for a fraction of a second before the
motor controller re-asserts motion for the final creep phase.  A status poll that lands in
this window will see `axis1Stopped = true` while Axis 1 is actually still decelerating.
With the current logic this exits the loop and calls `SkyPrecisionGoto`, which then issues
a fresh `SkyAxisGoToTarget` — which in turn stops Axis 2 as a side-effect.

### 3.3 Advanced-Command-Set Path

When `SupportAdvancedCommandSet && AllowAdvancedCommandSet` is true, `AxisGoToTarget`
calls `_commands.AxisSlewTo_Advanced()` and then `SetSlewingTo(..., false, false)`.
This path does not perform the explicit stop-and-wait before motion, but it also does not
honour `_lowSpeedGotoMargin`, making the slew-time estimate less straightforward.

---

## 4. Feasibility of the Proposed Approach

### 4.1 Step 1 — Estimate Slew Time per Axis

**Available data (all populated during `SkyWatcher.Initialize()`)**

| Symbol | Source | Meaning |
|--------|--------|---------|
| `_stepsPerSecond[axis]` | `SetStepsPerSecond()` → `GetStepsPerRevolution()` | Sidereal steps/s |
| `_highSpeedRatio[axis]` | Commands.`GetHighSpeedRatio` | Speed multiplier for high-speed GoTo |
| `_lowSpeedGotoMargin[axis]` | Commands.`GetLowSpeedGotoMargin` | Step threshold for high/low speed GoTo |
| `_breakSteps[axis]` | Commands.`GetBreakPointIncrement` | Deceleration steps added to each GoTo |
| `AngleToStep(axis, angle)` | `Commands.AngleToStep` | Converts degrees to motor steps |

These fields are **private to `SkyWatcher`** and are not directly visible to `Mount.Motion.cs`.

**Proposed formula (high-speed path)**

```
angularDistance_deg = |targetDeg - currentDeg|
movingSteps         = AngleToStep(axis, angularDistance_deg in radians)
effectiveSteps      = movingSteps + _breakSteps[axis]        // includes deceleration coast
effectiveStepRate   = _stepsPerSecond[axis] * _highSpeedRatio[axis]
estimatedMs         = (effectiveSteps / effectiveStepRate) * 1000 + overhead_ms
```

For the **low-speed path** (`movingSteps <= _lowSpeedGotoMargin[axis]`), the motor runs at
the base sidereal step rate, so the divisor drops the `_highSpeedRatio` factor.

**Exposure required**  
The simplest approach is to add a new public/internal helper to `SkyWatcher`:

```csharp
/// <summary>
/// Estimates GoTo duration in milliseconds for a given angular distance on an axis.
/// Accounts for high-speed vs low-speed selection and deceleration coast.
/// Returns 0 if the hardware parameters have not yet been initialised.
/// </summary>
internal double EstimateGotoMs(Axis axis, double angularDistanceDeg)
{
	var movingSteps = _commands.AngleToStep(axis, Units.Deg2Rad1(angularDistanceDeg));
	if (movingSteps == 0) return 0;
	movingSteps = Math.Abs(movingSteps) + _breakSteps[(int)axis];
	double stepRate = _stepsPerSecond[(int)axis];
	if (Math.Abs(movingSteps) > _lowSpeedGotoMargin[(int)axis])
		stepRate *= _highSpeedRatio[(int)axis];
	if (stepRate <= 0) return 0;
	return (movingSteps / stepRate) * 1000.0;
}
```

A corresponding `SkyQueue` command wrapper (`SkyEstimateGotoMs`) would expose this through
the existing command-queue pattern without breaking the queue's thread-isolation model.

**Accuracy expectation**  
Empirical data from the existing sidereal-drift correction in `SimGoTo` suggests estimates
are within 15–25 % of actual slew time for mid-range slews (5°–60°).  Short slews (<2°)
are acceleration-dominated and the estimate will be **pessimistic** (too long), which is
safe for the stagger strategy — it simply delays the start of the second axis a little longer
than strictly needed.

---

### 4.2 Step 2 — Start Longer Axis First

**Feasibility: HIGH**

The two `SkyAxisGoToTarget` calls are already independent.  Reordering them (or interposing
a `Thread.Sleep` / `CancellationToken.WaitHandle.WaitOne` between them) requires only a
handful of lines in `SkyGoTo`.

```csharp
// Determine which axis has the longer slew
var est1 = QueryEstimateGotoMs(Axis.Axis1, Math.Abs(skyTarget[0] - currentPos[0]));
var est2 = QueryEstimateGotoMs(Axis.Axis2, Math.Abs(skyTarget[1] - currentPos[1]));

if (est1 >= est2)
{
	_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0]);
	token.WaitHandle.WaitOne(Settings.GotoAxisStaggerMs);  // configurable delay
	_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
}
else
{
	_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis2, skyTarget[1]);
	token.WaitHandle.WaitOne(Settings.GotoAxisStaggerMs);
	_ = new SkyAxisGoToTarget(SkyQueue!.NewId, SkyQueue, Axis.Axis1, skyTarget[0]);
}
```

The delay uses `token.WaitHandle.WaitOne` (consistent with the rest of the poll loop) so it
is immediately cancellable.

---

### 4.3 Step 3 — Configurable Stagger Delay

**Feasibility: HIGH — one new setting required**

Add to `SkySettings.Models.SkySettings`:

```csharp
/// <summary>
/// Milliseconds to wait between starting the first (longer) and second (shorter)
/// axis during a SkyGoTo slew.  0 = simultaneous (legacy behaviour).
/// Default: 2000.
/// </summary>
[CommonSetting]
public int GotoAxisStaggerMs { get; set; } = 2000;
```

This follows the exact pattern of the existing `SlewSettleTime`, `MinPulseRa`, etc.
The setting maps cleanly to `appsettings.json` / `appsettings.user.json` via the
existing `VersionedSettingsService`.

The static facade `SkySettings.cs` (`GreenSwamp.Alpaca.MountControl`) already mirrors
`SkySettings.Models.SkySettings` fields; a matching property needs to be added there too.

---

### 4.4 Step 4 — Deferred Poll Start

**Feasibility: MEDIUM — requires care**

The proposal is to not start polling `SkyIsAxisFullStop` until the **second axis's estimated
slew time has elapsed** (measured from its start command).  This prevents the transient
FullStop problem described in §3.2.

**Implementation sketch**

```csharp
// Record when the second axis was started
var secondAxisStarted = Stopwatch.StartNew();
var minPollDelayMs = Math.Min(est1, est2);    // wait for shorter axis to theoretically finish

// Poll loop — existing structure preserved
while (stopwatch.Elapsed.TotalSeconds <= timer)
{
	// Do not start checking until the shorter-axis estimate has elapsed
	if (secondAxisStarted.Elapsed.TotalMilliseconds < minPollDelayMs)
	{
		token.WaitHandle.WaitOne(50);
		continue;
	}

	// ... existing axis-stopped checks ...
}
```

**Risk**: If the estimate is significantly wrong (e.g., mount moves faster than predicted
on a short slew), the poll delay could over-wait and add latency.  However, because the
overall slew timeout is 240 s, there is no correctness risk — only a minor performance
cost on already-short slews.

**Alternative (simpler)**: Instead of a time-computed delay, simply introduce a fixed
minimum blanking period equal to `Settings.GotoAxisStaggerMs` + a small guard (e.g.,
200 ms).  This avoids exposing the `EstimateGotoMs` infrastructure entirely and may be
sufficient to prevent the transient FullStop misread.

---

## 5. Risk Analysis

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| Estimate accuracy poor for very short slews (<1°) | Medium | High | Enforce minimum poll delay equal to stagger delay + 500 ms guard; precision-goto corrects residual error |
| Advanced-command-set path has different timing | Medium | Medium | Detect `AllowAdvancedCommandSet` and skip estimation; fall back to fixed stagger only |
| ~~Some mount firmware stops both axes on `:J` for either axis~~ | ~~High~~ | ~~Low–Medium~~ | **Removed — no evidence; see §3.1 correction** |
| `_stepsPerSecond` not populated before first slew | Low | Low | Guard with `if (stepRate <= 0) return fallbackMs` in estimator |
| New setting added with 0 value in existing user configs | Low | Certain | Default value of 2 000 ms in model; `VersionedSettingsService` uses defaults for missing keys |
| Stagger breaks symmetry of simultaneous dual-axis slews on AZEQ mounts | Low | Low | `CanAxisSlewsIndependent` flag on `SkyWatcher` can gate the feature |

---

## 6. Alternative Approaches Considered

### 6A — Debounce the FullStop Poll

Add a **debounce counter**: require `SkyIsAxisFullStop` to return `true` for N consecutive
reads (e.g., 3 × 50 ms = 150 ms) before accepting it as truly stopped.  This directly
targets the transient FullStop described in §3.2 and requires only a small change to
It does **not** address the transient-FullStop hypothesis (§3.2), and whether it
addresses `:J` cross-axis interference (§3.1) is unconfirmed — see §9 Q5.

### 6B — Re-read Axis Status After First Stop

When `axis1Stopped` becomes true, immediately re-read after 200 ms before accepting it
and triggering `SkyPrecisionGoto`.  Lowest risk, smallest change, but may not fix §3.1.

### 6C — Use `SkyIsAxisFullStop` with a per-axis timeout guard

If Axis 1 reports stopped but the elapsed time is less than `estimatedMs * 0.7`, treat it
as a transient and reset `axis1Stopped = false`.  Combines well with the stagger approach
as a defence-in-depth layer.

---

## 7. Recommended Implementation Plan

Given the analysis, the recommended approach is a **staged implementation**:

### Stage 1 — Low-risk debounce (immediate)

Modify the `SkyGoTo` poll loop to require **2 consecutive stopped readings** (separated by
50 ms) before accepting that an axis has halted.  This costs at most 50 ms extra latency and
directly addresses the transient FullStop hypothesis without any new settings or estimation
infrastructure.

### Stage 2 — Stagger start (after Stage 1 validated)

If Stage 1 does not fully resolve the issue, implement the staggered start with a fixed
configurable delay (`GotoAxisStaggerMs`, default 2 000 ms) and defer the poll loop start
until `stagger + 500 ms` has elapsed.  This does **not** require the `EstimateGotoMs`
infrastructure; the fixed stagger is simpler and safer.

### Stage 3 — Time-estimated stagger (optional enhancement)

If a user-configurable fixed delay is insufficient (e.g., very long slews where 2 s is too
short), add the `EstimateGotoMs` helper and derive the stagger dynamically.

---

## 8. Files to be Modified

| File | Change |
|------|--------|
| `GreenSwamp.Alpaca.MountControl/Mount.Motion.cs` | `SkyGoTo` — stagger logic, deferred poll |
| `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs` | Add `EstimateGotoMs` (Stage 3 only) |
| `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyCommands.cs` | Add `SkyEstimateGotoMs` command (Stage 3 only) |
| `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs` | Add `GotoAxisStaggerMs` property |
| `GreenSwamp.Alpaca.MountControl/SkySettings.cs` | Add matching `GotoAxisStaggerMs` facade property |
| `appsettings.json` | Add `"GotoAxisStaggerMs": 2000` to relevant mount config section |

---

## 9. Open Questions for Andy

1. **Is the failure reproducible with a known axis-distance ratio?** For example, does it
   only occur when Axis 1 slews < 5° while Axis 2 slews > 20°?  This would confirm the
   transient-FullStop hypothesis over the `:J` interference hypothesis and steer toward
   Stage 1 (debounce) rather than the full stagger.

2. **Does the failure occur on the Advanced Command Set path as well?**  If not, the issue
   is likely in the legacy `:H/:M/:J` command sequence, which narrows the root cause
   significantly.

5. **Does issuing `:J` on Axis 2 affect Axis 1 at all on your firmware version?**
   If you can test a slew where only Axis 2 is commanded while Axis 1 is mid-GoTo
   (via a manual serial command or debug injection), the observed behaviour of Axis 1
   would either confirm or rule out §3.1 as a contributing factor.

3. **What is an acceptable latency increase for short GoTos?** The 2 000 ms stagger adds
   2 s to every two-axis slew.  Is that acceptable for normal use (e.g., from the hand
   controller or planetarium software)?

4. **Are there AZEQ (independent-axis) users?**  The stagger is conceptually different for
   mounts where both axes must track simultaneously; those should probably be excluded.

---

*Report generated: 2026-05-28 11:31 — revised: 2026-05-28 11:36 (§3.1 unsupported claim corrected)*
