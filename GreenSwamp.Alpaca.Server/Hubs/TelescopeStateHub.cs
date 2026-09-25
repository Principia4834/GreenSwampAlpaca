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

using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GreenSwamp.Alpaca.Server.Hubs
{
    /// <summary>
    /// SignalR hub that streams live full <see cref="Models.TelescopeStateModel"/> snapshots to
    /// subscribed remote clients, per device. Group names are "TelescopeState-{n}".
    ///
    /// Joining also registers the connection as an active device view (via
    /// <see cref="TelescopeStateBroadcastService"/>, which mirrors <see cref="ActiveDeviceViewRegistry"/>
    /// keep-alive semantics) so <see cref="TelescopeStateService"/>'s background loop keeps producing
    /// snapshots for this device even when no Blazor tab is open — see spec §4/§7.2.
    ///
    /// Tracks per-connection group membership so that OnDisconnectedAsync can unregister the view
    /// and subscriber count when the last subscriber disconnects without a clean leave.
    /// </summary>
    public class TelescopeStateHub : Hub
    {
        private readonly TelescopeStateBroadcastService _broadcast;
        private readonly ILogger<TelescopeStateHub> _logger;

        // Tracks which device numbers each connection has joined.
        // Key = connectionId, Value = set of device numbers.
        // Static so it survives across the transient hub instances SignalR creates per call.
        private static readonly ConcurrentDictionary<string, HashSet<int>> _connectionGroups = new();

        public TelescopeStateHub(TelescopeStateBroadcastService broadcast, ILogger<TelescopeStateHub> logger)
        {
            _broadcast = broadcast;
            _logger = logger;
        }

        /// <summary>Subscribes the caller to telescope-state broadcasts for the given device.</summary>
        public async Task JoinTelescopeStateGroupAsync(int deviceNumber)
        {
            var groupName = $"TelescopeState-{deviceNumber}";
            _logger.LogInformation("TelescopeStateHub join requested: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}, Group={Group}",
                Context.ConnectionId, deviceNumber, groupName);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            var added = _connectionGroups.GetOrAdd(Context.ConnectionId, _ => []).Add(deviceNumber);
            _logger.LogInformation("TelescopeStateHub joined group: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}, AddedToConnectionSet={Added}",
                Context.ConnectionId, deviceNumber, added);

            if (added)
                _broadcast.OnClientJoined(Context.ConnectionId, deviceNumber);
        }

        /// <summary>Unsubscribes the caller from telescope-state broadcasts for the given device.</summary>
        public async Task LeaveTelescopeStateGroupAsync(int deviceNumber)
        {
            var groupName = $"TelescopeState-{deviceNumber}";
            _logger.LogInformation("TelescopeStateHub leave requested: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}, Group={Group}",
                Context.ConnectionId, deviceNumber, groupName);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            if (_connectionGroups.TryGetValue(Context.ConnectionId, out var groups) && groups.Remove(deviceNumber))
            {
                _logger.LogInformation("TelescopeStateHub left group: ConnectionId={ConnectionId}, DeviceNumber={DeviceNumber}",
                    Context.ConnectionId, deviceNumber);
                _broadcast.OnClientLeft(Context.ConnectionId, deviceNumber);
            }
        }

        /// <summary>
        /// Handles abrupt disconnections (client crash, network drop) so subscriber counts and
        /// view-registry entries are cleaned up even when the client never called LeaveTelescopeStateGroupAsync.
        /// </summary>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("TelescopeStateHub disconnected: ConnectionId={ConnectionId}, Exception={ExceptionMessage}",
                Context.ConnectionId, exception?.Message ?? "<none>");

            if (_connectionGroups.TryRemove(Context.ConnectionId, out var groups))
            {
                foreach (var deviceNumber in groups)
                    _broadcast.OnClientLeft(Context.ConnectionId, deviceNumber);
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
