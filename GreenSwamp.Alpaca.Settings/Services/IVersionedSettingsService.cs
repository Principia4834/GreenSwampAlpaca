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
    /// Interface for versioned settings service (redesigned for per-device file storage).
    /// Each device's settings are stored in a dedicated device-nn.settings.json file.
    /// Alpaca discovery metadata is stored in appsettings.alpaca.user.json.
    /// Server-wide settings (monitor) are stored in appsettings.user.json.
    /// </summary>
    public interface IVersionedSettingsService
    {
        // ── Metadata ──────────────────────────────────────────────────────────

        /// <summary>Gets the current application version.</summary>
        string CurrentVersion { get; }

        /// <summary>Gets the path to appsettings.user.json for the current version.</summary>
        string UserSettingsPath { get; }

        /// <summary>Gets the path to appsettings.alpaca.user.json for the current version.</summary>
        string AlpacaSettingsPath { get; }

        /// <summary>Gets the path to device-nn.settings.json for the specified device number (0-99).</summary>
        string GetDeviceSettingsPath(int deviceNumber);

        // ── Server-wide settings (appsettings.user.json) ─────────────────────

        /// <summary>Gets the current monitor settings.</summary>
        MonitorSettings GetMonitorSettings();

        /// <summary>Saves monitor settings to appsettings.user.json.</summary>
        Task SaveMonitorSettingsAsync(MonitorSettings settings);

        // ── Alpaca discovery (appsettings.alpaca.user.json) ──────────────────

        /// <summary>Gets Alpaca device discovery metadata for all configured devices.</summary>
        List<AlpacaDevice> GetAlpacaDevices();

        /// <summary>Replaces the full AlpacaDevices list in appsettings.alpaca.user.json.</summary>
        Task SaveAlpacaDevicesAsync(List<AlpacaDevice> devices);

        /// <summary>Adds a new AlpacaDevice entry. Validates device number uniqueness and 100-device limit.</summary>
        Task AddAlpacaDeviceAsync(AlpacaDevice device);

        /// <summary>Removes the AlpacaDevice entry for the specified device number.</summary>
        Task RemoveAlpacaDeviceAsync(int deviceNumber);

        // ── Per-device settings (device-nn.settings.json) ────────────────────

        /// <summary>Gets device settings for the specified device number, or null if not found.</summary>
        SkySettings? GetDeviceSettings(int deviceNumber);

        /// <summary>Gets all device settings by enumerating device-nn.settings.json files.
        /// Runs first-run initialisation from factory defaults if no files exist.</summary>
        List<SkySettings> GetAllDeviceSettings();

        /// <summary>Saves device settings atomically (temp file → rename) under a per-device lock.</summary>
        Task SaveDeviceSettingsAsync(int deviceNumber, SkySettings settings);

        /// <summary>Deletes the device-nn.settings.json file for the specified device number.</summary>
        Task DeleteDeviceSettingsAsync(int deviceNumber);

        /// <summary>Returns true if device-nn.settings.json exists for the specified device number.</summary>
        bool DeviceSettingsExist(int deviceNumber);

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>Validates device-nn.settings.json for the specified device number.</summary>
        ValidationResult ValidateDeviceSettings(int deviceNumber);

        /// <summary>Validates appsettings.alpaca.user.json.</summary>
        ValidationResult ValidateAlpacaDevices();

        // ── Observatory settings (observatory.settings.json) ─────────────────

        /// <summary>Gets the observatory physical settings. Creates observatory.settings.json from
        /// app defaults on first run if absent (Behaviour B4).</summary>
        ObservatorySettings GetObservatorySettings();

        /// <summary>Saves observatory settings to observatory.settings.json (Behaviour B4).
        /// Does not propagate changes to existing device-nn.settings.json files (v1).</summary>
        Task SaveObservatorySettingsAsync(ObservatorySettings settings);

        // ── Mode-aware device creation and change ────────────────────────────

        /// <summary>Creates a new device-nn.settings.json populated with app defaults for the specified
        /// alignment mode and observatory properties from observatory.settings.json (Behaviour B1).</summary>
        Task CreateDeviceForModeAsync(int deviceNumber, string deviceName, AlignmentMode mode);

        /// <summary>Changes the alignment mode on an existing device (Behaviour B2).
        /// Properties marked [UniqueSetting] are replaced with the new mode's defaults;
        /// all [CommonSetting] properties are preserved. Operation is atomic.</summary>
        Task ChangeAlignmentModeAsync(int deviceNumber, AlignmentMode newMode);

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Event raised when device settings are changed via SaveDeviceSettingsAsync.</summary>
        event EventHandler<SkySettings>? DeviceSettingsChanged;

        /// <summary>Event raised when monitor settings are changed.</summary>
        event EventHandler<MonitorSettings>? MonitorSettingsChanged;
    }
}
