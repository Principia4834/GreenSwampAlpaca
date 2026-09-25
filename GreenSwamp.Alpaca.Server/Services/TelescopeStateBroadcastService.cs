﻿/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

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
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using static GreenSwamp.Alpaca.Server.Services.TelescopeStateService;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Singleton service that bridges <see cref="TelescopeStateService.DeviceStateChanged"/> into
    /// real-time SignalR broadcasts to <see cref="TelescopeStateHub"/> subscribers, and resolves the
    /// "no active view = no data" gap (spec §4) by registering each subscribed hub connection as an
    /// active device view in <see cref="ActiveDeviceViewRegistry"/>, refreshed on a keep-alive timer.
    ///
    /// Registered as a singleton so the TelescopeStateService subscription and keep-alive timers
    /// persist for the lifetime of the server process, independent of individual hub connections.
    /// Group names follow the pattern "TelescopeState-{n}", mirroring <see cref="ChartDataService"/>.
    /// </summary>
    public sealed class TelescopeStateBroadcastService : IDisposable
    {
        /// <summary>
        /// How often a subscribed connection's view-registry entry is refreshed. Must stay well
        /// inside <see cref="ActiveDeviceViewRegistry"/>'s 10-second staleness window (spec §7.2).
        /// </summary>
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(5);

        private readonly IHubContext<TelescopeStateHub> _hub;
        private readonly ILogger<TelescopeStateBroadcastService> _logger;
        private readonly TelescopeStateService _telescopeState;
        private readonly ActiveDeviceViewRegistry _activeViews;

        // Active subscriber counts per device — gates broadcasts the same way
        // ChartDataService gates RA/Dec chart broadcasts.
        private readonly ConcurrentDictionary<int, int> _subscribersByDevice = new();

        // One keep-alive timer per (connectionId, deviceNumber) pair so a single connection can
        // subscribe to more than one device without the entries colliding in ActiveDeviceViewRegistry
        // (whose key is a single view-session id mapped to exactly one device).
        private readonly ConcurrentDictionary<string, Timer> _keepAliveTimers = new();

        public TelescopeStateBroadcastService(
            IHubContext<TelescopeStateHub> hub,
            ILogger<TelescopeStateBroadcastService> logger,
            TelescopeStateService telescopeState,
            ActiveDeviceViewRegistry activeViews)
        {
            _hub = hub;
            _logger = logger;
            _telescopeState = telescopeState;
            _activeViews = activeViews;

            _telescopeState.DeviceStateChanged += OnTelescopeStateChanged;
        }

        // -- Subscriber gate API (called by TelescopeStateHub) ------------------------------------

        /// <summary>
        /// Called when a client joins a telescope-state group. Opens the broadcast gate for the
        /// device and starts touching the connection's view-registry entry immediately and then
        /// on a repeating keep-alive timer (spec §7.2), so the device stays "active" for
        /// <see cref="TelescopeStateService"/>'s background loop for as long as the connection stays subscribed.
        /// </summary>
        public void OnClientJoined(string connectionId, int deviceNumber)
        {
            _subscribersByDevice.AddOrUpdate(deviceNumber, 1, (_, v) => v + 1);

            var viewSessionId = BuildViewSessionId(connectionId, deviceNumber);
            _logger.LogInformation("TelescopeStateBroadcastService client joined: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}, ViewSessionId={ViewSessionId}, SubscriberCount={SubscriberCount}",
                connectionId, deviceNumber, viewSessionId, _subscribersByDevice[deviceNumber]);

            _activeViews.Touch(viewSessionId, deviceNumber);
            _logger.LogInformation("TelescopeStateBroadcastService touched active view: ViewSessionId={ViewSessionId}, DeviceNumber={DeviceNumber}",
                viewSessionId, deviceNumber);

            var timer = new Timer(
                _ => _activeViews.Touch(viewSessionId, deviceNumber),
                null, KeepAliveInterval, KeepAliveInterval);

            if (!_keepAliveTimers.TryAdd(viewSessionId, timer))
                timer.Dispose(); // shouldn't happen (duplicate join), but avoid leaking a timer
        }

        /// <summary>
        /// Called when a client leaves a telescope-state group (clean leave or abrupt disconnect).
        /// Closes the broadcast gate when the last subscriber for the device leaves, stops the
        /// keep-alive timer, and removes the corresponding view-registry entry.
        /// </summary>
        public void OnClientLeft(string connectionId, int deviceNumber)
        {
            _subscribersByDevice.AddOrUpdate(deviceNumber, 0, (_, v) => Math.Max(0, v - 1));

            var viewSessionId = BuildViewSessionId(connectionId, deviceNumber);
            _logger.LogInformation("TelescopeStateBroadcastService client left: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}, ViewSessionId={ViewSessionId}, SubscriberCount={SubscriberCount}",
                connectionId, deviceNumber, viewSessionId, _subscribersByDevice.GetValueOrDefault(deviceNumber));

            if (_keepAliveTimers.TryRemove(viewSessionId, out var timer))
                timer.Dispose();

            _activeViews.Remove(viewSessionId);
            _logger.LogInformation("TelescopeStateBroadcastService removed active view: ViewSessionId={ViewSessionId}, DeviceNumber={DeviceNumber}",
                viewSessionId, deviceNumber);
        }

        private static string BuildViewSessionId(string connectionId, int deviceNumber) =>
            $"telescopestatehub:{connectionId}:{deviceNumber}";

        // -- TelescopeStateService event handler --------------------------------------------------

        /// <summary>
        /// Called every ~250 ms tick for each active device (spec §7.4 — inherits the existing
        /// cadence, no separate polling loop). Broadcasts the full snapshot unconditionally to
        /// subscribed groups, per spec §6's note on <c>PropertyName</c> not being change-detection.
        /// </summary>
        private void OnTelescopeStateChanged(object? sender, TelescopeStateChangedEventArgs e)
        {
            _logger.LogDebug("TelescopeStateBroadcastService state changed: DeviceNumber={DeviceNumber}, PropertyName={PropertyName}",
                e.DeviceNumber, e.PropertyName);

            if (!_subscribersByDevice.TryGetValue(e.DeviceNumber, out var subs) || subs <= 0)
            {
                _logger.LogDebug("TelescopeStateBroadcastService skipped send because there are no subscribers: DeviceNumber={DeviceNumber}",
                    e.DeviceNumber);
                return;
            }

            _logger.LogInformation("TelescopeStateBroadcastService sending snapshot: DeviceNumber={DeviceNumber}, SubscriberCount={SubscriberCount}, Group={Group}",
                e.DeviceNumber, subs, $"TelescopeState-{e.DeviceNumber}");

            _ = _hub.Clients.Group($"TelescopeState-{e.DeviceNumber}")
                .SendAsync("ReceiveTelescopeState", e.State)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _logger.LogError(t.Exception, "TelescopeStateBroadcastService SignalR send failed: DeviceNumber={DeviceNumber}", e.DeviceNumber);
                    else
                        _logger.LogInformation("TelescopeStateBroadcastService SignalR send completed: DeviceNumber={DeviceNumber}", e.DeviceNumber);
                }, TaskScheduler.Default);
        }

        // -- IDisposable ---------------------------------------------------------------------------

        public void Dispose()
        {
            _telescopeState.DeviceStateChanged -= OnTelescopeStateChanged;

            foreach (var timer in _keepAliveTimers.Values)
                timer.Dispose();
            _keepAliveTimers.Clear();
        }
    }
}
