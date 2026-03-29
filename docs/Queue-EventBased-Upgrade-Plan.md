# Queue Event-Based Upgrade Plan

**Date:** 2026-03-29  
**Branch:** master  
**Source of truth:** `T:\source\repos\Principia4834\GSServer\GSSolution.sln` (upstream GSServer)  
**Scope:** Port the upstream queue rewrite (dictionary+polling → event-based signaling) to GreenSwampAlpaca.

---

## 1. Background and Motivation

The upstream GSServer has replaced the queue result-retrieval mechanism in both `MountQueue` and `SkyQueue`.  
The old mechanism used a `ConcurrentDictionary<long, ICommand>` that callers polled in a tight `Thread.Sleep(1)` loop for up to 40 seconds.  
The new mechanism embeds a `ManualResetEventSlim CompletionEvent` in every command; the processing thread calls `CompletionEvent.Set()` in a `finally` block; callers block on `CompletionEvent.Wait(timeout, ct)`.

Additional changes were made alongside the core mechanism change:  
- `CommandQueueStatistics` — new class, thread-safe counters for Processed/Successful/Failed/TimedOut/Exceptions.  
- `_taskReadySignal` — ensures `Start()` does not return until the background task is actually consuming commands.  
- `TaskCreationOptions.LongRunning` — dedicated OS thread instead of threadpool.  
- Diagnostic logging — per-command timing logged at `MonitorType.Debug` behind a `MonitorLog.InTypes()` guard, with a configurable per-queue `commandTypesToLog` filter.  
- Performance degradation monitoring — state-transition warning/recovery logging when `queueDepth > 10 || queueWaitMs > 100 ms`.  
- Proper `Stop()` sequence — `CompleteAdding()` → `Cancel()` → `_processingTask.Wait(5 s)`.  
- `GetConsumingEnumerable(ct)` — cancellation token passed so the loop exits cleanly.

In GreenSwampAlpaca the two queues delegate to a shared `CommandQueueBase<TExecutor>`. All of the above improvements must be applied there rather than in each queue individually.

---

## 2. Dependency Graph

```
GreenSwamp.Alpaca.Principles
       ↑
GreenSwamp.Alpaca.Shared          ← CommandQueueStatistics goes here (Step 1)
       ↑
GreenSwamp.Alpaca.Mount.Commands  ← ICommand, CommandBase, CommandQueueBase, ICommandQueue (Steps 2–5)
       ↑                    ↑
GreenSwamp.Alpaca.Simulator  GreenSwamp.Alpaca.Mount.SkyWatcher  (Steps 6–7)
       ↑                    ↑
GreenSwamp.Alpaca.MountControl  (no changes needed here)
       ↑
GreenSwamp.Alpaca.MountControl.Tests  (Step 8)
```

`Mount.Commands` already has a `<ProjectReference>` to `Shared`, so no new project reference is needed for `CommandQueueStatistics` or `MonitorLog`. There is **no circular dependency risk**.

---

## 3. Files to Change (Ordered by Dependency)

| Step | File | Action |
|------|------|--------|
| 1 | `GreenSwamp.Alpaca.Shared\CommandQueueStatistics.cs` | **New file** |
| 2 | `GreenSwamp.Alpaca.Mount.Commands\ICommand.cs` | Add `CompletionEvent` property |
| 3 | `GreenSwamp.Alpaca.Mount.Commands\CommandBase.cs` | Implement `CompletionEvent` only (no `IDisposable` — Decision 1: Option B) |
| 4 | `GreenSwamp.Alpaca.Mount.Commands\CommandQueueBase.cs` | Full overhaul — see §4 |
| 5 | `GreenSwamp.Alpaca.Mount.Commands\ICommandQueue.cs` | Add `Statistics` property |
| 6 | `GreenSwamp.Alpaca.Simulator\MountQueue.cs` | Add `Statistics` pass-through; tune timeout |
| 7 | `GreenSwamp.Alpaca.Mount.SkyWatcher\SkyQueue.cs` | Add `Statistics` pass-through; tune timeout |
| 8 | `GreenSwamp.Alpaca.MountControl.Tests\QueueMigrationTests.cs` | Add new unit tests |

---

## 4. Detailed Change Descriptions

### Step 1 — `CommandQueueStatistics.cs` (new file in `GreenSwamp.Alpaca.Shared`)

Port directly from `GS.Shared\CommandQueueStatistics.cs`. No changes needed beyond namespace.  
Five `long` fields, all accessed via `Interlocked.*`. Methods: `Reset()`, `IncrementTotalProcessed()`, `IncrementSuccessful()`, `IncrementFailed()`, `IncrementTimedOut()`, `IncrementExceptions()`. `ToString()` returns a pipe-delimited summary.

```csharp
// Target namespace
namespace GreenSwamp.Alpaca.Shared
```

---

### Step 2 — `ICommand<TExecutor>` — Add `CompletionEvent`

Add one property to the interface:

```csharp
/// <summary>
/// Signals command completion to waiting callers. Set by the queue after Execute().
/// </summary>
ManualResetEventSlim CompletionEvent { get; }
```

**Impact:** `ISkyCommand` and `IMountCommand` both inherit from `ICommand<TExecutor>` so they get the property automatically. No changes needed in those files. However, any code that implements `ICommand<TExecutor>` directly (outside `CommandBase`) would break at compile time — verify there are none before proceeding.

---

### Step 3 — `CommandBase<TExecutor>` — Implement `CompletionEvent`

Add field and property:

```csharp
private readonly ManualResetEventSlim _completionEvent = new ManualResetEventSlim(false);
public ManualResetEventSlim CompletionEvent => _completionEvent;
```

> **Decision 1 — RESOLVED (Option B):** No `IDisposable` on `CommandBase`. Match upstream exactly. The kernel `WaitHandle` inside `ManualResetEventSlim` is only allocated on contended waits (>~10 ms); for a well-behaved queue this is negligible. Adding `IDisposable` would require 76 production `GetCommandResult` call sites to be updated with `using` patterns — cost outweighs benefit.

---

### Step 4 — `CommandQueueBase<TExecutor>` — Full Overhaul

This is the largest change. The complete list of modifications:

#### 4a. Remove `ConcurrentDictionary` and `CleanResults`

Delete fields:
```csharp
private ConcurrentDictionary<long, ICommand<TExecutor>> _resultsDictionary;
```
Delete method `CleanResults(int count, int seconds)`.  
Remove the call to `CleanResults(40, 180)` inside `AddCommand`.

#### 4b. Add new fields

```csharp
private Task _processingTask;
private ManualResetEventSlim _taskReadySignal;
private bool _isInWarningState;
```

`Statistics` property (public):
```csharp
public CommandQueueStatistics Statistics { get; private set; }
```

#### 4c. Parameterise the completion timeout

Add a virtual protected property so subclasses (`MountQueueImplementation`, `SkyQueueImplementation`) can override independently, matching the upstream (22 s Simulator, 40 s SkyWatcher):

```csharp
/// <summary>
/// Milliseconds to wait for a command's CompletionEvent before timing out.
/// Override per implementation: Simulator = 22000, SkyWatcher = 40000.
/// </summary>
protected virtual int CompletionTimeoutMs => 40000;
```

#### 4d. Rewrite `GetCommandResult`

Replace the `Stopwatch`+`Thread.Sleep` loop with:

```csharp
public virtual ICommand<TExecutor> GetCommandResult(ICommand<TExecutor> command)
{
    try
    {
        if (!IsRunning || _cts?.IsCancellationRequested != false)
        {
            var a = "Queue | IsRunning:" + IsRunning + "| IsCancel:" + _cts?.IsCancellationRequested + "| IsConnected:" + IsConnected();
            if (command.Exception != null) { a += "| Ex:" + command.Exception.Message; }
            command.Exception = new Exception(a);
            command.Successful = false;
            return command;
        }

        if (command.CompletionEvent.Wait(CompletionTimeoutMs, _cts.Token))
        {
            return command;
        }

        // Timeout
        Statistics?.IncrementTimedOut();
        command.Exception = new Exception($"Queue Read Timeout {command.Id}, {command}");
        command.Successful = false;
        return command;
    }
    catch (OperationCanceledException)
    {
        Statistics?.IncrementExceptions();
        command.Exception = new Exception("Operation cancelled");
        command.Successful = false;
        return command;
    }
    catch (Exception e)
    {
        Statistics?.IncrementExceptions();
        command.Exception = e;
        command.Successful = false;
        return command;
    }
}
```

#### 4e. Rewrite `Start()`

```csharp
public virtual void Start()
{
    Stop();
    if (_cts == null) _cts = new CancellationTokenSource();
    var ct = _cts.Token;

    _executor = CreateExecutor();
    InitializeExecutor(_executor);
    _commandBlockingCollection = new BlockingCollection<ICommand<TExecutor>>();
    _taskReadySignal = new ManualResetEventSlim(false);

    if (Statistics == null) Statistics = new CommandQueueStatistics();
    Statistics.Reset();

    _processingTask = Task.Factory.StartNew(() =>
    {
        try
        {
            _taskReadySignal?.Set();
            foreach (var command in _commandBlockingCollection.GetConsumingEnumerable(ct))
            {
                ProcessCommandQueue(command);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
    }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    if (_taskReadySignal.Wait(TimeSpan.FromSeconds(5)))
    {
        IsRunning = true;
        Thread.Sleep(100);  // pragmatic delay — matches upstream
    }
    else
    {
        Stop();
        throw new Exception("Background processing task failed to start within timeout");
    }

    _taskReadySignal?.Dispose();
    _taskReadySignal = null;
}
```

#### 4f. Rewrite `Stop()`

```csharp
public virtual void Stop()
{
    IsRunning = false;
    _commandBlockingCollection?.CompleteAdding();   // must be before Cancel()
    _cts?.Cancel();

    if (_processingTask != null)
    {
        try { _processingTask.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { /* cancellation aggregate */ }
        _processingTask = null;
    }

    CleanupExecutor(_executor);
    _executor = default;
    _cts?.Dispose();
    _cts = null;
    _commandBlockingCollection?.Dispose();
    _commandBlockingCollection = null;
}
```

**Critical ordering:** `CompleteAdding()` before `Cancel()`. If reversed, `GetConsumingEnumerable(ct)` throws `OperationCanceledException` immediately without draining queued items; in-flight commands are abandoned and their callers hang until `CompletionEvent.Wait` times out.

#### 4g. Rewrite `ProcessCommandQueue`

```csharp
private void ProcessCommandQueue(ICommand<TExecutor> command)
{
    Statistics?.IncrementTotalProcessed();

    var diagnosticsEnabled = MonitorLog.InTypes(MonitorType.Debug);
    var dequeuedAt = HiResDateTime.UtcNow;
    var queueDepth = _commandBlockingCollection?.Count ?? 0;
    string commandType = null;

    if (diagnosticsEnabled)
    {
        commandType = command.GetType().Name;
        var filter = DiagnosticCommandFilter;
        if (filter.Length > 0 && !Array.Exists(filter, t => t == commandType))
            diagnosticsEnabled = false;
    }

    try
    {
        if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
        {
            command.Exception = new Exception("Queue stopped or not connected");
            command.Successful = false;
            Statistics?.IncrementFailed();
            return;
        }

        var executionStart = HiResDateTime.UtcNow;
        command.Execute(_executor);

        if (command.Successful) Statistics?.IncrementSuccessful();
        else Statistics?.IncrementFailed();

        if (command.Exception != null)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Mount, Type = MonitorType.Warning,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{command.Exception.Message}|{command.Exception.StackTrace}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        var queueWaitMs = (executionStart - dequeuedAt).TotalMilliseconds;

        if (diagnosticsEnabled)
        {
            var executionMs = (HiResDateTime.UtcNow - executionStart).TotalMilliseconds;
            ThreadPool.GetAvailableThreads(out var worker, out var io);
            ThreadPool.GetMinThreads(out var minWorker, out var minIoc);
            ThreadPool.GetMaxThreads(out var maxWorker, out var portThreads);
            var threadMsg = $"|Worker:{worker:N0}|IO:{io:N0}|MinW:{minWorker:N0}|MinIO:{minIoc:N0}|MaxW:{maxWorker:N0}|MaxIO:{portThreads:N0}";
            var diagnosticItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount, Type = MonitorType.Debug,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"CmdId:{command.Id}|Type:{commandType}|QueueWait:{queueWaitMs:F3}ms|Execution:{executionMs:F3}ms|Total:{(queueWaitMs + executionMs):F3}ms|QueueDepth:{queueDepth}|Success:{command.Successful}|{threadMsg}"
            };
            MonitorLog.LogToMonitor(diagnosticItem);
        }

        // Performance state-transition logging
        var isSlowOrDeep = queueDepth > 10 || queueWaitMs > 100.0;
        switch (isSlowOrDeep)
        {
            case true when !_isInWarningState:
                _isInWarningState = true;
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount, Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Queue performance degraded - QueueDepth:{queueDepth}|QueueWait:{queueWaitMs:F3}ms"
                });
                break;
            case false when _isInWarningState:
                _isInWarningState = false;
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount, Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Queue performance normal - QueueDepth:{queueDepth}|QueueWait:{queueWaitMs:F3}ms"
                });
                break;
        }
    }
    catch (Exception e)
    {
        command.Exception = e;
        command.Successful = false;
        Statistics?.IncrementFailed();
        Statistics?.IncrementExceptions();
    }
    finally
    {
        command.CompletionEvent.Set();   // Always unblock the caller
    }
}
```

#### 4h. Add virtual diagnostic filter hook

```csharp
/// <summary>
/// Command type names to include in Debug diagnostic logging.
/// Empty array = log all types (when Debug monitoring is enabled).
/// Override in subclasses to filter to specific command types.
/// </summary>
protected virtual string[] DiagnosticCommandFilter => [];
```

---

### Step 5 — `ICommandQueue<TExecutor>` — Add `Statistics`

```csharp
/// <summary>
/// Thread-safe statistics for this queue session.
/// Null until the queue has been started at least once.
/// </summary>
CommandQueueStatistics? Statistics { get; }
```

---

### Step 6 — `MountQueueImplementation` in `MountQueue.cs`

Two changes:

**a) Override `CompletionTimeoutMs`** — Simulator is less latency-sensitive than hardware; upstream uses 22 s:

```csharp
protected override int CompletionTimeoutMs => 22000;
```

**b) Override `DiagnosticCommandFilter`** — Match upstream:

```csharp
protected override string[] DiagnosticCommandFilter => ["CmdAxisPulse"];
```

**c) Expose `Statistics` on static facade:**

```csharp
/// <summary>
/// Thread-safe queue processing statistics for the current session.
/// </summary>
public static CommandQueueStatistics Statistics => _instance?.Statistics;
```

---

### Step 7 — `SkyQueueImplementation` in `SkyQueue.cs`

**a) Override `DiagnosticCommandFilter`** — Match upstream:

```csharp
protected override string[] DiagnosticCommandFilter => ["SkyAxisPulse"];
```

**b) `CompletionTimeoutMs`** — SkyWatcher keeps the default 40 s; no override needed unless you want it explicit:

```csharp
// Optional — makes the 40 s explicit:
protected override int CompletionTimeoutMs => 40000;
```

**c) Expose `Statistics` on static facade:**

```csharp
public static CommandQueueStatistics Statistics => _instance?.Statistics;
```

---

### Step 8 — `QueueMigrationTests.cs` — New tests

Add the following unit tests (no integration tests needed for the core mechanism):

```
WhenCommandCompletedThenCompletionEventIsSet
WhenQueueStoppedBeforeCommandExecutedThenGetCommandResultReturnsFailure
WhenGetCommandResultTimesOutThenTimedOutIsIncrementedInStatistics
WhenCommandSucceedsThenSuccessfulIsIncrementedInStatistics
WhenCommandThrowsThenFailedAndExceptionsAreIncrementedInStatistics
WhenStartCalledThenStatisticsAreReset
WhenMountQueueImplementationCreatedThenCompletionTimeoutIs22Seconds
WhenSkyQueueImplementationCreatedThenCompletionTimeoutIs40Seconds
```

These test `CommandQueueBase` behavior via a minimal test double (a fake `TExecutor` and fake connected/disconnected state) — no live hardware or timers required.

---

## 5. Architectural Risks and Mitigations

### Risk 1 — `ICommand<TExecutor>` is a public interface: breaking change
**Description:** Adding `CompletionEvent` to `ICommand<TExecutor>` is a breaking change for any code implementing the interface directly outside of `CommandBase<TExecutor>`.  
**Evidence:** `ISkyCommand` and `IMountCommand` extend `ICommand<TExecutor>` but do not implement it directly — they delegate to `CommandBase`. The concrete command classes all inherit `CommandBase`. No external implementations are visible in this solution.  
**Mitigation:** Before implementation, run a `Find All References` on `ICommand<TExecutor>` and confirm every implementation is via `CommandBase`. If any exist, they will break at compile time and must be updated to call `new ManualResetEventSlim(false)`.  
**Residual risk:** LOW — solution is self-contained; no NuGet publishing that would affect downstream consumers.

---

### Risk 2 — `CompletionEvent.Set()` called before caller enters `Wait()`
**Description:** There is a brief window between `queue.AddCommand(this)` (in the `CommandBase` constructor) and the caller calling `GetCommandResult`. If the queue processes the command instantly and calls `CompletionEvent.Set()` before the caller calls `CompletionEvent.Wait()`, does the caller hang?  
**Answer:** No. `ManualResetEventSlim` is a *manual-reset* event: once `Set()` is called, it stays set. Any subsequent `Wait()` returns immediately. This is the correct primitive for this pattern.  
**Residual risk:** NONE.

---

### Risk 3 — Constructor self-enqueue race
**Description:** `CommandBase` constructor calls `queue.AddCommand(this)` before the subclass constructor body has run (field initialisers are complete, but the subclass constructor body runs after). If the queue processes the command before the subclass constructor completes, `ExecuteInternal` could read uninitialised state.  
**Evidence:** This is a **pre-existing race condition** in the current architecture, not introduced by this change. The new event-based approach doesn't make it worse or better.  
**Mitigation:** Out of scope for this ticket. Document in code comments. The risk is very low in practice because the background task processes from a `BlockingCollection` and the subclass constructors are trivial (single field assignments). A future ticket should consider moving the `queue.AddCommand(this)` call out of the base constructor.  
**Residual risk:** LOW (pre-existing, not worsened by this change).

---

### Risk 4 — `ManualResetEventSlim` disposal
**Description:** `ManualResetEventSlim` implements `IDisposable`. If the kernel `WaitHandle` is created (only happens under contention, i.e. when `Wait()` is called with timeout), it must be disposed to avoid kernel handle leaks. Command objects are not currently `IDisposable`.  
**Options:**  
  - **Option A:** Add `IDisposable` to `CommandBase` and call `_completionEvent.Dispose()`. `GetCommandResult` callers are responsible for disposal. This is a minor API surface change — 76 production call sites would require `using` patterns.  
  - **Option B:** Follow upstream exactly — no `IDisposable`. Accept the minor kernel handle leak on contended commands. In practice, each command's `Wait()` uses a managed spin-wait first; the kernel handle is only created if `Wait()` blocks for > ~10 ms. For a well-behaved queue this rarely allocates.  
**RESOLVED — Option B chosen:** No `IDisposable` on `CommandBase`. Match upstream. The 76-call-site update cost (Option A) outweighs the negligible leak risk for a well-behaved queue.  
**Residual risk:** LOW-MEDIUM on slow commands only (kernel handle not disposed); acceptable given queue performance targets.

---

### Risk 5 — `Stop()` ordering: `CompleteAdding()` before `Cancel()`
**Description:** If `_cts.Cancel()` is called before `_commandBlockingCollection.CompleteAdding()`, `GetConsumingEnumerable(ct)` throws `OperationCanceledException` immediately. Any commands already in the queue that have not yet been processed have their callers blocked on `CompletionEvent.Wait(timeout, ct)`. Since `ct` is now cancelled, `Wait()` throws `OperationCanceledException` in the caller, which `GetCommandResult` catches and returns a failed command. This is *mostly* acceptable, but commands added by the caller after `Cancel()` but before `CompleteAdding()` could throw inside `TryAdd`.  
**Mitigation:** Strictly follow the upstream sequence: `CompleteAdding()` → `Cancel()`. The implementation plan codifies this in §4f.  
**Residual risk:** LOW if the sequence is followed exactly.

---

### Risk 6 — `_isInWarningState` field in a shared base class
**Description:** In the upstream, `_isInWarningState` is a static field in a static class — it is inherently per-queue-class. In `CommandQueueBase<TExecutor>`, it will be an instance field — it is per-queue-instance. This is correct. `ProcessCommandQueue` runs on the single background task thread only, so no synchronisation of `_isInWarningState` is needed.  
**Residual risk:** NONE.

---

### Risk 7 — `MonitorLog` / `HiResDateTime` dependency from `CommandQueueBase`
**Description:** Adding diagnostic logging requires `MonitorLog` and `HiResDateTime` from `GreenSwamp.Alpaca.Shared`. `CommandQueueBase` is in `GreenSwamp.Alpaca.Mount.Commands`, which already has a `<ProjectReference>` to `GreenSwamp.Alpaca.Shared`. No new dependency needs to be added.  
**Circular dependency check:** `Shared` references `Principles` and `Settings` — it does NOT reference `Mount.Commands`. Safe.  
**Residual risk:** NONE.

---

### Risk 8 — `_taskReadySignal` disposal timing
**Description:** `_taskReadySignal` is created, used to synchronise task startup, then disposed and nulled. The `ReSharper disable AccessToDisposedClosure` comment in the upstream acknowledges that the background task lambda captures `_taskReadySignal` by reference and calls `_taskReadySignal?.Set()`. The null-conditional `?.` guards against the case where `_taskReadySignal` is disposed and set to null before `Set()` is called.  
**However:** In `CommandQueueBase`, `_taskReadySignal` is an instance field. The lambda captures `this`, and accesses `_taskReadySignal` through `this`. After `_taskReadySignal?.Set()` is called from inside the task, the outer code waits on it, disposes it, and sets it to null. If the task tries to call `_taskReadySignal?.Set()` a second time (it won't — it's called once at the top of the task body), there could be a null dereference. The null-conditional makes this safe.  
**Mitigation:** Keep the `_taskReadySignal?.Set()` pattern exactly as shown. Add the ReSharper suppression comment.  
**Residual risk:** NONE.

---

### Risk 9 — `Thread.Sleep(100)` after task ready
**Description:** The upstream adds `Thread.Sleep(100)` after confirming the background task is ready. This is a pragmatic delay to ensure the `BlockingCollection` is fully operational. In a Blazor server context, `Thread.Sleep` on a request thread is undesirable; however, `MountStart()` is called from a non-request background path (`MountInstance.MountStart()`), not from a Blazor render or Razor component lifecycle. The sleep is safe here.  
**Residual risk:** NONE in current call paths.

---

### Risk 10 — `AddCommand` no longer checks `IsConnected()` independently for the dictionary
**Description:** In the current `CommandQueueBase`, `AddCommand` calls `CleanResults()` and also checks `IsConnected()`. After this change, `AddCommand` still checks `IsConnected()` but no longer needs to clean the dictionary. The connectivity check in `ProcessCommandQueue` provides a second independent guard (belt and braces). This is the same pattern as the upstream.  
**Residual risk:** NONE.

---

## 6. Test Strategy

### Unit tests (new, in `QueueMigrationTests.cs`)

Use a `FakeCommandQueue<FakeExecutor>` test double that:
- Provides a minimal connected fake executor.
- Lets tests control `IsConnected()` by overriding the virtual method.
- Does not require any real serial port, media timer, or SkyServer state.

Key behaviours to assert:
1. `CompletionEvent.IsSet` is `true` after `ProcessCommandQueue` runs (via a thin subclass that exposes the private method for testing, or by calling `GetCommandResult` on a running queue).
2. `Statistics.CommandsSuccessful` increments on a successful command.
3. `Statistics.CommandsTimedOut` increments when `CompletionEvent` is never set within the timeout (simulate by a command that blocks).
4. `Statistics.ExceptionsHandled` increments when `Execute()` throws.
5. `Stop()` sequence: queue stops cleanly even if a command is in-flight.
6. `Start()` sets `IsRunning = true` only after the background task has signalled ready.
7. Two independent instances do not share `Statistics` (regression for Q2 isolation).

### Skipped integration tests (mark `[Fact(Skip = ...)]`)

- `WhenCommandSentToLiveSimulatorThenResultIsReturned` — requires full `MountInstance` startup.
- `WhenQueuePerfDegradedThenWarningIsLoggedOnce` — requires live timing.

---

## 7. Implementation Order and Commit Strategy

Each step should be a separate commit so that build failures are easy to bisect:

```
Step 1:  feat: add CommandQueueStatistics to GreenSwamp.Alpaca.Shared
Step 2:  feat: add CompletionEvent to ICommand<TExecutor>
Step 3:  feat: implement CompletionEvent in CommandBase<TExecutor>
Step 4:  feat: overhaul CommandQueueBase — event-based signaling, statistics, diagnostic logging
Step 5:  feat: expose Statistics on ICommandQueue<TExecutor>
Step 6:  feat: MountQueueImplementation — timeout 22s, diagnostic filter, Statistics facade
Step 7:  feat: SkyQueueImplementation — diagnostic filter, Statistics facade
Step 8:  test: add QueueMigrationTests for event-based queue behavior
```

Run `dotnet build` and `dotnet test` between each commit.

---

## 8. Open Decisions — All Resolved

| # | Question | Options | Decision | Rationale |
|---|----------|---------|----------|-----------|
| 1 | `IDisposable` on `CommandBase`? | A: Add `IDisposable`; B: Skip (match upstream) | **B — no `IDisposable`** | 76 production `GetCommandResult` call sites would need `using` patterns; kernel handle leak is negligible for a well-behaved queue; matches upstream exactly |
| 2 | `CompletionTimeoutMs` for Simulator | 22 s (upstream) or 40 s (current generic) | **22 s** | Match upstream GSServer `MountQueue` |
| 3 | `CommandQueueStatistics` location | `GreenSwamp.Alpaca.Shared` or `GreenSwamp.Alpaca.Mount.Commands` | **`GreenSwamp.Alpaca.Shared`** | Match upstream `GS.Shared` placement; no new project references needed |
| 4 | `DiagnosticCommandFilter` default | Empty (log all) or no-op | **Empty `[]`** | Opt-in via subclass override; matches upstream design intent |
| 5 | Exception type in `GetCommandResult` | Generic `Exception` or domain-specific | **Generic `Exception`** | Call sites treat `command.Exception` as a message carrier and never type-check it; existing exception handler in `SkyServer.Core.cs` uses stale WPF namespace strings that never match |

---

## 9. Summary of Upstream vs. Current Differences (Quick Reference)

| Area | Current `CommandQueueBase` | Upstream (target) |
|------|-----------------------------|-------------------|
| Result retrieval | `ConcurrentDictionary` + `Thread.Sleep(1)` poll (40 s max) | `ManualResetEventSlim.Wait(timeout, ct)` |
| Task startup | `_ = Task.Factory.StartNew(...)` — no ready signal | `_taskReadySignal` — waits up to 5 s for task to be ready |
| Task options | Default threadpool | `TaskCreationOptions.LongRunning` |
| Cancellation in loop | `GetConsumingEnumerable()` (no token) | `GetConsumingEnumerable(ct)` |
| `Stop()` sequence | Cancel → null | `CompleteAdding()` → `Cancel()` → `_processingTask.Wait(5s)` |
| Task reference | Discarded (`_ =`) | Stored in `_processingTask` |
| Statistics | None | `CommandQueueStatistics` (reset per `Start()`) |
| Diagnostic logging | None | Per-command timing at `Debug` with configurable type filter |
| Performance monitoring | None | State-transition warning/recovery logging |
| Exception logging | Silent | `MonitorLog` at `Warning` |
| `finally` in `ProcessCommandQueue` | None | `command.CompletionEvent.Set()` always fires |
| `_isInWarningState` | Not present | Instance field in base |
| `CleanResults` | Present (needed for dictionary) | Removed |
| Timeout per implementation | Hard-coded 40 s | Virtual `CompletionTimeoutMs` (22 s Simulator, 40 s SkyWatcher) |
