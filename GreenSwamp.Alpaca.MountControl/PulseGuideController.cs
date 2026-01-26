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

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using System.Diagnostics;
using System.Reflection;

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

        // Track last direction for backlash calculation
        private GuideDirection? _lastDirectionRa;
        private GuideDirection? _lastDirectionDec;

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

            var task = Task.Run(async () =>
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

            // Store task reference for cancellation support
            lock (_stateLock)
            {
                if (axis == Axis.Axis1)
                {
                    _raMovementTask = task;
                }
                else
                {
                    _decMovementTask = task;
                }
            }

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
                pulseEntry.Rejected = true;
                MonitorLog.LogToMonitor(pulseEntry);
                return;
            }

            // Execute via simulator command
            // Note: CmdAxisPulse handles the actual pulse execution
            _ = new CmdAxisPulse(0, operation.Axis, operation.GuideRate, operation.Duration, ct);

            // Wait for completion
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalMilliseconds < operation.Duration)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(10, ct);
            }

            // Log completion
            MonitorLog.LogToMonitor(pulseEntry);
        }

                /// <summary>
                /// Execute SkyWatcher pulse guide.
                /// </summary>
                private async Task ExecuteSkyWatcherPulseAsync(
                    PulseGuideOperation operation,
                    CancellationToken ct)
                {
                    // Delegate to SkyServer which will call the mount-specific implementation
                    // SkyServer has access to internal SkyWatcher methods
                    await Task.Run(() =>
                    {
                        // This will be replaced in Phase 2 with direct call to SkyServer.ExecutePulseAsync
                        // For now, throw to indicate implementation needed
                        throw new NotImplementedException(
                            "SkyWatcher pulse guide execution requires Phase 2 integration with SkyServer");
                    }, ct);
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