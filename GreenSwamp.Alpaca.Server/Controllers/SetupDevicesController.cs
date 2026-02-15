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

using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace GreenSwamp.Alpaca.Server.Controllers
{
    /// <summary>
    /// Controller for dynamic device management (Phase 4.8.1).
    /// Provides REST API for adding, removing, and listing telescope devices at runtime.
    /// </summary>
    [ServiceFilter(typeof(AuthorizationFilter))]
    [ApiExplorerSettings(GroupName = "AlpacaSetup")]
    [ApiController]
    [Route("setup")]
    [Produces(MediaTypeNames.Application.Json)]
    public class SetupDevicesController : ControllerBase
    {
        private readonly IVersionedSettingsService _settingsService;
        private readonly ILogger<SetupDevicesController> _logger;

        public SetupDevicesController(
            IVersionedSettingsService settingsService,
            ILogger<SetupDevicesController> logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Lists all configured telescope devices with detailed information.
        /// </summary>
        /// <returns>List of all registered devices</returns>
        /// <response code="200">Successfully retrieved device list</response>
        [HttpGet("devices")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<DeviceInfoResponse>), StatusCodes.Status200OK)]
        public IActionResult GetDevices()
        {
            var instances = MountInstanceRegistry.GetAllInstances();
            var devices = new List<DeviceInfoResponse>();

            foreach (var kvp in instances)
            {
                var deviceNumber = kvp.Key;
                var instance = kvp.Value;

                // Get device name and settings from instance
                var settings = instance.Settings;

                devices.Add(new DeviceInfoResponse
                {
                    DeviceNumber = deviceNumber,
                    DeviceName = instance.DeviceName,
                    Connected = instance.IsConnected,
                    IsReserved = Services.UnifiedDeviceRegistry.IsReservedSlot(deviceNumber), // Phase 4.11
                    AlignmentMode = settings.AlignmentMode.ToString(),
                    MountType = settings.Mount.ToString(),
                    ComPort = null, // TODO: Add to SkySettingsInstance in physical mount phase
                    BaudRate = null, // TODO: Add to SkySettingsInstance in physical mount phase
                    SerialProtocol = null // TODO: Add to SkySettingsInstance in physical mount phase
                });
            }

            _logger.LogInformation("Listed {Count} devices", devices.Count);
            return Ok(devices);
        }

        /// <summary>
        /// Adds a new telescope device to the system at runtime.
        /// </summary>
        /// <param name="request">Device configuration including device number, name, and profile</param>
        /// <returns>Device registration confirmation with assigned unique ID</returns>
        /// <response code="200">Device successfully added and registered</response>
        /// <response code="400">Invalid request or device number conflict</response>
        /// <response code="404">Specified profile not found</response>
        [HttpPost("devices")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AddDeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult AddDevice([FromBody] AddDeviceRequest request)
        {
            // Validate model state
            if (!ModelState.IsValid)
            {
                var errors = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(new ErrorResponse { Error = $"Validation failed: {errors}" });
            }

            // Auto-assign device number if requested (0 = auto)
            var deviceNumber = request.DeviceNumber;
            if (deviceNumber == 0)
            {
                deviceNumber = Services.UnifiedDeviceRegistry.GetNextAvailableDeviceNumber();
                _logger.LogInformation("Auto-assigned device number {DeviceNumber}", deviceNumber);
            }

            // Phase 4.11: Prevent manual addition to reserved slots
            if (Services.UnifiedDeviceRegistry.IsReservedSlot(deviceNumber))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = $"Cannot add device to reserved slot {deviceNumber}. Slots 0 and 1 are reserved. Use device number 2 or higher, or 0 for auto-assign."
                });
            }

            // Check if device number is already in use (checks BOTH registries)
            if (!Services.UnifiedDeviceRegistry.IsDeviceNumberAvailable(deviceNumber))
            {
                return BadRequest(new ErrorResponse { Error = $"Device number {deviceNumber} already exists" });
            }

            // Generate unique ID if not provided (ASCOM standard: pure GUID)
            var uniqueId = request.UniqueId;
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                uniqueId = Guid.NewGuid().ToString();
                _logger.LogInformation("Generated unique ID: {UniqueId}", uniqueId);
            }

            try
            {
                // Create settings instance using the injected settings service
                // TODO Phase 4.8.1: Load actual profile settings when profile loader service supports per-device profiles
                var settingsInstance = new SkySettingsInstance(_settingsService);

                // Register with BOTH registries atomically using UnifiedDeviceRegistry
                Services.UnifiedDeviceRegistry.RegisterDevice(
                    deviceNumber,
                    request.DeviceName,
                    uniqueId,
                    settingsInstance,
                    new GreenSwamp.Alpaca.Server.TelescopeDriver.Telescope(deviceNumber)
                );

                _logger.LogInformation(
                    "Successfully added device {DeviceNumber}: {DeviceName} (profile: {ProfileName})",
                    deviceNumber,
                    request.DeviceName,
                    request.ProfileName
                );

                return Ok(new AddDeviceResponse
                {
                    DeviceNumber = deviceNumber,
                    DeviceName = request.DeviceName,
                    UniqueId = uniqueId,
                    Message = "Device added successfully"
                });
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Profile '{ProfileName}' not found", request.ProfileName);
                return NotFound(new ErrorResponse { Error = $"Profile '{request.ProfileName}' not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add device {DeviceNumber}: {DeviceName}", deviceNumber, request.DeviceName);
                return BadRequest(new ErrorResponse { Error = $"Failed to add device: {ex.Message}" });
            }
        }

        /// <summary>
        /// Removes a telescope device from the system at runtime.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>Removal confirmation</returns>
        /// <response code="200">Device successfully removed</response>
        /// <response code="404">Device not found</response>
        [HttpDelete("devices/{deviceNumber}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult RemoveDevice(int deviceNumber)
        {
            // Check if device exists
            var instance = MountInstanceRegistry.GetInstance(deviceNumber);
            if (instance == null)
            {
                return NotFound(new ErrorResponse { Error = $"Device {deviceNumber} not found" });
            }

            try
            {
                // Phase 4.11: Use UnifiedDeviceRegistry to enforce reserved slot protection
                bool removed = Services.UnifiedDeviceRegistry.RemoveDevice(deviceNumber);

                if (!removed)
                {
                    return NotFound(new ErrorResponse { Error = $"Device {deviceNumber} not found" });
                }

                // Note: DeviceManager doesn't have RemoveTelescope method
                // Device will remain in DeviceManager.Telescopes until server restart
                // This is acceptable because:
                // - MountInstanceRegistry controls actual device behavior
                // - Removed devices become non-functional (no MountInstance)
                // - Reserved slots (0, 1) cannot be removed

                _logger.LogInformation("Successfully removed device {DeviceNumber} from registry", deviceNumber);

                return Ok(new { message = $"Device {deviceNumber} removed successfully" });
            }
            catch (InvalidOperationException ex)
            {
                // Phase 4.11: Catch reserved slot protection errors
                _logger.LogWarning(ex, "Attempted to remove reserved device {DeviceNumber}", deviceNumber);
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove device {DeviceNumber}", deviceNumber);
                return BadRequest(new ErrorResponse { Error = $"Failed to remove device: {ex.Message}" });
            }
        }

        /// <summary>
        /// Lists available settings profiles that can be used for device creation.
        /// </summary>
        /// <returns>List of available profile names</returns>
        /// <response code="200">Successfully retrieved profile list</response>
        [HttpGet("profiles")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<object>), StatusCodes.Status200OK)]
        public IActionResult GetProfiles()
        {
            // TODO Phase 4.8.1: Implement profile discovery when settings service supports it
            // For now, return hardcoded list of known simulator profiles
            var profiles = new List<object>
            {
                new { profileName = "simulator-altaz", path = "profiles/simulator-altaz.json" },
                new { profileName = "simulator-gem", path = "profiles/simulator-gem.json" },
                new { profileName = "simulator-polar", path = "profiles/simulator-polar.json" }
            };

            _logger.LogInformation("Listed {Count} profiles", profiles.Count);
            return Ok(profiles);
        }
    }
}
