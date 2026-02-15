/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

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

namespace GreenSwamp.Alpaca.Server.Models
{
    /// <summary>
    /// Response model for successful device addition (Phase 4.8.1).
    /// </summary>
    public class AddDeviceResponse
    {
        /// <summary>
        /// Device number assigned to the new device
        /// </summary>
        public int DeviceNumber { get; set; }

        /// <summary>
        /// Display name of the device
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Unique identifier assigned to the device
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Success message
        /// </summary>
        public string Message { get; set; } = "Device added successfully";
    }
}
