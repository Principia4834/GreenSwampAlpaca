# MoveAxis Failure Mode Investigation Report

**Author:** GitHub Copilot  
**Reviewed by:** Andy  
**Date:** 2026-05-04 13:38  
**Updated:** 2026-05-04 14:23  
**Status:** Implementation approved — targeted `SetTracking(Axis)` overload selected; execution in progress

---

## 1. Problem Statement

When the ASCOM `MoveAxis(Primary, rate)` command sequence is executed on a SkyWatcher mount
operating in German Equatorial (GermanPolar) mode using the advanced command set (`:X02`),
the following failure is observed:

- **Success scenario:** The mount moves forward (+8), stops, then reverses correctly (−8).
- **Failure scenario:** The mount moves forward (+8), stops, then moves forward again instead of
  reversing, even though `MoveAxis(Primary, -8)` is called.

The **only** difference between the two test scripts is a 2-second `start-sleep` delay inserted
between `MoveAxis(Primary, 0)` (stop) and `MoveAxis(Primary, -8)` (reverse) in the success case.
Removing that delay produces the failure.

---

## 2. Test Evidence

### 2.1 Test Scripts

| Scenario | Stop-to-Reverse Gap | Outcome |
|----------|-------------------|---------|
| Success.ps1 | 2000 ms delay (`start-sleep -Milliseconds 2000`) | Mount reverses correctly |
| Failure.ps1 | No delay (immediate next call) | Mount continues forward |

### 2.2 Key Trace Comparison — First Stop-to-Reverse Transition

**Success trace** (working):
```
10:15:46.340  MoveAxis(Primary, 0)
10:15:46.390  Axis1 Cmd = 020000000000000000    ← :X02 stop (rate=0)
10:15:46.437  Slewing = false
10:15:46.486  Slewing = false
10:15:46.529  Axis1 Cmd = 000000000000068BB0    ← SetTracking() restores sidereal tracking
-- 2000 ms delay in test script --
10:15:48.546  MoveAxis(Primary, -8)
10:15:48.594  Axis1 Cmd = 02FFFFFFFFCF0AAAAC    ← :X02 correct negative rate — mount reverses ✓
10:15:48.638  Slewing = true
```

**Failure trace** (broken):
```
10:16:08.833  MoveAxis(Primary, 0)
10:16:08.881  Axis1 Cmd = 020000000000000000    ← :X02 stop (rate=0)
10:16:08.928  Slewing = false
10:16:08.977  Slewing = false
10:16:09.019  Axis1 Cmd = 000000000000068BB0    ← SetTracking() restores sidereal tracking
10:16:09.063  MoveAxis(Primary, -8)             ← arrives only 44 ms after tracking restore
10:16:09.117  Slewing = true                    ← Slewing = true but no new Axis1 Cmd yet…
10:16:09.159  Axis2 Cmd = 020000000000000000
10:16:09.206  Axis1 Cmd = 02FFFFFFFFCF0AAAAC    ← :X02 correct negative rate IS sent…
```

At first glance the -8 rate IS sent in the failure case. However, examining what actually reaches
the mount (see Section 3 below) reveals the issue is in **command ordering within the serial queue**.

### 2.3 Command Ordering — Failure Case Critical Window

Between `MoveAxis(Primary, 0)` and `MoveAxis(Primary, -8)` the following commands are enqueued
to `SkyQueue` within a window of ~187 ms (from 10:16:08.881 to 10:16:09.206):

| Time | Enqueued by | SkyQueue command | `:X02` payload |
|------|-------------|-----------------|----------------|
| 08.881 | `RateMovePrimaryAxis = 0` setter | `SkyAxisSlew(Axis1, 0)` | `020000000000000000` (stop) |
| 09.019 | `SetTracking()` called from `RateMovePrimaryAxis` setter | `SkyAxisSlew(Axis1, siderealRate)` | `000000000000068BB0` (fwd sidereal) |
| 09.063 | `RateMovePrimaryAxis = -8` setter | `SkyAxisSlew(Axis1, -8)` | `02FFFFFFFFCF0AAAAC` (reverse) |

The queue is a **strict FIFO `BlockingCollection`** that executes one command at a time on a single
background thread. Each `:X02` serial transaction takes measurable time (serial round-trip).

The **sidereal tracking restore** command (`000000000000068BB0`) is enqueued **between** the stop
command and the reverse-slew command. In the success case the 2-second delay means the tracking
restore command has long since been processed and dispatched to the mount before the reverse is
enqueued. In the failure case all three arrive at the queue so rapidly that they execute in order:

1. **Stop** — `:X02 rate=0` → mount stops ✓
2. **Sidereal tracking restore** — `:X02 rate=+sidereal` → mount starts moving **forward** at sidereal rate
3. **Reverse slew** — `:X02 rate=−8` → this **should** reverse, but see Section 4

---

## 3. Root Cause Analysis

### 3.1 The SetTracking() injection

In `Mount.cs`, `RateMovePrimaryAxis` setter:

```csharp
// Mount.cs ~line 567
public double RateMovePrimaryAxis
{
	get => _rateMoveAxes.X;
	set
	{
		if (Math.Abs(_rateMoveAxes.X - value) < 0.0000000001) return;
		_rateMoveAxes.X = value;
		CancelAllAsync();
		SetRateMoveSlewState();
		switch (Settings.Mount)
		{
			case MountType.SkyWatcher:
				_ = new SkyAxisSlew(SkyQueue!.NewId, SkyQueue, Axis.Axis1, _rateMoveAxes.X); // (A)
				break;
		}
		if (Tracking) this.SetTracking();  // (B) ← THIS IS THE PROBLEM SOURCE
		LogMount(...);
	}
}
```

When `MoveAxis(Primary, 0)` is called (rate = 0):

1. Line **(A)** enqueues `SkyAxisSlew(Axis1, 0)` — the stop command.
2. `_rateMoveAxes.X` is now `0.0`.
3. `SetRateMoveSlewState()` sees both axes at 0 and sets `_slewState = SlewType.SlewNone`.
4. Line **(B)**: `Tracking` is `true` (GEQ tracking is active), so `this.SetTracking()` is called **immediately and synchronously on the caller thread**.
5. `SetTracking()` in `Mount.Tracking.cs` computes `SkyGetRate()`. At this point `_rateMoveAxes.X == 0.0`, so the guard `if (_rateMoveAxes.X == 0.0)` passes, and a new `SkyAxisSlew(Axis1, siderealRate)` is enqueued — the tracking restore command.

This enqueue happens on the **same caller thread, synchronously**, before the caller returns from the
`RateMovePrimaryAxis` setter. The queue now contains, in order:

```
[Stop (:X02=0)]  [TrackingRestore (:X02=+sidereal)]
```

### 3.2 The subsequent MoveAxis(-8) call

When `MoveAxis(Primary, -8)` arrives immediately (44 ms later in the failure case):

1. `_rateMoveAxes.X` is updated to `−8`.
2. `SkyAxisSlew(Axis1, -8)` is enqueued — the reverse command.
3. `SetTracking()` is called again; but now `_rateMoveAxes.X != 0.0` so the guard `if (_rateMoveAxes.X == 0.0)` **prevents** another sidereal tracking command.

The queue now contains:
```
[Stop (:X02=0)]  [TrackingRestore (:X02=+sidereal)]  [Reverse (:X02=−8)]
```

### 3.3 What the mount actually receives

The `SkyQueue` single-background-thread processes them strictly in arrival order.
The mount receives:

1. `:X02 rate=0` → motor stops
2. `:X02 rate=+sidereal` → motor starts moving **forward** at sidereal rate
3. `:X02 rate=−8` → motor now reverses

**Steps 2 and 3 happen within milliseconds of each other.** The SkyWatcher advanced command set
(`:X02`) performs an immediate velocity override — it does not stop and restart; it simply changes
the rate register while running. This is confirmed by Andy's own test that consecutive `:X02`
commands in opposite directions cause immediate reversal.

**However**, the mount's firmware response to receiving `:X02 +sidereal` followed immediately by
`:X02 −8` is not the same as receiving `:X02 −8` directly from stopped. The firmware's internal
motion controller may latch onto the **direction** set by step 2 (+forward) and not fully process
step 3 in time, OR the tracking restore at step 2 is the rate that persists because of queue-depth
timing.

### 3.4 Why the success case works

With the 2-second delay:

```
[Stop]  [TrackingRestore]   ... 2000 ms pass ...   [Reverse]
```

By the time `MoveAxis(-8)` is called, both the stop and the sidereal tracking restore have
**already been executed and transmitted to the mount**. The mount is steadily tracking forward
at sidereal rate. The reverse command then arrives as a standalone direction change — which the
mount handles correctly, as confirmed by Andy's direct `:X02` tests.

### 3.5 Why does `Slewing = true` appear before `Axis1 Cmd = 02FFFF…` in the failure trace?

```
10:16:09.063  MoveAxis(Primary, -8)
10:16:09.117  Slewing = true                    ← logged immediately by SetRateMoveSlewState()
10:16:09.159  Axis2 Cmd = 020000000000000000    ← Axis2 tracking restore (from SetTracking())
10:16:09.206  Axis1 Cmd = 02FFFFFFFFCF0AAAAC    ← Axis1 reverse, queued but awaiting execution
```

`Slewing = true` is set **in-process** by `SetRateMoveSlewState()` on the caller thread the moment
`_rateMoveAxes.X = -8`. This is a pure in-memory flag update with no serial I/O. The actual
`:X02` hardware command is still sitting in the queue behind the sidereal restore. The log entry
for `Slewing = true` therefore precedes the hardware dispatch — this is expected behaviour, not an
indication that the reverse command reached the mount at that timestamp.

---

## 4. The Interleaving Race — Summary Diagram

```
TIME →

Caller thread (MoveAxis=0):
  _rateMoveAxes.X = 0
  Enqueue: SkyAxisSlew(Axis1, 0)         ──► Queue: [Stop]
  SetTracking() called synchronously
  Enqueue: SkyAxisSlew(Axis1, +sidereal) ──► Queue: [Stop][TrackRestore]

  (returns to caller)

Caller thread (MoveAxis=-8), 44 ms later:
  _rateMoveAxes.X = -8
  Enqueue: SkyAxisSlew(Axis1, -8)        ──► Queue: [Stop][TrackRestore][Reverse]
  SetTracking() — guard blocks (X != 0)

SkyQueue background thread:
  Executes [Stop]        → :X02=0        → mount stops
  Executes [TrackRestore]→ :X02=+sidereal→ mount moves forward ← UNWANTED
  Executes [Reverse]     → :X02=−8       → mount gets direction change
											but firmware may not honour it
											after just starting forward
```

---

## 5. Confirming Evidence from the Trace

The trace entry at `10:16:09.019` (failure) shows:

```
Axis1 Cmd = 000000000000068BB0
```

This is the sidereal tracking rate (positive, forward direction) being sent to the mount **before**
the `-8` reverse command reaches it. This is the smoking gun: the tracking restore is injected
between stop and reverse, and in the no-delay case there is no time for the mount to complete
processing the stop before the sidereal restore pushes it forward again.

In the success trace at `10:15:46.529`:
```
Axis1 Cmd = 000000000000068BB0
```
The same tracking restore is sent — but 2 seconds elapse before the reverse. The mount has been
tracking quietly forward at sidereal rate for 2 full seconds. When the reverse arrives it is a
clean direction change from a stable state, which the mount handles correctly.

---

## 6. Code Locations Involved

| File | Lines | Role |
|------|-------|------|
| `GreenSwamp.Alpaca.MountControl\Mount.cs` | ~554–575 | `RateMovePrimaryAxis` setter — enqueues `SkyAxisSlew` then calls `SetTracking()` |
| `GreenSwamp.Alpaca.MountControl\Mount.Tracking.cs` | ~68–175 | `SetTracking()` — for GermanPolar/SkyWatcher enqueues `SkyAxisSlew(Axis1, rate.X)` guarded by `if (_rateMoveAxes.X == 0.0)` |
| `GreenSwamp.Alpaca.MountControl\Mount.Lifecycle.cs` | ~250–265 | `SetRateMoveSlewState()` — sets `_slewState` and `_moveAxisActive` |
| `GreenSwamp.Alpaca.Mount.SkyWatcher\SkyWatcher.cs` | ~191–300 | `AxisSlew()` — calls `AxisSlew_Advanced()` for the advanced command set |
| `GreenSwamp.Alpaca.Mount.SkyWatcher\Commands.cs` | ~1375–1420 | `AxisSlew_Advanced()` — constructs and sends `:X02` command |
| `GreenSwamp.Alpaca.Mount.Commands\CommandQueueBase.cs` | ~111–288 | FIFO `BlockingCollection` queue — strictly ordered, no command cancellation |

---

## 7. What Is NOT the Cause

The following have been ruled out by code inspection and Andy's own testing:

- ❌ Astronomical or geometrical calculation errors — confirmed correct by Andy
- ❌ Rate conversion errors — the correct negative rate `02FFFF…CF0AAAAC` is visibly sent
- ❌ `:X02` command format errors — the command is well-formed (correct padding, correct sign)
- ❌ PEC training interference — not active
- ❌ Axis limit interference — not active
- ❌ Unknown additional ASCOM commands — only `MoveAxis`, `Slewing`, and `Tracking` are involved
- ❌ `AngleToStep` overflow — `int` return type is sufficient for sidereal rates
- ❌ The `rateChangeOnly` fast-path in `AxisSlew` (non-advanced path) — not applicable since the
  advanced command set is in use and `AxisSlew_Advanced` is called directly

---

## 8. ASCOM Specification Constraint

### 8.1 What the specification requires

The [ASCOM `Telescope.MoveAxis` specification](https://ascom-standards.org/newdocs/telescope.html#Telescope.MoveAxis)
and the companion [MoveAxis FAQ](https://ascom-standards.org/newdocs/moveaxis-faq.html#moveaxis-faq)
place an explicit obligation on the driver:

> *"If Rate is set to zero, the axis is stopped and the scope will return to its*
> *previous state — `TrackingRate` if `Tracking` was `true`, or stationary if*
> *`Tracking` was `false`."*

This is not a suggestion — it is a **mandatory driver responsibility**. A driver that omits the
tracking restore after `MoveAxis(Rate=0)` is non-compliant and will fail clients that rely on
the scope tracking between segmented slew moves.

Additional relevant requirements:

| Requirement | Source |
|-------------|--------|
| After `MoveAxis(0)`, axis stops and previous tracking state is restored | Spec + FAQ |
| `Slewing` must return `true` while any axis is moving under `MoveAxis` | Spec |
| The `Tracking` property state is the driver's responsibility; clients should not alter it during `MoveAxis` sequences | FAQ |
| Each axis is independent; stopping one does not implicitly stop the other | Spec |

### 8.2 Why the previous options were non-compliant

The earlier analysis (pre-ASCOM review) proposed:

- **Option A (suppressed):** Guard `SetTracking()` with `!_moveAxisActive` to skip the restore
  when either axis is still active.
- **Option B (removed):** Remove the tracking restore from `MoveAxis(0)` entirely.

**Both are non-compliant.** The ASCOM specification does not make restoration conditional on
whether another `MoveAxis` call is about to follow. From the driver's perspective, `MoveAxis(0)`
means *"stop this axis and restore the prior state"* — unconditionally. A subsequent `MoveAxis(-8)`
arriving 44 ms later is a separate, independently-initiated client command. The driver cannot
anticipate it and must fulfil its obligations after the zero-rate call regardless.

### 8.3 Correctly framed root cause

The root cause is **not** that the tracking restore is injected — that restore is correct,
required behaviour per ASCOM. The root cause is that the driver has no mechanism to supersede
or cancel an already-enqueued hardware command when a new `MoveAxis` call arrives before the
queue has consumed the restore. The queue receives three commands in rapid succession:

```
[Stop(:X02=0)]  [TrackRestore(:X02=+sidereal)]  [Reverse(:X02=−8)]
```

The mount executes them in strict FIFO order. When the gap between `MoveAxis(0)` and
`MoveAxis(-8)` is less than the time required for the queue background thread to consume
`[TrackRestore]` and complete the serial round-trip, the mount has the sidereal rate active
when the reverse arrives — producing the observed wrong-direction behaviour.

**In the success case**, the 2-second client delay means `[Stop]` and `[TrackRestore]` are both
fully executed and the mount is stably tracking before `[Reverse]` is ever enqueued. The reverse
then arrives as a clean transition from a stable state.

---

## 9. Scope of the Race Condition — Which `MoveAxis` Transitions Are Affected?

Before examining fix options it is important to establish exactly which call sequences can
produce the interleaving race. Not all `MoveAxis` transitions are equivalent.

### 9.1 The only command that triggers a hardware tracking restore

The `SetTracking()` call in `RateMovePrimaryAxis` reaches `Mount.Tracking.cs` line 169:

```csharp
// Mount.Tracking.cs — SkyWatcher / GermanPolar branch
if (_rateMoveAxes.X == 0.0)
    _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);   // tracking restore
```

**This guard means a `SkyAxisSlew` tracking restore is enqueued for Axis 1 only when
`_rateMoveAxes.X` is exactly `0.0` at the time `SetTracking()` is called.** `_rateMoveAxes.X`
is assigned the new rate value *before* `SetTracking()` is called, so it reflects the rate that
was just set.

### 9.2 Transition analysis

| Sequence | `_rateMoveAxes.X` when `SetTracking()` runs | Restore enqueued? | Race possible? |
|----------|---------------------------------------------|-------------------|---------------|
| `MoveAxis(0)` (stop) | `0.0` | ✅ Yes | ✅ **Yes** |
| `MoveAxis(+8)` (start) | `+8` | ❌ No | ❌ No |
| `MoveAxis(-8)` (start/reverse) | `-8` | ❌ No | ❌ No |
| `MoveAxis(+8) → MoveAxis(-8)` (direction flip, no stop) | `-8` at time of second call | ❌ No | ❌ No |
| `MoveAxis(+8) → MoveAxis(0) → MoveAxis(-8)` | `0.0` at the zero-rate call | ✅ Yes | ✅ **Yes** |

### 9.3 Why `MoveAxis(+8) → MoveAxis(-8)` is already safe

When `MoveAxis(-8)` is called after `MoveAxis(+8)` with no zero stop:

1. `_rateMoveAxes.X` is set to `−8`.
2. `SkyAxisSlew(Axis1, -8)` is enqueued.
3. `SetTracking()` is called — but the guard `if (_rateMoveAxes.X == 0.0)` is **false**
   (value is `−8`), so **no tracking restore command is enqueued**.
4. The queue receives exactly one command: `[Reverse(:X02=−8)]`.

There is no interleaving because there is nothing to interleave. The firmware receives a clean
velocity change from `+8` to `−8` in a single `:X02` command — the same scenario Andy's direct
hardware tests confirmed works correctly.

**Conclusion: Only `MoveAxis(Rate=0)` creates the interleaving race. Making non-zero
`MoveAxis` calls synchronous would add latency for no benefit. Only the zero-rate path
requires a fix.**

---

## 10. Compliant Fix Options

All options below preserve the ASCOM-required tracking restore after `MoveAxis(0)`. The goal
is to ensure that a subsequent `MoveAxis(non-zero)` correctly supersedes that restore at the
hardware level, without skipping the restore when it is genuinely the final command.

### Option A — Command queue superseding

Extend `CommandQueueBase<T>` (or `SkyQueueImplementation`) with the ability to **remove or mark
stale** any pending `SkyAxisSlew` for the same axis before enqueuing a newer one. When
`RateMovePrimaryAxis = -8` is set:

1. Call `SkyQueue.CancelPendingAxisSlew(Axis.Axis1)` before enqueuing the new `SkyAxisSlew`.
2. The restore command `[TrackRestore]` queued by `MoveAxis(0)` is removed if not yet dispatched.
3. The new `[Reverse]` command is enqueued in its place.

If `[TrackRestore]` has already been dispatched when `MoveAxis(-8)` arrives, the new axis-slew
command immediately overrides it (confirmed by Andy's direct `:X02` tests). The window of
incorrect forward motion is at most one serial round-trip (~5–10 ms) rather than the firmware
direction-latch issue seen in the failure trace.

**ASCOM compliance:** `MoveAxis(0)` still enqueues the restore as required. Cancellation only
happens when the driver receives a new non-zero `MoveAxis` for the same axis. If no subsequent
call arrives the restore executes and the scope returns to tracking — fully compliant.

**Risk:** Requires thread-safe removal from `BlockingCollection`. The standard collection does not
support removal by predicate; the implementation would need drain-and-re-enqueue, or a
`ConcurrentQueue` wrapper with mark-and-skip logic. Non-trivial infrastructure change.

### Option B — Cancellable tracking restore token (recommended)

When `MoveAxis(0)` is processed, post the `SetTracking()` call asynchronously via a
`CancellationToken`-aware task with a minimal delay (e.g., 80–150 ms). Store the
`CancellationTokenSource` on the axis state. When `MoveAxis(non-zero)` arrives for the same
axis before the token fires, cancel it.

```csharp
// Pseudocode — RateMovePrimaryAxis setter, value == 0 branch
if (value == 0.0 && Tracking)
{
    _axis1TrackingRestoreCts?.Cancel();
    _axis1TrackingRestoreCts = new CancellationTokenSource();
    var cts = _axis1TrackingRestoreCts;
    _ = Task.Delay(100, cts.Token).ContinueWith(_ =>
    {
        if (!cts.IsCancellationRequested) this.SetTracking();
    }, TaskScheduler.Default);
}
```

When `MoveAxis(-8)` arrives within 100 ms, the token is cancelled before `SetTracking()` fires.
If no follow-up arrives within 100 ms, `SetTracking()` runs normally — fully compliant.

**ASCOM compliance:** The restore is guaranteed unless a follow-up `MoveAxis` call explicitly
cancels it within the token window.

**Risk:** The delay is heuristic. If the client sends the next `MoveAxis` after the token window
(but before 2 seconds), the restore fires, the mount starts tracking, and the subsequent
`MoveAxis(-8)` still correctly overrides it (same as the success case with a short gap rather
than 2 seconds — the firmware handles clean direction changes from a stable state). The token
window should be tuned to be safely above the observed failure gap (~44 ms) — 100–150 ms is a
reasonable starting range.

### Option C — Deferred restore via `TrackingCommandProcessor` timer only

Remove the immediate `SetTracking()` call after `MoveAxis(0)` from `RateMovePrimaryAxis`.
Instead, rely solely on the `TrackingCommandProcessor`'s `TimerTickCommand` path to restore
tracking on the next timer tick (typically every 100–250 ms).

`MoveAxis(0)` sets a `_pendingTrackingRestore` flag. On the next timer tick,
`TrackingCommandProcessor` checks: if `_pendingTrackingRestore && _rateMoveAxes.X == 0.0`,
it calls `SetTracking()`. If `MoveAxis(-8)` has already set `_rateMoveAxes.X = -8` by then,
the flag is cleared and no restore is sent.

**ASCOM compliance:** The restore is guaranteed to happen within one timer period (~250 ms)
after a genuine `MoveAxis(0)` — acceptably prompt for ASCOM purposes.

**Risk:** Tracking is not immediately restored after `MoveAxis(0)`. Over 250 ms at sidereal rate
the positional error is negligible for visual use, but may matter for autoguiding or
precision GoTo coordination.

### Option D — Synchronous hardware completion on `MoveAxis(0)` only (recommended)

Rather than managing timers or queue mutations, `MoveAxis(0)` **blocks the calling thread** until
both the stop command and the tracking restore command have been confirmed as executed by the
`SkyQueue` background thread. The setter only returns once the mount hardware is in its tracking
state.

Because an ASCOM client is a single-threaded COM caller (or sequential async caller in Alpaca),
it cannot issue the next `MoveAxis` call until the current one returns. The race is therefore
**structurally impossible** — there is nothing to race.

#### Execution sequence

```
MoveAxis(Primary, 0) arrives on caller thread:
  1. _rateMoveAxes.X = 0
  2. Enqueue SkyAxisSlew(Axis1, 0) → returns a completion Task
  3. BLOCK: await/Wait() until queue background thread confirms execution
     ↳ mount motor stops
  4. SetTracking() → enqueues SkyAxisSlew(Axis1, +sidereal) → returns a completion Task
  5. BLOCK: await/Wait() until queue background thread confirms execution
     ↳ mount is now tracking
  6. RETURN to client

Only after step 6 can the client call MoveAxis(Primary, -8):
  7. Enqueue SkyAxisSlew(Axis1, -8) ← clean, uncontested, no restore in queue ahead of it
```

#### Why only `MoveAxis(0)` needs this treatment

As established in Section 9, the tracking restore command is only ever enqueued when
`_rateMoveAxes.X == 0.0`. Non-zero rate changes (`+8 → −8`) enqueue only a single
`SkyAxisSlew` with no trailing restore — those transitions are already race-free.
Making them synchronous would add serial round-trip latency (~20–60 ms) on every
direction change or rate adjustment for no gain.

The fix is therefore scoped to the zero-rate branch only:
- `MoveAxis(Rate=0)` → **synchronous** (blocks until stop + restore both confirmed)
- `MoveAxis(Rate≠0)` → **fire-and-forget** (unchanged, already safe)

#### `CommandQueueBase` already supports awaitable completion

Each enqueued command already returns a result via `GetCommandResult()` which internally
awaits the command's `TaskCompletionSource`. The synchronous block in the setter would use
`.GetAwaiter().GetResult()` (or an equivalent `Task.Wait()`) on each command's completion
task, serialising the two hardware round-trips on the calling thread.

#### ASCOM compliance

This is the most faithful implementation of the ASCOM specification's word *"Immediately"*:

> *"If Rate is set to zero, the axis is stopped and the scope will return to its previous state."*

The axis **is** stopped and the scope **has** returned to its previous state before the method
returns. No timer, no heuristic, no window.

**ASCOM compliance:** ✅ Fully compliant — tracking restore is guaranteed complete before the
method returns, with no dependency on client behaviour or timing.

#### Latency

Each serial round-trip on SkyWatcher USB/serial is approximately 5–30 ms. Two round-trips
(stop + restore) add 10–60 ms to the `MoveAxis(0)` call. ASCOM clients typically have timeouts
of 1–5 seconds. This latency is imperceptible to a human operator and well within any
reasonable client timeout.

#### Deadlock risk

The `SkyQueue` background thread must not hold any lock or call any method that the
`RateMovePrimaryAxis` setter caller also holds when it blocks. Given the current architecture
(a `BlockingCollection<ICommand>` consumed on a dedicated background thread with no callbacks
into the setter), this is safe. This assumption must be confirmed during implementation by
reviewing any shared locks between the setter caller context and the queue background thread.

#### Summary

| Property | Assessment |
|----------|-----------|
| ASCOM compliant | ✅ Yes — restore guaranteed before return |
| Race eliminated | ✅ Structurally impossible |
| Heuristic timers | ✅ None |
| Queue mutation | ✅ Not needed |
| Scope | ✅ Zero-rate branch only — non-zero paths unchanged |
| Latency added | ⚠️ ~10–60 ms on `MoveAxis(0)` calls only |
| Deadlock risk | ⚠️ Low — requires confirmation of no shared locks |
| Implementation complexity | ✅ Low — uses existing `GetCommandResult()` pattern |

### Non-compliant options (rejected)

| Option | Why rejected |
|--------|-------------|
| Guard `SetTracking()` with `!_moveAxisActive` | Omits restore when another axis is still moving, violating the per-axis independent restore requirement |
| Remove `SetTracking()` from `MoveAxis(0)` path entirely | Completely omits the mandatory post-stop restore; directly non-compliant |

---

## 11. Recommendation

**Option D (synchronous hardware completion on `MoveAxis(0)`)** is the recommended approach
because it eliminates the race condition structurally rather than managing it, and it is the most
direct implementation of the ASCOM specification's stated intent.

### Recommendation summary

| Option | Mechanism | Compliant | Eliminates race | Complexity | Recommended |
|--------|-----------|-----------|-----------------|-----------|-------------|
| A | Queue superseding — cancel pending restore on new non-zero enqueue | ✅ | ✅ (mostly) | High — queue mutation | Long-term improvement |
| B | Cancellable async token — delay restore, cancel if follow-up arrives | ✅ | ⚠️ Heuristic window | Low | Fallback if D is not feasible |
| C | Timer-only deferred restore via `TrackingCommandProcessor` | ✅ | ⚠️ Timer latency | Low | Last resort |
| **D** | **Synchronous block on `MoveAxis(0)` until stop + restore confirmed** | ✅ | ✅ Structural | Low–Medium | **Recommended** |

### Scope confirmation

Only `MoveAxis(Rate=0)` requires any change. As proven in Section 9:

- `MoveAxis(+8) → MoveAxis(-8)` — **already safe**, no tracking restore is enqueued,
  no race exists, no change needed.
- `MoveAxis(0)` — **fix required**, this is the only transition that enqueues a tracking
  restore command ahead of a potential subsequent move command.

Non-zero `MoveAxis` calls must **not** be made synchronous — they would add unnecessary
serial round-trip latency on every rate change and direction flip.

### Symmetric application

The fix applies identically to `RateMoveSecondaryAxis`. The same `if (_rateMoveAxes.Y == 0.0)`
guard in `SetTracking()` (line 171 of `Mount.Tracking.cs`) means Axis 2 has the identical
race condition and must receive the same synchronous-completion treatment.

---

---

## 12. Deadlock Audit — Option D

This section traces every synchronisation primitive involved in the call chain to confirm that
blocking the `MoveAxis(0)` caller thread on `CompletionEvent.Wait()` cannot deadlock.

### 12.1 Call chain for Option D

```
HTTP request thread
  └─ Telescope.MoveAxis(Primary, 0)        [Telescope.cs line 1302]
       └─ _mount.RateMovePrimaryAxis = 0   [Mount.cs line 558]
            ├─ CancelAllAsync()            — cancels CTS tokens, Thread.Sleep(50)
            ├─ new SkyAxisSlew(...)        — CommandBase ctor calls queue.AddCommand(cmd)
            │    └─ queue.GetCommandResult(cmd)   ← PROPOSED BLOCK POINT
            │         └─ cmd.CompletionEvent.Wait(40000, _cts.Token)
            ├─ SetTracking()
            │    └─ new SkyAxisSlew(...)   — second command
            │         └─ queue.GetCommandResult(cmd)   ← PROPOSED SECOND BLOCK POINT
            └─ return (to client)
```

### 12.2 Locks held by the caller thread at each block point

| Block point | Locks held by caller thread |
|-------------|---------------------------|
| `CompletionEvent.Wait()` on stop command | **None** |
| `CompletionEvent.Wait()` on restore command | **None** |

The `CancelAllAsync()` method cancels `CancellationTokenSource` tokens and calls
`Thread.Sleep(50)` — it acquires no locks and releases nothing that the queue thread needs.
`AddCommand()` calls `BlockingCollection.TryAdd()`, which is internally lock-free. The
`RateMovePrimaryAxis` setter itself holds no lock at any point.

### 12.3 Locks held by the queue background thread

The queue background thread acquires exactly one lock during command execution:
`Monitor.TryEnter(_syncObject, 50)` inside `CmdToMount()` (`Commands.cs` line 1486) for each
serial I/O round-trip. This `_syncObject` is a `private readonly object` declared and owned
entirely within `Commands.cs` — it is **never exposed to any caller-thread code path**.

The `CompletionEvent.Set()` call (`CommandQueueBase.cs` line 284) is in the `finally` block of
`ProcessCommandQueue()`, which executes **after** `command.Execute(_executor)` returns and
**after** `Monitor.Exit(_syncObject)` is called inside `CmdToMount()`. The lock is therefore
fully released before the event is signalled.

### 12.4 Deadlock verdict

| Condition | Status |
|-----------|--------|
| Caller thread holds a lock that the queue thread needs | ❌ No |
| Queue thread holds a lock that the caller thread needs | ❌ No |
| `_syncObject` visible to caller thread | ❌ No — `private readonly` in `Commands.cs` |
| `CompletionEvent.Set()` called after `Monitor.Exit` | ✅ Yes — in `finally` block after `Execute()` |
| Circular wait possible | ❌ No |

**Result: No deadlock is possible with Option D.** The caller thread blocks on
`ManualResetEventSlim.Wait()` while holding no locks. The queue thread signals the event after
completing all its own locking. The two thread synchronisation domains are entirely disjoint.

### 12.5 `CancelAllAsync()` interaction

`CancelAllAsync()` is called at the start of `RateMovePrimaryAxis` setter, before any block
point. It cancels GoTo/pulse-guide CTS tokens and sleeps 50 ms. These tokens are separate from
the queue's `_cts`. Cancelling them does not interfere with `GetCommandResult()`, which awaits
on the queue's own `_cts.Token`. The `Thread.Sleep(50)` inside `CancelAllAsync()` happens
**before** any blocking wait, so it does not extend the Option D latency in the normal case
(CancelAllAsync fast-exits immediately when no async operation is active, which is the typical
`MoveAxis` scenario).

---

## 13. Impact on AltAz Mode

### 13.1 AltAz tracking restore path

In `Mount.Tracking.cs`, the `SetTracking()` method has two branches for `MountType.SkyWatcher`:

```csharp
case AlignmentMode.AltAz:
    if (rateChange != 0)
    {
        SetAltAzTrackingRates(AltAzTrackingType.Predictor);
        if (_altAzTrackingTimer?.IsRunning != true) StartAltAzTrackingTimerInternal();
    }
    else
    {
        if (_altAzTrackingTimer?.IsRunning == true) StopAltAzTrackingTimerInternal();
        _skyTrackingRate = new Vector(0, 0);
    }
    break;
case AlignmentMode.GermanPolar:
    _skyTrackingRate = new Vector(rateChange, 0);
    break;
```

Both branches fall through to the **common** enqueue block at lines 165–173:

```csharp
rate = SkyGetRate();
{
    var sq = SkyQueue;
    if (sq == null) return;
    if (_rateMoveAxes.X == 0.0)
        _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);   // ← same guard
    if (_rateMoveAxes.Y == 0.0)
        _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);   // ← same guard
}
```

**The `_rateMoveAxes.X == 0.0` and `_rateMoveAxes.Y == 0.0` guards are shared between
GermanPolar and AltAz.** The race condition exists in AltAz mode on exactly the same conditions
as GermanPolar, and Option D resolves it identically in both modes.

### 13.2 AltAz-specific considerations

| Consideration | Assessment |
|---------------|-----------|
| AltAz tracking timer (`_altAzTrackingTimer`) | Stopped/started on the caller thread by `SetTracking()` before the enqueue. The `StopAltAzTrackingTimerInternal()` call happens synchronously before the `new SkyAxisSlew` constructor — this ordering is unchanged by Option D |
| `TrackingCommandProcessor` | Posts commands to a `System.Threading.Channels.Channel` — non-blocking, lock-free write. Not affected by the caller-thread block |
| AltAz rate computed by `SkyGetRate()` | Depends on `_skyTrackingRate` which is set by the AltAz branch of `SetTracking()` before `SkyGetRate()` is called. This computation happens synchronously on the caller thread before any block point — no change |
| Two-axis restore (Alt and Az both zero) | Both `Axis1` and `Axis2` restore commands are enqueued in the same `SetTracking()` call. With Option D, both would need to be awaited. The correct implementation awaits each in sequence — **stop1, await; stop2, await; restore1, await; restore2, await** — or awaits them in pairs. See Section 14 for the recommended implementation shape |

### 13.3 AltAz verdict

**Option D is safe and correct for AltAz mode.** No AltAz-specific locking, timer, or
predictor logic is affected by the synchronous completion of the two hardware axis commands.

---

## 14. Impact on Simulator Mounts

### 14.1 Simulator queue infrastructure

The simulator uses `SimQueue` (a `CommandQueueBase<TSimExecutor>` derivative), which is
**the same `CommandQueueBase<T>` base class** as `SkyQueue`. It has:

- The same `BlockingCollection<ICommand<TExecutor>>` FIFO consumer
- The same `CompletionEvent` (`ManualResetEventSlim`) per command
- The same `GetCommandResult()` wait path with `CompletionTimeoutMs = 22000`
- The same `ProcessCommandQueue()` `finally` block that calls `CompletionEvent.Set()`

The simulator executor does **not** use `Monitor.TryEnter(_syncObject)` for serial I/O
(there is no serial port) — its commands execute in-memory. This means the blocking wait
resolves in microseconds rather than the ~5–30 ms serial round-trip seen on SkyWatcher hardware.

### 14.2 Simulator `SetTracking()` path

In `Mount.Tracking.cs`, the Simulator/GermanPolar branch (lines 122–136) also contains the
same guards:

```csharp
case MountType.Simulator:
    case AlignmentMode.GermanPolar:
        if (_rateMoveAxes.X == 0.0)
            _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
        ...
        if (_rateMoveAxes.Y == 0.0)
        {
            ...
            _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, decRate);
        }
```

The same interleaving race can occur on the simulator. Option D eliminates it there too.

### 14.3 Simulator verdict

**Option D is safe and correct for simulator mounts.** The `CompletionEvent.Wait()` resolves
almost instantly (microseconds) because simulator commands execute in-memory with no I/O.
No locking in the simulator executor path can interfere with the caller-thread block.

---

## 15. Dec Axis (Axis 2 / Secondary Axis) Confirmation

Andy has confirmed that the Declination axis is tested in exactly the same way as the
Right Ascension axis using equivalent test scripts. The following code analysis confirms the
fix scope is symmetric.

### 15.1 `RateMoveSecondaryAxis` setter

`Mount.cs` lines 579–601:

```csharp
public double RateMoveSecondaryAxis
{
    get => _rateMoveAxes.Y;
    set
    {
        if (Math.Abs(_rateMoveAxes.Y - value) < 0.0000000001) return;
        _rateMoveAxes.Y = value;
        CancelAllAsync();
        SetRateMoveSlewState();
        switch (Settings.Mount)
        {
            case MountType.SkyWatcher:
                _ = new SkyAxisSlew(SkyQueue!.NewId, SkyQueue, Axis.Axis2, _rateMoveAxes.Y);
                break;
        }
        if (Tracking) this.SetTracking();   // ← same pattern as Primary
    }
}
```

This is structurally identical to `RateMovePrimaryAxis`. The `SetTracking()` call triggers the
same guard at `Mount.Tracking.cs` line 171:

```csharp
if (_rateMoveAxes.Y == 0.0)
    _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);   // ← restore for Axis 2
```

The same interleaving race occurs; the same fix applies.

### 15.2 Transition analysis for Dec axis

| Sequence | `_rateMoveAxes.Y` when `SetTracking()` runs | Restore enqueued? | Race possible? |
|----------|---------------------------------------------|-------------------|---------------|
| `MoveAxis(Secondary, 0)` | `0.0` | ✅ Yes | ✅ Yes |
| `MoveAxis(Secondary, +8)` | `+8` | ❌ No | ❌ No |
| `MoveAxis(Secondary, -8)` | `-8` | ❌ No | ❌ No |
| `MoveAxis(Secondary, +8) → MoveAxis(Secondary, -8)` | `-8` at second call | ❌ No | ❌ No |

Identical outcome to the RA axis analysis in Section 9. Only the zero-rate call is affected.

### 15.3 Independent-axis interactions

The ASCOM spec states each axis is independent. The `_rateMoveAxes` vector holds both axes.
`SetTracking()` guards each axis independently (`X == 0.0` and `Y == 0.0` are separate
conditions). There is no cross-axis interaction. Stopping RA with `MoveAxis(Primary, 0)` will
enqueue a restore for Axis 1 only; `_rateMoveAxes.Y` is unaffected, so Axis 2 restore is
not enqueued unless Dec is also zero.

### 15.4 Dec axis verdict

**The fix must be applied to both `RateMovePrimaryAxis` and `RateMoveSecondaryAxis` setters.**
The deadlock audit, AltAz analysis, and simulator analysis all apply equally to both axes.
The test validation must cover both axes with the equivalent stop-and-reverse script.

---

## 16. Implementation Shape for Option D

For reference during implementation (no code changes in this document):

The two setters each need to await two commands when the rate is zero:
1. The axis-slew stop command (already enqueued by the `switch` block)
2. The tracking restore command (enqueued by `SetTracking()`)

The recommended approach:

```
// In RateMovePrimaryAxis setter, zero-rate branch only:
var stopCmd  = new SkyAxisSlew(SkyQueue.NewId, SkyQueue, Axis.Axis1, 0);
SkyQueue.GetCommandResult(stopCmd);          // block until stop confirmed on hardware

this.SetTracking();                          // enqueues restore command
// SetTracking() must return the restore command (currently discards it with _ = new ...)
// so that GetCommandResult() can be called on it here
SkyQueue.GetCommandResult(restoreCmd);       // block until tracking restore confirmed
```

The main code change required by Option D beyond the setter logic is that `SetTracking()` must
expose the enqueued `SkyAxisSlew` commands so the setter can await them. Currently these are
discarded with `_ = new SkyAxisSlew(...)`. A targeted refactor to return or pass back the
command objects will be needed — this is the primary implementation task.

---

## 17. Implementation Decisions (Confirmed by Andy, 2026-05-04)

| # | Decision | Answer |
|---|----------|--------|
| Q1 | Only a **targeted overload** `SetTracking(Axis axis)` — existing `SetTracking()` signature unchanged | ✅ Confirmed |
| Q2 | Dec-axis test scripts **already exist** — do not create new ones | ✅ Confirmed |
| Q3 | AltAz mode is tested identically and **must work** the same way | ✅ Confirmed |
| Q4 | A latency of **10–60 ms** per `MoveAxis(0)` call is acceptable | ✅ Confirmed |

### Selected Approach — Fence Command Pattern

Instead of modifying `SetTracking()` to return command objects, the overload will:

1. Call `SetTracking()` (unchanged — enqueues restore commands as today).
2. Enqueue a lightweight **fence** (no-op) command on the same queue.
3. Call `GetCommandResult(fence)` to block until the fence is dequeued and executed.

Because the queue is strict FIFO, the fence completing guarantees that any restore commands
enqueued by step 1 have already been processed by the hardware. This approach:

- Leaves `SetTracking()` completely unmodified.
- Works identically for SkyWatcher/GermanPolar, SkyWatcher/AltAz, Sim/GermanPolar,
  and Sim/AltAz — all four branches.
- Requires only a trivial no-op command in each executor.
- The fence command's serial round-trip (or simulator equivalent) determines the actual
  latency, expected ~10–60 ms — within the accepted budget.

### Next Steps

1. ~~Confirm Section 9 — already done.~~ ✅
2. ~~Confirm Option D is chosen.~~ ✅ Fence-command variant selected.
3. ~~Verify deadlock safety.~~ ✅ Audit passed (Section 12).
4. **Implement** `SetTracking(Axis axis)` overload in `Mount.Tracking.cs`.
5. **Update** `RateMovePrimaryAxis` / `RateMoveSecondaryAxis` in `Mount.cs` — zero-rate branch only.
6. Run `Failure.ps1` (no delay) — mount must now reverse correctly.
7. Run `Success.ps1` (2-second delay) — tracking restore must still fire, no regression.
8. Run full build and automated tests.

---

## 18. Closed Questions

| # | Question | Resolution |
|---|----------|------------|
| 1 | Targeted overload vs modifying `SetTracking()` | **Targeted overload** `SetTracking(Axis axis)` — existing method unchanged |
| 2 | Dec-axis test scripts | **Already exist** — Andy confirmed; none created |
| 3 | AltAz production use | **Must work** — AltAz is tested identically; fix covers all four branches |
| 4 | Client timeout / latency budget | **10–60 ms accepted** — well inside any ASCOM client timeout |

---

*Report generated: 2026-05-04 13:16 — Updated for ASCOM compliance: 2026-05-04 13:38 — Option D and scope analysis added: 2026-05-04 13:55 — Deadlock audit, AltAz/Simulator/Dec axis analysis added: 2026-05-04 14:11 — Implementation decisions confirmed, fence-command approach adopted: 2026-05-04 14:23*
