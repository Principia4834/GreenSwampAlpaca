/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

// ============================================================================
// SlewController.cs - Thread-safe Asynchronous Slew Operation Controller
// ============================================================================
// This class implements ASCOM ITelescopeV4 asynchronous slew operations with:
// - 3-phase execution (Setup < 1s, Movement, Completion)
// - Thread-safe cancellation via CancellationToken
// - Re-entrancy protection via SemaphoreSlim
// - Clean cancellation by AbortSlew or new slew commands
// - Proper state machine with validation
// 
// NOTE: This class is internal and designed to be used ONLY by SkyServer.
// It accesses internal SkyServer state and methods.
// ============================================================================

using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Manages telescope slew operations with thread-safe cancellation and state management.
    /// Implements ASCOM ITelescopeV4 async operation semantics.
    /// Internal class - accesses internal SkyServer methods via direct calls.
    /// </summary>
    internal sealed class SlewController : IDisposable
    {
        #region Private Fields

        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _stateLock = new();
        
        private CancellationTokenSource? _currentOperationCts;
        private Task? _movementTask;
        private SlewOperation? _currentOperation;
        
        private bool _isSlewing;
        private SlewType _currentSlewType = SlewType.SlewNone;
        private bool _disposed;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether a slew operation is currently in progress.
        /// Used by ASCOM clients to poll completion status.
        /// </summary>
        public bool IsSlewing
        {
            get
            {
                lock (_stateLock)
                {
                    return _isSlewing;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _isSlewing = value;
                }
            }
        }

        /// <summary>
        /// Gets the current type of slew operation being executed.
        /// </summary>
        public SlewType CurrentSlewType
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentSlewType;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _currentSlewType = value;
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Executes a slew operation asynchronously.
        /// If a slew is already in progress, it will be cancelled first.
        /// Returns when setup phase completes (< 1 second), with movement continuing in background.
        /// </summary>
        /// <param name="operation">The slew operation to execute</param>
        /// <param name="externalCancellationToken">Optional external cancellation token</param>
        /// <returns>Result of the setup phase</returns>
        /// <exception cref="ObjectDisposedException">Controller has been disposed</exception>
        public async Task<SlewResult> ExecuteSlewAsync(
            SlewOperation operation,
            CancellationToken externalCancellationToken = default)
        {
            ThrowIfDisposed();

            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            // Enforce < 1 second setup timeout
            using var setupTimeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(950));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                externalCancellationToken,
                setupTimeoutCts.Token
            );

            // Acquire exclusive lock for setup phase
            // Use try-wait to fail fast if another setup is in progress
            var lockAcquired = await _operationLock.WaitAsync(100, combinedCts.Token);
            if (!lockAcquired)
            {
                // Another setup in progress - wait with timeout
                await _operationLock.WaitAsync(combinedCts.Token);
            }

            try
            {
                // ===== PHASE 1: SETUP (< 1 second) =====
                var setupResult = await SetupPhaseAsync(operation, combinedCts.Token);

                if (!setupResult.CanProceed)
                {
                    return setupResult;
                }

                // Signal ASCOM that operation has started (IsSlewing = true)
                IsSlewing = true;
                CurrentSlewType = operation.SlewType;
                SkyServer.SlewState = operation.SlewType;

                // Start background movement task (does NOT block ASCOM caller)
                _movementTask = Task.Run(
                    async () => await ExecuteMovementAndCompletionAsync(operation, _currentOperationCts!.Token),
                    _currentOperationCts!.Token
                );

                return setupResult;
            }
            catch (OperationCanceledException) when (setupTimeoutCts.IsCancellationRequested)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = nameof(ExecuteSlewAsync),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Setup phase exceeded 1 second timeout"
                });
                return SlewResult.Failed("Setup phase exceeded 1 second timeout");
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// Cancels any active slew operation and waits for it to stop cleanly.
        /// Safe to call even if no slew is in progress.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait for clean cancellation (milliseconds)</param>
        /// <returns>Task that completes when cancellation is finished</returns>
        public async Task CancelCurrentSlewAsync(int timeoutMs = 5000)
        {
            ThrowIfDisposed();

            CancellationTokenSource? ctsToCancel = null;
            Task? taskToAwait = null;

            lock (_stateLock)
            {
                ctsToCancel = _currentOperationCts;
                taskToAwait = _movementTask;
            }

            if (ctsToCancel == null)
            {
                // No active operation to cancel
                return;
            }

            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = nameof(CancelCurrentSlewAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Cancelling {CurrentSlewType}"
            });

            // Request cancellation
            ctsToCancel.Cancel();

            if (taskToAwait != null)
            {
                try
                {
                    // Wait for movement to stop cleanly with timeout
                    using var timeoutCts = new CancellationTokenSource(timeoutMs);
                    await taskToAwait.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected - operation was cancelled successfully
                }
                catch (TimeoutException)
                {
                    // Movement didn't stop in time - force abort via hardware
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Warning,
                        Method = nameof(CancelCurrentSlewAsync),
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"Timeout waiting for {CurrentSlewType} to cancel, forcing stop"
                    });
                    await ForceStopAxesAsync();
                }
            }
        }

        /// <summary>
        /// Waits for the current slew operation to complete.
        /// Used for synchronous slew operations (e.g., SlewToCoordinates).
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task that completes when slewing finishes</returns>
        public async Task WaitForSlewCompletionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            while (IsSlewing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }
        }

        #endregion

        #region Private Phase Methods

        /// <summary>
        /// PHASE 1: Setup - Validates state, cancels previous operation, prepares new operation.
        /// Must complete in < 1 second (enforced by caller's timeout).
        /// </summary>
        private async Task<SlewResult> SetupPhaseAsync(
            SlewOperation operation,
            CancellationToken ct)
        {
            var log = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = nameof(SetupPhaseAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Starting {operation.SlewType} to [{operation.Target[0]:F4}, {operation.Target[1]:F4}]"
            };
            MonitorLog.LogToMonitor(log);

            // Cancel any existing operation first (auto-cancellation on new command)
            await CancelCurrentSlewAsync();

            ct.ThrowIfCancellationRequested();

            // Validate mount is running
            if (!SkyServer.IsMountRunning)
            {
                return SlewResult.Failed("Mount not running");
            }

            // Stop any residual motion from previous operation
            if (SkyServer.SlewState != SlewType.SlewNone)
            {
                var stopped = SkyServer.AxesStopValidate();
                if (!stopped)
                {
                    await ForceStopAxesAsync();
                    return SlewResult.Failed("Could not stop previous motion");
                }
            }

            // Create new cancellation token for this operation
            _currentOperationCts = new CancellationTokenSource();
            _currentOperation = operation;

            // Prepare operation-specific state (captures initial state, sets up predictor)
            operation.Prepare();

            return SlewResult.Success();
        }

        /// <summary>
        /// Executes PHASE 2 (Movement) and PHASE 3 (Completion) in background.
        /// Runs on ThreadPool thread after setup completes.
        /// </summary>
        private async Task ExecuteMovementAndCompletionAsync(
            SlewOperation operation,
            CancellationToken ct)
        {
            try
            {
                // ===== PHASE 2: MOVEMENT =====
                var moveResult = await MovementPhaseAsync(operation, ct);

                ct.ThrowIfCancellationRequested();

                // ===== PHASE 3: COMPLETION =====
                await CompletionPhaseAsync(operation.SlewType, operation.TrackingAfterSlew);
            }
            catch (OperationCanceledException)
            {
                await HandleCancellationAsync(operation);
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(operation, ex);
            }
            finally
            {
                CleanupOperation(operation);
            }
        }

        /// <summary>
        /// PHASE 2: Movement - Delegates to mount-specific GoTo implementation (SimGoTo/SkyGoTo).
        /// Monitors movement until completion or cancellation.
        /// </summary>
        private async Task<MoveResult> MovementPhaseAsync(
            SlewOperation operation,
            CancellationToken ct)
        {
            var log = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = nameof(MovementPhaseAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Executing {operation.SlewType} movement"
            };
            MonitorLog.LogToMonitor(log);

            // Delegate to mount-specific implementation (SimGoTo/SkyGoTo)
            // These methods handle the actual motor control and movement monitoring
            // Note: GoTo methods are called via the operation's Execute method which has access
            int returnCode = await operation.ExecuteMovementAsync(ct);

            return new MoveResult(returnCode == 0, returnCode);
        }

        /// <summary>
        /// PHASE 3: Completion - Handles slew-type-specific completion logic.
        /// Enables tracking, updates state, performs settling operations.
        /// </summary>
        /// <summary>
        /// Phase 3: Completion - finalize slew operation
        /// </summary>
        private async Task CompletionPhaseAsync(SlewType slewType, bool restoreTracking)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = "CompletionPhaseAsync",
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{slewType} completed successfully"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // ✅ FIX: Set AtPark for park operations ONLY when physically parked
            if (slewType == SlewType.SlewPark)
            {
                SkyServer.CompletePark();  // Sets AtPark, Tracking, resets predictor
            }

            // Clear slewing state
            _currentSlewType = SlewType.SlewNone;
            SkyServer.SlewState = SlewType.SlewNone;

            // Restore tracking if needed
            if (restoreTracking && slewType != SlewType.SlewPark)
            {
                SkyServer.Tracking = true;
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Error and Cleanup Handlers

        /// <summary>
        /// Handles cancellation of slew operation.
        /// Stops axes and resets state.
        /// </summary>
        private async Task HandleCancellationAsync(SlewOperation operation)
        {
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Warning,
                Method = nameof(HandleCancellationAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{operation.SlewType} cancelled"
            });

            // Stop axes immediately
            await ForceStopAxesAsync();

            // Reset state via operation
            operation.HandleCancellation();
        }

        /// <summary>
        /// Handles errors during slew operation.
        /// Stops axes and logs error.
        /// </summary>
        private async Task HandleErrorAsync(SlewOperation operation, Exception ex)
        {
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Error,
                Method = nameof(HandleErrorAsync),
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{operation.SlewType} error: {ex.Message}"
            });

            await ForceStopAxesAsync();

            // Set error state via operation
            operation.HandleError(ex);
        }

        /// <summary>
        /// Cleans up operation state after completion/cancellation/error.
        /// Resets controller state for next operation.
        /// </summary>
        private void CleanupOperation(SlewOperation operation)
        {
            lock (_stateLock)
            {
                IsSlewing = false;
                CurrentSlewType = SlewType.SlewNone;

                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                _currentOperation = null;
                _movementTask = null;
            }

            operation.Dispose();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Forces immediate stop of mount axes via hardware command.
        /// Used when cancellation timeout expires or errors occur.
        /// </summary>
        private async Task ForceStopAxesAsync()
        {
            try
            {
                // Call public SkyServer stop method
                await Task.Run(() => SkyServer.StopAxes());
            }
            catch (Exception ex)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Error,
                    Method = nameof(ForceStopAxesAsync),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Error forcing axes stop: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if controller has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SlewController));
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the controller and cancels any active operations.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Cancel any active operation
                _currentOperationCts?.Cancel();
                _currentOperationCts?.Dispose();

                // Wait briefly for movement task to complete
                if (_movementTask != null && !_movementTask.IsCompleted)
                {
                    _movementTask.Wait(TimeSpan.FromSeconds(2));
                }

                _operationLock.Dispose();

                _currentOperation?.Dispose();
            }
            catch (Exception ex)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Warning,
                    Method = nameof(Dispose),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Error during disposal: {ex.Message}"
                });
            }
            finally
            {
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Immutable data container for a single slew operation.
    /// Encapsulates all parameters needed for setup/movement/completion phases.
    /// Provides direct access to SkyServer internals for slew execution.
    /// </summary>
    internal sealed class SlewOperation : IDisposable
    {
        #region Public Properties

        public double[] Target { get; }
        public SlewType SlewType { get; }
        public bool TrackingAfterSlew { get; }

        // Captured state at operation creation
        public double InitialRa { get; private set; }
        public double InitialDec { get; private set; }
        public bool WasTracking { get; private set; }

        #endregion

        #region Constructor

        public SlewOperation(
            double[] target,
            SlewType slewType,
            bool trackingAfterSlew)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (target.Length != 2)
                throw new ArgumentException("Target must have exactly 2 elements (RA/Az, Dec/Alt)", nameof(target));

            SlewType = slewType;
            TrackingAfterSlew = trackingAfterSlew;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Captures current mount state and prepares operation-specific settings.
        /// Called during setup phase.
        /// </summary>
        public void Prepare()
        {
            // Capture initial state for potential rollback
            InitialRa = SkyServer.RightAscensionXForm;
            InitialDec = SkyServer.DeclinationXForm;
            WasTracking = SkyServer.Tracking;

            // Disable tracking during slew (will be restored in completion phase)
            SkyServer.Tracking = false;

            // Prepare predictor for Ra/Dec slews
            if (SlewType == SlewType.SlewRaDec)
            {
                SkyPredictor.Set(
                    SkyServer.TargetRa,
                    SkyServer.TargetDec,
                    SkyServer.RateRa,
                    SkyServer.RateDec
                );
            }
        }

        /// <summary>
        /// Executes the movement phase by delegating to mount-specific GoTo implementation.
        /// Has access to internal SkyServer methods.
        /// </summary>
        public async Task<int> ExecuteMovementAsync(CancellationToken ct)
        {
            // Direct access to SkyServer settings (no reflection needed)
            var settings = SkyServer.Settings;

            if (settings == null)
            {
                throw new InvalidOperationException("SkyServer settings not initialized");
            }

            int returnCode = settings.Mount switch
            {
                MountType.Simulator => await Task.Run(
                    () => SkyServer.SimGoTo(Target, TrackingAfterSlew, SlewType, ct),
                    ct),
                MountType.SkyWatcher => await Task.Run(
                    () => SkyServer.SkyGoTo(Target, TrackingAfterSlew, SlewType, ct),
                    ct),
                _ => throw new InvalidOperationException($"Unknown mount type: {settings.Mount}")
            };

            return returnCode;
        }

        /// <summary>
        /// Checks if the slew is still active (not cancelled externally).
        /// </summary>
        public bool IsStillActive()
        {
            return SkyServer.SlewState != SlewType.SlewNone;
        }

        /// <summary>
        /// Restores tracking state after slew completion or cancellation.
        /// </summary>
        public void RestoreTracking()
        {
            SkyServer.Tracking = TrackingAfterSlew;
        }

        /// <summary>
        /// Handles slew-type-specific completion logic.
        /// </summary>
        public async Task CompleteAsync(CancellationToken ct)
        {
            switch (SlewType)
            {
                case SlewType.SlewRaDec:
                    await CompleteRaDecSlewAsync(ct);
                    break;

                case SlewType.SlewAltAz:
                    // Direct Alt/Az slew - no special completion needed
                    break;

                case SlewType.SlewPark:
                    SkyServer.AtPark = true;
                    SkyPredictor.Reset();
                    break;

                case SlewType.SlewHome:
                    SkyPredictor.Reset();
                    break;

                case SlewType.SlewHandpad:
                    SkyPredictor.Set(
                        SkyServer.RightAscensionXForm,
                        SkyServer.DeclinationXForm
                    );
                    break;

                case SlewType.SlewMoveAxis:
                case SlewType.SlewSettle:
                    // No special completion needed
                    break;
            }

            // Mark slew as complete and restore tracking
            MarkComplete(true);
        }

        /// <summary>
        /// Marks the slew operation as complete.
        /// </summary>
        public void MarkComplete(bool success)
        {
            // Direct access to SlewState property (no reflection needed)
            SkyServer.SlewState = SlewType.SlewNone;

            if (success)
            {
                SkyServer.Tracking = TrackingAfterSlew;
            }
            else
            {
                SkyServer.Tracking = false;
            }
        }

        /// <summary>
        /// Handles cancellation by resetting rates and state.
        /// </summary>
        public void HandleCancellation()
        {
            // Reset rates and axis movement
            SkyServer.RateMoveSecondaryAxis = 0.0;
            SkyServer.RateMovePrimaryAxis = 0.0;
            SkyServer.MoveAxisActive = false;

            // Mark slew as complete (direct access, no reflection needed)
            SkyServer.SlewState = SlewType.SlewNone;

            SkyServer.Tracking = TrackingAfterSlew;
        }

        /// <summary>
        /// Handles error by setting mount error state.
        /// </summary>
        public void HandleError(Exception ex)
        {
            // Set mount error (direct access, no reflection needed)
            SkyServer.MountError = new Exception($"Slew Error|{SlewType}|{ex.Message}");

            // Mark slew as complete (direct access, no reflection needed)
            SkyServer.SlewState = SlewType.SlewNone;

            SkyServer.Tracking = false;
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Completion logic for Ra/Dec slews.
        /// For Alt/Az mounts, enables tracking and waits for settling.
        /// </summary>
        private async Task CompleteRaDecSlewAsync(CancellationToken ct)
        {
            // Direct access to SkyServer settings (no reflection needed)
            var settings = SkyServer.Settings;

            if (settings == null || (int)settings.AlignmentMode != 2) // 2 = AltAz
            {
                // Equatorial mount - no special completion needed
                return;
            }

            // Alt/Az mount - need tracking settle period
            // Update target if offset rates were used
            if (SkyPredictor.RatesSet)
            {
                var targetRaDec = SkyPredictor.GetRaDecAtTime(HiResDateTime.UtcNow);
                SkyServer.TargetRa = targetRaDec[0];
                SkyServer.TargetDec = targetRaDec[1];
            }

            // Enable Alt/Az tracking to complete the slew
            // NOTE: Replicate GoToAsync pattern - don't use Tracking property setter
            // because it resets SkyPredictor (line 301 in TelescopeAPI.cs)
            SkyPredictor.Set(SkyServer.TargetRa, SkyServer.TargetDec);

            // Manually set tracking without going through Tracking property
            SkyServer.SetTrackingDirect(true, TrackingMode.AltAz);

            // Wait for tracking to settle
            var minSteps = Math.Min(
                SkyServer.StepsPerRevolution[0],
                SkyServer.StepsPerRevolution[1]
            );
            var highResMount = Conversions.StepPerArcSec(minSteps) > 5;

            var settleTimeMs = highResMount
                ? 2 * settings.AltAzTrackingUpdateInterval
                : 4 * settings.AltAzTrackingUpdateInterval;

            var settleStart = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(settleStart).TotalMilliseconds < settleTimeMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            // No unmanaged resources to clean up
            // This is here for future extensibility
        }

        #endregion
    }

    /// <summary>
    /// Result of the slew setup phase.
    /// Indicates whether the slew can proceed to movement phase.
    /// </summary>
    public readonly struct SlewResult
    {
        public bool CanProceed { get; }
        public string? ErrorMessage { get; }

        private SlewResult(bool canProceed, string? errorMessage = null)
        {
            CanProceed = canProceed;
            ErrorMessage = errorMessage;
        }

        public static SlewResult Success() => new(true);
        public static SlewResult Failed(string reason) => new(false, reason);
    }

    /// <summary>
    /// Result of the slew movement phase.
    /// Indicates success/failure and return code from mount-specific GoTo.
    /// </summary>
    public readonly struct MoveResult
    {
        public bool Success { get; }
        public int Code { get; }

        public MoveResult(bool success, int code)
        {
            Success = success;
            Code = code;
        }
    }

    #endregion
}
