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
    /// </summary>
    public sealed class ChartDataService : IDisposable
    {
        /// <summary>Maximum buffered points retained per series for historical data requests.</summary>
        public const int MaxBufferPoints = 5000;

        private readonly IHubContext<ChartHub> _hub;
        private readonly ILogger<ChartDataService> _logger;

        // Ring buffers for historical data — new chart windows request these on connect
        private readonly ConcurrentQueue<ChartPointDto> _axis1Buffer = new();
        private readonly ConcurrentQueue<ChartPointDto> _axis2Buffer = new();
        private readonly ConcurrentQueue<PulsePointDto> _raBuffer = new();
        private readonly ConcurrentQueue<PulsePointDto> _decBuffer = new();

        private int _axis1Count;
        private int _axis2Count;
        private int _raCount;
        private int _decCount;

        public ChartDataService(IHubContext<ChartHub> hub, ILogger<ChartDataService> logger)
        {
            _hub = hub;
            _logger = logger;
            MonitorQueue.StaticPropertyChanged += OnMonitorQueuePropertyChanged;
        }

        // ── Public API for ChartHub historical data requests ────────────────────────

        /// <summary>Returns a snapshot of axis-1 and axis-2 buffers (oldest first).</summary>
        public (IReadOnlyList<ChartPointDto> Axis1, IReadOnlyList<ChartPointDto> Axis2) GetRaDecHistory()
            => (_axis1Buffer.ToArray(), _axis2Buffer.ToArray());

        /// <summary>Returns a snapshot of RA and Dec pulse buffers (oldest first).</summary>
        public (IReadOnlyList<PulsePointDto> Ra, IReadOnlyList<PulsePointDto> Dec) GetPulseHistory()
            => (_raBuffer.ToArray(), _decBuffer.ToArray());

        /// <summary>Clears all ring buffers (e.g. when user clicks Clear in the chart UI).</summary>
        public void ClearBuffers()
        {
            while (_axis1Buffer.TryDequeue(out _)) { }
            while (_axis2Buffer.TryDequeue(out _)) { }
            while (_raBuffer.TryDequeue(out _)) { }
            while (_decBuffer.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _axis1Count, 0);
            Interlocked.Exchange(ref _axis2Count, 0);
            Interlocked.Exchange(ref _raCount, 0);
            Interlocked.Exchange(ref _decCount, 0);
        }

        // ── MonitorQueue event handler ───────────────────────────────────────────────

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

        // ── RA/Dec handling ──────────────────────────────────────────────────────────

        private void HandleRaDecEntry(MonitorEntry? entry, bool isAxis1)
        {
            if (entry is null) return;

            // Message format: "<cmd>|<rawHex>|<longValue>"
            // The third pipe-delimited segment is the numeric step/degree value.
            var parts = entry.Message.Split('|');
            if (parts.Length < 3) return;
            if (!double.TryParse(parts[2].Trim(), out var value)) return;

            var tsMs = new DateTimeOffset(entry.Datetime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var point = new ChartPointDto(tsMs, value);

            if (isAxis1)
            {
                Enqueue(_axis1Buffer, ref _axis1Count, point);
                _ = _hub.Clients.Group("RaDecChart")
                        .SendAsync("ReceiveAxis1Point", point);
            }
            else
            {
                Enqueue(_axis2Buffer, ref _axis2Count, point);
                _ = _hub.Clients.Group("RaDecChart")
                        .SendAsync("ReceiveAxis2Point", point);
            }
        }

        // ── Pulse handling ───────────────────────────────────────────────────────────

        private void HandlePulseEntry(PulseEntry? entry)
        {
            if (entry is null) return;

            var tsMs = new DateTimeOffset(entry.StartTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var point = new PulsePointDto(tsMs, entry.Duration, entry.Rate, entry.Axis, entry.Rejected);

            if (entry.Axis == 0)
            {
                Enqueue(_raBuffer, ref _raCount, point);
            }
            else
            {
                Enqueue(_decBuffer, ref _decCount, point);
            }

            _ = _hub.Clients.Group("PulseChart")
                    .SendAsync("ReceivePulsePoint", point);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static void Enqueue<T>(ConcurrentQueue<T> queue, ref int count, T item)
        {
            queue.Enqueue(item);
            if (Interlocked.Increment(ref count) > MaxBufferPoints && queue.TryDequeue(out _))
                Interlocked.Decrement(ref count);
        }

        public void Dispose()
        {
            MonitorQueue.StaticPropertyChanged -= OnMonitorQueuePropertyChanged;
        }
    }
}
