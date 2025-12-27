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
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;

namespace GreenSwamp.Alpaca.Mount.SkyWatcher
{
    /// <summary>
    /// Implementation of command queue for SkyWatcher
    /// </summary>
    internal class SkyQueueImplementation : CommandQueueBase<SkyWatcher>
    {
        private ISerialPort _serial;
        private int[] _customMount360Steps;
        private double[] _customRaWormSteps;
        private EventHandler _lowVoltageEventHandler;

        public ISerialPort Serial => _serial;
        public int[] CustomMount360Steps => _customMount360Steps;
        public double[] CustomRaWormSteps => _customRaWormSteps;

        public void Start(ISerialPort serial, int[] customMount360Steps, double[] customRaWormSteps, EventHandler lowVoltageEventHandler = null)
        {
            try
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{serial?.IsOpen}|{customMount360Steps}|{customRaWormSteps}" };
                MonitorLog.LogToMonitor(monitorItem);

                _serial = serial;
                _customMount360Steps = customMount360Steps;
                _customRaWormSteps = customRaWormSteps;
                _lowVoltageEventHandler = lowVoltageEventHandler;

                base.Start();
            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{IsRunning}|{ex}" };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        protected override bool IsConnected()
        {
            return _executor?.IsConnected == true;
        }

        protected override SkyWatcher CreateExecutor()
        {
            return new SkyWatcher();
        }

        protected override void InitializeExecutor(SkyWatcher executor)
        {
            if (executor != null && _lowVoltageEventHandler != null)
            {
                executor.LowVoltageEvent += _lowVoltageEventHandler;
            }
        }

        protected override void CleanupExecutor(SkyWatcher executor)
        {
            if (executor != null && _lowVoltageEventHandler != null)
            {
                executor.LowVoltageEvent -= _lowVoltageEventHandler;
            }
        }
    }

    public static class SkyQueue
    {
        private static readonly SkyQueueImplementation _instance = new SkyQueueImplementation();
        private static bool _isPulseGuidingDec;
        private static bool _isPulseGuidingRa;
        private static double[] _steps;

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Instance for use by command constructors
        /// </summary>
        internal static SkyQueueImplementation Instance => _instance;

        /// <summary>
        /// Serial object
        /// </summary>
        internal static ISerialPort Serial => _instance.Serial;

        /// <summary>
        /// Custom Mount :s replacement
        /// </summary>
        internal static int[] CustomMount360Steps => _instance.CustomMount360Steps;

        /// <summary>
        /// Custom Mount :a replacement
        /// </summary>
        internal static double[] CustomRaWormSteps => _instance.CustomRaWormSteps;

        /// <summary>
        /// IsRunning
        /// </summary>
        public static bool IsRunning => _instance.IsRunning;

        /// <summary>
        /// Locking id
        /// </summary>
        public static long NewId => _instance.NewId;

        /// <summary>
        /// status for Dec Pulse
        /// </summary>
        public static bool IsPulseGuidingDec
        {
            get => _isPulseGuidingDec;
            set
            {
                _isPulseGuidingDec = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// status for Ra Pulse
        /// </summary>
        public static bool IsPulseGuidingRa
        {
            get => _isPulseGuidingRa;
            set
            {
                _isPulseGuidingRa = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// current steps, main property used to update Server and UI
        /// </summary>
        public static double[] Steps
        {
            get => _steps;
            set
            {
                _steps = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Add a command to the blocking queue
        /// </summary>
        /// <param name="command"></param>
        public static void AddCommand(ISkyCommand command)
        {
            _instance.AddCommand(command);
        }

        /// <summary>
        /// Mount data results
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public static ISkyCommand GetCommandResult(ISkyCommand command)
        {
            return (ISkyCommand)_instance.GetCommandResult(command);
        }

        /// <summary>
        /// Startup Queues
        /// </summary>
        /// <param name="serial"></param>
        /// <param name="customMount360Steps"></param>
        /// <param name="customRaWormSteps"></param>
        /// <param name="lowVoltageEventHandler"></param>
        public static void Start(ISerialPort serial, int[] customMount360Steps, double[] customRaWormSteps, EventHandler lowVoltageEventHandler = null)
        {
            _instance.Start(serial, customMount360Steps, customRaWormSteps, lowVoltageEventHandler);
        }

        /// <summary>
        /// Stop
        /// </summary>
        public static void Stop()
        {
            _instance.Stop();
        }

        /// <summary>
        /// called from the setter property.  property name is not required
        /// </summary>
        /// <param name="propertyName"></param>
        private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }
    }
}
