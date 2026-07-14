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

using GreenSwamp.Alpaca.Shared;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Singleton service that bridges the static <see cref="MonitorQueue.StaticPropertyChanged"/>
    /// event into a Blazor-friendly change notification.  Keeps a bounded in-memory ring of
    /// formatted monitor lines and fires <see cref="RecordsChanged"/> at most once per
    /// <see cref="ThrottleMs"/> milliseconds so pages can call StateHasChanged safely.
    /// </summary>
    public sealed class MonitorDisplayService : IDisposable
    {
        /// <summary>Maximum number of formatted lines retained in memory.</summary>
        public const int Capacity = 500;

        /// <summary>Minimum interval between <see cref="RecordsChanged"/> notifications (ms).</summary>
        public const int ThrottleMs = 250;

        private readonly ConcurrentQueue<string> _lines = new();
        private int _lineCount;

        private volatile bool _dirty;
        private readonly Timer _throttleTimer;

        /// <summary>
        /// Raised on the thread-pool at most every <see cref="ThrottleMs"/> ms when new records
        /// have arrived.  Blazor pages should call <c>InvokeAsync(StateHasChanged)</c> in the handler.
        /// </summary>
        public event EventHandler? RecordsChanged;

        public MonitorDisplayService()
        {
            MonitorQueue.StaticPropertyChanged += OnMonitorQueuePropertyChanged;

            _throttleTimer = new Timer(OnThrottleTick, null,
                TimeSpan.FromMilliseconds(ThrottleMs),
                TimeSpan.FromMilliseconds(ThrottleMs));
        }

        // -- Event handler ----------------------------------------------------

        private void OnMonitorQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MonitorQueue.MonitorEntry)) return;

            var entry = MonitorQueue.MonitorEntry;
            if (entry is null) return;

            var line = FormatEntry(entry);
            _lines.Enqueue(line);

            // Trim to capacity: add one; if over the limit, evict the oldest entry
            if (Interlocked.Increment(ref _lineCount) > Capacity && _lines.TryDequeue(out _))
                Interlocked.Decrement(ref _lineCount);

            _dirty = true;
        }

        private void OnThrottleTick(object? state)
        {
            if (!_dirty) return;
            _dirty = false;
            RecordsChanged?.Invoke(this, EventArgs.Empty);
        }

        // -- Public API -------------------------------------------------------

        /// <summary>
        /// Returns a snapshot of all current lines in chronological order (oldest first).
        /// </summary>
        public IReadOnlyList<string> GetSnapshot() => _lines.ToArray();

        /// <summary>
        /// Clears the in-memory display buffer.
        /// </summary>
        public void Clear()
        {
            while (_lines.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _lineCount, 0);
            _dirty = true;
        }

        // -- Helpers ----------------------------------------------------------

        private static string FormatEntry(MonitorEntry entry) =>
            $"{entry.Datetime.ToLocalTime():HH:mm:ss.fff zzz} [{entry.Index:D4}] [{entry.DeviceNumber}] {entry.Device,-12} {entry.Category,-12} {entry.Type,-12} {entry.Method} | {entry.Message}";

        public void Dispose()
        {
            MonitorQueue.StaticPropertyChanged -= OnMonitorQueuePropertyChanged;
            _throttleTimer.Dispose();
        }
    }
}
