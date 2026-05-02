# Counted Connection Management Design
# GreenSwamp Alpaca - Mount Connection Lifecycle

**Document date:** 2026-04-30 16:42
**Author:** Andy (assisted by GitHub Copilot)
**Status:** DRAFT v2 - revised after full codebase inspection; design decisions recorded

---

## Revision History

| Version | Date | Change |
|---------|------|--------|
| v1 | 2026-04-30 15:30 | Initial draft |
| v2 | 2026-04-30 16:42 | Corrected REST endpoint findings; `AlpacaController` already has `connect`/`disconnect`; updated gap analysis; incorporated Andy's design decisions |

---

## 1. Background: ASCOM ITelescope V4 Connection Model

### 1.1 Relevant ASCOM Interface Members (ITelescope V4)

| Member | Type | Description |
|--------|------|-------------|
| `Connect()` | `async void` | **Mandatory.** Non-blocking. On return, `Connecting` must be `True` unless already connected. Completion is indicated when `Connecting` becomes `False`. |
| `Disconnect()` | `async void` | **Mandatory.** Non-blocking. On return, `Connecting` must be `True` unless already disconnected. Completion is indicated when `Connecting` becomes `False`. |
| `Connected` | `bool` R/W | **Mandatory.** Read reports current hardware connection state. Write (set `True`/`False`) is **deprecated** in V4 but must still be implemented for backward compatibility. |
| `Connecting` | `bool` R/O | **Mandatory.** `True` while an async `Connect()` or `Disconnect()` operation is in progress. |

### 1.2 ASCOM Hub (Multi-Client) Requirement

The ASCOM V4 spec states:

> *"For a hub this means that `Connected` will be `True` when the first driver connects
> and will only be set to `False` when all drivers have disconnected."*

This is the **counted connection pattern** - mount hardware stays open while at least
one Alpaca client holds a live connection.

---

## 2. Alpaca REST Protocol: Client Identity

Every Alpaca REST request carries two identity fields:

| Form/Query Param | Type | Purpose |
|-----------------|------|---------|
| `ClientID` | `uint32` | **Persistent** identifier allocated by the client application at startup. Identifies **which application** is making the request. Range 0–4294967295. |
| `ClientTransactionID` | `uint32` | **Per-request** monotonically increasing counter. Identifies **which request** within a client session. |

The **`ClientID`** is the correct key for tracking whether a particular Alpaca client
application holds an active connection.

---

## 3. Full Layer Stack

```
Alpaca REST Client (e.g. NINA, Stellarium)
	  |  PUT /api/v1/telescope/0/connect  {ClientID=1234, ClientTransactionID=7}
	  v
AlpacaController.Connect()           (ASCOM.Alpaca.Razor/Controllers/AlpacaController.cs)
	  |  ProcessRequest(() => GetDevice(DeviceNumber).Connect(), ..., ClientID, ...)
	  |  ClientID is passed to ProcessRequest for logging only - NOT forwarded to driver
	  v
BaseController.ExecuteRequest<>()    (ASCOM.Alpaca.Razor/Controllers/BaseController.cs)
	  |  Logs ClientID; validates Alpaca protocol; checks DeviceCannotAcceptOperation()
	  |  Calls operation.Invoke() -> GetDevice(DeviceNumber).Connect()
	  v
Telescope.Connect()                  (GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs)
	  |  Connected = true;
	  |  -> _mount.SetConnected(_objectId, true)    <- _objectId is ALWAYS 0
	  v
Mount.SetConnected(id: 0, value: true)  (GreenSwamp.Alpaca.MountControl/Mount.Serial.cs)
	  |  _connectStates.TryAdd(0, true)   <- all clients share key 0
	  v
Mount hardware: serial/UDP port
```

---

## 4. Current Code Analysis

### 4.1 REST Layer - `AlpacaController.cs` (lines 32-81)

**Corrected finding from v1:** The `PUT /connect` and `PUT /disconnect` REST endpoints
**already exist** in `AlpacaController.cs`. They are shared common endpoints inherited
by all device controllers.

```csharp
// AlpacaController.cs line 35
[HttpPut]
[Route("{DeviceNumber}/connect")]
public ActionResult<Response> Connect(
	uint DeviceNumber,
	uint ClientID = 0,
	uint ClientTransactionID = 0)
{
	return ProcessRequest(() => GetDevice(DeviceNumber).Connect(),
		DeviceManager.ServerTransactionID, ClientID, ClientTransactionID);
}

// AlpacaController.cs line 55
[HttpPut]
[Route("{DeviceNumber}/disconnect")]
public ActionResult<Response> Disconnect(
	uint DeviceNumber,
	uint ClientID = 0,
	uint ClientTransactionID = 0)
{
	return ProcessRequest(() => GetDevice(DeviceNumber).Disconnect(),
		DeviceManager.ServerTransactionID, ClientID, ClientTransactionID);
}

// AlpacaController.cs line 75
[HttpGet]
[Route("{DeviceNumber}/connecting")]
public ActionResult<BoolResponse> Connecting(...)
{
	return ProcessRequest(() => GetDevice(DeviceNumber).Connecting, ...);
}

// AlpacaController.cs line 267 - legacy V3 write
[HttpPut]
[Route("{DeviceNumber}/connected")]
public ActionResult<Response> Connected(..., bool Connected, ...)
{
	if (Connected || !DeviceManager.Configuration.PreventRemoteDisconnects)
		return ProcessRequest(() => { GetDevice(DeviceNumber).Connected = Connected; }, ...);
	return ProcessRequest(() => { }, ...);
}
```

**Key observation:** `ClientID` is received by both `Connect()` and `Disconnect()` in
the REST layer and is passed into `ProcessRequest()`. However, inside `ProcessRequest`
and `ExecuteRequest`, `ClientID` is used **only for logging and response metadata**.
It is captured in the lambda closure for `LogAPICall` but is **never passed into the
driver method** (`GetDevice(DeviceNumber).Connect()`). The driver `Connect()` and
`Disconnect()` methods take no parameters.

### 4.2 `BaseController.cs` - `DeviceCannotAcceptOperation()` (lines 105-127)

This guard method prevents operations on a device that is not yet connected. The
allowed-when-not-connected list is:

```csharp
List<string> allowedList = ["connect", "connected", "connecting",
	"interfaceversion", "driverversion", "driverinfo", "name"];
```

`"connect"` and `"disconnect"` are handled as follows:
- `"connect"` is in the allowed list → always passes through even when not connected. ✅
- `"disconnect"` is **not** in the allowed list → blocked when device is not connected. ⚠️

This is logically correct (`disconnect` when already disconnected should be a no-op or
guarded), but it means a client that calls `disconnect` on an already-disconnected device
will receive a `NotConnectedException` response from the REST layer rather than a clean
no-op. This may need to be addressed.

### 4.3 `BaseController.cs` - `ExecuteRequest<>()` (lines 147-191)

The core request handler. `ClientID` is available as a `ref uint` parameter. It flows
through `LogAPICall`, `BadRequestAlpacaProtocol` (for casing validation), and the
`clientTransactionID`/`clientID` of the response. It is **never forwarded** to the
operation lambda.

### 4.4 `Telescope.cs` - `Connect()`, `Disconnect()`, `Connected`

```csharp
private readonly long _objectId;   // NEVER ASSIGNED - defaults to 0 for every instance

public void Connect()     { Connected = true; }
public void Disconnect()  { Connected = false; }
public bool Connected
{
	get => _mount.IsConnected;
	set => _mount.SetConnected(_objectId, value);  // _objectId is always 0
}
```

**Critical finding:** `_objectId` is declared `readonly long` and is never assigned in
the constructor. Its value is always `0` (C# default for `long`). Every `Telescope`
instance - and therefore every Alpaca client calling `Connect()` - uses connection key
`0` inside `_connectStates`.

### 4.5 `Mount.Serial.cs` - `SetConnected(long id, bool value)` (lines 47-137)

The counted connection engine. Logic is correct in isolation:

- **First add** (`_connectStates` was empty): sets `Connecting = true`, calls
  `MountStart()` in a background `Task.Run`, sets `Connecting = false` on completion.
- **Subsequent add** (`_connectStates` already had entries): no-op on hardware; just
  records the additional key.
- **Non-last remove** (`_connectStates` still has entries after remove): no hardware
  change.
- **Last remove** (`_connectStates` is now empty): calls `MountStop()`.

This logic is correct **if and only if** each caller supplies a distinct `id`. With
`_objectId=0` for all callers, it is broken (see §5).

### 4.6 `Mount.cs` - Connection State (lines 175-220, 507)

```csharp
private readonly ConcurrentDictionary<long, bool> _connectStates = new();
public bool IsConnected   => !_connectStates.IsEmpty;
public bool Connecting    { get; private set; }
```

The data structure and semantics are correct.

---

## 5. Current Behaviour vs. Expected Behaviour

| Scenario | Expected | Actual |
|----------|----------|--------|
| Client A connects (ClientID=100) | Add entry for key 100. Mount starts. | Adds entry for key 0. Mount starts. Works by accident. |
| Client B connects (ClientID=200) | Add entry for key 200. Mount already running. | `TryAdd(0, true)` - key 0 already exists, silently ignored. Mount stays up. |
| Client A disconnects (ClientID=100) | Remove key 100. Key 200 still present. Mount keeps running. | Removes key 0. `_connectStates` becomes empty. **`MountStop()` called.** ❌ BROKEN |
| Client B disconnects (ClientID=200) | Remove key 200. Count=0. `MountStop()`. | `TryRemove(0)` on already-empty dict. No-op. Mount already stopped. |
| Blazor UI connects | Add entry for a reserved server-side key. | Same as above - uses key 0. |

**The counted connection is broken** because `_objectId=0` for every `Telescope`
instance, making all clients share one slot. The first disconnect of **any** client tears
down the hardware for all remaining clients.

---

## 6. Root Cause Summary

There is a **two-part plumbing gap**:

1. **REST → Driver:** `ClientID` is available in `AlpacaController.Connect()` but the
   lambda `() => GetDevice(DeviceNumber).Connect()` does not pass it. The ASCOM
   interface `IAscomDeviceV2.Connect()` takes no parameters, so `ClientID` cannot flow
   through the interface without additional mechanism.

2. **Driver → Mount:** `Telescope.Connect()` calls `_mount.SetConnected(_objectId, value)`
   where `_objectId` is always `0`. The `_objectId` field is never initialised.

Both gaps must be closed for counted connection management to work.

---

## 7. Proposed Design

### 7.1 Andy's Decisions (Recorded)

| Question | Decision |
|----------|----------|
| Should `Disconnect()` respect the counted connection or tear down immediately? | **Respect the count.** Only call `MountStop()` when the last client disconnects. |
| What connection key should the Blazor server UI use? | **A dedicated reserved server-side key** that cannot clash with any Alpaca `ClientID`. |
| Should `MountStop()` only be called when the last client disconnects? | **Yes.** Mount hardware is only stopped when `_connectStates` becomes empty. |
| What happens on server restart? | **Mount hardware is released by process exit.** In-memory `_connectStates` is discarded; no persisted state needed. |

### 7.2 Connection Key Strategy

| Key | Value | Source |
|-----|-------|--------|
| Alpaca V4 client (NINA, Stellarium, etc.) | `(long)ClientID` | `ClientID` uint32 from REST form body, cast to `long`. Range: 0–4294967295. |
| Legacy V3 client using `PUT /connected=true` | `0L` | `ClientID` from the V3 request (which may be 0), cast to `long`. |
| Blazor server UI | `long.MinValue` (`-9223372036854775808`) | A reserved sentinel that can never clash with any `uint32`-derived key. |

Using `(long)ClientID` for Alpaca clients and `long.MinValue` for the Blazor UI means
there is zero risk of collision across the full key space.

### 7.3 How to Forward `ClientID` to the Driver Without Changing the ASCOM Interface

The ASCOM interface `IAscomDeviceV2.Connect()` and `IAscomDeviceV2.Disconnect()` take
no parameters and must not be changed. `ClientID` must be delivered to `Telescope` by
a side-channel mechanism.

**Recommended approach: thread-local / AsyncLocal context**

Before the lambda in `AlpacaController.Connect()` calls `GetDevice().Connect()`, store
the `ClientID` in an `AsyncLocal<uint>` or a simple static thread context accessor.
The `Telescope.Connect()` method reads this context to obtain the key to pass to
`SetConnected`.

```csharp
// Proposed static context carrier (new class, one line)
internal static class AlpacaRequestContext
{
	internal static readonly AsyncLocal<uint> ClientID = new();
}
```

`AlpacaController.Connect()` sets the context just before invoking the operation:

```csharp
// Inside the lambda or before ProcessRequest
AlpacaRequestContext.ClientID.Value = ClientID;
return ProcessRequest(() => GetDevice(DeviceNumber).Connect(), ...);
```

`Telescope.Connect()` reads the context:

```csharp
public void Connect()
{
	long key = (long)AlpacaRequestContext.ClientID.Value;
	_mount.SetConnected(key, true);
}
```

**Alternative approach: non-interface overloads on `Telescope`**

Add `Connect(uint clientId)` / `Disconnect(uint clientId)` overloads directly on the
`Telescope` class (not on the interface). `AlpacaController` casts `GetDevice()` to
`Telescope` (or to a GreenSwamp-specific interface) to call the extended overload.
This is simpler but requires the REST layer to know about the concrete driver type or
a non-ASCOM extension interface.

**Recommendation:** Use the `AsyncLocal` context carrier approach. It is clean, does not
require any change to the ASCOM interface or to `BaseController`, and is safe under async
execution because `AsyncLocal<T>` follows the execution context through `await` points.

### 7.4 Blazor UI Connection Key

The Blazor server-side UI that connects/disconnects the mount (via Blazor component
event handlers, not via REST) should call `_mount.SetConnected(long.MinValue, true/false)`
directly, bypassing `Telescope.Connect()` entirely, or call a dedicated method on the
mount service that uses the reserved key.

This means a Blazor "Disconnect" button will only remove the `long.MinValue` slot from
`_connectStates`. If Alpaca clients are still connected the mount stays up. This is the
correct ASCOM hub behaviour.

### 7.5 `PUT /disconnect` Guard in `DeviceCannotAcceptOperation()`

Currently `"disconnect"` is **not** in the `allowedList`, so calling `PUT /disconnect`
when the device is not connected returns `NotConnectedException`. Options:

1. Add `"disconnect"` to the `allowedList` so it is a clean no-op when already disconnected.
2. Leave the current behaviour and document that V4 clients should check `Connected`
   before calling `Disconnect()`.

**Recommendation:** Add `"disconnect"` to the allowed list. A well-behaved hub should
accept `Disconnect()` at any time and simply return success if already disconnected.
This matches the ASCOM V4 spec intent.

---

## 8. State Machine: Correct Counted Connection Flow

```
[Initial state]
Disconnected
	|
	| First client Connect(ClientID_A)
	|   -> AlpacaRequestContext.ClientID.Value = A
	|   -> Telescope.Connect() -> SetConnected(A, true)
	|   -> _connectStates = {A}, Connecting = true
	v
Connecting
	|
	| MountStart() completes (background task)
	|   -> Connecting = false
	v
Connected
	|
	| Second client Connect(ClientID_B)
	|   -> SetConnected(B, true)
	|   -> _connectStates = {A, B}, IsMountRunning=true -> no second MountStart
	v
Connected (_connectStates = {A, B})
	|
	| Client A Disconnect(ClientID_A)
	|   -> SetConnected(A, false)
	|   -> _connectStates = {B}, count > 0
	|   -> MountStop() NOT called
	v
Connected (_connectStates = {B})
	|
	| Blazor UI Connect
	|   -> SetConnected(long.MinValue, true)
	|   -> _connectStates = {B, long.MinValue}
	v
Connected (_connectStates = {B, long.MinValue})
	|
	| Client B Disconnect(ClientID_B)
	|   -> SetConnected(B, false)
	|   -> _connectStates = {long.MinValue}
	|   -> MountStop() NOT called
	v
Connected (_connectStates = {long.MinValue}) - Blazor UI still holding
	|
	| Blazor UI Disconnect
	|   -> SetConnected(long.MinValue, false)
	|   -> _connectStates = {}, count == 0
	|   -> MountStop() called
	v
Disconnected
```

---

## 9. Data Flow Diagram: Proposed Design

```
REST PUT /api/v1/telescope/0/connect
  Form: ClientID=1234, ClientTransactionID=42
		|
		v
AlpacaController.Connect(DeviceNumber=0, ClientID=1234)
		| AlpacaRequestContext.ClientID.Value = 1234   <- NEW: set context
		| ProcessRequest(() => GetDevice(0).Connect(), ..., 1234, ...)
		v
BaseController.ExecuteRequest<>()
		| LogAPICall(..., clientID=1234, ...)
		| DeviceCannotAcceptOperation() -> "connect" is in allowedList -> pass
		| operation.Invoke() -> GetDevice(0).Connect()
		v
Telescope.Connect()
		| long key = (long)AlpacaRequestContext.ClientID.Value   // key = 1234
		| _mount.SetConnected(key: 1234, value: true)
		v
Mount.SetConnected(id=1234, value=true)
		| _connectStates.TryAdd(1234, true)
		| _connectStates = { 1234: true }
		| if first entry -> MountStart() background task
		v
Mount hardware: OPEN. IsConnected = true.

----

REST PUT /api/v1/telescope/0/connect
  Form: ClientID=5678, ClientTransactionID=1
		...
		| _connectStates.TryAdd(5678, true)
		| _connectStates = { 1234: true, 5678: true }
		| IsMountRunning == true -> no second MountStart
		v
Mount hardware: STILL OPEN (no change)  OK

----

REST PUT /api/v1/telescope/0/disconnect
  Form: ClientID=1234, ClientTransactionID=99
		...
		| AlpacaRequestContext.ClientID.Value = 1234
		| Telescope.Disconnect() -> SetConnected(1234, false)
		| _connectStates.TryRemove(1234)
		| _connectStates = { 5678: true }  <- NOT empty
		| MountStop() NOT called
		v
Mount hardware: STILL OPEN  OK

----

REST PUT /api/v1/telescope/0/disconnect
  Form: ClientID=5678, ClientTransactionID=12
		...
		| SetConnected(5678, false)
		| _connectStates.TryRemove(5678)
		| _connectStates = {}  <- EMPTY
		| MountStop() called
		v
Mount hardware: CLOSED  OK

----

Blazor UI "Connect" button click
		| No REST involved; direct call in Blazor component
		| _mountService.SetConnected(long.MinValue, true)
		| _connectStates = { long.MinValue: true }
		| First entry -> MountStart()
		v
Mount hardware: OPEN  OK

Blazor UI "Disconnect" button click
		| _mountService.SetConnected(long.MinValue, false)
		| _connectStates.TryRemove(long.MinValue)
		| if empty -> MountStop()
		v
Mount hardware: CLOSED only if no Alpaca clients remain  OK
```

---

## 10. Files to Create or Modify

| File | Change | Scope |
|------|--------|-------|
| `ASCOM.Alpaca.Razor/Controllers/AlpacaController.cs` | **Modify** | In `Connect()` and `Disconnect()` lambdas: set `AlpacaRequestContext.ClientID.Value = ClientID` before calling the operation. |
| `ASCOM.Alpaca.Razor/Controllers/BaseController.cs` | **Modify (minor)** | Add `"disconnect"` to the `allowedList` in `DeviceCannotAcceptOperation()` so `PUT /disconnect` succeeds when already disconnected. |
| `ASCOM.Alpaca.Razor/AlpacaRequestContext.cs` | **Create (new, tiny)** | New static class with `AsyncLocal<uint> ClientID`. |
| `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` | **Modify** | `Connect()`: read `AlpacaRequestContext.ClientID.Value`, cast to `long`, pass to `SetConnected`. `Disconnect()`: same. Fix or remove unused `_objectId` field. |
| `GreenSwamp.Alpaca.MountControl/Mount.Serial.cs` | **No change** | `SetConnected(long id, bool value)` logic is already correct. |
| `GreenSwamp.Alpaca.MountControl/Mount.cs` | **No change** | `_connectStates`, `IsConnected`, `Connecting` are already correct. |
| Blazor UI component(s) that call mount connect/disconnect | **Modify** | Change to call `SetConnected(long.MinValue, true/false)` instead of going through `Telescope.Connect()`. |

---

## 11. Interface Contract Constraints

The ASCOM interface `IAscomDeviceV2.Connect()` and `IAscomDeviceV2.Disconnect()` take
no parameters and **must not be changed**. This is a hard constraint imposed by the
ASCOM standard.

The `AsyncLocal<uint>` context carrier is the cleanest way to thread `ClientID` through
the interface without modifying it.

The legacy `PUT /connected` write path (`AlpacaController.Connected` setter) does not
set `AlpacaRequestContext.ClientID` and uses `ClientID=0` implicitly, which maps to
`long key = 0L` in `Telescope.Connected.set`. This gives V3 legacy clients a single
shared slot (key=0), which is correct hub behaviour - V3 clients do not support
multi-client counted connections and share one anonymous slot.

---

## 12. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `AsyncLocal` context not set before `Connect()` is called | Low | Key defaults to 0 (V3 anonymous slot); V4 client silently shares it | Add a guard in `Telescope.Connect()` - log a warning if context is 0 for a V4 call path |
| Orphaned connections after client crash | Medium | Mount stays connected indefinitely | Add Blazor UI force-disconnect (Option D); optional inactivity timeout later |
| `long.MinValue` Blazor key clashes with a `uint32` ClientID cast to `long` | None | `uint32` max is `4294967295` = `0x00000000FFFFFFFF`; `long.MinValue` = `0x8000000000000000`; no overlap | Inherent in key space design |
| `PUT /disconnect` rejected when device not connected (current behaviour) | Medium | Well-behaved V4 clients get `NotConnectedException` | Add `"disconnect"` to `allowedList` in `DeviceCannotAcceptOperation()` |
| Race between `MountStop()` and a concurrent `Connect()` | Low | Already handled by `Connecting` flag and `IsMountRunning` check | No change needed |
| `_objectId` removal breaks a log string | Low | `_objectId` may appear in a logging call | Search for usages before removal; safe to replace with a constant or remove entirely |

---

## 13. Summary of Findings (Corrected from v1)

| Finding | Severity | File | Line(s) |
|---------|----------|------|---------|
| `PUT /connect` and `PUT /disconnect` endpoints **already exist** in `AlpacaController.cs` | ✅ OK (v1 was wrong) | `AlpacaController.cs` | 35, 55 |
| `GET /connecting` endpoint exists and is wired correctly | ✅ OK | `AlpacaController.cs` | 75 |
| `ClientID` is received at REST layer but not forwarded to driver | **HIGH** | `AlpacaController.cs` | 40, 60 |
| `_objectId` is always `0` - all clients share one connection slot | **HIGH** | `Telescope.cs` | 25 |
| `SetConnected()` logic is correct but broken by the `_objectId=0` input | Medium | `Mount.Serial.cs` | 47 |
| `"disconnect"` missing from `DeviceCannotAcceptOperation()` allowed list | Medium | `BaseController.cs` | 124 |
| `Connecting` flag and async background task pattern are correct | ✅ OK | `Mount.Serial.cs` | 51–136 |
| `IsConnected => !_connectStates.IsEmpty` is correct | ✅ OK | `Mount.cs` | 507 |
| `ConcurrentDictionary<long,bool>` as the counted-connection store is correct | ✅ OK | `Mount.cs` | 181 |

---

## 14. Minimal Change Surface

The fix is small. Only **four touchpoints** are needed:

1. **New file** `AlpacaRequestContext.cs` (3 lines of logic): `AsyncLocal<uint> ClientID`.
2. **`AlpacaController.cs`** (2 lines): Set context in `Connect()` and `Disconnect()` before `ProcessRequest`.
3. **`Telescope.cs`** (3 lines): Read context in `Connect()` and `Disconnect()`; remove or fix `_objectId`.
4. **`BaseController.cs`** (1 word): Add `"disconnect"` to the allowed list.

`Mount.Serial.cs` and `Mount.cs` require **zero changes**.

---

*Document generated: 2026-04-30 16:42*
*Repository: https://github.com/Principia4834/GreenSwampAlpaca | Branch: master*
