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

using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace GreenSwamp.Alpaca.Server.Hubs
{
    /// <summary>
    /// SignalR hub that streams live RA/Dec position and pulse guide data to chart windows.
    /// Chart windows join a named group on connect and leave on disconnect.
    /// </summary>
    public class ChartHub : Hub
    {
        private const string RaDecGroup = "RaDecChart";
        private const string PulseGroup = "PulseChart";

        private readonly ChartDataService _chartData;

        public ChartHub(ChartDataService chartData)
        {
            _chartData = chartData;
        }

        /// <summary>Subscribes the caller to RA/Dec position chart broadcasts.</summary>
        public async Task JoinRaDecGroupAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RaDecGroup);
        }

        /// <summary>Unsubscribes the caller from RA/Dec position chart broadcasts.</summary>
        public async Task LeaveRaDecGroupAsync()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RaDecGroup);
        }

        /// <summary>Subscribes the caller to pulse guide chart broadcasts.</summary>
        public async Task JoinPulseGroupAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PulseGroup);
        }

        /// <summary>Unsubscribes the caller from pulse guide chart broadcasts.</summary>
        public async Task LeavePulseGroupAsync()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, PulseGroup);
        }

        /// <summary>
        /// Returns buffered historical data to the calling client only.
        /// Called by a chart window immediately after joining to pre-populate the chart.
        /// </summary>
        public async Task RequestHistoricalDataAsync(string chartType)
        {
            if (chartType == "radec")
            {
                var (axis1, axis2) = _chartData.GetRaDecHistory();
                await Clients.Caller.SendAsync("ReceiveRaDecHistory", axis1, axis2);
            }
            else if (chartType == "pulse")
            {
                var (ra, dec) = _chartData.GetPulseHistory();
                await Clients.Caller.SendAsync("ReceivePulseHistory", ra, dec);
            }
        }
    }
}
