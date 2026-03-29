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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.Mount.Simulator
{
    /// <summary>
    /// Implementation of command queue for Simulator
    /// </summary>
    public class MountQueueImplementation : CommandQueueBase<Actions>
    {
        protected override int CompletionTimeoutMs => 22000;
        protected override string[] DiagnosticCommandFilter => ["CmdAxisPulse"];

        private Action<double[]>? _stepsCallback;
        private Action<bool>? _pulseGuideRaCallback;
        private Action<bool>? _pulseGuideDecCallback;

        public void SetupCallbacks(Action<double[]>? stepsCallback, Action<bool>? pulseGuideRaCallback, Action<bool>? pulseGuideDecCallback)
        {
            _stepsCallback = stepsCallback;
            _pulseGuideRaCallback = pulseGuideRaCallback;
            _pulseGuideDecCallback = pulseGuideDecCallback;
        }

        protected override bool IsConnected()
        {
            return Actions.IsConnected;
        }

        protected override Actions CreateExecutor()
        {
            return new Actions();
        }

        protected override void InitializeExecutor(Actions executor)
        {
            executor?.InitializeAxes();
            executor?.SetCallbacks(_stepsCallback, _pulseGuideRaCallback, _pulseGuideDecCallback);
        }

        protected override void CleanupExecutor(Actions executor)
        {
            executor?.Shutdown();
        }
    }

    public static class MountQueue
    {
        // Q2: Non-readonly; MountInstance registers its owned implementation via RegisterInstance()
        private static MountQueueImplementation _instance;
        private static bool _isPulseGuidingDec;
        private static bool _isPulseGuidingRa;
        private static double[] _steps;

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Register the instance-owned queue so the static facade delegates to it.
        /// Called by MountInstance.MountStart() before the queue is started.
        /// </summary>
        public static void RegisterInstance(MountQueueImplementation impl)
        {
            _instance = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        /// <summary>
        /// Instance for use by command constructors and MountInstance queue ownership.
        /// </summary>
        public static CommandQueueBase<Actions> Instance => _instance;

        /// <summary>
        /// IsRunning
        /// </summary>
        public static bool IsRunning => _instance?.IsRunning ?? false;

        /// <summary>
        /// Locking id
        /// </summary>
        public static long NewId => _instance?.NewId ?? 0;

        /// <summary>
        /// Thread-safe queue processing statistics for the current session.
        /// </summary>
        public static CommandQueueStatistics Statistics => _instance?.Statistics;

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
        /// current micro steps, used to update SkyServer and UI
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
        public static void AddCommand(IMountCommand command)
        {
            _instance?.AddCommand(command);
        }

        /// <summary>
        /// Mount data results
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public static IMountCommand GetCommandResult(IMountCommand command)
        {
            return _instance == null ? command : (IMountCommand)_instance.GetCommandResult(command);
        }

        /// <summary>
        /// Start the queue — delegates to the instance registered via RegisterInstance().
        /// </summary>
        public static void Start()
        {
            if (_instance == null) throw new InvalidOperationException("No MountQueueImplementation registered. Call RegisterInstance() before Start().");
            _instance.Start();
        }

        /// <summary>
        /// Stop the queue
        /// </summary>
        public static void Stop()
        {
            _instance?.Stop();
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
