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

using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace GreenSwamp.Alpaca.Server.Hubs
{
    /// <summary>
    /// SignalR hub that streams live RA/Dec position and pulse guide data to chart windows.
    /// Each chart window identifies its device by passing a deviceNumber to the join/history methods.
    /// Group names are "RaDecChart-{n}" and "PulseChart-{n}".
    ///
    /// Tracks per-connection group membership so that OnDisconnectedAsync can close the
    /// MonitorQueue data gates when the last subscriber disconnects without a clean leave.
    /// </summary>
    public class ChartHub : Hub
    {
        private readonly ChartDataService _chartData;

        // Tracks which chart type ("radec" | "pulse") each connection has joined.
        // Key = connectionId, Value = set of chart-type strings.
        // Static so it survives across the transient hub instances SignalR creates per call.
        private static readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();

        public ChartHub(ChartDataService chartData)
        {
            _chartData = chartData;
        }

        /// <summary>Subscribes the caller to RA/Dec position chart broadcasts for the given device.</summary>
        public async Task JoinRaDecGroupAsync(int deviceNumber)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"RaDecChart-{deviceNumber}");
            if (_connectionGroups.GetOrAdd(Context.ConnectionId, _ => []).Add($"radec:{deviceNumber}"))
                _chartData.OnRaDecClientJoined(deviceNumber);
        }

        /// <summary>Unsubscribes the caller from RA/Dec position chart broadcasts for the given device.</summary>
        public async Task LeaveRaDecGroupAsync(int deviceNumber)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"RaDecChart-{deviceNumber}");
            if (_connectionGroups.TryGetValue(Context.ConnectionId, out var groups) && groups.Remove($"radec:{deviceNumber}"))
                _chartData.OnRaDecClientLeft(deviceNumber);
        }

        /// <summary>Subscribes the caller to pulse guide chart broadcasts for the given device.</summary>
        public async Task JoinPulseGroupAsync(int deviceNumber)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"PulseChart-{deviceNumber}");
            if (_connectionGroups.GetOrAdd(Context.ConnectionId, _ => []).Add("pulse"))
                _chartData.OnPulseClientJoined();
        }

        /// <summary>Unsubscribes the caller from pulse guide chart broadcasts for the given device.</summary>
        public async Task LeavePulseGroupAsync(int deviceNumber)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"PulseChart-{deviceNumber}");
            if (_connectionGroups.TryGetValue(Context.ConnectionId, out var groups) && groups.Remove("pulse"))
                _chartData.OnPulseClientLeft();
        }

        /// <summary>
        /// Returns buffered historical data to the calling client only.
        /// Called by a chart window immediately after joining to pre-populate the chart.
        /// </summary>
        public async Task RequestHistoricalDataAsync(string chartType, int deviceNumber)
        {
            if (chartType == "radec")
            {
                var (axis1, axis2) = _chartData.GetRaDecHistory(deviceNumber);
                await Clients.Caller.SendAsync(
                    "ReceiveRaDecHistory",
                    new HistoricalDataDto(ChartType.RaDec, axis1, axis2));
            }
            else if (chartType == "pulse")
            {
                var (ra, dec) = _chartData.GetPulseHistory(deviceNumber);
                await Clients.Caller.SendAsync("ReceivePulseHistory", ra, dec);
            }
        }

        /// <summary>
        /// Handles abrupt disconnections (browser close, network drop) so the data gates
        /// are closed even when the chart page never called LeaveXxxGroupAsync.
        /// </summary>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionGroups.TryRemove(Context.ConnectionId, out var groups))
            {
                foreach (var g in groups)
                {
                    if (g.StartsWith("radec:") && int.TryParse(g["radec:".Length..], out var dn))
                        _chartData.OnRaDecClientLeft(dn);
                    else if (g == "pulse")
                        _chartData.OnPulseClientLeft();
                }
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
