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
        /// Gets the current settings (returns first device for single-device code compatibility)
        /// Phase 3 baseline (v1.0.0+): Reads from Devices array
        /// </summary>
        SkySettings GetSettings();

        /// <summary>
        /// Phase 3 baseline (v1.0.0+): Gets all configured device settings from Devices array
        /// </summary>
        List<SkySettings> GetAllDevices();

        /// <summary>
        /// Gets all configured device settings with validation results
        /// Invalid devices are quarantined (not included in returned list)
        /// </summary>
        /// <param name="validationResult">Detailed validation results including errors and warnings</param>
        /// <returns>List of valid devices only</returns>
        List<SkySettings> GetAllDevices(out ValidationResult validationResult);

        /// <summary>
        /// Validates current settings file without loading devices
        /// </summary>
        /// <returns>Validation results with all errors and warnings</returns>
        ValidationResult ValidateSettings();

        /// <summary>
        /// Gets Alpaca device discovery metadata for all configured devices
        /// Returns list of AlpacaDevices with DeviceNumber, DeviceName, DeviceType, and UniqueId
        /// </summary>
        List<GreenSwamp.Alpaca.Settings.Services.AlpacaDevice> GetAlpacaDevices();

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

        // ── Observatory settings (observatory.settings.json) ─────────────────

        /// <summary>
        /// Gets the observatory physical settings (latitude, longitude, elevation, UTC offset).
        /// Creates observatory.settings.json from app defaults on first run if absent (Behaviour B4).
        /// </summary>
        ObservatorySettings GetObservatorySettings();

        /// <summary>
        /// Saves observatory settings to observatory.settings.json (Behaviour B4).
        /// Does not propagate changes to existing device-nn.settings.json files (v1).
        /// </summary>
        Task SaveObservatorySettingsAsync(ObservatorySettings settings);

        // ── Mode-aware device creation and change ────────────────────────────

        /// <summary>
        /// Creates a new device-nn.settings.json populated with app defaults for the specified
        /// alignment mode and observatory properties from observatory.settings.json (Behaviour B1).
        /// </summary>
        Task CreateDeviceForModeAsync(int deviceNumber, string deviceName, AlignmentMode mode);

        /// <summary>
        /// Changes the alignment mode on an existing device (Behaviour B2).
        /// Properties marked [UniqueSetting] are replaced with the new mode's defaults;
        /// all [CommonSetting] properties (including user observatory values) are preserved.
        /// Operation is atomic.
        /// </summary>
        Task ChangeAlignmentModeAsync(int deviceNumber, AlignmentMode newMode);

        /// <summary>
        /// Migrates settings from the most recent previous version
        /// Phase 3 baseline (v1.0.0+): Only supports migration from v1.0.0 and above
        /// </summary>
        Task<bool> MigrateFromPreviousVersionAsync();

        /// <summary>
        /// Resets settings to defaults from appsettings.json
        /// </summary>
        Task ResetToDefaultsAsync();

        /// <summary>
        /// Performs automatic repair of common settings errors
        /// Creates backup before repair, restores on failure
        /// </summary>
        /// <returns>Repair results including actions performed and remaining errors</returns>
        Task<RepairResult> RepairSettingsAsync();

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
        /// Gets the full path to the user settings file
        /// </summary>
        string GetUserSettingsPath();

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
