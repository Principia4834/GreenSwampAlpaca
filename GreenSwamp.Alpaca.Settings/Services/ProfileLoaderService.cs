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

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Service for loading active profiles and managing profile copies to user documents
    /// </summary>
    public interface IProfileLoaderService
    {
        /// <summary>
        /// Load the currently active profile and convert to SkySettings
        /// </summary>
        Task<SkySettings> LoadActiveProfileAsync();
        
        /// <summary>
        /// Copy the active profile to user documents folder for backup
        /// </summary>
        Task<string> CopyActiveProfileToDocumentsAsync();
        
        /// <summary>
        /// Copy all profiles to user documents folder
        /// </summary>
        Task<IEnumerable<string>> CopyAllProfilesToDocumentsAsync();
    }
    
    /// <summary>
    /// Implementation of profile loader service
    /// </summary>
    public class ProfileLoaderService : IProfileLoaderService
    {
        private readonly ISettingsProfileService _profileService;
        private readonly ILogger<ProfileLoaderService> _logger;
        
        public ProfileLoaderService(
            ISettingsProfileService profileService,
            ILogger<ProfileLoaderService> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Load the currently active profile and return its settings
        /// </summary>
        public async Task<SkySettings> LoadActiveProfileAsync()
        {
            try
            {
                _logger.LogInformation("Loading active profile...");
                
                var activeProfile = await _profileService.GetActiveProfileAsync();
                
                _logger.LogInformation("Active profile loaded: {ProfileName} (Mode: {Mode})", 
                    activeProfile.Name, activeProfile.AlignmentMode);
                
                return activeProfile.Settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load active profile");
                throw;
            }
        }
        
        /// <summary>
        /// Copy the active profile to user documents folder
        /// </summary>
        public async Task<string> CopyActiveProfileToDocumentsAsync()
        {
            try
            {
                var activeProfile = await _profileService.GetActiveProfileAsync();
                
                // Get user documents path
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var appDocumentsPath = Path.Combine(documentsPath, "GreenSwampAlpaca", "Profiles");
                Directory.CreateDirectory(appDocumentsPath);
                
                // Create destination filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var fileName = $"{activeProfile.Name}-{timestamp}.json";
                var destinationPath = Path.Combine(appDocumentsPath, fileName);
                
                // Export profile to documents
                await _profileService.ExportProfileAsync(activeProfile.Name, destinationPath);
                
                _logger.LogInformation("Active profile copied to documents: {Path}", destinationPath);
                
                return destinationPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy active profile to documents");
                throw;
            }
        }
        
        /// <summary>
        /// Copy all profiles to user documents folder
        /// </summary>
        public async Task<IEnumerable<string>> CopyAllProfilesToDocumentsAsync()
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var copiedPaths = new List<string>();
                
                // Get user documents path
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var appDocumentsPath = Path.Combine(documentsPath, "GreenSwampAlpaca", "Profiles");
                Directory.CreateDirectory(appDocumentsPath);
                
                // Create timestamp for this backup batch
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var batchFolder = Path.Combine(appDocumentsPath, $"Backup-{timestamp}");
                Directory.CreateDirectory(batchFolder);
                
                foreach (var profile in profiles)
                {
                    try
                    {
                        var fileName = $"{profile.Name}.json";
                        var destinationPath = Path.Combine(batchFolder, fileName);
                        
                        await _profileService.ExportProfileAsync(profile.Name, destinationPath);
                        copiedPaths.Add(destinationPath);
                        
                        _logger.LogDebug("Profile copied: {ProfileName} to {Path}", profile.Name, destinationPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to copy profile: {ProfileName}", profile.Name);
                    }
                }
                
                _logger.LogInformation("Copied {Count} profiles to documents: {Path}", 
                    copiedPaths.Count, batchFolder);
                
                return copiedPaths;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy profiles to documents");
                throw;
            }
        }
    }
}
