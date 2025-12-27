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
using System.Collections.Concurrent;
using System.Diagnostics;

namespace GreenSwamp.Alpaca.Mount.Commands
{
    /// <summary>
    /// Abstract base class for command queue implementations
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process commands</typeparam>
    public abstract class CommandQueueBase<TExecutor> : ICommandQueue<TExecutor>
    {
        private BlockingCollection<ICommand<TExecutor>> _commandBlockingCollection;
        private ConcurrentDictionary<long, ICommand<TExecutor>> _resultsDictionary;
        protected TExecutor _executor;
        private CancellationTokenSource _cts;
        private long _id;

        public bool IsRunning { get; private set; }
        public long NewId => Interlocked.Increment(ref _id);

        public virtual void AddCommand(ICommand<TExecutor> command)
        {
            if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
                return;

            CleanResults(40, 180);
            if (_commandBlockingCollection.TryAdd(command) == false)
            {
                throw new Exception($"Unable to Add Command {command.Id}");
            }
        }

        public virtual ICommand<TExecutor> GetCommandResult(ICommand<TExecutor> command)
        {
            try
            {
                if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
                {
                    var a = "Queue | IsRunning:" + IsRunning + "| IsCancel:" + _cts?.IsCancellationRequested + "| IsConnected:" + IsConnected();
                    if (command.Exception != null) { a += "| Ex:" + command.Exception.Message; }
                    var e = new Exception(a);
                    command.Exception = e;
                    command.Successful = false;
                    return command;
                }

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalMilliseconds < 40000)
                {
                    if (_resultsDictionary == null) break;
                    var success = _resultsDictionary.TryRemove(command.Id, out var result);
                    if (success) return result;
                    Thread.Sleep(1);
                }

                var ex = new Exception($"Unable to Find Results {command.Id}, {command}, {sw.Elapsed.TotalMilliseconds}");
                command.Exception = ex;
                command.Successful = false;
                return command;
            }
            catch (Exception e)
            {
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
            _resultsDictionary = new ConcurrentDictionary<long, ICommand<TExecutor>>();
            _commandBlockingCollection = new BlockingCollection<ICommand<TExecutor>>();

            _ = Task.Factory.StartNew(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    foreach (var command in _commandBlockingCollection.GetConsumingEnumerable())
                    {
                        ProcessCommandQueue(command);
                    }
                }
            }, ct);

            IsRunning = true;
        }

        public virtual void Stop()
        {
            IsRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            CleanupExecutor(_executor);
            _executor = default(TExecutor);
            _resultsDictionary = null;
            _commandBlockingCollection = null;
        }

        protected abstract bool IsConnected();
        protected abstract TExecutor CreateExecutor();
        protected abstract void InitializeExecutor(TExecutor executor);
        protected abstract void CleanupExecutor(TExecutor executor);

        private void ProcessCommandQueue(ICommand<TExecutor> command)
        {
            try
            {
                if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
                    return;

                command.Execute(_executor);

                if (command.Id <= 0) return;
                if (_resultsDictionary.TryAdd(command.Id, command) == false)
                {
                    throw new Exception($"Unable to post results {command.Id}, {command}");
                }
            }
            catch (Exception e)
            {
                command.Exception = e;
                command.Successful = false;
            }
        }

        private void CleanResults(int count, int seconds)
        {
            if (!IsRunning || _cts.IsCancellationRequested || !IsConnected())
                return;
            if (_resultsDictionary.IsEmpty) return;

            var recordsCount = _resultsDictionary.Count;
            if (recordsCount == 0) return;
            if (count == 0 && seconds == 0)
            {
                _resultsDictionary.Clear();
                return;
            }

            if (recordsCount < count) return;
            var now = HiResDateTime.UtcNow;
            foreach (var result in _resultsDictionary)
            {
                if (result.Value.CreatedUtc.AddSeconds(seconds) >= now) continue;
                _resultsDictionary.TryRemove(result.Key, out _);
            }
        }
    }
}
