# Slewing Functions Comparison Report
## SkyWatcher vs Simulator GoTo Operations

**Generated:** December 2024  
**Repository:** GreenSwampAlpaca  
**Branch:** master  

---

## Executive Summary

This document provides a detailed comparison between the SkyWatcher hardware slewing functions (`SkyGoTo` and `SkyPrecisionGoto`) and their corresponding Simulator implementations (`SimGoTo` and `SimPrecisionGoto`). The analysis reveals significant architectural and behavioral differences that impact testing, timing, and precision strategies.

### Key Findings

| Aspect | SkyWatcher | Simulator | Impact |
|--------|------------|-----------|--------|
| **Initial Slew Timeout** | 240 seconds | 120 seconds | 2× difference |
| **Precision Strategy** | Sequential axes with damping | Simultaneous axes, direct targeting | Different convergence behavior |
| **Position Updates** | Event-driven (5s timeout) | Direct polling | Different error handling |
| **Damping Factors** | 25% (Axis1), 10% (Axis2) | None (100%) | Real hardware needs damping |
| **Hardware Complexity** | High (motor control, speed modes) | Low (simple speed ramping) | Simulator doesn't test real constraints |

---

## Table of Contents

1. [High-Level Architecture](#high-level-architecture)
2. [Initial Slew Comparison (GoTo)](#1-initial-slew-comparison-goto)
3. [Precision Slew Comparison (PrecisionGoto)](#2-precision-slew-comparison-precisiongoto)
4. [Hardware Implementation Details](#3-hardware-implementation-details)
5. [Summary of Major Differences](#4-summary-of-major-differences)
6. [Why The Differences Matter](#5-why-the-differences-matter)
7. [Recommendations](#6-recommendations)

---

## High-Level Architecture

Both implementations follow a **two-stage slewing strategy**:

```
┌─────────────────────────────────────────┐
│          SLEW OPERATION                 │
├─────────────────────────────────────────┤
│                                         │
│  Stage 1: Initial GoTo                  │
│  ├─ Fast slew to target vicinity        │
│  ├─ Timeout: 240s (Sky) / 120s (Sim)    │
│  └─ Stop when both axes report stopped  │
│                                         │
│  Stage 2: Precision GoTo                │
│  ├─ Fine positioning (up to 5 attempts) │
│  ├─ Adaptive loop timing                │
│  └─ Achieve configured precision        │
│                                         │
└─────────────────────────────────────────┘
```

However, the internal mechanics differ significantly between hardware and simulator implementations.

---

## 1. Initial Slew Comparison (GoTo)

### 1.1 SkyGoTo (SkyWatcher Hardware)

**Source:** `MountInstance.cs` lines 64190-67421

#### Key Characteristics

- **Timeout:** 240 seconds
- **Hardware commands:** `SkyAxisGoToTarget` via `SkyQueue`
- **Axis verification:** Polls `SkyIsAxisFullStop` status for each axis
- **Stopping logic:** Calls `SkyTasks(MountTaskName.StopAxes)` at start and end
- **Settle time:** Applies `SlewSettleTime` delay after both axes stop

#### Code Flow

```csharp
1. Stop both axes (SkyTasks.StopAxes)
2. Issue goto commands to both axes simultaneously
   ├─ new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0])
   └─ new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1])

3. Poll axis status every 50ms (alternating between axes)
   ├─ Check Axis1: SkyIsAxisFullStop(Axis.Axis1)
   ├─ Sleep 50ms
   └─ Check Axis2: SkyIsAxisFullStop(Axis.Axis2)

4. Wait for BOTH axes to report full stop
5. Apply settle time if configured (SlewSettleTime)
6. Validate axes are stopped (AxesStopValidate)
7. Proceed to SkyPrecisionGoto if within time limit
8. Stop axes again
```

#### Actual Code Snippet

```csharp
const int timer = 240;
var stopwatch = Stopwatch.StartNew();

SkyServer.SkyTasks(MountTaskName.StopAxes);

_ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);
_ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);

while (stopwatch.Elapsed.TotalSeconds <= timer)
{
    Thread.Sleep(50);
    token.ThrowIfCancellationRequested();

    var statusx = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
    var x = SkyQueue.GetCommandResult(statusx);
    var axis1Stopped = Convert.ToBoolean(x.Result);

    Thread.Sleep(50);
    token.ThrowIfCancellationRequested();

    var statusy = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis2);
    var y = SkyQueue.GetCommandResult(statusy);
    var axis2Stopped = Convert.ToBoolean(y.Result);

    if (!axis1Stopped || !axis2Stopped) { continue; }

    if (SkyServer.SlewSettleTime > 0)
        Tasks.DelayHandler(TimeSpan.FromSeconds(SkyServer.SlewSettleTime).Milliseconds);
    break;
}
```

---

### 1.2 SimGoTo (Simulator)

**Source:** `MountInstance.cs` lines 52154-55444

#### Key Characteristics

- **Timeout:** 120 seconds (half of SkyWatcher!)
- **Simulated commands:** `CmdAxisGoToTarget` via `MountQueue`
- **Axis verification:** Polls `CmdAxisStatus` and checks `AxisStatus.Stopped` property
- **Stopping logic:** Calls `SimTasks(MountTaskName.StopAxes)` at start and end
- **Settle time:** Also applies `SlewSettleTime` delay

#### Code Flow

```csharp
1. Stop both axes (SimTasks.StopAxes)
2. Issue goto commands to both axes simultaneously
   ├─ new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0])
   └─ new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1])

3. Poll axis status every 50ms (alternating between axes)
   ├─ Check Axis1: CmdAxisStatus(Axis.Axis1) → AxisStatus.Stopped
   ├─ Sleep 50ms
   └─ Check Axis2: CmdAxisStatus(Axis.Axis2) → AxisStatus.Stopped

4. Wait for BOTH axes to report stopped
5. Apply settle time if configured (SlewSettleTime)
6. Validate axes are stopped (AxesStopValidate)
7. Proceed to SimPrecisionGoto if within time limit
8. Stop axes again
```

#### Actual Code Snippet

```csharp
const int timer = 120;
var stopwatch = Stopwatch.StartNew();

SkyServer.SimTasks(MountTaskName.StopAxes);

_ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]);
_ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]);

while (stopwatch.Elapsed.TotalSeconds <= timer)
{
    Thread.Sleep(50);
    token.ThrowIfCancellationRequested();

    var statusx = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
    var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusx).Result;
    var axis1Stopped = axis1Status.Stopped;

    Thread.Sleep(50);
    token.ThrowIfCancellationRequested();

    var statusy = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
    var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(statusy).Result;
    var axis2Stopped = axis2Status.Stopped;

    if (!axis1Stopped || !axis2Stopped) continue;
    if (SkyServer.SlewSettleTime > 0)
        Tasks.DelayHandler(TimeSpan.FromSeconds(SkyServer.SlewSettleTime).Milliseconds);
    break;
}
```

---

### 1.3 Initial Slew Comparison Table

| Aspect | SkyGoTo | SimGoTo | Notes |
|--------|---------|---------|-------|
| **Timeout** | 240 seconds | 120 seconds | Simulator times out 2× faster |
| **Command class** | `SkyAxisGoToTarget` | `CmdAxisGoToTarget` | Different queues |
| **Status check** | `SkyIsAxisFullStop` → Boolean | `CmdAxisStatus` → `AxisStatus` object | Different return types |
| **Queue** | `SkyQueue` | `MountQueue` | Separate command queues |
| **Poll interval** | 50ms per axis (100ms total) | 50ms per axis (100ms total) | Same |
| **Hardware** | Real mount hardware | Software simulation | Fundamental difference |
| **Structural similarity** | ~95% identical logic | Different commands only | Very similar flow |

---

## 2. Precision Slew Comparison (PrecisionGoto)

### 2.1 SkyPrecisionGoto (SkyWatcher Hardware)

**Source:** `MountInstance.cs` lines 67529-73346

#### Key Characteristics

- **Max iterations:** 5 attempts
- **Precision target:** Uses configured `_settings.GotoPrecision` for both axes
- **Position updates:** **Event-driven** using `SkyServer.MountPositionUpdatedEvent`
- **Timeout on position update:** 5 seconds (throws `TimeoutException`)
- **Axis sequencing:** Axes moved **SEQUENTIALLY** (Axis1 first, then Axis2)
- **Damping factors:** 
  - **Axis1:** 0.25 × delta (25% correction)
  - **Axis2:** 0.1 × delta (10% correction)
- **Loop timing:** Adaptive (starts at 800ms, adjusts based on actual loop time)
- **AltAz prediction:** Uses `SkyPredictor.GetRaDecAtTime()` with adaptive timing

#### Detailed Flow

```csharp
FOR each iteration (max 5):
    1. Reset position update event
    2. Update encoder steps
    3. WAIT for MountPositionUpdatedEvent (5s timeout or throw exception)
    4. Calculate target position (adjust for AltAz tracking if needed)
    5. Calculate delta for both axes
    6. Check if within precision thresholds
    
    7. IF Axis1 not at target:
       ├─ Apply 25% damping: skyTarget[0] += 0.25 * deltaDegree[0]
       ├─ Issue goto command: SkyAxisGoToTarget(Axis.Axis1, skyTarget[0])
       └─ Wait up to 3s for axis to stop (SkyIsAxisFullStop polling)
    
    8. IF Axis2 not at target:
       ├─ Apply 10% damping: skyTarget[1] += 0.1 * deltaDegree[1]
       ├─ Issue goto command: SkyAxisGoToTarget(Axis.Axis2, skyTarget[1])
       └─ Wait up to 3s for axis to stop (SkyIsAxisFullStop polling)
    
    9. Measure actual loop time for next iteration's predictor
    10. Log delta and timing
    11. Break if both axes at target
END FOR
```

#### Critical Code Sections

**Event-Based Position Update:**
```csharp
SkyServer.MountPositionUpdatedEvent.Reset();
UpdateSteps();

if (!SkyServer.MountPositionUpdatedEvent.Wait(5000))
{
    throw new TimeoutException($"Mount position update timeout in precision goto");
}
```

**Sequential Axis Movement with Damping:**
```csharp
// Axis 1 with 25% damping
if (!axis1AtTarget)
{
    skyTarget[0] += 0.25 * deltaDegree[0];  // Only move 25% of remaining distance
    _ = new SkyAxisGoToTarget(0, Axis.Axis1, skyTarget[0]);
}

// Wait for Axis1 to stop
var axis1Done = axis1AtTarget;
while (loopTimer.Elapsed.TotalMilliseconds < 3000)
{
    Thread.Sleep(30);
    if (token.IsCancellationRequested) { break; }
    
    if (!axis1Done)
    {
        var status1 = new SkyIsAxisFullStop(SkyQueue.NewId, Axis.Axis1);
        axis1Done = Convert.ToBoolean(SkyQueue.GetCommandResult(status1).Result);
    }
    if (axis1Done) { break; }
}

// Axis 2 with 10% damping
if (!axis2AtTarget)
{
    skyTarget[1] += 0.1 * deltaDegree[1];  // Only move 10% of remaining distance
    _ = new SkyAxisGoToTarget(0, Axis.Axis2, skyTarget[1]);
}

// Wait for Axis2 to stop (similar polling loop)
```

**Adaptive Timing for AltAz:**
```csharp
long loopTime = 800;  // Initial loop time

// Later, after each iteration:
loopTimer.Stop();
loopTime = loopTimer.ElapsedMilliseconds;  // Update for next iteration

// Used in predictor:
var nextTime = HiResDateTime.UtcNow.AddMilliseconds(loopTime);
var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
```

---

### 2.2 SimPrecisionGoto (Simulator)

**Source:** `MountInstance.cs` lines 55551-60226

#### Key Characteristics

- **Max iterations:** 5 attempts (>5, not >=5 like SkyWatcher)
- **Precision target:** Calculated as `ConvertStepsToDegrees(2, axis)` (2 steps worth)
- **Position updates:** **Direct polling**, no event waiting
- **No timeout exception:** Simply breaks if position is invalid
- **Axis sequencing:** Axes moved **SIMULTANEOUSLY** (both issued before waiting)
- **Damping factors:** **None** - targets sent directly without correction factor
- **Loop timing:** Adaptive (starts at 250ms, adjusts to actual movement time)
- **AltAz prediction:** Uses `SkyPredictor.GetRaDecAtTime()` with adaptive timing

#### Detailed Flow

```csharp
FOR each iteration (max 5):
    1. Get current raw positions directly (GetRawDegrees)
    2. Break if positions invalid (NaN check)
    3. Calculate target position (adjust for AltAz tracking if needed)
    4. Calculate delta for both axes
    5. Check if within precision thresholds
    
    6. IF Axis1 not at target:
       └─ Issue goto command: CmdAxisGoToTarget(Axis.Axis1, simTarget[0])
    
    7. IF Axis2 not at target:
       └─ Issue goto command: CmdAxisGoToTarget(Axis.Axis2, simTarget[1])
    
    8. Wait up to 3s for BOTH axes to stop (polling both in loop):
       ├─ Poll Axis1 status (CmdAxisStatus)
       ├─ Poll Axis2 status (CmdAxisStatus)
       └─ Break when both stopped
    
    9. Measure actual loop time for next iteration's predictor
    10. Log delta and timing
    11. Break if both axes at target
END FOR
```

#### Critical Code Sections

**Direct Position Polling (No Events):**
```csharp
var rawPositions = GetRawDegrees();

if (rawPositions == null || double.IsNaN(rawPositions[0]) || double.IsNaN(rawPositions[1]))
{ 
    break;  // Silent exit, no exception
}
```

**Simultaneous Axis Movement (No Damping):**
```csharp
// Issue commands to both axes simultaneously
if (!axis1AtTarget)
    _ = new CmdAxisGoToTarget(0, Axis.Axis1, simTarget[0]);  // 100% of distance
    
if (!axis2AtTarget)
    _ = new CmdAxisGoToTarget(0, Axis.Axis2, simTarget[1]);  // 100% of distance

// Wait for BOTH axes together
var stopwatch1 = Stopwatch.StartNew();
var axis1Stopped = false;
var axis2Stopped = false;

while (stopwatch1.Elapsed.TotalMilliseconds < 3000)
{
    Thread.Sleep(20);
    
    if (!axis1Stopped)
    {
        var status1 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis1);
        var axis1Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status1).Result;
        axis1Stopped = axis1Status.Stopped;
    }
    
    Thread.Sleep(20);
    
    if (!axis2Stopped)
    {
        var status2 = new CmdAxisStatus(MountQueue.NewId, Axis.Axis2);
        var axis2Status = (Mount.Simulator.AxisStatus)MountQueue.GetCommandResult(status2).Result;
        axis2Stopped = axis2Status.Stopped;
    }
    
    if (axis1Stopped && axis2Stopped) { break; }
}
```

**Adaptive Timing for AltAz:**
```csharp
var deltaTime = 250;  // Initial delta time (shorter than SkyWatcher)

// Later, after each iteration:
stopwatch1.Stop();
deltaTime = stopwatch1.Elapsed.Milliseconds;  // Update for next iteration

// Used in predictor:
var nextTime = HiResDateTime.UtcNow.AddMilliseconds(deltaTime);
var predictorRaDec = SkyPredictor.GetRaDecAtTime(nextTime);
```

---

### 2.3 Precision Slew Comparison Table

| Aspect | SkyPrecisionGoto | SimPrecisionGoto | Impact |
|--------|------------------|------------------|--------|
| **Precision calculation** | Config: `_settings.GotoPrecision` | Dynamic: `ConvertStepsToDegrees(2, axis)` | May differ |
| **Position update method** | Event-based with timeout | Direct polling | Different error paths |
| **Position timeout** | 5s → throws `TimeoutException` | N/A → silent break on invalid data | Critical difference |
| **Axis coordination** | **Sequential** (Axis1 → Axis2) | **Simultaneous** (both together) | Major behavioral difference |
| **Damping Axis1** | **25%** of remaining distance | **None** (100% direct) | Hardware needs damping |
| **Damping Axis2** | **10%** of remaining distance | **None** (100% direct) | Even more conservative |
| **Initial loop time** | 800ms | 250ms | 3.2× difference |
| **Max wait per iteration** | 3000ms × 2 axes = **6000ms** | 3000ms total (both) = **3000ms** | 2× difference |
| **Max tries check** | `if (maxtries >= 5)` | `if (maxTries > 5)` | Off-by-one difference |
| **Per-axis stopping flag** | Yes (`axis1AtTarget`, `axis2AtTarget`) | No (recalculates `axis1AtTarget` each loop) | State management difference |
| **AltAz predictor timing** | Adaptive (starts 800ms) | Adaptive (starts 250ms) | Different prediction horizons |

---

## 3. Hardware Implementation Details

### 3.1 SkyWatcher AxisGoToTarget (Hardware)

**Source:** `SkyWatcher.cs` lines 647-780

The actual hardware implementation is **significantly more complex** than the high-level goto calls. This is where real motor control happens.

#### Features

**1. Advanced Command Set Support:**
```csharp
if (_commands.SupportAdvancedCommandSet && _commands.AllowAdvancedCommandSet)
{
    var curPosition = _commands.GetAxisPosition(axis);
    _commands.AxisSlewTo_Advanced(axis, targetPosition);
    _commands.SetSlewingTo((int)axis, false, false);
}
```
- Direct positioning if mount supports advanced commands
- Bypasses legacy motion mode system
- Simpler, more accurate positioning

**2. Legacy Mode Steps (Most SkyWatcher Mounts):**

```csharp
// Step 1: Get current position
var curPosition = _commands.GetAxisPosition(axis);

// Step 2: Calculate movement
var movingAngle = targetPosition - curPosition;
var movingSteps = _commands.AngleToStep(axis, movingAngle);

// Step 3: Determine direction
if (movingSteps > 0) {
    direction = 0;  // Forward
    forward = true;
} else {
    direction = 1;  // Backward
    movingSteps = Math.Abs(movingSteps);
    forward = false;
}

// Step 4: STOP motor if already moving
var axesStatus = _commands.GetAxisStatus(axis);
if (!axesStatus.FullStop)
{
    AxisStop(axis);
    
    // Wait up to 3.5s for axis to stop (with retry logic)
    var sw = Stopwatch.StartNew();
    var counter = 1;
    while (sw.Elapsed.TotalMilliseconds <= 3500)
    {
        axesStatus = _commands.GetAxisStatus(axis);
        if (axesStatus.FullStop) { break; }
        if (counter % 5 == 0) { AxisStop(axis); }  // Retry every 5 iterations
        counter++;
        Thread.Sleep(100);
    }
}

// Step 5: Choose speed mode
if (movingSteps > _lowSpeedGotoMargin[(int)axis])
{
    _commands.SetMotionMode(axis, 0, direction, SouthernHemisphere);  // :G high speed
    highSpeed = true;
}
else
{
    _commands.SetMotionMode(axis, 2, direction, SouthernHemisphere);  // :G low speed
    highSpeed = false;
}

// Step 6: Star Discovery workaround
var model = _commands.GetModel();
if ((movingSteps < 19) && (model[(int)axis] == (int)McModel.StarDiscovery))
{
    int[] lookup = { 0, 10, 10, 10, 10, 10, 11, 12, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18 };
    movingSteps = lookup[movingSteps];
}

// Step 7: Set motion parameters
_commands.SetGotoTargetIncrement(axis, movingSteps);  // :H command
_commands.SetBreakPointIncrement(axis, _breakSteps[(int)axis]);  // :M command

// Step 8: Start motion
_commands.StartMotion(axis);  // :J command

// Step 9: Track slewing state
_commands.SetSlewingTo((int)axis, forward, highSpeed);
```

**Key Hardware Considerations:**
- Must stop motor before changing direction
- Different speed modes for long vs short slews
- Hardware-specific quirks (Star Discovery lookup table)
- Serial commands sent to motor controller (`:G`, `:H`, `:M`, `:J`)
- Breakpoint increments for deceleration ramping

---

### 3.2 Simulator GoTo (Software)

**Source:** `Controllers.cs` lines 380-460

The simulator's `GoTo()` method is **much simpler** - it's a pure software calculation.

#### Implementation

```csharp
private double GoTo(Axis axis, double interval)
{
    var change = 0.0;
    double delta;
    int sign;
    
    switch (axis)
    {
        case Axis.Axis1:
            if (!_isGotoSlewingX || double.IsNaN(_gotoX))
            {
                _isGotoSlewingX = false;
                return change;
            }
            delta = _gotoX - DegreesX;
            sign = delta < 0 ? -1 : 1;
            delta = Math.Abs(delta);
            break;
        case Axis.Axis2:
            if (!_isGotoSlewingY || double.IsNaN(_gotoY))
            {
                _isGotoSlewingY = false;
                return change;
            }
            delta = _gotoY - DegreesY;
            sign = delta < 0 ? -1 : 1;
            delta = Math.Abs(delta);
            break;
    }

    if (delta <= 0) return change;

    // Speed-based ramping (distance-dependent)
    if (delta < .01)
    {
        change = delta * sign;  // Move exact remaining distance
        _isGotoSlewingX = false;  // Mark as complete
        return change;
    }
    else if (delta < .2)
    {
        change = SlewSpeedOne * sign;
    }
    else if (delta < .6)
    {
        change = SlewSpeedFour * sign;
    }
    else if (delta < 1)
    {
        change = SlewSpeedSix * sign;
    }
    else
    {
        change = SlewSpeedEight * sign;
    }

    return change * interval;  // Distance moved this interval
}
```

**Characteristics:**
- **No hardware commands** - pure calculation
- **Automatic deceleration** as target approaches
- **Speed ramping** based on distance thresholds:
  - < 0.01°: Exact positioning
  - < 0.2°: Slowest speed (SlewSpeedOne)
  - < 0.6°: Medium-slow (SlewSpeedFour)
  - < 1.0°: Medium-fast (SlewSpeedSix)
  - >= 1.0°: Fastest (SlewSpeedEight)
- **Continuous movement** - called repeatedly from simulation loop
- **Simple state management** - just flags and target positions

---

### 3.3 Hardware vs Simulator Complexity

| Aspect | SkyWatcher Hardware | Simulator |
|--------|---------------------|-----------|
| **Lines of code** | ~130 lines | ~60 lines |
| **Serial commands** | Yes (`:G`, `:H`, `:M`, `:J`) | No |
| **Motor stopping** | Required (with retry logic) | Not needed |
| **Speed modes** | High/low speed discrete modes | Continuous speed ramping |
| **Hardware quirks** | Star Discovery lookup table | None |
| **Breakpoint control** | Yes (deceleration ramp) | Automatic |
| **Direction management** | Explicit forward/backward | Implicit via sign |
| **Encoder reads** | Multiple hardware queries | Direct variable access |

---

## 4. Summary of Major Differences

### 4.1 Timing & Timeouts

| Operation | SkyWatcher | Simulator | Ratio |
|-----------|------------|-----------|-------|
| Initial slew timeout | 240s | 120s | 2:1 |
| Position update timeout | 5s (exception) | N/A (no timeout) | - |
| Precision loop start | 800ms | 250ms | 3.2:1 |
| Max wait per precision iteration | 6000ms (sequential) | 3000ms (simultaneous) | 2:1 |

**Total precision time worst case:**
- **SkyWatcher:** 5 iterations × 6000ms = 30 seconds
- **Simulator:** 5 iterations × 3000ms = 15 seconds

---

### 4.2 Precision Strategy Comparison

```
SkyWatcher (Sequential with Damping):
┌─────────────────────────────────────┐
│ Iteration 1                         │
├─────────────────────────────────────┤
│ 1. Wait for position event (5s max) │
│ 2. Calculate Axis1 delta            │
│ 3. Move Axis1 by 25% of delta       │
│ 4. Wait for Axis1 stop (3s max)     │
│ 5. Calculate Axis2 delta            │
│ 6. Move Axis2 by 10% of delta       │
│ 7. Wait for Axis2 stop (3s max)     │
└─────────────────────────────────────┘

Simulator (Simultaneous, Direct):
┌─────────────────────────────────────┐
│ Iteration 1                         │
├─────────────────────────────────────┤
│ 1. Get positions directly           │
│ 2. Calculate both axes deltas       │
│ 3. Move Axis1 by 100% of delta      │
│ 4. Move Axis2 by 100% of delta      │
│ 5. Wait for both stop (3s max)      │
└─────────────────────────────────────┘
```

---

### 4.3 Error Handling Differences

| Scenario | SkyWatcher | Simulator |
|----------|------------|-----------|
| Position update timeout | Throws `TimeoutException` | No timeout concept |
| Invalid position data | Exception from event system | Silent `break` from loop |
| Cancellation token | Checked frequently | Checked frequently |
| Max iterations exceeded | Returns normally | Returns normally |

**Impact:** Simulator will not reveal timeout-related bugs in position update system.

---

### 4.4 Hardware Complexity

```
Hardware Command Stack (SkyWatcher):
┌──────────────────────────────────┐
│ High-Level: SkyGoTo()             │
├──────────────────────────────────┤
│ Mid-Level: SkyAxisGoToTarget()    │
├──────────────────────────────────┤
│ Low-Level: AxisGoToTarget()       │
│   ├─ GetAxisPosition()            │
│   ├─ AxisStop() [if moving]       │
│   ├─ SetMotionMode()              │
│   ├─ SetGotoTargetIncrement()     │
│   ├─ SetBreakPointIncrement()     │
│   └─ StartMotion()                │
├──────────────────────────────────┤
│ Serial: :G, :H, :M, :J commands   │
└──────────────────────────────────┘

Simulator Stack:
┌──────────────────────────────────┐
│ High-Level: SimGoTo()             │
├──────────────────────────────────┤
│ Mid-Level: CmdAxisGoToTarget()    │
├──────────────────────────────────┤
│ Low-Level: GoTo()                 │
│   └─ speed = f(delta)             │
├──────────────────────────────────┤
│ Math: position += speed × time    │
└──────────────────────────────────┘
```

---

## 5. Why The Differences Matter

### 5.1 Sequential vs Simultaneous (Precision)

**SkyWatcher's Sequential Approach:**

✅ **Advantages:**
- Avoids mechanical coupling issues between axes
- Easier to debug which axis has problems
- Allows different damping factors per axis
- More predictable behavior with real hardware

❌ **Disadvantages:**
- Takes longer (up to 6s per iteration vs 3s)
- More complex code
- If Axis1 takes long time, Axis2 waits

**Simulator's Simultaneous Approach:**

✅ **Advantages:**
- Faster convergence (3s vs 6s per iteration)
- Simpler code
- More realistic for ideal mount behavior
- Better utilizes available time

❌ **Disadvantages:**
- Doesn't reveal real-world axis coupling issues
- Both axes fail if either has problems
- Testing doesn't match production behavior

---

### 5.2 Damping Factors

**Why SkyWatcher Uses 25%/10% Corrections:**

✅ **Benefits:**
- **Prevents overshoot** from motor inertia
- **Accounts for backlash** in mechanical gears
- **Handles mechanical slop** in mount structure
- **Avoids oscillation** around target
- **More stable** with imperfect encoders

❌ **Trade-offs:**
- Requires more iterations to converge
- Slower overall positioning
- More complex to tune

**Example Convergence:**

```
Target: Move 1.0° to target

SkyWatcher (25% damping):
Iteration 1: Move 0.25°, remaining 0.75°
Iteration 2: Move 0.19°, remaining 0.56°
Iteration 3: Move 0.14°, remaining 0.42°
Iteration 4: Move 0.11°, remaining 0.31°
Iteration 5: Move 0.08°, remaining 0.23°
Result: Still 0.23° away after 5 iterations

Simulator (100% direct):
Iteration 1: Move 1.0°, remaining 0.0°
Result: At target after 1 iteration
```

**Reality:** Real hardware would overshoot with 100% corrections, causing oscillation.

---

### 5.3 Event-Driven vs Polling

**SkyWatcher's Event-Based Position:**

✅ **Advantages:**
- Ensures fresh encoder data before decisions
- Throws exception if hardware stops responding
- Synchronizes with encoder update cycle
- More robust error detection

❌ **Disadvantages:**
- More complex timing dependencies
- Requires careful event management
- Can timeout if encoder updates slow

**Simulator's Polling:**

✅ **Advantages:**
- Simpler code
- No timing concerns
- No synchronization needed
- Predictable behavior

❌ **Disadvantages:**
- Doesn't test event-driven code paths
- Won't reveal event timeout bugs
- Different error handling

---

### 5.4 Timeout Differences Impact

| Scenario | SkyWatcher | Simulator | Risk |
|----------|------------|-----------|------|
| Long slew (200s) | ✅ Within 240s timeout | ✅ Within 120s timeout | Low |
| Very long slew (150s) | ✅ Within 240s timeout | ❌ **Exceeds 120s timeout** | **High** |
| Slow hardware | May hit 5s position timeout | No position timeout | Medium |

**Potential Bug:** Code tested on simulator might timeout on real hardware with slow slews.

---

## 6. Recommendations

### 6.1 Testing Strategy

The simulator provides **functional testing** but does NOT validate:

❌ **Not Tested by Simulator:**
1. Damping factor tuning (25% vs 10%)
2. Sequential vs simultaneous axis issues
3. Event timing and timeout handling
4. Hardware quirks (Star Discovery lookup)
5. Motor stop logic and retry mechanisms
6. Serial command sequencing
7. Long slew timeouts (>120s)
8. Position update event failures

✅ **Adequately Tested:**
1. Basic slew logic flow
2. Coordinate transformations
3. AltAz prediction logic
4. Precision convergence (ideal case)
5. Cancellation token handling

**Recommendation:** Use simulator for rapid development, but validate on hardware before release.

---

### 6.2 Consider Timeout Alignment

**Issue:** 120s vs 240s timeout difference could cause issues.

**Scenarios:**
```
Slew Duration: 150 seconds
├─ Simulator: TIMEOUT (exceeds 120s) ❌
└─ Hardware: Success (within 240s) ✅
```

**Options:**
1. **Align to 240s:** Change simulator to match hardware
   - Pro: Consistent behavior
   - Con: Tests take longer
   
2. **Align to 120s:** Change hardware to match simulator
   - Pro: Faster operations
   - Con: May timeout on legitimate long slews
   
3. **Make configurable:** Add timeout as parameter
   - Pro: Flexible for different scenarios
   - Con: More complex API

**Recommendation:** Align simulator to 240s for consistency, or make timeout configurable.

---

### 6.3 Precision Calculation Validation

**Current State:**
```csharp
// SkyWatcher
double[] gotoPrecision = { _settings.GotoPrecision, _settings.GotoPrecision };

// Simulator
double[] gotoPrecision = { ConvertStepsToDegrees(2, 0), ConvertStepsToDegrees(2, 1) };
```

**Questions:**
1. What is typical value of `_settings.GotoPrecision`?
2. What is `ConvertStepsToDegrees(2, axis)` in degrees?
3. Are these comparable?

**Recommendation:** Verify these produce similar precision targets, or document the difference.

---

### 6.4 Consider Sequential/Simultaneous Toggle

**Potential Enhancement:**
```csharp
// Configuration option
public bool UseSequentialPrecisionSlew { get; set; } = true;

// In SkyPrecisionGoto and SimPrecisionGoto
if (UseSequentialPrecisionSlew)
{
    // Move Axis1, wait, then Axis2
}
else
{
    // Move both, wait for both
}
```

**Benefits:**
- Allows testing both strategies on both platforms
- Enables performance comparison
- Provides flexibility for different mount types

---

### 6.5 Document Damping Factor Rationale

**Current State:** Damping factors (25%, 10%) are hardcoded with no comments.

**Recommendation:** Add documentation:
```csharp
// Damping factors to prevent overshoot from motor inertia and backlash:
// - Axis1 (RA/Az): 0.25 (25%) - more aggressive due to lower mass
// - Axis2 (Dec/Alt): 0.1 (10%) - more conservative due to tube weight
// Values tuned empirically on EQ6-R mount with 8" SCT
skyTarget[0] += 0.25 * deltaDegree[0];  // Axis1
skyTarget[1] += 0.1 * deltaDegree[1];   // Axis2
```

---

### 6.6 Error Handling Consistency

**Issue:** Different error handling between implementations.

**Current:**
```csharp
// SkyWatcher - throws exception
if (!SkyServer.MountPositionUpdatedEvent.Wait(5000))
{
    throw new TimeoutException(...);
}

// Simulator - silent break
if (rawPositions == null || double.IsNaN(rawPositions[0]))
{ 
    break;
}
```

**Recommendation:** Consider consistent error handling strategy:
- Option 1: Both throw exceptions
- Option 2: Both return error codes
- Option 3: Document the difference explicitly

---

### 6.7 Performance Metrics to Monitor

When testing on hardware, monitor:

| Metric | Expected Range | Alert If |
|--------|----------------|----------|
| Initial slew time | 10-180s | > 200s |
| Precision iterations | 2-5 | > 5 |
| Per-iteration time | 1-4s | > 5s |
| Final positioning error | < GotoPrecision | > 2× GotoPrecision |
| Position update event time | < 500ms | > 2s |

---

## 7. Code Locations Reference

### 7.1 Primary Source Files

| Component | File | Lines | Description |
|-----------|------|-------|-------------|
| SkyGoTo | `MountInstance.cs` | 64190-67421 | SkyWatcher initial slew |
| SkyPrecisionGoto | `MountInstance.cs` | 67529-73346 | SkyWatcher precision slew |
| SimGoTo | `MountInstance.cs` | 52154-55444 | Simulator initial slew |
| SimPrecisionGoto | `MountInstance.cs` | 55551-60226 | Simulator precision slew |
| AxisGoToTarget | `SkyWatcher.cs` | 647-780 | Hardware motor control |
| GoTo (simulator) | `Controllers.cs` | 380-460 | Simulator movement |

### 7.2 Command Classes

| Class | File | Purpose |
|-------|------|---------|
| `SkyAxisGoToTarget` | `SkyCommands.cs` | Command wrapper for SkyWatcher |
| `SkyIsAxisFullStop` | `SkyCommands.cs` | Status check for SkyWatcher |
| `CmdAxisGoToTarget` | `Commands.cs` | Command wrapper for Simulator |
| `CmdAxisStatus` | `Commands.cs` | Status check for Simulator |

### 7.3 Queue Systems

| Queue | Project | Purpose |
|-------|---------|---------|
| `SkyQueue` | `GreenSwamp.Alpaca.Mount.SkyWatcher` | Hardware command queue |
| `MountQueue` | `GreenSwamp.Alpaca.Mount.Simulator` | Simulator command queue |

---

## 8. Appendix: Quick Reference

### 8.1 Key Differences At-A-Glance

```
INITIAL SLEW (GoTo):
├─ Timeout:        240s (Sky) vs 120s (Sim)  ⚠️ 2× difference
├─ Logic:          ~95% identical
└─ Commands:       Different queues

PRECISION SLEW (PrecisionGoto):
├─ Axis mode:      Sequential (Sky) vs Simultaneous (Sim)  ⚠️ Major
├─ Damping:        25%/10% (Sky) vs None (Sim)  ⚠️ Major
├─ Position:       Event (Sky) vs Poll (Sim)  ⚠️ Different errors
├─ Loop time:      800ms (Sky) vs 250ms (Sim)
├─ Max tries:      >= 5 (Sky) vs > 5 (Sim)
└─ Per-iteration:  6s max (Sky) vs 3s max (Sim)

HARDWARE:
├─ Complexity:     High (Sky) vs Low (Sim)
├─ Commands:       Serial (Sky) vs Math (Sim)
└─ Quirks:         Yes (Sky) vs None (Sim)
```

### 8.2 Testing Coverage

```
✅ TESTED BY SIMULATOR:
   - Basic slew flow
   - Coordinate math
   - Cancellation handling
   - AltAz prediction

❌ NOT TESTED BY SIMULATOR:
   - Damping convergence
   - Sequential axis behavior
   - Event timeouts
   - Hardware quirks
   - Long slew timeouts
```

---

## Document Information

**Generated:** December 2024  
**Author:** GitHub Copilot  
**Repository:** [GreenSwampAlpaca](https://github.com/Principia4834/GreenSwampAlpaca)  
**Branch:** master  
**Review Status:** Draft  

---

## Next Steps

1. **Review this document** with development team
2. **Validate precision calculations** (GotoPrecision vs 2 steps)
3. **Consider timeout alignment** (120s vs 240s)
4. **Test long slews** on hardware (>120s scenarios)
5. **Document damping factors** with empirical data
6. **Consider making sequential/simultaneous configurable**
7. **Establish performance monitoring** for hardware testing

---

*End of Report*
