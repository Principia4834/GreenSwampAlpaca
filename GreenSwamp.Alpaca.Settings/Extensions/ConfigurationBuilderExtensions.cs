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

using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Extensions
{
    /// <summary>
    /// Extension methods for configuration builder to add versioned user settings
    /// </summary>
    public static class ConfigurationBuilderExtensions
    {
        /// <summary>
        /// Adds versioned user settings JSON file to the configuration
        /// </summary>
        /// <param name="builder">The configuration builder</param>
        /// <param name="appVersion">The application version (optional, will auto-detect if null)</param>
        /// <returns>The configuration builder for chaining</returns>
        public static IConfigurationBuilder AddVersionedUserSettings(
            this IConfigurationBuilder builder,
            string? appVersion = null)
        {
            // Get app version if not provided
            if (string.IsNullOrEmpty(appVersion))
            {
                var assembly = Assembly.GetEntryAssembly() 
                    ?? Assembly.GetExecutingAssembly();
                
                var infoVersionAttr = assembly
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as AssemblyInformationalVersionAttribute;
                
                appVersion = infoVersionAttr?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString()
                    ?? "1.0.0";
                
                // Remove build metadata
                var plusIndex = appVersion.IndexOf('+');
                if (plusIndex > 0)
                {
                    appVersion = appVersion.Substring(0, plusIndex);
                }
            }

            // Build path to versioned settings
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userSettingsPath = Path.Combine(
                appData, 
                "GreenSwampAlpaca", 
                appVersion, 
                "appsettings.user.json");

            // Issue 5.1 Fix: Validate JSON file before loading
            // Empty files (0 bytes) or corrupt JSON will cause AddJsonFile to throw unhandled exceptions
            // This pre-validation ensures graceful handling of invalid files
            if (File.Exists(userSettingsPath))
            {
                try
                {
                    var fileInfo = new FileInfo(userSettingsPath);

                    // Check for empty file (0 bytes)
                    if (fileInfo.Length == 0)
                    {
                        // Write minimal valid JSON to allow configuration system to load
                        File.WriteAllText(userSettingsPath, "{}");
                        ASCOM.Alpaca.Logging.LogWarning($"Empty settings file detected at {userSettingsPath}, wrote minimal JSON");
                    }
                    else
                    {
                        // Verify file contains valid JSON
                        var content = File.ReadAllText(userSettingsPath);

                        if (string.IsNullOrWhiteSpace(content))
                        {
                            // File contains only whitespace
                            File.WriteAllText(userSettingsPath, "{}");
                            ASCOM.Alpaca.Logging.LogWarning($"Whitespace-only settings file at {userSettingsPath}, wrote minimal JSON");
                        }
                        else
                        {
                            try
                            {
                                // Test JSON parsing (JsonDocument is faster than full deserialization)
                                using var doc = JsonDocument.Parse(content);
                                // Valid JSON - proceed normally
                            }
                            catch (JsonException ex)
                            {
                                // Corrupt JSON - backup and create new file
                                var backupPath = $"{userSettingsPath}.corrupted.{DateTime.Now:yyyyMMdd-HHmmss}";
                                File.Move(userSettingsPath, backupPath);
                                File.WriteAllText(userSettingsPath, "{}");

                                ASCOM.Alpaca.Logging.LogError($"Corrupt JSON detected at {userSettingsPath}: {ex.Message}");
                                ASCOM.Alpaca.Logging.LogWarning($"Backed up corrupt file to {backupPath}");
                                ASCOM.Alpaca.Logging.LogWarning("Created new minimal settings file");
                            }
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    // File access error - log but allow AddJsonFile to handle with optional: true
                    ASCOM.Alpaca.Logging.LogError($"Cannot access settings file at {userSettingsPath}: {ioEx.Message}");
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    // Permission error - log but allow AddJsonFile to handle with optional: true
                    ASCOM.Alpaca.Logging.LogError($"Permission denied accessing settings file at {userSettingsPath}: {uaEx.Message}");
                }
            }

            // Add the versioned user settings file
            // Wrap in try-catch as safety net (pre-validation above should prevent most errors)
            try
            {
                builder.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);
            }
            catch (Exception ex)
            {
                // Last-resort error handling - log and continue with defaults
                ASCOM.Alpaca.Logging.LogError($"Failed to load settings file {userSettingsPath}: {ex.Message}");
                ASCOM.Alpaca.Logging.LogWarning("Application will continue with default settings");
            }

            return builder;
        }
    }
}
