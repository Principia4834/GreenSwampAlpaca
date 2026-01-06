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
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Service for managing settings profiles with CRUD operations
    /// </summary>
    public class SettingsProfileService : ISettingsProfileService
    {
        private readonly string _profilesPath;
        private readonly ILogger<SettingsProfileService> _logger;
        private readonly ISettingsTemplateService _templateService;
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        private static readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;
        
        public SettingsProfileService(
            ILogger<SettingsProfileService> logger,
            ISettingsTemplateService templateService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            
            // Setup profiles directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var version = GetCurrentVersion();
            _profilesPath = Path.Combine(appData, "GreenSwampAlpaca", version, "profiles");
            
            Directory.CreateDirectory(_profilesPath);
            
            _logger.LogInformation("SettingsProfileService initialized | Profiles path: {Path}", _profilesPath);
            
            // Don't initialize here - do it lazily on first use to avoid blocking
        }
        
        /// <summary>
        /// Get current application version
        /// </summary>
        private string GetCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;
            
            var version = infoVersionAttr?.InformationalVersion 
                ?? assembly.GetName().Version?.ToString() 
                ?? "1.0.0";
            
            // Remove build metadata (e.g., +commitHash)
            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }
            
            return version;
        }
        
        /// <summary>
        /// Initialize default profiles for each alignment mode (lazy initialization)
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            
            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return; // Double-check after acquiring lock
                
                foreach (AlignmentMode mode in Enum.GetValues(typeof(AlignmentMode)))
                {
                    var defaultName = GetDefaultProfileName(mode);
                    var filePath = GetProfilePath(defaultName);
                    
                    if (!File.Exists(filePath))
                    {
                        var profile = await CreateDefaultProfileAsync(defaultName, mode);
                        await SaveProfileAsync(profile);
                        
                        _logger.LogInformation("Created default profile: {Name}", defaultName);
                    }
                }
                
                _initialized = true;
                _logger.LogInformation("Default profile initialization complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize default profiles");
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }
        
        public async Task<SettingsProfile> CreateProfileAsync(string name, AlignmentMode mode, string? copyFromProfile = null)
        {
            await EnsureInitializedAsync();
            
            try
            {
                // Validate name
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Profile name cannot be empty", nameof(name));
                
                // Check if profile already exists
                if (await ProfileExistsAsync(name))
                    throw new InvalidOperationException($"Profile '{name}' already exists");
                
                _logger.LogInformation("Creating profile: {Name} (Mode: {Mode})", name, mode);
                
                SettingsProfile profile;
                
                if (!string.IsNullOrEmpty(copyFromProfile))
                {
                    // Clone existing profile
                    var source = await GetProfileAsync(copyFromProfile);
                    profile = source.Clone(name);
                    profile.AlignmentMode = mode;
                    profile.Settings.AlignmentMode = mode.ToString();
                }
                else
                {
                    // Create from template
                    var settings = await _templateService.LoadTemplateAsync(mode);
                    
                    profile = new SettingsProfile
                    {
                        Name = name,
                        DisplayName = name,
                        Description = $"Custom {mode} profile",
                        AlignmentMode = mode,
                        Settings = settings,
                        IsReadOnly = false,
                        Created = DateTime.UtcNow,
                        LastModified = DateTime.UtcNow
                    };
                }
                
                // Save profile
                await SaveProfileAsync(profile);
                
                _logger.LogInformation("Created profile: {Name}", name);
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create profile: {Name}", name);
                throw;
            }
        }
        
        public async Task<SettingsProfile> GetProfileAsync(string name)
        {
            await EnsureInitializedAsync();
            
            try
            {
                var filePath = GetProfilePath(name);
                
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Profile '{name}' not found");
                
                await _fileLock.WaitAsync();
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var profile = JsonSerializer.Deserialize<SettingsProfile>(json);
                    
                    if (profile == null)
                        throw new InvalidOperationException($"Failed to deserialize profile '{name}'");
                    
                    _logger.LogDebug("Loaded profile: {Name}", name);
                    
                    return profile;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                _logger.LogError(ex, "Failed to get profile: {Name}", name);
                throw;
            }
        }
        
        private async Task<bool> ProfileExistsAsync(string name)
        {
            var filePath = GetProfilePath(name);
            return File.Exists(filePath);
        }
        
        public async Task<IEnumerable<SettingsProfile>> GetAllProfilesAsync()
        {
            await EnsureInitializedAsync();
            
            try
            {
                var profiles = new List<SettingsProfile>();
                
                // Get all .json files in profiles directory
                var files = Directory.GetFiles(_profilesPath, "*.json");
                
                foreach (var file in files)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var profile = await GetProfileAsync(name);
                        profiles.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load profile: {File}", file);
                    }
                }
                
                _logger.LogDebug("Loaded {Count} profiles", profiles.Count);
                
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all profiles");
                throw;
            }
        }
        
        public async Task UpdateProfileAsync(SettingsProfile profile)
        {
            try
            {
                if (profile == null)
                    throw new ArgumentNullException(nameof(profile));
                
                // Cannot update read-only profiles
                if (profile.IsReadOnly)
                    throw new InvalidOperationException($"Cannot update read-only profile '{profile.Name}'");
                
                // Validate before save
                var validation = await ValidateProfileAsync(profile);
                if (!validation.IsValid)
                    throw new InvalidOperationException($"Profile validation failed: {validation.ErrorMessage}");
                
                // Update metadata
                profile.LastModified = DateTime.UtcNow;
                
                // Save
                await SaveProfileAsync(profile);
                
                _logger.LogInformation("Updated profile: {Name}", profile.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update profile: {Name}", profile?.Name);
                throw;
            }
        }
        
        public async Task DeleteProfileAsync(string name)
        {
            try
            {
                // Prevent deletion of default profiles
                if (IsDefaultProfile(name))
                    throw new InvalidOperationException($"Cannot delete default profile '{name}'");
                
                // Prevent deletion of active profile
                var activeProfile = await GetActiveProfileNameAsync();
                if (activeProfile == name)
                    throw new InvalidOperationException($"Cannot delete active profile '{name}'. Set another profile as active first.");
                
                var filePath = GetProfilePath(name);
                
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Profile '{name}' not found");
                
                await _fileLock.WaitAsync();
                try
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted profile: {Name}", name);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete profile: {Name}", name);
                throw;
            }
        }
        
        public async Task<SettingsProfile> GetActiveProfileAsync()
        {
            await EnsureInitializedAsync();
            
            try
            {
                var name = await GetActiveProfileNameAsync();
                return await GetProfileAsync(name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active profile");
                throw;
            }
        }
        
        public async Task SetActiveProfileAsync(string name)
        {
            try
            {
                // Verify profile exists
                if (!await ProfileExistsAsync(name))
                    throw new FileNotFoundException($"Profile '{name}' not found");
                
                // Write active profile name
                var activeFilePath = Path.Combine(_profilesPath, "..", "active-profile.txt");
                
                await _fileLock.WaitAsync();
                try
                {
                    await File.WriteAllTextAsync(activeFilePath, name);
                    _logger.LogInformation("Set active profile: {Name}", name);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set active profile: {Name}", name);
                throw;
            }
        }
        
        public async Task<SettingsProfile> GetDefaultProfileAsync(AlignmentMode mode)
        {
            await EnsureInitializedAsync();
            
            try
            {
                var defaultName = GetDefaultProfileName(mode);
                return await GetProfileAsync(defaultName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get default profile for {Mode}", mode);
                throw;
            }
        }
        
        public async Task<ValidationResult> ValidateProfileAsync(SettingsProfile profile)
        {
            try
            {
                var errors = new List<string>();
                
                // Validate name
                if (string.IsNullOrWhiteSpace(profile.Name))
                    errors.Add("Profile name is required");
                
                // Validate alignment mode
                if (!Enum.IsDefined(typeof(AlignmentMode), profile.AlignmentMode))
                    errors.Add($"Invalid alignment mode: {profile.AlignmentMode}");
                
                // Validate settings
                if (profile.Settings == null)
                {
                    errors.Add("Settings are required");
                }
                else
                {
                    // Validate coordinates
                    if (profile.Settings.Latitude < -90 || profile.Settings.Latitude > 90)
                        errors.Add($"Invalid latitude: {profile.Settings.Latitude}");
                    
                    if (profile.Settings.Longitude < -180 || profile.Settings.Longitude > 180)
                        errors.Add($"Invalid longitude: {profile.Settings.Longitude}");
                    
                    // Validate mount type
                    if (string.IsNullOrWhiteSpace(profile.Settings.Mount))
                        errors.Add("Mount type is required");
                }
                
                return await Task.FromResult(errors.Count == 0
                    ? ValidationResult.Success()
                    : ValidationResult.Failure(string.Join("; ", errors)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate profile: {Name}", profile?.Name);
                throw;
            }
        }
        
        public async Task<string> ExportProfileAsync(string name, string destinationPath)
        {
            try
            {
                var profile = await GetProfileAsync(name);
                
                var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(destinationPath, json);
                
                _logger.LogInformation("Exported profile '{Name}' to {Path}", name, destinationPath);
                
                return destinationPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export profile: {Name}", name);
                throw;
            }
        }
        
        public async Task<SettingsProfile> ImportProfileAsync(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException($"Import file not found: {sourcePath}");
                
                var json = await File.ReadAllTextAsync(sourcePath);
                var profile = JsonSerializer.Deserialize<SettingsProfile>(json);
                
                if (profile == null)
                    throw new InvalidOperationException("Failed to deserialize profile");
                
                // Check if profile with same name exists
                if (await ProfileExistsAsync(profile.Name))
                {
                    // Generate unique name
                    var baseName = profile.Name;
                    var counter = 1;
                    while (await ProfileExistsAsync($"{baseName}-{counter}"))
                    {
                        counter++;
                    }
                    profile.Name = $"{baseName}-{counter}";
                    _logger.LogInformation("Profile renamed to {NewName} (name conflict)", profile.Name);
                }
                
                // Reset metadata
                profile.IsReadOnly = false;
                profile.Created = DateTime.UtcNow;
                profile.LastModified = DateTime.UtcNow;
                
                // Save imported profile
                await SaveProfileAsync(profile);
                
                _logger.LogInformation("Imported profile '{Name}' from {Path}", profile.Name, sourcePath);
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import profile from: {Path}", sourcePath);
                throw;
            }
        }
        
        // Helper methods
        
        private async Task<SettingsProfile> CreateDefaultProfileAsync(string name, AlignmentMode mode)
        {
            // Load settings from template asynchronously
            var settings = await _templateService.LoadTemplateAsync(mode);
            
            return new SettingsProfile
            {
                Name = name,
                DisplayName = GetDefaultDisplayName(mode),
                Description = $"Default settings for {mode} mounts",
                AlignmentMode = mode,
                Settings = settings,
                IsReadOnly = true,
                Created = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };
        }
        
        private async Task SaveProfileAsync(SettingsProfile profile)
        {
            var filePath = GetProfilePath(profile.Name);
            
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await _fileLock.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogDebug("Saved profile: {Name}", profile.Name);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        
        private string GetProfilePath(string name)
        {
            var fileName = $"{name}.json";
            return Path.Combine(_profilesPath, fileName);
        }
        
        private string GetDefaultProfileName(AlignmentMode mode)
        {
            return $"default-{mode.ToString().ToLowerInvariant()}";
        }
        
        private string GetDefaultDisplayName(AlignmentMode mode)
        {
            return mode switch
            {
                AlignmentMode.GermanPolar => "German Equatorial (Default)",
                AlignmentMode.Polar => "Fork Equatorial (Default)",
                AlignmentMode.AltAz => "Alt-Azimuth (Default)",
                _ => $"{mode} (Default)"
            };
        }
        
        private bool IsDefaultProfile(string name)
        {
            return name.StartsWith("default-", StringComparison.OrdinalIgnoreCase);
        }
        
        private async Task<string> GetActiveProfileNameAsync()
        {
            var activeFilePath = Path.Combine(_profilesPath, "..", "active-profile.txt");
            
            if (File.Exists(activeFilePath))
            {
                var name = await File.ReadAllTextAsync(activeFilePath);
                if (!string.IsNullOrWhiteSpace(name) && await ProfileExistsAsync(name.Trim()))
                {
                    return name.Trim();
                }
            }
            
            // Default to GermanPolar if no active profile set
            return GetDefaultProfileName(AlignmentMode.GermanPolar);
        }
    }
}
