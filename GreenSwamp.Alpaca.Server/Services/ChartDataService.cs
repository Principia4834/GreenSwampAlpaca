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
using static GreenSwamp.Alpaca.Server.Services.TelescopeStateService;

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

        private readonly TelescopeStateService _telescopeState;
        private readonly ConcurrentDictionary<int, int> _raDecSubscribersByDevice = new();

        public ChartDataService(
            IHubContext<ChartHub> hub,
            ILogger<ChartDataService> logger,
            TelescopeStateService telescopeState)
        {
            _hub = hub;
            _logger = logger;
            _telescopeState = telescopeState;

            _telescopeState.DeviceStateChanged += OnTelescopeStatePropertyChanged;
            MonitorQueue.StaticPropertyChanged += OnMonitorQueuePropertyChanged; // keep only pulse path
        }
        // -- Subscriber gate API (called by ChartHub) ----------------------------------------------

        /// <summary>Called when a client joins an RA/Dec chart group. Opens the data gate on first subscriber.</summary>
        public void OnRaDecClientJoined(int deviceNumber) =>
            _raDecSubscribersByDevice.AddOrUpdate(deviceNumber, 1, (_, v) => v + 1);

        public void OnRaDecClientLeft(int deviceNumber)
        {
            _raDecSubscribersByDevice.AddOrUpdate(deviceNumber, 0, (_, v) => Math.Max(0, v - 1));
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

        /// <summary>
        /// Called when the TelescopeStateService raises a DeviceStateChanged event. If the property is AxisSteps,
        /// this method updates the RA/Dec chart buffers and notifies connected clients.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnTelescopeStatePropertyChanged(object? sender, TelescopeStateChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TelescopeStateModel.AxisSteps)) return;
            if (!_raDecSubscribersByDevice.TryGetValue(e.DeviceNumber, out var subs) || subs <= 0) return;

            var steps = e.State.AxisSteps;
            if (steps is null || steps.Length < 2) return;

            var tsMs = new DateTimeOffset(e.State.LastUpdate, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var group = $"RaDecChart-{e.DeviceNumber}";

            var axis1Point = new ChartPointDto(tsMs, steps[0]);
            var axis2Point = new ChartPointDto(tsMs, steps[1]);

            var a1 = _axis1Buffers.GetOrAdd(e.DeviceNumber, _ => new());
            var a1c = _axis1Counts.GetOrAdd(e.DeviceNumber, 0);
            EnqueueItem(a1, ref a1c, axis1Point);
            _axis1Counts[e.DeviceNumber] = a1c;

            var a2 = _axis2Buffers.GetOrAdd(e.DeviceNumber, _ => new());
            var a2c = _axis2Counts.GetOrAdd(e.DeviceNumber, 0);
            EnqueueItem(a2, ref a2c, axis2Point);
            _axis2Counts[e.DeviceNumber] = a2c;

            _ = _hub.Clients.Group(group).SendAsync(
                "ReceiveAxisPoint",
                new[] { axis1Point, axis2Point });
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
                case nameof(MonitorQueue.PulseEntry):
                    HandlePulseEntry(MonitorQueue.PulseEntry);
                    break;
                default:
                    break; // ignore other properties; RA/Dec is handled via TelescopeStateService
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
