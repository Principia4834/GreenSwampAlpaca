# Multi-Telescope Serial Architecture: Options Analysis

**Date:** March 2026  
**Status:** For review — no code changes have been made  
**Context:** Supporting N physical telescopes on independent COM ports (e.g. COM3 + COM8), each with their own baud rate and serial settings, operating independently via the existing `MountInstanceRegistry` instance model.

---

## 1. Executive Summary

`MountInstanceRegistry` and `SkySettingsInstance` are already multi-device capable. All the per-device serial configuration fields (`Port`, `BaudRate`, `HandShake`, `DataBits`, `ReadTimeout`, `DtrEnable`, `RtsEnable`) exist on `SkySettingsInstance` today. The serial abstraction layer (`ISerialPort`, `GsSerialPort`, `SerialOverUdpPort`, `SkyQueueImplementation.Start(ISerialPort, ...)`) is already clean and injection-friendly.

Three interlocking static singletons prevent independent operation. Eliminating them in a structured sequence — directly continuing the existing static-to-instance migration — will unlock full per-device independence.

---

## 2. Current Architecture: The Three Bottlenecks

### 2.1 Bottleneck 1 — `SkySystem` Serial Singleton

`SkySystem` is a `static class` that owns:

```
private static SkySettingsInstance? _settings         ← ONE settings reference
public  static ISerialPort           Serial            ← ONE open port
private static ConcurrentDictionary<long,bool> _connectStates  ← ONE reference-count dict
private static bool _initialized                       ← ONE initialisation flag
```

The `ConnectSerial` setter creates a `GsSerialPort` (or `SerialOverUdpPort`) using `_settings.Port` and `_settings.BaudRate` — whichever single settings object was passed to `SkySystem.Initialize()`.

`MountInstance.MountStart()` for a SkyWatcher device does:

```
SkySystem.ConnectSerial = false;          // close any existing port
SkySystem.ConnectSerial = true;           // open with SkySystem._settings → one port
sqImpl.Start(SkySystem.Serial, ...);      // pass the single static port to queue
```

If two `MountInstance` objects call `MountStart()` in sequence, the second call closes the first device's port. Both ASCOM clients then share the same physical channel — chaos ensues.

### 2.2 Bottleneck 2 — `SkyQueue` Static Facade: Single `_instance` Pointer

```csharp
// SkyQueue.cs
private static SkyQueueImplementation _instance;  // single pointer

public static void RegisterInstance(SkyQueueImplementation impl)
    => _instance = impl;   // replaces any existing registration

public static void AddCommand(ISkyCommand command)
    => _instance?.AddCommand(command);   // always routes to the last-registered queue
```

Every command class (`SkyAxisSlew`, `SkyAxisPulse`, `SkyGetRate`, etc.) calls `SkyQueue.AddCommand(this)` in its constructor. When device 1's `MountStart()` calls `SkyQueue.RegisterInstance(sqImpl1)`, it silently replaces device 0's queue. All commands that were meant for device 0 now go to device 1's port.

### 2.3 Bottleneck 3 — ASCOM Connection State in `Telescope.cs`

`Telescope.Connected` getter and setter both go through `SkySystem`:

```csharp
// Telescope.cs (Blocker B2 — three callsites)
get { return SkySystem.Connected; }
set { SkySystem.SetConnected(_objectId, value); }
```

`SkySystem.SetConnected(id, true)` calls `SkyServer.IsMountRunning = true`. This is a single static flag — the second device connecting finds "already running" and does not start its own mount loop; the first device disconnecting sets it false and orphans the second.

### 2.4 What Is Already Multi-Device Ready

| Component | Status | Notes |
|---|---|---|
| `SkySettingsInstance` | ✅ Per-device | `Port`, `BaudRate`, `HandShake`, `DataBits`, `ReadTimeout`, `DtrEnable`, `RtsEnable` all present |
| `MountInstanceRegistry` | ✅ Multi-slot | `Dictionary<int, MountInstance>` — N devices |
| `ISerialPort` interface | ✅ Clean | `GsSerialPort` (COM) and `SerialOverUdpPort` (UDP/WiFi) both implement it |
| `GsSerialPort` / `SerialOverUdpPort` | ✅ Stateless factory | No static fields; safe to instantiate N times |
| `SkyQueueImplementation.Start(ISerialPort, ...)` | ✅ Injection-ready | Port is a parameter — not fetched from `SkySystem` |
| `SkyWatcher.Initialize(ISerialPort)` | ✅ Injection-ready | Receives port through `InitializeExecutor` |
| `MountInstance` backing fields | ✅ Per-instance | All state migrated; no shared mutable fields |
| `appsettings.json AlpacaDevices` array | ✅ N-device schema | Serial settings just need to move inside each entry |

---

## 3. Requirements for Independent Multi-Telescope Operation

1. Opening COM3 at 9600 baud must be entirely independent of opening COM8 at 115200 baud.
2. Connecting an ASCOM client to telescope #0 must not affect telescope #1.
3. Disconnecting telescope #0 must not close telescope #1's port.
4. Commands issued for telescope #0 must reach COM3; commands for telescope #1 must reach COM8.
5. Serial errors on one device must not cause the other device to fault.
6. Per-device serial settings must be configurable independently (Blazor UI + JSON config).

---

## 4. Option A — Instantiate `SkySystem`

Convert `SkySystem` from a `static class` to an instance class (`MountSerialConnection` or similar). Each `MountInstance` creates and owns one instance.

**Structural change:**

```
MountInstance
  ├── _settings: SkySettingsInstance     (already owned)
  ├── _connection: MountSerialConnection  (new — replaces SkySystem)
  │     ├── _serial: ISerialPort          (created from _settings.Port etc.)
  │     ├── _connectStates: ConcurrentDictionary<long,bool>
  │     ├── ConnectSerial get/set
  │     └── SetConnected(id, value)
  └── SkyQueueInstance: SkyQueueImplementation
```

`SkyQueue` is extended to a keyed dictionary:
```
SkyQueue.RegisterInstance(deviceNumber, impl)
SkyQueue.AddCommand(command, deviceNumber)
```

`Telescope.cs` routes `Connected` through `_mountInstance.Connection.IsConnected` / `_mountInstance.Connection.SetConnected()`.

**Pros:**
- Full isolation across all three bottlenecks
- `ConnectStates` reference-counting is per-device (correct: tracks how many ASCOM clients are connected to THIS telescope)
- `Error` state is per-device
- Clean ownership: `MountInstanceRegistry` → `MountInstance` → `MountSerialConnection` → `ISerialPort`
- `DiscoverSerialDevices()` becomes a shared static utility (`SerialPortDiscovery.GetAvailablePorts()`) with no per-device state

**Cons:**
- Largest refactor: `SkySystem.*` has callsites throughout `SkyServer.*`, `SkyCommands.cs`, `SkyWatcher.cs`
- `SkyQueue` command routing must be solved in the same pass (see §8)
- Two problems (serial singleton + queue routing) must be solved together, increasing scope

**Estimated effort:** Large — 2–3 weeks including integration testing.

---

## 5. Option B — Serial Port Factory (Bypass `SkySystem` in `MountInstance`)

Keep `SkySystem` structurally as-is. Add a `SerialPortFactory.Create(SkySettingsInstance) → ISerialPort` static helper that extracts the port-creation logic currently embedded in the `ConnectSerial` setter. `MountInstance` creates its own port via this factory and bypasses `SkySystem.ConnectSerial`.

**Structural change:**

```csharp
// New static helper (2 methods, no fields):
internal static class SerialPortFactory
{
    public static ISerialPort Create(SkySettingsInstance settings) { ... }
}

// MountInstance gains:
private ISerialPort? _serial;

// MountStart() changes:
_serial = SerialPortFactory.Create(_settings);
_serial.Open();
sqImpl.Start(_serial, ...);  // same sqImpl.Start() call — already compatible

// MountStop() changes:
_serial?.Dispose();
_serial = null;
```

`SkySystem` remains untouched and continues to serve slot 0 for backward compatibility.

**Pros:**
- Smallest code change: only `MountInstance.MountStart/Stop` and the new factory
- Serial port isolation is achieved immediately with low risk
- `SkyQueueImplementation.Start(ISerialPort, ...)` is already compatible — no queue changes required for this step
- `SkySystem` stays in place; existing single-device code paths continue to work
- Incremental: can ship this alone and build on it

**Cons:**
- `Telescope.Connected` still routes through `SkySystem.Connected` / `SkySystem.SetConnected()` — connection state isolation is NOT solved; `SkyServer.IsMountRunning` still shared
- `SkyQueue._instance` is still a single pointer — command routing is still broken for multi-device; all commands for both devices go to the last-registered queue
- Two code paths emerge: device 0 uses `SkySystem`; device 1+ uses `SerialPortFactory`
- `SkySystem.Serial` is still a public static property; `SkyCommands.cs` and `SkyWatcher.cs` still read it directly — they would use the wrong port for device 1+
- Technical debt: `SkySystem` remains as a half-retired artefact indefinitely unless followed up
- Does not satisfy requirements 2–4 from §3 without further work

**Estimated effort:** Small for serial isolation alone (~2 days). Leaves multi-device command routing and connection state entirely unsolved.

**Verdict:** Viable only as a very short-term proof-of-concept or as the first commit of a larger Option C sequence.

---

## 6. Option C — Move Serial Management Entirely Into `MountInstance` (Recommended)

`MountInstance` becomes the authoritative owner of its serial connection. This is the direct continuation of the static-to-instance migration pattern already in progress. `SkySystem` is reduced to a port-discovery shell and then removed.

### 6.1 Phase 1 — Serial & Connection Isolation

**`MountInstance` gains three fields:**
```
_serial: ISerialPort?
_connectType: ConnectType
_serialError: Exception?
```

**`MountInstance.MountStart()` changes:**
```
// Replace:
SkySystem.ConnectSerial = false;
SkySystem.ConnectSerial = true;
sqImpl.Start(SkySystem.Serial, ...)

// With:
CloseSerial();
OpenSerial();   // creates GsSerialPort or SerialOverUdpPort from _settings
if (_serial?.IsOpen != true) throw SkyServerException(ErrSerialFailed, ...)
sqImpl.Start(_serial, ...)
```

**`MountInstance` gains `ConnectStates` dictionary:**
```
private ConcurrentDictionary<long, bool> _connectStates
public void SetConnected(long objectId, bool value) { ... EnsureMountRunning() ... }
public bool IsConnected => _connectStates?.Count > 0
```

**`Telescope.cs` fix (Blocker B2):**
```csharp
// Connected getter:
return _mountInstance.IsConnected;

// Connected setter:
_mountInstance.SetConnected(_objectId, value);
```

**`SkySystem` reduction:** Retain only `DiscoverSerialDevices()` as a static utility; all instance-holding fields removed.

### 6.2 Phase 2 — SkyQueue Command Routing

**`SkyQueue` static facade extended:**
```csharp
// Replace single pointer:
private static SkyQueueImplementation _instance;

// With keyed dictionary:
private static Dictionary<int, SkyQueueImplementation> _instances = new();

public static void RegisterInstance(int deviceNumber, SkyQueueImplementation impl)
    => _instances[deviceNumber] = impl;

// Keep single-device AddCommand for backward compatibility:
public static void AddCommand(ISkyCommand command) => _instances.Values.FirstOrDefault()?.AddCommand(command);
```

**`SkyServer.SkyTasks()` updated to route through the instance queue directly:**
```csharp
// Replace:
SkyQueue.AddCommand(new SkyAxisSlew(...))

// With:
_defaultInstance?.SkyQueueInstance.AddCommand(new SkyAxisSlew(...))
```

This means commands are dispatched to the correct instance's queue without requiring changes to command class signatures.

**`MountInstance.MountStart()` no longer calls `SkyQueue.RegisterInstance()`** — the queue is already owned by the instance and is accessed directly via `SkyQueueInstance`.

### 6.3 Phase 3 — Configuration Schema

Extend `appsettings.json AlpacaDevices` entries to include per-device serial settings:

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Main Refractor",
      "ProfileName": "telescope-main",
      "SerialSettings": {
        "Port": "COM3",
        "BaudRate": 9600,
        "DataBits": 8,
        "Handshake": "None",
        "ReadTimeout": 5000,
        "DtrEnable": false,
        "RtsEnable": false
      }
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Guide Scope",
      "ProfileName": "telescope-guide",
      "SerialSettings": {
        "Port": "COM8",
        "BaudRate": 115200,
        "DataBits": 8,
        "Handshake": "None",
        "ReadTimeout": 5000,
        "DtrEnable": false,
        "RtsEnable": false
      }
    }
  ]
}
```

This is fully compatible with the existing `IVersionedSettingsService` pattern where each device has its own profile directory under `%AppData%/GreenSwampAlpaca/{version}/{profileName}/`.

**Pros:**
- Clean ownership: `MountInstanceRegistry` → `MountInstance` → `ISerialPort`
- Directly continues the static-to-instance migration — no architectural surprises
- Solves all three bottlenecks in a coherent sequence
- `SkySystem` is cleanly removed (or left as an empty shell)
- Testable: `MountInstance.OpenSerial()` can be replaced with an injected mock `ISerialPort` in unit tests
- No new patterns introduced — same idioms already used in the migration

**Cons:**
- Requires solving SkyQueue command routing in Phase 2 — this is non-trivial
- Must be done in parallel with or immediately after Blocker B2 (`Telescope.cs` connection routing)
- `SkyCommands.cs` and `SkyWatcher.cs` currently reference `SkySystem.*` directly — these callsites must be audited when `SkySystem` is removed

**Estimated effort:** Phase 1 (~3 days), Phase 2 (~1 week), Phase 3 (~1 day).

---

## 7. Option D — DI-Scoped Connection Service

Register a new `ISerialConnectionService` as a keyed/scoped DI service. Each `MountInstance` receives its own connection service through constructor injection.

**Pros:**
- Follows modern .NET DI patterns; highly testable

**Cons:**
- `MountInstanceRegistry.CreateInstance()` does not use DI — would require threading DI plumbing through the entire instance creation chain
- `SkyServer` static methods (`SkyTasks`, `SkyGoTo`, etc.) are fundamentally incompatible with DI scopes without a larger rearchitecture
- Highest complexity, highest risk
- Premature until the static-to-instance migration is complete

**Verdict:** Not recommended at this stage. Revisit once the static `SkyServer` surface area is eliminated.

---

## 8. The `SkyQueue` Routing Problem — Already Solved by Existing Design

No queue dictionary is required. The command layer is **already designed for injection**.

### The Existing Two-Constructor Pattern

Every SkyWatcher command class — and every simulator command class — already has two constructors:

```csharp
// Queue-injection constructor (the DI form — already present):
public SkyAxisSlew(long id, ICommandQueue<SkyWatcher> queue, Axis axis, double rate)
    : base(id, queue) { ... }

// Static-shortcut constructor (legacy single-device convenience form):
public SkyAxisSlew(long id, Axis axis, double rate)
    : this(id, SkyQueue.Instance, axis, rate) { }
```

`CommandBase<TExecutor>` calls `queue.AddCommand(this)` in its base constructor — the command self-routes to whichever queue was passed at construction time. The routing decision is at the `new` call site, not at a central dispatcher.

`MountInstance.SkyQueueInstance` is `CommandQueueBase<SkyWatcher>`, which implements `ICommandQueue<SkyWatcher>`. It is directly compatible with the injection constructor today — no new types required.

The simulator commands follow exactly the same pattern with `ICommandQueue<Actions>` / `MountQueue.Instance`.

### The Routing Fix

Change `SkyServer.SkyTasks()` call sites from the static-shortcut constructor to the injection constructor, passing the current instance's queue:

```csharp
// BEFORE — routes to SkyQueue._instance (shared static pointer, one port):
_ = new SkyAxisSlew(0, Axis.Axis1, rate.X);

// AFTER — routes to this device's own queue, which owns this device's ISerialPort:
var q = _defaultInstance!.SkyQueueInstance;
_ = new SkyAxisSlew(q.NewId, q, Axis.Axis1, rate.X);
```

Simulator commands in `SkyServer.SimTasks()` follow the identical pattern:

```csharp
// BEFORE:
_ = new CmdAxisPulse(0, Axis.Axis2, decGuideRate, duration, token);

// AFTER:
var q = _defaultInstance!.MountQueueInstance;
_ = new CmdAxisPulse(q.NewId, q, Axis.Axis2, decGuideRate, duration, token);
```

### What Does Not Change

- Command class signatures are unchanged — no new constructors are needed anywhere
- The static shortcut constructors (`SkyAxisSlew(id, axis, rate)`) remain valid and are not removed; they simply stop being called by `SkyTasks()`
- `SkyQueue._instance` and `MountQueue._instance` become unused once all `SkyTasks()`/`SimTasks()` call sites are updated; they can be deprecated at leisure
- `SkyQueue.RegisterInstance()` is no longer called from `MountInstance.MountStart()` — the queue is already owned and accessed directly via `SkyQueueInstance`

### Scope

Callsite changes are confined to `SkyServer.SkyTasks()`, `SkyServer.SimTasks()`, and the handful of places outside those methods that construct commands directly (e.g. pulse guide methods). No dictionary infrastructure, no DI container, no new types.

---

## 9. Comparison Matrix

| Criterion | Option A (Instantiate SkySystem) | Option B (Port Factory) | Option C (Into MountInstance) | Option D (DI Service) |
|---|---|---|---|---|
| **Serial port isolation** | ✅ Full | ✅ Full | ✅ Full | ✅ Full |
| **Connection state isolation** | ✅ Full | ❌ Not solved | ✅ Full | ✅ Full |
| **SkyQueue command routing** | Pass queue to existing injection ctor | Still broken | Pass queue to existing injection ctor | Pass queue to existing injection ctor |
| **Alignment with ongoing migration** | Medium | Low | ✅ High | Low |
| **Estimated effort** | Large (2–3 wk) | Small (2 days, partial) | Medium (~1.5 wk total) | Very large (4–6 wk) |
| **Technical debt introduced** | Low | High | Low | Low |
| **Testability** | Good | Fair | ✅ Good | Best |
| **Risk level** | Medium | Low (but incomplete) | Medium | High |
| **Removes `SkySystem`** | Replaces with class | No | ✅ Yes | ✅ Yes |
| **Solves all 5 requirements (§3)** | ✅ Yes | ❌ No (req 2–4 remain) | ✅ Yes | ✅ Yes |

---

## 10. Recommendation

**Adopt Option C implemented in three phases.**

### Rationale

1. `SkySettingsInstance` already carries all per-device serial configuration — no new data model work.
2. `SkyQueueImplementation.Start(ISerialPort, ...)` already accepts the port as a parameter — the queue layer does not need changing in Phase 1.
3. Option C directly continues the static-to-instance migration pattern that has already succeeded for settings, state fields, slew controller, and position calculations. No new patterns are introduced.
4. Option B leaves three of the five requirements unsolved and creates long-lived technical debt.
5. Option A achieves the same end state as Option C but frames it as a larger up-front rewrite rather than a sequenced migration.

### Suggested Implementation Sequence

**Phase 1 — Serial & Connection Isolation** (~3 days)  
Prerequisite: Blocker B2 (`SkySystem.*` references in `Telescope.cs`) is resolved in this phase.

1. Add `_serial: ISerialPort?`, `_connectType: ConnectType`, `_serialError: Exception?` to `MountInstance`
2. Add `OpenSerial()` / `CloseSerial()` to `MountInstance` (extract from `SkySystem.ConnectSerial` setter)
3. Update `MountInstance.MountStart()` / `MountStop()` to use `OpenSerial()` / `CloseSerial()`
4. Move `ConnectStates` from `SkySystem` to `MountInstance`; add `SetConnected(long, bool)` and `IsConnected`
5. Update `Telescope.cs Connected` to route to `_mountInstance.IsConnected` / `_mountInstance.SetConnected()`
6. Move `DiscoverSerialDevices()` to `UnifiedDeviceRegistry.InitializeReservedSlots()`; remove all instance-holding fields from `SkySystem` and delete the class
7. Build verify; run ConformU against device 0

**Phase 2 — SkyQueue Command Routing** (~1 week)

1. Update `SkyServer.SkyTasks()` call sites: pass `_defaultInstance.SkyQueueInstance` to the existing injection constructor on each command (`new SkyAxisSlew(q.NewId, q, ...)` instead of `new SkyAxisSlew(0, ...)`)
2. Update `SkyServer.SimTasks()` call sites identically using `_defaultInstance.MountQueueInstance`
3. Mark all static shortcut constructors in `SkyCommands.cs` and `Simulator/Commands.cs` as `[Obsolete("Use the ICommandQueue injection constructor")]` — plan for removal when `SkyQueue`/`MountQueue` facades are retired
4. Remove `SkyQueue.RegisterInstance()` call from `MountInstance.MountStart()` — the queue is already owned by the instance
5. Audit and update `SkyCommands.cs` and `SkyWatcher.cs` remaining `SkySystem.*` callsites
6. Build verify; run ConformU; smoke-test two simulators simultaneously

**Phase 3 — Configuration & UI** (~1 day + design time)

1. Extend `appsettings.json AlpacaDevices` entries with `SerialSettings` block (see §6.3)
2. Update `Program.cs` device registration to read per-device serial settings
3. Update the Blazor combined connection status panel to show port name, baud rate, and open/closed state (single panel — not per-device)
4. Update `UnifiedDeviceRegistry.InitializeReservedSlots()` to read per-device `SerialSettings` and apply them to each `SkySettingsInstance` before device start

*Detailed task-by-task breakdown: see `docs\MultiTelescope-OptionC-ImplementationPlan.md`.*

---

## 11. Risk Notes

| Risk | Mitigation |
|---|---|
| Two devices accidentally configured on the same COM port | Catch `IOException` on `Open()` in `MountInstance.OpenSerial()` and surface as `"Port COM3 is already in use by another device"` |
| Thread safety: static `SkyServer` properties (`Altitude`, `Azimuth`, etc.) are overwritten by whichever device's timer fires last | This is Blocker B5 (per-device Blazor events) — must be addressed in parallel; not introduced by this change |
| Port discovery: `SerialPort.GetPortNames()` is called during startup | Already safe to call from multiple threads; no lock required |
| `SerialOverUdpPort` (WiFi) vs `GsSerialPort` (COM): both must be supported in `MountInstance.OpenSerial()` | The `_settings.Port.Contains("COM")` branch logic already exists in `SkySystem.ConnectSerial` setter — move it verbatim |
| `SkyCommands.cs` lines 164–174 and `SkyWatcher.cs` lines 143–151 reference `SkySystem.ConnectSerial`/`SkySystem.Serial` | Audit during Phase 1; replace with `_serial` from the owning `MountInstance` via `SkyQueueImplementation.Serial` |

---

## 12. Design Decisions

All open questions from the initial design review have been resolved. The following decisions are binding for the Option C implementation.

| # | Question | Decision |
|---|---|---|
| 1 | **Reference counting scope** — `MountInstance` directly or a thin `ConnectionTracker` helper? | **`MountInstance` directly.** `_connectStates: ConcurrentDictionary<long, bool>` and `SetConnected()` live on the instance. No additional wrapper class. |
| 2 | **Static shortcut constructor retirement** — `[Obsolete]` immediately or silently unused? | **Mark `[Obsolete]` immediately** once all production call sites in `SkyTasks()`/`SimTasks()` are updated (Phase 2 step 3). Plan for removal when `SkyQueue`/`MountQueue` static facades are retired. |
| 3 | **Blazor UI** — per-device panel or combined panel? | **Combined status panel** is sufficient for the near term. Port name, baud rate, and open/closed state shown in one panel. |
| 4 | **`SkySystem.Initialize()` caller in `Program.cs`** — retain minimal call or move? | **Move port discovery to `UnifiedDeviceRegistry.InitializeReservedSlots()`.** After Phase 1, `SkySystem` has no remaining instance-holding state and is deleted entirely. `Program.cs` no longer calls `SkySystem.Initialize()`. |
| 5 | **Simulator devices** — are `SimTasks()` routing changes parallel and symmetric? | **Confirmed.** `SimTasks()` changes mirror `SkyTasks()` exactly, using `_defaultInstance.MountQueueInstance` and `ICommandQueue<Actions>`. |

---

*End of document.*
