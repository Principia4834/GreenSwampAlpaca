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
    /// Request model for adding a new telescope device dynamically (Phase 4.8.1).
    /// </summary>
    public class AddDeviceRequest
    {
        /// <summary>
        /// Device number (0-2147483647). Use 0 for auto-assignment.
        /// </summary>
        /// <example>2</example>
        [Range(0, int.MaxValue, ErrorMessage = "Device number must be >= 0")]
        public int DeviceNumber { get; set; }

        /// <summary>
        /// Display name for the device
        /// </summary>
        /// <example>Simulator (Polar)</example>
        [Required(ErrorMessage = "Device name is required")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Device name must be 1-100 characters")]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Name of the settings profile to load (without .json extension)
        /// </summary>
        /// <example>simulator-polar</example>
        [Required(ErrorMessage = "Profile name is required")]
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>
        /// Optional unique identifier. Generated automatically if not provided.
        /// </summary>
        /// <example>sim-polar-12345678-1234-1234-1234-123456789abc</example>
        public string? UniqueId { get; set; }
    }
}
