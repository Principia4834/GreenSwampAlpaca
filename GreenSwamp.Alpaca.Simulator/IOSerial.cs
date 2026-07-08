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

using System.Reflection;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.Mount.Simulator
{
    internal class IoSerial
    {
        private readonly Controllers _controllers;

        internal static bool IsConnected => true;

        internal IoSerial()
        {
            _controllers = new Controllers();
        }

        internal string Send(string command)
        {
            //if (Queues.Serial.Connected) return null; 
            var received = _controllers.Command(command.ToLowerInvariant().Trim());

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{command}={received}" };
            MonitorLog.LogToMonitor(monitorItem);

            return received;
        }

    }
}
