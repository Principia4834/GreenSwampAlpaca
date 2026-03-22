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

using Microsoft.AspNetCore.Mvc;
using GreenSwamp.Alpaca.Settings.Services;
using GreenSwamp.Alpaca.Settings.Models;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Server.Controllers
{
    /// <summary>
    /// REST API for remote client applications to manage profiles
    /// Phase 4.10: Enables external applications to download/upload profiles with server-side validation
    /// </summary>
    [ApiController]
    [Route("api/profiles")]
    public class ProfileApiController : ControllerBase
    {
        private readonly ISettingsProfileService _profileService;
        private readonly ILogger<ProfileApiController> _logger;
        private const int MaxProfileSizeBytes = 1_048_576; // 1MB limit

        public ProfileApiController(
            ISettingsProfileService profileService,
            ILogger<ProfileApiController> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// GET /api/profiles - List all profiles with metadata
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ProfileListResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListProfiles(
            [FromQuery] DateTime? modifiedSince = null,
            [FromQuery] bool? inUse = null)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var metadata = new List<ProfileMetadata>();

                foreach (var profile in profiles)
                {
                    var meta = new ProfileMetadata
                    {
                        Name = profile.Name,
                        DisplayName = profile.DisplayName,
                        AlignmentMode = profile.AlignmentMode.ToString(),
                        LastModified = profile.LastModified,
                        InUse = await _profileService.IsProfileInUseAsync(profile.Name),
                        Devices = (await _profileService.GetDevicesUsingProfileAsync(profile.Name)).ToList()
                    };

                    // Filter by modifiedSince
                    if (modifiedSince.HasValue && meta.LastModified < modifiedSince.Value)
                        continue;

                    // Filter by inUse
                    if (inUse.HasValue && meta.InUse != inUse.Value)
                        continue;

                    metadata.Add(meta);
                }

                var response = new ProfileListResponse
                {
                    Profiles = metadata,
                    Count = metadata.Count,
                    ServerVersion = GetServerVersion()
                };

                _logger.LogInformation("Listed {Count} profiles for remote client", metadata.Count);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list profiles for remote client");
                return StatusCode(500, new ErrorResponse { Error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/profiles/{name} - Download single profile
        /// </summary>
        [HttpGet("{name}")]
        [ProducesResponseType(typeof(ProfileDownloadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadProfile(string name)
        {
            try
            {
                if (!_profileService.ProfileExists(name))
                    return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });

                var profile = await _profileService.GetProfileDetailsAsync(name);
                
                var response = new ProfileDownloadResponse
                {
                    Name = name,
                    Settings = profile.Settings,
                    Metadata = new ProfileMetadata
                    {
                        Name = name,
                        DisplayName = profile.DisplayName,
                        AlignmentMode = profile.AlignmentMode.ToString(),
                        LastModified = profile.LastModified
                    }
                };

                _logger.LogInformation("Profile '{ProfileName}' downloaded by remote client", name);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download profile {ProfileName}", name);
                return StatusCode(500, new ErrorResponse { Error = ex.Message });
            }
        }

        /// <summary>
        /// HEAD /api/profiles/{name} - Check if profile exists
        /// </summary>
        [HttpHead("{name}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CheckProfileExists(string name)
        {
            return _profileService.ProfileExists(name) ? Ok() : NotFound();
        }

        /// <summary>
        /// GET /api/profiles/{name}/metadata - Get metadata without full content
        /// </summary>
        [HttpGet("{name}/metadata")]
        [ProducesResponseType(typeof(ProfileMetadata), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetProfileMetadata(string name)
        {
            try
            {
                if (!_profileService.ProfileExists(name))
                    return NotFound(new ErrorResponse { Error = $"Profile '{name}' not found" });

                var profile = await _profileService.GetProfileDetailsAsync(name);
                
                var metadata = new ProfileMetadata
                {
                    Name = name,
                    DisplayName = profile.DisplayName,
                    AlignmentMode = profile.AlignmentMode.ToString(),
                    LastModified = profile.LastModified,
                    InUse = await _profileService.IsProfileInUseAsync(name),
                    Devices = (await _profileService.GetDevicesUsingProfileAsync(name)).ToList()
                };

                return Ok(metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get metadata for profile {ProfileName}", name);
                return StatusCode(500, new ErrorResponse { Error = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/profiles/{name} - Upload/update profile with validation
        /// </summary>
        [HttpPut("{name}")]
        [ProducesResponseType(typeof(ProfileUploadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationResult), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadProfile(string name, [FromBody] ProfileUploadRequest request)
        {
            try
            {
                // Security: Sanitize profile name (prevent path traversal)
                if (!IsValidProfileName(name))
                {
                    return BadRequest(new ErrorResponse 
                    { 
                        Error = "Invalid profile name. Only alphanumeric, dash, and underscore characters allowed." 
                    });
                }

                // CRITICAL: Server-side validation (cannot trust client)
                var validation = await _profileService.ValidateProfileAsync(new SettingsProfile 
                { 
                    Name = name,
                    DisplayName = name,
                    AlignmentMode = ParseAlignmentMode(request.Settings.AlignmentMode),
                    Settings = request.Settings,
                    IsReadOnly = false,
                    Created = DateTime.UtcNow,
                    LastModified = DateTime.UtcNow
                });
                
                if (!validation.IsValid)
                {
                    _logger.LogWarning("Profile upload rejected due to validation errors: {ProfileName}", name);
                    return BadRequest(validation);
                }

                // Check if profile exists
                bool profileExists = _profileService.ProfileExists(name);

                if (profileExists && !request.Options.Overwrite)
                {
                    return Conflict(new ErrorResponse 
                    { 
                        Error = $"Profile '{name}' already exists. Set overwrite=true to update." 
                    });
                }

                // Save profile
                await _profileService.SaveProfileSettingsAsync(name, request.Settings, overwrite: request.Options.Overwrite);

                var response = new ProfileUploadResponse
                {
                    Success = true,
                    ProfileName = name,
                    Action = profileExists ? "updated" : "created",
                    Message = $"Profile '{name}' {(profileExists ? "updated" : "created")} successfully"
                };

                _logger.LogInformation(
                    "Profile '{ProfileName}' {Action} by remote client from {RemoteIp}", 
                    name, 
                    response.Action,
                    HttpContext.Connection.RemoteIpAddress);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload profile {ProfileName}", name);
                return StatusCode(500, new ErrorResponse { Error = ex.Message });
            }
        }

        /// <summary>
        /// POST /api/profiles/{name}/validate - Validate profile without saving
        /// </summary>
        [HttpPost("{name}/validate")]
        [ProducesResponseType(typeof(ValidationResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ValidateProfile(string name, [FromBody] SkySettings settings)
        {
            var validation = await _profileService.ValidateProfileAsync(new SettingsProfile 
            { 
                Name = name,
                DisplayName = name,
                AlignmentMode = ParseAlignmentMode(settings.AlignmentMode),
                Settings = settings,
                IsReadOnly = false,
                Created = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            });
            
            _logger.LogInformation(
                "Profile '{ProfileName}' validated by remote client: {IsValid}", 
                name, 
                validation.IsValid);

            return Ok(validation);
        }

        // Helper methods

        private bool IsValidProfileName(string name)
        {
            // Only allow alphanumeric, dash, underscore (prevent path traversal)
            return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
        }

        private AlignmentMode ParseAlignmentMode(string mode)
        {
            if (Enum.TryParse<AlignmentMode>(mode, out var result))
                return result;
            
            _logger.LogWarning("Failed to parse alignment mode '{Mode}', defaulting to AltAz", mode);
            return AlignmentMode.AltAz;
        }

        private string GetServerVersion()
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;
            return version?.ToString() ?? "4.10.0";
        }
    }

    // Request/Response Models

    public class ProfileUploadRequest
    {
        public SkySettings Settings { get; set; } = null!;
        public UploadOptions Options { get; set; } = new();
    }

    public class UploadOptions
    {
        public bool Overwrite { get; set; } = false;
        public bool Validate { get; set; } = true; // Always validates regardless
    }

    public class ProfileUploadResponse
    {
        public bool Success { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // "created" or "updated"
        public string Message { get; set; } = string.Empty;
    }

    public class ProfileListResponse
    {
        public List<ProfileMetadata> Profiles { get; set; } = new();
        public int Count { get; set; }
        public string ServerVersion { get; set; } = string.Empty;
    }

    public class ProfileMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AlignmentMode { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public bool InUse { get; set; }
        public List<int> Devices { get; set; } = new();
    }

    public class ProfileDownloadResponse
    {
        public string Name { get; set; } = string.Empty;
        public SkySettings Settings { get; set; } = null!;
        public ProfileMetadata Metadata { get; set; } = null!;
    }

    public class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }
}
