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

using GreenSwamp.Alpaca.Server.Hubs;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Singleton service that bridges <see cref="MonitorQueue"/> static property-changed events
    /// into real-time SignalR broadcasts to chart windows.
    ///
    /// Registered as a singleton so the MonitorQueue subscription and ring buffers persist
    /// for the lifetime of the server process, independent of individual Blazor circuits.
    ///
    /// RA/Dec axis-1 data comes from <see cref="MonitorQueue.CmdjSentEntry"/> (j1 / steps1).
    /// RA/Dec axis-2 data comes from <see cref="MonitorQueue.Cmdj2SentEntry"/> (j2 / steps2).
    /// Pulse data comes from <see cref="MonitorQueue.PulseEntry"/>.
    ///
    /// Buffers and SignalR groups are keyed by device number so multiple telescope instances
    /// are kept separate. Group names follow the pattern "RaDecChart-{n}" / "PulseChart-{n}".
    /// </summary>
    public sealed class ChartDataService : IDisposable
    {
        /// <summary>Maximum buffered points retained per series per device for historical data requests.</summary>
        public const int MaxBufferPoints = 5000;

        private readonly IHubContext<ChartHub> _hub;
        private readonly ILogger<ChartDataService> _logger;

        // Per-device ring buffers: key = deviceNumber
        private readonly ConcurrentDictionary<int, ConcurrentQueue<ChartPointDto>> _axis1Buffers = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<ChartPointDto>> _axis2Buffers = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<PulsePointDto>> _raBuffers = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<PulsePointDto>> _decBuffers = new();
        private readonly ConcurrentDictionary<int, int> _axis1Counts = new();
        private readonly ConcurrentDictionary<int, int> _axis2Counts = new();
        private readonly ConcurrentDictionary<int, int> _raCounts = new();
        private readonly ConcurrentDictionary<int, int> _decCounts = new();

        // Active subscriber counts — drive GetJEntries / GetPulses gates
        private int _raDecSubscribers;
        private int _pulseSubscribers;

        public ChartDataService(IHubContext<ChartHub> hub, ILogger<ChartDataService> logger)
        {
            _hub = hub;
            _logger = logger;
            MonitorQueue.StaticPropertyChanged += OnMonitorQueuePropertyChanged;
        }

        // -- Subscriber gate API (called by ChartHub) ----------------------------------------------

        /// <summary>Called when a client joins an RA/Dec chart group. Opens the data gate on first subscriber.</summary>
        public void OnRaDecClientJoined()
        {
            if (Interlocked.Increment(ref _raDecSubscribers) == 1)
                MonitorLog.GetJEntries = true;
        }

        /// <summary>Called when a client leaves an RA/Dec chart group. Closes the data gate when last subscriber leaves.</summary>
        public void OnRaDecClientLeft()
        {
            if (Interlocked.Decrement(ref _raDecSubscribers) <= 0)
            {
                Interlocked.Exchange(ref _raDecSubscribers, 0);
                MonitorLog.GetJEntries = false;
            }
        }

        /// <summary>Called when a client joins a Pulse chart group. Opens the pulse gate on first subscriber.</summary>
        public void OnPulseClientJoined()
        {
            if (Interlocked.Increment(ref _pulseSubscribers) == 1)
                MonitorLog.GetPulses = true;
        }

        /// <summary>Called when a client leaves a Pulse chart group. Closes the pulse gate when last subscriber leaves.</summary>
        public void OnPulseClientLeft()
        {
            if (Interlocked.Decrement(ref _pulseSubscribers) <= 0)
            {
                Interlocked.Exchange(ref _pulseSubscribers, 0);
                MonitorLog.GetPulses = false;
            }
        }

        // -- Public API for ChartHub historical data requests ------------------------------------

        /// <summary>Returns a snapshot of axis-1 and axis-2 buffers for a device (oldest first).</summary>
        public (IReadOnlyList<ChartPointDto> Axis1, IReadOnlyList<ChartPointDto> Axis2) GetRaDecHistory(int deviceNumber)
        {
            var a1 = _axis1Buffers.TryGetValue(deviceNumber, out var q1) ? q1.ToArray() : [];
            var a2 = _axis2Buffers.TryGetValue(deviceNumber, out var q2) ? q2.ToArray() : [];
            return (a1, a2);
        }

        /// <summary>Returns a snapshot of RA and Dec pulse buffers for a device (oldest first).</summary>
        public (IReadOnlyList<PulsePointDto> Ra, IReadOnlyList<PulsePointDto> Dec) GetPulseHistory(int deviceNumber)
        {
            var ra = _raBuffers.TryGetValue(deviceNumber, out var q1) ? q1.ToArray() : [];
            var dec = _decBuffers.TryGetValue(deviceNumber, out var q2) ? q2.ToArray() : [];
            return (ra, dec);
        }

        /// <summary>Clears all ring buffers for a device (e.g. when user clicks Clear in the chart UI).</summary>
        public void ClearBuffers(int deviceNumber)
        {
            if (_axis1Buffers.TryGetValue(deviceNumber, out var a1)) while (a1.TryDequeue(out _)) { }
            if (_axis2Buffers.TryGetValue(deviceNumber, out var a2)) while (a2.TryDequeue(out _)) { }
            if (_raBuffers.TryGetValue(deviceNumber, out var ra)) while (ra.TryDequeue(out _)) { }
            if (_decBuffers.TryGetValue(deviceNumber, out var dec)) while (dec.TryDequeue(out _)) { }
            _axis1Counts[deviceNumber] = 0;
            _axis2Counts[deviceNumber] = 0;
            _raCounts[deviceNumber] = 0;
            _decCounts[deviceNumber] = 0;
        }

        // -- MonitorQueue event handler ------------------------------------------------------------

        private void OnMonitorQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MonitorQueue.CmdjSentEntry):
                    HandleRaDecEntry(MonitorQueue.CmdjSentEntry, isAxis1: true);
                    break;

                case nameof(MonitorQueue.Cmdj2SentEntry):
                    HandleRaDecEntry(MonitorQueue.Cmdj2SentEntry, isAxis1: false);
                    break;

                case nameof(MonitorQueue.PulseEntry):
                    HandlePulseEntry(MonitorQueue.PulseEntry);
                    break;
            }
        }

        // -- RA/Dec handling -----------------------------------------------------------------------

        private void HandleRaDecEntry(MonitorEntry? entry, bool isAxis1)
        {
            if (entry is null) return;

            // Message format: "<cmd>|<rawHex>|<longValue>"
            // The third pipe-delimited segment is the numeric step/degree value.
            var parts = entry.Message.Split('|');
            if (parts.Length < 3) return;
            if (!double.TryParse(parts[2].Trim(), out var value)) return;

            var dn = entry.DeviceNumber;
            var tsMs = new DateTimeOffset(entry.Datetime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var point = new ChartPointDto(tsMs, value);
            var group = $"RaDecChart-{dn}";

            if (isAxis1)
            {
                var buf = _axis1Buffers.GetOrAdd(dn, _ => new());
                var cnt = _axis1Counts.GetOrAdd(dn, 0);
                EnqueueItem(buf, ref cnt, point);
                _axis1Counts[dn] = cnt;
                _ = _hub.Clients.Group(group).SendAsync("ReceiveAxis1Point", point);
            }
            else
            {
                var buf = _axis2Buffers.GetOrAdd(dn, _ => new());
                var cnt = _axis2Counts.GetOrAdd(dn, 0);
                EnqueueItem(buf, ref cnt, point);
                _axis2Counts[dn] = cnt;
                _ = _hub.Clients.Group(group).SendAsync("ReceiveAxis2Point", point);
            }
        }

        // -- Pulse handling ------------------------------------------------------------------------

        private void HandlePulseEntry(PulseEntry? entry)
        {
            if (entry is null) return;

            var dn = entry.DeviceNumber;
            var tsMs = new DateTimeOffset(entry.StartTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var point = new PulsePointDto(tsMs, entry.Duration, entry.Rate, entry.Axis, entry.Rejected);
            var group = $"PulseChart-{dn}";

            if (entry.Axis == 0)
            {
                var buf = _raBuffers.GetOrAdd(dn, _ => new());
                var cnt = _raCounts.GetOrAdd(dn, 0);
                EnqueueItem(buf, ref cnt, point);
                _raCounts[dn] = cnt;
            }
            else
            {
                var buf = _decBuffers.GetOrAdd(dn, _ => new());
                var cnt = _decCounts.GetOrAdd(dn, 0);
                EnqueueItem(buf, ref cnt, point);
                _decCounts[dn] = cnt;
            }

            _ = _hub.Clients.Group(group).SendAsync("ReceivePulsePoint", point);
        }

        // -- Buffer helpers ------------------------------------------------------------------------

        private static void EnqueueItem<T>(ConcurrentQueue<T> queue, ref int count, T item)
        {
            queue.Enqueue(item);
            if (Interlocked.Increment(ref count) > MaxBufferPoints)
            {
                queue.TryDequeue(out _);
                Interlocked.Decrement(ref count);
            }
        }

        // -- IDisposable ---------------------------------------------------------------------------

        public void Dispose()
        {
            MonitorLog.GetJEntries = false;
            MonitorLog.GetPulses   = false;
            MonitorQueue.StaticPropertyChanged -= OnMonitorQueuePropertyChanged;
        }
    }
}
