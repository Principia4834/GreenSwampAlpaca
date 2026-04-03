# Static-to-Instance Migration — Current Status Report
## GreenSwamp Alpaca — SkyServer / MountControl

**Prepared:** March 2026 (post-SkyWatcher GermanPolar confidence test)
**Branch:** `master`
**Build baseline:** ✅ Green — Simulator and SkyWatcher GermanPolar pass confidence tests
**Assessment method:** Direct code review of all five key files + queue subsystem

---

## 1. Executive Summary

The migration is **functionally complete for single-telescope operation** and is estimated
at approximately **93 %** complete overall. Every piece of per-device state now lives on
`MountInstance`; the static `SkyServer.*` surface is a pure thin delegation layer to
`MountInstanceRegistry.GetInstance(0)`. The ASCOM driver (`Telescope.cs`) routes entirely
through `GetInstance(_deviceNumber)`.

The remaining **7 %** consists of three categories:

| Category | Items | Blocking multi-telescope? |
|---|---|---|
| **Legacy static bridge methods** | `SkyTasks(MountTaskName)`, `AxesStopValidate()` (no-instance overloads) still compiled | 🔴 Yes — use `SkyQueue.Instance` |
| **`SkyQueue` / `MountQueue` static facades** | Still referenced by legacy methods + `RegisterInstance()` still called | 🔴 Yes — single `_instance` slot |
| **Remaining true static fields** | `StepsTimeFreq`, `_mountRunning`, `SnapPort1/2`, `_goToAsyncLock`, `LastDecDirection` | 🟠 Partial |
| **B5 Blazor notifications** | `StaticPropertyChanged` fires globally for all devices | 🟡 UI only |
| **Option C Phase 3** | Per-device serial config in JSON, Blazor status panel | 🟡 UI / config |

---

## 2. What Is Confirmed Complete (code-verified)

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
| Shortcut ctors marked `[Obsolete]` in `SkyCommands.cs` | `SkyCommands.cs` | All 38+ classes |
| PEC system fully migrated | `MountInstance.Pec.cs` | Fully instance-based |
| Cancellation tokens per-instance | `MountInstance.cs` | `_ctsGoTo`, `_ctsPulseGuide*`, `_ctsHcPulseGuide` |
| `SlewController` per-instance | `MountInstance.cs` | `_slewController` field |
| HC anti-backlash fields per-instance | `MountInstance.cs` | `_hcPrevMoveRa/Dec`, `_hcPrevMovesDec` |
| Tracking offset rate per-instance | `MountInstance.cs` | `_trackingOffsetRate` field |
| `_skyTrackingOffset` per-instance | `MountInstance.cs` | `int[2]` array |

---

## 3. Remaining Work — Detailed Inventory

### 3.1 🔴 CRITICAL — Legacy `SkyTasks(MountTaskName)` Still Exists

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`
**Contrary to the previous progress report, this method was NOT deleted.**
A comment immediately above the method body reads "Legacy SkyTasks(MountTaskName) removed"
but the method definition continues below it. The method is live, compiled code.

```csharp
// SkyServer.Core.cs — this method still exists
public static void SkyTasks(MountTaskName taskName)
{
#pragma warning disable CS0618   // still uses SkyQueue.NewId, SkyQueue.GetCommandResult
    if (!IsMountRunning) { return; }
    // ... switch (_settings!.Mount) ...
    //     case SkyWatcher: uses SkyQueue.NewId, SkyQueue.GetCommandResult directly
#pragma warning restore CS0618
}
```

**Impact:** Any call reaching this overload dispatches to `SkyQueue.Instance` (whichever
telescope last called `RegisterInstance()`), not the caller's telescope.

**Callers confirmed in codebase:** None found by audit — all known external callers
(`CommandStrings.cs`, `SkySettingsInstance.cs`, `SkyServer.TelescopeAPI.cs`) have already
been migrated to the instance-aware two-parameter overload. The legacy method is dead
for single-telescope operation but is compiled and creates risk if called via reflection
or a future refactor path.

**Action:** Delete the method body. The comment claiming "removed" should be deleted too.

---

### 3.2 🔴 CRITICAL — Legacy `AxesStopValidate()` (No Instance Parameter) Still Exists

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`

```csharp
internal static bool AxesStopValidate()   // legacy overload — still compiled
{
#pragma warning disable CS0618
    // ... uses SkyQueue.NewId, SkyQueue.GetCommandResult, MountQueue.NewId ...
#pragma warning restore CS0618
}
```

The instance-aware `AxesStopValidate(MountInstance instance)` overload is complete
and correct. The no-parameter legacy overload should be deleted. Verify no callers remain
before deletion.

---

### 3.3 🔴 CRITICAL — `SkyQueue.RegisterInstance(sqImpl)` Still Called from `MountStart()`

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

```csharp
// MountInstance.MountStart() — SkyWatcher branch
SkyQueue.RegisterInstance(sqImpl);  // ← still present; overwrites single _instance slot
sqImpl.Start(_serial, custom360Steps, customWormSteps, SkyServer.LowVoltageEventSet);
SkyQueueInstance = sqImpl;
```

`SkyQueue._instance` is a single static pointer. A second telescope's `MountStart()`
overwrites it. The comment in the source correctly notes:
> "must be called before Start() so the facade is live when commands execute"

This dependency means the static facade is still load-bearing for the SkyWatcher path.
The facade needs to be removed, and with it this `RegisterInstance` call.

**Root cause:** The legacy `SkyTasks(MountTaskName)` method still uses `SkyQueue.NewId`
and `SkyQueue.GetCommandResult`, requiring the facade to be active. Once that method is
deleted, `RegisterInstance()` can be removed.

**Note:** The Simulator path does NOT call `MountQueue.RegisterInstance()` — it starts
the queue directly. Only SkyWatcher still calls `RegisterInstance()`.

---

### 3.4 🔴 CRITICAL — Pulse-Guide Callbacks Write Through Static Property

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

```csharp
// MountStart() — both Simulator and SkyWatcher branches
mqImpl.SetupCallbacks(
    steps => ReceiveSteps(steps),              // ✅ per-instance
    v => SkyServer.IsPulseGuidingRa = v,       // ❌ static setter → _defaultInstance
    v => SkyServer.IsPulseGuidingDec = v);     // ❌ static setter → _defaultInstance
```

For a single telescope this works because `_defaultInstance` is the same instance.
For multiple telescopes, all devices' pulse-guide callbacks write to device 0's
`_isPulseGuidingRa/Dec` fields.

**Fix:** Change callbacks to write directly to `this` fields:
```csharp
v => { _isPulseGuidingRa = v; },
v => { _isPulseGuidingDec = v; }
```

---

### 3.5 🟠 HIGH — `PropertyChangedSkyQueue` / `PropertyChangedMountQueue` Are Dead Code

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`

These two handlers are defined but **never subscribed** to any event. Searching the
entire codebase for `SkyQueue.StaticPropertyChanged += PropertyChangedSkyQueue` and
`MountQueue.StaticPropertyChanged += PropertyChangedMountQueue` yields zero results.
The `Initialize()` method only wires `_settings.PropertyChanged += PropertyChangedSkySettings`.

The `MountStart()` callbacks (`ReceiveSteps`, static `IsPulseGuidingRa/Dec`) entirely
bypass `SkyQueue.Steps`, `SkyQueue.IsPulseGuidingRa/Dec` static properties, so
`SkyQueue.StaticPropertyChanged` is never fired in practice.

**Impact:** Benign for single-telescope operation. These handlers and their references to
`SkyQueue.Steps` / `MountQueue.Steps` are currently unreachable code.

**Action:** Delete both handlers when the static queue facades are removed.

---

### 3.6 🟠 HIGH — `StepsTimeFreq` Is a Truly Static Field

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
public static long[] StepsTimeFreq { get; private set; } = { 0, 0 };
```

This has no instance backing. It is written by:
```csharp
// In SkyTasks(MountTaskName.StepTimeFreq, instance) — instance-aware overload
var skyStepTimeFreq = new SkyGetStepTimeFreq(q.NewId, q);
StepsTimeFreq = (long[])q.GetCommandResult(skyStepTimeFreq).Result;  // ← writes static
```

The instance-aware `SkyTasks` overload still writes to the **static** `StepsTimeFreq`
instead of `instance._stepsTimeFreq`. For a second telescope this overwrites the first
telescope's timing frequency, breaking `CalcCustomTrackingOffset()` for both.

**Fix required:** Add `_stepsTimeFreq` backing field to `MountInstance`; update the
instance-aware `SkyTasks` to write `instance._stepsTimeFreq`; update
`CalcCustomTrackingOffset()` to read from `instance._stepsTimeFreq`.

---

### 3.7 🟠 HIGH — `_mountRunning` Static Field Still Exists

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
private static bool _mountRunning;   // ← true static field

public static bool IsMountRunning
{
    get => _defaultInstance?.IsMountRunning ?? _mountRunning;
    set
    {
        _mountRunning = value;         // ← writes static
        if (value) MountStart();
        else       MountStop();
    }
}
```

`SkyErrorHandler()` sets `IsMountRunning = false` which writes `_mountRunning = false`
and calls `MountStop()` on `_defaultInstance` only. The static `_mountRunning` is a
remnant fallback that diverges from per-device truth once multiple devices are connected.

---

### 3.8 🟠 HIGH — `SnapPort1` / `SnapPort2` Are Truly Static

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
public static bool SnapPort1 { get; set; }
public static bool SnapPort2 { get; set; }
```

These have no `_defaultInstance` delegation. For two telescopes, snap port state is a
single global boolean. The `SnapPort1Result` / `SnapPort2Result` properties (which report
the hardware response) are already per-instance. The command-trigger properties are not.

---

### 3.9 🟡 MEDIUM — `_goToAsyncLock` Is Static

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

```csharp
private static readonly object _goToAsyncLock = new object();
```

GoTo on telescope 1 acquires this lock, blocking GoTo on telescope 2 during the
entire slew setup phase. Note: the lock appears to currently be commented out in
`GoToAsync()`, so the practical impact is low, but the field should move to
`MountInstance` when GoTo logic is fully migrated.

---

### 3.10 🟡 MEDIUM — `LastDecDirection` Is Static

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
private static GuideDirection LastDecDirection { get; set; }
```

Used in pulse-guide Dec backlash logic. No instance backing. Second telescope's guiding
corrupts first telescope's backlash direction state.

---

### 3.11 🟡 MEDIUM — `SkySettingsBridge.cs` Status Unclear

The progress report states this file is "excluded from compilation." The file still
exists in the workspace. Verify it is excluded from the `.csproj` and add a comment
explaining why it is being kept, or delete it.

---

### 3.12 🟡 MEDIUM — B5: Blazor Per-Device UI Notifications Not Implemented

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
public static event PropertyChangedEventHandler StaticPropertyChanged;
```

All Blazor UI components subscribe to this single static event. Every `SkyServer.*`
property setter fires it. For multiple telescopes, every state change on any device
causes all Blazor components to refresh regardless of which device they are bound to.

No per-device notification mechanism exists. Implementing it requires:
- Each `MountInstance` to have its own `PropertyChanged` event
- Blazor components to bind to the specific `MountInstance` event for their device number
- The static `NotifyStepsChanged()` helper to be replaced or supplemented with
  per-device routing

---

### 3.13 🟡 MEDIUM — Option C Phase 3: Config & UI

Not started. Per-device serial settings need to be moveable into the `AlpacaDevices`
array in `appsettings.json` so each entry can specify `Port`, `BaudRate`, etc.
independently. The Blazor connection status panel also needs per-device controls.

---

## 4. Data-Flow Verification (Single Telescope — Current Operation)

The following paths have been code-verified as correct for single-telescope:

```
Hardware steps
  → SkyWatcher executor (_stepsCallback)
  → MountInstance.ReceiveSteps(steps)            ✅ per-instance
  → _steps[0,1] set
  → SetSteps() — coordinate conversion          ✅ per-instance settings
  → _mountPositionUpdatedEvent.Set()            ✅ per-instance event
  → SkyServer.NotifyStepsChanged()              ✅ fires global Blazor event (B5 caveat)

Hardware pulse-guide Ra/Dec
  → _pulseGuideRaCallback / _pulseGuideDecCallback
  → SkyServer.IsPulseGuidingRa/Dec = v          ❌ writes _defaultInstance (Issue 3.4)

ASCOM GoTo (Telescope.cs)
  → GetInstance(_deviceNumber)                  ✅ per-device routing
  → SkyServer.SlewRaDecAsync(...)               (static method; uses _defaultInstance)
  → GoToAsync → SkyGoTo → SkyPrecisionGoto/SkyPulseGoto
  → MountInstance.SkyGoTo (instance method)     ✅
  → SkyAxisGoToTarget(SkyQueueInstance!.NewId, SkyQueueInstance!, ...)  ✅ per-instance queue

Mount position event wait (SkyPrecisionGoto)
  → _mountPositionUpdatedEvent.Reset/Wait       ✅ per-instance event
```

---

## 5. Static Code Still Requiring Removal — Prioritised Task List

The items below are ordered by dependency. Each must be done before the next
unless marked as independent.

### Phase A — Remove Legacy Bridge Methods (prerequisite for Phase B)

| Task | File | Description | Dependency |
|---|---|---|---|
| **A1** | `SkyServer.Core.cs` | Delete `SkyTasks(MountTaskName)` legacy overload (confirm zero callers first) | None |
| **A2** | `SkyServer.Core.cs` | Delete `AxesStopValidate()` no-parameter overload (confirm zero callers first) | None |
| **A3** | `SkyServer.Core.cs` | Delete misleading "removed" comment above A1 body | A1 |

### Phase B — Remove `SkyQueue` / `MountQueue` Static Facades (requires Phase A)

| Task | File | Description | Dependency |
|---|---|---|---|
| **B1** | `MountInstance.cs` | Remove `SkyQueue.RegisterInstance(sqImpl)` call from `MountStart()` SkyWatcher branch | A1 (legacy method was the only static facade consumer) |
| **B2** | `SkyServer.Core.cs` | Delete `PropertyChangedSkyQueue` handler (dead code, never subscribed) | B1 |
| **B3** | `SkyServer.Core.cs` | Delete `PropertyChangedMountQueue` handler (dead code, never subscribed) | B1 |
| **B4** | `SkyCommands.cs` | Remove shortcut (0-arg or 1-arg `id`) constructors marked `[Obsolete]` from all 38+ command classes | A1, B1 |
| **B5** | `SkyCommands.cs` | Remove `SkyCommandBase(long id)`, `SkyQueryCommand(long id)`, `SkyActionCommand(long id)` base class shortcut ctors | B4 |
| **B6** | `SkyQueue.cs` | Delete `public static class SkyQueue` entirely | B1–B5 |
| **B7** | `MountQueue.cs` | Delete `public static class MountQueue` entirely | B1–B5 |

### Phase C — Fix Pulse-Guide Callback Routing (independent, can be done now)

| Task | File | Description | Dependency |
|---|---|---|---|
| **C1** | `MountInstance.cs` | Change Simulator `SetupCallbacks` pulse-guide lambdas to `v => { _isPulseGuidingRa = v; }` / `v => { _isPulseGuidingDec = v; }` | None |
| **C2** | `MountInstance.cs` | Same fix for SkyWatcher `SetupCallbacks` | None |

### Phase D — Fix Remaining True Static Fields (mostly independent)

| Task | File | Description | Dependency |
|---|---|---|---|
| **D1** | `MountInstance.cs` | Add `internal long[] _stepsTimeFreq = { 0, 0 };` field | None |
| **D2** | `SkyServer.cs` | Change `StepsTimeFreq` from auto-property to delegating property `=> _defaultInstance?._stepsTimeFreq ?? new long[]{0,0}` | D1 |
| **D3** | `SkyServer.Core.cs` | Update instance-aware `SkyTasks(StepTimeFreq, instance)` to write `instance._stepsTimeFreq` | D1 |
| **D4** | `SkyServer.cs` | Remove `private static bool _mountRunning`; `IsMountRunning` getter removes `?? _mountRunning` fallback | None |
| **D5** | `MountInstance.cs` | Add `internal bool _snapPort1`, `internal bool _snapPort2` fields | None |
| **D6** | `SkyServer.cs` | Change `SnapPort1`/`SnapPort2` to delegate to `_defaultInstance` | D5 |
| **D7** | `SkyServer.TelescopeAPI.cs` | Move `_goToAsyncLock` to `MountInstance` (or remove if lock body is still commented out) | None |
| **D8** | `SkyServer.cs` | Move `LastDecDirection` to `MountInstance` as `_lastDecDirection` | None |

### Phase E — Blazor Per-Device UI (B5, independent sprint)

| Task | Description |
|---|---|
| **E1** | Add `event PropertyChangedEventHandler InstancePropertyChanged` to `MountInstance` |
| **E2** | Have `MountInstance` fire `InstancePropertyChanged` for its own state changes |
| **E3** | Blazor components bind to specific `MountInstance.InstancePropertyChanged` by device number |
| **E4** | `NotifyStepsChanged()` in `SkyServer` changed to route per-instance |
| **E5** | Evaluate whether `SkyServer.StaticPropertyChanged` can be removed once E1–E4 are complete |

### Phase F — Configuration & UI (Option C Phase 3)

| Task | Description |
|---|---|
| **F1** | Add `Port`, `BaudRate`, `HandShake`, `DataBits`, `ReadTimeout`, `DtrEnable`, `RtsEnable` to each `AlpacaDevices[]` entry in `appsettings.json` schema |
| **F2** | Update Blazor settings UI to show per-device serial controls |
| **F3** | Add Blazor connection status panel per registered device |

---

## 6. Discrepancies Between Previous Progress Report and Current Code

The following claims in `StaticToInstance-ProgressReport.md` do not match the actual
code state as of this assessment:

| Claim | Actual state |
|---|---|
| **Step 10b ✅ Done** — "Legacy `SkyTasks(MountTaskName)` method deleted" | Method still exists in `SkyServer.Core.cs` with a misleading "removed" comment above its body |
| **Option C Phase 2 ✅ Complete** — "zero `RegisterInstance()` calls" | `SkyQueue.RegisterInstance(sqImpl)` is still called in `MountInstance.MountStart()` SkyWatcher branch |
| **Option C Phase 2 ✅ Complete** — "zero `SkySystem` refs in driver" | `SkySystem` is correctly deleted (matches) |
| **Option C Phase 2 ✅ Complete** — "legacy method removed" | Legacy `SkyTasks(MountTaskName)` still exists (contradicts) |
| **Blocker B2 ✅ Resolved** | Correctly resolved — `Telescope.cs` uses `GetInstance()` |

The root cause is likely that Step 10b and Phase 2 completion were recorded when the
work was planned, before a later change re-introduced or retained the legacy method,
possibly during the `SkyTasks` instance-aware implementation sprint.

---

## 7. File Sizes (current)

| File | Approx. Lines | Notes |
|---|---:|---|
| `SkyServer.Core.cs` | ~2 059 | Still contains two legacy static methods |
| `SkyServer.TelescopeAPI.cs` | ~1 870 | Clean; all delegates to `_defaultInstance` |
| `SkyServer.cs` | ~998 | Contains `_mountRunning`, `SnapPort1/2`, `StepsTimeFreq` |
| `MountInstance.cs` | ~2 145 | Pulse-guide callback routing still uses static setters |
| `MountInstance.Pec.cs` | ~427 | ✅ Fully migrated |
| `SkyQueue.cs` | ~250 | Static facade — to be deleted |
| `MountQueue.cs` | ~185 | Static facade — to be deleted |
| `SkyCommands.cs` | ~large | 38+ command classes, all with `[Obsolete]` shortcut ctors |
| `Telescope.cs` | ~2 127 | ✅ Fully migrated to `GetInstance()` |

---

## 8. Recommended Execution Sequence

```
Phase C (pulse-guide fix — 2 lines, zero risk, do immediately)
    ↓
Phase A (delete 2 legacy methods — verify callers first)
    ↓
Phase B (remove static facades SkyQueue/MountQueue + shortcut ctors)
    ↓
Phase D (fix StepsTimeFreq, _mountRunning, SnapPort, _goToAsyncLock, LastDecDirection)
    ↓
Phase E (Blazor per-device notifications — design spike first)
    ↓
Phase F (Option C Phase 3 — config & UI last)
```

After **Phase B** completes, the following files can be deleted entirely:
- `GreenSwamp.Alpaca.Mount.SkyWatcher\SkyQueue.cs` (the `public static class SkyQueue` portion)
- `GreenSwamp.Alpaca.Simulator\MountQueue.cs` (the `public static class MountQueue` portion)

The `SkyQueueImplementation` class (in `SkyQueue.cs`) and `MountQueueImplementation`
class (in `MountQueue.cs`) must be kept — only the static wrapper classes are deleted.

---

## 9. Multi-Telescope Readiness Checklist

Before a second telescope can be connected without corrupting the first:

- [ ] **C1/C2** — Pulse-guide callbacks write directly to `this._isPulseGuidingRa/Dec`
- [ ] **A1** — Legacy `SkyTasks(MountTaskName)` deleted
- [ ] **B1** — `SkyQueue.RegisterInstance()` removed from `MountStart()`
- [ ] **B6/B7** — `SkyQueue` / `MountQueue` static facades deleted (or at minimum `_instance` made per-key)
- [ ] **D1–D3** — `StepsTimeFreq` moved to per-instance field
- [ ] **D4** — `_mountRunning` static field removed
- [ ] **D5–D6** — `SnapPort1/2` delegated to `_defaultInstance`
- [ ] Integration test: `WhenTwoDevicesConnectedThenQueuesAreIndependent` passes

Items **not** blocking a second telescope:
- `_goToAsyncLock` (commented-out lock body)
- `LastDecDirection` (backlash state; per-device corruption, not crash)
- Phase E Blazor notifications (UI degradation, not correctness)
- Phase F config/UI

---

*Document generated from direct code review of `master` branch, March 2026.*
*No code was modified during this assessment.*
