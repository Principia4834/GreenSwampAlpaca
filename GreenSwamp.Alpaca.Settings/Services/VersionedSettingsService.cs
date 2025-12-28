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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Versioned settings service that manages user settings with version-specific folders
    /// </summary>
    public class VersionedSettingsService : IVersionedSettingsService
    {
        private readonly string _appDataRoot;
        private readonly string _currentVersionPath;
        private readonly string _versionFile;
        private readonly IConfiguration _configuration;
        private readonly ILogger? _logger;
        private static readonly SemaphoreSlim _fileLock = new(1, 1);

        public string CurrentVersion { get; }
        public string[] AvailableVersions => GetAvailableVersions();
        public string UserSettingsPath => GetUserSettingsPath(CurrentVersion);

        public event EventHandler<SkySettings>? SettingsChanged;

        public VersionedSettingsService(
            IConfiguration configuration,
            ILogger? logger = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;

            // Get current app version from assembly
            CurrentVersion = GetAssemblyVersion();

            // Setup versioned folder structure
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataRoot = Path.Combine(appData, "GreenSwampAlpaca");
            _currentVersionPath = Path.Combine(_appDataRoot, CurrentVersion);
            _versionFile = Path.Combine(_appDataRoot, "current.version");

            Directory.CreateDirectory(_currentVersionPath);

            // Initialize versioned settings
            InitializeVersionedSettings();
        }

        public SkySettings GetSettings()
        {
            var settings = new SkySettings();
            _configuration.GetSection("SkySettings").Bind(settings);
            return settings;
        }

        public async Task SaveSettingsAsync(SkySettings settings)
        {
            await _fileLock.WaitAsync();
            try
            {
                var userSettingsPath = GetUserSettingsPath(CurrentVersion);
                
                var userSettings = await ReadUserSettingsFileAsync(userSettingsPath);
                userSettings["SkySettings"] = JsonSerializer.SerializeToElement(settings);
                userSettings["Version"] = JsonSerializer.SerializeToElement(CurrentVersion);
                userSettings["LastModified"] = JsonSerializer.SerializeToElement(DateTime.UtcNow);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(userSettings, options);
                await File.WriteAllTextAsync(userSettingsPath, json);

                // Update version file
                await File.WriteAllTextAsync(_versionFile, CurrentVersion);

                _logger?.LogInformation("Settings saved to version {Version}", CurrentVersion);
                
                // Raise settings changed event
                SettingsChanged?.Invoke(this, settings);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> MigrateFromPreviousVersionAsync()
        {
            var versions = GetAvailableVersions()
                .Where(v => v != CurrentVersion)
                .OrderByDescending(v => new Version(v))
                .ToList();

            if (!versions.Any())
            {
                _logger?.LogInformation("No previous versions found for migration");
                return false;
            }

            var previousVersion = versions.First();
            var previousSettingsPath = GetUserSettingsPath(previousVersion);

            if (!File.Exists(previousSettingsPath))
            {
                return false;
            }

            try
            {
                _logger?.LogInformation("Migrating settings from {Previous} to {Current}", 
                    previousVersion, CurrentVersion);

                // Read previous version settings
                var previousJson = await File.ReadAllTextAsync(previousSettingsPath);
                var previousSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(previousJson);

                if (previousSettings == null || !previousSettings.ContainsKey("SkySettings"))
                {
                    return false;
                }

                // Deserialize to typed object for validation
                var oldSettings = previousSettings["SkySettings"].Deserialize<SkySettings>();
                
                if (oldSettings == null)
                {
                    return false;
                }
                
                // Apply any version-specific migrations
                var migratedSettings = ApplyMigrations(oldSettings, previousVersion, CurrentVersion);

                // Save to current version
                await SaveSettingsAsync(migratedSettings);

                _logger?.LogInformation("Successfully migrated settings from version {Previous}", 
                    previousVersion);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to migrate settings from version {Previous}", 
                    previousVersion);
                return false;
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);
            
            if (File.Exists(userSettingsPath))
            {
                // Backup before deleting
                var backupPath = userSettingsPath + ".backup";
                File.Copy(userSettingsPath, backupPath, true);
                File.Delete(userSettingsPath);
            }
            
            CreateDefaultUserSettings(CurrentVersion);
            
            _logger?.LogInformation("Settings reset to defaults for version {Version}", CurrentVersion);
            
            // Reload settings and notify
            var settings = GetSettings();
            SettingsChanged?.Invoke(this, settings);
            
            await Task.CompletedTask;
        }

        private void InitializeVersionedSettings()
        {
            var userSettingsPath = GetUserSettingsPath(CurrentVersion);

            if (File.Exists(userSettingsPath))
            {
                _logger?.LogInformation("Using existing settings for version {Version}", CurrentVersion);
                return;
            }

            // Check if migration is needed
            if (File.Exists(_versionFile))
            {
                var lastVersion = File.ReadAllText(_versionFile).Trim();
                if (lastVersion != CurrentVersion)
                {
                    _logger?.LogInformation("Version change detected: {Old} ? {New}", 
                        lastVersion, CurrentVersion);
                }
            }

            // Create initial settings from defaults
            CreateDefaultUserSettings(CurrentVersion);
        }

        private void CreateDefaultUserSettings(string version)
        {
            var userSettingsPath = GetUserSettingsPath(version);
            
            // Get defaults from appsettings.json
            var defaultSettings = new
            {
                SkySettings = _configuration.GetSection("SkySettings").Get<SkySettings>(),
                Version = version,
                CreatedDate = DateTime.UtcNow
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(defaultSettings, options);
            File.WriteAllText(userSettingsPath, json);

            _logger?.LogInformation("Created default settings for version {Version}", version);
        }

        private SkySettings ApplyMigrations(SkySettings settings, string fromVersion, string toVersion)
        {
            // Version-specific migration logic
            var from = new Version(fromVersion);
            var to = new Version(toVersion);

            // Example: Migrate from 1.0 to 1.1
            if (from < new Version("1.1.0") && to >= new Version("1.1.0"))
            {
                _logger?.LogInformation("Applying 1.0 ? 1.1 migration");
                // Example: New setting added in 1.1
                // if (settings.NewProperty == default) settings.NewProperty = defaultValue;
            }

            // Example: Migrate from 1.1 to 2.0
            if (from < new Version("2.0.0") && to >= new Version("2.0.0"))
            {
                _logger?.LogInformation("Applying 1.1 ? 2.0 migration");
                // Example: Breaking changes in 2.0
            }

            return settings;
        }

        private string GetUserSettingsPath(string version)
        {
            var versionPath = Path.Combine(_appDataRoot, version);
            Directory.CreateDirectory(versionPath);
            return Path.Combine(versionPath, "appsettings.user.json");
        }

        private async Task<Dictionary<string, JsonElement>> ReadUserSettingsFileAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, JsonElement>();
            }

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }

        private string[] GetAvailableVersions()
        {
            if (!Directory.Exists(_appDataRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(_appDataRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && Version.TryParse(name, out _))
                .ToArray()!;
        }

        private string GetAssemblyVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            
            // Try to get informational version first (includes pre-release tags)
            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;
            
            var informationalVersion = infoVersionAttr?.InformationalVersion;
            
            if (!string.IsNullOrEmpty(informationalVersion))
            {
                // Remove any build metadata (e.g., +commitHash)
                var plusIndex = informationalVersion.IndexOf('+');
                if (plusIndex > 0)
                {
                    informationalVersion = informationalVersion.Substring(0, plusIndex);
                }
                return informationalVersion;
            }
            
            // Fall back to assembly version
            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }
    }
}
