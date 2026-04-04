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

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// ASCOM Alpaca device discovery metadata.
    /// Stored in appsettings.alpaca.user.json — one entry per registered device.
    /// Does NOT contain operational settings (those are in device-nn.settings.json).
    /// </summary>
    public class AlpacaDevice
    {
        /// <summary>ASCOM device number (0–99). Must match the nn in device-nn.settings.json.</summary>
        public int DeviceNumber { get; set; }

        /// <summary>Human-readable device name displayed in ASCOM discovery.</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>ASCOM device type. Always "Telescope" in current scope.</summary>
        public string DeviceType { get; set; } = "Telescope";

        /// <summary>Unique identifier for ASCOM device discovery (GUID string).</summary>
        public string UniqueId { get; set; } = Guid.NewGuid().ToString();
    }
}
