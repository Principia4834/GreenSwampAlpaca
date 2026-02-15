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

using GreenSwamp.Alpaca.Server.Models;
using System.Net.Http.Json;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Service for managing telescope devices via REST API (Phase 4.11).
    /// Provides Blazor-friendly wrapper around SetupDevicesController endpoints.
    /// </summary>
    public class DeviceManagementService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DeviceManagementService> _logger;
        private const string BaseUrl = "/setup";

        public DeviceManagementService(HttpClient httpClient, ILogger<DeviceManagementService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all configured telescope devices.
        /// </summary>
        public async Task<List<DeviceInfoResponse>?> GetDevicesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/devices");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<DeviceInfoResponse>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve devices");
                throw;
            }
        }

        /// <summary>
        /// Adds a new telescope device to the system.
        /// </summary>
        public async Task<AddDeviceResponse?> AddDeviceAsync(AddDeviceRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/devices", request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AddDeviceResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add device: {DeviceName}", request.DeviceName);
                throw;
            }
        }

        /// <summary>
        /// Removes a telescope device from the system.
        /// </summary>
        public async Task<bool> RemoveDeviceAsync(int deviceNumber)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{BaseUrl}/devices/{deviceNumber}");
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove device: {DeviceNumber}", deviceNumber);
                throw;
            }
        }

        /// <summary>
        /// Retrieves available settings profiles.
        /// </summary>
        public async Task<List<ProfileInfo>?> GetProfilesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/profiles");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<ProfileInfo>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve profiles");
                throw;
            }
        }
    }

    /// <summary>
    /// Profile information for device creation.
    /// </summary>
    public class ProfileInfo
    {
        public string ProfileName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
