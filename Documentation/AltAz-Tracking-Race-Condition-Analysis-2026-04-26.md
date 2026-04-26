# AltAz Tracking Race Condition Analysis

**Document:** AltAz-Tracking-Race-Condition-Analysis-2026-04-26.md
**Author:** Andy Watson / GitHub Copilot
**Date:** 2026-04-26 11:15
**Revision:** 3 -- Q1/Q2/Q3 answered; Section 9 closed; implementation in progress
**Status:** Architecture finalised -- implementation in progress

---

## 1. Executive Summary

The AltAz tracking subsystem uses a `MediaTimer`-driven periodic callback combined with three independent entry points -- `AltAzTrackingTimerTick`, `ActionRateRaDec`, and `ApplyTracking` -- that all converge on `SetTracking()` and the shared `SkyPredictor` state. No thread-synchronisation primitive guards these shared objects across entry points. Eight identifiable race conditions exist, ranging from benign jitter under normal use to hard object-disposal crashes under adversarial timing.

This document catalogues each race, its likelihood and impact, records the design decisions made by Andy, presents the results of a full `SkyPredictor` write-site audit, and proposes a revised queued-command architecture that resolves all hazards with minimal latency.

---

## 2. Design Decisions (Andy, 2026-04-26)

The following decisions were made after reviewing the initial analysis and govern the architecture described in Section 7.

| # | Decision |
|---|---|
| D1 | ASCOM rate setters (`RightAscensionRate`, `DeclinationRate`) must return as fast as possible. The consumer must respond **immediately** -- there must be no one-tick latency before a rate change takes effect. |
| D2 | RA and Dec rates must be applied **atomically together** inside the consumer. However, there must be no cross-axis waiting: setting RA must not block until Dec is also set. A combined `RateChangeCommand(ra, dec)` carries both axes; the caller merges the pending value of the other axis before enqueue. |
| D3 | `SkyPredictor` write sites outside the primary tracking path must be audited before a design decision is made on their handling. See Section 5. |
| D4 | Pulse guiding is additive on the Right Ascension rate and must be routed through the queue. |
| D5 | The queue must operate with **minimal latency** -- the consumer must process each command as soon as it arrives, not batch on a fixed tick. |
| D6 | Slew/sync `SkyPredictor` write sites S1-S4 use **Option A**: `SlewBoundaryCommand` with ACK. Slew controller writes `SkyPredictor` directly after ACK. `SkyPredictor` remains accessible outside `TrackingContext` for reads (S5, S6). Sync paths S7/S8 use `SeedAndEnableCommand`; abort S9 folds into `StopTrackingCommand`. |
| D7 | `_mountPositionUpdatedEvent.Wait` is bounded to **500 ms fixed** timeout. If position data does not arrive, apply the previous rate unchanged and log a warning. This value is not configurable. |
| D8 | `PulseGuideAltAz` is **not redesigned**. Only the two direct `SkyPredictor.Set` calls inside the `Task.Run` body are replaced by an enqueued `PulseGuideCommand`. The `pulseGoTo` hardware action and outer `Task.Run` structure remain unchanged. |

---

## 3. Component Map

| Component | File | Role |
|---|---|---|
| `_altAzTrackingTimer` (`MediaTimer`) | `Mount.cs` | Win32 multimedia timer -- fires `AltAzTrackingTimerTick` periodically |
| `AltAzTrackingTimerTick` | `Mount.cs` (line 2416) | Timer callback -- uses `Interlocked.CompareExchange` as re-entrancy guard, calls `SetTracking()` |
| `StartAltAzTrackingTimer` / `StopAltAzTrackingTimer` | `Mount.cs` (lines 2429-2449) | Create/destroy the timer; called from `SetTracking()` |
| `SetTracking()` (void, no bool) | `Mount.Tracking.cs` (line 69) | Core tracking dispatch -- reads `TrackingMode`, calls `SetAltAzTrackingRates`, starts/stops timer, writes `_skyTrackingRate` |
| `ApplyTracking(bool)` | `Mount.cs` (line 2292) | Turns tracking on/off; seeds `SkyPredictor`, calls `SetTracking()` |
| `ActionRateRaDec` | `Mount.cs` (line 2388) | Called by `SetRateRa` / `SetRateDec` -- reads/writes `SkyPredictor`, calls `SetTracking()` |
| `SetRateRa` / `SetRateDec` | `Mount.cs` (lines 1047, 1039) | ASCOM `RightAscensionRate` / `DeclinationRate` setters dispatch here |
| `PulseGuideAltAz` | `Mount.cs` (line 3131) | Fires on `Task.Run`, calls `StopAltAzTrackingTimer` and writes `SkyPredictor` directly |
| `SkyPredictor` | `SkyPredictor.cs` | Holds `_ra`, `_dec`, `_rateRa`, `_rateDec`, `ReferenceTime` -- no internal locking |
| `SetAltAzTrackingRates` | `SkyServer.TelescopeAPI.cs` (line 269) | Reads `SkyPredictor`, issues `UpdateSteps`, waits on `_mountPositionUpdatedEvent`, writes `_skyTrackingRate` |
| `_skyTrackingRate` (`Vector`) | `Mount.cs` (line 154) | Shared mutable; written by `SetAltAzTrackingRates` and read by `SkyGetRate` |
| `_rateRaDec` (`Vector`) | `Mount.cs` (line 171) | Stores `RateRa` / `RateDec` -- no locking |
| `MediaTimer` | `Principles\MediaTimer.cs` | Win32 `timeSetEvent` wrapper -- callbacks fire on OS thread-pool threads; no `SynchronizingObject` set |

---

## 4. Thread Model

| Thread label | Origin | Calls into tracking |
|---|---|---|
| **T-ASCOM** (HTTP thread pool) | `TelescopeController` -> `Telescope` -> `_mount.SetRateDec/SetRateRa` | `ActionRateRaDec` -> `SetTracking()` |
| **T-ASCOM** (HTTP thread pool) | `Telescope.Tracking` setter -> `ApplyTracking` | `SetTracking()`, `SkyPredictor.Set/Reset` |
| **T-TIMER** (Win32 OS thread pool) | `MediaTimer` native callback, no `SynchronizingObject` | `AltAzTrackingTimerTick` -> `SetTracking()` |
| **T-UPDATE** (managed thread) | Main update tick -> `OnUpdateServerEvent` | `UpdateSteps`, signals `_mountPositionUpdatedEvent` |
| **T-PULSEGUIDE** (`Task.Run`) | `PulseGuideAltAz` | `StopAltAzTrackingTimer`, `SkyPredictor.Set` |
| **T-SLEW** (`Task.Run`) | `SlewController` completion phases | `SkyPredictor.Set/Reset`, `InstanceApplyTrackingDirect` |

`MediaTimer` fires directly on a Win32 thread-pool thread -- no managed context, no `SynchronizingObject`. Every tick executes on a **different** OS thread from the ASCOM caller.

---

## 5. SkyPredictor Write-Site Audit

The following is the complete set of write operations (`.Set(...)`, `.Reset()`, direct property assignment) on `SkyPredictor` across the entire solution, excluding the `SkyPredictor.cs` definition file.

### 5.1 Write Sites Within the Tracking Path (already in scope for the queue)

| Site | File / Line | Caller thread | Operation | Notes |
|---|---|---|---|---|
| `ActionRateRaDec` | `Mount.cs` 2394-2395 | T-ASCOM | `GetRaDecAtTime` then `Set(ra, dec, rateRa, rateDec)` | Called by `SetRateRa` / `SetRateDec`; primary rate-change path |
| `ActionRateRaDec` (tracking=false) | `Mount.cs` 2403 | T-ASCOM | `Set(RaXForm, DecXForm, rateRa, rateDec)` | Seeds predictor when tracking is off |
| `ApplyTracking(true)` | `Mount.cs` 2302-2305 | T-ASCOM | `Set(RaXForm, DecXForm, 0, 0)` or `ReferenceTime = now` | First-time seed or reference-time refresh on tracking enable |
| `ApplyTracking(false)` | `Mount.cs` 2310 | T-ASCOM | `Reset()` | Clears predictor on tracking disable |
| `InstanceCompletePark` | `Mount.cs` 2338 | T-ASCOM | `Reset()` | Park completion |
| `PulseGuideAltAz` (axis 0) | `Mount.cs` 3144 | T-PULSEGUIDE | `Set(Ra - raOffset, Dec)` | RA pulse nudge |
| `PulseGuideAltAz` (axis 1) | `Mount.cs` 3151 | T-PULSEGUIDE | `Set(Ra, Dec + decOffset)` | Dec pulse nudge |
| `SetRateMoveSlewState` | `Mount.cs` 2380 | T-ASCOM | `Set(RaXForm, DecXForm)` | Re-seeds predictor when MoveAxis stops |

### 5.2 Write Sites Outside the Tracking Path (slew and sync paths)

| Site | File / Line | Caller thread | Operation | Context / significance |
|---|---|---|---|---|
| **S1** `SlewController.PrepareSlewAsync` | `SlewController.cs` 642 | T-SLEW | `Set(TargetRa, TargetDec, RateRa, RateDec)` | Seeds predictor at start of RA/Dec GoTo slew. Tracking disabled by `ApplyTracking(false)` immediately before, so timer should be stopped -- but no lock guarantees the ordering. |
| **S2** `SlewController.CompleteAsync` -- `SlewHome` | `SlewController.cs` 694 | T-SLEW | `Reset()` | Resets predictor at end of Home slew. Timer is stopped. No concurrent write risk in practice, but not guaranteed. |
| **S3** `SlewController.CompleteAsync` -- `SlewHandpad` | `SlewController.cs` 698-701 | T-SLEW | `Set(RaXForm, DecXForm)` | Seeds predictor at end of handpad slew. |
| **S4** `SlewController.CompleteRaDecSlewAsync` | `SlewController.cs` 793 | T-SLEW | `Set(TargetRa, TargetDec)` | Re-seeds predictor just before re-enabling tracking at end of GoTo. Currently safe but fragile. |
| **S5** `SimPrecisionGoto` | `Mount.cs` 2569, 2664, 2667 | T-SLEW | `GetRaDecAtTime` (read-only) | **Read only** -- predictor used to compute moving target for precision GoTo loop. Not a write. |
| **S6** `SkyPrecisionGoto` | `Mount.cs` 2874, 2994 | T-SLEW | `GetRaDecAtTime` (read-only) | **Read only** -- same as S5 for SkyWatcher hardware. |
| **S7** `SyncToAltAz` | `Mount.cs` 1220 | T-ASCOM | `Set(ra, dec)` then `InstanceApplyTrackingDirect` | Syncs predictor then enables tracking. Ordering currently safe (sync halts tracking first), but unsynchronised with a concurrent timer tick. |
| **S8** `SyncToTargetRaDec` | `Mount.cs` 1259 | T-ASCOM | `Set(TargetRa, TargetDec)` then `InstanceApplyTrackingDirect` | Same pattern as S7. |
| **S9** `AbortSlew` | `Mount.Operations.cs` 129 | T-ASCOM | `Set(RaXForm, DecXForm)` | Re-seeds predictor before calling `ApplyTracking(tracking)`. Safe today because abort disables tracking first, but fragile. |

### 5.3 Audit Conclusions

**Reads outside the tracking path (no design change needed):**
S5 and S6 are pure reads (`GetRaDecAtTime`). They compute a target position during a GoTo slew while tracking is disabled and the timer is stopped. No write hazard exists provided the "disable tracking before slew" contract is always upheld.

**Writes outside the tracking path -- three categories:**

| Category | Sites | Characteristic | Recommended treatment |
|---|---|---|---|
| **A -- Slew lifecycle** | S1, S2, S3, S4 | Occur when tracking is disabled and timer should be stopped. Safe in practice but ordering not guaranteed. | Use `SlewBoundaryCommand` with ACK (see Section 7.11) to guarantee timer is stopped before slew writes `SkyPredictor`. Slew controller may then write `SkyPredictor` directly after ACK. |
| **B -- Sync operations** | S7, S8 | Write + immediate re-enable of tracking. No timer running at write time, but fragile. | Route as `SeedAndEnableCommand(ra, dec)` through the queue so write and timer-start are a single atomic consumer operation. |
| **C -- Abort restore** | S9 | Write then conditional `ApplyTracking`. Safe today (abort stops timer first). | Fold `SkyPredictor.Set` at line 129 into a `SeedAndEnableCommand` or `StopTrackingCommand` posted to the queue. |

**Andy's decision on S1-S9 treatment (Option A or B from Section 9, Q1) is required before Phase 3 implementation.**

---

## 6. Race Condition Catalogue

### RC-1 -- Timer tick vs. `StopAltAzTrackingTimer` (ObjectDisposedException)

**Severity:** HIGH -- can throw `ObjectDisposedException` or `NullReferenceException`
**Likelihood:** Low but reproducible under rapid rate changes or stop/start cycles

The Win32 `timeKillEvent` does not guarantee a callback already executing (or enqueued on the OS thread pool) will not fire once more after the kill. After `StopAltAzTrackingTimer` nulls `_altAzTrackingTimer`, a tick still in flight calls `SetTracking()`, which may call `StartAltAzTrackingTimer()` or `StopAltAzTrackingTimer()` against a null or disposed reference. `MediaTimer.Period` getter throws `ObjectDisposedException` when `_disposed == true`.

### RC-2 -- Concurrent `_skyTrackingRate` write (stale rate applied to hardware)

**Severity:** MEDIUM -- wrong rates sent to mount silently

T-ASCOM (`ActionRateRaDec` -> `SetTracking`) and T-TIMER (`AltAzTrackingTimerTick` -> `SetTracking`) both calculate and assign `_skyTrackingRate = new Vector(...)`. The last write wins; if both calculations used a `SkyPredictor` snapshot taken before the other thread's write, one valid result silently overwrites the other.

### RC-3 -- Torn `SkyPredictor` read/write (position discontinuity)

**Severity:** MEDIUM -- jump in tracked target coordinates

`SetRaDecNow()` performs a three-field read-modify-write on `_ra`, `_dec`, `ReferenceTime` without a lock. T-TIMER can read a partially-updated state (new `_ra`, old `_dec`), producing a wrong AltAz conversion and incorrect hardware rates.

### RC-4 -- Partial RA/Dec rate update (mixed old/new rates)

**Severity:** MEDIUM -- one timer period of incorrect tracking rate

`SetRateRa` and `SetRateDec` arrive as separate sequential ASCOM calls. T-TIMER reads `_rateRa` and `_rateDec` between the two setter calls, observing mixed old-RA/new-Dec or new-RA/old-Dec state. Decision D2 addresses this directly.

### RC-5 -- Double `StartAltAzTrackingTimer` (leaked timer, doubled tick rate)

**Severity:** LOW-MEDIUM -- rate jitter; possible second timer instance

Both T-ASCOM and T-TIMER can simultaneously evaluate `_altAzTrackingTimer?.IsRunning != true` as true and both call `StartAltAzTrackingTimer`. Two timer instances fire for one period before the second start's internal stop kills the first.

### RC-6 -- `ApplyTracking(false)` vs. in-flight tick (timer re-armed after disable)

**Severity:** LOW-MEDIUM -- tracking effectively stays on after explicit disable

A tick that passed the `IsMountRunning` guard but has not yet called `SetTracking()` completes with `TrackingMode` still set to `AltAz` and may call `StartAltAzTrackingTimer` after `ApplyTracking(false)` has finished.

### RC-7 -- `_altAzTrackingLock` scope too narrow

**Severity:** LOW -- concurrent `SetTracking()` from ASCOM and timer threads

`Interlocked.CompareExchange` on `_altAzTrackingLock` is only checked in `AltAzTrackingTimerTick`. Every other caller of `SetTracking()` -- `ActionRateRaDec`, `ApplyTracking`, `InstanceApplyTrackingDirect` -- bypasses it entirely.

### RC-8 -- Blocking `_mountPositionUpdatedEvent.Wait(5000)` on Win32 timer thread

**Severity:** LOW (liveness / performance risk)

`SetAltAzTrackingRates` waits up to 5 s on `_mountPositionUpdatedEvent` while executing on the OS multimedia timer thread. This starves the Win32 timer thread pool. If the update thread is delayed (serial timeout), the timer tick thread is blocked and subsequent ticks are lost for up to 5 s.

### RC-9 -- `PulseGuideAltAz` writes `SkyPredictor` on `Task.Run` thread

**Severity:** MEDIUM -- unguarded concurrent write from a third thread

`PulseGuideAltAz` runs on `Task.Run` (T-PULSEGUIDE). It calls `StopAltAzTrackingTimer()` and then immediately writes `SkyPredictor.Set(...)` with no synchronisation. Between the `Stop` call and the `Set` call, T-TIMER may fire a final post-kill callback and read `SkyPredictor` concurrently with the pulse write. Additionally, overlapping `Task.Run` calls can write `SkyPredictor` simultaneously.

---

## 7. Proposed Queued-Command Architecture

### 7.1 Governing Constraints (from design decisions)

- **D1 / D5:** The queue is a `Channel<ITrackingCommand>` with a single consumer that wakes immediately on enqueue via `await channel.Reader.ReadAsync()`. No polling; no waiting for the next timer tick.
- **D2:** RA and Dec rates are always applied atomically as a single `RateChangeCommand`. The writer merges the pending value of the other axis before posting -- no cross-axis blocking.
- **D4:** Pulse-guide RA offsets route through the queue as a `PulseGuideCommand` and are additive.

### 7.2 Architecture Overview

```
+---------------------------------------------------------------------------+
|  Producers (any thread -- fire-and-forget TryWrite, never block)          |
|                                                                           |
|  MediaTimer.Tick   ---- TrackingTickCommand ----------------------------+  |
|  ASCOM SetRateRa   --+                                                  |  |
|  ASCOM SetRateDec  --+-- RateChangeCommand(ra, dec) -------------------+  |
|  ApplyTracking     ---- TrackingStateCommand(enable, seed?) -----------+  |
|  PulseGuideAltAz   ---- PulseGuideCommand(axis, offset) ---------------+  |
|  StopAxes/Abort    ---- StopTrackingCommand ----------------------------+  |
|  SyncToAltAz/RaDec ---- SeedAndEnableCommand(ra, dec) -----------------+  |
|  SlewBoundary      ---- SlewBoundaryCommand(start|end, ack?) ----------+  |
|                                                                         |  |
|                         Channel<ITrackingCommand> (Unbounded)           |  |
|                                         |                               v  |
|               +-------------------------+-----------------------------+    |
|               | TrackingCommandProcessor  (single consumer Task)     |    |
|               |                                                       |    |
|               |  while (true)                                         |    |
|               |  {                                                    |    |
|               |    var cmd = await _channel.Reader.ReadAsync(ct);    |    |
|               |    cmd.Apply(_context);   // never blocks             |    |
|               |    ApplyHardwareRates(_context); // posts to HW queue|    |
|               |    _snapshot = _context.ToSnapshot();                |    |
|               |  }                                                    |    |
|               +-------------------------------------------------------+    |
|                                         |                                  |
|                         Immutable TrackingSnapshot published               |
|                         +-- Hardware queue (CmdAxisTracking/SkyAxisSlew)  |
|                         +-- Blazor UI state (volatile read)               |
+---------------------------------------------------------------------------+
```

### 7.3 Why Unbounded Channel

Using `UnboundedChannel` removes back-pressure overhead and guarantees no command is ever dropped. With D1/D5 requiring immediate consumer response, the channel will normally contain at most one or two items. Tick de-duplication at the writer side (see Section 7.6) prevents accumulation of stale tick commands.

### 7.4 Command Interface and Types

```csharp
internal interface ITrackingCommand
{
    /// <summary>Apply this command to the tracking context. Must never block.</summary>
    void Apply(TrackingContext context);
}

/// <summary>Timer tick -- recalculate AltAz rates from current predictor position.</summary>
internal sealed class TrackingTickCommand : ITrackingCommand
{
    public static readonly TrackingTickCommand Instance = new();
    private TrackingTickCommand() { }
    public void Apply(TrackingContext ctx) => ctx.UpdateAltAzRates();
}

/// <summary>
/// Combined RA+Dec rate change -- always carries both axes atomically (D2).
/// Writer merges the current pending value of the other axis before posting.
/// </summary>
internal sealed record RateChangeCommand(double RaDegreesPerSec, double DecDegreesPerSec)
    : ITrackingCommand
{
    public void Apply(TrackingContext ctx) => ctx.SetRatesAndUpdate(RaDegreesPerSec, DecDegreesPerSec);
}

/// <summary>Enable or disable tracking; optionally seeds the predictor.</summary>
internal sealed record TrackingStateCommand(
    bool Enable,
    double? SeedRa = null,
    double? SeedDec = null) : ITrackingCommand
{
    public void Apply(TrackingContext ctx) => ctx.SetTrackingEnabled(Enable, SeedRa, SeedDec);
}

/// <summary>
/// Pulse guide -- RA offset is additive on the tracking rate (D4).
/// Dec offset adjusts the predictor Dec position.
/// </summary>
internal sealed record PulseGuideCommand(int Axis, double GuideRate, int DurationMs)
    : ITrackingCommand
{
    public void Apply(TrackingContext ctx) => ctx.ApplyPulseGuide(Axis, GuideRate, DurationMs);
}

/// <summary>Stop all tracking and zero all rates (from StopAxes / AbortSlew).</summary>
internal sealed class StopTrackingCommand : ITrackingCommand
{
    public static readonly StopTrackingCommand Instance = new();
    private StopTrackingCommand() { }
    public void Apply(TrackingContext ctx) => ctx.Stop();
}

/// <summary>
/// Seed predictor and immediately enable tracking (Sync and post-slew paths -- S7, S8, S4).
/// </summary>
internal sealed record SeedAndEnableCommand(double Ra, double Dec) : ITrackingCommand
{
    public void Apply(TrackingContext ctx) => ctx.SeedAndEnable(Ra, Dec);
}

/// <summary>
/// Marks a slew boundary.  IsStart=true: consumer stops and ACKs before slew proceeds.
/// IsStart=false: no-op (slew completion uses SeedAndEnableCommand or StopTrackingCommand).
/// </summary>
internal sealed record SlewBoundaryCommand(bool IsStart, TaskCompletionSource? Ack = null)
    : ITrackingCommand
{
    public void Apply(TrackingContext ctx)
    {
        if (IsStart) ctx.Stop();
        Ack?.SetResult();
    }
}
```

### 7.5 Writer-Side Rate Merging (D1 + D2)

`RightAscensionRate` and `DeclinationRate` arrive as independent ASCOM property calls. The writer merges them into a combined `RateChangeCommand` via a **pending-rate accumulator** on `Mount`:

```csharp
// Mount -- written from ASCOM HTTP thread pool (effectively serialised per ASCOM spec)
private double _pendingRateRa;
private double _pendingRateDec;

public void SetRateRa(double degrees)
{
    Volatile.Write(ref _pendingRateRa, degrees);
    _trackingChannel.Writer.TryWrite(
        new RateChangeCommand(degrees, Volatile.Read(ref _pendingRateDec)));
    // Returns immediately -- no block (D1)
}

public void SetRateDec(double degrees)
{
    Volatile.Write(ref _pendingRateDec, degrees);
    _trackingChannel.Writer.TryWrite(
        new RateChangeCommand(Volatile.Read(ref _pendingRateRa), degrees));
    // Returns immediately -- no block (D1)
}
```

**Key properties:**
- Each setter returns immediately; `TryWrite` on an unbounded channel never blocks (D1, D5).
- The command always carries the most current known value for **both** axes at the moment of posting.
- If RA is set then Dec in rapid succession, two commands arrive: `(newRA, oldDec)` then `(newRA, newDec)`. The consumer applies both in order, reaching the correct final state. The transient `(newRA, oldDec)` state lasts for at most one consumer iteration -- microseconds at typical rates (D2 trade-off, accepted).
- No cross-axis waiting; no blocking between the two ASCOM calls (D2).

### 7.6 Tick De-duplication (D5)

A writer-side flag prevents stale ticks from accumulating if the consumer is momentarily busy:

```csharp
private int _tickPending; // 0 = none queued, 1 = one already queued

// Called from Win32 timer callback thread (T-TIMER)
private void AltAzTrackingTimerTick(object sender, EventArgs e)
{
    if (Interlocked.CompareExchange(ref _tickPending, 1, 0) == 0)
        _trackingChannel.Writer.TryWrite(TrackingTickCommand.Instance);
    // Returns immediately -- never blocks Win32 timer thread (RC-8 fixed)
}
```

Consumer clears the flag after processing the tick:

```csharp
case TrackingTickCommand:
    _context.UpdateAltAzRates();
    Interlocked.Exchange(ref _tickPending, 0); // allow next tick to queue
    break;
```

At most one `TrackingTickCommand` is ever in the channel at a time. The timer callback completes in nanoseconds (RC-8 resolved).

### 7.7 `TrackingContext` -- Single-Threaded State Owner

`TrackingContext` owns all previously-shared state. Accessed exclusively from the single consumer task -- **no locks needed inside it**.

```csharp
internal sealed class TrackingContext
{
    // Predictor state -- replaces SkyPredictor fields for consumer-owned access
    private double _predictorRa, _predictorDec;
    private double _predictorRateRa, _predictorRateDec;
    private DateTime _referenceTime;

    // Rate and tracking state
    private Vector _skyTrackingRate;
    private bool _tracking;
    private TrackingMode _trackingMode;
    private MediaTimer? _altAzTrackingTimer; // owned entirely here

    // Callbacks to hardware layer (hardware queues are already thread-safe)
    private readonly Action<Vector> _applyHardwareRate; // posts CmdAxisTracking / SkyAxisSlew
    private readonly Func<StepSnapshot> _readCurrentSteps;
    private readonly int _updateIntervalMs;

    public void UpdateAltAzRates()
    {
        // predictor math -> _skyTrackingRate -> _applyHardwareRate(rate)
        // Executes immediately on rate change (D1) and on each timer tick
    }

    public void SetRatesAndUpdate(double ra, double dec)
    {
        // Update predictor rates, then call UpdateAltAzRates() immediately (D1)
        _predictorRateRa = ra;
        _predictorRateDec = dec;
        UpdateAltAzRates();
    }

    public void SetTrackingEnabled(bool enable, double? seedRa, double? seedDec)
    {
        _tracking = enable;
        if (enable)
        {
            if (seedRa.HasValue) { _predictorRa = seedRa.Value; _predictorDec = seedDec!.Value; }
            _referenceTime = DateTime.UtcNow;
            _trackingMode = TrackingMode.AltAz;
            EnsureTimerRunning();
        }
        else
        {
            EnsureTimerStopped();
            _trackingMode = TrackingMode.Off;
        }
    }

    public void ApplyPulseGuide(int axis, double rate, int ms)
    {
        // RA: additive offset on _predictorRa (D4)
        // Dec: additive offset on _predictorDec
        // Then call UpdateAltAzRates() immediately
    }

    public void SeedAndEnable(double ra, double dec)
        => SetTrackingEnabled(true, ra, dec);

    public void Stop()
    {
        EnsureTimerStopped();
        _tracking = false;
        _trackingMode = TrackingMode.Off;
        _skyTrackingRate = new Vector(0, 0);
    }

    private void EnsureTimerRunning()
    {
        if (_altAzTrackingTimer?.IsRunning == true) return;
        _altAzTrackingTimer = new MediaTimer { Period = _updateIntervalMs };
        _altAzTrackingTimer.Tick += OnTimerTick;
        _altAzTrackingTimer.Start();
    }

    private void EnsureTimerStopped()
    {
        if (_altAzTrackingTimer == null) return;
        _altAzTrackingTimer.Tick -= OnTimerTick;
        if (_altAzTrackingTimer.IsRunning) _altAzTrackingTimer.Stop();
        _altAzTrackingTimer.Dispose();
        _altAzTrackingTimer = null;
    }

    public TrackingSnapshot ToSnapshot() => new(
        _tracking, _trackingMode,
        _skyTrackingRate.X, _skyTrackingRate.Y,
        _predictorRa, _predictorDec,
        _predictorRateRa, _predictorRateDec,
        _referenceTime, DateTimeOffset.UtcNow);
}
```

### 7.8 Immediate Rate Response (D1)

When `RateChangeCommand` arrives, `SetRatesAndUpdate` calls `UpdateAltAzRates()` in the **same consumer iteration** -- the hardware rate command is posted to the hardware queue immediately, not deferred to the next timer tick. This satisfies D1 without polling.

### 7.9 `TrackingSnapshot` -- Immutable Read Model

```csharp
internal sealed record TrackingSnapshot(
    bool Tracking,
    TrackingMode Mode,
    double SkyTrackingRateX,
    double SkyTrackingRateY,
    double PredictorRa,
    double PredictorDec,
    double PredictorRateRa,
    double PredictorRateDec,
    DateTime ReferenceTime,
    DateTimeOffset PublishedAt);
```

Published after every command. Blazor UI and ASCOM read-back properties consume the snapshot via `volatile` reference. No locks, no torn reads on any external consumer.

### 7.10 `_mountPositionUpdatedEvent.Wait` -- Off the Timer Thread (RC-8)

Under the queue architecture, `UpdateAltAzRates()` runs on the consumer task thread (a managed thread-pool thread), not the Win32 timer thread. The blocking wait no longer starves the multimedia timer. However, blocking the consumer for up to 5 s stalls subsequent commands.

**Recommended:** Bound the wait to the timer period (e.g. 600 ms maximum). If position data does not arrive in time, apply the previous rate unchanged and log a warning. This maintains D5 latency for all non-blocked commands.

### 7.11 Slew Boundary Contract

Before any slew-lifecycle write to `SkyPredictor` (sites S1-S4), the slew controller posts `SlewBoundaryCommand(IsStart: true, Ack: tcs)` and awaits the ACK. The consumer processes `Stop()` -- guaranteeing the timer is stopped and the consumer is idle -- then signals ACK. The slew controller may then write `SkyPredictor` directly.

```csharp
// SlewController.PrepareSlewAsync
var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
mount.PostTrackingCommand(new SlewBoundaryCommand(IsStart: true, Ack: tcs));
await tcs.Task;  // confirmed: consumer idle, timer stopped
Mount.SkyPredictor.Set(Target[0], Target[1], RateRa, RateDec);  // safe
```

---

## 8. Race Condition Resolution Matrix

| RC | Description | Resolution |
|---|---|---|
| RC-1 | Timer disposal race / `ObjectDisposedException` | Timer owned exclusively by `TrackingContext` inside consumer task -- no other thread can dispose it |
| RC-2 | Concurrent `_skyTrackingRate` write | Single consumer -- only one writer to `_skyTrackingRate` exists |
| RC-3 | Torn `SkyPredictor` read/write | All `SkyPredictor` state private to `TrackingContext`, accessed only from consumer task |
| RC-4 | Partial RA/Dec rate update | `RateChangeCommand` carries both axes; writer merges pending values at enqueue time (Section 7.5) |
| RC-5 | Double `StartAltAzTrackingTimer` | `EnsureTimerRunning` is single-threaded inside consumer; only called from consumer |
| RC-6 | `ApplyTracking(false)` vs. in-flight tick | `TrackingStateCommand(false)` processed by consumer; timer can only be stopped from consumer |
| RC-7 | `_altAzTrackingLock` insufficient | Deleted; tick de-duplication via `_tickPending` flag (Section 7.6) |
| RC-8 | Blocking wait on Win32 timer thread | Timer callback returns immediately; blocking wait runs on consumer thread, bounded to timer period |
| RC-9 | `PulseGuideAltAz` concurrent `SkyPredictor` write | Replaced by `PulseGuideCommand` through queue; `Task.Run` SkyPredictor write eliminated |

---

## 9. Design Decisions -- Final Answers (Andy, 2026-04-26)

| Question | Answer | Decision recorded |
|---|---|---|
| Q1 -- SkyPredictor slew write sites | **Option A** -- `SlewBoundaryCommand` ACK for S1-S4; direct write after ACK; `SkyPredictor` remains readable outside `TrackingContext` | D6 |
| Q2 -- `UpdateAltAzRates` blocking wait bound | **500 ms fixed** -- not configurable; apply previous rate and log warning on timeout | D7 |
| Q3 -- `PulseGuideAltAz` refactoring scope | **Minimal scope** -- only replace the two `SkyPredictor.Set` lines with `PulseGuideCommand`; do not redesign `PulseGuideAltAz` | D8 |

---

## 10. Recommended Phased Approach

### Phase 1 -- Immediate low-risk mitigations (no architecture change required)

1. **RC-1:** In `StopAltAzTrackingTimer`, capture the timer reference in a local before nulling the field. Add a `Thread.SpinWait` of one timer period after `Stop()` to allow any in-flight callback to drain.
2. **RC-8:** Bound `_mountPositionUpdatedEvent.Wait` to the timer period (not 5 s); log and skip rate update on timeout.
3. **RC-6:** Add a `Tracking` guard in `AltAzTrackingTimerTick` before calling `SetTracking()`.

### Phase 2 -- Queue infrastructure

4. Add `Channel<ITrackingCommand>`, `ITrackingCommand`, all command records, `TrackingCommandProcessor`, and `TrackingSnapshot` as new types in `GreenSwamp.Alpaca.MountControl`.
5. Start the consumer task in `MountConnect()`; complete the channel writer and await the consumer in `MountDisconnect()`.
6. Route `AltAzTrackingTimerTick` through the channel with `_tickPending` de-duplication.
7. Route `SetRateRa` / `SetRateDec` through the channel with writer-side merging.

### Phase 3 -- State ownership transfer (Q1-Q3 answered; D6-D8 apply)

8. Move timer lifecycle (`StartAltAzTrackingTimer`, `StopAltAzTrackingTimer`) inside `TrackingContext`. Remove from `Mount`.
9. Route `ApplyTracking`, `StopAxes`, `AbortSlew`, `PulseGuideAltAz`, `SyncToAltAz`, `SyncToTargetRaDec`, and `SlewController` boundaries through the queue.
10. Delete `_altAzTrackingLock`, `_altAzTrackingTimer`, `StartAltAzTrackingTimer`, and `StopAltAzTrackingTimer` from `Mount`.

### Phase 4 -- Validation

11. Write unit tests for `TrackingCommandProcessor` using an in-memory `Channel` and mock hardware callbacks, covering: timer tick, rate change, pulse guide, tracking disable, and slew boundary.
12. Verify via monitor logging that RA/Dec rate changes produce hardware commands within one consumer cycle.

---

*Document updated: 2026-04-26 11:15*