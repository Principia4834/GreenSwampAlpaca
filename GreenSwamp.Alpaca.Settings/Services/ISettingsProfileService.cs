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

using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Service for managing settings profiles with CRUD operations
    /// Profiles are stored in %AppData%/GreenSwampAlpaca/{version}/profiles/
    /// </summary>
    public interface ISettingsProfileService
    {
        /// <summary>
        /// Create a new settings profile
        /// </summary>
        /// <param name="name">Unique profile name (used as filename)</param>
        /// <param name="mode">Alignment mode for the profile</param>
        /// <param name="copyFromProfile">Optional: Name of existing profile to clone</param>
        /// <returns>The created profile</returns>
        Task<SettingsProfile> CreateProfileAsync(string name, AlignmentMode mode, string? copyFromProfile = null);
        
        /// <summary>
        /// Get a specific profile by name
        /// </summary>
        /// <param name="name">Profile name</param>
        /// <returns>The profile</returns>
        Task<SettingsProfile> GetProfileAsync(string name);
        
        /// <summary>
        /// Get all available profiles
        /// </summary>
        /// <returns>Collection of all profiles</returns>
        Task<IEnumerable<SettingsProfile>> GetAllProfilesAsync();
        
        /// <summary>
        /// Update an existing profile
        /// </summary>
        /// <param name="profile">Profile to update</param>
        Task UpdateProfileAsync(SettingsProfile profile);
        
        /// <summary>
        /// Delete a profile
        /// Cannot delete default profiles or the active profile
        /// </summary>
        /// <param name="name">Profile name</param>
        Task DeleteProfileAsync(string name);
        
        /// <summary>
        /// Get the currently active profile
        /// </summary>
        /// <returns>The active profile</returns>
        Task<SettingsProfile> GetActiveProfileAsync();
        
        /// <summary>
        /// Set a profile as the active profile
        /// </summary>
        /// <param name="name">Profile name</param>
        Task SetActiveProfileAsync(string name);
        
        /// <summary>
        /// Get the default profile for an alignment mode
        /// </summary>
        /// <param name="mode">Alignment mode</param>
        /// <returns>Default profile for the mode</returns>
        Task<SettingsProfile> GetDefaultProfileAsync(AlignmentMode mode);
        
        /// <summary>
        /// Validate a profile
        /// </summary>
        /// <param name="profile">Profile to validate</param>
        /// <returns>Validation result</returns>
        Task<ValidationResult> ValidateProfileAsync(SettingsProfile profile);
        
        /// <summary>
        /// Export a profile to a file
        /// </summary>
        /// <param name="name">Profile name</param>
        /// <param name="destinationPath">Destination file path</param>
        /// <returns>Destination path</returns>
        Task<string> ExportProfileAsync(string name, string destinationPath);
        
        /// <summary>
        /// Import a profile from a file
        /// </summary>
        /// <param name="sourcePath">Source file path</param>
        /// <returns>The imported profile</returns>
        Task<SettingsProfile> ImportProfileAsync(string sourcePath);
    }
}
