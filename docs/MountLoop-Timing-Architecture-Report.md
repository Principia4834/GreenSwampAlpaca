# Mount Simulator Loop Timing: Feasibility and Architecture Report

**Date:** 2025  
**Subject:** CPU-efficient alternatives to `Thread.Sleep(20)` in `Controllers.MountLoopAsync()`  
**File under review:** `GreenSwamp.Alpaca.Simulator\Controllers.cs` — `MountLoopAsync()` / `MoveAxes()`  
**No code was modified to produce this report.**

---

## 1. Current Implementation

```csharp
private async void MountLoopAsync()
{
    if (_ctsMount == null) _ctsMount = new CancellationTokenSource();
    var ct = _ctsMount.Token;
    _running = true;
    _lastUpdateTime = HiResDateTime.UtcNow;
    var task = System.Threading.Tasks.Task.Run(() =>
    {
        while (!ct.IsCancellationRequested)
        {
            MoveAxes();         // Thread.Sleep(20) inside
        }
    }, ct);
    await task;
    task.Wait(ct);              // redundant — task already completed at this point
    _running = false;
}

private void MoveAxes()
{
    Thread.Sleep(20);
    var now = HiResDateTime.UtcNow;
    var seconds = (now - _lastUpdateTime).TotalSeconds;
    _lastUpdateTime = now;
    // ... physics calculations using `seconds` as the time delta
}
```

### Structural observations (not defects requiring immediate fix, but context for evaluation)

| Issue | Detail |
|---|---|
| `async void` signature | Fire-and-forget. Unhandled exceptions are swallowed; the method cannot be awaited by its caller. Microsoft recommends `async void` only for event handlers. See [Async/Await Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming). |
| `await task; task.Wait(ct)` | `Wait()` after a completed `await` is a no-op in the success path. In the cancelled path, `Wait()` wraps the exception in `AggregateException`, whereas `await` had already propagated it cleanly. The `task.Wait(ct)` line is redundant. |
| `Task.Run` + `Thread.Sleep` | `Task.Run` borrows a **ThreadPool** thread. `Thread.Sleep` blocks that thread for 20 ms per iteration — ~50 blocked moments per second. Microsoft explicitly warns: _"You have tasks that cause the thread to block for long periods of time…a large number of blocked thread pool threads might prevent tasks from starting."_ See [When not to use thread pool threads](https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool#skipping-security-checks). |
| No priority control | ThreadPool threads run at `ThreadPriority.Normal` and cannot be promoted. The OS scheduler may defer wakeup after Sleep, adding jitter. |

### Key physics insight

`MoveAxes()` computes `seconds = (now - _lastUpdateTime).TotalSeconds` and uses that as the physics time delta. This means the **simulation is physically accurate regardless of timing jitter** — if a tick fires 5 ms late, `seconds` simply reflects the true elapsed time and the axis calculations remain correct. What timing accuracy actually governs is:

1. **Command latency** — how quickly a `Command()` call (setting member variables) is reflected in mount state. With a 20 ms loop, max latency is 20 ms.
2. **GoTo settling accuracy** — the `GoTo()` ramp tests `delta < .01` etc. against the current position; very coarse jitter could overshoot these bands, but they are already generous.

In short, timing jitter is **not a simulation correctness problem** — it is a **CPU efficiency and scheduling reliability** problem.

---

## 2. Windows Timer Resolution Background

### Default system clock

On Windows, the default system clock interrupt interval is approximately **15.6 ms** (64 Hz). `Thread.Sleep(N)` and all user-mode timer primitives are ultimately quantised to this clock.

- `Thread.Sleep(20)` with a 15.6 ms clock typically sleeps for **~15.6 ms or ~31.2 ms** — whichever clock tick arrives first after 20 ms.
- The actual sleep duration is therefore 16–32 ms depending on when the thread was scheduled relative to the clock edge.

### `timeBeginPeriod` / per-process resolution

Applications (or the .NET runtime itself) can request a finer clock resolution using the Win32 `timeBeginPeriod(1)` call, setting a 1 ms interrupt interval system-wide. On **Windows 11 build 22000+** the resolution is per-process rather than system-wide, so it no longer affects battery life for other processes.

> Note: Starting with .NET 6 on Windows, the runtime may internally call `timeBeginPeriod(1)` for certain operations (e.g., `Task.Delay`). However this is not guaranteed and not documented as a stable contract.

See: [Timer Resolution (Win32)](https://learn.microsoft.com/windows/win32/multimedia/timer-resolution) and [Obtaining and Setting Timer Resolution](https://learn.microsoft.com/windows/win32/multimedia/obtaining-and-setting-timer-resolution).

### Practical impact for 20 ms target

With **1 ms resolution** (likely in practice on modern Windows): Sleep(20) delivers 19–21 ms — acceptable for a simulator.  
With **15.6 ms resolution** (worst case / locked OS): Sleep(20) delivers 15.6 ms or 31.2 ms — more jitter, but physics remain correct because of time-delta measurement.

---

## 3. Architectural Options

### Option A — Status Quo: `Task.Run` + `Thread.Sleep(20)` (Current)

**Description:** A ThreadPool thread runs a `while` loop calling `MoveAxes()` which sleeps 20 ms.

| Attribute | Assessment |
|---|---|
| CPU efficiency | Poor. Blocks a ThreadPool thread 50 times/sec. ThreadPool may inject spare threads to compensate. |
| Timing accuracy | Medium. Subject to default clock resolution; no priority boost. |
| Code complexity | Low. |
| Priority control | None — ThreadPool threads run at Normal. |
| Cancellation | Functional but coarse: `CancellationRequested` is only checked between ticks. |

**Verdict:** Functional but not optimal. The occupied ThreadPool slot is the primary inefficiency.

---

### Option B — Dedicated Long-Lived Thread + `Thread.Sleep` + `AboveNormal` Priority ⭐ Recommended

**Description:** Replace `Task.Run` with `new Thread(...)`, set `IsBackground = true` and `Priority = ThreadPriority.AboveNormal`, and run the same `while` / `MoveAxes()` loop.

```csharp
// Illustrative pattern — NOT a code change, for report purposes only
var thread = new Thread(() =>
{
    while (!ct.IsCancellationRequested)
        MoveAxes();
})
{
    IsBackground = true,
    Priority = ThreadPriority.AboveNormal,
    Name = "MountSimLoop"
};
thread.Start();
```

Microsoft's guidance: _"There are several scenarios in which it's appropriate to create and manage your own threads instead of using thread pool threads: **You require a thread to have a particular priority**."_  
Source: [The managed thread pool — When not to use thread pool threads](https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool#skipping-security-checks)

| Attribute | Assessment |
|---|---|
| CPU efficiency | Good. One dedicated thread, no ThreadPool pressure. Thread blocks in Sleep — cheap. |
| Timing accuracy | Good. `AboveNormal` means the OS scheduler wakes the thread promptly after Sleep expires, reducing post-sleep latency. |
| Code complexity | Low — minimal change from current. |
| Priority control | Full: `ThreadPriority.AboveNormal` (Win32 priority 10 above Normal). |
| Cancellation | Same as current: poll `IsCancellationRequested` at top of loop. |
| Platform | Windows and Linux/macOS (Linux maps .NET thread priorities to `nice` values). |

**Priority level reference** (from [ThreadPriority Enum](https://learn.microsoft.com/dotnet/api/system.threading.threadpriority)):

| .NET Priority | Win32 Base Priority | Description |
|---|---|---|
| Lowest | 6 | Runs only when all others are idle |
| BelowNormal | 7 | Below normal threads |
| Normal | 8 | **Default for all threads** |
| AboveNormal | 10 | Scheduled before Normal threads |
| Highest | 15 | Highest user-mode priority |

Setting `AboveNormal` ensures the loop thread pre-empts normal Blazor server / SignalR threads, reducing wake latency after `Sleep()`.

**Risks:**
- If `MoveAxes()` ever becomes expensive, it will pre-empt UI threads.
- Priority inversion: if a lower-priority thread holds a lock this thread needs, starvation can occur. Not currently a concern since `MoveAxes()` only accesses private fields.
- `Highest` priority should be avoided — it can starve UI update threads.

---

### Option C — `System.Threading.PeriodicTimer` + `async Task` (Modern .NET 6+ Idiom)

**Description:** Use the `PeriodicTimer` class introduced in .NET 6, running inside an `async Task` started with `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`.

```csharp
// Illustrative pattern — NOT a code change, for report purposes only
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
while (await timer.WaitForNextTickAsync(ct))
{
    MoveAxes(); // without its internal Thread.Sleep
}
```

From the official docs ([PeriodicTimer](https://learn.microsoft.com/dotnet/api/system.threading.periodictimer)):  
> _"Provides a periodic timer that enables waiting asynchronously for timer ticks."_  
> _"The PeriodicTimer behaves like an auto-reset event, in that multiple ticks are coalesced into a single tick if they occur between calls to WaitForNextTickAsync."_

The coalescing behaviour is important: if `MoveAxes()` ever takes longer than 20 ms, the next tick fires immediately rather than queuing a backlog.

| Attribute | Assessment |
|---|---|
| CPU efficiency | Excellent. No thread blocking — the thread is returned to the pool during `WaitForNextTickAsync`. |
| Timing accuracy | Same underlying OS timer as `Thread.Sleep`. |
| Code complexity | Low — cleaner API; cancellation is first-class via `CancellationToken`. |
| Priority control | None — continuations resume on ThreadPool at Normal priority. |
| Async patterns | Correct: no `async void`, proper `CancellationToken` propagation. |
| `async void` fix | Naturally resolves the `async void` issue — the method becomes `async Task`. |

**Limitation:** `WaitForNextTickAsync` continuations are dispatched on ThreadPool threads at `Normal` priority. For a simulator this is acceptable; for hardware-driving this could be marginal.

To use `PeriodicTimer` on a dedicated thread requires blocking the async wait: `timer.WaitForNextTickAsync(ct).GetAwaiter().GetResult()` — which works but negates the async benefit.

---

### Option D — `TaskCreationOptions.LongRunning` + `PeriodicTimer` (Blocking)

**Description:** Force a dedicated thread for the loop body using `LongRunning`, then synchronously block on `PeriodicTimer`.

```csharp
// Illustrative pattern — NOT a code change, for report purposes only
Task.Factory.StartNew(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
    while (await timer.WaitForNextTickAsync(ct))
        MoveAxes();
}, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
```

`TaskCreationOptions.LongRunning` hints the scheduler to allocate a real thread rather than a ThreadPool slot. However, `async` continuations after `await` resume on a ThreadPool thread, so the `LongRunning` guarantee only applies to the synchronous portion before the first `await`.

| Attribute | Assessment |
|---|---|
| CPU efficiency | Good for the waiting phase; continuations still hit ThreadPool. |
| Timing accuracy | Same as Option C. |
| Code complexity | Medium. The `async` lambda inside `StartNew` is a common pitfall (returns `Task<Task>`). |
| Priority control | None. |

**Verdict:** More complex than Options B or C for minimal gain. Not recommended.

---

### Option E — Spin-Wait Hybrid (`Thread.Sleep` + `Stopwatch` Busy-Wait)

**Description:** Sleep for most of the period, then spin-wait the final milliseconds using a `Stopwatch` for sub-millisecond accuracy.

```csharp
// Illustrative pattern — NOT a code change, for report purposes only
var sw = Stopwatch.StartNew();
while (!ct.IsCancellationRequested)
{
    Thread.Sleep(15);                          // rough sleep
    while (sw.ElapsedMilliseconds < 20) { }   // spin the remaining ~5ms
    sw.Restart();
    MoveAxes(); // without internal Sleep
}
```

| Attribute | Assessment |
|---|---|
| CPU efficiency | Poor. The busy-wait spin consumes 100% of a CPU core for ~5ms per 20ms cycle — 25% CPU usage just for timing. |
| Timing accuracy | Excellent: ±0.1 ms achievable. |
| Code complexity | Medium. |
| Priority control | Beneficial only on dedicated threads. |

**Verdict:** Appropriate for real hardware drivers where sub-millisecond accuracy matters. For a simulator inside a Blazor server application, the CPU cost is unjustified. Accuracy far exceeds the simulator's needs.

---

### Option F — Windows Multimedia Class Scheduler Service (MMCSS) via P/Invoke

**Description:** Register the loop thread with MMCSS using `AvSetMmThreadCharacteristics("Games", ref taskIndex)`. MMCSS boosts the thread to Win32 priority 23–26 and guarantees CPU time.

From the official docs ([Multimedia Class Scheduler Service](https://learn.microsoft.com/windows/win32/procthread/multimedia-class-scheduler-service)):  
> _"The MMCSS boosts the priority of threads that are working on high-priority multimedia tasks… Category: High — Priority 23–26 — designed for Pro Audio tasks."_

| Attribute | Assessment |
|---|---|
| CPU efficiency | Excellent — only active when scheduled, OS timer boosted to 1 ms. |
| Timing accuracy | Excellent — comparable to Spin-Wait with low CPU cost. |
| Code complexity | High. Requires P/Invoke to `avrt.dll`. Windows-only. |
| Priority control | Maximum user-mode — second only to system-level real-time threads. |

**P/Invoke surface required:**
```csharp
[DllImport("avrt.dll", SetLastError = true)]
static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

[DllImport("avrt.dll", SetLastError = true)]
static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
```

**Verdict:** Substantial overkill for a simulator. This is appropriate for audio/video processing or a real hardware driver. Not recommended for the simulator.

---

## 4. Comparative Summary

| Option | CPU Efficiency | Timing Accuracy (jitter) | Code Complexity | Priority Control | Recommended For |
|---|---|---|---|---|---|
| A — Current (Task.Run + Sleep) | ⚠️ Poor | ±5–15 ms | Low | None | — |
| **B — Dedicated Thread + Sleep + AboveNormal** | ✅ Good | **±1–5 ms** | **Low** | **Full** | **✅ Simulator** |
| **C — PeriodicTimer + async Task** | ✅ Excellent | ±1–5 ms | Low | None | **✅ Simulator (cleaner code)** |
| D — LongRunning + PeriodicTimer | ✅ Good | ±1–5 ms | Medium | None | — |
| E — Spin-Wait Hybrid | ❌ High CPU | ±0.1 ms | Medium | — | Real hardware driver |
| F — MMCSS P/Invoke | ✅ Excellent | ±0.5 ms | High | Maximum | Real-time audio/hardware |

---

## 5. Recommended Path

### For the simulator specifically, two options stand out:

#### Primary Recommendation — Option B: Dedicated Thread + `AboveNormal` Priority

- **Minimal delta from current code.** The inner `MoveAxes()` loop structure stays identical.
- Frees the ThreadPool from a persistently blocked worker.
- `ThreadPriority.AboveNormal` reduces post-sleep scheduling latency without starving UI threads.
- Works identically on Windows and Linux (where .NET maps it to a lower `nice` value).
- `IsBackground = true` ensures the thread does not prevent process shutdown.

#### Modern Alternative — Option C: `PeriodicTimer.WaitForNextTickAsync`

- Idiomatic .NET 8 code.
- Naturally fixes `async void` and the redundant `task.Wait()`.
- `Dispose()` cleanly cancels a waiting tick.
- Tick coalescing prevents backlog if `MoveAxes()` ever overruns.
- No `Thread.Sleep` in `MoveAxes()` — the delay is entirely in `WaitForNextTickAsync`.
- **Caveat:** Continuations run on ThreadPool at Normal priority. Acceptable for a simulator where physics correctness (via time-delta) is not affected by ±5 ms jitter.

### What NOT to do

- Do not use `Highest` priority — it risks starving Blazor SignalR and UI threads.
- Do not spin-wait — burns CPU for no simulator benefit.
- Do not implement MMCSS — unjustified complexity for a software simulator.
- Do not `await task; task.Wait(ct)` — the `Wait()` after `await` is dead code in the success path and wraps exceptions in `AggregateException` in the failure path.

---

## 6. Additional Notes on Existing Code

### `async void` signature

`async void` should only be used for event handlers in .NET. For all other scenarios, `async Task` is required so that:
- Callers can `await` the method.
- Exceptions propagate correctly rather than being thrown on the `SynchronizationContext` and potentially crashing the process.
- The operation can be tracked (e.g., verified as complete during shutdown).

Reference: [Async/await best practices — Avoid async void](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming#avoid-async-void)

### `Task.Run` with long-running blocking work

Microsoft explicitly documents that `Task.Run` (ThreadPool) is inappropriate when:
> _"You have tasks that cause the thread to block for long periods of time. The thread pool has a maximum number of threads, so a large number of blocked thread pool threads might prevent tasks from starting."_  
> _"You require a thread to have a particular priority."_  
> _"You need to have a stable identity associated with the thread, or to dedicate a thread to a task."_

All three conditions apply to the mount loop.  
Source: [When not to use thread pool threads](https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool#skipping-security-checks)

### `Thread.Sleep` accuracy on modern Windows

On Windows 10/11 with the default 15.6 ms system clock, `Thread.Sleep(20)` delivers either ~15.6 ms or ~31.2 ms depending on clock phase. With 1 ms resolution active (any process calling `timeBeginPeriod(1)`, or Windows 11 per-process resolution), delivery is 19–21 ms. Given that `MoveAxes()` measures actual elapsed time via `HiResDateTime`, the simulation accuracy is unaffected in either case.

Reference: [Game Timing and Multicore Processors — QueryPerformanceCounter](https://learn.microsoft.com/windows/win32/dxtecharts/game-timing-and-multicore-processors)

---

## 7. References

| Document | URL |
|---|---|
| The managed thread pool | https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool |
| ThreadPriority Enum | https://learn.microsoft.com/dotnet/api/system.threading.threadpriority |
| Thread.Priority Property | https://learn.microsoft.com/dotnet/api/system.threading.thread.priority |
| Scheduling threads | https://learn.microsoft.com/dotnet/standard/threading/scheduling-threads |
| System.Threading.PeriodicTimer | https://learn.microsoft.com/dotnet/api/system.threading.periodictimer |
| PeriodicTimer.WaitForNextTickAsync | https://learn.microsoft.com/dotnet/api/system.threading.periodictimer.waitfornexttickasync |
| .NET Timers overview | https://learn.microsoft.com/dotnet/standard/threading/timers |
| Multimedia Class Scheduler Service | https://learn.microsoft.com/windows/win32/procthread/multimedia-class-scheduler-service |
| Timer Resolution (Win32) | https://learn.microsoft.com/windows/win32/multimedia/timer-resolution |
| Obtaining and Setting Timer Resolution | https://learn.microsoft.com/windows/win32/multimedia/obtaining-and-setting-timer-resolution |
| About Multimedia Timers | https://learn.microsoft.com/windows/win32/multimedia/about-multimedia-timers |
| Game Timing and Multicore Processors | https://learn.microsoft.com/windows/win32/dxtecharts/game-timing-and-multicore-processors |
| Async/Await Best Practices | https://learn.microsoft.com/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming |
| Runtime config: threading | https://learn.microsoft.com/dotnet/core/runtime-config/threading |

---

*End of report. No code was modified.*
