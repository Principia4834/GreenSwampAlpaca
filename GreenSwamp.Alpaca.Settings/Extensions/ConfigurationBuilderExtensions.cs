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

            // Add the versioned user settings file
            builder.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);

            return builder;
        }
    }
}
