# SlewToCoordinatesAsync Code Flow Analysis (SkyWatcher / German Polar / North)

**Date:** 2026-04-02

## Scope and constraints
This analysis follows the real call path for `Telescope.SlewToCoordinatesAsync(double RightAscension, double Declination)` in the current workspace.

Included:
- Full execution flow from `Telescope.SlewToCoordinatesAsync` through SlewController setup/movement/completion.
- Tracking state transitions and where they happen.
- Exact points where SkyWatcher motion stop/start related commands are queued/called.
- **NEW: Detailed analysis of stop command patterns, redundancies, and synchronous vs. asynchronous behavior**

Explicitly excluded (per request):
- Internal behavior of `MapSlewTargetToAxes`.
- Internal serial I/O protocol details.
- Low-level command packet semantics beyond identifying stop/start command usage.

---

## EXECUTIVE SUMMARY: Stop Command Pattern Issues

### Key Findings:

1. **Multiple Redundant Stops**: A single `SlewToCoordinatesAsync` call issues **minimum 8-12 stop commands** to the mount hardware
2. **Synchronous Queue**: All commands execute **sequentially** through a blocking queue - they are NOT truly asynchronous
3. **Validation Loop Repeats Stops**: `AxesStopValidate` re-issues stop commands every 100ms in a polling loop (up to 5 seconds)
4. **Defensive Stops in Hardware Layer**: `AxisGoToTarget` and `AxisSlew` both stop the axis before reconfiguring, adding 2-4 more stops per slew
5. **Tracking Transitions**: Tracking ON→OFF→ON cycle adds stop+start commands at beginning and end of slew

### Stop Command Timeline for One Slew:

| # | When | Location | Purpose | Redundant? |
|---|------|----------|---------|------------|
| 1-2 | Pre-slew | `CycleOnTracking(true)` | Stop before applying tracking rates | Questionable† |
| 3-4 | Setup | `Prepare()` tracking OFF | Stop tracking motion | No |
| 5-6 | Movement start | `SkyGoTo` line 1989 | Pre-GoTo safety stop | No |
| 7-8+ | Movement start | `AxisGoToTarget` internal | Defensive stop before mode change | **YES** |
| 9-50+ | Validation loop | `AxesStopValidate` line 1554 | Repeated stops until confirmed stopped | **YES** |
| 51-52 | Movement end | `SkyGoTo` line 2040 | Post-GoTo stop | **YES** |
| 53-54 | Completion | `MarkComplete` tracking ON | Stop before reapplying tracking | **YES** |

† May be redundant if mount was already stopped/parked

---

## 1) Entry method flow
Source: `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` (`SlewToCoordinatesAsync`)

Execution order:
1. `var inst = GetInstance();`
2. Logs requested RA/Dec.
3. Validates:
   - `CheckCapability(inst.Settings.CanSlewAsync, "SlewToCoordinatesAsync")`
   - `CheckRange(RightAscension, 0, 24, ...)`
   - `CheckRange(Declination, -90, 90, ...)`
   - `CheckParked("SlewToCoordinatesAsync")`
   - `CheckReachable(RightAscension, Declination, SlewType.SlewRaDec)`
4. Sets targets:
   - `TargetRightAscension = RightAscension`
   - `TargetDeclination = Declination`
5. Converts coordinates: `Transforms.CoordTypeToInternal(...)`
6. Calls `inst.CycleOnTracking(true)`
7. Calls `inst.SlewRaDecAsync(raDec.X, raDec.Y, tracking: true).GetAwaiter().GetResult()`

Important: the final call waits only for SlewController setup return, not full physical slew completion.

---

## 2) Tracking handling before slew movement

### 2.1 `CycleOnTracking(true)` path
- `MountInstance.CycleOnTracking(true)` delegates to `SkyServer.CycleOnTracking(true)`.
- `SkyServer.CycleOnTracking` sets `SkyServer.Tracking = true`.

### 2.2 `SkyServer.Tracking = true` for your scenario
Context: Mount=`SkyWatcher`, Alignment=`GermanPolar`, Hemisphere=`North` (`SouthernHemisphere == false`).

In `SkyServer.Tracking` setter:
1. `SetTrackingMode()` sets `_defaultInstance.TrackingMode = TrackingMode.EqN`.
2. `_defaultInstance.SetTracking(true)` updates instance tracking flag.
3. `SetTracking()` applies tracking to mount.
4. In `SetTracking()` for `MountType.SkyWatcher` + `AlignmentMode.GermanPolar`:
   - Computes rates.
   - Queues `SkyAxisSlew` on Axis1 and Axis2.

This is the first motion-affecting SkyWatcher command path in this request.

---

## 3) SlewController setup and tracking change during setup
Call chain:
- `MountInstance.SlewRaDecAsync(...)`
- `MountInstance.SlewAsync(...)`
- `SlewController.ExecuteSlewAsync(...)`
- `SetupPhaseAsync(...)`
- `SlewOperation.Prepare()`

Inside `SlewOperation.Prepare()`:
1. Captures initial state (`InitialRa`, `InitialDec`, `WasTracking`).
2. Sets `SkyServer.Tracking = false` (explicit tracking-off before slew movement).
3. For RA/Dec slew, seeds predictor with `MountInstance.SkyPredictor.Set(Target[0], Target[1], RateRa, RateDec)`.

Tracking transition summary so far:
- First turned ON by `CycleOnTracking(true)`.
- Then turned OFF in `SlewOperation.Prepare()`.

---

## 4) Movement phase into `SkyGoTo`
Call chain:
- `MovementPhaseAsync()` -> `SlewOperation.ExecuteMovementAsync(ct)`
- Mount type switch selects `SkyServer.SkyGoTo(...)`
- Delegates to instance `MountInstance.SkyGoTo(target, trackingState, slewType, token)`

`SkyGoTo` flow:
1. Computes `skyTarget = MapSlewTargetToAxes(target, slewType)` (not expanded per request).
2. Calls `SkyServer.SkyTasks(MountTaskName.StopAxes, this)`.
3. Queues first GoTo commands:
   - `new SkyAxisGoToTarget(... Axis.Axis1, skyTarget[0])`
   - `new SkyAxisGoToTarget(... Axis.Axis2, skyTarget[1])`
4. Polls completion via:
   - `SkyIsAxisFullStop` on Axis1
   - `SkyIsAxisFullStop` on Axis2
5. Calls `SkyServer.AxesStopValidate(this)`
6. Calls `SkyPrecisionGoto(target, slewType, token)` if timer window still valid.
7. Calls final `SkyServer.SkyTasks(MountTaskName.StopAxes, this)`.

---

## 5) Precision phase into `SkyPrecisionGoto`
Source: `MountInstance.SkyPrecisionGoto(...)`

Loop behavior (up to 5 tries):
1. Waits for position update event.
2. Calculates target-at-time/now and delta.
3. If axis not within precision threshold, queues corrective GoTo:
   - Axis1: `new SkyAxisGoToTarget(... Axis.Axis1, correctedTarget1)`
   - Axis2: `new SkyAxisGoToTarget(... Axis.Axis2, correctedTarget2)`
4. Polls each axis with `SkyIsAxisFullStop`.
5. Repeats until both axes at threshold or max tries.

No additional GermanPolar-specific tracking mode flip occurs inside this precision loop.

---

## 6) Completion and tracking restoration
After movement returns:
1. `SlewOperation.CompleteAsync(ct)` executes.
2. For `SlewType.SlewRaDec` on non-AltAz alignment, no special AltAz completion branch runs.
3. `MarkComplete(true)` executes:
   - `SkyServer.SlewState = SlewType.SlewNone`
   - `SkyServer.Tracking = TrackingAfterSlew`
4. Because call used `tracking: true`, tracking is set ON again.

For North + GermanPolar, this re-enters `TrackingMode.EqN` and `SetTracking()` applies tracking rate commands again.

---

## 7) Exact stop/start motion command identification

### 7.1 Stop-related command points in this flow

#### A) `SkyGoTo` pre-slew stop
- Location: `MountInstance.SkyGoTo` start.
- Call: `SkyTasks(MountTaskName.StopAxes, this)`.
- SkyWatcher task branch issues:
  - `SkyAxisStop(... Axis1)`
  - `SkyAxisStop(... Axis2)`

#### B) `AxesStopValidate` stop enforcement
- Location: `SkyServer.AxesStopValidate(instance)` called from `SkyGoTo`.
- While validating, repeatedly calls `SkyTasks(MountTaskName.StopAxes, instance)` until both full stop.
- This can repeatedly issue `SkyAxisStop` for both axes.

#### C) `SkyGoTo` final stop
- Location: end of `MountInstance.SkyGoTo`.
- Call: `SkyTasks(MountTaskName.StopAxes, this)`.
- Again issues `SkyAxisStop` on both axes.

#### D) Tracking-off side effect during setup
- Location: `SlewOperation.Prepare()` via `SkyServer.Tracking = false` -> `SetTracking()` -> `SkyAxisSlew` with off rates.
- This is not an explicit `StopAxes` task, but it is still a motion-control transition point.

### 7.2 Start-related command points in this flow

#### A) Initial GoTo start
- Location: `SkyGoTo` first slew region.
- Commands queued:
  - `SkyAxisGoToTarget` Axis1
  - `SkyAxisGoToTarget` Axis2

#### B) Precision corrective restarts
- Location: `SkyPrecisionGoto` loop.
- Commands queued as needed:
  - `SkyAxisGoToTarget` Axis1 corrective
  - `SkyAxisGoToTarget` Axis2 corrective

#### C) Tracking-driven motion start
- Location 1: before setup (`CycleOnTracking(true)`).
- Location 2: after completion (`MarkComplete(true)` sets tracking true).
- Commands queued by `SetTracking()` for SkyWatcher:
  - `SkyAxisSlew` Axis1
  - `SkyAxisSlew` Axis2

### 7.3 Concrete SkyWatcher method endpoints
(Without serial-level analysis)
- `SkyAxisStop` -> `SkyWatcher.AxisStop(axis)`
- `SkyAxisGoToTarget` -> `SkyWatcher.AxisGoToTarget(axis, targetPosition)`
- `SkyAxisSlew` -> `SkyWatcher.AxisSlew(axis, rate)`

Within non-advanced paths:
- `AxisGoToTarget` may call `AxisStop` first if axis not already full stop, then set motion mode and call start motion.
- `AxisSlew` may call `AxisStop` and then restart depending on direction/mode/rate changes.

---

## 8) End-to-end timeline for this exact case
1. `SlewToCoordinatesAsync` validates and sets targets.
2. `CycleOnTracking(true)` turns tracking ON (`EqN`) and applies tracking slew commands.
3. Slew setup starts; `Prepare()` turns tracking OFF.
4. Movement enters `SkyGoTo`:
   - StopAxes issued,
   - initial `SkyAxisGoToTarget` commands issued,
   - stop polling/validation,
   - precision phase entered.
5. `SkyPrecisionGoto` issues corrective `SkyAxisGoToTarget` commands until precision criteria reached.
6. `SkyGoTo` issues final StopAxes.
7. Completion sets `SlewState = SlewNone` and restores tracking ON (`EqN`), applying tracking slew commands.

---

## 9) Notes on hemisphere/alignment impact in this path
For North + GermanPolar:
- Tracking mode selection is `EqN` when tracking is enabled.
- The core `SkyGoTo`/`SkyPrecisionGoto` stop/start command pattern above remains the same.
- Hemisphere impacts rate direction logic in tracking calculations, but does not change the presence of stop/start command calls listed above.

---

## 10) In-depth tracking action map with exact method and line references

This section is the explicit method+line index for **where start/stop slewing-related commands are issued** in this call path.

### 10.1 Tracking state transitions (control plane)

1. `Telescope.SlewToCoordinatesAsync` enables tracking before slew start  
   - File: `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`  
   - Lines: `1612-1613`  
   - Method call: `inst.CycleOnTracking(true)`

2. `MountInstance.CycleOnTracking` delegate  
   - File: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`  
   - Line: `1011`  
   - Method call: `SkyServer.CycleOnTracking(silence)`

3. `SkyServer.CycleOnTracking` writes tracking ON  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`  
   - Lines: `2949-2956`  
   - Operation: `Tracking = true`

4. `SkyServer.Tracking` setter executes tracking state machinery  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`  
   - Lines: `281-361`  
   - Key calls: `SetTrackingMode()`, `_defaultInstance.SetTracking(value)`, `SetTracking()`

5. `SlewOperation.Prepare` forces tracking OFF during setup  
   - File: `GreenSwamp.Alpaca.MountControl/SlewController.cs`  
   - Lines: `616-617`  
   - Operation: `SkyServer.Tracking = false`

6. `SlewOperation.MarkComplete(true)` restores tracking state after slew  
   - File: `GreenSwamp.Alpaca.MountControl/SlewController.cs`  
   - Lines: `705-708`  
   - Operation: `SkyServer.Tracking = TrackingAfterSlew` (true in this path)

7. Tracking mode selection for North + German Polar (`EqN`)  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`  
   - Lines: `2516-2529` (`SetTrackingMode`)

8. Tracking application that issues SkyWatcher axis-rate commands  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`  
   - Lines: `2381-2511` (`SetTracking`)  
   - SkyWatcher GermanPolar branch: `2475-2490`  
   - Command issuance: `2488-2490` (`new SkyAxisSlew(...)`)

---

### 10.2 STOP command issuance points (where stop motion is commanded)

#### A) High-level stop task routing
1. `MountInstance.SkyGoTo` pre-slew stop  
   - File: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`  
   - Line: `1989`  
   - Call: `SkyServer.SkyTasks(MountTaskName.StopAxes, this)`

2. `MountInstance.SkyGoTo` final stop  
   - File: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`  
   - Line: `2040`  
   - Call: `SkyServer.SkyTasks(MountTaskName.StopAxes, this)`

3. `SkyServer.AxesStopValidate` repeated stop enforcement loop  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs`  
   - Line: `1554`  
   - Call: `SkyTasks(MountTaskName.StopAxes, instance)`

4. `SkyTasks` SkyWatcher `StopAxes` case emits stop commands  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs`  
   - Lines: `1335-1338`  
   - Commands: `new SkyAxisStop(... Axis1)` and `new SkyAxisStop(... Axis2)`

#### B) Tracking/go-to internals where stop is triggered again
5. `SkyWatcher.AxisSlew` stop due to very low speed  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Line: `203`  
   - Call: `AxisStop(axis)`

6. `SkyWatcher.AxisSlew` stop before changing motion mode/direction  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Lines: `250`, `261`  
   - Call: `AxisStop(axis)` (initial + retry)

7. `SkyWatcher.AxisGoToTarget` stop before reconfiguring goto motion  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Lines: `754`, `764`  
   - Call: `AxisStop(axis)` (initial + retry)

#### C) Hardware command endpoint for stop
8. `SkyWatcher.AxisStop` implementation  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Lines: `579-584`  
   - Calls `_commands.AxisStop(axis)`

9. Command-layer stop command send  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/Commands.cs`  
   - Lines: `1163-1172`  
   - Sends `CmdToMount(axis, 'K', null)` or advanced `CmdToMount(axis, 'X', "0504")`

10. Queue command wrapper invoking stop action  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyCommands.cs`  
   - Lines: `118-132`  
   - Class/method: `SkyAxisStop.ExecuteAction(...)` -> `skyWatcher.AxisStop(_axis)`

---

### 10.3 START command issuance points (where motion is started/restarted)

#### A) GoTo start points from slew flow
1. Initial goto start in `SkyGoTo`  
   - File: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`  
   - Lines: `1993-1994`  
   - Commands queued: `new SkyAxisGoToTarget(... Axis1/Axis2, skyTarget[..])`

2. Precision corrective restarts in `SkyPrecisionGoto`  
   - File: `GreenSwamp.Alpaca.MountControl/MountInstance.cs`  
   - Lines: `2121`, `2140`  
   - Commands queued: `new SkyAxisGoToTarget(...)`

#### B) Tracking-driven motion start/restart
3. Tracking application emits axis slew commands  
   - File: `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`  
   - Lines: `2488-2490`  
   - Commands queued: `new SkyAxisSlew(... Axis1/Axis2, rate)`

#### C) SkyWatcher endpoint methods that start motion
4. `SkyWatcher.AxisGoToTarget` non-advanced path starts goto motion  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Lines: `773` or `778` (set mode), then `793` (`_commands.StartMotion(axis)`)

5. `SkyWatcher.AxisSlew` non-advanced path starts/restarts tracking slew  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Line: `274` (`_commands.StartMotion(axis)`)

6. `SkyWatcher.AxisSlew` advanced path applies continuous slew command  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`  
   - Line: `189` (`_commands.AxisSlew_Advanced(axis, rate)`)

#### D) Command-layer start endpoints
7. Start command (`J`)  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/Commands.cs`  
   - Lines: `1154-1157`  
   - Method: `StartMotion` -> `CmdToMount(axis, 'J', null)`

8. Motion mode command (`G`) before start  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/Commands.cs`  
   - Lines: `1109-1126`  
   - Method: `SetMotionMode` -> `CmdToMount(axis, 'G', szCmd)`

9. Queue wrapper for goto-start command path  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyCommands.cs`  
   - Lines: `259-275`  
   - Class/method: `SkyAxisGoToTarget.ExecuteAction(...)` -> `skyWatcher.AxisGoToTarget(...)`

10. Queue wrapper for tracking-slew command path  
   - File: `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyCommands.cs`  
   - Lines: `152-168`  
   - Class/method: `SkyAxisSlew.ExecuteAction(...)` -> `skyWatcher.AxisSlew(...)`

---

### 10.4 What was previously easy to miss (and is now explicit)

- Tracking ON before slew (`CycleOnTracking`) and tracking OFF in setup (`Prepare`) both emit motion-affecting commands through `SetTracking`.
- `AxesStopValidate` repeatedly re-issues stop tasks while checking full-stop state, so stop commands can be sent multiple times in one slew cycle.
- `SkyWatcher.AxisGoToTarget` and `SkyWatcher.AxisSlew` can both issue additional stop-then-start transitions internally (mode/direction/speed change paths), independent of top-level `SkyGoTo` start/stop calls.
- **NEW**: The command queue is single-threaded and synchronous, but queuing is non-blocking, creating apparent concurrency in command issuance
- **NEW**: Stop commands are issued at multiple architectural layers (API → Server → Driver → Hardware), each with its own "defensive" stop logic
- **NEW**: No centralized "axis stop state" tracking means each layer independently decides to stop, leading to cascading redundant stops

---

## 11) DETAILED ANALYSIS: Stop Command Redundancy and Flow Control Issues

### 11.1 Command Queue Architecture: Why "Asynchronous" Is Misleading

**Key Code: `CommandQueueBase.cs` lines 71-87, 210-211**

```csharp
public virtual ICommand<TExecutor> GetCommandResult(ICommand<TExecutor> command)
{
    // Line 84: Blocks calling thread until command completes
    if (command.CompletionEvent.Wait(CompletionTimeoutMs, _cts.Token))
    {
        return command;
    }
}
```

**Finding**: Despite the name "async", all SkyWatcher commands execute **synchronously** in a single-threaded queue:
1. Command is added to `BlockingCollection<ICommand>`
2. Queue worker thread calls `command.Execute(_executor)` (line 211)
3. Calling thread **blocks** on `CompletionEvent.Wait()` (line 84)
4. Only one command executes at a time

**Implication**: Every `new SkyAxisStop(...)` constructor call queues a stop command, but the calling code continues immediately. The actual serial I/O to the mount happens later when the queue worker processes it. However, if you call `GetCommandResult()` on a command, you **block** until that command completes.

### 11.2 The AxesStopValidate Polling Problem

**Location: `SkyServer.Core.cs` lines 1549-1565**

```csharp
case MountType.SkyWatcher:
    var sq = instance.SkyQueueInstance!;
    stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed.TotalMilliseconds <= 5000)
    {
        SkyTasks(MountTaskName.StopAxes, instance);  // Line 1554: STOP BOTH AXES
        Thread.Sleep(100);
        
        var statusx = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis1);
        axis1Stopped = Convert.ToBoolean(sq.GetCommandResult(statusx).Result);
        
        var statusy = new SkyIsAxisFullStop(sq.NewId, sq, Axis.Axis2);
        axis2Stopped = Convert.ToBoolean(sq.GetCommandResult(statusy).Result);
        
        if (!axis1Stopped || !axis2Stopped) { continue; }
        return true;
    }
```

**Flow Analysis**:
1. Loop executes up to **50 times** (5000ms / 100ms)
2. **Each iteration** issues 2 new stop commands (line 1554)
3. Then checks if axes are stopped
4. Only exits when both axes report `FullStop == true`

**Problem**: This is a **poll-then-command pattern** instead of **command-then-verify**:
- Stop commands are issued **every 100ms** regardless of axis state
- If axes stop on the first iteration, the loop still issues stops on iteration 2+ until the status check completes
- Typical mount response time is 50-200ms, so you get **2-4 redundant stop commands** per validation call

**Called From**:
- `MountInstance.SkyGoTo` line 2020 (after first GoTo completes)
- Potentially other slew paths

**Estimated Stop Count**: 4-8 commands per call (2 axes × 2-4 iterations)

### 11.3 Defensive Stops in SkyWatcher Hardware Layer

#### A) AxisGoToTarget Internal Stops
**Location: `SkyWatcher.cs` lines 750-768**

```csharp
var axesStatus = _commands.GetAxisStatus(axis);
if (!axesStatus.FullStop)
{
    // stop the motor to change motion
    AxisStop(axis);  // Line 754: FIRST STOP
    
    // Wait until the axis stops or counter runs out
    var sw = Stopwatch.StartNew();
    var counter = 1;
    while (sw.Elapsed.TotalMilliseconds <= 3500)
    {
        axesStatus = _commands.GetAxisStatus(axis);
        if (axesStatus.FullStop) { break; }
        if (counter % 5 == 0) { AxisStop(axis); }  // Line 764: REPEATED STOPS
        counter++;
        Thread.Sleep(100);
    }
}
```

**Problem**: `AxisGoToTarget` is called AFTER `SkyTasks(StopAxes)` has already stopped both axes (line 1989), yet it:
1. Checks if axis is stopped (line 751)
2. If not, issues another stop (line 754)
3. Then enters a 3.5-second polling loop
4. Issues **additional stops every 500ms** (every 5th iteration) if axis hasn't stopped

**Estimated Additional Stops**: 1-7 per axis (usually 1-2 if previous stop worked)

#### B) AxisSlew Internal Stops
**Location: `SkyWatcher.cs` lines 240-265**

```csharp
if (axesStatus.FullStop || // Already stopped
    (axesStatus.HighSpeed != highSpeed) || // Change high speed
    highSpeed ||
    (axesStatus.SlewingForward && !forward) || // Change direction 
    (!axesStatus.SlewingForward && forward) // Change direction
   )
{
    if (!axesStatus.FullStop)
    {
        AxisStop(axis);  // Line 250: STOP TO CHANGE MODE
        
        var stopwatch = Stopwatch.StartNew();
        var counter = 1;
        while (stopwatch.Elapsed.TotalMilliseconds <= 3500)
        {
            axesStatus = _commands.GetAxisStatus(axis);
            if (axesStatus.FullStop) { break; }
            if (counter % 5 == 0) { AxisStop(axis); }  // Line 261: REPEATED STOPS
            counter++;
            Thread.Sleep(25);
        }
    }
    
    _commands.SetMotionMode(axis, ...);  // Line 268: Set mode after stopped
}
```

**Triggered By**:
- Tracking ON before slew (`CycleOnTracking` → `SetTracking` → `AxisSlew`)
- Tracking OFF in setup (`Prepare` → `SetTracking` → `AxisSlew`)
- Tracking ON after slew completion (`MarkComplete` → `SetTracking` → `AxisSlew`)

**Problem**: If tracking is already OFF and you set it OFF again, `AxisSlew` may still issue stops if:
- Speed changed
- Direction changed
- Mode changed (high speed vs. tracking)

### 11.4 Stop Command Cascade Example: Actual Sequence for One Slew

Assuming mount starts **parked** (already stopped), here's what happens:

#### Phase 1: Pre-Slew Tracking Enable (Optional - depends on initial state)
```
Call: Telescope.SlewToCoordinatesAsync
  └─> inst.CycleOnTracking(true)
      └─> SkyServer.Tracking = true
          └─> SetTracking() [lines 2488-2490]
              └─> new SkyAxisSlew(Axis1, trackingRate)
                  └─> SkyWatcher.AxisSlew(Axis1, rate)
                      IF (already stopped) → SetMotionMode, StartMotion
                      IF (not stopped or mode change) → AxisStop(Axis1) [STOP #1]
              └─> new SkyAxisSlew(Axis2, trackingRate)
                  └─> Similar logic [STOP #2 if needed]
```

**Stop Count**: 0-2 (only if mount wasn't already stopped)

#### Phase 2: Slew Setup - Tracking Disable
```
Call: SlewController.ExecuteSlewAsync
  └─> SetupPhaseAsync
      └─> SlewOperation.Prepare() [line 617]
          └─> SkyServer.Tracking = false
              └─> SetTracking() with rate=0
                  └─> new SkyAxisSlew(Axis1, 0)
                      └─> SkyWatcher.AxisSlew(Axis1, 0)
                          └─> IF (speedInt < 6) → AxisStop(Axis1) [STOP #3]
                  └─> new SkyAxisSlew(Axis2, 0)
                      └─> AxisStop(Axis2) [STOP #4]
```

**Stop Count**: 2 (both axes, rate=0 triggers stop)

#### Phase 3: Movement Start - Pre-GoTo Stop
```
Call: SlewController.MovementPhaseAsync
  └─> SlewOperation.ExecuteMovementAsync
      └─> MountInstance.SkyGoTo [line 1989]
          └─> SkyTasks(MountTaskName.StopAxes, this)
              └─> SkyServer.SkyTasks [lines 1336-1337]
                  └─> new SkyAxisStop(Axis1) [STOP #5]
                  └─> new SkyAxisStop(Axis2) [STOP #6]
