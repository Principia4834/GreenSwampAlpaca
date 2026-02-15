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
    /// Response model for device information in device list (Phase 4.8.1).
    /// </summary>
    public class DeviceInfoResponse
    {
        /// <summary>
        /// Device number
        /// </summary>
        public int DeviceNumber { get; set; }

        /// <summary>
        /// Display name of the device
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Connection status
        /// </summary>
        public bool Connected { get; set; }

        /// <summary>
        /// Alignment mode (AltAz, Polar, GermanPolar)
        /// </summary>
        public string AlignmentMode { get; set; } = string.Empty;

        /// <summary>
        /// Mount type (Simulator, etc.)
        /// </summary>
        public string MountType { get; set; } = string.Empty;

        /// <summary>
        /// Serial port for physical mounts (null for simulators)
        /// </summary>
        public string? ComPort { get; set; }

        /// <summary>
        /// Baud rate for physical mounts (null for simulators)
        /// </summary>
        public int? BaudRate { get; set; }

        /// <summary>
        /// Serial protocol for physical mounts (null for simulators)
        /// </summary>
        public string? SerialProtocol { get; set; }
    }
}
