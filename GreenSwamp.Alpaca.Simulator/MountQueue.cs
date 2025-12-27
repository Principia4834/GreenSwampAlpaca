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

namespace GreenSwamp.Alpaca.Mount.Simulator
{
    /// <summary>
    /// Implementation of command queue for Simulator
    /// </summary>
    internal class MountQueueImplementation : CommandQueueBase<Actions>
    {
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
        }

        protected override void CleanupExecutor(Actions executor)
        {
            executor?.Shutdown();
        }
    }

    public static class MountQueue
    {
        private static readonly MountQueueImplementation _instance = new MountQueueImplementation();
        private static bool _isPulseGuidingDec;
        private static bool _isPulseGuidingRa;
        private static double[] _steps;

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Instance for use by command constructors
        /// </summary>
        internal static MountQueueImplementation Instance => _instance;

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
            _instance.AddCommand(command);
        }

        /// <summary>
        /// Mount data results
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public static IMountCommand GetCommandResult(IMountCommand command)
        {
            return (IMountCommand)_instance.GetCommandResult(command);
        }

        /// <summary>
        /// Start the queue
        /// </summary>
        public static void Start()
        {
            _instance.Start();
        }

        /// <summary>
        /// Stop the queue
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
