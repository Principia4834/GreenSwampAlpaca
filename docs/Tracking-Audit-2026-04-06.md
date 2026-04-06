# Tracking Functionality Audit
**Generated:** 2026-04-06 19:08
**Scope:** Simulator · GermanPolar · Northern Hemisphere · Device-00 (Sections 1–13)  
**Extended:** SkyWatcher · AltAz · Device-00 (Sections 14–24)  
**Further extended:** ASCOM V4 Rate Offset Spec Compliance (Section 25)  
**Further extended:** `InstanceSetTrackingMode` deep-dive · Finding 14 correction · Finding 15 (Sections 26–27)  
**Compares:** GreenSwamp.Alpaca (current) vs GSServer (legacy at `T:\source\repos\Principia4834\GSServer`)  
**ASCOM spec ref:** [ITelescope V4 — `RightAscensionRate` / `DeclinationRate`](https://ascom-standards.org/newdocs/telescope.html#Telescope.RightAscensionRate)

---

## 1. Audit Scope and Method

This audit traces the `Telescope.Tracking` property — both getter and setter — through the full call stack for **device-00 in Simulator mode, GermanPolar alignment, northern hemisphere**. Every code path is verified from the ASCOM interface boundary down to the simulator hardware loop. Where the current solution diverges from the legacy GSServer intent, findings are recorded with severity ratings.

---

## 2. Reference Architecture Summary

### Legacy GSServer (single-instance, static)
```
Telescope.Tracking (getter/setter)
  └─► SkyServer.Tracking (static property — one global instance)
        ├─► _tracking (bool field)
        ├─► _trackingMode (TrackingMode enum)
        └─► SetTracking() (private static — sends to hardware)
              └─► CmdAxisTracking / CmdRaDecRate (Simulator queue)
                    └─► Controllers.Command("tracking|axis|rate")
                          └─► MoveAxes() loop: _trackingX · _raDecRateX
```

### Current GreenSwamp (instance-based, device-00)
```
Telescope.Tracking (getter/setter)
  ├─ getter: inst = MountInstanceRegistry.GetInstance(0)
  │    └─► inst.Tracking (→ _tracking bool field)
  │    └─► inst.SlewState (→ _slewState enum)
  │
  └─ setter: inst.ApplyTracking(value)
       └─► InstanceApplyTracking(bool tracking)
             ├─► _tracking = tracking
             ├─► if ON:  InstanceSetTrackingMode()  → _trackingMode = EqN
             │           SkyPredictor NOT reset here (intentional — N5)
             ├─► if OFF: SkyPredictor.Reset()
             │           _trackingMode = TrackingMode.Off
             └─► SkyServer.SetTracking(this)   [static, private]
                   └─► CmdAxisTracking / CmdRaDecRate (Simulator queue)
                         └─► Controllers.Command("tracking|axis|rate")
                               └─► MoveAxes() loop: _trackingX · _raDecRateX
```

---

## 3. Tracking Getter — Full Trace

### 3.1 Legacy GSServer

**`Telescope.Tracking` getter (GS.Ascom.SkyTelescope\Telescope.cs:1055):**
```csharp
var r = SkyServer.Tracking || SkyServer.SlewState == SlewType.SlewRaDec;
```

**`SkyServer.Tracking` getter (SkyServer.cs:1484):**
```csharp
get => _trackingMode != TrackingMode.Off;
```
The ASCOM getter reads the *tracking mode enum*, not the boolean backing field. It returns `true` whenever the mount is in any active tracking mode (EqN, EqS, or AltAz).

**`SkyServer.SlewState` (SkyServer.cs):**
A static field — one global state. The `|| SlewState == SlewRaDec` clause ensures clients see `Tracking = true` during an RA/Dec GoTo even before `_trackingMode` is set.

### 3.2 Current GreenSwamp

**`Telescope.Tracking` getter (Telescope.cs — highlighted line):**
```csharp
var r = inst.Tracking || inst.SlewState == SlewType.SlewRaDec;
```

**`inst.Tracking` (MountInstance.cs:238):**
```csharp
public bool Tracking => _tracking;
```
The getter reads `_tracking` — the **boolean backing field**, NOT `_trackingMode != Off`.

**`SkyServer.Tracking` static getter (SkyServer.TelescopeAPI.cs:283):**
```csharp
get => (_defaultInstance?.TrackingMode ?? TrackingMode.Off) != TrackingMode.Off;
```
The static getter reads `_trackingMode`, consistent with the legacy. However, the ASCOM Telescope interface now reads `_tracking` via `inst.Tracking`.

### 3.3 Getter Analysis

Both `_tracking` and `_trackingMode != Off` are kept in sync through `InstanceApplyTracking` and `CheckAxisLimits`. Under normal conditions they are equivalent. However:

- There is an architectural split: the ASCOM interface (`Telescope.Tracking`) reads `_tracking` (bool), while internal SkyServer machinery (e.g. PEC: `if (PecOn && Tracking && ...)`) reads `_trackingMode` via the static getter.
- A future code path that sets one without the other could create a silent divergence invisible to both the ASCOM client and the UI.
- The `|| SlewState == SlewRaDec` clause is present and correct in both versions.

**Verdict on getter: Functionally equivalent. Structural split is a latent risk.**

---

## 4. Tracking Setter — Full Trace

### 4.1 Legacy GSServer Setter Path

**`Telescope.Tracking` setter (Telescope.cs:1073):**
```csharp
SkyServer.Tracking = value;
```

**`SkyServer.Tracking` setter (SkyServer.cs:1485–1551):**

| Step | Code | Notes |
|------|------|-------|
| 1 | `if (value == _tracking) { OnStaticPropertyChanged(); return; }` | Early exit — fires notification even on no-op |
| 2 | `SkyPredictor.Reset();` | **Always** resets predictor, even when turning ON |
| 3 (ON) | `SetTrackingMode()` | Sets `_trackingMode = EqN` (northern GermanPolar) |
| 3 (ON) | GermanPolar case: no-op (no AltAz predictor needed) | |
| 3 (OFF) | `IsPulseGuidingDec = false; IsPulseGuidingRa = false;` | Clears pulse flags |
| 3 (OFF) | `_trackingMode = TrackingMode.Off;` | Mode cleared |
| 4 | `_tracking = value;` | Bool backing field set |
| 5 | `SetTracking();` | Applies to simulator hardware |
| 6 | `OnStaticPropertyChanged();` | **Always** fires UI/binding notification |

### 4.2 Current GreenSwamp Setter Path

**`Telescope.Tracking` setter (Telescope.cs):**
```csharp
inst.ApplyTracking(value);
```

**`MountInstance.ApplyTracking(bool value)` (MountInstance.cs):**
```csharp
public void ApplyTracking(bool value) => InstanceApplyTracking(value);
```

**`InstanceApplyTracking(bool tracking)` (MountInstance.cs):**

| Step | Code | Notes |
|------|------|-------|
| 1 | `if (tracking == _tracking) return;` | Early exit — **does NOT fire `OnStaticPropertyChanged()`** ⚠️ |
| 2 | `_tracking = tracking;` | Bool backing field set |
| 3 (ON) | `InstanceSetTrackingMode()` | Sets `_trackingMode = EqN` (northern GermanPolar) |
| 3 (ON) | GermanPolar: no predictor work | |
| 3 (OFF) | `SkyPredictor.Reset();` | Reset deferred to OFF path only (intentional N5) |
| 3 (OFF) | `_isPulseGuidingRa/Dec = false;` | Clears pulse flags |
| 3 (OFF) | `_trackingMode = TrackingMode.Off;` | Mode cleared |
| 4 | `SkyServer.SetTracking(this);` | Applies to simulator hardware |
| **—** | *(no OnStaticPropertyChanged call)* | **Missing notification** ⚠️ |

### 4.3 Setter Analysis — Key Differences

#### FINDING 1 (Medium): `OnStaticPropertyChanged()` not called via `ApplyTracking` path

The static `SkyServer.Tracking` property setter always calls `OnStaticPropertyChanged()`, which triggers UI data-binding updates. `InstanceApplyTracking` never calls it. When `Telescope.Tracking` is set by an ASCOM client, Blazor UI components bound to `SkyServer.Tracking` will not receive a change notification.

The legacy code fires the notification even on early exit (no-op), ensuring the UI is always told the current value. The new code silently swallows both the change and the no-op.

#### FINDING 2 (Medium): The `SkyServer.Tracking` static setter is bypassed

The ASCOM path (`Telescope.Tracking` setter → `inst.ApplyTracking`) does not go through `SkyServer.Tracking = value`. Instead it calls the private `SkyServer.SetTracking(this)` directly. This means two separate setter paths exist in the codebase:

| Path | Caller | Fires `OnStaticPropertyChanged`? |
|------|--------|----------------------------------|
| `SkyServer.Tracking = value` | `CycleOnTracking()`, internal | ✅ Yes |
| `inst.ApplyTracking(value)` | `Telescope.Tracking` setter, `Telescope.Unpark()` | ❌ No |

Both paths call `SkyServer.SetTracking(this)` at the hardware layer so the mount *behaves* correctly, but the two paths have different side effects on UI state.

#### FINDING 3 (Low/Positive): SkyPredictor.Reset() timing improved

Legacy: `SkyPredictor.Reset()` always called, including when turning tracking ON. This discards any existing predictor state even during a re-enable (e.g. after a GoTo that briefly paused tracking).

New: `SkyPredictor.Reset()` called only on OFF path. On ON path the predictor is preserved or its reference time refreshed (`SkyPredictor.ReferenceTime = DateTime.Now`). This is an intentional and documented improvement (comment: "N5: reset on tracking OFF only — never on re-enable").

---

## 5. SetTracking() Hardware Commands — Simulator + GermanPolar + North

This is `SkyServer.SetTracking(MountInstance inst)` in SkyServer.TelescopeAPI.cs. Both versions produce the same hardware commands for this configuration.

### Tracking ON (EqN mode, sidereal rate):

| Command | Legacy | New | Effect |
|---------|--------|-----|--------|
| `CmdAxisTracking(Axis1, siderealRate)` | ✅ (if `!MovePrimaryAxisActive`) | ✅ (if `!MovePrimaryAxisActive`) | Sets `_trackingX = siderealRate` in simulator |
| `CmdRaDecRate(Axis1, GetRaRateDirection(raOffset))` | ✅ unconditional | ✅ unconditional (when `TrackingMode != Off`) | Sets `_raDecRateX` = RA offset rate |
| `CmdRaDecRate(Axis2, GetDecRateDirection(decOffset))` | ✅ if `!MoveSecondaryAxisActive` | ✅ if `!MoveSecondaryAxisActive` | Sets `_raDecRateY` = Dec offset rate |

### Tracking OFF:

| Command | Legacy | New | Effect |
|---------|--------|-----|--------|
| `CmdAxisTracking(Axis1, 0)` | ✅ | ✅ | Zeroes `_trackingX` |
| `CmdRaDecRate(Axis1, GetRaRateDirection(raOffset))` | ✅ **unconditional** — sends current offset | ✅ **sends 0** (`currentTrackingMode == Off`) | **Subtle difference below** |
| `CmdRaDecRate(Axis2, GetDecRateDirection(decOffset))` | ✅ unconditional | ✅ **sends 0** when tracking Off | Same subtle difference |

**Behavioural difference on tracking OFF with active offset rates — now spec-compliant:**  
The ASCOM ITelescope V4 specification (formalised in V4) states that `RightAscensionRate` and `DeclinationRate` **apply only when the mount is tracking at sidereal rate**. When tracking is off, offset rates must not be applied to the hardware.

| Behaviour | Legacy | New | Spec compliance |
|-----------|--------|-----|-----------------|
| Tracking OFF + non-zero RA/Dec rate offset | Sends non-zero rate to hardware — axis drifts | Sends 0 — axis stops | Legacy ❌ non-compliant; New ✅ compliant |

**Implication for Finding 7:** What was described as a subtle difference that is "arguably more correct" is in fact a straightforward **spec compliance fix**. The legacy behaviour of continuing to apply offset rates when tracking is off violates the ITelescope V4 contract. See Section 25 for the full spec analysis.

### Simulator MoveAxes loop (Controllers.cs):
```
changeX += rdrX  (RaDecRate — offset tracking)
         + trkX  (Tracking — sidereal rate)
```
Both contribute to RA axis motion. Neither is applied when `_trackingX == 0` and `_raDecRateX == 0`.

---

## 6. Unpark Tracking Behaviour

### 6.1 Legacy Unpark
```csharp
SkyServer.AtPark = false;
if (SkySettings.TrackAfterUnpark)
    SkyServer.Tracking = (AlignmentMode != AlignmentModes.algAltAz);
else
    SkyServer.Tracking = false;
```
The legacy respects the `TrackAfterUnpark` user setting. For GermanPolar this gives the user a choice: unpark tracking on or off.

### 6.2 New Unpark
```csharp
inst.AtPark = false;
inst.ApplyTracking(AlignmentMode != AlignmentMode.AltAz);
```

#### FINDING 4 (High): `TrackAfterUnpark` user setting is ignored

The new `Unpark()` **always** enables tracking for GermanPolar regardless of user preference. The `TrackAfterUnpark` setting existed in GSServer and allowed planetarium-controlled workflows where the client software manages tracking state after unpark.

The new settings model uses `AutoTrack` (in `SkySettings.cs`) rather than `TrackAfterUnpark`, but `Unpark()` does not consult either setting — it hardcodes the behaviour.

**Impact:** Any user who previously set `TrackAfterUnpark = false` (common for fully automated imaging sessions where the planetarium software drives tracking) will find the mount starts tracking immediately on unpark, which may cause unexpected slew behaviour if the mount is still in its parked position.

---

## 7. Object Identity — `_objectId` Never Assigned

### 7.1 Legacy
```csharp
_objectId = SkySystem.GetId();  // unique long per instance
// ...
SkySystem.SetConnected(_objectId, value);
```
Each `Telescope` COM object received a unique ID. `SetConnected` used this to track how many clients were connected and defer disconnect until the last client disconnects.

### 7.2 New
```csharp
private readonly long _objectId;  // declared — never assigned → always 0
// ...
GetInstance().SetConnected(_objectId, value);  // always passes 0
```

#### FINDING 5 (Medium): `_objectId` is always 0 — multi-client connection tracking broken

If multiple ASCOM clients connect simultaneously, they all pass `_objectId = 0` to `SetConnected`. The connection reference counting depends on unique IDs; with all IDs = 0, `Disconnect()` from any one client may disconnect all. The `SkySystem.GetId()` call was present in the legacy constructor and has been dropped without replacement.

---

## 8. `AsComOn` Guard Removal

### 8.1 Legacy
Many methods (Tracking setter, MoveAxis, SlewToCoordinates*, Park, FindHome, etc.) started with:
```csharp
if (!SkyServer.AsComOn) return;
```
This allowed the server to safely ignore ASCOM commands when the mount system was not yet ready or was shutting down.

### 8.2 New
These guards are absent. The new code relies on `GetInstance()` throwing `InvalidOperationException` if device-00 is not registered, but this throws rather than silently returning, which changes the contract for ASCOM clients.

#### FINDING 6 (Low): Command rejection behaviour changed from silent return to exception

Callers expecting a graceful no-op when the server is not ready will now receive an exception. This is a breaking change in ASCOM interface behaviour. Whether it is an improvement depends on the ASCOM spec interpretation (the spec generally prefers exceptions over silent discards).

---

## 9. `Tracking` Setter — Park Check

Both versions use the bitwise `&` operator for the park check:
```csharp
if (value & inst.AtPark) { CheckParked(...); }  // new
if (value & SkyServer.AtPark) { CheckParked(...); }  // legacy
```
The non-short-circuit `&` evaluates both sides unconditionally. This is idiomatic to both codebases and is not a defect, but `&&` would be more conventional C#.

---

## 10. `SkyServer.Tracking` Static Getter — Dual Backing Value Risk

| Context | Property | Backing value | Notes |
|---------|----------|--------------|-------|
| ASCOM interface | `inst.Tracking` | `_tracking` (bool) | Used by Telescope.cs |
| Internal/PEC | `SkyServer.Tracking` (static) | `_trackingMode != Off` | Used by SetTracking, PEC |
| UI bindings | `SkyServer.Tracking` (static) | `_trackingMode != Off` | Blazor data binding |

All three are normally in sync. However, any code path that sets `_tracking` without setting `_trackingMode` (or vice versa) will cause silent divergence. The risk is most acute in future refactoring since the two backing values are in different classes (`MountInstance` vs implicit `TrackingMode` in same `MountInstance`).

---

## 11. Summary of Findings

| # | Severity | Finding | File | Line area |
|---|----------|---------|------|-----------|
| 1 | **Medium** | `OnStaticPropertyChanged()` not called when tracking toggled via ASCOM `Telescope.Tracking` setter — UI bindings not notified | `MountInstance.cs` | `InstanceApplyTracking` |
| 2 | **Medium** | Two parallel setter paths (`SkyServer.Tracking = v` vs `inst.ApplyTracking(v)`) have different side effects; no single authoritative toggle point | `SkyServer.TelescopeAPI.cs` / `MountInstance.cs` | static setter vs instance method |
| 3 | **Positive** | `SkyPredictor.Reset()` correctly deferred to OFF path only — preserves predictor state on re-enable | `MountInstance.cs` | `InstanceApplyTracking` |
| 4 | **High** | `Unpark()` ignores `TrackAfterUnpark` user setting; always enables tracking for GermanPolar | `Telescope.cs` | `Unpark()` |
| 5 | **Medium** | `_objectId` always 0 — connection reference counting broken for multi-client ASCOM scenarios | `Telescope.cs` | Constructor |
| 6 | **Low** | `AsComOn` guard removed — command rejection changed from silent no-op to thrown exception | `Telescope.cs` | Multiple methods |
| 7 | **Positive** | Offset rates zeroed on tracking OFF — legacy applied non-zero rates when tracking off (spec-non-compliant with ITelescope V4); new correctly zeros them (spec-compliant). See Section 25. | `SkyServer.TelescopeAPI.cs` | `SetTracking()` |

---

## 12. Hardware Outcome — Simulator GermanPolar North

For the specific test case of **Tracking = true** with no offset rates (the normal tracking-on case):

Both legacy and current code end up issuing identical simulator commands:
```
tracking|axis1|<siderealRateInDeg/s>   → _trackingX = siderealRate
radecrate|axis1|0.0                    → _raDecRateX = 0
radecrate|axis2|0.0                    → _raDecRateY = 0
```

The simulator `MoveAxes()` loop then adds `trkX * seconds` to axis-1 each 20 ms tick, producing correct sidereal tracking motion. There is no observable difference at the hardware level for this base case.

---

## 13. Recommended Actions

1. **Fix Finding 4 (High):** Restore `TrackAfterUnpark` / `AutoTrack` setting check in `Telescope.Unpark()`. Suggested fix:
   ```csharp
   inst.AtPark = false;
   inst.ApplyTracking(inst.Settings.AutoTrack && AlignmentMode != AlignmentMode.AltAz);
   ```

2. **Fix Finding 1 (Medium):** After `SkyServer.SetTracking(this)` in `InstanceApplyTracking`, add `SkyServer.OnStaticPropertyChanged()` (or make it internal) to ensure UI bindings are refreshed.

3. **Fix Finding 5 (Medium):** Assign `_objectId` in the `Telescope` constructor using a thread-safe counter or `MountInstanceRegistry` mechanism.

4. **Consider Finding 2 (Medium):** Consolidate tracking-toggle logic into a single authoritative method. `InstanceApplyTracking` is the best candidate; ensure it handles all side effects currently spread across both paths.

5. **Document Finding 6 (Low):** The exception-on-not-ready behaviour should be explicitly documented as an intentional breaking change from the legacy silent-return pattern.

---

*End of original audit (GermanPolar/Simulator) — 2026-04-06 12:34*

---

## 14. Extension Scope — AltAz + SkyWatcher

This extension traces `Telescope.Tracking` for **device-00 in SkyWatcher mode, AltAz alignment**. It covers the full call stack from the ASCOM boundary through to the SkyWatcher hardware queue, with particular attention to `SkyPredictor` — the continuous prediction engine that computes time-varying Alt/Az slew rates to compensate for the rotation of the sky.

---

## 15. AltAz Architecture Summary

### Legacy GSServer (static AltAz)
```
Telescope.Tracking = true
  └─► SkyServer.Tracking setter
        ├─► SkyPredictor.Reset()                   ← ALWAYS on every toggle
        ├─► SetTrackingMode() → _trackingMode = AltAz
        ├─► AltAzTrackingMode = Predictor
        ├─► if (!RaDecSet) SkyPredictor.Set(RA, Dec, 0, 0)
        │   else          SkyPredictor.ReferenceTime = DateTime.Now
        ├─► SetTracking()
        │     └─► SetAltAzTrackingRates(Predictor)
        │           └─► MountPositionUpdated=false; UpdateSteps()
        │               while (!MountPositionUpdated) Thread.Sleep(10)  ← spin-wait, no timeout
        │               nextTime = Now + AltAzInterval
        │               raDec = SkyPredictor.GetRaDecAtTime(nextTime)
        │               skyTarget = RaDec2AltAz(raDec, nextTime, Latitude)
        │               delta = skyTarget - currentAxes
        │               _skyTrackingRate = delta / interval
        │         StartAltAzTrackingTimer() → wires static AltAzTrackingTimerEvent
        │         SkyAxisSlew(Axis1, rate.X) + SkyAxisSlew(Axis2, rate.Y)
        └─► OnStaticPropertyChanged()

Static AltAz timer tick:
  AltAzTrackingTimerEvent → SetTracking() (no args — _defaultInstance)
```

### Current GreenSwamp (per-instance AltAz)
```
Telescope.Tracking = true
  └─► inst.ApplyTracking(true)
        └─► InstanceApplyTracking(true)
              ├─► _tracking = true
              ├─► InstanceSetTrackingMode() → _trackingMode = AltAz
              ├─► _altAzTrackingMode = Predictor
              ├─► if (!SkyPredictor.RaDecSet)
              │       SkyPredictor.Set(RA, Dec, 0, 0)     ← first-time seed
              │   else
              │       SkyPredictor.ReferenceTime = DateTime.Now  ← preserve existing (N5)
              └─► SkyServer.SetTracking(this)
                    └─► SetAltAzTrackingRates(Predictor, inst)
                          └─► evt.Reset(); inst.UpdateSteps()
                              if (!evt.Wait(5000)) break      ← timeout guard
                              nextTime = Now + AltAzInterval
                              raDec = inst.SkyPredictor.GetRaDecAtTime(nextTime)
                              skyTarget = RaDec2AltAz(raDec, nextTime, Latitude)
                              delta = skyTarget - currentAxes
                              inst._skyTrackingRate = delta / interval
                        inst.StartAltAzTrackingTimer() → wires per-instance AltAzTrackingTimerTick
                        rate = SkyGetRate(inst)
                        SkyAxisSlew(Axis1, rate.X) + SkyAxisSlew(Axis2, rate.Y)

Per-instance AltAz timer tick:
  AltAzTrackingTimerTick(inst) → SkyServer.SetTracking(this)
```

---

## 16. AltAz Tracking Getter — No New Divergences

The getter analysis from Section 3 applies unchanged. For AltAz mode `inst.Tracking` reads `_tracking` (bool) while `SkyServer.Tracking` reads `_trackingMode != Off`. Both are kept in sync by `InstanceApplyTracking` and `CheckAxisLimits`. No new divergences for AltAz mode.

---

## 17. AltAz Tracking Setter — Predictor Seeding Analysis

### 17.1 `InstanceApplyTracking` AltAz Branch (new)

```csharp
// MountInstance.cs — InstanceApplyTracking (AltAz ON path)
_tracking = tracking;
InstanceSetTrackingMode();                     // _trackingMode = AltAz
if (_settings.AlignmentMode == AlignmentMode.AltAz)
{
    _altAzTrackingMode = AltAzTrackingType.Predictor;
    if (!SkyPredictor.RaDecSet)
        SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, 0, 0); // N5: first-time seed
    else
        SkyPredictor.ReferenceTime = DateTime.Now;                      // N5: preserve existing target
}
SkyServer.SetTracking(this);
```

### 17.2 Legacy `SkyServer.Tracking` Setter AltAz Branch

```csharp
// Legacy SkyServer.cs — Tracking setter (AltAz ON path)
SkyPredictor.Reset();                          // ← ALWAYS, before any branch
SetTrackingMode();                             // _trackingMode = AltAz
AltAzTrackingMode = AltAzTrackingType.Predictor;
if (!SkyPredictor.RaDecSet)                   // ALWAYS true — Reset() just cleared Ra/Dec to NaN
    SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, 0, 0);
else
    SkyPredictor.ReferenceTime = DateTime.Now; // UNREACHABLE in legacy — Reset() precedes this
_tracking = value;
SetTracking();
OnStaticPropertyChanged();
```

### 17.3 Critical Divergence — The `else` Branch That Legacy Never Reaches

Because the legacy always calls `SkyPredictor.Reset()` before the `if (!RaDecSet)` check, `RaDecSet` is always `false` at that point (both `_ra` and `_dec` are `Double.NaN` after reset). This means:

| Scenario | Legacy | New |
|----------|--------|-----|
| **First enable** | `Reset()` → `Set(RA, Dec, 0, 0)` | `Set(RA, Dec, 0, 0)` |
| **Re-enable after brief pause (pure sidereal, zero offset rates)** | `Reset()` → `Set(RA, Dec, 0, 0)` — reseeds with fresh position | `ReferenceTime = DateTime.Now` — preserves old RA/Dec; zero-rate `GetRaDecAtTime` will reset `ReferenceTime` again on next tick anyway — **functionally equivalent** |
| **Re-enable after pause with non-zero offset rates** | `Reset()` → `Set(RA, Dec, 0, 0)` — always reseeds with fresh RA/Dec from current mount | `ReferenceTime = DateTime.Now` — keeps stale `_ra/_dec`, continues accumulating from old epoch with preserved rates — **may drift** |

**For pure sidereal AltAz (the common case), both are equivalent.** For comet or asteroid tracking with non-zero `RightAscensionRate`/`DeclinationRate`, re-enabling tracking in the new code after an extended pause will start predicting from a stale RA/Dec epoch, which could place the target at an incorrect position until the closed-loop correction accumulates enough to recover.

---

## 18. SkyPredictor Deep-Dive

### 18.1 Class Structure — Legacy vs New

| Aspect | Legacy (`GSServer\SkyPredictor.cs`) | New (`GreenSwamp.Alpaca.MountControl\SkyPredictor.cs`) |
|--------|-------------------------------------|--------------------------------------------------------|
| Instantiation | `internal static class` — singleton | `public class` — per-instance, held by `MountInstance` |
| `SiderealRate` | `SkySettings.SiderealRate` (user setting, default `15.0410671787`) | `private const double SiderealRate = 15.0410671786691` |
| `Reset()` monitoring | Commented out | Active `MonitorLog` call |
| `Set()` monitoring | Commented out | Active `MonitorLog` call |
| `GetRaDecAtTime` monitoring | Commented out | Active `MonitorLog` call |

The `SiderealRate` difference is negligible (δ = 9 × 10⁻¹¹ arcsec/s).

### 18.2 `RateRa` / `RateDec` Setter Side-Effect

Both legacy and new have identical behaviour. Setting `RateRa` or `RateDec` calls `SetRaDecNow()` first:

```csharp
public double RateRa
{
    set
    {
        SetRaDecNow();   // advance _ra/_dec to current time using OLD rate
        _rateRa = value; // then change the rate
    }
}
```

`SetRaDecNow()` advances `_ra` and `_dec` to the current epoch before the rate changes. This ensures continuity — the RA/Dec at the moment of rate change is the new anchor point for future predictions.

**Important: `Set(ra, dec, raRate, decRate)` calls `RateRa = raRate` before `Ra = ra`.**  
The setter calls `SetRaDecNow()` but the result is immediately overwritten by the explicit `Ra = ra; Dec = dec` assignments. The `SetRaDecNow()` side-effect inside `Set()` is therefore always a no-op for the coordinate values. This is not a bug, but the apparent redundancy is confusing.

### 18.3 `GetRaDecAtTime` — Two Overloads, Different Behaviour

**Overload 1 — Array return (used by `SetAltAzTrackingRates`):**
```csharp
public double[] GetRaDecAtTime(DateTime time)
{
    if (_rateRa == 0 && _rateDec == 0)
        ReferenceTime = HiResDateTime.UtcNow;  // ← mutates state as side-effect of a read!
    else
    {
        var deltaTime = (time - ReferenceTime).TotalSeconds;
        result[0] = Range.Range24(Ra + (deltaTime * _rateRa) / 15.0);
        result[1] = Dec + deltaTime * _rateDec;
    }
    return result;
}
```

**Overload 2 — `out` parameters (used by GoTo/slew coordinate conversions):**
```csharp
public void GetRaDecAtTime(DateTime time, out double raOut, out double decOut)
{
    if (RateRa == 0 && RateDec == 0)
        ReferenceTime = HiResDateTime.UtcNow;  // ← same mutation
    else
    {
        var deltaTime = (time - ReferenceTime).TotalSeconds;
        var deltaRaRate = (SkyServer.CurrentTrackingRate() - SiderealRate) * 3600; // ← tracking rate correction
        raOut = Range.Range24(_ra + deltaTime * (_rateRa + deltaRaRate) / 15.0);
        decOut = _dec + deltaTime * _rateDec;
    }
}
```

**Key differences between the two overloads:**

| Aspect | Array overload | `out` overload |
|--------|---------------|----------------|
| Tracking rate correction (`deltaRaRate`) | ❌ Not applied | ✅ Applied |
| Used by AltAz timer loop | ✅ Yes | ❌ No |
| `time` parameter honoured (zero-rate path) | ❌ `time` ignored; returns current RA/Dec | ❌ same |

The **AltAz correction loop uses the array overload** — which does **not** apply the `deltaRaRate` correction for non-sidereal tracking rates. The `out` overload with `deltaRaRate` is used only for GoTo target computation, not for the running AltAz correction. This is identical in both legacy and new — not a new divergence — but it means that non-sidereal rate offsets are not propagated into the AltAz correction loop's rate calculation.

### 18.4 Zero-Rate `GetRaDecAtTime` — Read With Write Side-Effect

When `_rateRa == 0 && _rateDec == 0` (pure sidereal), both overloads set `ReferenceTime = HiResDateTime.UtcNow` as a side effect of the read. This "anchors" the predictor to the current moment on each call. Consequence for the AltAz timer loop:

```
Each tick: GetRaDecAtTime(nextTime)
  → zero rates → ReferenceTime = Now; return {_ra, _dec}
  → skyTarget = RaDec2AltAz(_ra, _dec, nextTime)  ← uses FUTURE time for Alt/Az calculation
  → delta = skyTarget - currentAxes
  → _skyTrackingRate = delta / interval
```

The future-time Alt/Az projection still works correctly because `RaDec2AltAz` uses `nextTime` and LST at `nextTime`. The predictor's RA/Dec is not propagated forward, but the sky-position calculation IS projected forward. For pure sidereal this is the correct approach.

### 18.5 `SkyServer.CurrentTrackingRate()` — Static Call Inside Per-Instance Predictor

```csharp
// SkyPredictor.cs line 227 (out overload)
var deltaRaRate = (SkyServer.CurrentTrackingRate() - SiderealRate) * 3600;
```

`SkyServer.CurrentTrackingRate()` is a static method that reads from `_defaultInstance`. Since `SkyPredictor` is now a per-instance object, non-default-instance predictors will use device-00's tracking rate when computing `deltaRaRate`. For the current single-device-00 deployment, this is harmless. For future multi-device scenarios, this is a correctness bug — effectively the same dual-backing-value risk documented in Section 10.

---

## 19. `SetAltAzTrackingRates()` — Predictor Case Comparison

### 19.1 Legacy Implementation (static)

```csharp
// GSServer SkyServer.cs line 3449
MountPositionUpdated = false;
UpdateSteps();
while (!MountPositionUpdated) Thread.Sleep(10);  // ← infinite spin-wait, no timeout
var steps = Steps;
DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(SkySettings.AltAzTrackingUpdateInterval);
var raDec = SkyPredictor.GetRaDecAtTime(nextTime);   // ← array overload
var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1]);
var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, GetLocalSiderealTime(nextTime), SkySettings.Latitude);
Array.Reverse(skyTarget);
skyTarget = GetSyncedAxes(skyTarget);
var rawPositions = new[] { ConvertStepsToDegrees(steps[0], 0), ConvertStepsToDegrees(steps[1], 1) };
delta[0] = Range.Range180(skyTarget[0] - rawPositions[0]);
delta[1] = Range.Range180(skyTarget[1] - rawPositions[1]);
_skyTrackingRate.X = delta[0] / (interval * 0.001);
_skyTrackingRate.Y = delta[1] / (interval * 0.001);
```

### 19.2 New Implementation (per-instance)

```csharp
// SkyServer.TelescopeAPI.cs line 2725
var evt = inst._mountPositionUpdatedEvent;
evt.Reset();
inst.UpdateSteps();
if (!evt.Wait(5000)) break;      // ← 5-second timeout; SILENT SKIP if timeout
var steps = inst._steps;
DateTime nextTime = HiResDateTime.UtcNow.AddMilliseconds(_settings!.AltAzTrackingUpdateInterval);
var raDec = inst.SkyPredictor.GetRaDecAtTime(nextTime);   // ← per-instance predictor
var internalRaDec = Transforms.CoordTypeToInternal(raDec[0], raDec[1]);
var skyTarget = Coordinate.RaDec2AltAz(internalRaDec.X, internalRaDec.Y, GetLocalSiderealTime(nextTime), _settings!.Latitude);
Array.Reverse(skyTarget);
skyTarget = GetSyncedAxes(skyTarget);
var rawPositions = new[] { inst.ConvertStepsToDegrees(steps[0], 0), inst.ConvertStepsToDegrees(steps[1], 1) };
delta[0] = Range.Range180(skyTarget[0] - rawPositions[0]);
delta[1] = Range.Range180(skyTarget[1] - rawPositions[1]);
inst._skyTrackingRate = new Vector(delta[0] / (interval * 0.001), delta[1] / (interval * 0.001));
```

### 19.3 Behavioural Differences

| Aspect | Legacy | New | Impact |
|--------|--------|-----|--------|
| Mount position sync | `while (!MountPositionUpdated) Thread.Sleep(10)` — infinite spin | `evt.Wait(5000)` — 5-second timeout | Legacy can deadlock on hardware failure; new silently skips rate update |
| Predictor access | `SkyPredictor.*` (static singleton) | `inst.SkyPredictor.*` (per-instance) | New is architecturally correct for multi-device |
| Rate write | `_skyTrackingRate.X/Y = ...` (mutable fields) | `inst._skyTrackingRate = new Vector(...)` (immutable assignment) | Semantically equivalent |
| `RaDecSet` guard | ✅ Both check `if (SkyPredictor.RaDecSet)` before computing | ✅ | No divergence |

---

## 20. AltAz Tracking Timer Architecture

### 20.1 Legacy — Single Global Static Timer

```csharp
// GSServer: one shared static timer, one event handler
private static MediaTimer _altAzTrackingTimer;

private static void StartAltAzTrackingTimer()
{
    _altAzTrackingTimer = new MediaTimer { Period = SkySettings.AltAzTrackingUpdateInterval };
    _altAzTrackingTimer.Tick += AltAzTrackingTimerEvent;
    _altAzTrackingTimer.Start();
}

internal static void AltAzTrackingTimerEvent(object sender, EventArgs e)
{
    if (_altAzTrackingTimer?.IsRunning == true)
        // lock via Interlocked
        SetTracking();   // ← no instance arg — always uses _defaultInstance
}
```

### 20.2 New — Per-Instance Timer (correct for multi-device)

```csharp
// MountInstance.cs — per-instance timer owned by MountInstance
internal MediaTimer? _altAzTrackingTimer;

internal void StartAltAzTrackingTimer()
{
    StopAltAzTrackingTimer();
    _altAzTrackingTimer = new MediaTimer { Period = _settings.AltAzTrackingUpdateInterval };
    _altAzTrackingTimer.Tick += AltAzTrackingTimerTick;
    _altAzTrackingTimer.Start();
}

internal void AltAzTrackingTimerTick(object sender, EventArgs e)
{
    if (_altAzTrackingTimer?.IsRunning == true &&
        Interlocked.CompareExchange(ref _altAzTrackingLock, -1, 0) == 0)
    {
        SkyServer.SetTracking(this);   // ← passes THIS instance explicitly
        _altAzTrackingLock = 0;
    }
}
```

### 20.3 Dead Code: Static Timer in `SkyServer.TelescopeAPI.cs`

`SkyServer.TelescopeAPI.cs` still contains a **static** `_altAzTrackingTimer` field, static `AltAzTrackingTimerEvent` handler, and static `StartAltAzTrackingTimer()` / `StopAltAzTrackingTimer()` methods. These were the legacy infrastructure.

In the current `SetTracking()` code, **both** the Simulator+AltAz and SkyWatcher+AltAz branches call `inst!.StartAltAzTrackingTimer()` — the per-instance version on `MountInstance`. The static `StartAltAzTrackingTimer()` is never invoked. The static `AltAzTrackingTimerEvent` is never wired to any timer. These static members are dead code.

---

## 21. SkyWatcher + AltAz — Hardware Dispatch Trace

### 21.1 Tracking ON (`SkyServer.SetTracking(inst)`)

```
rateChange = CurrentTrackingRate()   // sidereal rate, e.g. 0.00417807°/s
SkyWatcher case:
  AltAz case (rateChange != 0):
    SetAltAzTrackingRates(Predictor, inst)
      → inst._skyTrackingRate = Vector(azRate, altRate)  // deg/s for each axis
    inst.StartAltAzTrackingTimer()   // fires every AltAzTrackingUpdateInterval ms
    break
// Falls through to shared SkyWatcher dispatch:
rate = SkyGetRate(inst)              // returns _skyTrackingRate (+ HC/MoveAxis offsets)
SkyAxisSlew(sq, Axis1, rate.X)       // Azimuth axis rate
SkyAxisSlew(sq, Axis2, rate.Y)       // Altitude axis rate
```

`SkyAxisSlew` sends a rate command to the SkyWatcher mount firmware. The mount then drives each axis at the commanded rate until superseded by the next timer tick.

### 21.2 Tracking OFF

```
rateChange = 0
SkyWatcher case:
  AltAz case (rateChange == 0):
    inst.StopAltAzTrackingTimer()
    inst._skyTrackingRate = Vector(0, 0)
    break
// Shared SkyWatcher dispatch:
rate = SkyGetRate(inst)              // returns Vector(0, 0)
SkyAxisSlew(sq, Axis1, 0)
SkyAxisSlew(sq, Axis2, 0)
```

Both ON and OFF paths are functionally equivalent to the legacy.

### 21.3 Comparison with Simulator + AltAz (same alignment mode, different mount)

| Aspect | Simulator + AltAz | SkyWatcher + AltAz |
|--------|------------------|--------------------|
| Hardware command | `CmdAxisTracking(Axis1, rate.X)` + `CmdAxisTracking(Axis2, rate.Y)` | `SkyAxisSlew(Axis1, rate.X)` + `SkyAxisSlew(Axis2, rate.Y)` |
| Rate source | `SkyGetRate(inst)._skyTrackingRate` | Same |
| Predictor type | `AltAzTrackingType.Predictor` | Same |
| Timer | Per-instance `_altAzTrackingTimer` | Same |
| Both axes driven | ✅ | ✅ |
| Rate dispatch location | Inside Simulator case | Shared block after alignment switch |

---

## 22. AltAz/SkyWatcher — Additional Findings

### FINDING 8 (Medium): Predictor re-enable with non-zero offset rates starts from stale RA/Dec epoch

**ASCOM V4 spec context:** The spec permits setting non-zero `RightAscensionRate`/`DeclinationRate` on an AltAz mount provided `TrackingRate = driveSidereal` — the setter throws `InvalidOperationException` otherwise (verified in `Telescope.cs` line 553 / 864). Setting these rates while in AltAz tracking mode is therefore a valid ASCOM scenario (comet/asteroid tracking via AltAz correction loop). The rates feed `SkyPredictor.Set()` via `InstanceActionRateRaDec()`, modifying the predicted RA/Dec trajectory used by the AltAz correction loop.

**The stale-epoch issue:** The legacy always calls `SkyPredictor.Reset()` before seeding, which forces `RaDecSet = false` and guarantees `SkyPredictor.Set(RA, Dec, 0, 0)` is always called with the current sky position. The new code preserves the existing predictor state on re-enable (`ReferenceTime = DateTime.Now`), which is an intentional improvement for pure sidereal tracking (Finding 3 from the original audit is a positive finding that applies here).

However, for non-zero offset rates (`RightAscensionRate ≠ 0` or `DeclinationRate ≠ 0` — comet/asteroid tracking), re-enabling tracking after an extended pause uses the stale `_ra/_dec` epoch from the last `Set()` call. The delta accumulates from that old epoch at the preserved rate, potentially placing the predicted position far from the actual current sky position of the target. The legacy avoids this by always reseeding from `RightAscensionXForm`/`DeclinationXForm`.

**Impact:** Only affects AltAz mode with non-zero offset tracking rates. Pure sidereal AltAz is unaffected because the zero-rate path in `GetRaDecAtTime` anchors to current time on every call.

**Recommendation:** In `InstanceApplyTracking`, when `SkyPredictor.RaDecSet` is true and rates are non-zero, consider refreshing RA/Dec alongside `ReferenceTime`:
```csharp
SkyPredictor.Set(RightAscensionXForm, DeclinationXForm, SkyPredictor.RateRa, SkyPredictor.RateDec);
```

---

### FINDING 9 (Low): `SetAltAzTrackingRates` 5-second timeout silently skips rate update

If `evt.Wait(5000)` times out (mount position event not signalled within 5 seconds), the `break` exits the Predictor case without updating `_skyTrackingRate`. The timer continues running and the next tick will use the stale rate. The legacy used an infinite spin-wait (`while (!MountPositionUpdated) Thread.Sleep(10)`) which would block indefinitely but never silently skip.

The new timeout prevents deadlocks, which is the more robust failure mode. However, no warning is logged when the timeout occurs — a monitor entry would help diagnose hardware communication issues.

**Recommendation:** Add a `MonitorLog` warning entry at the `if (!evt.Wait(5000)) break;` site.

---

### FINDING 10 (Low): Static AltAz timer infrastructure in `SkyServer.TelescopeAPI.cs` is dead code

`SkyServer.TelescopeAPI.cs` contains `AltAzTrackingTimerEvent`, static `StartAltAzTrackingTimer()`, and static `StopAltAzTrackingTimer()`. These were the legacy single-instance timer mechanisms. In the current code, `SetTracking()` always calls `inst!.StartAltAzTrackingTimer()` / `inst!.StopAltAzTrackingTimer()` (the per-instance versions). The static methods and event handler are never called and the static `_altAzTrackingTimer` field is never armed.

**Impact:** Dead code — no functional consequence. Risk: future maintainers may mistake the static methods for the active code path.

**Recommendation:** Remove or `[Obsolete]`-mark the static AltAz timer members in `SkyServer.TelescopeAPI.cs`.

---

### FINDING 11 (Positive): Per-instance AltAz timer is correctly scoped for multi-device

The new `AltAzTrackingTimerTick` on `MountInstance` passes `this` explicitly to `SkyServer.SetTracking(this)`. Each device owns its own timer, its own lock (`_altAzTrackingLock`), and its own predictor. This is the architecturally correct design for multi-device support. The legacy single-instance static timer would have been fundamentally broken for a second AltAz device.

---

### FINDING 12 (Medium): `SkyPredictor.GetRaDecAtTime` (out overload) calls `SkyServer.CurrentTrackingRate()` — uses `_defaultInstance`

```csharp
// SkyPredictor.cs line 227
var deltaRaRate = (SkyServer.CurrentTrackingRate() - SiderealRate) * 3600;
```

`SkyServer.CurrentTrackingRate()` is a static method reading from `_defaultInstance`. Since `SkyPredictor` is now per-instance, if the predictor of a non-default device calls this overload, it receives device-00's tracking rate. For the current single-device deployment this is harmless; for future multi-device deployments this is a correctness issue.

**Note:** The `out` overload is used for GoTo target computation and slew coordinate conversions — not for the AltAz timer loop (which uses the array overload). The tracking-rate correction in the `out` overload is therefore not applied during continuous AltAz tracking, only during GoTo operations.

---

### FINDING 13 (Low / Informational): `GetRaDecAtTime` (array overload) has a read-with-write side-effect

When `_rateRa == 0 && _rateDec == 0` (pure sidereal), both overloads set `ReferenceTime = HiResDateTime.UtcNow`. This mutation during a seemingly-read-only call is semantically surprising. The `time` parameter passed to `GetRaDecAtTime(nextTime)` is completely ignored for zero-rate tracking — the method returns `{Ra, Dec}` anchored to now, not to `nextTime`.

The Alt/Az correction loop still works correctly because `RaDec2AltAz` is called with `GetLocalSiderealTime(nextTime)` as a separate argument — the future-time sky projection is applied at the coordinate-conversion layer, not via the predictor's own projection. This is consistent between legacy and new.

---

## 23. Updated Summary of All Findings

| # | Severity | Finding | File | Line area |
|---|----------|---------|------|-----------|
| 1 | **Medium** | `OnStaticPropertyChanged()` not called when tracking toggled via ASCOM `Telescope.Tracking` setter — UI bindings not notified | `MountInstance.cs` | `InstanceApplyTracking` |
| 2 | **Medium** | Two parallel setter paths (`SkyServer.Tracking = v` vs `inst.ApplyTracking(v)`) have different side effects; no single authoritative toggle point | `SkyServer.TelescopeAPI.cs` / `MountInstance.cs` | static setter vs instance method |
| 3 | **Positive** | `SkyPredictor.Reset()` correctly deferred to OFF path only — preserves predictor state on re-enable | `MountInstance.cs` | `InstanceApplyTracking` |
| 4 | **High** | `Unpark()` ignores `TrackAfterUnpark` / `AutoTrack` user setting; always enables tracking for GermanPolar and AltAz | `Telescope.cs` | `Unpark()` |
| 5 | **Medium** | `_objectId` always 0 — connection reference counting broken for multi-client ASCOM scenarios | `Telescope.cs` | Constructor |
| 6 | **Low** | `AsComOn` guard removed — command rejection changed from silent no-op to thrown exception | `Telescope.cs` | Multiple methods |
| 7 | **Positive** | Offset rates zeroed on tracking OFF — legacy applied non-zero rates when tracking off (ITelescope V4 non-compliant); new code correctly zeros them (spec-compliant). See Section 25. | `SkyServer.TelescopeAPI.cs` | `SetTracking()` |
| 8 | **Medium** | Predictor re-enable preserves stale RA/Dec epoch for non-zero offset rates — legacy always reseeds with current sky position; valid per ASCOM spec for AltAz + driveSidereal but causes drift after extended pause | `MountInstance.cs` | `InstanceApplyTracking` (AltAz ON branch) |
| 9 | **Low** | `SetAltAzTrackingRates` 5-second timeout silently skips rate update with no log warning — stale tracking rate used for one timer interval | `SkyServer.TelescopeAPI.cs` | `SetAltAzTrackingRates` Predictor case |
| 10 | **Low** | Static AltAz timer (`AltAzTrackingTimerEvent`, static `StartAltAzTrackingTimer`, static `StopAltAzTrackingTimer`) in `SkyServer.TelescopeAPI.cs` is dead code | `SkyServer.TelescopeAPI.cs` | AltAz timer region |
| 11 | **Positive** | Per-instance AltAz timer on `MountInstance` correctly scoped per device — a genuine improvement over the legacy for multi-device AltAz | `MountInstance.cs` | `AltAzTrackingTimerTick`, `StartAltAzTrackingTimer` |
| 12 | **Medium** | `SkyPredictor.GetRaDecAtTime` (`out` overload) calls static `SkyServer.CurrentTrackingRate()` — reads `_defaultInstance`; incorrect for non-default device predictors | `SkyPredictor.cs` | `GetRaDecAtTime` (out overload) line 227 |
| 13 | **Low** | `GetRaDecAtTime` (both overloads) mutates `ReferenceTime` on zero-rate read — `time` parameter ignored for pure sidereal; `nextTime` projection handled at coordinate layer, not predictor layer | `SkyPredictor.cs` | `GetRaDecAtTime` zero-rate branch |
| 14 | **Low** | `RightAscensionRate` and `DeclinationRate` getters return raw stored value — spec requires returning 0 when `TrackingRate ≠ driveSidereal`; setters correctly throw but getters do not zero | `Telescope.cs` | `RightAscensionRate` getter (line 846), `DeclinationRate` getter (line 536) |

---

## 24. Hardware Outcome — SkyWatcher AltAz

For the test case of **Tracking = true**, AltAz, SkyWatcher, first enable (no prior predictor state):

```
Predictor seeded: Set(RA_current, Dec_current, 0, 0)
SetAltAzTrackingRates():
  nextTime = Now + AltAzInterval (e.g. 500 ms)
  raDec = GetRaDecAtTime(nextTime)  → rates=0, returns {RA_current, Dec_current}, ReferenceTime=Now
  internalRaDec = CoordTypeToInternal(RA, Dec)
  skyTarget = RaDec2AltAz(internalRA, internalDec, LST(nextTime), Latitude)
  delta = skyTarget - currentAxes  (typically small — mount near target)
  _skyTrackingRate = delta / interval  (deg/s for Az and Alt axes)

Hardware: SkyAxisSlew(Axis1, azRate) + SkyAxisSlew(Axis2, altRate)

Each subsequent timer tick:
  AltAzTrackingTimerTick → SetTracking(this)
    → SetAltAzTrackingRates() → recomputes delta based on updated axis positions
    → SkyAxisSlew(both axes, new rates)
```

The AltAz correction loop continuously steers the mount to track the sky rotation. Both axes are driven simultaneously. For pure sidereal (zero offset rates), the predicted target RA/Dec is fixed and only the Alt/Az changes as the sky rotates — the delta correction loop provides the closed-loop error correction. This is functionally equivalent to the legacy.

---

---

## 25. ASCOM ITelescope V4 — Rate Offset Spec Compliance

**Reference:** [ITelescope V4 `RightAscensionRate`](https://ascom-standards.org/newdocs/telescope.html#Telescope.RightAscensionRate) · [FAQ: What are `RightAscensionRate` and `DeclinationRate`?](https://ascom-standards.org/newdocs/trkoffset-faq.html)

### 25.1 What ITelescope V4 Requires

ITelescope V4 formalised the following rules for `RightAscensionRate` and `DeclinationRate` (the change note reads: *"Formalized to clarify that these rates apply only when the mount is tracking at sidereal rate"*):

| Rule | Requirement | Source |
|------|------------|--------|
| **Scope of application** | Rate offsets apply **only** when `TrackingRate = driveSidereal` | Spec note |
| **Setter guard** | Must throw `InvalidOperationException` if `TrackingRate ≠ driveSidereal` | Raises section |
| **Getter zero return** | Must return **0** if `TrackingRate ≠ driveSidereal` | Spec note |
| **Hardware application** | Rates must not be applied to hardware when tracking is off | Implied by "only during sidereal tracking" |

The distinction between `TrackingRate` and `Tracking` is important:
- `TrackingRate` selects the drive rate (`driveSidereal`, `driveLunar`, `driveSolar`, `driveKing`)
- `Tracking` turns the selected tracking rate on or off
- The spec guard applies to `TrackingRate ≠ driveSidereal`, but the "apply only during sidereal tracking" note implies both `TrackingRate = driveSidereal` **and** `Tracking = true` are required for offset rates to take effect at the hardware level

### 25.2 Current Implementation — Setter Compliance ✅

Both setters guard correctly:

```csharp
// Telescope.cs line 553 — DeclinationRate setter
if (TrackingRate != DriveRate.Sidereal)
    throw new ASCOM.InvalidOperationException("DeclinationRate - cannot set rate because TrackingRate is not Sidereal");

// Telescope.cs line 864 — RightAscensionRate setter
if (TrackingRate != DriveRate.Sidereal)
    throw new InvalidOperationException("RightAscensionRate - cannot set rate because TrackingRate is not Sidereal");
```

Both setters throw the correct exception type (`InvalidOperationException`) with a descriptive message. **Setter compliance: ✅ spec-correct.**

### 25.3 Current Implementation — Getter Compliance ❌ (Finding 14)

Both getters return the raw stored value without a `TrackingRate` check:

```csharp
// Telescope.cs line 536 — DeclinationRate getter
var r = inst.RateDecOrg;   // ← no TrackingRate check; returns stored value

// Telescope.cs line 846 — RightAscensionRate getter
var r = inst.RateRaOrg;   // ← no TrackingRate check; returns stored value
```

The spec requires both getters to return `0` when `TrackingRate ≠ driveSidereal`. While the setter guard makes it hard to store a non-zero value with a non-sidereal `TrackingRate` (since the setter throws), it is possible:

```
1. TrackingRate = driveSidereal
2. RightAscensionRate = 0.5   → stored in RateRaOrg
3. TrackingRate = driveLunar  → no setter fired; RateRaOrg still 0.5
4. RightAscensionRate getter  → returns 0.5  ← spec requires 0
```

**Getter compliance: ❌ spec gap. Both getters should add:**
```csharp
if (TrackingRate != DriveRate.Sidereal) return 0;
```

### 25.4 Current Implementation — Hardware Application Compliance ✅

The new `SetTracking()` zeros `CmdRaDecRate` (Simulator) or `_skyTrackingRate` (SkyWatcher) when `_trackingMode == TrackingMode.Off`:

```csharp
// SkyServer.TelescopeAPI.cs — Simulator + GermanPolar/Polar
// When tracking mode is Off, sends 0 for both axes:
_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, 0);
_ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, 0);
```

The legacy sent the non-zero offset rates to hardware even when tracking was off — that is non-compliant with the spec's intent that rates only apply during active sidereal tracking. **Hardware application compliance: ✅ new code; ❌ legacy.**

### 25.5 Rate Offsets in AltAz Mode

Setting non-zero `RightAscensionRate`/`DeclinationRate` on an AltAz mount with `TrackingRate = driveSidereal` is **valid per the ASCOM spec**. The setter guard does not distinguish by `AlignmentMode` — only by `TrackingRate`. The GreenSwamp implementation routes these rates into `SkyPredictor` via `InstanceActionRateRaDec()`, feeding `SkyPredictor.Set(ra, dec, _rateRaDec.X, _rateRaDec.Y)`. The AltAz correction loop then uses the predicted moving RA/Dec to compute the required Az/Alt axis rates — effectively tracking a moving solar system object (comet, asteroid) in AltAz mode. This is a valid and spec-compliant use case.

The stale-epoch risk on re-enable (Finding 8) applies specifically to this non-zero rate + AltAz + re-enable scenario.

### 25.6 Legacy Rate Compliance

The legacy GSServer had the same setter guard and getter gap. It also sent non-zero rates to the hardware simulator when tracking was off (Finding 7 / Section 5). The legacy was therefore equally non-compliant with ITelescope V4 on the getter and hardware-application dimensions.

### 25.7 Recommended Fixes

| Priority | Fix | Location |
|----------|-----|---------|
| 1 | Add `if (TrackingRate != DriveRate.Sidereal) return 0;` to `RightAscensionRate` getter | `Telescope.cs` line 844 |
| 2 | Add `if (TrackingRate != DriveRate.Sidereal) return 0;` to `DeclinationRate` getter | `Telescope.cs` line 534 |

These are two-line fixes with no risk of hardware impact (the rate is already stored; only the getter return value changes).

---

*End of audit extension (ASCOM V4 Rate Offset Spec Compliance) — 2026-04-06 18:06*

---

## 26. `InstanceSetTrackingMode` — Deep-Dive Analysis

### 26.1 Implementation

```csharp
// MountInstance.cs — InstanceSetTrackingMode()
internal void InstanceSetTrackingMode()
{
    switch (_settings.AlignmentMode)
    {
        case AlignmentMode.AltAz:
            _trackingMode = TrackingMode.AltAz;
            break;
        case AlignmentMode.Polar:
        case AlignmentMode.GermanPolar:
            _trackingMode = _settings.Latitude < 0 ? TrackingMode.EqS : TrackingMode.EqN;
            break;
    }
}
```

### 26.2 Comparison with Static `SetTrackingMode()`

| Aspect | `InstanceSetTrackingMode` | Static `SetTrackingMode` |
|--------|--------------------------|--------------------------|
| Hemisphere logic | `_settings.Latitude < 0` | `SouthernHemisphere` = `_settings!.Latitude < 0` — identical |
| Write target | `_trackingMode` direct field write | `_defaultInstance.TrackingMode = ...` property setter |
| Property setter side-effects | n/a (bypassed) | `get => _trackingMode; set => _trackingMode = value;` — trivial, none |
| Null guard | None needed (instance method) | `if (_defaultInstance == null) return;` |
| Default case | No `default:` — unrecognised mode silently no-ops | Same |

**`TrackingMode` property setter confirmed trivial** — `get => _trackingMode; set => _trackingMode = value;`. Direct field write in `InstanceSetTrackingMode` is exactly equivalent to the property setter call in the static version.

### 26.3 Verdict

`InstanceSetTrackingMode` is a clean, correct per-instance equivalent of `SetTrackingMode`. Called exactly once from `InstanceApplyTracking` on the tracking-ON path. No divergence from legacy semantics. No latent risk. **No action needed.**

---

## 27. Finding 14 Correction — Rate Getters Already Fixed

Section 25.3 identified Finding 14: that the `RightAscensionRate` and `DeclinationRate` getters returned the raw stored value without a `TrackingRate` check.

**Inspection of current `Telescope.cs` shows this is already resolved:**

```csharp
// DeclinationRate getter
var r = (TrackingRate == DriveRate.Sidereal) ? inst.RateDecOrg : 0.0;

// RightAscensionRate getter
var r = (TrackingRate == DriveRate.Sidereal) ? inst.RateRaOrg : 0.0;
```

Both getters return `0.0` when `TrackingRate != DriveRate.Sidereal`, as the ITelescope V4 spec requires.

**Finding 14 status: Already fixed. The Section 25.7 recommended fixes are already in place. No action needed.**

---

## 28. FINDING 15 (Medium): `SyncToTarget` Passes Current Scope Position to Sync-Limit Guard

### 28.1 The Bug

```csharp
// Telescope.cs — SyncToTarget()
public void SyncToTarget()
{
    var inst = GetInstance();
    // ...ranges checked on inst.TargetRa / inst.TargetDec above...

    var a = Transforms.CoordTypeToInternal(RightAscension, Declination);  // <-- BUG
    CheckRaDecSync(a.X, a.Y, "SyncToTarget");
    // ...
}
```

`RightAscension` and `Declination` resolve to `this.RightAscension` and `this.Declination` — the ASCOM property getters returning the **current telescope position** — not the sync target. `CheckRaDecSync` is therefore always checking the current position against a transform of itself, producing a near-zero delta that always passes regardless of how far the actual target is from the current position. The sync-limit guard is completely ineffective.

### 28.2 Contrast with `SyncToCoordinates` (Correct)

```csharp
// Telescope.cs — SyncToCoordinates(double RightAscension, double Declination)
var a = Transforms.CoordTypeToInternal(RightAscension, Declination);  // <-- method params: correct
CheckRaDecSync(a.X, a.Y, "SyncToCoordinates");
```

In `SyncToCoordinates`, `RightAscension`/`Declination` are method parameters (the sync target). In `SyncToTarget` there are no parameters, so the same names silently resolve to the ASCOM property getters.

### 28.3 Fix

```csharp
// SyncToTarget() — replace the buggy line:
var a = Transforms.CoordTypeToInternal(inst.TargetRa, inst.TargetDec);
CheckRaDecSync(a.X, a.Y, "SyncToTarget");
```

### 28.4 Impact

The sync-limit guard in `SyncToTarget` has been silently ineffective since this method was written. Clients calling `SyncToTarget` with a target far outside the configured sync-limit radius will succeed without error, potentially corrupting alignment silently. `SyncToCoordinates` and `SyncToAltAz` are unaffected.

---

## 29. Final Consolidated Findings

| # | Severity | Status | Finding | File | Area |
|---|----------|--------|---------|------|------|
| 1 | **Medium** | Open | `OnStaticPropertyChanged()` not called via `ApplyTracking` — UI bindings not notified on ASCOM tracking toggle | `MountInstance.cs` | `InstanceApplyTracking` |
| 2 | **Medium** | Open | Two parallel setter paths with different side effects; no single authoritative toggle point | `SkyServer.TelescopeAPI.cs` / `MountInstance.cs` | static setter vs instance method |
| 3 | **Positive** | Good | `SkyPredictor.Reset()` correctly deferred to OFF path only | `MountInstance.cs` | `InstanceApplyTracking` |
| 4 | **High** | Open | `Unpark()` ignores `AutoTrack` setting; always enables tracking for GermanPolar/Polar | `Telescope.cs` | `Unpark()` |
| 5 | **Medium** | Open | `_objectId` always 0 — multi-client connection reference counting broken | `Telescope.cs` | Constructors |
| 6 | **Low** | Documented | `AsComOn` guard removed — command rejection now throws instead of silent no-op | `Telescope.cs` | Multiple methods |
| 7 | **Positive** | Good | Offset rates zeroed on tracking OFF — new code is spec-compliant; legacy was not | `SkyServer.TelescopeAPI.cs` | `SetTracking()` |
| 8 | **Medium** | Open | Predictor re-enable with non-zero offset rates starts from stale RA/Dec epoch (AltAz only) | `MountInstance.cs` | `InstanceApplyTracking` AltAz ON branch |
| 9 | **Low** | Open | 5-second timeout in `SetAltAzTrackingRates` silently skips rate update with no log warning | `SkyServer.TelescopeAPI.cs` | `SetAltAzTrackingRates` |
| 10 | **Low** | Open | Static AltAz timer infrastructure is dead code | `SkyServer.TelescopeAPI.cs` | AltAz timer region |
| 11 | **Positive** | Good | Per-instance AltAz timer correctly scoped per device | `MountInstance.cs` | `AltAzTrackingTimerTick` |
| 12 | **Medium** | Open | `SkyPredictor.GetRaDecAtTime` (`out` overload) calls static `CurrentTrackingRate()` — reads `_defaultInstance` | `SkyPredictor.cs` | `GetRaDecAtTime` out overload |
| 13 | **Low** | Documented | `GetRaDecAtTime` mutates `ReferenceTime` as read side-effect on zero-rate path | `SkyPredictor.cs` | Both overloads |
| 14 | **Low** | **Already Fixed** | Rate getters returned raw value when `TrackingRate != Sidereal` — both getters now use ternary guard | `Telescope.cs` | `RightAscensionRate` / `DeclinationRate` getters |
| 15 | **Medium** | Open | `SyncToTarget` passes current scope position to `CheckRaDecSync` instead of target — sync-limit guard always passes | `Telescope.cs` | `SyncToTarget()` |

### Priority Action List

| Priority | Finding | Change | Effort |
|----------|---------|--------|--------|
| 1 | **F4 High** — Restore `AutoTrack` in `Unpark()` | `inst.ApplyTracking(inst.Settings.AutoTrack && AlignmentMode != AlignmentMode.AltAz)` | 1 line |
| 2 | **F15 Medium** — Fix `SyncToTarget` coordinate bug | Replace `RightAscension, Declination` with `inst.TargetRa, inst.TargetDec` | 1 line |
| 3 | **F1 Medium** — Add `OnStaticPropertyChanged()` after `SkyServer.SetTracking(this)` in `InstanceApplyTracking` | 1 line | Low risk |
| 4 | **F5 Medium** — Assign `_objectId` from thread-safe counter in constructors | 3 lines | Low risk |
| 5 | **F8 Medium** — Reseed predictor on AltAz re-enable when rates non-zero | 3 lines | Medium risk |
| 6 | **F9 Low** — Add `MonitorLog` warning at 5-second timeout in `SetAltAzTrackingRates` | 1 line | No risk |
| 7 | **F10 Low** — Remove dead static AltAz timer infrastructure | Cleanup | No risk |
| 8 | **F12 Medium** — Pass tracking rate as parameter to predictor | Refactor | Medium risk |

---

*End of audit — complete — 2026-04-06 19:08*
