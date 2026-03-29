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
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Mount.Commands
{
    /// <summary>
    /// Abstract base class for command queue implementations
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process commands</typeparam>
    public abstract class CommandQueueBase<TExecutor> : ICommandQueue<TExecutor>
    {
        private BlockingCollection<ICommand<TExecutor>> _commandBlockingCollection;
        private Task _processingTask;
        private ManualResetEventSlim _taskReadySignal;
        private bool _isInWarningState;
        protected TExecutor _executor;
        private CancellationTokenSource _cts;
        private long _id;

        public bool IsRunning { get; private set; }
        public long NewId => Interlocked.Increment(ref _id);

        /// <summary>
        /// Thread-safe statistics for this queue session. Null until the queue has been started at least once.
        /// </summary>
        public CommandQueueStatistics Statistics { get; private set; }

        /// <summary>
        /// Milliseconds to wait for a command's CompletionEvent before timing out.
        /// Override per implementation: Simulator = 22000, SkyWatcher = 40000.
        /// </summary>
        protected virtual int CompletionTimeoutMs => 40000;

        /// <summary>
        /// Command type names to include in Debug diagnostic logging.
        /// Empty array = log all types (when Debug monitoring is enabled).
        /// Override in subclasses to filter to specific command types.
        /// </summary>
        protected virtual string[] DiagnosticCommandFilter => [];

        public virtual void AddCommand(ICommand<TExecutor> command)
        {
            if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
                return;

            if (_commandBlockingCollection.TryAdd(command) == false)
            {
                throw new Exception($"Unable to Add Command {command.Id}");
            }
        }

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

            // ReSharper disable once AccessToDisposedClosure
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

        protected abstract bool IsConnected();
        protected abstract TExecutor CreateExecutor();
        protected abstract void InitializeExecutor(TExecutor executor);
        protected abstract void CleanupExecutor(TExecutor executor);

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
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope,
                        Category = MonitorCategory.Mount, Type = MonitorType.Warning,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"{command.Exception.Message}|{command.Exception.StackTrace}"
                    });
                }

                var queueWaitMs = (executionStart - dequeuedAt).TotalMilliseconds;

                if (diagnosticsEnabled)
                {
                    var executionMs = (HiResDateTime.UtcNow - executionStart).TotalMilliseconds;
                    ThreadPool.GetAvailableThreads(out var worker, out var io);
                    ThreadPool.GetMinThreads(out var minWorker, out var minIoc);
                    ThreadPool.GetMaxThreads(out var maxWorker, out var portThreads);
                    var threadMsg = $"|Worker:{worker:N0}|IO:{io:N0}|MinW:{minWorker:N0}|MinIO:{minIoc:N0}|MaxW:{maxWorker:N0}|MaxIO:{portThreads:N0}";
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server,
                        Category = MonitorCategory.Mount, Type = MonitorType.Debug,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"CmdId:{command.Id}|Type:{commandType}|QueueWait:{queueWaitMs:F3}ms|Execution:{executionMs:F3}ms|Total:{(queueWaitMs + executionMs):F3}ms|QueueDepth:{queueDepth}|Success:{command.Successful}|{threadMsg}"
                    });
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
    }
}
