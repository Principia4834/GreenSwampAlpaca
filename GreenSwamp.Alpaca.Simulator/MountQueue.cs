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
}
