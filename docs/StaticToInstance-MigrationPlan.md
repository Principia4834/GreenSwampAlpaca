# Static-to-Instance Migration Plan
## GreenSwamp Alpaca — SkyServer / MountControl

**Prepared for:** Offline review  
**Codebase state analysed:** June 2025 (`master` branch)  
**Scope:** `GreenSwamp.Alpaca.MountControl` + `GreenSwamp.Alpaca.Server.TelescopeDriver`

---

## 1. Executive Summary

The application is approximately **60 % migrated** from a monolithic static `SkyServer` class to
instance-based `MountInstance` objects managed by `MountInstanceRegistry`.  
The migration is structured as "Phase 4.x" in the codebase comments.

The pattern in use is:

```
static SkyServer  ──delegates──>  MountInstance._defaultInstance
SkySettings (static facade) ──delegates──> SkySettingsInstance
Telescope.cs (ASCOM driver)  ──calls──> static SkyServer / SkySettings
```

The **critical remaining work** is:

0. **[Phase 0 — Prerequisite]** Migrate `SkyQueue` and `MountQueue` from static singletons to
   per-device instances owned by `MountInstance`. The queue subsystem is a foundational dependency
   of all SkyServer migration work — command dispatch, step-update notifications, and hardware
   serial access all route through it. See §4 Phase 0 for detailed sub-steps and risk assessment.
1. Migrate the ~15 purely-static state clusters in `SkyServer.*` to `MountInstance`.
2. Move the core per-tick pipeline (`Steps` setter, timers, event handlers) to instance scope.
3. Make `Telescope.cs` resolve its `MountInstance` by `_deviceNumber` instead of going through the static
   `SkyServer`/`SkySettings` facade.
4. Remove, or heavily reduce, the static `SkyServer` surface once all consumers use instances.

---

## 2. Current Architecture (as-is)

### 2.1 Files & sizes

| File | Lines | Role |
|------|------:|------|
| `SkyServer.cs` | 1 443 | Static partial — PEC system, axis state, misc fields |
| `SkyServer.Core.cs` | 1 828 | Static partial — constructor, timers, event handlers, core ops |
| `SkyServer.TelescopeAPI.cs` | 3 348 | Static partial — ASCOM telescope API surface |
| `MountInstance.cs` | 1 853 | Instance class — destination for migrated state |
| `MountInstanceRegistry.cs` | 155 | Thread-safe dictionary of `MountInstance` objects |
| `SkySettings.cs` | ~400 | Static facade — all properties delegate to `SkySettingsInstance` |
| `SkySettingsInstance.cs` | ~800 | Fully instance-based settings (✅ done) |
| `Telescope.cs` | ~700+ | ASCOM driver — still calls static `SkyServer` / `SkySettings` |
| `UnifiedDeviceRegistry.cs` | ~170 | Facade over `DeviceManager` + `MountInstanceRegistry` |

### 2.2 What is already instance-based (completed phases)

| Area | Where | Notes |
|------|-------|-------|
| All 134 settings properties | `SkySettingsInstance` | Static `SkySettings` facade delegates here |
| Slew speeds (×8) | `MountInstance._slewSpeed*` | Static `SkyServer.SlewSpeedN` delegates |
| Tracking state & mode | `MountInstance._tracking`, `_trackingMode` | Static `SkyServer.Tracking` delegates |
| Guide rates (Ra/Dec) | `MountInstance._guideRate` | Static delegates in `TelescopeAPI` |
| Target RA/Dec | `MountInstance._targetRaDec` | Static delegates in `TelescopeAPI` |
| Rate offsets (Ra/Dec) | `MountInstance._rateRaDec` | Static delegates in `TelescopeAPI` |
| Position coords | `MountInstance._raDec`, `_altAzm`, `_rightAscensionXForm` etc. | Static delegates in `TelescopeAPI` |
| IsSideOfPier | `MountInstance._isSideOfPier` | Static delegate |
| SiderealTime, LHA | `MountInstance._siderealTime`, `_lha` | Static delegates |
| AppAxes, HomeAxes, AltAzSync | `MountInstance._appAxes`, `_homeAxes`, `_altAzSync` | Static read-only delegates |
| SkyWatcher rate vectors | `MountInstance._skyHcRate`, `_skyTrackingRate` | internal delegates |
| Factor / worm / step arrays | `MountInstance._factorStep`, `_stepsPerRevolution`, `_stepsWormPerRevolution` | Instance fields added |
| PEC bin fields | `MountInstance._wormTeethCount`, `_pecBinSteps` | Instance fields added |
| Mount capabilities | `MountInstance._canPPec`, `_canHomeSensor` etc. | Instance fields added |
| AtPark | `MountInstance._atPark` | Instance field added |
| Timer references | `MountInstance._mediaTimer`, `_altAzTrackingTimer` | Instance fields added |

---

## 3. Remaining Static State (what still needs migrating)

### 3.1 SkyServer.cs — pure static fields NOT yet delegating to `MountInstance`

| Field / Property | Type | Notes |
|-----------------|------|-------|
| `_actualAxisX`, `_actualAxisY` | `double` | UI diagnostics — still static backing fields |
| `_appAxisX`, `_appAxisY` | `double` | App-coordinate cache — still static |
| `_isHome` | `bool` | UI indicator |
| `_lowVoltageEventState` | `bool` | Error state |
| `_mountError` | `Exception` | Error propagation to UI |
| `_monitorPulse` | `bool` | Monitor toggle |
| `_moveAxisActive` | `bool` | MoveAxis active flag |
| `_rateMoveAxes` | `Vector` | Both axis move rates |
| `_slewSettleTime` | `double` | Settle time |
| `_snapPort1Result`, `_snapPort2Result` | `bool` | Hardware snap-port state |
| `_autoHomeProgressBar` | `int` | UI progress |
| `_autoHomeStop` | `bool` | User cancel flag |
| `_isAutoHomeRunning` | `bool` | AutoHome state |
| `_canHomeSensor`, `_canPPec`, `_canPolarLed`, `_canAdvancedCmdSupport` | `bool` | Mount caps (already in instance, see §2.2, but static copies survive) |
| `_mountRunning` | `bool` | Mount running state |
| `FactorStep[]` (static) | `double[]` | Noted "backward compat" — duplicate of instance field |
| `StepsPerRevolution[]` (static) | `long[]` | Noted "backward compat" — duplicate of instance field |
| `StepsWormPerRevolution[]` (static) | `double[]` | Noted "backward compat" — duplicate of instance field |
| `WormTeethCount[]` (static) | `int[]` | Noted "backward compat" — duplicate of instance field |
| `LoopCounter` | `ulong` | Per-device tick count |
| `TimerOverruns` | `int` | Per-device diagnostic |
| `AltAzTrackingMode` | `AltAzTrackingType` | Per-device tracking mode |

### 3.2 SkyServer.cs — PEC system (entirely static, not yet migrated)

| Item | Notes |
|------|-------|
| `Pec360Master` | `SortedList` — PEC data for 360-mode |
| `PecWormMaster` | `SortedList` — PEC worm data |
| `PecBinsSubs` | Cache subset |
| `PecBinNow` | Current bin tuple |
| `PecBinCount`, `PecBinSteps` | Worm parameters |
| `PecOn`, `PPecOn`, `PecTraining`, `PecTrainInProgress`, `PecShow` | State properties |
| `LoadPecFile()`, `CleanUpBins()`, `MakeWormMaster()`, `UpdateWormMaster()` | File I/O + data methods |
| `PecCheck()`, `CheckPecTraining()` | Timer-tick PEC logic (called from `Steps` setter) |

All of this must move into `MountInstance` and be driven from instance-owned timer events.

### 3.3 SkyServer.Core.cs — static timer & event infrastructure

| Item | Notes |
|------|-------|
| `static TimerLock` object | Lock object for timer re-entrancy — must become instance-owned |
| `static _mediaTimer` | MediaTimer — already in instance; static copy also exists |
| `static _altAzTrackingTimer` | AltAz timer — same dual-existence problem |
| `static _altAzTrackingLock` | Int32 lock — must become instance |
| `UpdateServerEvent()` (static) | **Core per-tick callback** — calls Steps setter, PEC, limit checks |
| `PropertyChangedSkySettings()` (static) | Reacts to shared settings changes — needs per-instance version |
| `PropertyChangedSkyQueue()` (static) | Receives step updates from SkyQueue |
| `PropertyChangedMountQueue()` (static) | Receives step updates from MountQueue |
| `LowVoltageEventSet()` (static) | Hardware event handler |
| `static lastUpdateStepsTime`, `lastUpdateLock` | Throttle for UpdateSteps — already in instance, static copies remain |

### 3.4 SkyServer.Core.cs — static core operations with inline logic

| Method | Issue |
|--------|-------|
| `GetDefaultPositions_Internal()` | Directly accesses `_settings!.XxxAxes` — no instance routing |
| `GetLocalSiderealTime()` | Directly accesses `_settings!.Longitude` — no instance routing |
| `SkyErrorHandler()` | Sets static `IsMountRunning`, `MountError` |

### 3.5 SkyServer.TelescopeAPI.cs — static fields with no instance equivalent yet

| Field / Property | Notes |
|-----------------|-------|
| `_isSlewing` | Backing field — partly overridden by `SlewController` |
| `_flipOnNextGoto` | Per-slew UI flag |
| `_slewState` | SlewType enum |
| `_lastAutoHomeError` | Error from async AutoHome |
| `_ctsGoTo`, `_ctsPulseGuideRa`, `_ctsPulseGuideDec`, `_ctsHcPulseGuide` | **Cancellation tokens** — class-level; multiple instances share/collide |
| `_goToAsyncLock` | Static object lock |
| `_slewController` | SlewController — static, not per-instance |
| `_isPulseGuidingRa`, `_isPulseGuidingDec` | Static backing fields (also in `SkyServer.cs`) |
| `SkyTrackingOffset[]` | Static array |
| `IsMountRunning` | Static property wrapping `_mountRunning` |

### 3.6 Telescope.cs — still uses static API

Every method in `Telescope.cs` does one of:

```csharp
SkyServer.Altitude      // static SkyServer property
SkySettings.CanAltAz    // static SkySettings facade
SkySystem.Connected     // static SkySystem
```

The `_deviceNumber` field and `GetInstance()` helper exist but are only used in ~2 places.
All the `Can*`, position, and state properties bypass the device-number routing.

---

### 3.7 Queue subsystem (`SkyQueue` / `MountQueue`) — static singletons (B1, elevated to Phase 0)

**This is the highest-priority blocker, promoted from B1 to a standalone prerequisite phase.**

The queue infrastructure has two distinct layers:

| Layer | File | Status | Action needed |
|-------|------|--------|---------------|
| `CommandQueueBase<TExecutor>` | `Mount.Commands` | ✅ Already instance-based | None — do not modify |
| `ICommandQueue<TExecutor>` | `Mount.Commands` | ✅ Interface only | None |
| `CommandBase<TExecutor>` | `Mount.Commands` | ✅ Accepts `ICommandQueue` via constructor | None — injection point already exists |
| `SkyQueueImplementation` | `Mount.SkyWatcher` | ✅ Instance class | Promote to `MountInstance` ownership |
| `MountQueueImplementation` | `Simulator` | ✅ Instance class | Promote to `MountInstance` ownership |
| **`public static class SkyQueue`** | `Mount.SkyWatcher` | ❌ Static singleton | Convert to delegating facade then remove |
| **`public static class MountQueue`** | `Simulator` | ❌ Static singleton | Convert to delegating facade then remove |
| **`SkyCommandBase` / `SkyQueryCommand` / `SkyActionCommand`** | `Mount.SkyWatcher` | ❌ Hardcode `SkyQueue.Instance` | Change constructors to accept `ICommandQueue<SkyWatcher>` |
| **`MountCommandBase` / `MountQueryCommand` / `MountActionCommand`** | `Simulator` | ❌ Hardcode `MountQueue.Instance` | Change constructors to accept `ICommandQueue<Actions>` |

**The good news:** `CommandQueueBase<TExecutor>` requires zero changes. `CommandBase<TExecutor>` already accepts `ICommandQueue<TExecutor>` in its constructor — the injection mechanism exists at the base level. Only the intermediate abstract command base classes and the two static wrapper classes need changing.

#### Two coupling channels that must both be broken

**Forward channel (SkyServer → hardware):** Call sites create commands using `SkyQueue.NewId` / `MountQueue.NewId`, dispatch via command base class constructors that hardcode `SkyQueue.Instance`, and retrieve results via `SkyQueue.GetCommandResult()`. The blast radius is **97 concrete command classes** (76 SkyWatcher + 21 Simulator) plus ~127 call sites that reference `SkyQueue.*` / `MountQueue.*` directly.

**Backward channel (hardware → SkyServer):** The hardware executor (`SkyWatcher.cs`) and simulator executor (`Actions.cs`) report position and pulse-guide state by writing to static properties: `SkyQueue.Steps = a`, `MountQueue.IsPulseGuidingRa = true`, etc. These writes fire `StaticPropertyChanged`, which `SkyServer.Core.cs` receives via `PropertyChangedSkyQueue()` / `PropertyChangedMountQueue()` and uses to drive the coordinate pipeline. This notification path must be replaced with instance-level callbacks before the static queue classes can be removed.

**Additional static coupling — serial port access:**

`GreenSwamp.Alpaca.Mount.SkyWatcher\Commands.cs` (hardware protocol driver) references the static queue for serial port access:
```csharp
// Line 72 — not just the queue but the ISerialPort inside it:
internal bool IsConnected => SkyQueue.Serial.IsOpen && MountConnected;
```
This requires passing `ISerialPort` into the `Commands` constructor directly rather than pulling it from the static class.

#### Full call-site blast radius

| File | Project | Queue refs | Role |
|------|---------|-----------|------|
| `SkyServer.Core.cs` | MountControl | 65 | Creates commands; subscribes to static events |
| `MountInstance.cs` | MountControl | 49 | Creates commands (partially migrated call sites) |
| `Commands.cs` | Mount.SkyWatcher | 16 | Hardware driver; `SkyQueue.Serial`, `NewId`, `GetCommandResult` |
| `SkyWatcher.cs` | Mount.SkyWatcher | 6 | Executor write-back: sets `SkyQueue.Steps/IsPulseGuiding*` |
| `AutohomeSky.cs` | MountControl | 8 | Creates SkyWatcher commands directly |
| `AutohomeSim.cs` | MountControl | 5 | Creates Simulator commands directly |
| `SkyCommands.cs` | Mount.SkyWatcher | 3 | Abstract base constructors hardcode `SkyQueue.Instance` |
| `Actions.cs` | Simulator | 9 | Executor write-back: sets `MountQueue.Steps/IsPulseGuiding*` |
| `Commands.cs` | Simulator | 3 | Abstract base constructors hardcode `MountQueue.Instance` |
| `SkyServer.cs` | MountControl | 4 | Reads `IsRunning`; creates 1 diagnostic command |
| `IOSerial.cs` | Simulator | 8 | **Commented-out code only** — no active references |

---

## 4. Migration Steps — Recommended Sequence

> **Rule:** Build must be green before and after each step.  
> Each step below is independently buildable/testable.

---

### Phase 0 — Migrate `SkyQueue` / `MountQueue` to Instance Scope

> **This entire phase must be completed and tested before beginning Step 1.**  
> Steps Q1–Q5 below are each independently buildable.

#### Architecture decision: instance-per-device vs. device-keyed dispatch

| Approach | Description | Pros | Cons |
|---------|-------------|------|------|
| **A: Instance-per-device** ✅ Recommended | Each `MountInstance` creates and owns a `SkyQueueImplementation` / `MountQueueImplementation` | Clean multi-device isolation; no shared state | High surface-area refactoring (97 command classes) |
| **B: Device-keyed dispatch** | Keep static singletons; tag commands with `DeviceNumber`; dispatch results to correct instance | Smaller blast radius; backward compatible during transition | Adds indirection; does not truly isolate devices; deferred technical debt |

**Recommendation: Approach A.** The base infrastructure (`CommandQueueBase`, `ICommandQueue`, `CommandBase`) is already fully instance-capable. The command class changes are mechanical and compiler-guided.

---

#### Step Q1 — Decouple executor write-back from static queue properties

**Files:** `SkyQueue.cs` (`SkyQueueImplementation`), `MountQueue.cs` (`MountQueueImplementation`), `SkyWatcher.cs`, `Actions.cs`

The executor classes (`SkyWatcher` and `Actions`) currently report position and pulse-guide results by writing to static properties (`SkyQueue.Steps = a`, `MountQueue.IsPulseGuidingRa = true`, etc.). These writes fire `StaticPropertyChanged` and are how the coordinate pipeline receives data. This coupling must be replaced with instance-level callbacks before the static classes can be removed.

**Plan:**

1. Add instance-level events to `SkyQueueImplementation` and `MountQueueImplementation`:
   ```csharp
   public event Action<double[]>? StepsUpdated;
   public event Action<bool>? PulseGuideRaUpdated;
   public event Action<bool>? PulseGuideDecUpdated;
   ```
2. Override `InitializeExecutor()` to inject the event-raise delegates into the executor at creation time:
   ```csharp
   protected override void InitializeExecutor(SkyWatcher executor)
   {
       executor.SetStepsCallback(steps => StepsUpdated?.Invoke(steps));
       executor.SetPulseGuideRaCallback(v => PulseGuideRaUpdated?.Invoke(v));
       executor.SetPulseGuideDecCallback(v => PulseGuideDecUpdated?.Invoke(v));
       if (_lowVoltageEventHandler != null)
           executor.LowVoltageEvent += _lowVoltageEventHandler;
   }
   ```
3. In `SkyWatcher.cs` / `Actions.cs`, replace static writes with callback invocations:
   ```csharp
   // Before:  SkyQueue.Steps = a;
   // After:   _stepsCallback?.Invoke(a);
   ```
4. Retain the static `SkyQueue.Steps` / `MountQueue.Steps` property setters as a **temporary backward-compat bridge** (forwarding through the implementation instance) until Step Q4 removes the static subscription from `SkyServer.Core.cs`.

**Risk:** Medium. This is the data path that drives the coordinate pipeline. A regression silently breaks position tracking. Write the `WhenStepsUpdatedThenMountInstanceReceivesCallback` test (see Phase 0 test table) before making this change and verify it passes before removing the static fallback path.

---

#### Step Q2 — Move queue instances to `MountInstance`

**Files:** `MountInstance.cs`, `SkyQueue.cs`, `MountQueue.cs`

The static `_instance` fields in `SkyQueue` and `MountQueue` must become instance fields on `MountInstance`. This step is **additive only** — the static singletons continue to exist and function until Step Q5.

**Plan:**

1. Add queue fields to `MountInstance`:
   ```csharp
   internal SkyQueueImplementation? SkyQueue { get; private set; }     // SkyWatcher mount
   internal MountQueueImplementation? MountQueue { get; private set; } // Simulator
   ```
2. In `MountInstance.MountStart()` / `Connect()`, create, subscribe, and start the appropriate queue:
   ```csharp
   // SkyWatcher path:
   SkyQueue = new SkyQueueImplementation();
   SkyQueue.StepsUpdated += OnSkyStepsUpdated;
   SkyQueue.PulseGuideRaUpdated += v => IsPulseGuidingRa = v;
   SkyQueue.PulseGuideDecUpdated += v => IsPulseGuidingDec = v;
   SkyQueue.Start(_serial, _custom360Steps, _customWormSteps, LowVoltageEventSet);
   ```
3. In `MountInstance.MountStop()`, stop and null the reference:
   ```csharp
   SkyQueue?.Stop();
   SkyQueue = null;
   ```
4. Verify that two `MountInstance` objects created in the test project hold independent, non-null queue references after `MountStart()`.

**Risk:** Low — additive change. Build green before and after.

---

#### Step Q3 — Decouple command base classes from static singletons

**Files:** `SkyCommands.cs` (3 abstract + 76 concrete), `Simulator/Commands.cs` (3 abstract + 21 concrete)

This is the **highest surface-area change in Phase 0**: 97 concrete command classes inherit from base classes that hardcode the static queue singleton. The change per class is mechanical — thread the `ICommandQueue<T>` parameter down from the abstract base.

**Current (hardcoded):**
```csharp
protected SkyActionCommand(long id) : base(id, SkyQueue.Instance) { }

public class SkyAxisSlew : SkyActionCommand
{
    public SkyAxisSlew(long id, Axis axis, long steps) : base(id) { }
}
```

**Target (injected):**
```csharp
protected SkyActionCommand(long id, ICommandQueue<SkyWatcher> queue) : base(id, queue) { }

public class SkyAxisSlew : SkyActionCommand
{
    public SkyAxisSlew(long id, ICommandQueue<SkyWatcher> queue, Axis axis, long steps) : base(id, queue) { }
}
```

**Transition strategy:** Add an `[Obsolete]` overload retaining `SkyQueue.Instance` as the default so call sites that have not yet been updated continue to compile. Remove these obsolete overloads in Step Q4 once all call sites are migrated.

```csharp
// Temporary backward-compat overload:
[Obsolete("Pass queue explicitly; static SkyQueue will be removed in Phase 5.x")]
protected SkyActionCommand(long id) : base(id, SkyQueue.Instance) { }
```

**Risk:** High surface area, low per-class risk. Change one abstract base at a time; the compiler will enumerate all affected concrete classes immediately. The obsolete overloads ensure the build stays green throughout.

---

#### Step Q4 — Route all command call sites through `MountInstance` queue

**Files:** `SkyServer.Core.cs` (65 refs), `MountInstance.cs` (49 refs), `AutohomeSky.cs` (8 refs), `AutohomeSim.cs` (5 refs), `SkyServer.cs` (4 refs)

With queue instances owned by `MountInstance` (Q2) and command base classes accepting the queue (Q3), all remaining call sites can now be routed through the instance.

**Plan:**

1. **`MountInstance.cs` call sites** — already partially migrated; change `SkyQueue.NewId` → `this.SkyQueue!.NewId`, `SkyQueue.GetCommandResult(cmd)` → `this.SkyQueue!.GetCommandResult(cmd)`, etc.

2. **`SkyServer.Core.cs` and `SkyServer.cs`** (static methods) — add temporary static delegation helpers to maintain backward compat while this file is being worked through:
   ```csharp
   // Temporary helpers in SkyServer (remove when SkyServer.Core is instance-based in Step 6):
   private static long SkyQueueNewId => _defaultInstance?.SkyQueue?.NewId ?? 0;
   ```

3. **`AutohomeSky.cs` / `AutohomeSim.cs`** — these classes need access to the device-specific queue. Pass the owning `MountInstance` (or its queue directly) as a constructor parameter. Both classes are currently created inside `SkyServer.Core.cs` methods.

4. **Remove static event subscription** from `SkyServer.Core.cs`:
   ```csharp
   // Remove (in static constructor):
   SkyQueue.StaticPropertyChanged += PropertyChangedSkyQueue;
   MountQueue.StaticPropertyChanged += PropertyChangedMountQueue;
   // The per-instance StepsUpdated callback registered in Q2 already handles this.
   ```

5. Remove the `[Obsolete]` backward-compat overloads added in Q3.

**Risk:** Medium. The `AutoHome` classes require constructor injection which must propagate from their creation sites inside `SkyServer.Core.cs`. Do not proceed until Q2 and Q3 are fully complete and the build is green.

---

#### Step Q5 — Fix serial port reference in `Commands.cs` (SkyWatcher hardware driver)

**Files:** `GreenSwamp.Alpaca.Mount.SkyWatcher\Commands.cs`, `SkyQueue.cs` (`SkyQueueImplementation`)

The `Commands` class (SkyWatcher hardware protocol driver) references the static queue for the serial port:

```csharp
// Before (line 72):
internal bool IsConnected => SkyQueue.Serial.IsOpen && MountConnected;
```

`SkyQueueImplementation.Start()` already receives `ISerialPort` from `MountInstance`. Forward it into the `Commands` / `SkyWatcher` executor at construction time:

```csharp
// After — Commands.cs constructor:
private readonly ISerialPort _serial;
internal Commands(ISerialPort serial) { _serial = serial; }
internal bool IsConnected => _serial?.IsOpen == true && MountConnected;
```

`SkyQueueImplementation.CreateExecutor()` creates the `SkyWatcher` executor; pass `_serial` there.

**Risk:** Low — contained two-file change. Build and verify `IsConnected` behaviour before and after.

---

#### Phase 0 Risk Summary

| Step | Surface Area | Risk Level | Key Concern |
|------|-------------|------------|-------------|
| Q1 — Executor write-back | `SkyWatcher.cs`, `Actions.cs`, 2 queue impl files | Medium | Coordinate pipeline data regression |
| Q2 — Queue instances to `MountInstance` | `MountInstance.cs`, 2 queue files | Low | Additive only; static bridge maintained |
| Q3 — Decouple command base classes | 97 concrete + 6 abstract command classes | High surface / low per-class risk | Mechanical; compiler guides completion; obsolete overloads keep build green |
| Q4 — Route call sites through instance | ~127 call sites across 5 files | Medium | `AutoHome` classes need constructor injection; static event subscription removal |
| Q5 — Fix serial port in `Commands.cs` | 1 class, 1 file | Low | Contained change |

#### Phase 0 Test Requirements

Create a `GreenSwamp.Alpaca.MountControl.Tests` xUnit project **before** executing Step Q1. Add these tests:

| Test | Validates | Before step |
|------|-----------|-------------|
| `WhenSimulatorConnectedThenMountInstanceOwnsQueue` | `inst.MountQueue != null` after `MountStart()` | Q2 |
| `WhenTwoDevicesRegisteredThenQueuesAreIndependent` | `device0.MountQueue != device1.MountQueue` (reference inequality) | Q2 |
| `WhenStepsUpdatedThenMountInstanceReceivesCallback` | Executor write-back reaches `MountInstance`; static `SkyServer.Steps` is NOT updated | Q1 |
| `WhenCommandSentToDevice0ThenDevice1QueueUnaffected` | End-to-end device isolation after Q3+Q4 | Q4 |
| `WhenQueueStoppedThenGetCommandResultReturnsFailure` | `CommandQueueBase.GetCommandResult()` when `IsRunning == false` | Q2 |

---

### Step 1 — Consolidate duplicate static arrays into instance delegation

**Files:** `SkyServer.cs`

`FactorStep[]`, `StepsPerRevolution[]`, `StepsWormPerRevolution[]`, `WormTeethCount[]` are
annotated "static for backward compatibility only" yet instance fields already exist on
`MountInstance`.  Replace each static property getter/setter with a delegation to `_defaultInstance`
— exactly as was done for `SlewSpeed*` in Phase 4.2.

```csharp
// Before (static array):
public static long[] StepsPerRevolution { get; private set; } = { 0, 0 };

// After (delegating):
public static long[] StepsPerRevolution
{
    get => _defaultInstance?._stepsPerRevolution ?? new long[] { 0, 0 };
    set { if (_defaultInstance != null) _defaultInstance._stepsPerRevolution = value; }
}
```

**Risk:** Low — same pattern already used many times.

---

### Step 2 — Migrate remaining flat state fields from `SkyServer.cs` to `MountInstance`

**Files:** `SkyServer.cs`, `MountInstance.cs`

Move the following to `MountInstance` backing fields with delegating wrappers in `SkyServer`:

- `_actualAxisX/Y` → `MountInstance.ActualAxisX/Y`
- `_appAxisX/Y` → already have `AppAxisX/Y` in instance; remove static backing fields and route through instance
- `_isHome` → `MountInstance.IsHome`
- `_lowVoltageEventState` → `MountInstance.LowVoltageEventState`
- `_mountError` → `MountInstance.MountError`
- `_monitorPulse` → `MountInstance.MonitorPulse`
- `_rateMoveAxes` → `MountInstance.RateMoveAxes` (Vector)
- `_slewSettleTime` → `MountInstance.SlewSettleTime`
- `_autoHomeProgressBar`, `_autoHomeStop`, `_isAutoHomeRunning` → `MountInstance`
- `_isPulseGuidingRa/Dec` → `MountInstance`
- `_isSlewing`, `_flipOnNextGoto`, `_slewState`, `_lastAutoHomeError` → `MountInstance`
- `LoopCounter`, `TimerOverruns`, `AltAzTrackingMode` → `MountInstance`
- `IsMountRunning` → `MountInstance.IsMountRunning` (already a read-only property there; expose setter)

**Risk:** Medium — many call sites within SkyServer methods. Cross-reference each field before
removing to avoid accidental loss of `OnStaticPropertyChanged()` raises.

---

### Step 3 — Migrate PEC system to `MountInstance`

**Files:** `SkyServer.cs`, `MountInstance.cs`

The entire PEC subsystem (see §3.2) is self-contained but large (~500 lines).  

Approach:

1. Create a new `PecState` record or inner struct on `MountInstance` (or a separate
   `MountInstancePec.cs` partial class) that holds all PEC fields.
2. Move `LoadPecFile()`, `CleanUpBins()`, `MakeWormMaster()`, `UpdateWormMaster()` as public
   methods on `MountInstance`.
3. Move `PecCheck()` and `CheckPecTraining()` to instance methods (called from instance timer).
4. Provide thin static wrappers in `SkyServer.cs` that delegate to `_defaultInstance` for any
   external call sites (Blazor UI pages etc.).

**Risk:** Medium — PEC interacts with `_settings`, `Steps`, and timer events.  
**Blocker prerequisite:** Step 1 (worm teeth/steps arrays must already be on instance).

---

### Step 4 — Move CancellationTokenSources to `MountInstance`

**Files:** `SkyServer.TelescopeAPI.cs`, `MountInstance.cs`

`_ctsGoTo`, `_ctsPulseGuideRa`, `_ctsPulseGuideDec`, `_ctsHcPulseGuide` are currently static and
**shared across all device instances**.  This is a correctness bug — cancelling a GoTo on device 0
also cancels device 1.

Move all four CTS fields to `MountInstance`.  Provide static delegating accessors in `SkyServer`
that route through `_defaultInstance` for backward compatibility.

```csharp
// SkyServer — delegate CTS through default instance
private static CancellationTokenSource? _ctsGoTo
{
    get => _defaultInstance?._ctsGoTo;
    set { if (_defaultInstance != null) _defaultInstance._ctsGoTo = value; }
}
```

**Risk:** Medium — cancellation flow is critical to slew correctness.  
Run all slew/abort tests after this step.

---

### Step 5 — Move `SlewController` to `MountInstance`

**Files:** `SkyServer.TelescopeAPI.cs`, `MountInstance.cs`

`_slewController` is a static field on `SkyServer`.  It holds the only live slew state for the
current slew operation.  With multiple devices, each needs its own `SlewController`.

1. Add `private SlewController? _slewController` to `MountInstance`.
2. Move `EnsureSlewController()`, `SlewAsync()`, `SlewSync()`, `WaitForSlewCompletionAsync()` to
   instance methods.
3. Provide static delegating wrappers in `SkyServer` using `_defaultInstance`.

**Risk:** Medium — affects `SlewRaDec`, `SlewAltAz`, `SlewAxes`, `SideOfPier` setter.

---

### Step 6 — Migrate timer infrastructure and `UpdateServerEvent` to `MountInstance`

**Files:** `SkyServer.Core.cs`, `MountInstance.cs`

This is the most complex step.  `UpdateServerEvent` is the core per-tick loop that:

- Reads sidereal time
- Calls `UpdateSteps()` (fetches hardware step counts)
- Updates RA/Dec/Alt/Az
- Checks slew state, axis limits, PEC training
- Updates `IsSideOfPier`, `IsHome`

**Plan:**

1. The instance-level `MountStart()`/`MountStop()` methods (already in `MountInstance`) should
   create/destroy `_mediaTimer` and `_altAzTrackingTimer` using instance methods, not the static
   ones on `SkyServer`.
2. Move `UpdateServerEvent` logic into `MountInstance.OnTimerTick()`.
3. Move `PropertyChangedSkyQueue` / `PropertyChangedMountQueue` subscriptions into
   `MountInstance`'s constructor so each instance subscribes its own handler.
4. Move `LowVoltageEventSet` handler to instance.
5. Move `TimerLock`, `_altAzTrackingLock` to `MountInstance`.

**Blocker prerequisite:** Steps 2, 3, 4, 5 must be complete (all state fields must be on instance
before the tick loop can operate on instance state).

**Risk:** High — this is the hot path. Regression risk is significant.  
Add integration tests for the tick loop before making this change.

---

### Step 7 — Migrate `Steps` setter coordinate pipeline

**Files:** `SkyServer.cs`

The `Steps` property setter is the central coordinate-conversion pipeline. Every hardware tick
goes through:

```
raw steps → convert to degrees → PEC check → mount→app axes transform
         → ActualAxisX/Y → AppAxisX/Y → Alt/Az → RA/Dec → XForm RA/Dec
```

After Steps 2, 3, 6 are done, this setter should be converted to an instance method
`MountInstance.SetSteps(double[] steps)` that operates entirely on instance state.

The static `SkyServer.Steps` setter becomes:

```csharp
public static double[] Steps
{
    get => _steps;
    set
    {
        _steps = value;
        _defaultInstance?.SetSteps(value);
    }
}
```

**Risk:** High — affects every position update in the system.

---

### Step 8 — Route `Telescope.cs` through `MountInstance` for all operations

**Files:** `Telescope.cs`

Currently `Telescope.cs` calls:
- `SkyServer.Altitude`, `SkyServer.Tracking`, etc. — all static
- `SkySettings.CanAltAz`, `SkySettings.AlignmentMode`, etc. — all static facade
- `SkySystem.Connected` — static

The device driver already stores `_deviceNumber` and has `GetInstance()`.  
After all state is on instances, the driver should read everything through its instance:

```csharp
// Before:
public double Altitude
{
    get
    {
        CheckCapability(SkySettings.CanAltAz, "Altitude", false);
        return SkyServer.Altitude;
    }
}

// After:
public double Altitude
{
    get
    {
        var inst = GetInstance();
        CheckCapability(inst.Settings.CanAltAz, "Altitude", false);
        return inst.Altitude;
    }
}
```

**Prerequisite:** All state fields and action methods must be accessible on `MountInstance`.  
This step should be done file-by-file through `Telescope.cs`.

**Risk:** Low per-property, but the file is ~700+ lines of properties and methods.  
Split into sub-tasks:
- 8a: `Can*` capabilities → `inst.Settings.*`
- 8b: Position properties → `inst.Altitude/Azimuth/RightAscension/Declination/etc.`
- 8c: State properties → `inst.AtPark/AtHome/IsSlewing/Tracking/etc.`
- 8d: Action methods → `inst.SlewRaDec/GoTo/Park/FindHome/etc.`
- 8e: `Connected`/`Connecting` → requires `SkySystem` refactor (see §5)

---

### Step 9 — Remove `_defaultInstance` bridge from `SkyServer`

After Step 8 is complete, the static `SkyServer` surface is no longer called by `Telescope.cs`.  
The remaining callers of static `SkyServer` are:

- Blazor UI components (pages/services in `GreenSwamp.Alpaca.Server`)
- `SkySystem` (connection management)
- `SlewController` (which is now on the instance)

For the UI, either:
- Keep static `SkyServer` as an application-level singleton wrapper around device 0 for UI
  purposes (pragmatic approach); **or**
- Inject `MountInstance` into Blazor pages via `MountInstanceRegistry` (full DI approach).

**Recommendation:** Keep the static facade for UI use for now; focus complete removal for the
multi-instance ASCOM path first.

---

### Step 10 — Remove static settings arrays and `SkySettings` facade (long term)

Once `Telescope.cs` reads from `inst.Settings` directly, `SkySettings` static facade serves only
Blazor UI pages.  Consider:

1. Inject `SkySettingsInstance` into Blazor pages via DI.
2. Remove `SkySettings.Initialize()` call and the static class.

**Risk:** Low after Step 8 — it is a purely cosmetic/maintenance improvement.

---

## 5. Key Blockers

| # | Blocker | Impact | Notes |
|---|---------|--------|-------|
| B1 | `SkyQueue` and `MountQueue` are static | **Phase 0** (prerequisite to all steps) | Elevated to a standalone prerequisite phase. See §3.7 for full analysis and §4 Phase 0 for sub-steps Q1–Q5. Two-way coupling: forward (97 command classes hardcode the static singleton) and backward (executor write-back via `Steps`/`IsPulseGuiding*` fires `StaticPropertyChanged`). `SkyQueue.Serial` is also referenced directly by the hardware driver. |
| B2 | `SkySystem` is static | Step 8e | Connection counting (`SetConnected`, `Connected`, `Connecting`) is a shared reference counter. Needs a per-device connection model. |
| B3 | `SkyPredictor` is static | Step 6 | Alt/Az predictor is a global singleton. Must become per-instance. |
| B4 | `MonitorLog` / `MonitorEntry` call sites use `MethodBase.GetCurrentMethod()?.Name` | All steps | No functional blocker, but high noise in logs if multiple instances share the same method names. |
| B5 | `StaticPropertyChanged` event on `SkyServer` | Step 6, UI | Blazor UI subscribes to this static event. Migrating to per-instance events requires re-wiring all UI subscriptions. Consider `INotifyPropertyChanged` on `MountInstance` and a bridge in `TelescopeStateService`. |
| B6 | `AutoHomeAsync` — `async void` method | Step 6 | This is fire-and-forget and swallows results. Should be converted to `async Task` before being moved to instance. |
| B7 | Blazor pages access `SkyServer.*` directly | Step 9 | Need an inventory of all Blazor `.razor` / `.razor.cs` files that reference `SkyServer.*` before removing the static surface. |
| B8 | Duplicate state arrays (§3.1 static + §2.2 instance) | Steps 1, 2 | Both copies exist right now. The static copies are not kept in sync with the instance copies. This is a latent data-coherency bug. Highest priority to fix. |

---

## 6. Test Coverage Recommendations

Create a `GreenSwamp.Alpaca.MountControl.Tests` xUnit project. The project currently has no unit tests.

**Phase 0 tests (required before Q1 — see also Phase 0 test table in §4):**

| Area | Recommended Test |
|------|-----------------|
| Queue lifecycle | `WhenSimulatorConnectedThenMountInstanceOwnsQueue` |
| Queue isolation | `WhenTwoDevicesRegisteredThenQueuesAreIndependent` |
| Step callback | `WhenStepsUpdatedThenMountInstanceReceivesCallback` |
| Command isolation | `WhenCommandSentToDevice0ThenDevice1QueueUnaffected` |
| Queue stop | `WhenQueueStoppedThenGetCommandResultReturnsFailure` |

**Phase 5.x tests (required before Step 6):**

| Area | Recommended Test |
|------|-----------------|
| Timer tick / coordinate pipeline | Integration test: connect simulator, wait 3 ticks, assert RA/Dec change |
| GoTo / SlewRaDec | Assert `IsSlewing` transitions and final position within tolerance |
| Tracking on/off transitions | Assert `TrackingMode` changes propagate to queue commands |
| Pulse guiding | Assert `IsPulseGuidingRa/Dec` set/cleared correctly |
| PEC load/apply | Load a test PEC file, run tick loop, assert bin correction applied |
| Multi-instance isolation | Register 2 devices, slew device 0, assert device 1 state unchanged |
| CancellationToken isolation | Cancel GoTo on device 0, assert device 1 continues (Step 4) |

---

## 7. Recommended Phase Grouping

| Phase | Steps | Estimated Risk | Prerequisite |
|-------|-------|---------------|-------------|
| **0 — Queue migration** | Q1–Q5 | Medium–High | None — implement and test first |
| 5.1 — State consolidation | 1, 2 | Low–Medium | Phase 0 done |
| 5.2 — PEC migration | 3 | Medium | 5.1 done |
| 5.3 — Cancellation / SlewController | 4, 5 | Medium | 5.1 done |
| 5.4 — Timer / tick loop | 6, 7 | High | 5.1–5.3 done + tests added |
| 5.5 — Driver routing | 8a–8d | Medium | 5.4 done |
| 5.6 — SkySystem/connection | 8e, B2 | High | 5.5 done |
| 5.7 — Static removal | 9, 10 | Low | 5.5 done |

---

## 8. Files That Should NOT Be Edited

> The following are auto-generated or external — do not modify:

- `obj\**\*.cs` — build output
- `ASCOM.Alpaca.Razor\**` — external ASCOM library
- Any `*.g.cs` file

---

## 9. Quick-Reference: Per-Step File Touch List

| Step | Primary Files Changed |
|------|-----------------------|
| **Q1** | `SkyQueue.cs` (add instance events to `SkyQueueImplementation`), `MountQueue.cs` (add instance events to `MountQueueImplementation`), `SkyWatcher.cs` (replace static writes with callbacks), `Actions.cs` (replace static writes with callbacks) |
| **Q2** | `MountInstance.cs` (add `SkyQueue`/`MountQueue` fields, create/start/stop in `MountStart`/`MountStop`) |
| **Q3** | `SkyCommands.cs` (change 3 abstract bases + 76 concrete constructors), `Simulator/Commands.cs` (change 3 abstract bases + 21 concrete constructors) |
| **Q4** | `SkyServer.Core.cs` (remove static event subscription, update 65 call sites), `MountInstance.cs` (update 49 call sites), `AutohomeSky.cs` (inject queue), `AutohomeSim.cs` (inject queue), `SkyServer.cs` (update 4 call sites) |
| **Q5** | `SkyQueue.cs` (`SkyQueueImplementation.CreateExecutor`), `Commands.cs` SkyWatcher (add `ISerialPort` constructor param, remove `SkyQueue.Serial`) |
| 1 | `SkyServer.cs` (remove 4 static arrays, add delegating props) |
| 2 | `SkyServer.cs` (remove ~15 static fields), `MountInstance.cs` (add fields + wrappers) |
| 3 | `SkyServer.cs` (remove PEC), `MountInstance.cs` (add PEC methods), new `MountInstance.Pec.cs` optional |
| 4 | `SkyServer.TelescopeAPI.cs` (remove CTS fields), `MountInstance.cs` (add CTS fields) |
| 5 | `SkyServer.TelescopeAPI.cs` (remove SlewController), `MountInstance.cs` |
| 6 | `SkyServer.Core.cs` (remove timer/event), `MountInstance.cs` (add timer/event) |
| 7 | `SkyServer.cs` (`Steps` setter → instance call) |
| 8 | `Telescope.cs` (all property getters/setters route through instance) |
| 9 | `SkyServer.cs`, `SkyServer.Core.cs`, `SkyServer.TelescopeAPI.cs` (remove `_defaultInstance`, static surface) |
| 10 | `SkySettings.cs` (remove), `Telescope.cs` (use `inst.Settings` directly) |

---

*End of document*
