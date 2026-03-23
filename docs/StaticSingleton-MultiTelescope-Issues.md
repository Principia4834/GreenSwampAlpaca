# Static Singleton Issues Blocking Multi-Telescope Support

**Created:** 2026-03  
**Status:** Analysis only — no code changes made  
**Scope:** All static state that is shared globally across telescope instances  
**Build baseline:** ✅ Build SUCCESS — 0 errors before this analysis

---

## Executive Summary

The codebase is mid-migration from a single-telescope static architecture to a
multi-instance design. A substantial amount of static state still exists at three
layers:

| Layer | Component | Problem class |
|-------|-----------|---------------|
| Queue | `SkyQueue` (static facade) | Single `_instance` slot — second telescope registration evicts the first |
| Dispatch | `SkyServer.SkyTasks()` / `SimTasks()` | All commands use default constructors that capture `SkyQueue.Instance` |
| Event/callback | `SkyQueue.Steps`, `IsPulseGuidingRa/Dec` | Static fields; last-writer wins across instances |
| Serial | `SkySystem.Serial` | One port; second connect overwrites first |
| Position event | `SkyServer.MountPositionUpdatedEvent` | Single `ManualResetEventSlim`; two precision-gotos race |
| State | `SkyServer._defaultInstance` | The whole static facade serves only one telescope |
| Calculations | `_trackingOffsetRate`, `SkyTrackingOffset`, `_hcPrevMove*` | True static fields; no instance backing |

The **critical path** when a second telescope is added:

1. `MountInstance.MountStart()` calls `SkyQueue.RegisterInstance(sqImpl2)` — this
   **overwrites** `SkyQueue._instance`, detaching telescope 1 from its hardware.
2. Every subsequent `SkyTasks()` call (for either telescope) sends commands to
   telescope 2's hardware only.
3. The `Steps`/`IsPulseGuidingRa/Dec` callbacks from both queues write to the
   same three static fields; `PropertyChangedSkyQueue` propagates the mixed values
   into `SkyServer._defaultInstance` (telescope 1's logical state).

---

## Issue 1 — `SkyQueue` Static Singleton (CRITICAL)

**File:** `GreenSwamp.Alpaca.Mount.SkyWatcher\SkyQueue.cs`  
**Lines:** 107–239

```csharp
public static class SkyQueue
{
    private static SkyQueueImplementation _instance;          // ← single slot
    private static bool   _isPulseGuidingDec;                 // ← shared
    private static bool   _isPulseGuidingRa;                  // ← shared
    private static double[] _steps;                           // ← shared

    public static void RegisterInstance(SkyQueueImplementation impl)
    {
        _instance = impl ?? throw new ArgumentNullException(nameof(impl));
        // no previous-instance preservation
    }
}
```

**What goes wrong with two telescopes:**

- `MountInstance.MountStart()` (line 1314) calls `SkyQueue.RegisterInstance(sqImpl)`
  for each telescope. The second call silently replaces the first.
- After the second telescope starts, **all** static `SkyQueue.AddCommand()`,
  `SkyQueue.GetCommandResult()`, and `SkyQueue.NewId` calls operate on telescope 2's
  hardware, even when logically handling telescope 1's requests.
- `_steps`, `_isPulseGuidingRa`, `_isPulseGuidingDec` have no per-instance identity;
  values from the faster-polling telescope overwrite the other's data continuously.

**MountInstance.MountStart() — the registration site:**

```csharp
// MountInstance.cs lines 1309-1316
var sqImpl = new SkyQueueImplementation();
sqImpl.SetupCallbacks(
    steps => SkyQueue.Steps = steps,              // writes static field
    v    => SkyQueue.IsPulseGuidingRa = v,        // writes static field
    v    => SkyQueue.IsPulseGuidingDec = v);      // writes static field
SkyQueue.RegisterInstance(sqImpl);               // ← OVERWRITES previous
sqImpl.Start(SkySystem.Serial, ...);
SkyQueueInstance = sqImpl;                       // per-instance copy OK
```

---

## Issue 2 — All `SkyCommands` Default Constructors Capture `SkyQueue.Instance`

**File:** `GreenSwamp.Alpaca.Mount.SkyWatcher\SkyCommands.cs`  
**Lines:** 35, 44, 53 (base classes) + every command class

The three abstract base classes each provide a "convenience" constructor that
permanently binds the command to whatever `SkyQueue.Instance` returns **at construction
time**:

```csharp
protected SkyCommandBase(long id)        : base(id, SkyQueue.Instance) { }
protected SkyQueryCommand(long id)       : base(id, SkyQueue.Instance) { }
protected SkyActionCommand(long id)      : base(id, SkyQueue.Instance) { }
```

Every concrete command class (38+ classes) has a matching single-argument constructor
of this form. Every call to `SkyTasks()` uses these default constructors.

**Affected commands in `SkyTasks()` (`SkyServer.Core.cs` lines 1258–1428):**

| Command | Constructor used | Bound to |
|---------|-----------------|----------|
| `SkyAllowAdvancedCommandSet(0, …)` | `SkyAllowAdvancedCommandSet(id, on)` | `SkyQueue.Instance` |
| `SkySetAlternatingPPec(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetDecPulseToGoTo(0, …)` | ditto | `SkyQueue.Instance` |
| `SkyGetAdvancedCmdSupport(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyCanPPec(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyCanPolarLed(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyCanHomeSensors(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyGetCapabilities(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkySetEncoder(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetFullCurrent(0, …)` | ditto | `SkyQueue.Instance` |
| `SkyGetFactorStepToRad(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyLoadDefaultMountSettings(0)` | ditto | `SkyQueue.Instance` |
| `SkyAxisStopInstant(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetMinPulseDuration(0, …)` (×2) | ditto | `SkyQueue.Instance` |
| `SkySetMonitorPulse(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetPPecTrain(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetPPec(SkyQueue.NewId, …)` | ditto | `SkyQueue.Instance` |
| `SkySetPolarLedLevel(0, …)` | ditto | `SkyQueue.Instance` |
| `SkyAxisStop(0, …)` (×2) | ditto | `SkyQueue.Instance` |
| `SkySetSt4GuideRate(0, …)` | ditto | `SkyQueue.Instance` |
| `SkySetSouthernHemisphere(SkyQueue.NewId, …)` | ditto | `SkyQueue.Instance` |
| `SkySetSnapPort(SkyQueue.NewId, …)` (×2) | ditto | `SkyQueue.Instance` |
| `SkySyncAxis(0, …)` (×6) | ditto | `SkyQueue.Instance` |
| `SkyGetAxisStringVersions(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyMountType(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyMountVersion(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyGetStepsPerRevolution(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkyGetPecPeriod(SkyQueue.NewId, …)` (×2) | ditto | `SkyQueue.Instance` |
| `SkyGetStepTimeFreq(SkyQueue.NewId)` | ditto | `SkyQueue.Instance` |
| `SkySetAxisPosition(0, …)` (×2) | ditto | `SkyQueue.Instance` |

**Commands also in `AxesStopValidate()` (lines 1554-1563):**

```csharp
var statusx = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);  // SkyQueue.Instance
axis1Stopped = Convert.ToBoolean(SkyQueue.GetCommandResult(statusx).Result);
```

---

## Issue 3 — `GetRawDegrees()` and `GetRawSteps()` Use Static Queue

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 978–1001 (`GetRawDegrees`), 1031–1056 (`GetRawSteps`)

These are **instance methods** on `MountInstance` but dispatch to the static queue:

```csharp
// GetRawDegrees() — line 991
var skyPositions = new SkyGetPositionsInDegrees(SkyQueue.NewId);       // static queue
actualDegrees = (double[])SkyQueue.GetCommandResult(skyPositions).Result;

// GetRawSteps() — line 1046
var skySteps = new SkyGetSteps(SkyQueue.NewId);                         // static queue
steps = (double[])SkyQueue.GetCommandResult(skySteps).Result;
```

Note the inconsistency: the **single-axis** overload `GetRawSteps(int axis)` at
line 1072 correctly uses `SkyQueueInstance` (the per-instance field):

```csharp
var b = new SkyGetAxisPositionCounter(SkyQueueInstance!.NewId, SkyQueueInstance, Axis.Axis1);
return Convert.ToDouble(SkyQueueInstance.GetCommandResult(b).Result);  // ✅ correct
```

Only `GetRawDegrees()` and the two-axis `GetRawSteps()` were not updated.  
These methods are called from `MountConnect()` and `UpdateSteps()` — the main
position-tracking path.

---

## Issue 4 — `SkyTasks()` / `SimTasks()` Are Fully Static Dispatch Methods

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 967 (`SimTasks`), 1233 (`SkyTasks`)

Both methods live on the static `SkyServer` class and act as global dispatch for
all mount commands. They have no instance parameter. When called from
`MountInstance.MountConnect()`:

```csharp
// MountInstance.cs lines 638-661
SkyServer.SkyTasks(MountTaskName.AllowAdvancedCommandSet);
SkyServer.SkyTasks(MountTaskName.LoadDefaults);
SkyServer.SkyTasks(MountTaskName.StepsPerRevolution);
// ... 20 more calls
```

Each call reads settings from the static `SkyServer._settings` (which points to
`_defaultInstance.Settings`) and dispatches commands to `SkyQueue.Instance`.
For Telescope 2 these calls use the wrong settings and the wrong hardware.

---

## Issue 5 — `PropertyChangedSkyQueue` Propagates Mixed-Telescope Data

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 667–682

```csharp
private static void PropertyChangedSkyQueue(object sender, PropertyChangedEventArgs e)
{
    switch (e.PropertyName)
    {
        case "IsPulseGuidingRa":
            IsPulseGuidingRa = SkyQueue.IsPulseGuidingRa;  // static read → default instance
            break;
        case "IsPulseGuidingDec":
            IsPulseGuidingDec = SkyQueue.IsPulseGuidingDec;
            break;
        case "Steps":
            Steps = SkyQueue.Steps;                          // static read → default instance
            _mountPositionUpdatedEvent.Set();
            break;
    }
}
```

This handler is attached **once** at static constructor time (line 217):

```csharp
SkyQueue.StaticPropertyChanged += PropertyChangedSkyQueue;
```

When Telescope 2's hardware fires a `Steps` change, this handler runs and writes
Telescope 2's raw step counts into `SkyServer._defaultInstance._steps` (Telescope 1's
state). The single `_mountPositionUpdatedEvent` is also set, unblocking any
precision-goto that Telescope 1 may be performing.

---

## Issue 6 — `SkySystem.Serial` — Single Static Serial Port

**File:** `GreenSwamp.Alpaca.MountControl\SkySystem.cs`  
**Line:** 52

```csharp
public static ISerialPort Serial { get; private set; }
```

`SkySystem` is a static class that manages one serial port. When
`MountInstance.MountStart()` calls:

```csharp
SkySystem.ConnectSerial = false;
SkySystem.ConnectSerial = true;  // opens port for _current_ settings
// ...
sqImpl.Start(SkySystem.Serial, ...);  // passes the single static serial object
```

A second telescope on a different COM port cannot be accommodated.  
Calling `ConnectSerial = true` for Telescope 2 overwrites `SkySystem.Serial`
with Telescope 2's port object. If Telescope 1 subsequently calls `SkySystem.Serial`,
it gets Telescope 2's port.

---

## Issue 7 — `SkyServer.MountPositionUpdatedEvent` — Single Shared Event

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 61–67

```csharp
private static readonly ManualResetEventSlim _mountPositionUpdatedEvent =
    new ManualResetEventSlim(false);
```

Used in `SkyPrecisionGoto` and `SkyPulseGoto` (both in `MountInstance.cs`):

```csharp
SkyServer.MountPositionUpdatedEvent.Reset();
UpdateSteps();
if (!SkyServer.MountPositionUpdatedEvent.Wait(5000))
    throw new TimeoutException(...);
```

With two telescopes doing precision-goto simultaneously:
- Either telescope's `Steps` callback fires → `_mountPositionUpdatedEvent.Set()`
- Both waiting threads are released regardless of which telescope's data arrived
- The wrong telescope could proceed with the other's position data

---

## Issue 8 — `_trackingOffsetRate` — True Static Field

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 1117–1135

```csharp
private static Vector _trackingOffsetRate;   // ← no instance backing

public static double TrackingOffsetRaRate
{
    get => _trackingOffsetRate.X;
    private set => _trackingOffsetRate.X = value;
}
public static double TrackingOffsetDecRate
{
    get => _trackingOffsetRate.Y;
    private set => _trackingOffsetRate.Y = value;
}
```

`CalcCustomTrackingOffset()` (called from `MountConnect()` line 697) calculates
rates using `StepsTimeFreq` and `StepsPerRevolution` from whichever instance
just populated those static values, and stores the result in `_trackingOffsetRate`.
The second telescope's call overwrites the first telescope's custom rates.

---

## Issue 9 — `SkyTrackingOffset` — Static Array

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Line:** 1170

```csharp
private static readonly int[] SkyTrackingOffset = { 0, 0 };
```

Used inside `SkyGetRate()` to accumulate custom mount `:I` offset across calls.
Not per-instance; any telescope's tracking-rate calculation overwrites the shared
array.

---

## Issue 10 — HC Anti-Backlash — Static Fields

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Lines:** 105–107

```csharp
private static HcPrevMove _hcPrevMoveRa;
private static HcPrevMove _hcPrevMoveDec;
private static readonly IList<double> HcPrevMovesDec = new List<double>();
```

These fields record the previous hand-controller move direction for backlash
compensation. No instance backing exists. Hand-controller use on Telescope 2 will
corrupt the backlash state for Telescope 1.

---

## Issue 11 — `SkyServer._defaultInstance` Serves Only One Telescope

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs`  
**Line:** 68

```csharp
private static MountInstance? _defaultInstance;
```

Every `SkyServer.xxx` public/internal property delegates to `_defaultInstance`.
The full surface area includes (non-exhaustive):

- All ASCOM position properties: `RightAscension`, `Declination`, `Altitude`,
  `Azimuth`, `SiderealTime`, `Lha`, `IsSideOfPier`, `SideOfPier`
- All ASCOM state properties: `IsSlewing`, `AtPark`, `IsHome`, `Tracking`,
  `TrackingMode`, `IsPulseGuidingRa`, `IsPulseGuidingDec`
- Hardware properties: `CanPPec`, `CanHomeSensor`, `CanPolarLed`,
  `CanAdvancedCmdSupport`, `MountName`, `MountVersion`, `Capabilities`
- Motion properties: `SlewState`, `MoveAxisActive`, `FlipOnNextGoto`
- Derived quantities: `ActualAxisX`, `ActualAxisY`, `AppAxisX`, `AppAxisY`
- Slew speeds (1–8), guide rates, rate offsets

`MountInstanceRegistry` (correctly) creates multiple `MountInstance` objects, but
none of them become the `_defaultInstance` unless explicitly set. The second
telescope's instance is isolated in the registry but is never wired to the
`SkyServer` static facade. Any ASCOM client targeting device-number 1 (Telescope 2)
will therefore read Telescope 1's position data through `SkyServer`.

---

## Issue 12 — `MountInstance.SideOfPier` Reads `SkyServer.SouthernHemisphere`

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 335–341

```csharp
// Inside MountInstance.SideOfPier get { ... }
case AlignmentMode.GermanPolar:
    bool southernHemisphere = SkyServer.SouthernHemisphere;  // ← reads static/default
    if (southernHemisphere) ...
```

`SkyServer.SouthernHemisphere` delegates to `_defaultInstance._settings.SouthernHemisphere`.
If Telescope 1 is in the northern hemisphere and Telescope 2 is in the southern
hemisphere, this property will return Telescope 1's hemisphere for both. The
correct call is `_settings.SouthernHemisphere`.

---

## Issue 13 — `OnUpdateServerEvent` Writes Static `SkyServer` State

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 1404–1427

The per-instance update loop (called by each telescope's `_mediaTimer`) writes
exclusively to static `SkyServer` properties:

```csharp
SkyServer.SiderealTime = SkyServer.GetLocalSiderealTime();
SkyServer.UpdateSteps();                                   // delegates to _defaultInstance
SkyServer.Lha = Coordinate.Ra2Ha12(...);
SkyServer.CheckSlewState();
SkyServer.CheckAxisLimits();
SkyServer.IsHome = SkyServer.AtHome;
SkyServer.IsSideOfPier = SkyServer.SideOfPier;
```

For Telescope 2 (not the `_defaultInstance`), this update loop still updates
`_defaultInstance`'s state rather than its own. This means Telescope 1's position,
slew-state, and pier-side indicators are overwritten on every tick of Telescope 2's
timer.

---

## Issue 14 — `MountConnect()` Reads Capabilities Back from Static After Dispatch

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 532–538 (Simulator), 666–672 (SkyWatcher)

```csharp
// After SkyServer.SkyTasks(MountTaskName.CanPpec) writes to SkyServer.CanPPec...
_canPPec = SkyServer.CanPPec;                // reads from _defaultInstance
_canHomeSensor = SkyServer.CanHomeSensor;
_canPolarLed = SkyServer.CanPolarLed;
_canAdvancedCmdSupport = SkyServer.CanAdvancedCmdSupport;
_mountName = SkyServer.MountName ?? string.Empty;
_mountVersion = SkyServer.MountVersion ?? string.Empty;
_capabilities = SkyServer.Capabilities ?? string.Empty;
```

`SkyTasks` writes the query results into `SkyServer.CanPPec` (→
`_defaultInstance._canPPec`). For Telescope 2 these values are written into
Telescope 1's backing fields, then read back into Telescope 2's `_canPPec`.
This produces wrong capability data in `_canPPec` for Telescope 2 and simultaneously
corrupts Telescope 1's capability flags.

---

## Issue 15 — Commands in `SkyGoTo` / `SimGoTo` Use Default Constructors

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`  
**Lines:** 1479–1480, 1587–1590, 1683–1688 (Simulator)  
**Lines:** 1764–1765, 1892–1893, 1911–1912, 2011, 2032 (SkyWatcher)

Several commands in the GoTo path use `id = 0` and no queue argument, which
resolves to `SkyQueue.Instance`:

```csharp
// Simulator GoTo — first slew
_ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]);   // MountQueue.Instance
_ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]);

// SkyWatcher GoTo — first slew
_ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);   // SkyQueue.Instance
_ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);

// Precision GoTo
_ = new SkyAxisGoToTarget(0, Axis.Axis1, ...);            // SkyQueue.Instance
_ = new SkyAxisGoToTarget(0, Axis.Axis2, ...);

// Pulse GoTo
_ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);   // SkyQueue.Instance
_ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);
```

Status-check commands in these loops correctly use `SkyQueueInstance`:
```csharp
var statusx = new SkyIsAxisFullStop(SkyQueueInstance.NewId, SkyQueueInstance, Axis.Axis1);
```

This creates an inconsistency: the **stop command** that initiates the goto goes
to the static (shared) queue, while the status-check polls the per-instance queue.
For Telescope 2, the `SkyAxisGoToTarget(0, …)` command moves whichever hardware
is registered in `SkyQueue._instance` (probably Telescope 2 since it registered
last), but if Telescope 1 also fires a goto it will move Telescope 2's hardware.

---

## Summary Table — Issues by Severity

| # | Issue | Severity | Impact |
|---|-------|----------|--------|
| 1 | `SkyQueue` static singleton; `RegisterInstance` overwrites previous | 🔴 CRITICAL | Telescope 1 is detached from hardware on Telescope 2 start |
| 2 | All `SkyTasks()` commands use `SkyQueue.Instance` default constructors | 🔴 CRITICAL | Every task command goes to last-registered hardware |
| 3 | `GetRawDegrees()` and `GetRawSteps()` use static queue | 🔴 CRITICAL | Position reads return wrong telescope's data |
| 4 | `SkyTasks()` / `SimTasks()` use `SkyServer._settings` (default instance) | 🔴 CRITICAL | Settings for Telescope 2 tasks are from Telescope 1 |
| 5 | `PropertyChangedSkyQueue` writes `SkyQueue.Steps` into `_defaultInstance` | 🔴 CRITICAL | Telescope 2 step updates corrupt Telescope 1 position state |
| 6 | `SkySystem.Serial` is a single static port | 🔴 CRITICAL | Cannot open two COM ports simultaneously |
| 7 | `_mountPositionUpdatedEvent` is a single shared event | 🟠 HIGH | Precision-goto race condition under concurrent slews |
| 8 | `_trackingOffsetRate` is a true static field | 🟠 HIGH | Custom tracking rates cross-contaminate between telescopes |
| 9 | `SkyTrackingOffset` is a true static array | 🟠 HIGH | Tracking-rate calculation corrupted |
| 10 | HC anti-backlash fields are true static | 🟠 HIGH | Backlash compensation state shared |
| 11 | `SkyServer._defaultInstance` wires only one telescope | 🟠 HIGH | Second telescope invisible to ASCOM clients via static API |
| 12 | `MountInstance.SideOfPier` reads `SkyServer.SouthernHemisphere` | 🟡 MEDIUM | Wrong hemisphere for Telescope 2 |
| 13 | `OnUpdateServerEvent` writes only static `SkyServer` state | 🟡 MEDIUM | Telescope 2 timer updates corrupt Telescope 1 state |
| 14 | `MountConnect()` reads capabilities back from static after dispatch | 🟡 MEDIUM | Wrong capability flags for Telescope 2 |
| 15 | `SkyAxisGoToTarget(0, …)` in GoTo loops use default constructor | 🟡 MEDIUM | GoTo commands may target wrong hardware |

---

## Recommended Fix Sequence

The issues above fall into four distinct work items that must be addressed in order:

### Work Item A — Per-Instance Queue (fixes Issues 1, 2, 3, 5, 6, 15)

`SkyQueue` must become a per-instance object, not a static singleton facade.
Each `MountInstance` already owns a `SkyQueueInstance` field — this should be the
**only** way commands are dispatched. Concrete steps:

1. Remove the `SkyQueue.RegisterInstance()` / `SkyQueue._instance` singleton pattern.
2. Pass `SkyQueueInstance` explicitly to every command construction site in
   `SkyTasks()`, `AxesStopValidate()`, `GetRawDegrees()`, `GetRawSteps()`, and
   all GoTo loops.
3. Replace `MountInstance.MountStart()` callbacks that write to `SkyQueue.Steps`
   etc. with per-instance callbacks that write to `_steps`, `_isPulseGuidingRa`,
   `_isPulseGuidingDec` on the owning `MountInstance` directly.
4. `SkySystem` must be made per-instance (or at minimum given a dictionary keyed
   by instance ID) to support multiple serial ports.

### Work Item B — Per-Instance `SkyTasks` / `SimTasks` (fixes Issues 4, 14)

`SkyTasks()` and `SimTasks()` must accept a `MountInstance` parameter (or become
instance methods on `MountInstance`). Settings must be read from the calling
instance's `_settings`, and results must be written to that instance's fields
rather than to `SkyServer.CanPPec` (→ `_defaultInstance`).

### Work Item C — Instance-Scoped Position and Slew Events (fixes Issues 7, 13)

- `_mountPositionUpdatedEvent` must move to `MountInstance` (one per telescope).
- `OnUpdateServerEvent` must write to `this._xxx` fields rather than to
  static `SkyServer.xxx` properties.

### Work Item D — Remove Remaining True Static Fields (fixes Issues 8–12)

- Move `_trackingOffsetRate`, `SkyTrackingOffset`, `_hcPrevMoveRa`, `_hcPrevMoveDec`,
  `HcPrevMovesDec` into `MountInstance` backing fields.
- Change `MountInstance.SideOfPier` to use `_settings.SouthernHemisphere`.

---

## Files Requiring Changes

| File | Work Items | Notes |
|------|-----------|-------|
| `SkyQueue.cs` | A | Replace singleton with per-instance design or remove static facade |
| `SkyCommands.cs` | A | Remove default constructors that capture `SkyQueue.Instance` |
| `SkyServer.Core.cs` | A, B, C, D | `SkyTasks`, `SimTasks`, `AxesStopValidate`, static fields |
| `MountInstance.cs` | A, B, C, D | `GetRawDegrees`, `GetRawSteps`, `MountStart`, `OnUpdateServerEvent`, `SideOfPier`, `MountConnect` callbacks |
| `SkySystem.cs` | A | Make serial port per-instance |

---

*This document was auto-generated from code analysis. No code was modified.*
