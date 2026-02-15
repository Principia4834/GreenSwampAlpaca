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
    /// Interface for versioned settings service
    /// </summary>
    public interface IVersionedSettingsService
    {
        /// <summary>
        /// Gets the current settings
        /// </summary>
        SkySettings GetSettings();
        
        /// <summary>
        /// Saves settings to the current version folder
        /// </summary>
        Task SaveSettingsAsync(SkySettings settings);
        
        /// <summary>
        /// Gets the current monitor settings
        /// </summary>
        MonitorSettings GetMonitorSettings();
        
        /// <summary>
        /// Saves monitor settings to the current version folder
        /// </summary>
        Task SaveMonitorSettingsAsync(MonitorSettings settings);
        
        /// <summary>
        /// Migrates settings from the most recent previous version
        /// </summary>
        Task<bool> MigrateFromPreviousVersionAsync();

        /// <summary>
        /// Resets settings to defaults from appsettings.json
        /// </summary>
        Task ResetToDefaultsAsync();

        /// <summary>
        /// Gets the current application version
        /// </summary>
        string CurrentVersion { get; }
        
        /// <summary>
        /// Gets all available version folders
        /// </summary>
        string[] AvailableVersions { get; }
        
        /// <summary>
        /// Gets the path to the user settings file for the current version
        /// </summary>
        string UserSettingsPath { get; }
        
        /// <summary>
        /// Event raised when settings are changed
        /// </summary>
        event EventHandler<SkySettings>? SettingsChanged;
        
        /// <summary>
        /// Event raised when monitor settings are changed
        /// </summary>
        event EventHandler<MonitorSettings>? MonitorSettingsChanged;
    }
}
