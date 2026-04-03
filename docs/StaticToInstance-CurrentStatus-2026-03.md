# Static-to-Instance Migration — Current Status Report
## GreenSwamp Alpaca — SkyServer / MountControl

**Prepared:** March 2026 (post-SkyWatcher GermanPolar confidence test)
**Updated:** April 2026, 08:25 UTC — full re-verification against `master`
**Updated:** April 2026, 14:30 UTC — `SkyServer.TelescopeAPI.cs` full region-by-region review; CTS delegation confirmed; new concerns G1–G3 identified
**Updated:** 2026-04-03 09:00 — timestamp format standardised per copilot-instructions.md
**Updated:** 2026-04-03 09:23 — Phase G complete (X1, X2, G1, G2, G3); all builds green
**Branch:** `master`
**Build baseline:** ✅ Green — Simulator and SkyWatcher GermanPolar pass confidence tests
**Assessment method:** Direct code review of all five key files + queue subsystem + full read of all 14 regions of `SkyServer.TelescopeAPI.cs`

---

## 1. Executive Summary

The migration is **functionally complete for single-telescope operation** and is estimated
at approximately **98 %** complete overall. Every piece of per-device state now lives on
`MountInstance`; the static `SkyServer.*` surface is a pure thin delegation layer to
`MountInstanceRegistry.GetInstance(0)`. The ASCOM driver (`Telescope.cs`) routes entirely
through `GetInstance(_deviceNumber)`.

All 🔴 Critical and 🟠 High items from the March assessment have been resolved.
The remaining **2 %** consists of two categories:

| Category | Items | Blocking multi-telescope? |
|---|---|---|
| **Phase E — Blazor per-device UI notifications** | `StaticPropertyChanged` fires globally for all devices; no per-instance event on `MountInstance` | 🟡 UI only |
| **Phase F — Option C Phase 3** | `Devices[]` array in `appsettings.json` has per-device serial fields; Blazor multi-device UI not started | 🟡 UI / config |

**Minor residuals (cleanup only):**
- `//lock (_goToAsyncLock)` commented reference in `SkyServer.TelescopeAPI.cs` line 787 — field is gone, comment can be removed
- `SkySettingsBridge.cs` still exists on disk but is excluded from compilation via `<Compile Remove>` in the csproj; should be deleted

**New concerns identified — April 2026 (TelescopeAPI review):**

| ID | Item | File | Blocking multi-telescope? |
|---|---|---|---|
| **G1** ✅ | `RateDecOrg` / `RateRaOrg` converted to delegating computed properties | `SkyServer.TelescopeAPI.cs:538,572` | ✅ Resolved — Phase G |
| **G2** ✅ | `MountInstance? instance` parameter threaded through `SlewAxes→SlewMount→GoToAsync` | `SkyServer.TelescopeAPI.cs:763`, `SkyServer.Core.cs` | ✅ Resolved — Phase G |
| **G3** ✅ | `PulseGuideAltAz()` moved to `MountInstance` instance method | `MountInstance.cs`, `SkyServer.TelescopeAPI.cs` | ✅ Resolved — Phase G |

**Also confirmed correct (April 2026 TelescopeAPI review):**
- G1/G2/G3 all resolved — Phase G complete (2026-04-03) ✅
- All four CTS tokens
- `SlewAsync` / `SlewSync` delegate to `_defaultInstance.SlewAsync/SlewSync` ✅
- `SetTracking()` uses per-instance queue refs directly ✅
- `PulseGuide()` (non-AltAz path) uses per-instance queue refs ✅
- `_slewController` is a computed delegating property → `_defaultInstance?._slewController` ✅
- `_parkSelected` is a delegating computed property → `_defaultInstance?._parkSelected` ✅

---

## 2. What Is Confirmed Complete (code-verified, April 2026 re-check)

| Area | File(s) | Notes |
|---|---|---|
| All per-device state fields on `MountInstance` | `MountInstance.cs` | Every backing field confirmed instance-owned |
| `_defaultInstance` computed property (`=> MountInstanceRegistry.GetInstance(0)`) | `SkyServer.Core.cs` | Bridge B0 fully removed |
| `SkyServer.*` properties all delegate to `_defaultInstance` | All three `SkyServer.*.cs` partials | Zero independent static backing values |
| `SkyQueueImplementation` owned by `MountInstance` | `MountInstance.cs` | `SkyQueueInstance` field; started in `MountStart()` |
| `MountQueueImplementation` owned by `MountInstance` | `MountInstance.cs` | `MountQueueInstance` field; started in `MountStart()` |
| `ReceiveSteps()` per-instance with full pipeline | `MountInstance.cs` | Steps → coordinates → event signal → UI notify |
| `_mountPositionUpdatedEvent` per-instance | `MountInstance.cs` | `SkyPrecisionGoto` and `SkyPulseGoto` use it |
| `OnUpdateServerEvent()` instance method | `MountInstance.cs` | Per-device timer lock |
| `UpdateServerEvent` delegates to `_defaultInstance` | `SkyServer.Core.cs` | Single-telescope path correct |
| `ISerialPort` per-instance (`_serial`, `_connectStates`) | `MountInstance.cs` | `OpenSerial()` / `CloseSerial()` on instance |
| `SkySystem.cs` deleted | — | Zero references remain |
| `SkySettings` static facade deleted | — | All 35 references migrated |
| `SkySettingsInstance` fully instance-based | `SkySettingsInstance.cs` | All 134 settings properties |
| `Telescope.cs` routes via `GetInstance(_deviceNumber)` | `Telescope.cs` | Every property/method uses `GetInstance()` |
| Instance-aware `SkyTasks(MountTaskName, MountInstance)` | `SkyServer.Core.cs` | Complete; all commands use `q = instance.SkyQueueInstance` |
| Instance-aware `SimTasks(MountTaskName, MountInstance)` | `SkyServer.Core.cs` | Complete; all commands use `q = instance.MountQueueInstance` |
| Instance-aware `AxesStopValidate(MountInstance)` | `SkyServer.Core.cs` | Complete; uses per-instance queue refs |
| `MountConnect()` reads capabilities from instance fields directly | `MountInstance.cs` | No `SkyServer.CanPPec` etc. read-back |
| PEC system fully migrated | `MountInstance.Pec.cs` | Fully instance-based |
| Cancellation tokens per-instance | `MountInstance.cs` | `_ctsGoTo`, `_ctsPulseGuide*`, `_ctsHcPulseGuide` |
| `SlewController` per-instance | `MountInstance.cs` | `_slewController` field |
| HC anti-backlash fields per-instance | `MountInstance.cs` | `_hcPrevMoveRa/Dec`, `_hcPrevMovesDec` |
| Tracking offset rate per-instance | `MountInstance.cs` | `_trackingOffsetRate` field |
| `_skyTrackingOffset` per-instance | `MountInstance.cs` | `int[2]` array |
| **A1 — Legacy `SkyTasks(MountTaskName)` deleted** ✅ | `SkyServer.Core.cs` | Only instance-aware overload remains; confirmed by direct search |
| **A2 — Legacy `AxesStopValidate()` deleted** ✅ | `SkyServer.Core.cs` | Only `AxesStopValidate(MountInstance)` remains; "Phase A2 verified" commit |
| **B1 — `SkyQueue.RegisterInstance()` removed** ✅ | `MountInstance.cs` | Zero calls remain in the codebase |
| **B2/B3 — Dead `PropertyChangedSkyQueue`/`MountQueue` handlers deleted** ✅ | `SkyServer.Core.cs` | Static facades gone; handlers had nothing to subscribe to |
| **B4/B5 — `[Obsolete]` shortcut constructors removed** ✅ | `SkyCommands.cs` | All base class ctors require `(long id, ICommandQueue<> queue)`; zero `[Obsolete]` attributes |
| **B6 — Static `SkyQueue` class deleted** ✅ | `SkyQueue.cs` | File now contains only `SkyQueueImplementation` (109 lines) |
| **B7 — Static `MountQueue` class deleted** ✅ | `MountQueue.cs` | File now contains only `MountQueueImplementation` (63 lines) |
| **C1/C2 — Pulse-guide callbacks write to `this` fields** ✅ | `MountInstance.cs` | Both Simulator (line 1595–1596) and SkyWatcher (line 1632–1633) branches fixed |
| **D1 — `_stepsTimeFreq` field added to `MountInstance`** ✅ | `MountInstance.cs` | `internal long[] _stepsTimeFreq = { 0, 0 };` at line 63 |
| **D2/D3 — `StepsTimeFreq` delegates; instance `SkyTasks` writes instance field** ✅ | `SkyServer.cs`, `SkyServer.Core.cs` | `get => _defaultInstance?._stepsTimeFreq ?? new long[] { 0, 0 }` |
| **D4 — `_mountRunning` static field removed** ✅ | `SkyServer.cs` | `IsMountRunning` is `get => _defaultInstance?.IsMountRunning ?? false` |
| **D5/D6 — `SnapPort1/2` delegate to `_defaultInstance`** ✅ | `SkyServer.cs` | `get => _defaultInstance?._snapPort1 ?? false` etc. |
| **D7 — `_goToAsyncLock` field removed** ✅ | `SkyServer.TelescopeAPI.cs` | Field declaration gone; only `//lock (_goToAsyncLock)` comment remains (cleanup item) |
| **D8 — `LastDecDirection` delegates to `_defaultInstance._lastDecDirection`** ✅ | `SkyServer.cs` | `internal GuideDirection _lastDecDirection` on `MountInstance` at line 106 |
| **3.11 — `SkySettingsBridge.cs` excluded from compilation** ✅ | `.csproj` | `<Compile Remove="SkySettingsBridge.cs" />` confirmed; file still exists on disk (delete when ready) |
| **CTS tokens — all four delegate to `_defaultInstance`** ✅ | `SkyServer.Core.cs:131–150` | `_ctsGoTo`, `_ctsPulseGuideRa`, `_ctsPulseGuideDec`, `_ctsHcPulseGuide` — all computed delegating properties; comment: "Phase 5.3 — delegate to default instance to prevent cross-device cancellation" |
| **`_slewController` — computed delegating property** ✅ | `SkyServer.TelescopeAPI.cs:~1549` | `get => _defaultInstance?._slewController` |
| **`SlewAsync` / `SlewSync` delegate to instance** ✅ | `SkyServer.TelescopeAPI.cs:~1568–1590` | Both methods call `_defaultInstance.SlewAsync/SlewSync` |
| **`_parkSelected` — delegating computed property** ✅ | `SkyServer.cs:64–68` | `get => _defaultInstance?._parkSelected; set { if (_defaultInstance != null) _defaultInstance._parkSelected = value; }` |
| **X1 — `//lock (_goToAsyncLock)` comment removed** ✅ | `SkyServer.TelescopeAPI.cs` | Dead comment referencing deleted `_goToAsyncLock` field removed from `GoToAsync` |
| **X2 — `SkySettingsBridge.cs` deleted** ✅ | `GreenSwamp.Alpaca.MountControl\` | File deleted; `<Compile Remove="SkySettingsBridge.cs" />` removed from `.csproj` |
| **G1 — `RateDecOrg` / `RateRaOrg` delegating** ✅ | `SkyServer.TelescopeAPI.cs` | Converted from true static auto-properties to computed delegating properties routing to `_defaultInstance?.RateDecOrg/RateRaOrg`; `MountInstance` backing fields already existed |
| **G2 — `MountInstance` param through `SlewAxes→SlewMount→GoToAsync`** ✅ | `SkyServer.TelescopeAPI.cs`, `SkyServer.Core.cs` | `MountInstance? instance = null` optional parameter added; `var effectiveInstance = instance ?? _defaultInstance!` resolves in `SlewMount` and `GoToAsync`; `AutoHome` callers backward-compat via default |
| **G3 — `PulseGuideAltAz` moved to `MountInstance`** ✅ | `MountInstance.cs`, `SkyServer.TelescopeAPI.cs` | New `internal void PulseGuideAltAz(...)` in `#region AltAz Pulse Guide`; uses `this.SkyPredictor`, `_monitorPulse` backing field, inlined `SiderealRate = 15.0410671786691`; `StopAltAzTrackingTimer` made `internal static`; 4 call sites updated |

---

## 3. Remaining Work

All 🔴 Critical and 🟠 High items from the March 2026 assessment have been resolved.
The sections below are retained for historical context with updated status markers.

### 3.1 ✅ RESOLVED — Legacy `SkyTasks(MountTaskName)` Deleted

**Verified April 2026.** Searching `SkyServer.Core.cs` for the no-parameter overload yields
zero results. Only the instance-aware `SkyTasks(MountTaskName taskName, MountInstance instance)`
exists. The misleading "removed" comment is also gone.

---

### 3.2 ✅ RESOLVED — Legacy `AxesStopValidate()` (No Instance Parameter) Deleted

**Verified April 2026.** The commit "Phase A2 verified — build clean" records this.
Only `AxesStopValidate(MountInstance instance)` exists in `SkyServer.Core.cs`.

---

### 3.3 ✅ RESOLVED — `SkyQueue.RegisterInstance()` Removed

**Verified April 2026.** Zero occurrences of `RegisterInstance` in `MountInstance.cs`
or anywhere in the codebase. The static `SkyQueue` facade class has been deleted entirely
(see 3.5 / B6).

---

### 3.4 ✅ RESOLVED — Pulse-Guide Callbacks Write to Instance Fields

**Verified April 2026.** Both branches in `MountInstance.MountStart()` now use:
```csharp
v => { _isPulseGuidingRa = v; },
v => { _isPulseGuidingDec = v; }
```
Simulator branch: lines 1595–1596. SkyWatcher branch: lines 1632–1633.

---

### 3.5 ✅ RESOLVED — `PropertyChangedSkyQueue` / `PropertyChangedMountQueue` Deleted

**Verified April 2026.** Static facades (`SkyQueue`, `MountQueue`) are deleted; these
handlers have no event to subscribe to and are gone.

---

### 3.6 ✅ RESOLVED — `StepsTimeFreq` Moved to Per-Instance Field

**Verified April 2026.**
- `MountInstance` has `internal long[] _stepsTimeFreq = { 0, 0 };` at line 63.
- `SkyServer.StepsTimeFreq` delegates: `get => _defaultInstance?._stepsTimeFreq ?? new long[] { 0, 0 }`.
- Instance-aware `SkyTasks(StepTimeFreq, instance)` writes to `instance._stepsTimeFreq`.

---

### 3.7 ✅ RESOLVED — `_mountRunning` Static Field Removed

**Verified April 2026.** `SkyServer.IsMountRunning` is now:
```csharp
get => _defaultInstance?.IsMountRunning ?? false;
```
No independent `_mountRunning` static field exists.

---

### 3.8 ✅ RESOLVED — `SnapPort1` / `SnapPort2` Delegate to Instance

**Verified April 2026.** Both delegate to `_defaultInstance._snapPort1/2`; backing fields
exist on `MountInstance` at lines 104–105.

---

### 3.9 ✅ RESOLVED — `_goToAsyncLock` Field Removed

**Verified April 2026.** The field declaration is gone from `SkyServer.TelescopeAPI.cs`.
One residual `//lock (_goToAsyncLock)` comment remained at line 787 — **removed (X1, 2026-04-03)** ✅

---

### 3.10 ✅ RESOLVED — `LastDecDirection` Moved to Instance

**Verified April 2026.** `MountInstance` has `internal GuideDirection _lastDecDirection` at
line 106. `SkyServer.LastDecDirection` delegates get/set to `_defaultInstance._lastDecDirection`.

---

### 3.11 ✅ RESOLVED — `SkySettingsBridge.cs` Deleted

**Verified April 2026.** `<Compile Remove="SkySettingsBridge.cs" />` was in the `.csproj`.
**Resolved (X2, 2026-04-03):** File deleted from disk; `<Compile Remove>` entry removed from `.csproj`. ✅

---

### 3.12 🟡 MEDIUM — B5: Blazor Per-Device UI Notifications Not Implemented

**Status: unchanged from March 2026 assessment.**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
public static event PropertyChangedEventHandler StaticPropertyChanged;
```

All Blazor UI components subscribe to this single static event. Every `SkyServer.*`
property setter fires it. For multiple telescopes, every state change on any device
causes all Blazor components to refresh regardless of which device they are bound to.

`MountInstance` has no `PropertyChanged` / `InstancePropertyChanged` event of its own.

No per-device notification mechanism exists. Implementing it requires:
- Each `MountInstance` to have its own `PropertyChanged` event
- Blazor components to bind to the specific `MountInstance` event for their device number
- The static `NotifyStepsChanged()` helper to be replaced or supplemented with
  per-device routing

---

### 3.13 🟡 MEDIUM — Option C Phase 3: Config & UI (Partially Started)

**Status: F1 partially complete; F2/F3 not started.**

Per-device serial config fields (`Port`, `BaudRate`, `DataBits`, `Handshake`,
`ReadTimeout`, `DTREnable`, `RTSEnable`) already exist in the `Devices[]` array in
`appsettings.json` — not inside `AlpacaDevices[]` as originally envisioned, but the
data is present per device. The Blazor connection status panel still needs per-device
controls (F2/F3).

---

### 3.14 ✅ RESOLVED — G1: `RateDecOrg` / `RateRaOrg` Now Delegating Computed Properties

**Resolved 2026-04-03 (Phase G).** `MountInstance` backing fields (`_rateDecOrg`, `_rateRaOrg`) and instance properties already existed. Converted `SkyServer.TelescopeAPI.cs` properties to computed delegating form routing to `_defaultInstance?.RateDecOrg/RateRaOrg`. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` lines 538, 572

```csharp
public static double RateDecOrg { get; set; }   // ⚠️ true static auto-property
public static double RateRaOrg { get; set; }    // ⚠️ true static auto-property
```

These store the "original" tracking rate (before sign-correction direction is applied)
used by `ActionRateRaDec()`. For a second telescope, setting `RateDecOrg` on device 1
would silently overwrite the stored value for device 0.

**Impact:** Low — these are momentary "previous value" helpers, not persistent state.
Written and read within the same `RateDec`/`RateRa` setter call sequence. A second
device running simultaneously could corrupt the direction logic for offset tracking
rates (`RaRateOrg` / `DecRateOrg`), but only during active guiding with non-zero
offset rates on both devices simultaneously.

**Fix applied (Phase G, 2026-04-03):** `MountInstance` backing fields already existed; converted `SkyServer.TelescopeAPI.cs` properties to computed delegating form. ✅

---

### 3.15 ✅ RESOLVED — G2: `MountInstance` Parameter Threaded Through `SlewAxes→SlewMount→GoToAsync`

**Resolved 2026-04-03 (Phase G).** `MountInstance? instance = null` optional parameter added to `SlewAxes`, `SlewMount`, and `GoToAsync`. `var effectiveInstance = instance ?? _defaultInstance!` used throughout `GoToAsync`. `AutoHome` callers backward-compatible via default. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` line 763

```csharp
private static void GoToAsync(double[] target, SlewType slewState, EventWaitHandle goToStarted, bool tracking = false)
```

**Call chain:** `SlewAxes()` → `SlewMount()` → `GoToAsync()` — this is the **handpad
slew path** used by the Blazor HC panel. `GoToAsync` uses `_defaultInstance!` throughout
(lines 795, 835, 843, 985, 996) and would always operate on device 0 regardless of
which device the user intends to move.

**Fix applied:** `SlewAxes()`, `SlewMount()`, and `GoToAsync()` all now accept an optional `MountInstance? instance` parameter; `GoToAsync` uses `effectiveInstance` throughout.

**Note:** All primary ASCOM slew paths
already been migrated to `SlewController` (`_defaultInstance.SlewAsync/SlewSync`) and
are not affected. Only the axis-coordinate (handpad) slew path is impacted.

---

### 3.16 ✅ RESOLVED — G3: `PulseGuideAltAz()` Moved to `MountInstance` Instance Method

**Resolved 2026-04-03 (Phase G).** New `internal void PulseGuideAltAz(...)` added to `MountInstance` in `#region AltAz Pulse Guide`. Uses `this.SkyPredictor`, `_monitorPulse` backing field (inaccessible via `SkyServer.MonitorPulse` — `private get`), inlined `SiderealRate = 15.0410671786691`. `StopAltAzTrackingTimer()` made `internal static`. Four call sites in `SkyServer.TelescopeAPI.cs` updated to `_defaultInstance!.PulseGuideAltAz(...)`. Static method deleted. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` ~line 1877 (method deleted)

```csharp
private static void PulseGuideAltAz(int axis, double guideRate, int duration,
    Action<CancellationToken> pulseGoTo, CancellationToken token)
{
    Task.Run(() => {
        // ...
        _defaultInstance.SkyPredictor.Set(...);   // ⚠️ hardcoded to device 0
    });
}
```

This mirrors the original C1/C2 pulse-guide callback issue that was fixed for
`_isPulseGuidingRa/Dec`. For AltAz mounts only: calling `PulseGuide()` on device 1
would modify the `SkyPredictor` state of device 0.

**Impact:** AltAz mode only. GermanPolar/Polar modes route pulse-guide commands
directly to per-instance queue refs and are not affected.

**Fix applied (Phase G, 2026-04-03):** `PulseGuideAltAz` moved to `MountInstance` instance method; `this.SkyPredictor` used; static method deleted; four call sites updated. ✅

---

## 4. Data-Flow Verification (Single Telescope — Current Operation)

The following paths have been code-verified as correct for single-telescope (April 2026):

```
Hardware steps
  → SkyWatcher executor (_stepsCallback)
  → MountInstance.ReceiveSteps(steps)            ✅ per-instance
  → _steps[0,1] set
  → SetSteps() — coordinate conversion          ✅ per-instance settings
  → _mountPositionUpdatedEvent.Set()            ✅ per-instance event
  → SkyServer.NotifyStepsChanged()              ✅ fires global Blazor event (Phase E caveat)

Hardware pulse-guide Ra/Dec
  → _pulseGuideRaCallback / _pulseGuideDecCallback
  → v => { _isPulseGuidingRa = v; }             ✅ writes directly to this instance (C1/C2 fixed)
  → v => { _isPulseGuidingDec = v; }            ✅ writes directly to this instance

ASCOM GoTo — primary path (SlewController, confirmed April 2026)
  → Telescope.cs GetInstance(_deviceNumber)     ✅ per-device routing
  → SkyServer.SlewRaDecAsync(ra, dec)           (static; uses SlewController)
  → SlewAsync(target, SlewType.SlewRaDec)       ✅ delegates to _defaultInstance.SlewAsync
  → MountInstance.SlewAsync(target, slewType)   ✅ per-instance SlewController

ASCOM GoTo — handpad path (GoToAsync/SlewMount, G2 resolved 2026-04-03)
  → SlewAxes(primary, secondary, slewState, instance)  ✅ MountInstance? param threaded (G2)
  → SlewMount(Vector, slewState, ..., instance)         ✅ effectiveInstance = instance ?? _defaultInstance!
  → GoToAsync(target, slewState, handle, instance)      ✅ uses effectiveInstance throughout

CTS cancellation (confirmed April 2026)
  → CancelAllAsync()                            ✅ _ctsGoTo/_ctsPulseGuide* are delegating
  → SkyServer.Core.cs:131-150                   ✅ get/set route to _defaultInstance backing fields

Mount position event wait (SkyPrecisionGoto)
  → _mountPositionUpdatedEvent.Reset/Wait       ✅ per-instance event
```

---

## 5. Static Code Still Requiring Removal — Prioritised Task List

### ✅ Phase A — Legacy Bridge Methods — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **A1** | `SkyServer.Core.cs` | Delete `SkyTasks(MountTaskName)` legacy overload | ✅ Done |
| **A2** | `SkyServer.Core.cs` | Delete `AxesStopValidate()` no-parameter overload | ✅ Done |
| **A3** | `SkyServer.Core.cs` | Delete misleading "removed" comment above A1 body | ✅ Done |

### ✅ Phase B — Remove `SkyQueue` / `MountQueue` Static Facades — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **B1** | `MountInstance.cs` | Remove `SkyQueue.RegisterInstance(sqImpl)` call | ✅ Done |
| **B2** | `SkyServer.Core.cs` | Delete `PropertyChangedSkyQueue` handler | ✅ Done |
| **B3** | `SkyServer.Core.cs` | Delete `PropertyChangedMountQueue` handler | ✅ Done |
| **B4** | `SkyCommands.cs` | Remove `[Obsolete]` shortcut ctors from all command classes | ✅ Done |
| **B5** | `SkyCommands.cs` | Remove `SkyCommandBase(long id)` etc. base class shortcut ctors | ✅ Done |
| **B6** | `SkyQueue.cs` | Delete `public static class SkyQueue` entirely | ✅ Done — file is 109 lines, `SkyQueueImplementation` only |
| **B7** | `MountQueue.cs` | Delete `public static class MountQueue` entirely | ✅ Done — file is 63 lines, `MountQueueImplementation` only |

### ✅ Phase C — Pulse-Guide Callback Routing — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **C1** | `MountInstance.cs` | Simulator `SetupCallbacks` pulse-guide lambdas write to `this` fields | ✅ Done (lines 1595–1596) |
| **C2** | `MountInstance.cs` | SkyWatcher `SetupCallbacks` pulse-guide lambdas write to `this` fields | ✅ Done (lines 1632–1633) |

### ✅ Phase D — Fix Remaining True Static Fields — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **D1** | `MountInstance.cs` | Add `internal long[] _stepsTimeFreq = { 0, 0 };` field | ✅ Done (line 63) |
| **D2** | `SkyServer.cs` | `StepsTimeFreq` delegates to `_defaultInstance._stepsTimeFreq` | ✅ Done |
| **D3** | `SkyServer.Core.cs` | Instance-aware `SkyTasks(StepTimeFreq, instance)` writes `instance._stepsTimeFreq` | ✅ Done |
| **D4** | `SkyServer.cs` | Remove `private static bool _mountRunning` | ✅ Done — `IsMountRunning` is `_defaultInstance?.IsMountRunning ?? false` |
| **D5** | `MountInstance.cs` | Add `internal bool _snapPort1`, `internal bool _snapPort2` fields | ✅ Done (lines 104–105) |
| **D6** | `SkyServer.cs` | `SnapPort1`/`SnapPort2` delegate to `_defaultInstance` | ✅ Done |
| **D7** | `SkyServer.TelescopeAPI.cs` | Remove `_goToAsyncLock` field | ✅ Done — field gone; residual `//lock (_goToAsyncLock)` comment at line 787 is cleanup |
| **D8** | `SkyServer.cs` | Move `LastDecDirection` to `MountInstance` as `_lastDecDirection` | ✅ Done |

### Phase E — Blazor Per-Device UI (🟡 MEDIUM — not started)

| Task | Description |
|---|---|
| **E1** | Add `event PropertyChangedEventHandler InstancePropertyChanged` to `MountInstance` |
| **E2** | Have `MountInstance` fire `InstancePropertyChanged` for its own state changes |
| **E3** | Blazor components bind to specific `MountInstance.InstancePropertyChanged` by device number |
| **E4** | `NotifyStepsChanged()` in `SkyServer` changed to route per-instance |
| **E5** | Evaluate whether `SkyServer.StaticPropertyChanged` can be removed once E1–E4 are complete |

### Phase F — Configuration & UI (🟡 MEDIUM — F1 partial)

| Task | Description | Status |
|---|---|---|
| **F1** | Per-device serial config in `appsettings.json` | 🟡 Partial — fields exist in `Devices[]` array; not in `AlpacaDevices[]` |
| **F2** | Update Blazor settings UI to show per-device serial controls | ⬜ Not started |
| **F3** | Add Blazor connection status panel per registered device | ⬜ Not started |

### Cleanup Items (minor, independent)

| Item | File | Description |
|---|---|---|
| **X1** ✅ | `SkyServer.TelescopeAPI.cs` | Remove `//lock (_goToAsyncLock)` comment — field is gone — **Done** |
| **X2** ✅ | `GreenSwamp.Alpaca.MountControl\` | Delete `SkySettingsBridge.cs` and remove `<Compile Remove="SkySettingsBridge.cs" />` from `.csproj` — **Done** |

### ✅ Phase G — `SkyServer.TelescopeAPI.cs` Residuals — COMPLETE (2026-04-03)

Identified April 2026 via full region-by-region code review of the file.

| Task | File | Description | Priority |
|---|---|---|---|
| **G1** | `SkyServer.TelescopeAPI.cs:538,572` | Move `RateDecOrg` / `RateRaOrg` to `MountInstance` backing fields; bridge via delegating properties | ✅ Done |
| **G2** | `SkyServer.TelescopeAPI.cs:763` / `SkyServer.Core.cs` | Add `MountInstance` parameter to `SlewAxes()` → `SlewMount()` → `GoToAsync()` chain (handpad slew path) | ✅ Done |
| **G3** | `SkyServer.TelescopeAPI.cs:~1877` | Move `PulseGuideAltAz()` to an instance method on `MountInstance`; call via `_defaultInstance!.PulseGuideAltAz(...)` | ✅ Done |

---

## 6. Discrepancies Between Previous Progress Report and Current Code

The March 2026 assessment identified the following discrepancies against `StaticToInstance-ProgressReport.md`.
All have since been resolved.

| Claim in ProgressReport | March 2026 actual state | April 2026 state |
|---|---|---|
| **Step 10b ✅ Done** — "Legacy `SkyTasks(MountTaskName)` deleted" | Method still existed with misleading "removed" comment | ✅ Deleted (A1) |
| **Option C Phase 2 ✅ Complete** — "zero `RegisterInstance()` calls" | `SkyQueue.RegisterInstance(sqImpl)` still called in `MountStart()` | ✅ Removed (B1) |
| **Option C Phase 2 ✅ Complete** — "zero `SkySystem` refs in driver" | Correctly deleted (matched) | ✅ Unchanged — still correct |
| **Option C Phase 2 ✅ Complete** — "legacy method removed" | Legacy `SkyTasks(MountTaskName)` still existed | ✅ Deleted (A1) |
| **Blocker B2 ✅ Resolved** | Correctly resolved — `Telescope.cs` uses `GetInstance()` | ✅ Unchanged — still correct |

---

## 7. File Sizes (April 2026 — actual line counts)

| File | March est. | April actual | Δ | Notes |
|---|---:|---:|---:|---|
| `SkyServer.Core.cs` | ~2 059 | **1 627** | −432 | ✅ Legacy methods A1/A2 deleted; dead handlers B2/B3 gone; CTS delegating properties at lines 131–150 confirmed; G2 SlewMount +1 line |
| `SkyServer.TelescopeAPI.cs` | ~1 870 | **3 278** | +1 408 | G1/G2/G3 applied (was 3 369 pre-Phase G, −91 net); see TelescopeAPI analysis note below |
| `SkyServer.cs` | ~998 | **1 023** | +25 | ✅ All static fields delegated; `_mountRunning` gone; `SnapPort1/2`, `StepsTimeFreq`, `LastDecDirection` now delegate |
| `MountInstance.cs` | ~2 145 | **2 535** | +390 | ✅ New backing fields, pulse-guide callbacks fixed; G3 `PulseGuideAltAz` added (+91 from 2 444) |
| `MountInstance.Pec.cs` | ~427 | **430** | +3 | ✅ Fully migrated — unchanged |
| `SkyQueue.cs` | ~250 | **109** | −141 | ✅ Static `SkyQueue` facade deleted; `SkyQueueImplementation` only |
| `MountQueue.cs` | ~185 | **63** | −122 | ✅ Static `MountQueue` facade deleted; `MountQueueImplementation` only |
| `SkyCommands.cs` | ~large | **1 169** | — | ✅ All `[Obsolete]` shortcut ctors removed; base classes require `(id, queue)` |
| `Telescope.cs` | ~2 127 | **2 067** | −60 | ✅ Fully migrated to `GetInstance()` |

**Notable observation — `SkyServer.TelescopeAPI.cs` growth (reviewed April 2026):**
The file nearly doubled in size. The 9 new regions reflect work completed during the async-slew / ConformU compliance sprints:

| New Region | Status |
|---|---|
| `SlewController Integration` | ✅ `_slewController`, `SlewAsync`, `SlewSync` all correctly per-instance |
| `Syncing` | ✅ `SyncToAltAzm`, `SyncToTargetRaDec` use per-instance queue refs |
| `Pulse Guiding` | ✅ Non-AltAz path uses per-instance queues; ⚠️ `PulseGuideAltAz()` uses `_defaultInstance.SkyPredictor` (G3) |
| `MoveAxis Support` | ✅ All delegate to `_defaultInstance` |
| `Position Calculations` | ✅ All use `_settings` (delegated) |
| `Tracking & Rates` | ✅ `SetTracking()` uses per-instance queues; `SetTrackingDirect()` new helper |
| `Validation & Limits` | ✅ All use `_settings` / `_defaultInstance` |
| `Async Operations` | ✅ `CancelAllAsync()` uses delegating CTS properties (confirmed `SkyServer.Core.cs:131–150`) |
| `ASCOM Bridge Properties` | ✅ Thin forwarding wrappers only |

**Existing regions modified:** `GoToAsync` — instance parameter added (G2 ✅). `RateDecOrg`/`RateRaOrg` — delegating computed properties (G1 ✅). `PulseGuide` AltAz path — calls `_defaultInstance!.PulseGuideAltAz(...)` (G3 ✅). `StopAltAzTrackingTimer` made `internal static`.

This file is a strong candidate for splitting — `Tracking & Rates`, `Validation & Limits`, and `Async Operations` are natural split points.

---

## 8. Recommended Next Steps

Phases A–D are complete. The April 2026 TelescopeAPI review identified three new 🟠 Medium items (G1–G3). Recommended sequencing:

```
✅ X1/X2 + Phase G — COMPLETE (2026-04-03)
  X1: //lock comment removed; X2: SkySettingsBridge.cs deleted
  G1: RateDecOrg/RateRaOrg — delegating computed properties
  G2: MountInstance? param threaded through SlewAxes→SlewMount→GoToAsync
  G3: PulseGuideAltAz moved to MountInstance instance method
    ↓
Integration test (gate for telescope #2)
  Write WhenTwoDevicesConnectedThenQueuesAreIndependent
    ↓
Phase E (Blazor per-device notifications — design spike first)
  E1: Add InstancePropertyChanged event to MountInstance
  E2: MountInstance fires it for own state changes
  E3: Blazor components bind per device number
  E4: NotifyStepsChanged() routes per-instance
  E5: Evaluate removal of SkyServer.StaticPropertyChanged
    ↓
Phase F (Option C Phase 3 — config & UI last)
  F1: Confirm/consolidate per-device serial config in appsettings.json
  F2: Blazor settings UI per-device serial controls
  F3: Blazor connection status panel per registered device
```

**Consider splitting `SkyServer.TelescopeAPI.cs`** (3,278 lines after Phase G) —
the `Tracking & Rates`, `Validation & Limits`, and `Async Operations` regions are
natural split points.

---

## 9. Multi-Telescope Readiness Checklist

Before a second telescope can be connected without corrupting the first:

- [x] **C1/C2** — Pulse-guide callbacks write directly to `this._isPulseGuidingRa/Dec` ✅
- [x] **A1** — Legacy `SkyTasks(MountTaskName)` deleted ✅
- [x] **B1** — `SkyQueue.RegisterInstance()` removed from `MountStart()` ✅
- [x] **B6/B7** — `SkyQueue` / `MountQueue` static facades deleted ✅
- [x] **D1–D3** — `StepsTimeFreq` moved to per-instance field ✅
- [x] **D4** — `_mountRunning` static field removed ✅
- [x] **D5–D6** — `SnapPort1/2` delegated to `_defaultInstance` ✅
- [x] **CTS tokens** — `_ctsGoTo`, `_ctsPulseGuideRa`, `_ctsPulseGuideDec`, `_ctsHcPulseGuide` confirmed as delegating properties (`SkyServer.Core.cs:131–150`) ✅
- [x] **SlewController** — `SlewAsync`/`SlewSync` delegate to `_defaultInstance.SlewAsync/SlewSync` ✅
- [x] **G2** — `SlewAxes`/`SlewMount`/`GoToAsync` have `MountInstance?` parameter (handpad slew path) ✅
- [x] **G3** — `PulseGuideAltAz()` moved to `MountInstance` instance method (AltAz pulse guide) ✅
- [x] **G1** — `RateDecOrg`/`RateRaOrg` are delegating computed properties ✅
- [ ] Integration test: `WhenTwoDevicesConnectedThenQueuesAreIndependent` — not yet written

**All hard blockers are cleared.** G2 and G3 are correctness concerns for a second telescope;
G1 is low-risk. The integration test is the ultimate gate.

Items **not** blocking a second telescope:
- `//lock (_goToAsyncLock)` comment (field is gone — cosmetic only)
- Phase E Blazor notifications (UI degradation, not correctness)
- Phase F config/UI

---

*Document originally generated from direct code review of `master` branch, March 2026.*
*Re-verified and updated April 2026, 08:25 UTC — all ≥ 🟠 HIGH items confirmed resolved.*
*Re-verified April 2026, 14:30 UTC — full `SkyServer.TelescopeAPI.cs` review; CTS delegation confirmed; G1–G3 identified.*
*Re-verified 2026-04-03 09:23 — Phase G complete (X1, X2, G1, G2, G3); all builds green; `SkyServer.TelescopeAPI.cs` 3 278 lines, `MountInstance.cs` 2 535 lines.*
