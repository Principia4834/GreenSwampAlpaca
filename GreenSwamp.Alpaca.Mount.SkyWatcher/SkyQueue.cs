/* Copyright(C) 2019-2026 Rob  Morgan (robert.morgan.e@gmail.com)

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
    public class SkyQueueImplementation : CommandQueueBase<SkyWatcher>
    {
        protected override string[] DiagnosticCommandFilter => ["SkyAxisPulse"];

        private ISerialPort _serial;
        private int[] _customMount360Steps;
        private double[] _customRaWormSteps;
        private EventHandler _lowVoltageEventHandler;
        private Action<double[]>? _stepsCallback;
        private Action<bool>? _pulseGuideRaCallback;
        private Action<bool>? _pulseGuideDecCallback;

        public ISerialPort Serial => _serial;
        public int[] CustomMount360Steps => _customMount360Steps;
        public double[] CustomRaWormSteps => _customRaWormSteps;

        public void Start(ISerialPort serial, int[] customMount360Steps, double[] customRaWormSteps, EventHandler lowVoltageEventHandler = null)
        {
            try
            {
                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{serial?.IsOpen}|{customMount360Steps}|{customRaWormSteps}" };
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
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{IsRunning}|{ex}" };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        /// <summary>
        /// Set the callbacks used by the executor to report steps and pulse-guide state.
        /// Must be called before Start() so the callbacks are available in InitializeExecutor.
        /// </summary>
        public void SetupCallbacks(Action<double[]>? stepsCallback, Action<bool>? pulseGuideRaCallback, Action<bool>? pulseGuideDecCallback)
        {
            _stepsCallback = stepsCallback;
            _pulseGuideRaCallback = pulseGuideRaCallback;
            _pulseGuideDecCallback = pulseGuideDecCallback;
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
            executor?.Initialize(_serial);
            executor?.SetCustomGearing(_customMount360Steps, _customRaWormSteps);
            executor?.SetCallbacks(_stepsCallback, _pulseGuideRaCallback, _pulseGuideDecCallback);
        }

        protected override void CleanupExecutor(SkyWatcher executor)
        {
            if (executor != null && _lowVoltageEventHandler != null)
            {
                executor.LowVoltageEvent -= _lowVoltageEventHandler;
            }
        }
    }
}
