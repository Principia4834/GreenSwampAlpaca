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

using GreenSwamp.Alpaca.Settings.Models;
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
        private static readonly SemaphoreSlim _fileLock = new(1, 1);
        
        // Cache templates to avoid repeated file I/O
        private SkySettings? _commonSettingsCache;
        private readonly Dictionary<AlignmentMode, Dictionary<string, object>> _overridesCache = new();
        
        public SettingsTemplateService()
        {
            // Setup templates directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var version = GetCurrentVersion();
            _templatesPath = Path.Combine(appData, "GreenSwampAlpaca", version, "templates");

            Directory.CreateDirectory(_templatesPath);

            ASCOM.Alpaca.Logging.LogVerbose($"SettingsTemplateService initialized | Templates path: {_templatesPath}");

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

                ASCOM.Alpaca.Logging.LogVerbose("Template initialization complete");
            }
            catch (Exception ex)
            {
                ASCOM.Alpaca.Logging.LogError($"Failed to initialize templates: {ex.Message}");
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
                ASCOM.Alpaca.Logging.LogVerbose($"Created template: {fileName}");
            }
        }
        
        public async Task<SkySettings> LoadTemplateAsync(AlignmentMode mode)
        {
            try
            {
                ASCOM.Alpaca.Logging.LogVerbose($"Loading template for {mode}");

                // Load common settings
                var common = await GetCommonSettingsAsync();

                // Load mode-specific overrides
                var overrides = await GetModeOverridesAsync(mode);

                // Merge
                var merged = await MergeSettingsAsync(common, overrides);

                ASCOM.Alpaca.Logging.LogVerbose($"Successfully loaded template for {mode}");

                return merged;
            }
            catch (Exception ex)
            {
                ASCOM.Alpaca.Logging.LogError($"Failed to load template for {mode}: {ex.Message}");
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

                ASCOM.Alpaca.Logging.LogVerbose("Loaded common settings template");

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

                ASCOM.Alpaca.Logging.LogVerbose($"Loaded {mode} override template");

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
                    ASCOM.Alpaca.Logging.LogWarning($"Override property '{kvp.Key}' not found in SkySettings");
                    continue;
                }
                
                if (!property.CanWrite)
                {
                    ASCOM.Alpaca.Logging.LogWarning($"Override property '{kvp.Key}' is read-only");
                    continue;
                }
                
                try
                {
                    // Get existing value from merged settings
                    var existingValue = property.GetValue(merged);
                    
                    // Handle different property types - deep merge complex objects
                    var value = ConvertValue(kvp.Value, property.PropertyType, existingValue);
                    property.SetValue(merged, value);

                    ASCOM.Alpaca.Logging.LogVerbose($"Applied override: {kvp.Key} = {value}");
                }
                catch (Exception ex)
                {
                    ASCOM.Alpaca.Logging.LogError($"Failed to apply override for '{kvp.Key}': {ex.Message}");
                }
            }
            
            return await Task.FromResult(merged);
        }
        
        /// <summary>
        /// Convert override value to target property type
        /// </summary>
        private object? ConvertValue(object value, Type targetType, object? existingValue = null)
        {
            if (value == null) return null;
            
            // Handle JsonElement from deserialization
            if (value is JsonElement jsonElement)
            {
                // For complex types, perform deep merge if we have an existing value
                if (existingValue != null && !targetType.IsPrimitive && !targetType.IsEnum && targetType != typeof(string))
                {
                    return DeepMergeJsonElement(jsonElement, existingValue);
                }
                
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
        
        /// <summary>
        /// Deep merge a JsonElement into an existing object
        /// Only overwrites properties that are explicitly defined in the JsonElement
        /// </summary>
        private object DeepMergeJsonElement(JsonElement source, object target)
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                // For non-objects, just deserialize to target type
                return source.Deserialize(target.GetType())!;
            }
            
            // Use reflection to update only the properties present in the source
            var targetType = target.GetType();
            
            foreach (var property in source.EnumerateObject())
            {
                var propertyInfo = targetType.GetProperty(property.Name);
                if (propertyInfo == null || !propertyInfo.CanWrite)
                {
                    ASCOM.Alpaca.Logging.LogVerbose($"Skipping override property '{property.Name}' - not found or read-only");
                    continue;
                }
                
                try
                {
                    var existingValue = propertyInfo.GetValue(target);
                    var propertyValue = property.Value;
                    
                    // Recursively merge nested objects
                    if (existingValue != null && 
                        propertyValue.ValueKind == JsonValueKind.Object &&
                        !propertyInfo.PropertyType.IsPrimitive && 
                        !propertyInfo.PropertyType.IsEnum && 
                        propertyInfo.PropertyType != typeof(string))
                    {
                        DeepMergeJsonElement(propertyValue, existingValue);
                    }
                    else
                    {
                        // Set the value directly
                        var newValue = propertyValue.Deserialize(propertyInfo.PropertyType);
                        propertyInfo.SetValue(target, newValue);
                        ASCOM.Alpaca.Logging.LogVerbose($"Merged property '{property.Name}' = {newValue}");
                    }
                }
                catch (Exception ex)
                {
                    ASCOM.Alpaca.Logging.LogWarning($"Failed to merge property '{property.Name}': {ex.Message}");
                }
            }
            
            return target;
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
                ASCOM.Alpaca.Logging.LogWarning($"Embedded resource not found: {resourceName}. Using fallback.");
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
