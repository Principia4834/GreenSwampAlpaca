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
    /// Service for managing JSON template files for settings profiles
    /// Templates define common settings and alignment mode-specific overrides
    /// </summary>
    public class SettingsTemplateService : ISettingsTemplateService
    {
        private readonly string _templatesPath;
        private readonly ILogger<SettingsTemplateService> _logger;
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        
        // Cache templates to avoid repeated file I/O
        private SkySettings? _commonSettingsCache;
        private readonly Dictionary<AlignmentMode, Dictionary<string, object>> _overridesCache = new();
        
        public SettingsTemplateService(ILogger<SettingsTemplateService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Setup templates directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var version = GetCurrentVersion();
            _templatesPath = Path.Combine(appData, "GreenSwampAlpaca", version, "templates");
            
            Directory.CreateDirectory(_templatesPath);
            
            _logger.LogInformation("SettingsTemplateService initialized | Templates path: {Path}", _templatesPath);
            
            // Initialize template files if they don't exist
            InitializeTemplates();
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
        /// Initialize template files if they don't exist
        /// </summary>
        private void InitializeTemplates()
        {
            try
            {
                // Create common template
                CreateTemplateIfNotExists("common.json", CreateCommonTemplateJson());
                
                // Create mode-specific override templates
                CreateTemplateIfNotExists("germanpolar-overrides.json", CreateGermanPolarOverridesJson());
                CreateTemplateIfNotExists("polar-overrides.json", CreatePolarOverridesJson());
                CreateTemplateIfNotExists("altaz-overrides.json", CreateAltAzOverridesJson());
                
                _logger.LogInformation("Template initialization complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize templates");
                throw;
            }
        }
        
        /// <summary>
        /// Create template file if it doesn't exist
        /// </summary>
        private void CreateTemplateIfNotExists(string fileName, string content)
        {
            var filePath = Path.Combine(_templatesPath, fileName);
            
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, content);
                _logger.LogInformation("Created template: {FileName}", fileName);
            }
        }
        
        public async Task<SkySettings> LoadTemplateAsync(AlignmentMode mode)
        {
            try
            {
                _logger.LogInformation("Loading template for {Mode}", mode);
                
                // Load common settings
                var common = await GetCommonSettingsAsync();
                
                // Load mode-specific overrides
                var overrides = await GetModeOverridesAsync(mode);
                
                // Merge
                var merged = await MergeSettingsAsync(common, overrides);
                
                _logger.LogInformation("Successfully loaded template for {Mode}", mode);
                
                return merged;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load template for {Mode}", mode);
                throw;
            }
        }
        
        public async Task<SkySettings> GetCommonSettingsAsync()
        {
            // Return cached if available
            if (_commonSettingsCache != null)
                return DeepClone(_commonSettingsCache);
            
            var templatePath = Path.Combine(_templatesPath, "common.json");
            
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Common template not found: {templatePath}");
            
            await _fileLock.WaitAsync();
            try
            {
                var json = await File.ReadAllTextAsync(templatePath);
                var template = JsonSerializer.Deserialize<SettingsTemplate>(json);
                
                if (template?.Settings == null)
                    throw new InvalidOperationException("Failed to deserialize common template");
                
                // Cache for future use
                _commonSettingsCache = template.Settings;
                
                _logger.LogDebug("Loaded common settings template");
                
                return DeepClone(template.Settings);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        
        public async Task<Dictionary<string, object>> GetModeOverridesAsync(AlignmentMode mode)
        {
            // Return cached if available
            if (_overridesCache.TryGetValue(mode, out var cached))
                return new Dictionary<string, object>(cached);
            
            var fileName = GetOverrideFileName(mode);
            var templatePath = Path.Combine(_templatesPath, fileName);
            
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Override template not found: {templatePath}");
            
            await _fileLock.WaitAsync();
            try
            {
                var json = await File.ReadAllTextAsync(templatePath);
                
                // Deserialize as JsonElement to handle dynamic override structure
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                
                if (!root.TryGetProperty("Overrides", out var overridesElement))
                    throw new InvalidOperationException($"Failed to find 'Overrides' in {mode} template");
                
                // Convert JsonElement to Dictionary
                var overrides = JsonSerializer.Deserialize<Dictionary<string, object>>(overridesElement.GetRawText());
                
                if (overrides == null)
                    throw new InvalidOperationException($"Failed to deserialize {mode} overrides");
                
                // Cache for future use
                _overridesCache[mode] = overrides;
                
                _logger.LogDebug("Loaded {Mode} override template", mode);
                
                return new Dictionary<string, object>(overrides);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        
        /// <summary>
        /// Get override file name for alignment mode
        /// </summary>
        private string GetOverrideFileName(AlignmentMode mode)
        {
            return mode switch
            {
                AlignmentMode.GermanPolar => "germanpolar-overrides.json",
                AlignmentMode.Polar => "polar-overrides.json",
                AlignmentMode.AltAz => "altaz-overrides.json",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown alignment mode")
            };
        }
        
        public async Task<SkySettings> MergeSettingsAsync(SkySettings common, Dictionary<string, object> overrides)
        {
            // Clone common settings
            var merged = DeepClone(common);
            
            // Apply overrides using reflection
            var settingsType = typeof(SkySettings);
            
            foreach (var kvp in overrides)
            {
                var property = settingsType.GetProperty(kvp.Key);
                if (property == null)
                {
                    _logger.LogWarning("Override property '{Property}' not found in SkySettings", kvp.Key);
                    continue;
                }
                
                if (!property.CanWrite)
                {
                    _logger.LogWarning("Override property '{Property}' is read-only", kvp.Key);
                    continue;
                }
                
                try
                {
                    // Handle different property types
                    var value = ConvertValue(kvp.Value, property.PropertyType);
                    property.SetValue(merged, value);
                    
                    _logger.LogTrace("Applied override: {Property} = {Value}", kvp.Key, value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply override for '{Property}'", kvp.Key);
                }
            }
            
            return await Task.FromResult(merged);
        }
        
        /// <summary>
        /// Convert override value to target property type
        /// </summary>
        private object? ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            
            // Handle JsonElement from deserialization
            if (value is JsonElement jsonElement)
            {
                return jsonElement.Deserialize(targetType);
            }
            
            // Handle direct type conversion
            if (targetType.IsInstanceOfType(value))
                return value;
            
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
                targetType = underlyingType;
            
            return Convert.ChangeType(value, targetType);
        }
        
        // Template creation methods - now read from embedded resources
        private string CreateCommonTemplateJson()
        {
            return ReadEmbeddedResource("common.json");
        }
        
        private string CreateGermanPolarOverridesJson()
        {
            return ReadEmbeddedResource("germanpolar-overrides.json");
        }
        
        private string CreatePolarOverridesJson()
        {
            return ReadEmbeddedResource("polar-overrides.json");
        }
        
        private string CreateAltAzOverridesJson()
        {
            return ReadEmbeddedResource("altaz-overrides.json");
        }
        
        /// <summary>
        /// Read embedded resource template file
        /// </summary>
        private string ReadEmbeddedResource(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"GreenSwamp.Alpaca.Settings.Templates.{fileName}";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _logger.LogWarning("Embedded resource not found: {Resource}. Using fallback.", resourceName);
                return GetFallbackTemplate(fileName);
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        
        /// <summary>
        /// Fallback if embedded resource is not found
        /// Returns minimal valid JSON template
        /// </summary>
        private string GetFallbackTemplate(string fileName)
        {
            if (fileName == "common.json")
            {
                return @"{""Description"":""Fallback common template"",""Version"":""1.0.0"",""Settings"":{}}";
            }
            else
            {
                return @"{""Description"":""Fallback override template"",""AlignmentMode"":""GermanPolar"",""Overrides"":{}}";
            }
        }
        
        /// <summary>
        /// Deep clone settings using JSON serialization
        /// </summary>
        private SkySettings DeepClone(SkySettings source)
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<SkySettings>(json)!;
        }
        
        /// <summary>
        /// Internal template structure for JSON deserialization
        /// </summary>
        private class SettingsTemplate
        {
            public string? Description { get; set; }
            public string? Version { get; set; }
            public SkySettings? Settings { get; set; }
        }
    }
}
