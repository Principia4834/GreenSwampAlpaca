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
using System.IO;
using System.Text;
using System.Threading;

namespace GreenSwamp.Alpaca.Shared
{
    /// <summary>
    /// Thread-safe rotating ring buffer for fast monitor logging.
    /// Holds up to <see cref="Capacity"/> MonitorEntry records.
    /// Oldest records are silently overwritten when the buffer is full.
    /// Call <see cref="WriteBuffer"/> to persist a snapshot to disk without clearing the buffer.
    /// </summary>
    public static class FastMonitorBuffer
    {
        #region Fields

        /// <summary>Maximum number of entries held in the ring buffer.</summary>
        public const int Capacity = 2000;

        private static readonly MonitorEntry[] _buffer = new MonitorEntry[Capacity];
        private static int _ringIndex;    // next write slot (0-based, wraps at Capacity)
        private static int _recordIndex;  // global record index (monotonically increasing, not reset or wrapped)                      
        private static int _count;        // number of valid entries (capped at Capacity)
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private const string Fmt = "0000#";

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds an entry to the ring buffer. Thread-safe.
        /// When the buffer is full the oldest entry is silently overwritten.
        /// </summary>
        public static void Add(MonitorEntry entry)
        {
            lock (_lock)
            {
                _buffer[_ringIndex] = entry;
                _ringIndex = (_ringIndex + 1) % Capacity;
                if (_count < Capacity) _count++;
                _recordIndex++;
            }
        }

        /// <summary>
        /// Snapshots the current buffer and writes it to a new datetime-stamped file in the
        /// standard log directory. The buffer is NOT cleared — the ring continues rolling.
        /// Concurrent calls are serialised via an internal semaphore.
        /// </summary>
        public static void WriteBuffer()
        {
            var snapshot = GetSnapshot();
            if (snapshot.Length == 0) return;

            _fileLock.Wait();
            try
            {
                var filePath = Path.Combine(
                    GsFile.GetLogPath(),
                    $"GSFastMonitorLog{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt");

                Directory.CreateDirectory(
                    Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException());

                using var sw = new StreamWriter(filePath, append: false, Encoding.UTF8);
                int idx = _recordIndex - snapshot.Length;
                foreach (var entry in snapshot)
                {
                    idx++;
                    sw.WriteLine(
                        $"{entry.Datetime.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}" +
                        $"|{idx.ToString(Fmt)}" +
                        $"|{entry.DeviceNumber}|{entry.Device}|{entry.Category}|{entry.Type}" +
                        $"|{entry.Thread}|{entry.Method}|{entry.Message}");
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Returns a snapshot of the current buffer contents in insertion order (oldest first).
        /// </summary>
        internal static MonitorEntry[] GetSnapshot()
        {
            lock (_lock)
            {
                var snapshot = new MonitorEntry[_count];
                // When not yet full start from index 0; when full start from _ringIndex (oldest slot)
                int startIndex = (_count < Capacity) ? 0 : _ringIndex;
                for (int i = 0; i < _count; i++)
                    snapshot[i] = _buffer[(startIndex + i) % Capacity];
                return snapshot;
            }
        }

        #endregion
    }
}
