/* Copyright(C) 2019-2025 Rob  Morgan (robert.morgan.e@gmail.com)

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
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GreenSwamp.Alpaca.MountControl
{
    public class CommandQueue
    {
        CommandQueue(MountCommander mountCommander, CancellationTokenSource cts)
        {
            _commandBlockingCollection = new BlockingCollection<IMountCommand>();
            _resultsDictionary = new ConcurrentDictionary<long, IMountCommand>();
            _cts = cts;
        }

        #region Fields

        private BlockingCollection<IMountCommand> _commandBlockingCollection;
        private ConcurrentDictionary<long, IMountCommand> _resultsDictionary;
        private CancellationTokenSource _cts;
        public event PropertyChangedEventHandler StaticPropertyChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Serial object
        /// </summary>
        internal ISerialPort Serial { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; private set; }

        /// <summary>
        /// Custom Mount :s replacement
        /// </summary>
        internal int[] CustomMount360Steps { get; private set; }
        
        /// <summary>
        /// Custom Mount :s replacement
        /// </summary>// Custom Mount :a replacement
        internal double[] CustomRaWormSteps { get; private set; }
        
        /// <summary>
        /// IsRunning
        /// </summary>
        public bool IsRunning { get; private set; }

        private long _id;
        /// <summary>
        /// Locking id
        /// </summary>
        public long NewId => Interlocked.Increment(ref _id);

        private bool _isPulseGuidingDec;
        /// <summary>
        /// status for Dec Pulse
        /// </summary>
        public bool IsPulseGuidingDec
        {
            get => _isPulseGuidingDec;
            set
            {
                _isPulseGuidingDec = value;
                OnStaticPropertyChanged();
            }
        }

        private bool _isPulseGuidingRa;
        /// <summary>
        /// status for Dec Pulse
        /// </summary>
        public bool IsPulseGuidingRa
        {
            get => _isPulseGuidingRa;
            set
            {
                _isPulseGuidingRa = value;
                OnStaticPropertyChanged();
            }
        }

        private double[] _steps;
        /// <summary>
        /// current steps, main property used to update Server and UI
        /// </summary>
        public double[] Steps
        {
            get => _steps;
            set
            {
                _steps = value;
                OnStaticPropertyChanged();
            }
        }
        #endregion

        #region Queues

        /// <summary>
        /// Add a command to the blocking queue
        /// </summary>
        /// <param name="mountCommand"></param>
        public void AddCommand(IMountCommand mountCommand)
        {
            if (!IsRunning || _cts.IsCancellationRequested || mountCommander?.IsConnected != true) return;
            CleanResults(40, 180);
            if (_commandBlockingCollection.TryAdd(mountCommand) == false)
            {
                throw new MountControlException(ErrorCode.ErrQueueFailed, $"Unable to Add Command {mountCommand.Id}, {mountCommand}");
            }

        }

        /// <summary>
        /// Cleans up the results dictionary
        /// </summary>
        /// <param name="count"></param>
        /// <param name="seconds"></param>
        private void CleanResults(int count, int seconds)
        {
            if (!IsRunning || _cts.IsCancellationRequested || mountCommander?.IsConnected != true) return;
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

        /// <summary>
        /// Mount data results
        /// </summary>
        /// <remarks>
        /// There could be timing issues between this method and timeouts for commands reading mount data
        /// </remarks>
        /// <param name="mountCommand"></param>
        /// <returns></returns>
        public IMountCommand GetCommandResult(IMountCommand mountCommand)
        {
            try
            {
                if (!IsRunning || _cts.IsCancellationRequested || mountCommander?.IsConnected != true)
                {
                    var a = "Queue | IsRunning:" + IsRunning + "| IsCancel:" + _cts?.IsCancellationRequested + "| IsConnected:" + (mountCommander?.IsConnected == true);
                    if (mountCommand.Exception != null) { a += "| Ex:" + mountCommand.Exception.Message; }
                    var e = new MountControlException(ErrorCode.ErrQueueFailed, a);
                    mountCommand.Exception = e;
                    mountCommand.Successful = false;
                    return mountCommand;
                }
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalMilliseconds < 40000)
                {
                    if (_resultsDictionary == null) break;
                    var success = _resultsDictionary.TryRemove(mountCommand.Id, out var result);
                    if (success) return result;
                    Thread.Sleep(1);
                }
                var ex = new MountControlException(ErrorCode.ErrQueueFailed, $"Unable to Find Results {mountCommand.Id}, {mountCommand}, {sw.Elapsed.TotalMilliseconds}");
                mountCommand.Exception = ex;
                mountCommand.Successful = false;
                return mountCommand;
            }
            catch (Exception e)
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{mountCommand.Id}|{e.Message}" };
                MonitorLog.LogToMonitor(monitorItem);

                mountCommand.Exception = e;
                mountCommand.Successful = false;
                return mountCommand;
            }

        }

        /// <summary>
        /// Process command queue
        /// </summary>
        /// <param name="mountCommand"></param>
        private void ProcessCommandQueue(IMountCommand mountCommand)
        {
            try
            {
                if (!IsRunning || _cts.IsCancellationRequested || mountCommander?.IsConnected != true) return;
                mountCommand.Execute(mountCommander);
                if (mountCommand.Exception != null)
                {
                    var monitorItem = new MonitorEntry
                        { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{mountCommand.Exception.Message}|{mountCommand.Exception.StackTrace}" };
                    MonitorLog.LogToMonitor(monitorItem);
                }
                if (mountCommand.Id <= 0) return;
                if (_resultsDictionary.TryAdd(mountCommand.Id, mountCommand) == false)
                {
                    throw new MountControlException(ErrorCode.ErrQueueFailed, $"Unable to post results {mountCommand.Id}, {mountCommand}");
                }
            }
            catch (Exception e)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{mountCommand.Id}|{e.Message}" };
                MonitorLog.LogToMonitor(monitorItem);

                mountCommand.Exception = e;
                mountCommand.Successful = false;
            }
        }

        /// <summary>
        /// Startup Queues
        /// </summary>
        /// <param name="serial"></param>
        /// <param name="customMount360Steps"></param>
        /// <param name="customRaWormSteps"></param>
        /// <param name="lowVoltageEventHandler"></param>
        public void Start(ISerialPort serial, int[] customMount360Steps, double[] customRaWormSteps, EventHandler lowVoltageEventHandler = null)
        {
            try
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{serial?.IsOpen}|{customMount360Steps}|{customRaWormSteps}" };
                MonitorLog.LogToMonitor(monitorItem);

                Serial = serial;
                CustomMount360Steps = customMount360Steps;
                CustomRaWormSteps = customRaWormSteps;
                Stop();
                if (_cts == null) _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                mountCommander = new SkyWatcher();
                mountCommander.LowVoltageEvent += lowVoltageEventHandler;

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
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{IsRunning}|{ex}" };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            mountCommander = null;
            _resultsDictionary = null;
            _commandBlockingCollection = null;
        }

        /// <summary>
        /// called from the setter property.  property name is not required
        /// </summary>
        /// <param name="propertyName"></param>
        private void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
