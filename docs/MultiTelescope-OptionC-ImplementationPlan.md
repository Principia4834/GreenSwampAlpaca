# Multi-Telescope Option C — Implementation Plan
## GreenSwamp Alpaca — Serial & Queue Isolation

**Prepared for:** Coding reference  
**Design document:** `docs\MultiTelescope-SerialArchitecture.md`  
**Design decisions:** §12 of architecture document  
**Status:** Ready to implement — no code changes have been made

---

## 1. Overview

Option C moves serial connection ownership from the static `SkySystem` singleton into each `MountInstance`, and routes command dispatch through instance-owned queues using the injection constructors that already exist on every command class. Three phases must be completed in order.

```
Phase 1 — Serial & Connection Isolation  (prerequisite for Phase 2)
    └── Phase 2 — SkyQueue Command Routing  (prerequisite for Phase 3 testing)
            └── Phase 3 — Configuration & UI
```

**Step 10 of the static-to-instance migration** (`SkySettings` static facade removal) is independent of all three phases and may proceed in parallel.

---

## 2. Prerequisites

- [x] Build is green before starting each phase (`run_build`) — ✅ Confirmed March 2026
- [x] Existing 3 automated tests pass — ✅ Confirmed March 2026 (3 pass, 3 integration tests skipped)
- [ ] ConformU passes against slot 0 (single telescope) — baseline not yet recorded

---

## 3. Phase 1 — Serial & Connection Isolation

**Goal:** Each `MountInstance` owns its `ISerialPort`. No two instances share a port. `Telescope.cs Connected` routes through the instance (Blocker B2 resolved).

**Estimated effort:** ~3 days

### Files Modified

| File | Change |
|---|---|
| `GreenSwamp.Alpaca.MountControl/MountInstance.cs` | Major additions — serial fields, open/close, connect state |
| `GreenSwamp.Alpaca.MountControl/SkySystem.cs` | Remove all instance-holding fields; delete class |
| `GreenSwamp.Alpaca.Server/Services/UnifiedDeviceRegistry.cs` | Absorb `DiscoverSerialDevices()` |
| `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` | Blocker B2 — `Connected` property |

---

### Task 1.1 — Add serial fields to `MountInstance`

**File:** `MountInstance.cs`

Add alongside the existing instance fields:

```csharp
private ISerialPort?  _serial;
private ConnectType   _connectType;   // ConnectType enum — same values used in SkySystem today
private Exception?    _serialError;
```

---

### Task 1.2 — Add `OpenSerial()` and `CloseSerial()` to `MountInstance`

**File:** `MountInstance.cs`

Extract the port-creation branch logic verbatim from `SkySystem.ConnectSerial` setter. Both `GsSerialPort` (COM) and `SerialOverUdpPort` (UDP/WiFi) must be supported — the `Port.Contains("COM")` branch already exists in `SkySystem` and must be moved, not rewritten.

```csharp
internal void OpenSerial()
{
    CloseSerial();
    if (_settings == null) return;

    _serial = _settings.Port.Contains("COM")
        ? new GsSerialPort(
              _settings.Port, (int)_settings.BaudRate, (int)_settings.DataBits,
              _settings.HandShake, _settings.ReadTimeout,
              _settings.DtrEnable, _settings.RtsEnable)
        : (ISerialPort)new SerialOverUdpPort(_settings.Port, (int)_settings.BaudRate);

    _serial.Open();
    _connectType = _settings.Port.Contains("COM") ? ConnectType.Serial : ConnectType.Udp;
}

internal void CloseSerial()
{
    _serial?.Dispose();
    _serial = null;
    _connectType = ConnectType.None;
}
```

> **Risk note:** Verify the exact `GsSerialPort` and `SerialOverUdpPort` constructor signatures before coding — they may differ from the parameters shown above. Read the current `SkySystem.ConnectSerial` setter and copy the call verbatim.

---

### Task 1.3 — Add `ConnectStates`, `SetConnected()`, and `IsConnected` to `MountInstance`

**File:** `MountInstance.cs`

Move `ConcurrentDictionary<long, bool> _connectStates` from `SkySystem` to `MountInstance`. Reference-count semantics are preserved: port opens on first connect, closes on last disconnect.

```csharp
private ConcurrentDictionary<long, bool> _connectStates = new();

public bool IsConnected => !_connectStates.IsEmpty;

public void SetConnected(long objectId, bool value)
{
    if (value)
    {
        _connectStates[objectId] = true;
        if (IsConnected) EnsureMountRunning();   // replaces SkyServer.IsMountRunning = true
    }
    else
    {
        _connectStates.TryRemove(objectId, out _);
        if (!IsConnected) EnsureMountStopped();  // replaces SkyServer.IsMountRunning = false
    }
}
```

> **Note:** Identify the current `EnsureMountRunning()` / `EnsureMountStopped()` equivalent in `SkySystem.SetConnected()` before coding. The body of this method must reproduce the mount-start/stop lifecycle logic currently in `SkySystem`.

---

### Task 1.4 — Update `MountInstance.MountStart()` to use `OpenSerial()`

**File:** `MountInstance.cs`

Replace:
```csharp
SkySystem.ConnectSerial = false;
SkySystem.ConnectSerial = true;
sqImpl.Start(SkySystem.Serial, ...);
```

With:
```csharp
CloseSerial();
OpenSerial();
if (_serial?.IsOpen != true)
    throw new SkyServerException(ErrorCode.ErrSerialFailed,
        $"Failed to open port {_settings?.Port}");
sqImpl.Start(_serial, ...);
```

`SkyQueueImplementation.Start(ISerialPort, ...)` already accepts the port as a parameter — no changes to the queue layer are needed in Phase 1.

---

### Task 1.5 — Update `MountInstance.MountStop()` to use `CloseSerial()`

**File:** `MountInstance.cs`

Replace:
```csharp
SkySystem.ConnectSerial = false;
```

With:
```csharp
CloseSerial();
```

---

### Task 1.6 — Fix Blocker B2: `Telescope.cs Connected` property

**File:** `Telescope.cs`

The three `SkySystem.*` references in `Connected` (getter and setter) are the last Telescope.cs blocker.

Replace:
```csharp
get { return SkySystem.Connected; }
set { SkySystem.SetConnected(_objectId, value); }
```

With:
```csharp
get { return _mountInstance?.IsConnected ?? false; }
set { _mountInstance?.SetConnected(_objectId, value); }
```

Where `_mountInstance` is the already-resolved `MountInstance` reference from `MountInstanceRegistry.GetInstance(_deviceNumber)` that was established during Step 8 of the static-to-instance migration.

---

### Task 1.7 — Move port discovery; delete `SkySystem`

**Files:** `SkySystem.cs`, `UnifiedDeviceRegistry.cs`

1. Copy `SkySystem.DiscoverSerialDevices()` into `UnifiedDeviceRegistry.InitializeReservedSlots()` as a local helper (or a `private static` method on the same class).
2. Update any callers of `SkySystem.DiscoverSerialDevices()` (typically the Blazor settings page) to call the new location.
3. Remove all remaining fields and methods from `SkySystem`. Delete the file.
4. Remove the `SkySystem.Initialize()` call from `Program.cs`.

> **Before deleting:** Run `find_symbol` on `SkySystem` to enumerate all remaining callsites and resolve each one.

---

### Task 1.8 — Build verify + ConformU

```
run_build
// ConformU full suite against slot 0
// Verify: connect, slew, track, disconnect — no regression
```

**Accept criteria:**
- Zero new build errors
- Existing 3 automated tests pass
- ConformU reports same result as baseline

---

## 4. Phase 2 — SkyQueue Command Routing

**Goal:** Commands dispatched by `SkyTasks()` and `SimTasks()` route to the correct instance's `ICommandQueue`. Static shortcut constructors are marked `[Obsolete]`.

**Estimated effort:** ~1 week

### Files Modified

| File | Change |
|---|---|
| `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs` | `SkyTasks()` call sites |
| `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs` | Pulse guide and direct command call sites |
| `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyCommands.cs` | `[Obsolete]` on shortcut constructors |
| `GreenSwamp.Alpaca.Mount.Simulator/Commands.cs` | `[Obsolete]` on shortcut constructors |
| `GreenSwamp.Alpaca.MountControl/MountInstance.cs` | Remove `SkyQueue.RegisterInstance()` from `MountStart()` |
| `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs` | Audit `SkySystem.*` remaining callsites (§11 Risk) |

> **Current state (March 2026):** Instance-aware overloads `SkyTasks(MountTaskName, MountInstance)`,
> `SimTasks(MountTaskName, MountInstance)`, and `AxesStopValidate(MountInstance)` already exist in
> `SkyServer.Core.cs` with correct injection constructors. The legacy single-argument static overloads
> are `#pragma warning disable CS0618`-suppressed bridges. Tasks 2.2–2.4 cover migrating the remaining
> callers (`CommandStrings.cs`, `SkySettingsInstance.cs`) to the instance-aware overloads, then
> deleting the legacy static overloads and their `#pragma` blocks.
> Task 2.5 (`[Obsolete]` marking on shortcut constructors) is **already complete**.

---

### Task 2.1 — Audit all command construction call sites

Before editing, enumerate every location that constructs a `SkyXxx` or `CmdXxx` command:

```
code_search ["new Sky", "new Cmd"]
```

Expected locations:
- `SkyServer.Core.cs` — `SkyTasks()`, `SimTasks()`
- `SkyServer.TelescopeAPI.cs` — pulse guide, slew initiation, axis stop
- Anywhere else that constructs commands outside the main task loops

Document the full list before starting Task 2.2.

---

### Task 2.2 — Update `SkyTasks()` call sites

**File:** `SkyServer.Core.cs`

For every `new SkyXxx(id, ...)` that uses the static shortcut constructor, change to the injection form. Compute `q` once per `SkyTasks()` invocation:

```csharp
var q = _defaultInstance!.SkyQueueInstance;

// BEFORE (static shortcut — always routes to SkyQueue._instance):
_ = new SkyAxisSlew(SkyQueue.NewId, Axis.Axis1, rate);

// AFTER (injection form — routes to this instance's queue and ISerialPort):
_ = new SkyAxisSlew(q.NewId, q, Axis.Axis1, rate);
```

Apply to every `new SkyXxx(...)` call in `SkyTasks()`.

---

### Task 2.3 — Update `SimTasks()` call sites

**File:** `SkyServer.Core.cs`

Identical pattern — use `MountQueueInstance` and `ICommandQueue<Actions>`:

```csharp
var q = _defaultInstance!.MountQueueInstance;

// BEFORE:
_ = new CmdAxisPulse(0, Axis.Axis2, decGuideRate, duration, token);

// AFTER:
_ = new CmdAxisPulse(q.NewId, q, Axis.Axis2, decGuideRate, duration, token);
```

---

### Task 2.4 — Update remaining direct command call sites

**File:** `SkyServer.TelescopeAPI.cs` (and any others found in Task 2.1)

Apply the same injection constructor pattern to pulse guide, slew initiation, and any other direct command construction outside the main task loops.

---

### Task 2.5 — Mark static shortcut constructors `[Obsolete]`

**Files:** `SkyCommands.cs`, `Simulator/Commands.cs`

Once all production call sites have been updated in Tasks 2.2–2.4, mark every static shortcut constructor:

```csharp
// In SkyCommands.cs — apply to ALL shortcut constructors:
[Obsolete("Use the ICommandQueue<SkyWatcher> injection constructor. " +
          "Static shortcut will be removed when SkyQueue facade is retired.")]
public SkyAxisSlew(long id, Axis axis, double rate)
    : this(id, SkyQueue.Instance, axis, rate) { }
```

```csharp
// In Simulator/Commands.cs — apply to ALL shortcut constructors:
[Obsolete("Use the ICommandQueue<Actions> injection constructor. " +
          "Static shortcut will be removed when MountQueue facade is retired.")]
public CmdAxisPulse(long id, Axis axis, double rate, TimeSpan duration, CancellationToken token)
    : this(id, MountQueue.Instance, axis, rate, duration, token) { }
```

**Verification:** After marking `[Obsolete]`, run a build. Any remaining production code that still calls a shortcut constructor will generate a warning, acting as a safety net for missed call sites.

---

### Task 2.6 — Remove `SkyQueue.RegisterInstance()` from `MountInstance.MountStart()`

**File:** `MountInstance.cs`

The queue is already owned by the instance and accessed directly via `SkyQueueInstance`. The `RegisterInstance()` call with the static facade is no longer needed. Remove it.

---

### Task 2.7 — Audit `SkyCommands.cs` and `SkyWatcher.cs` for `SkySystem.*`

Per §11 Risk Notes in the architecture document:
- `SkyCommands.cs` lines ~164–174 may reference `SkySystem.ConnectSerial` / `SkySystem.Serial`
- `SkyWatcher.cs` lines ~143–151 may do the same

Replace any direct `SkySystem.Serial` read with access through the owning `MountInstance`. Add an `internal ISerialPort? Serial => _serial;` property to `MountInstance` if needed to expose the port for these callers.

---

### Task 2.8 — Build verify + dual smoke test

```
run_build
// Verify: zero [Obsolete] warnings from SkyTasks()/SimTasks()
// ConformU full suite against slot 0
// Smoke test: launch two simulator MountInstances simultaneously
//   → each should operate independently with no cross-routing
```

**Accept criteria:**
- Zero new build errors
- Zero unexpected `[Obsolete]` warnings (only warnings should be from shortcut ctors, not from callers)
- Two simulators run concurrently without interfering with each other's command queues

---

## 5. Phase 3 — Configuration & UI

**Goal:** Per-device serial settings configurable in `appsettings.json`. Blazor combined status panel updated.

**Estimated effort:** ~1 day + design time for Blazor component

### Files Modified / Created

| File | Change |
|---|---|
| `GreenSwamp.Alpaca.Server/appsettings.json` | Add `SerialSettings` per `AlpacaDevices` entry |
| `GreenSwamp.Alpaca.Settings/Models/` | New `SerialSettingsModel.cs` (or equivalent location) |
| `GreenSwamp.Alpaca.Server/Services/UnifiedDeviceRegistry.cs` | Read serial settings in `InitializeReservedSlots()` |
| `GreenSwamp.Alpaca.Server/Program.cs` | Wire `SerialSettings` into device registration |
| Blazor connection status component | Update to show port, baud rate, open/closed |

---

### Task 3.1 — Define `SerialSettingsModel`

Create a POCO in the settings models project (or `Shared`) to match the JSON schema:

```csharp
public class SerialSettingsModel
{
    public string Port        { get; set; } = "COM3";
    public int    BaudRate    { get; set; } = 9600;
    public int    DataBits    { get; set; } = 8;
    public string Handshake   { get; set; } = "None";
    public int    ReadTimeout { get; set; } = 5000;
    public bool   DtrEnable   { get; set; } = false;
    public bool   RtsEnable   { get; set; } = false;
}
```

---

### Task 3.2 — Extend `appsettings.json`

Add a `SerialSettings` block to each entry in `AlpacaDevices`:

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
    }
  ]
}
```

The `SerialSettings` values override what the user may have saved in `appsettings.user.json` at startup. The user can edit these values through the Blazor settings page (Task 3.4) and they are persisted by `IVersionedSettingsService`.

---

### Task 3.3 — Update `UnifiedDeviceRegistry.InitializeReservedSlots()`

Read `SerialSettings` for each device slot and apply them to the `SkySettingsInstance` before the device is started. This replaces the single-device path where `SkySystem.Initialize(_settings)` was called once:

```csharp
// Example — adapt to the actual IConfiguration pattern in use:
var serialSettings = config
    .GetSection($"AlpacaDevices:{i}:SerialSettings")
    .Get<SerialSettingsModel>();

if (serialSettings != null)
{
    instance.Settings.Port        = serialSettings.Port;
    instance.Settings.BaudRate    = Enum.Parse<SerialSpeed>(serialSettings.BaudRate.ToString());
    instance.Settings.DataBits    = serialSettings.DataBits;
    instance.Settings.HandShake   = Enum.Parse<Handshake>(serialSettings.Handshake);
    instance.Settings.ReadTimeout = serialSettings.ReadTimeout;
    instance.Settings.DtrEnable   = serialSettings.DtrEnable;
    instance.Settings.RtsEnable   = serialSettings.RtsEnable;
}
```

> **Note:** Verify the `SerialSpeed` and `Handshake` enum value names against `SkySettingsInstance` before coding.

---

### Task 3.4 — Update Blazor combined connection status panel

In the existing connection status component, surface per-device serial state. **One panel only** — not duplicated per device for the near term.

Suggested displayed values:
- Device number and name
- Port (`COM3` / UDP address)
- Baud rate
- Port open/closed state (from `MountInstance._serial?.IsOpen`)
- Error message if `_serialError` is set

The `MountInstance` will need `internal` or `public` read-only properties to expose these values to the Blazor layer (following the same pattern as other instance state already exposed via `SkyServer` static properties).

---

### Task 3.5 — Full integration test

```
run_build
// ConformU full suite against slot 0
// Settings round-trip: change Port in Blazor UI → restart → verify Port restored
// Physical dual-device test (or two simulators with distinct ProfileNames)
//   → verify each device reads its own SerialSettings independently
```

---

## 6. Integration Test Checkpoints

| After | Test | Pass Criteria |
|---|---|---|
| Phase 1 | Connect, slew, track, disconnect — single telescope | No regression; port opens/closes cleanly |
| Phase 1 | Two simulators connected simultaneously | Each shows independent connected state; one disconnect does not affect the other |
| Phase 2 | SkyTasks commands reach correct device | No cross-routing; dual simulator smoke test passes |
| Phase 2 | `[Obsolete]` audit | Zero `[Obsolete]` warnings emitted from production code paths |
| Phase 3 | Settings round-trip | Port/baud saved and restored per device; each device config distinct |
| Phase 3 | ConformU full suite | No new conformance failures vs Phase 2 baseline |

---

## 7. Key Invariants to Preserve

| Invariant | Where enforced |
|---|---|
| One port opened per `MountInstance` | `OpenSerial()` calls `CloseSerial()` first |
| `IOException` on duplicate COM port surfaced clearly | `OpenSerial()` catch block |
| Reference count: port closes only when last ASCOM client disconnects | `_connectStates.IsEmpty` check in `SetConnected()` |
| `SkyQueueImplementation.Start(ISerialPort, ...)` receives the instance-owned port | `MountStart()` passes `_serial` directly |
| Static shortcut constructors remain compilable (not removed) until `SkyQueue` is retired | `[Obsolete]` attribute only — no deletion in this work |

---

## 8. Files to Create

| File | Purpose |
|---|---|
| `GreenSwamp.Alpaca.Settings/Models/SerialSettingsModel.cs` (or equivalent) | Typed POCO for `appsettings.json SerialSettings` block |

All other changes are modifications to existing files.

---

*End of document.*
