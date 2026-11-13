/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

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

using System;
using System.Runtime.Versioning;
using System.Threading;

namespace GreenSwamp.Alpaca.Principles
{
    /// <summary>
    /// Cross-platform timer implementation for Linux and Raspberry Pi.
    /// Provides the same <see cref="IMediaTimer"/> contract as <see cref="MediaTimer"/>
    /// without any Windows P/Invoke dependencies.
    /// Uses a dedicated background thread with <see cref="Thread.Sleep"/> to fire
    /// the <see cref="Tick"/> event at the requested period.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    public sealed class LinuxMediaTimer : IMediaTimer
    {
        private volatile bool _disposed;
        private volatile bool _running;
        private volatile int _period = 1;
        private volatile int _resolution = 1;
        private volatile TimerMode _mode = TimerMode.Periodic;
        private Thread? _thread;
        private readonly object _lock = new();

        /// <inheritdoc/>
        public event EventHandler? Tick;

        /// <inheritdoc/>
        public event EventHandler? Started;

        /// <inheritdoc/>
        public event EventHandler? Stopped;

        /// <inheritdoc/>
        public int Period
        {
            get => _period;
            set
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LinuxMediaTimer));
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(Period), value, "Period must be greater than zero.");
                if (_period == value) return;
                _period = value;
                if (_running)
                {
                    Stop();
                    Start();
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>Resolution is accepted but not enforced — Linux kernel scheduling determines actual precision.</remarks>
        public int Resolution
        {
            get => _resolution;
            set
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LinuxMediaTimer));
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Resolution), value, "Resolution must be >= 0.");
                _resolution = value;
            }
        }

        /// <inheritdoc/>
        public TimerMode Mode
        {
            get => _mode;
            set
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LinuxMediaTimer));
                if (_mode == value) return;
                _mode = value;
                if (_running)
                {
                    Stop();
                    Start();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsRunning => _running;

        /// <inheritdoc/>
        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LinuxMediaTimer));
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _thread = new Thread(RunLoop)
                {
                    IsBackground = true,
                    Name = "LinuxMediaTimer"
                };
                _thread.Start();
            }
            Started?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Stop()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                _thread = null;
            }
            Stopped?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        private void RunLoop()
        {
            while (_running)
            {
                Thread.Sleep(_period);
                if (!_running) break;
                Tick?.Invoke(this, EventArgs.Empty);
                if (_mode == TimerMode.OneShot)
                {
                    Stop();
                    break;
                }
            }
        }
    }
}
