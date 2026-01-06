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
    /// Service for managing JSON template files for settings profiles
    /// Templates define common settings and alignment mode-specific overrides
    /// </summary>
    public interface ISettingsTemplateService
    {
        /// <summary>
        /// Load complete settings for an alignment mode
        /// Merges common settings with mode-specific overrides
        /// </summary>
        /// <param name="mode">The alignment mode</param>
        /// <returns>Complete settings instance for the mode</returns>
        Task<SkySettings> LoadTemplateAsync(AlignmentMode mode);
        
        /// <summary>
        /// Load common settings applicable to all alignment modes
        /// </summary>
        /// <returns>Common settings instance</returns>
        Task<SkySettings> GetCommonSettingsAsync();
        
        /// <summary>
        /// Load mode-specific override settings
        /// </summary>
        /// <param name="mode">The alignment mode</param>
        /// <returns>Dictionary of property name to override value</returns>
        Task<Dictionary<string, object>> GetModeOverridesAsync(AlignmentMode mode);
        
        /// <summary>
        /// Merge common settings with mode-specific overrides
        /// Overrides take precedence over common settings
        /// </summary>
        /// <param name="common">Common settings</param>
        /// <param name="overrides">Mode-specific overrides</param>
        /// <returns>Merged settings instance</returns>
        Task<SkySettings> MergeSettingsAsync(SkySettings common, Dictionary<string, object> overrides);
    }
}
