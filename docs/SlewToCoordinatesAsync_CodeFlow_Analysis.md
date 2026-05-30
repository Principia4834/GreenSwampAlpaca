# SlewToCoordinatesAsync Code Flow Analysis
## GreenSwamp Alpaca — Polar Alignment / SkyWatcher Mount
### Precondition: Tracking=true, no active slew or guide operations
**Document date:** 2026-05-28 15:29  
**Revision:** Full code-verified audit — all claims checked against source

---

## 1. Entry Point — `Telescope.SlewToCoordinatesAsync` (ASCOM HTTP Thread)

The call originates on the ASP.NET Core request-handling thread (a ThreadPool thread managed
by Kestrel).  This thread is the **"ASCOM caller thread"** throughout this document.

```
Telescope.SlewToCoordinatesAsync(RA, Dec)   [ASCOM caller thread]
```

### Step-by-step

| # | Code location | What happens |
|---|---------------|--------------|
| 1 | `Telescope.cs` | Validation guards run synchronously on the ASCOM caller thread:<br>• `CheckCapability` — verifies `CanSlewAsync`<br>• `CheckRange` — RA ∈ [0,24], Dec ∈ [−90,90]<br>• `CheckParked` — throws `ParkedException` if `AtPark == true`<br>• `CheckReachable(RightAscension, Declination, SlewRaDec)` — called at line 1517 with the **raw RA/Dec passed to `SlewToCoordinatesAsync`**, before `CoordTypeToInternal` is called. The guard body is **only entered for `AlignmentMode.Polar`** (`if (_mount.Settings.AlignmentMode != AlignmentMode.Polar \|\| _mount.IsTargetReachable(...)) return;`). For `GermanPolar`, `IsTargetReachable` early-returns `true` immediately. For `AltAz`, `CheckReachable` returns before calling `IsTargetReachable` because `!= Polar` is true. For **Polar**, `IsTargetReachable` falls through to `IsTargetWithinLimits(MapSlewTargetToAxes(target, SlewRaDec))` — a real hardware-limits check using `Axes.RaDecToAxesXy`. The precondition says this passes; if it failed it would throw `InvalidOperationException("SlewToCoordinates outside hardware limits")`. |
| 2 | `Telescope.cs` | Sets `TargetRightAscension` and `TargetDeclination` on the mount object |
| 3 | `Telescope.cs` | Calls `Transforms.CoordTypeToInternal(RightAscension, Declination)` to convert from the catalogue coordinate type (J2000, JNow, etc.) to internal RA/Dec angles `raDec`. Note: `CheckReachable` (step 1) used the **raw** values; the converted `raDec.X`/`raDec.Y` are what get passed to `SlewRaDecAsync`. |
| 4 | `Telescope.cs` | **Tracking guard** — `if(!Tracking) Tracking = true;`<br>Tracking is already `true` in the precondition, so this **no-ops** — `ApplyTrackingAndWait` is **not** called here. |
| 5 | `Telescope.cs` | Calls `_mount.SlewRaDecAsync(raDec.X, raDec.Y, tracking: true).GetAwaiter().GetResult()` |

> **Important:** `.GetAwaiter().GetResult()` **blocks the ASCOM caller thread** until the
> setup phase (≤ 950 ms) completes and `IsSlewing` has been set to `true`.  Movement
> continues in the background after the HTTP response is returned.

---

## 2. `Mount.SlewRaDecAsync` / `Mount.SlewAsync`

```
Mount.SlewRaDecAsync(ra, dec, tracking: true)       [ASCOM caller thread]
  └─ Mount.SlewAsync([ra, dec], SlewType.SlewRaDec, tracking: true)
	   └─ Mount.EnsureSlewController()               (lazy-init, first call only)
	   └─ new SlewOperation(this, target, SlewRaDec, trackingAfterSlew: true, rateRa, rateDec)
	   └─ _slewController.ExecuteSlewAsync(operation)
```

`SlewAsync` is `private async Task<SlewResult>` — it awaits `ExecuteSlewAsync` and returns
that result to the `.GetAwaiter().GetResult()` blocker in `Telescope.cs`.

---

## 3. `SlewController.ExecuteSlewAsync` — Setup Phase (< 950 ms)

Still on the **ASCOM caller thread** (the `await` has not yet yielded to a thread switch).

```
SlewController.ExecuteSlewAsync(operation)   [ASCOM caller thread]
```

### Substeps

| # | What happens | Tracking state |
|---|--------------|----------------|
| 3a | Acquires `_operationLock` (SemaphoreSlim, 1-concurrency) with up to 100 ms wait | — |
| 3b | Calls `SetupPhaseAsync(operation, ct)` | — |
| — | **Inside `SetupPhaseAsync`:** | |
| 3c | `CancelCurrentSlewAsync()` — no prior slew active, immediate return | — |
| 3d | Validates `operation.Mount.IsMountRunning == true` | — |
| 3e | `AxesStopValidate()` — only called if `_slewState != SlewNone`; in the precondition `_slewState == SlewNone` so this block is **skipped entirely** | — |
| 3f | Creates new `CancellationTokenSource` (`_currentOperationCts`) and assigns `_currentOperation = operation` | — |
| 3g | Calls **`operation.Prepare()`** ← **TRACKING DISABLED HERE** | ↓ see §4 |
| 3h | `SetupPhaseAsync` returns `SlewResult.Success()` | Tracking OFF |
| 3i | Back in `ExecuteSlewAsync`: sets `IsSlewing = true`, `CurrentSlewType = SlewRaDec`, `operation.Mount._slewState = SlewRaDec` | — |
| 3j | Fires off `_movementTask = Task.Run(async () => await ExecuteMovementAndCompletionAsync(operation, _currentOperationCts.Token), _currentOperationCts.Token)` ← **background thread starts** | — |
| 3k | Releases `_operationLock` in the `finally` block | — |
| 3l | Returns `SlewResult.Success()` to the awaiter in `SlewAsync`, which propagates to `GetAwaiter().GetResult()` in `Telescope.cs` | — |

At this point the **ASCOM caller thread is released** — the HTTP response goes back to the
ASCOM client with `IsSlewing = true`, and the client begins polling.

---

## 4. `SlewOperation.Prepare()` — Tracking Disabled (still Setup Phase)

```
SlewOperation.Prepare()   [ASCOM caller thread, inside SetupPhaseAsync]
```

| Action | Tracking state |
|--------|----------------|
| Captures `InitialRa`, `InitialDec`, `WasTracking = true` | Tracking ON |
| Calls **`Mount.ApplyTracking(false)`** | **Tracking OFF** |
| — `ApplyTrackingCore(false, waitForQueueCompletion: false)` | |
| — — `Tracking = false` | |
| — — `SkyPredictor.Reset()` | |
| — — `_isPulseGuidingRa = false`, `_isPulseGuidingDec = false` | |
| — — `TrackingMode = TrackingMode.Off` | |
| — — `SetTracking(waitForQueueCompletion: false)` — for SkyWatcher/Polar: sends **`SkyAxisSlew(Axis1, rate: 0)`** and **`SkyAxisSlew(Axis2, rate: 0)`** to the SkyQueue (fire-and-forget, no wait) | **Hardware tracking STOP** |
| For `SlewType.SlewRaDec`: calls `SendSlewBoundaryAck()` (blocks ≤ 500 ms for TrackingCommandProcessor ACK) | — |
| Calls **`SkyPredictor.Set(Target[0], Target[1], RateRa=0, RateDec=0)`** | — |

> **Tracking is disabled synchronously on the ASCOM caller thread before the HTTP response
> is returned.** The `SkyAxisSlew(rate=0)` commands are queued into SkyQueue but not
> awaited; the SkyQueue's serial worker (a dedicated background thread — see §7) processes
> them independently.

---

## 5. Background Movement Phase — `ExecuteMovementAndCompletionAsync`

```
Task.Run(ExecuteMovementAndCompletionAsync)  [ThreadPool background thread — "slew thread"]
```

### 5a. `MovementPhaseAsync`

Delegates to `operation.ExecuteMovementAsync(ct)`:

```csharp
MountType.SkyWatcher => await Task.Run(() => Mount.SkyGoTo(Target, trackingAfterSlew: true, SlewRaDec, ct), ct)
```

This spawns **another** `Task.Run` — the actual `SkyGoTo` blocking loop runs on a
**second ThreadPool thread** (called the **"goto thread"** below). The slew thread
`await`s its completion. The `Task.Run` is passed `_currentOperationCts.Token` as both the work and the task's cancellation token.

### 5b. `Mount.SkyGoTo` — Coarse Slew (goto thread, blocks with `Thread.Sleep`/`WaitOne`)

| Step | Hardware action | Tracking state |
|------|----------------|----------------|
| `SkyTasks(MountTaskName.StopAxes)` | Queues stop commands to SkyQueue | Still OFF |
| `MapSlewTargetToAxes(target, slewType)` → `skyTarget` | Converts RA/Dec to axis degrees for the coarse slew. Target is now internal values from `SlewRaDecAsync`. | — |
| `SkyAxisGoToTarget(Axis1, skyTarget[0])` | Queues Axis1 GoTo to SkyQueue (note: **`skyTarget`** not raw `target`) | — |
| `SkyAxisGoToTarget(Axis2, skyTarget[1])` | Queues Axis2 GoTo to SkyQueue | — |
| **Polling loop** (up to 240 s): | | |
| — `token.WaitHandle.WaitOne(250)` — **goto thread sleeps 250 ms** per iteration, checking `CancellationToken` | — |
| — `SkyIsAxisFullStop(Axis1)` — polls SkyQueue | — |
| — `SkyIsAxisFullStop(Axis2)` — polls SkyQueue | — |
| — Loops until both axes report FullStop | — |
| If `_slewSettleTime > 0`: `Thread.Sleep(settleMs)` — settle sleep happens **inside** the while loop, after both axes stop, before the loop breaks | — |
| `SkyPrecisionGoto(target, slewType, token)` — called only if `elapsedSeconds <= timer (240s)` | Fine correction | — |
| `SkyTasks(MountTaskName.StopAxes)` | Final stop after precision goto | — |

**`WaitUpdateMountPosition` is NOT called during the coarse slew loop.** The loop relies
on `SkyIsAxisFullStop` hardware queries only, with `token.WaitHandle.WaitOne(250)` between polls.

### 5c. `Mount.SkyPrecisionGoto` — Precision Slew (goto thread, iterative, max 5 tries)

For each iteration (up to 5):

| Step | Detail | WaitUpdateMountPosition? |
|------|--------|--------------------------|
| `WaitUpdateMountPosition(5000)` | **YES — called here** at the TOP of every loop iteration (including the final one that checks `maxTries >= 5`) | ✅ |
| — resets `_mountPositionUpdatedEvent`, queues `SkyUpdateSteps` to SkyQueue | | |
| — blocks the goto thread until the SkyQueue serial worker delivers new step counts and calls `ReceiveSteps()` → `_mountPositionUpdatedEvent.Set()` | | |
| — timeout = 5 000 ms; throws `TimeoutException` on timeout | | |
| `if (maxTries >= 5) break` — exit check happens **after** `WaitUpdateMountPosition` but **before** any correction commands | | |
| `maxTries++` — incremented after the exit check | | |
| Since `AlignmentMode == Polar` (not AltAz): the SkyPredictor re-prediction block is skipped entirely | n/a | |
| `MapSlewTargetToAxes(target, SlewRaDec)` → `skyTarget` using `Axes.RaDecToAxesXy` | | |
| Reads raw axis degrees via `GetRawDegrees()` | | |
| Computes `deltaDegree[0]` = `Range180(skyTarget[0] - rawPositions[0])`, `deltaDegree[1]` = `Range180(skyTarget[1] - rawPositions[1])` | | |
| `axis1AtTarget` and `axis2AtTarget` are **sticky** — once set `true` they never go back to `false` | | |
| If both axes within `GotoPrecision`: break | | |
| For Axis1 (SlewRaDec, any alignment): `predictor = 0.25` | | |
| Queues `SkyAxisGoToTarget(Axis1, skyTarget[0] + 0.25*delta[0])` only if `!axis1AtTarget` | | |
| Waits for Axis1 FullStop (inner loop, up to `loopTimer < 3000 ms`, `Thread.Sleep(30)`) | | |
| For Axis2 (Polar, SlewRaDec): `predictor = 0` — condition is `slewType == SlewRaDec && AlignmentMode != AltAz` → `0` for Polar | | |
| Queues `SkyAxisGoToTarget(Axis2, skyTarget[1] + 0*delta[1])` only if `!axis2AtTarget` | | |
| Waits for Axis2 FullStop (inner loop shares same `loopTimer < 3000 ms`, `Thread.Sleep(30)`) | | |
| Records `deltaTime = loopTimer.ElapsedMilliseconds` for next iteration's predictor offset | | |

After precision goto exits, control returns to `SkyGoTo`:

```
SkyTasks(MountTaskName.StopAxes)   — final stop after precision, queued to SkyQueue
SkyGoTo returns 0 (success)
```

---

## 6. Completion Phase — `operation.CompleteAsync` (slew thread)

`ExecuteMovementAndCompletionAsync` calls `operation.CompleteAsync(ct)` after movement:

### `CompleteRaDecSlewAsync`

```
if (settings.AlignmentMode != AlignmentMode.AltAz)
	return;   // ← For POLAR alignment this is an IMMEDIATE RETURN — no AltAz settle logic
```

Since the mount is **Polar**, `CompleteRaDecSlewAsync` returns immediately without any
AltAz-specific tracking settle.

### `MarkComplete(success: true)`

```
Mount._slewState = SlewType.SlewNone
Mount.ApplyTracking(TrackingAfterSlew: true)   ← TRACKING RE-ENABLED HERE
```

`ApplyTracking(true)` → `ApplyTrackingCore(true, waitForQueueCompletion: false)`:

| Action | Tracking state |
|--------|----------------|
| `Tracking = true` | ON |
| `InstanceSetTrackingMode()` — for Polar/NH: `TrackingMode = EqN`; for Polar/SH: `TrackingMode = EqS` | — |
| **AltAz branch skipped** (Polar alignment) | — |
| `SetTracking(waitForQueueCompletion: false)` — for SkyWatcher/Polar: `_skyTrackingRate = new Vector(siderealRate, 0)`, then sends **`SkyAxisSlew(Axis1, siderealRate)`** and **`SkyAxisSlew(Axis2, 0)`** to SkyQueue (fire-and-forget) | **Hardware tracking RESUME** |

> **Tracking is re-enabled on the slew thread (background), after physical movement is
> complete.** This happens asynchronously with respect to the ASCOM client, which learns
> of completion only by polling `Slewing` (which reads `IsSlewing`).

### `CleanupOperation` (SlewController, slew thread)

```
// CleanupOperation runs inside a lock(_stateLock):
IsSlewing = false            ← ASCOM client polls see Slewing=false
CurrentSlewType = SlewNone
_currentOperationCts?.Dispose()
_currentOperationCts = null
_currentOperation = null     // Note: not Disposed here — Dispose() called after the lock
_movementTask = null
// After lock:
operation.Dispose()          // SlewOperation.Dispose() is a no-op (no unmanaged resources)
```

---

## 7. Parallel and Concurrent Threads

The following threads operate concurrently throughout the slew:

| Thread | Created by | Role | Runs during slew? |
|--------|-----------|------|-------------------|
| **ASCOM caller thread** | Kestrel ThreadPool | Executes `SlewToCoordinatesAsync` through setup phase; blocks on `.GetAwaiter().GetResult()` until `IsSlewing=true` is set; then returns HTTP response to client | Setup phase only |
| **Slew thread** | `Task.Run` in `ExecuteSlewAsync` | Runs `ExecuteMovementAndCompletionAsync`, awaits the goto thread, then runs completion | Entire movement + completion |
| **Goto thread** | `Task.Run` in `ExecuteMovementAsync` | Runs `SkyGoTo` (coarse + precision), blocking with `Thread.Sleep`/`WaitOne` | Entire movement only |
| **SkyQueue serial worker** | Dedicated thread inside `SkyQueue` at connection | Dequeues hardware commands in order, sends bytes to the SkyWatcher serial/UDP port, receives responses, calls `ReceiveSteps()` → signals `_mountPositionUpdatedEvent` | Continuously while connected |
| **MediaTimer thread** | `MediaTimerFactory.Create()` at connection, fires at `CheckInterval` (default 2 000 ms for SkyWatcher) | Fires `OnUpdateServerEvent` → `CheckAxisLimits`, `CheckPecTraining`; does NOT call `WaitUpdateMountPosition` | Continuously while connected |
| **TrackingCommandProcessor** | Background `Task` in `TrackingCommandProcessor.Start()` | Processes `TimerTickCommand` and `SlewBoundaryCommand` for AltAz tracking serialisation; `SendSlewBoundaryAck()` in `Prepare()` will return immediately (no-op) if the processor is not running, which is the case for a non-AltAz mount | Continuously while connected (AltAz only; likely not started for Polar) |

> **Thread count summary:** At peak during the coarse slew there are at minimum 4 active
> concurrent threads related to this operation: ASCOM caller (blocked), slew thread,
> goto thread, and SkyQueue serial worker.  After the setup phase the ASCOM caller thread
> is released and can serve other requests.

---

## 8. `WaitUpdateMountPosition` — Where and When

| Call site | Location | When called | Waits up to |
|-----------|----------|-------------|-------------|
| **`SkyPrecisionGoto` loop top** | `Mount.Motion.cs` line 534 | Once per loop iteration — including the **6th iteration** that hits `maxTries >= 5` and breaks. So called up to **6 times** (5 corrections + 1 final position read). On the **goto thread**. | 5 000 ms |
| `SetAltAzTrackingRates` | `Mount.Tracking.cs` | Called only when `AlignmentMode == AltAz` — **skipped for Polar** | 500 ms |
| `SyncToAltAzm` | `Mount.cs` | Not in this flow | 5 000 ms |
| `SyncToTargetRaDec` | `Mount.cs` | Not in this flow | 5 000 ms |
| Public `WaitUpdateMountPosition()` (100 ms variant) | `Mount.cs` | Called by `Telescope.cs` properties: `Altitude`, `Azimuth`, `Declination`, `RightAscension` — only when ASCOM client polls those | 100 ms |

**For Polar/SkyWatcher `SlewToCoordinatesAsync`, `WaitUpdateMountPosition(5000)` is called
at the top of every `SkyPrecisionGoto` loop iteration — up to 6 times (5 correction passes + 1 final position read that triggers the `maxTries >= 5` break). All calls are on the goto thread.**

---

## 9. Tracking State Summary Timeline

```
TIME ──────────────────────────────────────────────────────────────────────────────▶

[PRECONDITION]   Tracking = ON  (SkyAxisSlew Axis1 = sidereal rate)

[SETUP PHASE]    SlewOperation.Prepare()  →  ApplyTracking(false)
─────────────────────────────────────────────────────────────────────────────────
				 Tracking = OFF  (SkyAxisSlew Axis1 = 0, Axis2 = 0)
				 SkyPredictor.Reset() then SkyPredictor.Set(target)

[COARSE SLEW]    SkyGoTo loop — axes slewing at max rate toward target
				 Tracking remains OFF
				 MediaTimer fires every ~2 000 ms (CheckAxisLimits only — no tracking change)

[PRECISION SLEW] SkyPrecisionGoto loop — up to 5 correction passes (up to 6 WaitUpdateMountPosition calls)
				 WaitUpdateMountPosition(5000) called at the top of EACH iteration including the break iteration
				 Tracking remains OFF
				 No AltAz predictor correction (Polar alignment — target is static)

[COMPLETION]     SlewOperation.MarkComplete(true)  →  ApplyTracking(true=TrackingAfterSlew)
─────────────────────────────────────────────────────────────────────────────────
				 Tracking = ON  (SkyAxisSlew Axis1 = sidereal rate, Axis2 = 0)
				 TrackingMode = EqN (Northern hemisphere) or EqS (Southern)

				 IsSlewing = false  ← ASCOM client polls see Slewing=false
```

---

## 10. Key Design Notes

1. **No `CheckTracking` call in `SlewToCoordinatesAsync`.** Unlike `SlewToTarget`
   (which calls `CheckTracking(true, "SlewToTarget")` at line 1553), `SlewToCoordinatesAsync` does **not** call `CheckTracking(true,...)`
   before proceeding. The code instead forces `Tracking = true` if it isn't already (`if(!Tracking) Tracking = true;`), then
   proceeds regardless.

2. **Tracking is disabled in `Prepare()`, not in `SkyGoTo`.** `SkyGoTo` itself never
   touches the tracking state. All tracking transitions are owned by `SlewOperation`.

3. **`SkyGoTo` is a synchronous blocking loop** running inside `Task.Run`, polling hardware
   with `Thread.Sleep`/`WaitHandle.WaitOne` on a dedicated ThreadPool thread. It does not
   use `async/await` internally; it is wrapped in `Task.Run` purely to keep the slew thread
   free to respond to cancellation at the `await` point.

4. **`WaitUpdateMountPosition` is an event-based rendezvous** between the goto thread and the
   SkyQueue serial worker: the goto thread resets a `ManualResetEventSlim`, queues a
   `SkyUpdateSteps` command, and blocks until the serial worker delivers the response and
   calls `ReceiveSteps()` → `_mountPositionUpdatedEvent.Set()`.

5. **Cancellation path:** If `AbortSlewAsync` is called while slewing, `Mount.AbortSlewAsync` calls
   `_slewController.RequestCancellation()`, which calls `_currentOperationCts?.Cancel()`.
   The goto thread's `token.ThrowIfCancellationRequested()` / `token.WaitHandle.WaitOne(250)` detect
   this and propagate `OperationCanceledException` up through `ExecuteMovementAndCompletionAsync`, which
   catches it and calls `HandleCancellationAsync`. That calls `ForceStopAxesAsync`
   (→ `Task.Run(() => mount.InstanceStopAxes())`) and then `operation.HandleCancellation()`, which
   resets `_rateMoveAxes`, sets `_slewState = SlewNone`, and calls `Mount.ApplyTracking(TrackingAfterSlew=true)` —
   **tracking is re-enabled even on cancellation**. Finally `CleanupOperation` sets `IsSlewing = false`.

6. **For Polar alignment the `CompleteRaDecSlewAsync` AltAz settle block is skipped entirely.**
   The settle period (`2–4 × AltAzTrackingUpdateInterval`) and `ApplyTrackingDirect` calls
   exist only for AltAz mounts. On a Polar mount `MarkComplete` immediately enables
   sidereal tracking via `ApplyTracking(true)`.
```
