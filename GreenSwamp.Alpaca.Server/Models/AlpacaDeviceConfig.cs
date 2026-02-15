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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Server.Models
{
    /// <summary>
    /// Configuration model for device definition in appsettings.json.
    /// Used for static device loading at application startup (Phase 4.8).
    /// </summary>
    public class AlpacaDeviceConfig
    {
        /// <summary>
        /// Device number (must be >= 0 and unique)
        /// </summary>
        [Required]
        [Range(0, int.MaxValue)]
        public int DeviceNumber { get; set; }

        /// <summary>
        /// Display name for the device
        /// </summary>
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Device type (currently only "Telescope" supported)
        /// </summary>
        [Required]
        public string DeviceType { get; set; } = "Telescope";

        /// <summary>
        /// Name of the settings profile to load (without .json extension)
        /// </summary>
        [Required]
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>
        /// Unique identifier for the device (must be globally unique GUID format)
        /// </summary>
        [Required]
        public string UniqueId { get; set; } = string.Empty;
    }
}
