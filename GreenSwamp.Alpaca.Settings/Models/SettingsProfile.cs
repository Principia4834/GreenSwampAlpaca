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

using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Settings profile - wrapper for SkySettings with metadata
    /// Represents a complete mount configuration that can be saved/loaded
    /// </summary>
    public class SettingsProfile
    {
        /// <summary>
        /// Unique profile name (file name without extension)
        /// Used as the profile identifier
        /// </summary>
        public string Name { get; set; } = null!;
        
        /// <summary>
        /// Human-readable display name for UI
        /// </summary>
        public string DisplayName { get; set; } = null!;
        
        /// <summary>
        /// Optional description of the profile
        /// </summary>
        public string? Description { get; set; }
        
        /// <summary>
        /// Alignment mode for this profile
        /// Determines home/park positions and mount behavior
        /// </summary>
        public AlignmentMode AlignmentMode { get; set; }
        
        /// <summary>
        /// Actual settings data
        /// </summary>
        public SkySettings Settings { get; set; } = null!;
        
        /// <summary>
        /// Is this a built-in default profile (read-only)
        /// Default profiles cannot be deleted or have their alignment mode changed
        /// </summary>
        public bool IsReadOnly { get; set; }
        
        /// <summary>
        /// Profile creation timestamp (UTC)
        /// </summary>
        public DateTime Created { get; set; }
        
        /// <summary>
        /// Last modification timestamp (UTC)
        /// </summary>
        public DateTime LastModified { get; set; }
        
        /// <summary>
        /// Clone this profile with a new name
        /// Clones are never read-only
        /// </summary>
        public SettingsProfile Clone(string newName)
        {
            return new SettingsProfile
            {
                Name = newName,
                DisplayName = newName,
                Description = this.Description,
                AlignmentMode = this.AlignmentMode,
                Settings = CloneSettings(this.Settings),
                IsReadOnly = false, // Clones are never read-only
                Created = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };
        }
        
        /// <summary>
        /// Deep clone settings using JSON serialization
        /// </summary>
        private static SkySettings CloneSettings(SkySettings source)
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<SkySettings>(json)!;
        }
    }
}
