# PulseGuide Async Refactoring - Comprehensive Design Document

**Project:** GreenSwamp Alpaca ASCOM Telescope Driver  
**Target Framework:** .NET 8  
**Date:** January 2025  
**Status:** Design Complete - Ready for Implementation

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Analysis](#problem-analysis)
3. [ASCOM Specification Requirements](#ascom-specification-requirements)
4. [Current Implementation Issues](#current-implementation-issues)
5. [Proposed Architecture](#proposed-architecture)
6. [Detailed Design](#detailed-design)
7. [Integration with Existing Code](#integration-with-existing-code)
8. [Exception Handling](#exception-handling)
9. [Migration Plan](#migration-plan)
10. [Testing Strategy](#testing-strategy)
11. [Code Examples](#code-examples)

---

## Executive Summary

### Problem
The current pulse guiding implementation has race conditions, improper CancellationTokenSource lifecycle management, and fails ConformU testing when executing simultaneous RA and Dec pulse guide operations.

**Error:** `System.TimeoutException: The "Pulse guiding East and North" operation exceeded its 6 second timeout.`

### Root Cause
- `CancellationTokenSource` instances disposed prematurely when one axis completes before the other
- Flags (`IsPulseGuidingRa`, `IsPulseGuidingDec`) set in multiple locations causing race conditions
- No controller pattern - scattered logic across `Telescope.cs`, `SkyServer.TelescopeAPI.cs`, `Actions.cs`, `SkyWatcher.cs`
- Fire-and-forget `Task.Run()` without proper task tracking

### Solution
Implement `PulseGuideController` following the same architectural patterns as `SlewController`:
- Thread-safe per-axis locking (allows simultaneous RA+Dec pulses)
- Independent `CancellationTokenSource` per axis
- Centralized flag management
- Proper async lifecycle management
- ASCOM-compliant exception handling

### Effort Estimate
**12-15 development days + 2-3 testing days = 3-4 weeks total**

---

## Problem Analysis

### Current Code Flow

```
Telescope.PulseGuide(Direction, Duration)
    ↓ (sets IsPulseGuidingRa/Dec = true)
SkyServer.PulseGuide(Direction, Duration, altRate)
    ↓ (sets IsPulseGuidingRa/Dec = true AGAIN)
    ↓ (creates NEW CancellationTokenSource)
    ↓ (calls mount-specific implementation)
    ↓
    ├─ Simulator: CmdAxisPulse → Actions.AxisPulse()
    │     └─ Task.Run() → sets flags in finally block
    │
    └─ SkyWatcher: SkyAxisPulse → SkyWatcher.AxisPulse()
          └─ Task.Run() → sets flags in finally block
```

### Race Condition Scenario

```
Time  | RA Pulse (East, 1000ms)           | Dec Pulse (North, 1000ms)
------+-----------------------------------+----------------------------------
t=0   | IsPulseGuidingRa = true          | IsPulseGuidingDec = true
t=1   | _ctsPulseGuideRa = new CTS()     | _ctsPulseGuideDec = new CTS()
t=2   | Task.Run() started               | Task.Run() started
t=950 | RA pulse completes               |
t=951 | IsPulseGuidingRa = false         |
t=952 | [PROPERTY SETTER RUNS]            |
t=953 | _ctsPulseGuideRa?.Dispose()      | ⚠️ Dec still running
t=954 | _ctsPulseGuideRa = null          | ⚠️ Checks if RA CTS exists
t=1000|                                  | Dec completes
      |                                  | ❌ TIMEOUT - flags not cleared properly
```

**The Issue:** Property setters in `SkyServer.cs` lines 860-864 and 881-885 dispose the `CancellationTokenSource` when flags change, but async operations may still be checking/using them.

---

## ASCOM Specification Requirements

### ITelescopeV4.PulseGuide() Requirements

1. **Non-Blocking Return** ✅ CRITICAL
   - Method MUST return immediately (< 50ms)
   - Do NOT block or await

2. **Completion Property** ✅ CRITICAL
   - `IsPulseGuiding` = true when operation starts
   - `IsPulseGuiding` = false when operation completes
   - Client polls this property to detect completion

3. **Simultaneous Axes** ✅ CRITICAL
   - MUST support simultaneous RA and Dec pulse guiding
   - Exception if mount hardware doesn't support this (with clear error message)

4. **Allowed ASCOM Exceptions**
   - `InvalidValueException` - Invalid Direction or Duration
   - `InvalidOperationException` - Cannot pulse guide (e.g., slewing, not tracking, or simultaneous limitation)
   - `NotConnectedException` - Device not connected
   - `DriverException` - Other errors with detailed message
   - **NEVER throw:** `ArgumentException`, `ArgumentOutOfRangeException`, generic `Exception`

5. **State Requirements**
   - Only allowed when Tracking = true
   - NOT allowed when Slewing = true
   - NOT allowed when Parked = true

---

## Current Implementation Issues

### Issue 1: CancellationTokenSource Lifecycle

**Location:** `SkyServer.Core.cs` lines 116-118
```csharp
// Static fields - shared across operations
private static volatile CancellationTokenSource _ctsGoTo;
private static volatile CancellationTokenSource _ctsPulseGuideRa;
private static volatile CancellationTokenSource _ctsPulseGuideDec;
```

**Location:** `SkyServer.cs` lines 860-887
```csharp
public static bool IsPulseGuidingDec
{
    get => _isPulseGuidingDec;
    set
    {
        if (_isPulseGuidingDec != value)
        {
            _isPulseGuidingDec = value;
            // ❌ PROBLEM: Disposes CTS when flag changes
            if (!_isPulseGuidingDec && _ctsPulseGuideDec != null)
            {
                _ctsPulseGuideDec?.Dispose();
                _ctsPulseGuideDec = null;  // ⚠️ Other operations may still reference this
            }
        }
    }
}
```

**Problem:** 
- When RA pulse completes and sets `IsPulseGuidingRa = false`, the setter disposes `_ctsPulseGuideRa`
- If Dec pulse is still running and checks `_ctsPulseGuideRa`, it may get null or disposed instance
- No coordination between axes

### Issue 2: Redundant Flag Setting

**Flags set in 4 different locations:**

1. **Telescope.cs lines 1405, 1409** - Before calling SkyServer.PulseGuide
2. **SkyServer.TelescopeAPI.cs lines 1993, 2080** - In PulseGuide() method
3. **Actions.cs lines 161, 166, 225, 229** - In simulator AxisPulse()
4. **SkyWatcher.cs lines 402, 406, 475** - In SkyWatcher AxisPulse()

**Problem:** Multiple writers without synchronization = race conditions

### Issue 3: Fire-and-Forget Tasks

**Location:** `SkyServer.TelescopeAPI.cs` line 1888
```csharp
private static void PulseGuideAltAz(int axis, double guideRate, int duration, 
    Action<CancellationToken> pulseGoTo, CancellationToken token)
{
    Task.Run(() =>  // ❌ Not tracked, not awaited
    {
        // ... pulse execution ...
    });
    // Returns immediately - no task tracking
}
```

**Location:** `Actions.cs` line 155, `SkyWatcher.cs` line 153
```csharp
internal void AxisPulse(...)
{
    Task.Run(() =>  // ❌ Not tracked, not awaited
    {
        // ... pulse execution ...
    });
}
```

**Problem:**
- Tasks run in background with no tracking
- Cannot properly cancel or wait for completion
- Completion relies entirely on flags being set in finally blocks

### Issue 4: Non-ASCOM Exception Handling

**Location:** `Telescope.cs` lines 1413, 1417-1425
```csharp
default:
    throw new ArgumentOutOfRangeException(nameof(Direction), Direction, null);  // ❌ Not ASCOM

catch (Exception e)
{
    // Clear flags and re-throw
    throw;  // ❌ Propagates non-ASCOM exceptions
}
```

**Problem:** Violates ASCOM specification - must throw only allowed exception types

---

## Proposed Architecture

### High-Level Design

```
┌─────────────────────────────────────────────────────────┐
│                     ASCOM Client                        │
│  - Calls PulseGuide()                                   │
│  - Polls IsPulseGuiding for completion                  │
└────────────────────┬────────────────────────────────────┘
                     │ (synchronous call)
┌────────────────────▼────────────────────────────────────┐
│              Telescope.PulseGuide()                     │
│  - ASCOM validation (CheckParked, CheckCapability, etc.)│
│  - Exception handling (ASCOM-compliant)                 │
│  - Delegates to SkyServer.PulseGuide()                  │
└────────────────────┬────────────────────────────────────┘
                     │ (synchronous call)
┌────────────────────▼────────────────────────────────────┐
│           SkyServer.PulseGuide()                        │
│  - Minimal logging                                      │
│  - Delegates to PulseGuideController.StartPulseGuide()  │
└────────────────────┬────────────────────────────────────┘
                     │ (synchronous call)
┌────────────────────▼────────────────────────────────────┐
│     PulseGuideController.StartPulseGuide()              │
│  ┌─────────────────────────────────────────────────┐   │
│  │ SYNCHRONOUS PHASE (< 10ms)                      │   │
│  │  1. Determine axis (RA or Dec)                  │   │
│  │  2. Set IsPulseGuidingRa/Dec = true (locked)    │   │
│  │  3. Create CancellationTokenSource (locked)     │   │
│  │  4. Start Task.Run() for background execution   │   │
│  │  5. Return immediately                          │   │
│  └─────────────────────────────────────────────────┘   │
│                     │                                    │
│  ┌─────────────────▼────────────────────────────────┐   │
│  │ ASYNC PHASE (background Task)                    │   │
│  │  1. Acquire per-axis SemaphoreSlim               │   │
│  │  2. Execute mount-specific pulse operation       │   │
│  │  3. Wait for duration to complete                │   │
│  │  4. Clear IsPulseGuidingRa/Dec (in finally)      │   │
│  │  5. Dispose CTS (in finally, locked)             │   │
│  │  6. Release SemaphoreSlim                        │   │
│  └──────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### Key Architectural Decisions

1. **Per-Axis Locking** - Separate `SemaphoreSlim` for RA and Dec
   - Allows simultaneous RA+Dec pulses
   - Prevents re-entrant calls on same axis

2. **Independent CTS Per Axis** - No shared cancellation tokens
   - RA completing doesn't affect Dec's CTS
   - Thread-safe disposal under lock

3. **Synchronous Entry Point** - `StartPulseGuide()` returns immediately
   - Flags set BEFORE `Task.Run()`
   - Ensures ASCOM compliance

4. **Centralized Flag Management** - Only controller touches flags
   - No flag setting in `Telescope.cs`
   - No flag setting in mount implementations
   - Single source of truth

5. **Proper Task Tracking** - Controller keeps references to running tasks
   - Enables clean cancellation
   - Supports `CancelAllAsync()` integration

---

## Detailed Design

### Class: PulseGuideController

**File:** `GreenSwamp.Alpaca.MountControl/PulseGuideController.cs`

```csharp
/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)
    [License header - same as other files]
*/

// ============================================================================
// PulseGuideController.cs - Thread-safe Asynchronous Pulse Guide Controller
// ============================================================================
// This class implements ASCOM ITelescopeV4 asynchronous pulse guide operations:
// - Synchronous entry point (returns immediately)
// - Per-axis thread-safe cancellation via CancellationToken
// - Per-axis re-entrancy protection via SemaphoreSlim
// - Proper state management with completion flags
// - Clean cancellation by AbortSlew or new pulse commands
// 
// NOTE: This class is internal and designed to be used ONLY by SkyServer.
// It accesses internal SkyServer state and methods.
// ============================================================================

using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;
using ASCOM.Common.DeviceInterfaces;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Manages telescope pulse guide operations with thread-safe cancellation and state management.
    /// Implements ASCOM ITelescopeV4 async pulse guide semantics.
    /// Internal class - accesses internal SkyServer methods via direct calls.
    /// </summary>
    internal sealed class PulseGuideController : IDisposable
    {
        #region Private Fields
        
        // Separate locks for each axis to allow simultaneous operations
        private readonly SemaphoreSlim _raOperationLock = new(1, 1);
        private readonly SemaphoreSlim _decOperationLock = new(1, 1);
        
        private readonly object _stateLock = new();
        
        // Per-axis cancellation tokens
        private CancellationTokenSource? _raOperationCts;
        private CancellationTokenSource? _decOperationCts;
        
        // Per-axis task tracking
        private Task? _raMovementTask;
        private Task? _decMovementTask;
        
        // Per-axis state flags
        private bool _isPulseGuidingRa;
        private bool _isPulseGuidingDec;
        
        private bool _disposed;
        
        #endregion
        
        #region Public Properties
        
        /// <summary>
        /// Gets whether RA axis is currently pulse guiding.
        /// ASCOM completion property - client polls this.
        /// </summary>
        public bool IsPulseGuidingRa
        {
            get { lock (_stateLock) { return _isPulseGuidingRa; } }
            private set { lock (_stateLock) { _isPulseGuidingRa = value; } }
        }
        
        /// <summary>
        /// Gets whether Dec axis is currently pulse guiding.
        /// ASCOM completion property - client polls this.
        /// </summary>
        public bool IsPulseGuidingDec
        {
            get { lock (_stateLock) { return _isPulseGuidingDec; } }
            private set { lock (_stateLock) { _isPulseGuidingDec = value; } }
        }
        
        /// <summary>
        /// Gets whether either axis is pulse guiding.
        /// Combined status for convenience.
        /// </summary>
        public bool IsPulseGuiding => IsPulseGuidingRa || IsPulseGuidingDec;
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// SYNCHRONOUS entry point that starts pulse guide and returns immediately.
        /// This is what SkyServer.PulseGuide() calls.
        /// </summary>
        /// <param name="direction">Guide direction (North, South, East, West)</param>
        /// <param name="duration">Duration in milliseconds</param>
        /// <param name="altRate">Alternate rate to replace guide rate (0 = use default)</param>
        public void StartPulseGuide(
            GuideDirection direction,
            int duration,
            double altRate = 0)
        {
            ThrowIfDisposed();
            
            // Determine axis
            var axis = (direction == GuideDirection.North || direction == GuideDirection.South)
                ? Axis.Axis2
                : Axis.Axis1;
            
            // ✅ CRITICAL: Set flag and create CTS SYNCHRONOUSLY before Task.Run()
            lock (_stateLock)
            {
                if (axis == Axis.Axis1)
                {
                    IsPulseGuidingRa = true;
                    
                    // Cancel and dispose any existing RA operation
                    _raOperationCts?.Cancel();
                    _raOperationCts?.Dispose();
                    _raOperationCts = new CancellationTokenSource();
                }
                else
                {
                    IsPulseGuidingDec = true;
                    
                    // Cancel and dispose any existing Dec operation
                    _decOperationCts?.Cancel();
                    _decOperationCts?.Dispose();
                    _decOperationCts = new CancellationTokenSource();
                }
            }
            
            // ✅ NOW start background task (flag is already true, CTS is created)
            var operation = new PulseGuideOperation(direction, duration, altRate, axis);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    // Background work happens here
                    await ExecutePulseGuideInternalAsync(operation, axis);
                }
                catch (Exception ex)
                {
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Error,
                        Method = nameof(StartPulseGuide),
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Pulse guide error: {ex.Message}"
                    });
                }
                finally
                {
                    // ✅ Clear flag when done (clients polling will see this)
                    lock (_stateLock)
                    {
                        if (axis == Axis.Axis1)
                        {
                            IsPulseGuidingRa = false;
                            _raOperationCts?.Dispose();
                            _raOperationCts = null;
                            _raMovementTask = null;
                        }
                        else
                        {
                            IsPulseGuidingDec = false;
                            _decOperationCts?.Dispose();
                            _decOperationCts = null;
                            _decMovementTask = null;
                        }
                    }
                }
            });
            
            // ✅ Method returns immediately - flag is TRUE, CTS is created
        }
        
        /// <summary>
        /// Cancel pulse guide on specific axis.
        /// </summary>
        /// <param name="axis">Axis to cancel (Axis1=RA, Axis2=Dec)</param>
        /// <param name="timeoutMs">Timeout for cancellation</param>
        public async Task CancelPulseAsync(Axis axis, int timeoutMs = 2000)
        {
            ThrowIfDisposed();
            
            CancellationTokenSource? ctsToCancel = null;
            Task? taskToAwait = null;
            
            lock (_stateLock)
            {
                ctsToCancel = (axis == Axis.Axis1) ? _raOperationCts : _decOperationCts;
                taskToAwait = (axis == Axis.Axis1) ? _raMovementTask : _decMovementTask;
            }
            
            if (ctsToCancel == null) return;
            
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = nameof(CancelPulseAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Cancelling {axis} pulse guide"
            });
            
            // Request cancellation
            ctsToCancel.Cancel();
            
            if (taskToAwait != null)
            {
                try
                {
                    // Wait for pulse to stop cleanly with timeout
                    using var timeoutCts = new CancellationTokenSource(timeoutMs);
                    await taskToAwait.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected - operation was cancelled successfully
                }
                catch (TimeoutException)
                {
                    // Pulse didn't stop in time
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = nameof(CancelPulseAsync),
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Timeout cancelling {axis} pulse guide"
                    });
                }
            }
        }
        
        /// <summary>
        /// Cancel all pulse guide operations on both axes.
        /// Called by CancelAllAsync().
        /// </summary>
        public async Task CancelAllPulsesAsync(int timeoutMs = 2000)
        {
            ThrowIfDisposed();
            
            var tasks = new List<Task>();
            
            if (IsPulseGuidingRa)
                tasks.Add(CancelPulseAsync(Axis.Axis1, timeoutMs));
            
            if (IsPulseGuidingDec)
                tasks.Add(CancelPulseAsync(Axis.Axis2, timeoutMs));
            
            if (tasks.Any())
                await Task.WhenAll(tasks);
        }
        
        /// <summary>
        /// Clear all pulse guide flags - called on error in Telescope.cs
        /// </summary>
        public void ClearAllFlags()
        {
            lock (_stateLock)
            {
                IsPulseGuidingRa = false;
                IsPulseGuidingDec = false;
                
                _raOperationCts?.Cancel();
                _raOperationCts?.Dispose();
                _raOperationCts = null;
                
                _decOperationCts?.Cancel();
                _decOperationCts?.Dispose();
                _decOperationCts = null;
            }
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Internal async execution with proper lifecycle management.
        /// Runs in background after StartPulseGuide returns.
        /// </summary>
        private async Task ExecutePulseGuideInternalAsync(
            PulseGuideOperation operation,
            Axis axis)
        {
            var axisLock = (axis == Axis.Axis1) ? _raOperationLock : _decOperationLock;
            
            // Try to acquire lock (allows simultaneous RA+Dec, prevents re-entrant same axis)
            var lockAcquired = await axisLock.WaitAsync(50);
            if (!lockAcquired)
            {
                // Another pulse on this axis is running
                // Wait for it to finish (new pulse cancels old one)
                await axisLock.WaitAsync();
            }
            
            try
            {
                // Get the CTS that was created synchronously
                CancellationTokenSource? cts;
                lock (_stateLock)
                {
                    cts = (axis == Axis.Axis1) ? _raOperationCts : _decOperationCts;
                }
                
                if (cts == null)
                {
                    // Operation was cancelled before we started
                    return;
                }
                
                // Execute the pulse movement
                await ExecuteMovementAsync(operation, cts.Token);
            }
            finally
            {
                axisLock.Release();
            }
        }
        
        /// <summary>
        /// Execute mount-specific pulse guide operation.
        /// </summary>
        private async Task ExecuteMovementAsync(
            PulseGuideOperation operation,
            CancellationToken ct)
        {
            var settings = SkyServer.Settings;
            if (settings == null)
            {
                throw new InvalidOperationException("SkyServer settings not initialized");
            }
            
            // Mount-specific execution
            switch (settings.Mount)
            {
                case MountType.Simulator:
                    await ExecuteSimulatorPulseAsync(operation, ct);
                    break;
                    
                case MountType.SkyWatcher:
                    await ExecuteSkyWatcherPulseAsync(operation, ct);
                    break;
                    
                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.Mount), 
                        settings.Mount, "Unsupported mount type");
            }
        }
        
        /// <summary>
        /// Execute simulator pulse guide.
        /// </summary>
        private async Task ExecuteSimulatorPulseAsync(
            PulseGuideOperation operation,
            CancellationToken ct)
        {
            var pulseStartTime = HiResDateTime.UtcNow;
            
            // Setup monitoring
            var pulseEntry = new PulseEntry
            {
                Axis = (int)operation.Axis,
                Duration = operation.Duration,
                Rate = operation.GuideRate,
                StartTime = pulseStartTime
            };
            
            // Check for minimum duration
            var arcSecs = operation.Duration / 1000.0 * Conversions.Deg2ArcSec(Math.Abs(operation.GuideRate));
            if (arcSecs < 0.0002)
            {
                if (SkyServer.MonitorPulse)
                {
                    pulseEntry.Rejected = true;
                    MonitorLog.LogToMonitor(pulseEntry);
                }
                return;
            }
            
            // Execute via simulator command
            // Note: CmdAxisPulse handles the actual pulse execution
            _ = new CmdAxisPulse(0, operation.Axis, operation.GuideRate, operation.Duration, ct);
            
            // Wait for completion with position updates
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalMilliseconds < operation.Duration)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(10, ct);
                
                // Update mount positions periodically
                if (stopwatch.Elapsed.TotalMilliseconds % 100 < 15)
                {
                    SkyServer.UpdateSteps();
                }
            }
            
            // Final position update
            SkyServer.UpdateSteps();
            
            // Log
            if (SkyServer.MonitorPulse)
            {
                MonitorLog.LogToMonitor(pulseEntry);
            }
        }
        
        /// <summary>
        /// Execute SkyWatcher pulse guide.
        /// </summary>
        private async Task ExecuteSkyWatcherPulseAsync(
            PulseGuideOperation operation,
            CancellationToken ct)
        {
            var pulseStartTime = HiResDateTime.UtcNow;
            
            // Setup monitoring
            var pulseEntry = new PulseEntry
            {
                Axis = (int)operation.Axis,
                Duration = operation.Duration,
                Rate = operation.GuideRate,
                StartTime = pulseStartTime
            };
            
            // Calculate backlash if direction changed
            var backlashSteps = operation.CalculateBacklashSteps();
            
            // Choose execution strategy
            var settings = SkyServer.Settings;
            if (settings == null)
            {
                throw new InvalidOperationException("SkyServer settings not initialized");
            }
            
            bool useGoToOptimization = operation.Axis == Axis.Axis2 && settings.DecPulseGoTo;
            
            if (useGoToOptimization)
            {
                await ExecuteDecGoToPulseAsync(operation, backlashSteps, pulseEntry, ct);
            }
            else
            {
                await ExecuteSlewPulseAsync(operation, backlashSteps, pulseEntry, ct);
            }
        }
        
        /// <summary>
        /// Execute Dec pulse using GoTo optimization (faster for SkyWatcher).
        /// </summary>
        private async Task ExecuteDecGoToPulseAsync(
            PulseGuideOperation operation,
            int backlashSteps,
            PulseEntry pulseEntry,
            CancellationToken ct)
        {
            // Implementation based on SkyWatcher.cs lines 410-476
            // Convert pulse to steps and use AxisMoveSteps for faster execution
            
            var stepsPerSec = SkyServer.GetStepsPerSecond(operation.Axis);
            var arcSecs = operation.Duration / 1000.0 * Conversions.Deg2ArcSec(Math.Abs(operation.GuideRate));
            var stepsNeeded = (int)(arcSecs * stepsPerSec);
            stepsNeeded += backlashSteps;
            
            // Adjust duration for backlash
            var duration = operation.Duration;
            if (backlashSteps > 0)
            {
                var lashDuration = backlashSteps / stepsPerSec / 3600 / Math.Abs(operation.GuideRate) * 1000;
                duration += (int)lashDuration;
            }
            
            // Check minimum
            var settings = SkyServer.Settings;
            if (settings == null || stepsNeeded < 1 || duration < settings.MinPulseDurationDec)
            {
                if (SkyServer.MonitorPulse)
                {
                    pulseEntry.Rejected = true;
                    MonitorLog.LogToMonitor(pulseEntry);
                }
                return;
            }
            
            // Apply direction
            if (operation.GuideRate < 0)
            {
                stepsNeeded = -stepsNeeded;
            }
            
            pulseEntry.StartTime = HiResDateTime.UtcNow;
            
            // Execute GoTo
            ct.ThrowIfCancellationRequested();
            _ = new SkyAxisMoveSteps(0, operation.Axis, stepsNeeded);
            
            // Wait for completion
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalMilliseconds <= 3500)
            {
                ct.ThrowIfCancellationRequested();
                
                var axesStatus = SkyServer.GetAxisStatus(operation.Axis);
                if (axesStatus.FullStop)
                {
                    break;
                }
                
                await Task.Delay(10, ct);
            }
            
            // Log
            if (SkyServer.MonitorPulse)
            {
                MonitorLog.LogToMonitor(pulseEntry);
            }
        }
        
        /// <summary>
        /// Execute pulse using slew rate method.
        /// </summary>
        private async Task ExecuteSlewPulseAsync(
            PulseGuideOperation operation,
            int backlashSteps,
            PulseEntry pulseEntry,
            CancellationToken ct)
        {
            // Implementation based on SkyWatcher.cs lines 478-580
            
            // Adjust duration for backlash
            var duration = operation.Duration;
            if (backlashSteps > 0)
            {
                var stepsPerSec = SkyServer.GetStepsPerSecond(operation.Axis);
                var lashDuration = Convert.ToInt32(backlashSteps / stepsPerSec / 3600 / 
                    Math.Abs(operation.GuideRate) * 1000);
                
                // PHD2 will error if pulse doesn't return within 2 seconds
                if (lashDuration > 1000) { lashDuration = 1000; }
                duration += lashDuration;
            }
            
            // Check minimum
            var settings = SkyServer.Settings;
            var minDuration = (operation.Axis == Axis.Axis1) 
                ? settings?.MinPulseDurationRa ?? 0 
                : settings?.MinPulseDurationDec ?? 0;
                
            if (duration < minDuration)
            {
                if (SkyServer.MonitorPulse)
                {
                    pulseEntry.Rejected = true;
                    MonitorLog.LogToMonitor(pulseEntry);
                }
                return;
            }
            
            // Execute slew
            ct.ThrowIfCancellationRequested();
            _ = new SkyAxisSlew(0, operation.Axis, operation.GuideRate);
            
            pulseEntry.StartTime = HiResDateTime.UtcNow;
            
            // Wait for duration
            var stopwatch = Stopwatch.StartNew();
            var updateInterval = Math.Max(duration / 20, 50);
            
            while (stopwatch.Elapsed.TotalMilliseconds < duration)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(updateInterval, ct);
                SkyServer.UpdateSteps();
            }
            
            // Stop the slew
            _ = new SkyAxisStop(0, operation.Axis);
            
            // Final position update
            SkyServer.UpdateSteps();
            
            // Log
            if (SkyServer.MonitorPulse)
            {
                MonitorLog.LogToMonitor(pulseEntry);
            }
        }
        
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PulseGuideController));
            }
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_stateLock)
            {
                _raOperationCts?.Cancel();
                _raOperationCts?.Dispose();
                _raOperationCts = null;
                
                _decOperationCts?.Cancel();
                _decOperationCts?.Dispose();
                _decOperationCts = null;
                
                _disposed = true;
            }
            
            _raOperationLock?.Dispose();
            _decOperationLock?.Dispose();
        }
        
        #endregion
    }
}
```

### Class: PulseGuideOperation

**File:** `GreenSwamp.Alpaca.MountControl/PulseGuideOperation.cs`

```csharp
/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)
    [License header]
*/

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Encapsulates a pulse guide operation with all necessary parameters.
    /// </summary>
    internal class PulseGuideOperation
    {
        public GuideDirection Direction { get; }
        public int Duration { get; }
        public double AltRate { get; }
        public Axis Axis { get; }
        
        private GuideDirection? _lastDirectionRa;
        private GuideDirection? _lastDirectionDec;
        
        public PulseGuideOperation(
            GuideDirection direction,
            int duration,
            double altRate,
            Axis axis)
        {
            Direction = direction;
            Duration = duration;
            AltRate = altRate;
            Axis = axis;
        }
        
        /// <summary>
        /// Calculate guide rate based on direction and settings.
        /// </summary>
        public double GuideRate
        {
            get
            {
                var useAltRate = Math.Abs(AltRate) > 0;
                var settings = SkyServer.Settings;
                if (settings == null)
                {
                    throw new InvalidOperationException("SkyServer settings not initialized");
                }
                
                double rate;
                
                switch (Direction)
                {
                    case GuideDirection.North:
                    case GuideDirection.South:
                        rate = useAltRate ? AltRate : Math.Abs(settings.GuideRateDec);
                        rate = ApplyDecDirectionAndMode(rate, settings);
                        break;
                        
                    case GuideDirection.East:
                    case GuideDirection.West:
                        rate = useAltRate ? AltRate : Math.Abs(settings.GuideRateRa);
                        rate = ApplyRaDirectionAndMode(rate, settings);
                        break;
                        
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Direction), Direction, null);
                }
                
                return rate;
            }
        }
        
        /// <summary>
        /// Calculate backlash compensation steps if direction changed.
        /// </summary>
        public int CalculateBacklashSteps()
        {
            var settings = SkyServer.Settings;
            if (settings == null || settings.Mount != MountType.SkyWatcher)
            {
                return 0;
            }
            
            int backlash = 0;
            
            if (Axis == Axis.Axis1) // RA
            {
                if (_lastDirectionRa.HasValue && _lastDirectionRa.Value != Direction)
                {
                    // Direction changed - apply backlash (RA typically has no backlash)
                    backlash = 0;
                }
                _lastDirectionRa = Direction;
            }
            else // Dec
            {
                if (_lastDirectionDec.HasValue && _lastDirectionDec.Value != Direction)
                {
                    // Direction changed - apply backlash
                    backlash = settings.DecBacklash;
                }
                _lastDirectionDec = Direction;
            }
            
            return backlash;
        }
        
        private double ApplyDecDirectionAndMode(double rate, Settings.Models.SkySettings settings)
        {
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    if (Direction == GuideDirection.South) { rate = -rate; }
                    break;
                    
                case AlignmentMode.Polar:
                    if (SkyServer.SideOfPier == PointingState.Normal)
                    {
                        if (Direction == GuideDirection.North) { rate = -rate; }
                    }
                    else
                    {
                        if (Direction == GuideDirection.South) { rate = -rate; }
                    }
                    if (settings.PolarMode == PolarMode.Left) { rate = -rate; }
                    break;
                    
                case AlignmentMode.GermanPolar:
                    if (SkyServer.SideOfPier == PointingState.Normal)
                    {
                        if (Direction == GuideDirection.North) { rate = -rate; }
                    }
                    else
                    {
                        if (Direction == GuideDirection.South) { rate = -rate; }
                    }
                    break;
            }
            
            return rate;
        }
        
        private double ApplyRaDirectionAndMode(double rate, Settings.Models.SkySettings settings)
        {
            if (settings.AlignmentMode != AlignmentMode.AltAz)
            {
                if (settings.SouthernHemisphere)
                {
                    if (Direction == GuideDirection.West) { rate = -rate; }
                }
                else
                {
                    if (Direction == GuideDirection.East) { rate = -rate; }
                }
            }
            else
            {
                if (Direction == GuideDirection.East) { rate = -rate; }
            }
            
            return rate;
        }
    }
}
```

---

## Integration with Existing Code

### 1. Update SkyServer.Core.cs

**Remove static CTS fields (lines 117-118):**

```csharp
// ❌ REMOVE THESE
// private static volatile CancellationTokenSource _ctsPulseGuideRa;
// private static volatile CancellationTokenSource _ctsPulseGuideDec;
```

**Add controller field:**

```csharp
// Add after line 120
private static PulseGuideController? _pulseGuideController;
```

**Initialize in MountConnect() or appropriate initialization:**

```csharp
// After mount initialization succeeds
_pulseGuideController = new PulseGuideController();
```

**Dispose in MountDisconnect():**

```csharp
_pulseGuideController?.Dispose();
_pulseGuideController = null;
```

### 2. Update SkyServer.cs

**Remove property setters that dispose CTS (lines 860-887):**

```csharp
// ❌ REMOVE THESE PROPERTIES ENTIRELY
// They will be replaced by delegating properties to the controller

/// <summary>
/// Is Dec pulse guiding - delegates to controller
/// </summary>
public static bool IsPulseGuidingDec => _pulseGuideController?.IsPulseGuidingDec ?? false;

/// <summary>
/// Is Ra pulse guiding - delegates to controller
/// </summary>
public static bool IsPulseGuidingRa => _pulseGuideController?.IsPulseGuidingRa ?? false;
```

### 3. Update SkyServer.TelescopeAPI.cs

**Replace PulseGuide() method (lines 1974-2130):**

```csharp
/// <summary>
/// Pulse commands - delegates to PulseGuideController
/// </summary>
/// <param name="direction">GuideDirections</param>
/// <param name="duration">in milliseconds</param>
/// <param name="altRate">alternate rate to replace the guide rate</param>
public static void PulseGuide(GuideDirection direction, int duration, double altRate)
{
    if (!IsMountRunning) { throw new Exception("Mount not running"); }
    
    var monitorItem = new MonitorEntry
    { 
        Datetime = HiResDateTime.UtcNow, 
        Device = MonitorDevice.Server, 
        Category = MonitorCategory.Mount, 
        Type = MonitorType.Data, 
        Method = MethodBase.GetCurrentMethod()?.Name, 
        Thread = Thread.CurrentThread.ManagedThreadId, 
        Message = $"{direction}|{duration}" 
    };
    MonitorLog.LogToMonitor(monitorItem);
    
    // ✅ Delegate to controller - flag will be set synchronously before this returns
    if (_pulseGuideController == null)
    {
        throw new InvalidOperationException("PulseGuideController not initialized");
    }
    
    _pulseGuideController.StartPulseGuide(direction, duration, altRate);
    
    // ✅ IsPulseGuiding is TRUE when we reach here
}

/// <summary>
/// Clear pulse guide flags - called on error
/// </summary>
public static void ClearPulseGuideFlags()
{
    _pulseGuideController?.ClearAllFlags();
}
```

**Update CancelAllAsync() (around line 3250):**

```csharp
/// <summary>
/// Cancel all currently executing async operations
/// </summary>
public static void CancelAllAsync()
{
    // Cancel pulse guide operations via controller
    if (_pulseGuideController != null)
    {
        // Use synchronous wait with timeout
        try
        {
            _pulseGuideController.CancelAllPulsesAsync().Wait(2000);
        }
        catch (AggregateException ex)
        {
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = nameof(CancelAllAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Error cancelling pulse guides: {ex.InnerException?.Message}"
            });
        }
    }
    
    // Cancel legacy HC pulse guide (if still used)
    if (_ctsHcPulseGuide != null)
    {
        _ctsHcPulseGuide?.Cancel();
        var sw = Stopwatch.StartNew();
        while (_ctsHcPulseGuide != null && sw.ElapsedMilliseconds < 2000)
            Thread.Sleep(200);
    }
    
    // ⚠️ _ctsGoTo should be removed if SlewController handles all slews
}
```

**Remove PulseGuideAltAz() method (lines 1886-1966):**

This logic is now inside `PulseGuideController.ExecuteSimulatorPulseAsync()` and `ExecuteSkyWatcherPulseAsync()`.

### 4. Update Telescope.cs

**Replace PulseGuide() method (lines 1388-1426):**

```csharp
public void PulseGuide(GuideDirection Direction, int Duration)
{
    var monitorItem = new MonitorEntry
    {
        Datetime = HiResDateTime.UtcNow,
        Device = MonitorDevice.Telescope,
        Category = MonitorCategory.Driver,
        Type = MonitorType.Data,
        Method = MethodBase.GetCurrentMethod()?.Name,
        Thread = Thread.CurrentThread.ManagedThreadId,
        Message = FormattableString.Invariant($"{Direction},{Duration}")
    };
    MonitorLog.LogToMonitor(monitorItem);

    try
    {
        // ✅ STEP 1: Validate state and inputs (throws ASCOM exceptions)
        CheckParked("PulseGuide");
        CheckCapability(SkySettings.CanPulseGuide, "PulseGuide");
        CheckRange(Duration, 0, 30000, "PulseGuide", "Duration");
        
        // ✅ STEP 2: Check operational state
        if (SkyServer.IsSlewing)
        {
            throw new InvalidOperationException("PulseGuide is not allowed while slewing");
        }
        
        if (!SkyServer.Tracking)
        {
            throw new InvalidOperationException("PulseGuide is not allowed when tracking is off");
        }
        
        // ✅ STEP 3: Validate direction (throw ASCOM exception)
        if (!Enum.IsDefined(typeof(GuideDirection), Direction))
        {
            throw new InvalidValueException("PulseGuide", Direction.ToString(), 
                "Valid values: North, South, East, West");
        }
        
        // ✅ STEP 4: Check simultaneous pulse capability
        bool raDirection = (Direction == GuideDirection.East || Direction == GuideDirection.West);
        bool decDirection = (Direction == GuideDirection.North || Direction == GuideDirection.South);
        
        if (raDirection && SkyServer.IsPulseGuidingDec && !CanSimultaneousPulseGuide())
        {
            throw new InvalidOperationException(
                "PulseGuide: This mount cannot pulse guide in RA and Dec simultaneously. " +
                "Please wait for the current Dec pulse guide to complete before starting an RA pulse guide.");
        }
        
        if (decDirection && SkyServer.IsPulseGuidingRa && !CanSimultaneousPulseGuide())
        {
            throw new InvalidOperationException(
                "PulseGuide: This mount cannot pulse guide in RA and Dec simultaneously. " +
                "Please wait for the current RA pulse guide to complete before starting a Dec pulse guide.");
        }
        
        // ✅ STEP 5: Start the pulse guide
        SkyServer.PulseGuide(Direction, Duration, 0);
        
        // ✅ Method returns immediately - IsPulseGuiding is true
    }
    catch (InvalidValueException)
    {
        throw; // ASCOM exception
    }
    catch (InvalidOperationException)
    {
        throw; // ASCOM exception
    }
    catch (ParkedException)
    {
        throw; // ASCOM exception
    }
    catch (MethodNotImplementedException)
    {
        throw; // ASCOM exception
    }
    catch (NotConnectedException)
    {
        throw; // ASCOM exception
    }
    catch (Exception ex)
    {
        monitorItem = new MonitorEntry
        {
            Datetime = HiResDateTime.UtcNow,
            Device = MonitorDevice.Telescope,
            Category = MonitorCategory.Driver,
            Type = MonitorType.Error,
            Method = MethodBase.GetCurrentMethod()?.Name,
            Thread = Thread.CurrentThread.ManagedThreadId,
            Message = $"PulseGuide error: {ex.GetType().Name}: {ex.Message}"
        };
        MonitorLog.LogToMonitor(monitorItem);
        
        // Clear flags on error
        SkyServer.ClearPulseGuideFlags();
        
        // Wrap in ASCOM DriverException
        throw new DriverException(
            $"PulseGuide failed: {ex.Message}. " +
            $"Direction: {Direction}, Duration: {Duration}ms. " +
            $"See driver logs for details.",
            ex);
    }
}

/// <summary>
/// Check if mount supports simultaneous RA and Dec pulse guiding
/// </summary>
private static bool CanSimultaneousPulseGuide()
{
    switch (SkySettings.Mount)
    {
        case MountType.Simulator:
            return true;
        case MountType.SkyWatcher:
            return true;
        default:
            return true;
    }
}
```

### 5. Update Simulator Actions.cs

**Remove flag management from AxisPulse() (lines 161-166, 223-230):**

```csharp
// ❌ REMOVE these lines from Actions.AxisPulse():
// if (axis == Axis.Axis1)
// {
//     MountQueue.IsPulseGuidingRa = true && deltaOk;
// }
// else
// {
//     MountQueue.IsPulseGuidingDec = true && deltaOk;
// }

// ... and in finally block:
// if (axis == Axis.Axis1)
// {
//     MountQueue.IsPulseGuidingRa = false;
// }
// else
// {
//     MountQueue.IsPulseGuidingDec = false;
// }
```

The flags are now managed by `PulseGuideController`.

### 6. Update SkyWatcher.cs

**Remove flag management from AxisPulse() (lines 402, 406, 475):**

```csharp
// ❌ REMOVE these lines from SkyWatcher.AxisPulse():
// SkyQueue.IsPulseGuidingRa = false;  // line 402
// SkyQueue.IsPulseGuidingDec = true;  // line 406
// SkyQueue.IsPulseGuidingDec = false; // line 475
```

The flags are now managed by `PulseGuideController`.

### 7. Update SlewController.cs

**Add pulse guide cancellation to SetupPhaseAsync():**

```csharp
// Around line 297, add after CancelCurrentSlewAsync():
private async Task<SlewResult> SetupPhaseAsync(
    SlewOperation operation,
    CancellationToken ct)
{
    // ... existing logging ...
    
    // Cancel any existing slew operation first
    await CancelCurrentSlewAsync();
    
    // ✅ NEW: Also cancel any pulse guide operations
    // New slew should stop pulse guiding
    if (SkyServer._pulseGuideController != null)
    {
        await SkyServer._pulseGuideController.CancelAllPulsesAsync(1000);
    }
    
    ct.ThrowIfCancellationRequested();
    
    // ... rest of setup ...
}
```

---

## Exception Handling

### ASCOM Exception Requirements

From the ASCOM specification, `PulseGuide()` must only throw:

1. **InvalidValueException** - Invalid Direction or Duration
2. **InvalidOperationException** - Cannot pulse guide (slewing, not tracking, simultaneous limitation)
3. **NotConnectedException** - Device not connected
4. **DriverException** - Other errors with detailed message

### Implementation Pattern

```csharp
try
{
    // Validation
    CheckParked("PulseGuide");
    CheckCapability(SkySettings.CanPulseGuide, "PulseGuide");
    CheckRange(Duration, 0, 30000, "PulseGuide", "Duration");
    
    // State checks
    if (SkyServer.IsSlewing) throw new InvalidOperationException(...);
    if (!SkyServer.Tracking) throw new InvalidOperationException(...);
    
    // Direction validation
    if (!Enum.IsDefined(typeof(GuideDirection), Direction))
        throw new InvalidValueException(...);
    
    // Execute
    SkyServer.PulseGuide(Direction, Duration, 0);
}
catch (InvalidValueException) { throw; }
catch (InvalidOperationException) { throw; }
catch (ParkedException) { throw; }
catch (MethodNotImplementedException) { throw; }
catch (NotConnectedException) { throw; }
catch (Exception ex)
{
    // Log and wrap in DriverException
    SkyServer.ClearPulseGuideFlags();
    throw new DriverException($"PulseGuide failed: {ex.Message}...", ex);
}
```

---

## Migration Plan

### Phase 1: Create Infrastructure (1-2 days)

**Tasks:**
1. Create `GreenSwamp.Alpaca.MountControl/PulseGuideController.cs`
2. Create `GreenSwamp.Alpaca.MountControl/PulseGuideOperation.cs`
3. Add `_pulseGuideController` field to `SkyServer.Core.cs`
4. Initialize/dispose controller in mount connect/disconnect

**Validation:**
- Solution builds successfully
- Controller can be instantiated
- No regressions in existing functionality

### Phase 2: Refactor Common Logic (2-3 days)

**Tasks:**
1. Update `SkyServer.TelescopeAPI.cs`:
   - Replace `PulseGuide()` to delegate to controller
   - Add `ClearPulseGuideFlags()` method
   - Update `CancelAllAsync()` to call controller
   - Remove `PulseGuideAltAz()` method

2. Update `SkyServer.cs`:
   - Replace `IsPulseGuidingRa`/`Dec` properties to delegate to controller
   - Remove property setters that dispose CTS

3. Update `SkyServer.Core.cs`:
   - Remove `_ctsPulseGuideRa` and `_ctsPulseGuideDec` fields

4. Update `Telescope.cs`:
   - Remove flag setting (lines 1405-1413)
   - Add ASCOM-compliant exception handling
   - Add state validation (slewing, tracking checks)
   - Add simultaneous pulse capability check

**Validation:**
- Solution builds
- Basic pulse guide test (single axis) works
- Flags set/cleared correctly

### Phase 3: Refactor Mount Implementations (3-4 days)

**Simulator (1-2 days):**
1. Update `Actions.cs`:
   - Remove flag management from `AxisPulse()`
   - Keep core pulse execution logic
   - Ensure cancellation token is respected

2. Test simulator pulse guiding:
   - Single axis (RA only)
   - Single axis (Dec only)
   - Simultaneous RA+Dec
   - Cancellation via AbortSlew
   - Cancellation via new pulse

**SkyWatcher (2-3 days):**
1. Update `SkyWatcher.cs`:
   - Remove flag management from `AxisPulse()`
   - Keep two code paths (GoTo vs Slew)
   - Keep backlash handling
   - Ensure cancellation token is respected

2. Test SkyWatcher pulse guiding:
   - Same test scenarios as simulator
   - Verify backlash compensation
   - Verify both GoTo and Slew modes

**Validation:**
- Both mount types work correctly
- Simultaneous RA+Dec pulses work
- Cancellation works properly
- Backlash compensation works (SkyWatcher)

### Phase 4: Handle Special Cases (1-2 days)

**AltAz Mode:**
1. Refactor AltAz-specific pulse logic
2. Ensure tracking stop/resume coordination
3. Test with simulator in AltAz mode

**Hand Controller Pulses:**
1. Evaluate if HC pulses can use same controller
2. If separate: ensure they don't conflict
3. Update `CancelAllAsync()` accordingly

**Validation:**
- AltAz pulse guiding works
- HC pulses work (if applicable)
- No conflicts between different pulse sources

### Phase 5: Integration Testing (2-3 days)

**ConformU Testing:**
1. Run full ConformU test suite
2. Fix any issues found
3. Verify simultaneous RA+Dec pulse test passes

**Manual Testing:**
1. PHD2 guiding test (if available)
2. Stress test: rapid sequential pulses
3. Test all exception scenarios
4. Test with actual mount hardware (SkyWatcher)

**Documentation:**
1. Update user documentation
2. Update developer documentation
3. Add comments to new code

**Validation:**
- ✅ ConformU tests pass
- ✅ PHD2 can guide successfully
- ✅ No race conditions or timeouts
- ✅ All ASCOM exceptions handled correctly

---

## Testing Strategy

### Unit Tests

Create `PulseGuideControllerTests.cs`:

```csharp
[TestFixture]
public class PulseGuideControllerTests
{
    private PulseGuideController _controller;
    
    [SetUp]
    public void SetUp()
    {
        _controller = new PulseGuideController();
    }
    
    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }
    
    [Test]
    public void StartPulseGuide_SetsFlag_Immediately()
    {
        // Arrange
        var direction = GuideDirection.East;
        var duration = 1000;
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        _controller.StartPulseGuide(direction, duration);
        stopwatch.Stop();
        
        // Assert
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(50), "Should return immediately");
        Assert.That(_controller.IsPulseGuidingRa, Is.True, "Flag should be set");
    }
    
    [Test]
    public async Task StartPulseGuide_ClearsFlag_AfterCompletion()
    {
        // Arrange
        var direction = GuideDirection.East;
        var duration = 500;
        
        // Act
        _controller.StartPulseGuide(direction, duration);
        
        // Assert - flag is true immediately
        Assert.That(_controller.IsPulseGuidingRa, Is.True);
        
        // Wait for completion
        await WaitForCondition(() => !_controller.IsPulseGuidingRa, timeout: 2000);
        
        // Assert - flag is false after completion
        Assert.That(_controller.IsPulseGuidingRa, Is.False);
    }
    
    [Test]
    public void SimultaneousPulses_BothAxes_WorkCorrectly()
    {
        // Arrange
        var raDirection = GuideDirection.East;
        var decDirection = GuideDirection.North;
        var duration = 1000;
        
        // Act
        _controller.StartPulseGuide(raDirection, duration);
        _controller.StartPulseGuide(decDirection, duration);
        
        // Assert - both flags set
        Assert.That(_controller.IsPulseGuidingRa, Is.True);
        Assert.That(_controller.IsPulseGuidingDec, Is.True);
    }
    
    [Test]
    public async Task CancelPulse_StopsOperation_AndClearsFlag()
    {
        // Arrange
        var direction = GuideDirection.East;
        var duration = 5000; // Long duration
        
        // Act
        _controller.StartPulseGuide(direction, duration);
        Assert.That(_controller.IsPulseGuidingRa, Is.True);
        
        // Wait a bit then cancel
        await Task.Delay(100);
        await _controller.CancelPulseAsync(Axis.Axis1);
        
        // Assert - flag cleared
        Assert.That(_controller.IsPulseGuidingRa, Is.False);
    }
    
    [Test]
    public void ReentrantPulse_SameAxis_CancelsPrevious()
    {
        // Arrange
        var direction = GuideDirection.East;
        var duration = 5000;
        
        // Act - start first pulse
        _controller.StartPulseGuide(direction, duration);
        Assert.That(_controller.IsPulseGuidingRa, Is.True);
        
        // Start second pulse on same axis
        _controller.StartPulseGuide(direction, 1000);
        
        // Assert - still guiding (new pulse replaced old one)
        Assert.That(_controller.IsPulseGuidingRa, Is.True);
    }
    
    private async Task WaitForCondition(Func<bool> condition, int timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeout)
        {
            await Task.Delay(50);
        }
        
        if (!condition())
        {
            throw new TimeoutException($"Condition not met within {timeout}ms");
        }
    }
}
```

### Integration Tests

```csharp
[TestFixture]
public class PulseGuideIntegrationTests
{
    [Test]
    public async Task ASCOM_PulseGuide_ReturnsImmediately()
    {
        // Arrange
        var telescope = new Telescope(0);
        telescope.Connect();
        telescope.Tracking = true;
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        telescope.PulseGuide(GuideDirection.East, 1000);
        stopwatch.Stop();
        
        // Assert
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(50));
        Assert.That(telescope.IsPulseGuidingRa, Is.True);
        
        // Wait for completion
        await WaitFor(() => !telescope.IsPulseGuidingRa, timeout: 2000);
        Assert.That(telescope.IsPulseGuidingRa, Is.False);
    }
    
    [Test]
    public async Task ASCOM_SimultaneousRaAndDec_BothComplete()
    {
        // Arrange
        var telescope = new Telescope(0);
        telescope.Connect();
        telescope.Tracking = true;
        
        // Act - start both pulses
        telescope.PulseGuide(GuideDirection.East, 1000);
        Assert.That(telescope.IsPulseGuidingRa, Is.True);
        
        telescope.PulseGuide(GuideDirection.North, 1000);
        Assert.That(telescope.IsPulseGuidingDec, Is.True);
        
        // Assert - both running
        Assert.That(telescope.IsPulseGuidingRa, Is.True);
        Assert.That(telescope.IsPulseGuidingDec, Is.True);
        
        // Wait for both to complete
        await WaitFor(() => !telescope.IsPulseGuiding, timeout: 2000);
        
        Assert.That(telescope.IsPulseGuidingRa, Is.False);
        Assert.That(telescope.IsPulseGuidingDec, Is.False);
    }
    
    [Test]
    public void ASCOM_PulseWhileSlewing_ThrowsInvalidOperation()
    {
        // Arrange
        var telescope = new Telescope(0);
        telescope.Connect();
        telescope.Tracking = true;
        telescope.SlewToCoordinatesAsync(5.0, 45.0); // Start slew
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            telescope.PulseGuide(GuideDirection.East, 1000);
        });
        
        Assert.That(ex.Message, Does.Contain("slewing"));
    }
    
    [Test]
    public void ASCOM_PulseWithoutTracking_ThrowsInvalidOperation()
    {
        // Arrange
        var telescope = new Telescope(0);
        telescope.Connect();
        telescope.Tracking = false; // Not tracking
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            telescope.PulseGuide(GuideDirection.East, 1000);
        });
        
        Assert.That(ex.Message, Does.Contain("tracking"));
    }
    
    [Test]
    public void ASCOM_InvalidDuration_ThrowsInvalidValue()
    {
        // Arrange
        var telescope = new Telescope(0);
        telescope.Connect();
        telescope.Tracking = true;
        
        // Act & Assert
        Assert.Throws<InvalidValueException>(() =>
        {
            telescope.PulseGuide(GuideDirection.East, -100); // Negative duration
        });
        
        Assert.Throws<InvalidValueException>(() =>
        {
            telescope.PulseGuide(GuideDirection.East, 50000); // Too long
        });
    }
}
```

### ConformU Test Scenarios

1. **Single Axis Pulse** - RA only, Dec only
2. **Simultaneous RA+Dec Pulse** - This is the failing test case ✅
3. **Rapid Sequential Pulses** - PHD2 dithering simulation
4. **Cancel via AbortSlew** - During pulse guide
5. **Cancel via New Pulse** - Same axis re-entrancy
6. **Cancel via Slew** - Start slew during pulse
7. **Zero Duration Pulse** - Should clear flags immediately
8. **Invalid Parameters** - Proper exceptions

---

## Code Examples

### Example 1: Basic Pulse Guide Usage

```csharp
// ASCOM client code
telescope.Tracking = true;

// Start RA pulse
telescope.PulseGuide(GuideDirection.East, 1000);
Console.WriteLine($"IsPulseGuidingRa: {telescope.IsPulseGuidingRa}"); // True

// Start Dec pulse simultaneously
telescope.PulseGuide(GuideDirection.North, 1000);
Console.WriteLine($"IsPulseGuidingDec: {telescope.IsPulseGuidingDec}"); // True

// Poll for completion
while (telescope.IsPulseGuiding)
{
    Thread.Sleep(50);
}

Console.WriteLine("Pulse guides complete");
```

### Example 2: PHD2 Dithering Simulation

```csharp
// PHD2 sends rapid pulses for dithering
async Task DitherAsync(Telescope telescope)
{
    // East pulse
    telescope.PulseGuide(GuideDirection.East, 500);
    await PollUntilComplete(telescope, Axis.Axis1);
    
    // West correction
    telescope.PulseGuide(GuideDirection.West, 300);
    await PollUntilComplete(telescope, Axis.Axis1);
    
    // North pulse
    telescope.PulseGuide(GuideDirection.North, 400);
    await PollUntilComplete(telescope, Axis.Axis2);
}

async Task PollUntilComplete(Telescope telescope, Axis axis)
{
    bool isGuiding() => axis == Axis.Axis1 
        ? telescope.IsPulseGuidingRa 
        : telescope.IsPulseGuidingDec;
    
    while (isGuiding())
    {
        await Task.Delay(20);
    }
}
```

### Example 3: Error Handling

```csharp
try
{
    telescope.PulseGuide(GuideDirection.East, 1000);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("slewing"))
{
    Console.WriteLine("Cannot pulse guide while slewing");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("tracking"))
{
    Console.WriteLine("Start tracking before pulse guiding");
}
catch (InvalidValueException ex)
{
    Console.WriteLine($"Invalid parameter: {ex.Message}");
}
catch (DriverException ex)
{
    Console.WriteLine($"Driver error: {ex.Message}");
}
```

---

## Appendix: Key Files to Modify

### Files to Create

1. `GreenSwamp.Alpaca.MountControl/PulseGuideController.cs` (new)
2. `GreenSwamp.Alpaca.MountControl/PulseGuideOperation.cs` (new)

### Files to Modify

1. `GreenSwamp.Alpaca.MountControl/SkyServer.Core.cs`
   - Add `_pulseGuideController` field
   - Remove `_ctsPulseGuideRa`, `_ctsPulseGuideDec` fields
   - Initialize/dispose controller

2. `GreenSwamp.Alpaca.MountControl/SkyServer.cs`
   - Replace `IsPulseGuidingRa`/`Dec` properties to delegate to controller
   - Remove property setters that dispose CTS

3. `GreenSwamp.Alpaca.MountControl/SkyServer.TelescopeAPI.cs`
   - Replace `PulseGuide()` method (lines 1974-2130)
   - Add `ClearPulseGuideFlags()` method
   - Update `CancelAllAsync()` method
   - Remove `PulseGuideAltAz()` method (lines 1886-1966)

4. `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`
   - Replace `PulseGuide()` method (lines 1388-1426)
   - Add ASCOM-compliant exception handling
   - Add `CanSimultaneousPulseGuide()` method

5. `GreenSwamp.Alpaca.Simulator/Actions.cs`
   - Remove flag management from `AxisPulse()` (lines 161-166, 223-230)

6. `GreenSwamp.Alpaca.Mount.SkyWatcher/SkyWatcher.cs`
   - Remove flag management from `AxisPulse()` (lines 402, 406, 475)

7. `GreenSwamp.Alpaca.MountControl/SlewController.cs`
   - Add pulse guide cancellation to `SetupPhaseAsync()` (after line 297)

### Files to Reference (No Changes)

1. `GreenSwamp.Alpaca.MountControl/SlewController.cs` - Pattern to follow
2. `GreenSwamp.Alpaca.MountControl/MountInstance.cs` - Instance architecture
3. `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs` - Settings reference

---

## Summary

This design document provides a complete blueprint for refactoring the PulseGuide implementation to:

1. ✅ **Fix the race condition** causing ConformU test failures
2. ✅ **Follow ASCOM specification** exactly (non-blocking, proper completion flags)
3. ✅ **Match existing patterns** (`SlewController` architecture)
4. ✅ **Support simultaneous RA+Dec** pulse guiding
5. ✅ **Handle exceptions properly** (only ASCOM-compliant exceptions)
6. ✅ **Integrate cleanly** with existing code (`CancelAllAsync`, `AbortSlew`, etc.)

The implementation is ready to begin following the phased migration plan.

---

**End of Design Document**
