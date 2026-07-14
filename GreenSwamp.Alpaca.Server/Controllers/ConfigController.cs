/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

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
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Server.Controllers
{
    /// <summary>
    /// Manages all GreenSwamp Alpaca configuration files via a REST API.
    /// <para>
    /// Provides CRUD operations, JSON file upload, and JSON file download for the five
    /// configuration resources persisted under the versioned AppData folder:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Resource</term><description>File</description></listheader>
    ///   <item><term>monitor</term><description>monitor.settings.user.json — logging and monitoring filter flags</description></item>
    ///   <item><term>server</term><description>appsettings.server.user.json — Alpaca network and authentication settings</description></item>
    ///   <item><term>observatory</term><description>observatory.settings.json — physical site location (lat/lon/elevation)</description></item>
    ///   <item><term>alpaca-devices</term><description>devices.alpaca.user.json — ASCOM discovery metadata for each device</description></item>
    ///   <item><term>devices/{n}</term><description>device-nn.settings.json — full operational settings for device n</description></item>
    /// </list>
    /// <para>
    /// All write operations are atomic (temp-file rename) and take effect immediately without
    /// an application restart. Upload endpoints apply a 7-check validation pipeline before
    /// accepting any file (presence, size ≤ 1 MB, JSON content-type, non-empty content,
    /// well-formed JSON, root is an object, object has at least one property).
    /// </para>
    /// </summary>
    [ServiceFilter(typeof(AuthorizationFilter))]
    [ApiExplorerSettings(GroupName = "Config")]
    [ApiController]
    [Route("api/config")]
    [Produces(MediaTypeNames.Application.Json)]
    public class ConfigController : ControllerBase
    {
        private const long MaxUploadBytes = 1 * 1024 * 1024; // 1 MB

        private readonly IVersionedSettingsService _settingsService;
        private readonly ILogger<ConfigController> _logger;

        public ConfigController(
            IVersionedSettingsService settingsService,
            ILogger<ConfigController> logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // -- Monitor settings --------------------------------------------------

        /// <summary>
        /// Returns the current monitor settings from <c>monitor.settings.user.json</c>.
        /// </summary>
        /// <remarks>
        /// Monitor settings control which device types and event categories appear in the
        /// real-time monitoring view.  Changes made via PUT take effect in the UI immediately
        /// without restarting the server.
        /// </remarks>
        /// <returns>The full <see cref="MonitorSettings"/> object as stored on disk.</returns>
        /// <response code="200">Monitor settings returned successfully.</response>
        [HttpGet("monitor")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MonitorSettings), StatusCodes.Status200OK)]
        public IActionResult GetMonitorSettings()
        {
            var settings = _settingsService.GetMonitorSettings();
            return Ok(settings);
        }

        /// <summary>
        /// Replaces the current monitor settings and persists them to <c>monitor.settings.user.json</c>.
        /// </summary>
        /// <remarks>
        /// Supply the complete <see cref="MonitorSettings"/> object in the request body.  Partial
        /// updates are not supported — omitted boolean properties will default to <c>false</c>.
        /// The file is written atomically and the change takes effect immediately; no server
        /// restart is required.
        /// </remarks>
        /// <param name="settings">
        /// Complete replacement monitor settings.  All boolean filter flags should be
        /// explicitly set; any omitted properties revert to their type defaults.
        /// </param>
        /// <returns>The saved <see cref="MonitorSettings"/> as read back from disk.</returns>
        /// <response code="200">Settings saved and returned successfully.</response>
        /// <response code="400">Request body was null or could not be bound.</response>
        [HttpPut("monitor")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MonitorSettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PutMonitorSettingsAsync([FromBody] MonitorSettings settings)
        {
            if (settings is null)
                return BadRequest(new ErrorResponse { Error = "Request body is required." });

            await _settingsService.SaveMonitorSettingsAsync(settings);
            _logger.LogInformation("Monitor settings updated via Config API");
            return Ok(_settingsService.GetMonitorSettings());
        }

        /// <summary>
        /// Downloads the live <c>monitor.settings.user.json</c> file as a JSON attachment.
        /// </summary>
        /// <remarks>
        /// Returns the file that is currently persisted in the versioned AppData folder.
        /// The downloaded file can be edited offline and re-uploaded via <c>POST /api/config/monitor/upload</c>.
        /// A 404 is returned only if the file has never been written (e.g. fresh install before
        /// first save); in normal operation the file always exists.
        /// </remarks>
        /// <returns>The raw JSON file as a <c>application/json</c> attachment named <c>monitor.settings.user.json</c>.</returns>
        /// <response code="200">File returned as an attachment.</response>
        /// <response code="404">The settings file does not exist on disk yet.</response>
        [HttpGet("monitor/download")]
        [AllowAnonymous]
        [Produces("application/octet-stream", MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult DownloadMonitorSettings()
        {
            var path = _settingsService.MonitorSettingsPath;
            if (!System.IO.File.Exists(path))
                return NotFound(new ErrorResponse { Error = $"Settings file not found: {path}" });

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, MediaTypeNames.Application.Json, "monitor.settings.user.json");
        }

        /// <summary>
        /// Uploads a replacement <c>monitor.settings.user.json</c> file, validates it, and applies it.
        /// </summary>
        /// <remarks>
        /// The uploaded file is passed through a 7-check validation pipeline before being accepted:
        /// <list type="number">
        ///   <item>File must be present and non-empty.</item>
        ///   <item>File size must not exceed 1 MB.</item>
        ///   <item>Content-Type or file extension must indicate JSON.</item>
        ///   <item>File content must be non-whitespace.</item>
        ///   <item>Content must parse as valid JSON.</item>
        ///   <item>JSON root element must be an object (not an array or scalar).</item>
        ///   <item>The root object must contain at least one property.</item>
        /// </list>
        /// On success the settings are persisted atomically and take effect immediately.
        /// No server restart is required.
        /// </remarks>
        /// <param name="file">Multipart form file containing the replacement <c>monitor.settings.user.json</c> content.</param>
        /// <returns>The saved <see cref="MonitorSettings"/> as read back from disk after the upload.</returns>
        /// <response code="200">File passed all validation checks and settings have been applied.</response>
        /// <response code="400">File failed a validation check (empty, malformed JSON, wrong structure, etc.).</response>
        /// <response code="413">File size exceeds the 1 MB upload limit.</response>
        /// <response code="415">File is not a JSON file (wrong Content-Type and wrong extension).</response>
        [HttpPost("monitor/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(MonitorSettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413RequestEntityTooLarge)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
        public async Task<IActionResult> UploadMonitorSettingsAsync(IFormFile file)
        {
            var (validationError, statusCode, json) = await ValidateUploadAsync(file);
            if (validationError is not null)
                return StatusCode(statusCode, new ErrorResponse { Error = validationError });

            MonitorSettings? uploaded;
            try
            {
                uploaded = JsonSerializer.Deserialize<MonitorSettings>(json!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return BadRequest(new ErrorResponse { Error = $"JSON deserialisation failed: {ex.Message}" });
            }

            if (uploaded is null)
                return BadRequest(new ErrorResponse { Error = "Deserialized settings object was null." });

            await _settingsService.SaveMonitorSettingsAsync(uploaded);
            _logger.LogInformation("Monitor settings replaced via file upload");
            return Ok(_settingsService.GetMonitorSettings());
        }

        // -- Shared upload validation pipeline (7 checks) ---------------------

        /// <summary>
        /// Runs the 7-check upload validation pipeline.
        /// Returns (errorMessage, httpStatusCode, jsonContent).
        /// errorMessage is null when all checks pass; jsonContent is null when they fail.
        /// </summary>
        private static async Task<(string? Error, int StatusCode, string? Json)> ValidateUploadAsync(IFormFile? file)
        {
            // Check 1 – file present
            if (file is null || file.Length == 0)
                return ("No file was uploaded or the file is empty.", StatusCodes.Status400BadRequest, null);

            // Check 2 – size guard (≤ 1 MB)
            if (file.Length > MaxUploadBytes)
                return ($"File size {file.Length:N0} bytes exceeds the 1 MB limit.", StatusCodes.Status413RequestEntityTooLarge, null);

            // Check 3 – content-type must be JSON
            var ct = file.ContentType ?? string.Empty;
            if (!ct.Contains("json", StringComparison.OrdinalIgnoreCase)
                && !file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return ("Only JSON files are accepted.", StatusCodes.Status415UnsupportedMediaType, null);

            // Check 4 – read content
            string json;
            using (var reader = new StreamReader(file.OpenReadStream()))
                json = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(json))
                return ("Uploaded file content is empty.", StatusCodes.Status400BadRequest, null);

            // Check 5 – well-formed JSON
            JsonDocument? doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                return ($"Invalid JSON: {ex.Message}", StatusCodes.Status400BadRequest, null);
            }

            // Check 6 – root must be a JSON object
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ("Uploaded JSON must be a top-level object.", StatusCodes.Status400BadRequest, null);

            // Check 7 – must not be empty object
            if (!doc.RootElement.EnumerateObject().Any())
                return ("Uploaded JSON object has no properties.", StatusCodes.Status400BadRequest, null);

            return (null, StatusCodes.Status200OK, json);
        }

        // -- Server configuration ----------------------------------------------

        /// <summary>
        /// Returns the current Alpaca server configuration from <c>appsettings.server.user.json</c>.
        /// </summary>
        /// <remarks>
        /// Server configuration controls network settings (port, remote access), authentication,
        /// and Alpaca discovery advertisement.  The returned object reflects the values currently
        /// persisted on disk; changes written by another process since startup will be visible here.
        /// </remarks>
        /// <returns>The full <see cref="ServerConfig"/> object as stored on disk.</returns>
        /// <response code="200">Server configuration returned successfully.</response>
        [HttpGet("server")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ServerConfig), StatusCodes.Status200OK)]
        public IActionResult GetServerConfig()
        {
            var config = _settingsService.GetServerConfig();
            return Ok(config);
        }

        /// <summary>
        /// Replaces the current server configuration and persists it to <c>appsettings.server.user.json</c>.
        /// </summary>
        /// <remarks>
        /// Supply the complete <see cref="ServerConfig"/> object.  Changes to network port or
        /// bind address only take full effect after a server restart; all other properties
        /// (e.g. authentication flags, discovery settings) are read dynamically and apply
        /// on the next relevant operation without a restart.
        /// </remarks>
        /// <param name="config">
        /// Complete replacement server configuration.  Key properties include
        /// <c>ServerPort</c> (default 11111), <c>AllowRemoteAccess</c>, and <c>UseAuth</c>.
        /// </param>
        /// <returns>The saved <see cref="ServerConfig"/> as read back from disk.</returns>
        /// <response code="200">Configuration saved and returned successfully.</response>
        /// <response code="400">Request body was null or could not be bound.</response>
        [HttpPut("server")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ServerConfig), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PutServerConfigAsync([FromBody] ServerConfig config)
        {
            if (config is null)
                return BadRequest(new ErrorResponse { Error = "Request body is required." });

            await _settingsService.SaveServerConfigAsync(config);
            _logger.LogInformation("Server configuration updated via Config API");
            return Ok(_settingsService.GetServerConfig());
        }

        /// <summary>
        /// Downloads the live <c>appsettings.server.user.json</c> file as a JSON attachment.
        /// </summary>
        /// <remarks>
        /// Returns the file currently persisted in the versioned AppData folder.
        /// Use this endpoint to take a backup before making changes, or to clone the
        /// server configuration to another instance.  The file can be re-uploaded via
        /// <c>POST /api/config/server/upload</c>.
        /// </remarks>
        /// <returns>The raw JSON file as a <c>application/json</c> attachment named <c>appsettings.server.user.json</c>.</returns>
        /// <response code="200">File returned as an attachment.</response>
        /// <response code="404">The configuration file does not exist on disk yet.</response>
        [HttpGet("server/download")]
        [AllowAnonymous]
        [Produces("application/octet-stream", MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult DownloadServerConfig()
        {
            var path = _settingsService.ServerConfigPath;
            if (!System.IO.File.Exists(path))
                return NotFound(new ErrorResponse { Error = $"Settings file not found: {path}" });

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, MediaTypeNames.Application.Json, "appsettings.server.user.json");
        }

        /// <summary>
        /// Uploads a replacement <c>appsettings.server.user.json</c> file, validates it, and applies it.
        /// </summary>
        /// <remarks>
        /// Applies the same 7-check upload validation pipeline as all other upload endpoints
        /// (presence, size, content-type, non-empty, valid JSON, object root, non-empty object).
        /// Network port and bind-address changes in the uploaded file will only take full effect
        /// after the server is restarted; all other settings apply on the next use.
        /// </remarks>
        /// <param name="file">Multipart form file containing the replacement <c>appsettings.server.user.json</c> content.</param>
        /// <returns>The saved <see cref="ServerConfig"/> as read back from disk after the upload.</returns>
        /// <response code="200">File passed all validation checks and configuration has been applied.</response>
        /// <response code="400">File failed a validation check.</response>
        /// <response code="413">File size exceeds the 1 MB upload limit.</response>
        /// <response code="415">File is not a JSON file.</response>
        [HttpPost("server/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ServerConfig), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413RequestEntityTooLarge)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
        public async Task<IActionResult> UploadServerConfigAsync(IFormFile file)
        {
            var (validationError, statusCode, json) = await ValidateUploadAsync(file);
            if (validationError is not null)
                return StatusCode(statusCode, new ErrorResponse { Error = validationError });

            ServerConfig? uploaded;
            try
            {
                uploaded = JsonSerializer.Deserialize<ServerConfig>(json!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return BadRequest(new ErrorResponse { Error = $"JSON deserialisation failed: {ex.Message}" });
            }

            if (uploaded is null)
                return BadRequest(new ErrorResponse { Error = "Deserialized configuration object was null." });

            await _settingsService.SaveServerConfigAsync(uploaded);
            _logger.LogInformation("Server configuration replaced via file upload");
            return Ok(_settingsService.GetServerConfig());
        }

        // -- Observatory settings ----------------------------------------------

        /// <summary>
        /// Returns the current observatory physical settings from <c>observatory.settings.json</c>.
        /// </summary>
        /// <remarks>
        /// Observatory settings define the geographic location of the telescope site:
        /// latitude (−90 to +90°), longitude (−180 to +180°), elevation (−500 to 9000 m),
        /// and UTC offset.  If the file does not yet exist it is created from application
        /// defaults (51.476852° N, 0.0° E, 10 m, UTC+0) before being returned.
        /// These values are used when creating a new device via the device-management workflow
        /// but are not automatically propagated to existing device files.
        /// </remarks>
        /// <returns>The full <see cref="ObservatorySettings"/> object as stored on disk.</returns>
        /// <response code="200">Observatory settings returned successfully.</response>
        [HttpGet("observatory")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ObservatorySettings), StatusCodes.Status200OK)]
        public IActionResult GetObservatorySettings()
        {
            var settings = _settingsService.GetObservatorySettings();
            return Ok(settings);
        }

        /// <summary>
        /// Replaces the current observatory settings and persists them to <c>observatory.settings.json</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Latitude must be in the range −90 to +90 degrees.
        /// Longitude must be in the range −180 to +180 degrees (negative = West).
        /// Elevation must be in the range −500 to 9000 metres above sea level.
        /// </para>
        /// <para>
        /// <strong>Important (v1 behaviour):</strong> changes saved here are used only when
        /// creating new devices.  Existing <c>device-nn.settings.json</c> files are not
        /// updated automatically; edit them individually via <c>PUT /api/config/devices/{deviceNumber}</c>
        /// if propagation is required.
        /// </para>
        /// </remarks>
        /// <param name="settings">
        /// Replacement observatory settings.  All four properties (Latitude, Longitude,
        /// Elevation, UTCOffset) should be supplied.
        /// </param>
        /// <returns>The saved <see cref="ObservatorySettings"/> as read back from disk.</returns>
        /// <response code="200">Settings saved and returned successfully.</response>
        /// <response code="400">Request body was null or could not be bound.</response>
        [HttpPut("observatory")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ObservatorySettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PutObservatorySettingsAsync([FromBody] ObservatorySettings settings)
        {
            if (settings is null)
                return BadRequest(new ErrorResponse { Error = "Request body is required." });

            await _settingsService.SaveObservatorySettingsAsync(settings);
            _logger.LogInformation("Observatory settings updated via Config API");
            return Ok(_settingsService.GetObservatorySettings());
        }

        /// <summary>
        /// Downloads the live <c>observatory.settings.json</c> file as a JSON attachment.
        /// </summary>
        /// <remarks>
        /// Returns the file currently persisted in the versioned AppData folder.
        /// A 404 is returned only if the file has never been written; calling
        /// <c>GET /api/config/observatory</c> first will create it from defaults if absent.
        /// </remarks>
        /// <returns>The raw JSON file as a <c>application/json</c> attachment named <c>observatory.settings.json</c>.</returns>
        /// <response code="200">File returned as an attachment.</response>
        /// <response code="404">The settings file does not exist on disk yet.</response>
        [HttpGet("observatory/download")]
        [AllowAnonymous]
        [Produces("application/octet-stream", MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult DownloadObservatorySettings()
        {
            var path = _settingsService.ObservatorySettingsPath;
            if (!System.IO.File.Exists(path))
                return NotFound(new ErrorResponse { Error = $"Settings file not found: {path}" });

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, MediaTypeNames.Application.Json, "observatory.settings.json");
        }

        /// <summary>
        /// Uploads a replacement <c>observatory.settings.json</c> file, validates it, and applies it.
        /// </summary>
        /// <remarks>
        /// Applies the 7-check upload validation pipeline.  The uploaded JSON must contain a
        /// top-level object with at least one property; property values outside the legal
        /// numeric ranges are accepted at upload time but may be rejected by the mount
        /// driver at runtime.
        /// Changes do not propagate to existing device files (v1 behaviour).
        /// </remarks>
        /// <param name="file">Multipart form file containing the replacement <c>observatory.settings.json</c> content.</param>
        /// <returns>The saved <see cref="ObservatorySettings"/> as read back from disk after the upload.</returns>
        /// <response code="200">File passed all validation checks and settings have been applied.</response>
        /// <response code="400">File failed a validation check.</response>
        /// <response code="413">File size exceeds the 1 MB upload limit.</response>
        /// <response code="415">File is not a JSON file.</response>
        [HttpPost("observatory/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ObservatorySettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413RequestEntityTooLarge)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
        public async Task<IActionResult> UploadObservatorySettingsAsync(IFormFile file)
        {
            var (validationError, statusCode, json) = await ValidateUploadAsync(file);
            if (validationError is not null)
                return StatusCode(statusCode, new ErrorResponse { Error = validationError });

            ObservatorySettings? uploaded;
            try
            {
                uploaded = JsonSerializer.Deserialize<ObservatorySettings>(json!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return BadRequest(new ErrorResponse { Error = $"JSON deserialisation failed: {ex.Message}" });
            }

            if (uploaded is null)
                return BadRequest(new ErrorResponse { Error = "Deserialized settings object was null." });

            await _settingsService.SaveObservatorySettingsAsync(uploaded);
            _logger.LogInformation("Observatory settings replaced via file upload");
            return Ok(_settingsService.GetObservatorySettings());
        }

        // -- Alpaca devices ----------------------------------------------------

        /// <summary>
        /// Returns all Alpaca discovery device entries from <c>devices.alpaca.user.json</c>.
        /// </summary>
        /// <remarks>
        /// Each entry contains the discovery metadata that ASCOM clients see during UDP
        /// device discovery: device number, human-readable name, device type, and the
        /// unique GUID.  This list does not contain operational settings — those are in
        /// the per-device files returned by <c>GET /api/config/devices</c>.
        /// Returns an empty array if no devices have been registered.
        /// </remarks>
        /// <returns>List of <see cref="AlpacaDevice"/> entries, one per registered device.</returns>
        /// <response code="200">Device list returned successfully (may be empty).</response>
        [HttpGet("alpaca-devices")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<AlpacaDevice>), StatusCodes.Status200OK)]
        public IActionResult GetAlpacaDevices()
        {
            var devices = _settingsService.GetAlpacaDevices();
            return Ok(devices);
        }

        /// <summary>
        /// Returns the Alpaca discovery entry for a single device by its device number.
        /// </summary>
        /// <param name="deviceNumber">Device number in the range 0–99, matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <returns>The <see cref="AlpacaDevice"/> entry for the requested device.</returns>
        /// <response code="200">Device entry returned successfully.</response>
        /// <response code="404">No Alpaca device entry exists for the specified device number.</response>
        [HttpGet("alpaca-devices/{deviceNumber:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AlpacaDevice), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult GetAlpacaDevice(int deviceNumber)
        {
            var device = _settingsService.GetAlpacaDevices()
                .FirstOrDefault(d => d.DeviceNumber == deviceNumber);

            if (device is null)
                return NotFound(new ErrorResponse { Error = $"Alpaca device {deviceNumber} not found." });

            return Ok(device);
        }

        /// <summary>
        /// Adds a new Alpaca discovery device entry to <c>devices.alpaca.user.json</c>.
        /// </summary>
        /// <remarks>
        /// <para>The <c>DeviceNumber</c> must be unique within the existing device list (0–99).</para>
        /// <para>The total number of registered devices cannot exceed 100.</para>
        /// <para>If <c>UniqueId</c> is omitted or empty in the request body a new GUID is generated automatically.</para>
        /// <para>
        /// Note: this endpoint writes only the discovery metadata file.  To make the device
        /// active at runtime use <c>POST /setup/devices</c> instead, which registers the
        /// device in the live server registry.
        /// </para>
        /// </remarks>
        /// <param name="device">
        /// The discovery entry to add.  Required fields: <c>DeviceNumber</c>, <c>DeviceName</c>.
        /// <c>DeviceType</c> defaults to <c>"Telescope"</c> if omitted.
        /// </param>
        /// <returns>The newly created <see cref="AlpacaDevice"/> entry.</returns>
        /// <response code="201">Device entry created; <c>Location</c> header points to the new resource.</response>
        /// <response code="400">Body is null, device number already exists, or the 100-device limit has been reached.</response>
        [HttpPost("alpaca-devices")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AlpacaDevice), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddAlpacaDeviceAsync([FromBody] AlpacaDevice device)
        {
            if (device is null)
                return BadRequest(new ErrorResponse { Error = "Request body is required." });

            try
            {
                await _settingsService.AddAlpacaDeviceAsync(device);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }

            _logger.LogInformation("Alpaca device {DeviceNumber} added via Config API", device.DeviceNumber);
            return CreatedAtAction(nameof(GetAlpacaDevice),
                new { deviceNumber = device.DeviceNumber }, device);
        }

        /// <summary>
        /// Deletes an Alpaca discovery device entry and atomically removes the corresponding
        /// <c>device-nn.settings.json</c> file if it exists (consistency rule Q2).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both <c>devices.alpaca.user.json</c> (discovery metadata) and
        /// <c>device-nn.settings.json</c> (operational settings) are removed in a single
        /// logical operation so the two files remain consistent.
        /// </para>
        /// <para>
        /// <strong>Runtime effect:</strong> this endpoint does not unregister the device from
        /// the live server registry.  If the device is currently active it will remain
        /// operational until the server is restarted.  To remove a device from the running
        /// server use <c>DELETE /setup/devices/{deviceNumber}</c>.
        /// </para>
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) to delete.</param>
        /// <returns>No content.</returns>
        /// <response code="204">Both the discovery entry and settings file (if present) have been deleted.</response>
        /// <response code="404">No Alpaca device entry exists for the specified device number.</response>
        [HttpDelete("alpaca-devices/{deviceNumber:int}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteAlpacaDeviceAsync(int deviceNumber)
        {
            var exists = _settingsService.GetAlpacaDevices()
                .Any(d => d.DeviceNumber == deviceNumber);

            if (!exists)
                return NotFound(new ErrorResponse { Error = $"Alpaca device {deviceNumber} not found." });

            // Remove from devices.alpaca.user.json
            await _settingsService.RemoveAlpacaDeviceAsync(deviceNumber);

            // Enforce Q2 consistency: remove the matching device settings file if present
            if (_settingsService.DeviceSettingsExist(deviceNumber))
                await _settingsService.DeleteDeviceSettingsAsync(deviceNumber);

            _logger.LogInformation(
                "Alpaca device {DeviceNumber} and its settings file deleted via Config API", deviceNumber);

            return NoContent();
        }

        /// <summary>
        /// Downloads the live <c>devices.alpaca.user.json</c> file as a JSON attachment.
        /// </summary>
        /// <remarks>
        /// The file uses the wrapper format <c>{ "AlpacaDevices": [ ... ] }</c>.
        /// This same format is accepted by the upload endpoint.
        /// </remarks>
        /// <returns>The raw JSON file as a <c>application/json</c> attachment named <c>devices.alpaca.user.json</c>.</returns>
        /// <response code="200">File returned as an attachment.</response>
        /// <response code="404">The discovery file does not exist on disk yet (no devices registered).</response>
        [HttpGet("alpaca-devices/download")]
        [AllowAnonymous]
        [Produces("application/octet-stream", MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult DownloadAlpacaDevices()
        {
            var path = _settingsService.AlpacaDevicesSettingsPath;
            if (!System.IO.File.Exists(path))
                return NotFound(new ErrorResponse { Error = $"Settings file not found: {path}" });

            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, MediaTypeNames.Application.Json, "devices.alpaca.user.json");
        }

        /// <summary>
        /// Uploads a replacement <c>devices.alpaca.user.json</c> file, validates it, and applies it.
        /// </summary>
        /// <remarks>
        /// <para>Applies the 7-check upload validation pipeline.</para>
        /// <para>
        /// The file may be in either of two formats:
        /// <list type="bullet">
        ///   <item><strong>Wrapper format (recommended):</strong> <c>{ "AlpacaDevices": [ ... ] }</c></item>
        ///   <item><strong>Bare array:</strong> <c>[ { ... }, { ... } ]</c></item>
        /// </list>
        /// </para>
        /// <para>The device list must not exceed 100 entries.</para>
        /// <para>
        /// Semantic validation (duplicate device numbers, missing required fields) is run
        /// after saving.  Semantic errors do not cause the upload to be rejected — they are
        /// logged as warnings and included in the server log.  Check
        /// <c>GET /api/config/devices/{n}/validate</c> after upload if strict validation is required.
        /// </para>
        /// </remarks>
        /// <param name="file">Multipart form file containing the replacement <c>devices.alpaca.user.json</c> content.</param>
        /// <returns>The full device list as read back from disk after the upload.</returns>
        /// <response code="200">File passed all validation checks and discovery entries have been applied.</response>
        /// <response code="400">File failed a structural validation check or exceeds the 100-device limit.</response>
        /// <response code="413">File size exceeds the 1 MB upload limit.</response>
        /// <response code="415">File is not a JSON file.</response>
        [HttpPost("alpaca-devices/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(List<AlpacaDevice>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413RequestEntityTooLarge)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
        public async Task<IActionResult> UploadAlpacaDevicesAsync(IFormFile file)
        {
            var (validationError, statusCode, json) = await ValidateUploadAsync(file);
            if (validationError is not null)
                return StatusCode(statusCode, new ErrorResponse { Error = validationError });

            // Deserialise the wrapper document: { "AlpacaDevices": [ ... ] }
            List<AlpacaDevice>? uploaded;
            try
            {
                using var doc = JsonDocument.Parse(json!);
                if (doc.RootElement.TryGetProperty("AlpacaDevices", out var arrayElement))
                {
                    uploaded = JsonSerializer.Deserialize<List<AlpacaDevice>>(
                        arrayElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    // Also accept a bare array for convenience
                    uploaded = JsonSerializer.Deserialize<List<AlpacaDevice>>(json!,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (JsonException ex)
            {
                return BadRequest(new ErrorResponse { Error = $"JSON deserialisation failed: {ex.Message}" });
            }

            if (uploaded is null)
                return BadRequest(new ErrorResponse { Error = "Deserialized device list was null." });

            if (uploaded.Count > 100)
                return BadRequest(new ErrorResponse { Error = "Device list exceeds the 100-device limit." });

            await _settingsService.SaveAlpacaDevicesAsync(uploaded);

            // Run semantic validation and surface any errors as warnings in the log
            var result = _settingsService.ValidateAlpacaDevices();
            if (result.HasErrors)
                _logger.LogWarning("Alpaca devices uploaded with {Count} validation error(s)", result.Errors.Count);

            _logger.LogInformation("Alpaca devices replaced via file upload ({Count} devices)", uploaded.Count);
            return Ok(_settingsService.GetAlpacaDevices());
        }

        // -- Per-device settings -----------------------------------------------

        /// <summary>
        /// Returns the full operational settings for every configured device by enumerating
        /// all <c>device-nn.settings.json</c> files in the versioned AppData folder.
        /// </summary>
        /// <remarks>
        /// If no device files exist (first run) the service initialises one device from
        /// factory defaults and returns that single entry.  Each element in the returned
        /// array is the complete <see cref="SkySettings"/> for that device, including
        /// alignment mode, mount type, axis rates, PEC data, and all other operational
        /// parameters.  For just the discovery metadata (name, GUID) use
        /// <c>GET /api/config/alpaca-devices</c> instead.
        /// </remarks>
        /// <returns>List of <see cref="SkySettings"/> objects, one per device file found on disk.</returns>
        /// <response code="200">Device settings list returned successfully (at least one entry).</response>
        [HttpGet("devices")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<SkySettings>), StatusCodes.Status200OK)]
        public IActionResult GetAllDeviceSettings()
        {
            var settings = _settingsService.GetAllDeviceSettings();
            return Ok(settings);
        }

        /// <summary>
        /// Returns the full operational settings for a single device from its
        /// <c>device-nn.settings.json</c> file.
        /// </summary>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <returns>The complete <see cref="SkySettings"/> for the specified device.</returns>
        /// <response code="200">Device settings returned successfully.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        [HttpGet("devices/{deviceNumber:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(SkySettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult GetDeviceSettings(int deviceNumber)
        {
            if (!_settingsService.DeviceSettingsExist(deviceNumber))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            var settings = _settingsService.GetDeviceSettings(deviceNumber);
            return Ok(settings);
        }

        /// <summary>
        /// Replaces the full operational settings for a specific device and persists them
        /// to <c>device-nn.settings.json</c>.
        /// </summary>
        /// <remarks>
        /// <para>The target <c>device-nn.settings.json</c> file must already exist.  To create
        /// a new device use the device-management workflow: add an Alpaca entry via
        /// <c>POST /api/config/alpaca-devices</c> and create the settings file via
        /// <c>POST /setup/devices</c>.</para>
        /// <para>The entire settings object is replaced — partial updates are not supported.
        /// Retrieve the current settings with <c>GET /api/config/devices/{deviceNumber}</c>
        /// first, modify the required fields, then PUT the complete object back.</para>
        /// <para>The write is atomic (temp-file rename) and is protected by a per-device lock.
        /// Changes take effect on the next mount operation without a server restart.</para>
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <param name="settings">Complete replacement <see cref="SkySettings"/> object.</param>
        /// <returns>The saved <see cref="SkySettings"/> as read back from disk.</returns>
        /// <response code="200">Settings saved and returned successfully.</response>
        /// <response code="400">Request body was null or could not be bound.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        [HttpPut("devices/{deviceNumber:int}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(SkySettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PutDeviceSettingsAsync(int deviceNumber, [FromBody] SkySettings settings)
        {
            if (settings is null)
                return BadRequest(new ErrorResponse { Error = "Request body is required." });

            if (!_settingsService.DeviceSettingsExist(deviceNumber))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            await _settingsService.SaveDeviceSettingsAsync(deviceNumber, settings);
            _logger.LogInformation("Device {DeviceNumber} settings updated via Config API", deviceNumber);
            return Ok(_settingsService.GetDeviceSettings(deviceNumber));
        }

        /// <summary>
        /// Deletes the <c>device-nn.settings.json</c> operational settings file for the specified device.
        /// </summary>
        /// <remarks>
        /// <para>Only the device settings file is removed.  The corresponding Alpaca discovery
        /// entry in <c>devices.alpaca.user.json</c> is <strong>not</strong> removed by this
        /// endpoint.  To remove both atomically use
        /// <c>DELETE /api/config/alpaca-devices/{deviceNumber}</c> instead (Q2 consistency rule).</para>
        /// <para>The device will continue to operate in the running server until it is
        /// restarted; the deleted file means it will fail to load on next startup.</para>
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <returns>No content.</returns>
        /// <response code="204">Settings file deleted successfully.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        [HttpDelete("devices/{deviceNumber:int}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteDeviceSettingsAsync(int deviceNumber)
        {
            if (!_settingsService.DeviceSettingsExist(deviceNumber))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            await _settingsService.DeleteDeviceSettingsAsync(deviceNumber);
            _logger.LogInformation("Device {DeviceNumber} settings file deleted via Config API", deviceNumber);
            return NoContent();
        }

        /// <summary>
        /// Downloads the live <c>device-nn.settings.json</c> file for the specified device
        /// as a JSON attachment.
        /// </summary>
        /// <remarks>
        /// The returned file is named <c>device-nn.settings.json</c> where <c>nn</c> is the
        /// zero-padded device number (e.g. <c>device-00.settings.json</c>).  It can be edited
        /// offline and re-uploaded via <c>POST /api/config/devices/{deviceNumber}/upload</c>.
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <returns>The raw JSON file as a <c>application/json</c> attachment.</returns>
        /// <response code="200">File returned as an attachment.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        [HttpGet("devices/{deviceNumber:int}/download")]
        [AllowAnonymous]
        [Produces("application/octet-stream", MediaTypeNames.Application.Json)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult DownloadDeviceSettings(int deviceNumber)
        {
            var path = _settingsService.GetDeviceSettingsPath(deviceNumber);
            if (!System.IO.File.Exists(path))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            var bytes = System.IO.File.ReadAllBytes(path);
            var fileName = $"device-{deviceNumber:D2}.settings.json";
            return File(bytes, MediaTypeNames.Application.Json, fileName);
        }

        /// <summary>
        /// Uploads a replacement <c>device-nn.settings.json</c> file for the specified device,
        /// validates it, and applies it.
        /// </summary>
        /// <remarks>
        /// <para>The target device settings file must already exist (404 is returned if it does not).</para>
        /// <para>Applies the 7-check upload validation pipeline.</para>
        /// <para>
        /// After saving, semantic validation is run against the new file.  Any semantic errors
        /// (e.g. out-of-range axis rates, inconsistent alignment mode properties) are logged as
        /// warnings but do not cause the upload to be rejected.  Use
        /// <c>GET /api/config/devices/{deviceNumber}/validate</c> to retrieve the full
        /// validation result if strict checking is required.
        /// </para>
        /// <para>The write is atomic and protected by a per-device lock.  Changes take effect
        /// on the next mount operation without a server restart.</para>
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <param name="file">Multipart form file containing the replacement <c>device-nn.settings.json</c> content.</param>
        /// <returns>The saved <see cref="SkySettings"/> as read back from disk after the upload.</returns>
        /// <response code="200">File passed all validation checks and settings have been applied.</response>
        /// <response code="400">File failed a structural validation check.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        /// <response code="413">File size exceeds the 1 MB upload limit.</response>
        /// <response code="415">File is not a JSON file.</response>
        [HttpPost("devices/{deviceNumber:int}/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(SkySettings), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413RequestEntityTooLarge)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
        public async Task<IActionResult> UploadDeviceSettingsAsync(int deviceNumber, IFormFile file)
        {
            if (!_settingsService.DeviceSettingsExist(deviceNumber))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            var (validationError, statusCode, json) = await ValidateUploadAsync(file);
            if (validationError is not null)
                return StatusCode(statusCode, new ErrorResponse { Error = validationError });

            SkySettings? uploaded;
            try
            {
                uploaded = JsonSerializer.Deserialize<SkySettings>(json!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return BadRequest(new ErrorResponse { Error = $"JSON deserialisation failed: {ex.Message}" });
            }

            if (uploaded is null)
                return BadRequest(new ErrorResponse { Error = "Deserialized settings object was null." });

            await _settingsService.SaveDeviceSettingsAsync(deviceNumber, uploaded);

            // Run semantic validation and surface any errors as warnings in the log
            var result = _settingsService.ValidateDeviceSettings(deviceNumber);
            if (result.HasErrors)
                _logger.LogWarning("Device {DeviceNumber} uploaded with {Count} validation error(s)",
                    deviceNumber, result.Errors.Count);

            _logger.LogInformation("Device {DeviceNumber} settings replaced via file upload", deviceNumber);
            return Ok(_settingsService.GetDeviceSettings(deviceNumber));
        }

        /// <summary>
        /// Validates <c>device-nn.settings.json</c> for the specified device and returns
        /// a detailed result object.
        /// </summary>
        /// <remarks>
        /// <para>Validation checks include: required property presence, alignment-mode consistency,
        /// axis rate ranges, PEC data integrity, and inter-property constraints.</para>
        /// <para>The response always returns HTTP 200 — inspect the <c>IsValid</c> field to
        /// determine whether validation passed.  The <c>Errors</c> collection lists blocking
        /// issues; <c>Warnings</c> lists advisory issues that do not prevent operation.</para>
        /// <para>This endpoint is read-only and does not modify any files.</para>
        /// </remarks>
        /// <param name="deviceNumber">Device number (0–99) matching the <c>nn</c> in <c>device-nn.settings.json</c>.</param>
        /// <returns>
        /// A <see cref="ValidationResult"/> containing <c>IsValid</c>, a list of <c>Errors</c>
        /// (each with <c>ErrorCode</c>, <c>Severity</c>, <c>Message</c>, and <c>Resolution</c>),
        /// and a list of <c>Warnings</c>.
        /// </returns>
        /// <response code="200">Validation completed; inspect <c>IsValid</c> and <c>Errors</c> for the outcome.</response>
        /// <response code="404">No <c>device-nn.settings.json</c> file exists for the specified device number.</response>
        [HttpGet("devices/{deviceNumber:int}/validate")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ValidationResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public IActionResult ValidateDeviceSettings(int deviceNumber)
        {
            if (!_settingsService.DeviceSettingsExist(deviceNumber))
                return NotFound(new ErrorResponse { Error = $"Settings file not found for device {deviceNumber}." });

            var result = _settingsService.ValidateDeviceSettings(deviceNumber);
            return Ok(result);
        }
    }
}
