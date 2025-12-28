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

using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GreenSwamp.Alpaca.Settings.Extensions
{
    /// <summary>
    /// Extension methods for registering settings services
    /// </summary>
    public static class SettingsServiceCollectionExtensions
    {
        /// <summary>
        /// Adds versioned settings services to the dependency injection container
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configuration">The configuration root</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddVersionedSettings(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Register the versioned settings service as singleton
            services.AddSingleton<IVersionedSettingsService>(sp =>
            {
                var loggerFactory = sp.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger(typeof(VersionedSettingsService).FullName ?? "VersionedSettingsService");
                return new VersionedSettingsService(configuration, logger);
            });

            return services;
        }
    }
}
