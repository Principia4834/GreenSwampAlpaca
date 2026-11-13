# Limit Handling Review: Gaps and Requirement Clarifications

**Generated:** 2026-10-11 11:54
**Prepared for:** Andy
**Scope:** `CheckAxisLimits()` (Mount.Lifecycle.cs), `SetSteps(double[])` (Mount.cs), and the code that feeds or consumes them (timer loop, slew controller, ASCOM driver reachability check, UI limit icon).
**Method:** Static read-only review. No code was edited, no packages were inspected or decompiled.

## 0. Review boundaries (read this first)

- **Not reviewed (per your constraint):** the mount-specific operation code for Simulator and SkyWatcher, including `SimGoTo`, `SkyGoTo`, `SimTasks`, `SkyTasks`, the axis/queue commands, and the Simulator/SkyWatcher projects.
- **Consequence:** I cannot say whether the GoTo loops, hand-controller moves or axis commands enforce limits internally. Every "not enforced" statement below means "not enforced in the shared logic I reviewed". Where a conclusion depends on unreviewed code it is marked **[UNVERIFIED]**.
- **Disclosure:** one repo-wide text search returned a single matching line from `GreenSwamp.Alpaca.Simulator/Actions.cs` (a `MoveAxisRate` method signature). I did not open that file. `Mount.Tracking.cs` and `Mount.Serial.cs` were read only around the limit-related call sites, which include the per-mount-type dispatch in `SetTracking()`.
- **Requirement numbering:** your list has two items numbered "3". I use R1, R2, R3a (GEM yellow), R3b (Polar/AltAz yellow) and R4.

## 1. How the limit logic works today

### 1.1 Data path

| Step | Where | Detail |
|---|---|---|
| 1 | Hardware steps arrive | `ReceiveSteps()` calls `SetSteps()` (Mount.cs 1457-1462) |
| 2 | `SetSteps()` | Converts steps to mount-frame degrees, then sets four `_limitStatus` booleans (Mount.cs 1409-1423) |
| 3 | `SetSteps()` | Converts to app axes (`_appAxes`) and Alt/Az (`_altAzm`) (Mount.cs 1429-1439) |
| 4 | `CheckAxisLimits()` | Reads `_limitStatus`, `_appAxes.X` and `_altAzm.Y`, then decides whether a limit is active (Mount.Lifecycle.cs 37-95) |
| 5 | `HandleLimitAction()` | Performs None, StopTracking or Park (Mount.Lifecycle.cs 104-148) |
| 6 | State publish | `LimitTriggered`, `LimitWarningActive`, `LimitWarningMessage` and `LimitWarningSequence` are copied into `TelescopeStateModel` (TelescopeStateService.cs 128-134) |
| 7 | UI icon | `GetLimitIconColor()` gives Default (limits off), Success (green) or Error (red) (MountControl.razor.cs 171-181) |

`CheckAxisLimits()` has only two callers:

1. `OnUpdateServerEvent`, the MediaTimer tick (Mount.Serial.cs 760). It runs **before** `UpdateSteps()` in the same tick (line 763).
2. `SkyGetRate()`, invoked on every `SetTracking()` (Mount.Tracking.cs 260), including the AltAz predictor timer.

`SetSteps()` itself never calls `CheckAxisLimits()`.

### 1.2 What `SetSteps()` computes

- X axis: `AtLowerLimitAxisX = raw[0] <= -AxisLimitX - 1"` and `AtUpperLimitAxisX = raw[0] >= AxisLimitX + 1"`. So the flags are true only when the axis is **more than 1 arcsecond beyond** the limit.
- Y axis: the same pattern against `AxisLowerLimitY` and `AxisUpperLimitY`, mirrored for Polar mode with `PolarMode.Left`.
- The Polar upper Y limit is silently increased by `abs(Latitude)` in the settings getter (MountControl/SkySettings.cs 1408-1411). The UI edits the raw value.
- These flags are the only "hardware limit" signal in the system.

### 1.3 What `CheckAxisLimits()` computes

| Limit type | Condition | Applies to |
|---|---|---|
| Hardware | any `_limitStatus` flag | Polar, AltAz (forced false for GEM, line 63) |
| Meridian | `_appAxes.X >= HourAngleLimit + AxisTrackingLimit + 180` or `<= -(HourAngleLimit + AxisTrackingLimit)` (hemisphere-mirrored) | GEM only (forced false otherwise, line 67) |
| Horizon | `SideOfPier == Normal && _altAzm.Y <= AxisHzTrackingLimit` | GEM only (forced false otherwise, line 68) |

- The first active limit sets `LimitTriggered = true`, then calls `HandleLimitAction`.
- The primary limit (Meridian for GEM, Hardware otherwise) takes `MeridianTrackingAction` and `ParkLimitName`.
- Otherwise the Horizon branch takes `HorizonTrackingAction` and `ParkHzLimitName`.
- `LimitTriggered` is a single latch. It is cleared only when **no** limit is active (`ClearLimitWarningState`).

## 2. Requirement compliance summary

Legend: **Met** / **Partial** / **Not met** / **Undefined** (the requirement cannot be assessed until clarified).

| Req | GEM | Polar | AltAz |
|---|---|---|---|
| R1 No movement beyond hardware limits | **Not met / Undefined** | **Partial** | **Not met** |
| R2 Action at tracking limit | **Partial** (meridian only) | **Partial** (fires at the hardware limit, not a tracking limit) | **Partial** (as Polar) |
| R2 Action at horizon limit | **Partial** (Normal pier side only) | **Not met** | **Not met** |
| R3a Yellow zone, flip HA to tracking limit | **Not met** | n/a | n/a |
| R3b Yellow at tracking-limit degrees from the hardware limit | n/a | **Not met** | **Not met** |
| R4 Red at limit (stop: stays red) | **Partial** | **Partial** | **Partial** |
| R4 Park: red then yellow then green | **Not met** (no yellow) | **Not met** | **Not met** |

## 3. Implementation gaps

Severity: **Critical** means it can let the mount exceed a limit or defeat the configured action. **High** means a requirement is not delivered. **Medium** means it is incorrect or fragile in edge cases. **Low** means naming or consistency.

### 3.1 Safety gaps (R1)

**G1. Detection is reactive; it fires only after the limit is exceeded. [Critical]**
- `SetSteps()` flags a hardware limit only when position is beyond the limit plus 1" (Mount.cs 1412-1413, 1421-1422).
- Nothing predicts the position or leaves a stopping margin. The mount has already exceeded the limit when the action starts, which contradicts R1.
- For GEM, the meridian check also fires at the limit value itself (HAL + TL), with no deceleration margin.

**G2. Detection latency and stale data. [Critical]**
- `CheckAxisLimits()` runs on the MediaTimer tick. `appsettings.json` sets `CheckInterval` to 2000 ms (lines 120, 266, 412). It also runs on `SetTracking()` calls, which fire at the 2500 ms AltAz tracking interval or when rates change.
- In the tick, the check runs **before** `UpdateSteps()` (Mount.Serial.cs 760 then 763), and `UpdateSteps()` is asynchronous. The check therefore uses positions from the previous cycle or older.
- At `MaximumSlewRate` 3.5 (units not confirmed; if deg/s) the mount could travel several degrees past a limit before the check sees it.
- `SetSteps()` is the point where fresh position arrives, but it never triggers the check.

**G3. The slew path has no limit gate in the shared code. [Critical]**
- `SlewController.SetupPhaseAsync` (SlewController.cs 350-393) validates only "mount running" and residual motion. There is no target or path limit check.
- `IsTargetReachable()` returns **true unconditionally for GEM** (Mount.cs 1130) and **false** for Park, Home and MoveAxis slew types (lines 1139-1141).
- `IsTargetWithinLimits()` (Mount.Position.cs 182-194) is used only by the Polar alternate-position logic and `IsTargetReachable()`.
- The driver's `CheckReachable()` (Telescope.cs 2014-2031) runs **only for Polar** (line 2023). AltAz and GEM targets are never checked against `AxisLimitX` or the Y limits. Park and Home targets are not checked for any mount type.
- GEM and AltAz targets are partly constrained by coordinate normalisation (`GetAlternatePosition*`), but that is not a limit check.
- **[UNVERIFIED]** Whether `SimGoTo` or `SkyGoTo` clamp or abort at limits.

**G4. Axis moves are not gated or stopped by limits. [Critical]**
- `RateMovePrimaryAxis` and `RateMoveSecondaryAxis` (Mount.cs 694-743) issue the axis command with no limit check.
- The hand-controller methods (`HcMoves`, `HcPulseMoveAsync`) were not traced into the mount-specific send code, so their behaviour is **[UNVERIFIED]**.
- Once a limit is reached, the StopTracking action does not stop these moves (see G6).

**G5. `LimitsOn = false` disables hardware-limit protection entirely. [Critical, needs your decision, see C2]**
- `CheckAxisLimits()` returns immediately when `LimitsOn` is false (Mount.Lifecycle.cs 39-43).
- The Limits editor labels the hardware limit values "always enforced for slew safety" (LimitsEditor.razor line 7).
- Per the code, tracking and moves can run into the hardware stops whenever Limits is off. Only the Polar ASCOM reachability check is independent of `LimitsOn`.

**G6. The StopTracking action does not stop motion. [Critical]**
- `HandleLimitAction` acts only if `TrackingMode != Off` (Mount.Lifecycle.cs 109-114).
- During any slew, `SlewOperation.Prepare()` has already switched tracking off (SlewController.cs 680), so the check is false and the action falls through as "Warning only".
- During a rate move, `ApplyTracking(false)` does not zero `_rateMoveAxes`. `SetTracking` simply skips the axis whose rate is non-zero (Mount.Tracking.cs 209, 216).
- `MountStopAxes()` (which cancels GoTo and zeroes rates) is used only when a park target is missing (line 122).
- The log text is misleading too. It reports "Warning only" for a mount configured to Stop.
- Net effect: a limit reached during a GoTo or MoveAxis triggers a warning but not a stop.

**G7. The latch lets tracking be re-enabled beyond a limit. [Critical]**
- `LimitTriggered` stays true while any limit remains active, and `CheckAxisLimits()` returns early on subsequent calls (lines 80-83).
- If tracking is re-enabled while still beyond the limit, no new action fires and the mount tracks on toward the hard stop.
- `ApplyTracking` / `ApplyTrackingCore` (Mount.Lifecycle.cs 214-251) contain no limit check, so nothing refuses the re-enable.
- **[UNVERIFIED]** The ASCOM `Tracking` setter was not read.

**G8. GEM hardware limits are never evaluated. [High]**
- The Hardware limit is forced false for GEM (line 63). `AxisLimitX` and `AxisUpper/LowerLimitY` are exposed in the UI for GEM, but changing them has no effect on tracking.
- Template defaults are 180, ±180 and ±180, so the omission is currently invisible. The "hardware limit" for GEM is only ever the meridian formula.

**G9. Y-axis hardware limits are not checked at slew time for AltAz or GEM. [High]**
- Only Polar slews are checked, and only through the driver path (G3).
- AltAz default Y limits are -10 and 80 degrees. A slew target outside them is not rejected by shared code.

### 3.2 Action gaps (R2)

**G10. The Horizon tracking limit is not implemented for Polar and AltAz. [High]**
- It is forced false for non-GEM modes (line 68). The Limits editor shows `AxisHzTrackingLimit` and the Horizon action only when `IsGem` (LimitsEditor.razor 29-43, 68-89).
- The USER-GUIDE (lines 511-514) describes a horizontal tracking limit for AltAz that the code does not honour.

**G11. The GEM horizon limit applies only on the Normal pier side. [Medium]**
- The condition is `SideOfPier == PointingState.Normal && _altAzm.Y <= AxisHzTrackingLimit` (lines 57-58). On the ThroughThePole side the horizon limit never fires. I could find no documented reason for this (see C5).

**G12. A single latch and a single primary-wins decision. [High]**
- If Meridian and Horizon are active together, only the primary action runs and the horizon action is never executed.
- If Horizon fires first and latches, a later Meridian limit is ignored until all limits clear.
- Requirement R2 expects each limit's own action to fire.

**G13. The Polar/AltAz "tracking limit" does not exist. [High, see C3]**
- `AxisTrackingLimit` is used only inside the GEM meridian sum (line 45). For Polar and AltAz the action fires at the hardware limit (plus 1"), not at "tracking limit degrees" from anything.
- The setting is hidden in the UI for non-GEM modes.

**G14. The park action is fire-and-forget. [High]**
- `StartGoToParkAsync()` discards the `SlewResult` (`_ = SlewAsync(...)`, Mount.Lifecycle.cs 190).
- `ExecuteSlewAsync` returns a failure (not an exception) when another client owns the active slew (SlewController.cs 175-181).
- It also returns a failure when `SetupPhaseAsync` cannot stop previous motion (SlewController.cs 375-383).
- In both cases the log says "Started park", `warningStaysOn` is set false, and the latch prevents a retry. The mount keeps moving and the UI shows no warning.
- The limit-triggered park uses the default client id `0:0.0.0.0:0`, so it cannot pre-empt another client's slew.
- A park target is mapped and slewed without a limit check.

**G15. Concurrency. [Medium]**
- `CheckAxisLimits()` can be entered from the MediaTimer thread, the tracking-consumer thread, the AltAz timer and ASCOM request threads. `LimitTriggered`, `_limitStatus` (four separately written struct fields), `_appAxes` and `_altAzm` are read and written without synchronisation.
- Two threads can both see `LimitTriggered == false` and run the action twice.
- The re-entry via `HandleLimitAction` then `ApplyTracking` then `SkyGetRate` then `CheckAxisLimits` is harmless only because the latch is set first (line 85). That ordering is fragile.

**G16. No hysteresis or debounce. [Medium]**
- The latch clears the moment the position is back inside the limit. A position oscillating around the boundary could re-fire stop or park actions repeatedly.

### 3.3 UI/state gaps (R3 and R4)

**G17. There is no "approaching limit" state at all. [High]**
- The icon has three colours: Default, Success, Error (MountControl.razor.cs 171-181).
- No code computes a warning zone for GEM (flip HA to HA + TL) or for Polar/AltAz (hardware limit minus TL).
- `TelescopeStateModel` carries only booleans (`LimitsOn`, `LimitTriggered`, `LimitWarningActive`), so R3a, R3b and the yellow phase of R4 cannot be expressed.
- `LimitWarningActive` is not a proximity flag. It is "action taken and warning stays on", and it is false when a park is started (line 129).

**G18. Red timing does not match "reached a limit". [Medium]**
- Red follows `LimitTriggered`. For hardware limits it appears only after the limit is exceeded by more than 1" (G1). For GEM it appears at HAL + TL.
- "Reached" (at the limit) versus "exceeded" needs a definition (see C10).

**G19. The park colour sequence is not delivered. [High]**
- After a park action starts, `LimitTriggered` stays true (red) until the mount leaves the limit region, then clears directly to green. No yellow stage exists.
- If the park position lies inside the limit region, the icon stays red.

**G20. The UI snapshot is sampled. [Low]**
- `BuildSnapshot` runs per timer tick. A short-lived state (for example a quick park) can be missed by the UI.

### 3.4 Consistency and naming

**G21. `MeridianTrackingAction` and `ParkLimitName` are used for hardware limits on Polar and AltAz. [Low]**
- The UI relabels the control by mode (LimitsEditor.razor 119-125), but the setting names and the log source string "Hardware Limit" differ from what the key says.

**G22. Tolerance inconsistencies. [Low]**
- `SetSteps()` uses a 1" margin on X and Y. `IsTargetWithinLimits()` applies the 1" margin to X only (Mount.Position.cs 192-193).
- The polar upper Y limit includes `+abs(Latitude)` in the getter, which is invisible to the user in the editor.

**G23. `HorizonLimit` (Horizon Go To Limit) is not enforced in the shared logic I reviewed. [Medium]**
- It appears in settings and the GoTo panel, but the searched `MountControl` and `TelescopeDriver` code contains no use. **[UNVERIFIED]** for the mount-specific code.

**G24. No automated tests for limits. [Medium]**
- The test project contains `QueueMigrationTests.cs` and `CarouselSettingsTests.cs` only. There is no coverage of limit logic.

## 4. Requirement clarifications needed

Please decide these before implementation. Each item lists what the code does today.

**C1. What is the "hardware limit" for a GEM?**
- Today it is not evaluated (G8). The meridian limit (HAL + TL) is the only hard boundary.
- Options: (a) hardware limit = the meridian limit, (b) honour `AxisLimitX` and `AxisUpper/LowerLimitY` for GEM as well, (c) both.

**C2. Must hardware limits be enforced when `LimitsOn` is false?**
- Today everything is off (G5). The UI text and R1 suggest hardware limits should always apply, with `LimitsOn` controlling only the tracking and horizon actions.

**C3. For Polar and AltAz, where does the action fire relative to the tracking limit?**
- GEM: warn from HAL, act at HAL + TL. Polar/AltAz per R3b: warn at TL degrees before the hardware limit.
- Does the action fire at the hardware limit, or earlier, at (hardware limit − stopping margin)? If at the hardware limit, R1 cannot hold because the mount cannot stop instantly.
- What role should `AxisTrackingLimit` play for Polar and AltAz (it is unused and hidden today)?

**C4. Is R1 a hard guarantee or best-effort?**
- A hard guarantee needs pre-emptive checks: target validation before slews, rate-aware stop margins, and checks on every position update instead of every 2 s.
- What is the acceptable overshoot, if any?

**C5. Horizon limit scope.**
- Should the horizon tracking limit apply to all three mount types (R2 says yes)? Which altitude does it use?
- Why is it restricted to the Normal pier side for GEM today? Should it apply on both sides?
- How do `AxisHzTrackingLimit` (tracking) and `HorizonLimit` (Go To) relate? Should the Go To limit also reject slew targets?

**C6. Meaning of "stop".**
- Stop tracking only, or stop all motion (slew, MoveAxis, hand controller, pulse guide)?
- Should re-enabling tracking while beyond the limit be refused (G7)?
- Should motion **away** from the limit always be allowed?

**C7. Meaning of "none".**
- Warning only, with the icon red. Confirm this is intended for all mount types.

**C8. Park action behaviour.**
- If the park slew cannot start (another client owns the slew, or an axis move is active), what should happen (retry, stop axes, warn)?
- Should a limit park pre-empt another client's slew?
- Must the park target itself be outside the limit zone? What should the icon show if it is not?
- What happens after park completes while still inside the zone?

**C9. Behaviour when multiple limits are active.**
- Precedence, or run both actions? One latch or independent latches per limit type (G12)?

**C10. Icon state machine.**
- Please confirm or amend: **Green** = outside the warn zone. **Yellow** = inside the warn zone, not yet at the limit (R3a/R3b), and also while recovering from a limit after a park. **Red** = at or beyond the limit. **Default/grey** = limits off.
- "Reached" in R4: at the limit, or strictly beyond it (G18)?
- Is there hysteresis on leaving red or yellow?
- Does yellow show only while tracking, or whenever the position is inside the zone (stationary, slewing through)?

**C11. GEM yellow zone geometry.**
- Both pier sides? What happens when `AxisTrackingLimit` is 0 (a zero-width zone)?
- Should the zone mirror for the southern hemisphere (the formula already does)?

**C12. Slews that cross or end in the zone.**
- Should a GoTo into the warn zone be allowed (yellow)? Into the action zone (reject, allow, or clamp)?
- Should the limit action fire during a slew that is legitimately passing through?

**C13. Detection cadence.**
- Is a check on every position update (`ReceiveSteps`/`SetSteps`) acceptable?
- Is a faster dedicated limit timer acceptable? Is `CheckInterval` allowed to remain 2000 ms?

**C14. State contract for the UI.**
- May `TelescopeStateModel` gain a limit state enum (for example None, Warn, AtLimit, Recovering) and optionally the distance to the limit?
- What should `LimitWarningActive` mean in future?

**C15. Settings semantics.**
- May `MeridianTrackingAction`/`ParkLimitName` be renamed or documented as the primary limit action for all modes?
- Should the Limits editor show tracking-limit and horizon fields for Polar and AltAz?

**C16. Test approach.**
- May unit tests drive `SetSteps()` / `CheckAxisLimits()` with synthetic positions, without touching mount-specific code?
- Which matrix do you want covered: 3 mount types × north/south hemisphere × Polar Left/Right × the three actions?

## 5. Suggested order for resolving the gaps (for discussion only)

1. Settle the clarifications (C1 to C4, C6, C10 first); they drive the design.
2. Define a single limit-evaluation model (distance to each limit, zone classification, per mount type) fed by `SetSteps()`.
3. Evaluate on every position update, with stop margins.
4. Gate slews, park targets and axis moves against the model.
5. Make "stop" really stop motion, and make the park result observable.
6. Expose the state enum to the UI and add yellow.
7. Add tests for the matrix in C16.

## 6. What already works

- The action selection and the log/notification plumbing are in place (`LimitWarningSequence` drives toasts).
- A missing park position falls back to `MountStopAxes()`.
- The GEM meridian formula handles both pier sides and the southern hemisphere.
- Polar ASCOM slews are validated against the hardware limits before moving (`CheckReachable`).
- Recursion between `HandleLimitAction` and `SkyGetRate` is guarded by setting the latch first.