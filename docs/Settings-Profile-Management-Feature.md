# Settings Profile Management Feature Analysis
**Date**: 2025-01-XX  
**Project**: GreenSwamp Alpaca Server  
**Phase**: Settings Management Enhancement  
**Related**: [Multi-Telescope Architecture Analysis](Multi-Telescope-Architecture-Analysis.md)

---

## Executive Summary

Implement a **Settings Profile Management System** to support:
1. ? **Multiple settings profiles** (one per mount configuration)
2. ? **Template-based defaults** using JSON templates for common + mode-specific settings
3. ? **CRUD operations** via Blazor UI
4. ? **Profile selection** at telescope device creation
5. ? **AppData-based storage** (version-aware)

**Key Insight**: Home and park axis positions differ between alignment modes, requiring mode-specific templates that merge with common settings.

**Impact on Telescope/Mount Code**: ? **MINIMAL** - Only `Program.cs` initialization needs changes

---

## Table of Contents

1. [Requirements Analysis](#requirements-analysis)
2. [Template Architecture](#template-architecture)
3. [Current Architecture](#current-architecture)
4. [Proposed Architecture](#proposed-architecture)
5. [Data Model](#data-model)
6. [Template System](#template-system)
7. [UI Design](#ui-design)
8. [Implementation Plan](#implementation-plan)
9. [Migration Strategy](#migration-strategy)
10. [Testing Strategy](#testing-strategy)

---

## Requirements Analysis

### **Functional Requirements**

| ID | Requirement | Priority |
|----|-------------|----------|
| **FR-1** | Create new settings profile with alignment mode | ?? HIGH |
| **FR-2** | View list of available settings profiles | ?? HIGH |
| **FR-3** | Edit existing settings profile | ?? HIGH |
| **FR-4** | Delete settings profile | ?? MEDIUM |
| **FR-5** | Select settings profile when creating telescope device | ?? HIGH |
| **FR-6** | Use JSON templates for common + mode-specific settings | ?? HIGH |
| **FR-7** | Store profiles in AppData with version awareness | ?? HIGH |
| **FR-8** | Validate profile data before save | ?? MEDIUM |
| **FR-9** | Export/import profiles for backup | ?? LOW |
| **FR-10** | Clone existing profile | ?? LOW |

---

### **Non-Functional Requirements**

| ID | Requirement | Priority |
|----|-------------|----------|
| **NFR-1** | Profile operations complete in <500ms | ?? MEDIUM |
| **NFR-2** | Support at least 10 concurrent profiles | ?? LOW |
| **NFR-3** | Thread-safe profile operations | ?? HIGH |
| **NFR-4** | Graceful degradation if profile load fails | ?? HIGH |
| **NFR-5** | Template changes don't require code changes | ?? HIGH |

---

## Template Architecture

### **Overview**

Instead of hard-coding defaults in C#, use **JSON template files** that separate:
1. **Common settings** - Applicable to all alignment modes
2. **Mode-specific overrides** - Unique to each alignment mode

When creating a new profile, the system **merges** common + mode-specific templates to produce the complete settings file.

---

### **Template Storage Structure**

```
%AppData%/GreenSwampAlpaca/{version}/
??? templates/                          ? NEW: Template directory
?   ??? common.json                     ? Common settings for all modes
?   ??? germanpolar-overrides.json      ? GermanPolar-specific settings
?   ??? polar-overrides.json            ? Polar-specific settings
?   ??? altaz-overrides.json            ? AltAz-specific settings
??? profiles/                           
?   ??? default-germanpolar.json        ? Generated from templates
?   ??? default-polar.json              ? Generated from templates
?   ??? default-altaz.json              ? Generated from templates
?   ??? my-eq6r-mount.json              ? User profile
?   ??? ...
??? active-profile.txt                  
??? appsettings.user.json               ? Legacy (for migration)
```

---

### **Template Files**

#### **1. Common Settings Template** (`templates/common.json`)

```json
{
  "$schema": "../appsettings.schema.json",
  "Description": "Common settings applicable to all alignment modes",
  "Version": "1.0.0",
  "Settings": {
    "Mount": "Simulator",
    "Port": "COM1",
    "BaudRate": 9600,
    "DataBits": 8,
    "Handshake": "None",
    "ReadTimeout": 1000,
    "DTREnable": false,
    "RTSEnable": false,
    
    "Latitude": 56.76,
    "Longitude": -5.85,
    "Elevation": 10.0,
    "UTCOffset": "00:00:00",
    
    "AutoTrack": false,
    "EquatorialCoordinateType": "Topocentric",
    "AtPark": false,
    
    "ApertureDiameter": 0.2,
    "ApertureArea": 0.0269,
    "FocalLength": 1.26,
    
    "Refraction": true,
    "Temperature": 20.0,
    
    "CustomGearing": false,
    "CustomRa360Steps": 0,
    "CustomRaWormTeeth": 0,
    "CustomDec360Steps": 0,
    "CustomDecWormTeeth": 0,
    "CustomRaTrackingOffset": 0,
    "CustomDecTrackingOffset": 0,
    
    "RaBacklash": 0,
    "DecBacklash": 0,
    
    "MinPulseRa": 20,
    "MinPulseDec": 20,
    "DecPulseToGoTo": false,
    "St4Guiderate": 2,
    "GuideRateOffsetX": 0.5,
    "GuideRateOffsetY": 0.5,
    
    "TrackingRate": "Sidereal",
    "SiderealRate": 15.0410671787,
    "LunarRate": 14.685,
    "SolarRate": 15.0,
    "KingRate": 15.0369,
    "RATrackingOffset": 0,
    
    "LimitPark": false,
    "ParkLimitName": "Default",
    "LimitTracking": false,
    "HourAngleLimit": 15.0,
    "NoSyncPastMeridian": false,
    "SyncLimit": 30,
    "SyncLimitOn": true,
    "AxisTrackingLimit": 0.0,
    "HzLimitTracking": false,
    "ParkHzLimitName": "Default",
    "HzLimitPark": false,
    "AxisHzTrackingLimit": 0.0,
    "AxisUpperLimitY": 180.0,
    "AxisLowerLimitY": -180.0,
    "AxisLimitX": 180.0,
    
    "PecOn": false,
    "PpecOn": false,
    "AlternatingPPEC": false,
    "PecOffSet": 0,
    "PecWormFile": "",
    "Pec360File": "",
    "PecMode": "PecWorm",
    
    "EncodersOn": false,
    "HcSpeed": "Seven",
    "HcMode": "Axes",
    "HcAntiRa": false,
    "HcAntiDec": false,
    "HcFlipEW": false,
    "HcFlipNS": false
  }
}
```

---

#### **2. GermanPolar Override Template** (`templates/germanpolar-overrides.json`)

```json
{
  "Description": "German Equatorial mount-specific settings",
  "AlignmentMode": "GermanPolar",
  "Overrides": {
    "AlignmentMode": "GermanPolar",
    
    "HomeAxisX": 0.0,
    "HomeAxisY": 90.0,
    "AutoHomeAxisX": 0.0,
    "AutoHomeAxisY": 90.0,
    
    "ParkName": "Default",
    "ParkPositions": [
      {
        "Name": "Default",
        "X": 180.0,
        "Y": 90.0
      },
      {
        "Name": "Home",
        "X": 0.0,
        "Y": 90.0
      },
      {
        "Name": "Zenith",
        "X": 0.0,
        "Y": 0.0
      }
    ],
    "ParkAxes": [180.0, 90.0],
    
    "Description": "German Equatorial (GEM) mount. Counterweight down at home (0°, 90°), tube pointing north. Park position: Counterweight up (180°, 90°)."
  }
}
```

---

#### **3. Polar Override Template** (`templates/polar-overrides.json`)

```json
{
  "Description": "Fork Equatorial mount-specific settings",
  "AlignmentMode": "Polar",
  "Overrides": {
    "AlignmentMode": "Polar",
    
    "HomeAxisX": 0.0,
    "HomeAxisY": 0.0,
    "AutoHomeAxisX": 0.0,
    "AutoHomeAxisY": 0.0,
    
    "ParkName": "Default",
    "ParkPositions": [
      {
        "Name": "Default",
        "X": 0.0,
        "Y": 90.0
      },
      {
        "Name": "Home",
        "X": 0.0,
        "Y": 0.0
      },
      {
        "Name": "Horizon",
        "X": 0.0,
        "Y": 0.0
      }
    ],
    "ParkAxes": [0.0, 90.0],
    
    "Description": "Fork Equatorial mount. Tube horizontal at home (0°, 0°). Park position: Tube pointing up (0°, 90°)."
  }
}
```

---

#### **4. AltAz Override Template** (`templates/altaz-overrides.json`)

```json
{
  "Description": "Alt-Azimuth mount-specific settings",
  "AlignmentMode": "AltAz",
  "Overrides": {
    "AlignmentMode": "AltAz",
    
    "HomeAxisX": 0.0,
    "HomeAxisY": 0.0,
    "AutoHomeAxisX": 0.0,
    "AutoHomeAxisY": 0.0,
    
    "ParkName": "Default",
    "ParkPositions": [
      {
        "Name": "Default",
        "X": 0.0,
        "Y": 90.0
      },
      {
        "Name": "Home",
        "X": 0.0,
        "Y": 0.0
      },
      {
        "Name": "North-Horizon",
        "X": 0.0,
        "Y": 0.0
      }
    ],
    "ParkAxes": [0.0, 90.0],
    
    "NoSyncPastMeridian": false,
    "HourAngleLimit": 0.0,
    
    "Description": "Alt-Azimuth mount. No meridian flip. Home: North at horizon (0°, 0°). Park: Zenith (0°, 90°)."
  }
}
```

---

### **Template Merge Logic**

When creating a new profile:

```
Common Settings
    +
Mode-Specific Overrides
    =
Complete Profile Settings
```

**Example**: Creating a GermanPolar profile:

```json
// Step 1: Load common.json
{
  "Mount": "Simulator",
  "Latitude": 56.76,
  // ... all common settings
}

// Step 2: Load germanpolar-overrides.json
{
  "AlignmentMode": "GermanPolar",
  "HomeAxisX": 0.0,
  "HomeAxisY": 90.0,
  // ... mode-specific overrides
}

// Step 3: Merge (overrides win)
{
  "Mount": "Simulator",              // From common
  "Latitude": 56.76,                 // From common
  "AlignmentMode": "GermanPolar",    // From override
  "HomeAxisX": 0.0,                  // From override
  "HomeAxisY": 90.0,                 // From override
  // ... rest of settings
}
```

---

## Proposed Architecture

### **1. Template Service**

```csharp
namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Manages JSON template loading and merging
    /// </summary>
    public interface ISettingsTemplateService
    {
        Task<SkySettings> LoadTemplateAsync(AlignmentMode mode);
        Task<SkySettings> GetCommonSettingsAsync();
        Task<Dictionary<string, object>> GetModeOverridesAsync(AlignmentMode mode);
        Task<SkySettings> MergeSettingsAsync(SkySettings common, Dictionary<string, object> overrides);
    }
    
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
            _logger = logger;
            
            // Setup templates directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var version = GetCurrentVersion();
            _templatesPath = Path.Combine(appData, "GreenSwampAlpaca", version, "templates");
            
            Directory.CreateDirectory(_templatesPath);
            
            // Initialize template files if they don't exist
            InitializeTemplates();
        }
        
        public async Task<SkySettings> LoadTemplateAsync(AlignmentMode mode)
        {
            // Load common settings
            var common = await GetCommonSettingsAsync();
            
            // Load mode-specific overrides
            var overrides = await GetModeOverridesAsync(mode);
            
            // Merge
            var merged = await MergeSettingsAsync(common, overrides);
            
            _logger.LogInformation("Loaded template for {Mode}", mode);
            
            return merged;
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
                var template = JsonSerializer.Deserialize<OverrideTemplate>(json);
                
                if (template?.Overrides == null)
                    throw new InvalidOperationException($"Failed to deserialize {mode} override template");
                
                // Cache for future use
                _overridesCache[mode] = template.Overrides;
                
                return new Dictionary<string, object>(template.Overrides);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        
        public async Task<SkySettings> MergeSettingsAsync(
            SkySettings common, 
            Dictionary<string, object> overrides)
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
                
                try
                {
                    // Handle different property types
                    var value = ConvertValue(kvp.Value, property.PropertyType);
                    property.SetValue(merged, value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply override for '{Property}'", kvp.Key);
                }
            }
            
            return merged;
        }
        
        private void InitializeTemplates()
        {
            // Create common template
            CreateTemplateIfNotExists("common.json", CreateCommonTemplate());
            
            // Create mode-specific override templates
            CreateTemplateIfNotExists("germanpolar-overrides.json", CreateGermanPolarOverrides());
            CreateTemplateIfNotExists("polar-overrides.json", CreatePolarOverrides());
            CreateTemplateIfNotExists("altaz-overrides.json", CreateAltAzOverrides());
            
            _logger.LogInformation("Template initialization complete");
        }
        
        private void CreateTemplateIfNotExists(string fileName, string content)
        {
            var filePath = Path.Combine(_templatesPath, fileName);
            
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, content);
                _logger.LogInformation("Created template: {FileName}", fileName);
            }
        }
        
        private string CreateCommonTemplate()
        {
            // Load from embedded resource or appsettings.json
            // For now, return the JSON structure shown above
            return @"{
  ""$schema"": ""../appsettings.schema.json"",
  ""Description"": ""Common settings applicable to all alignment modes"",
  ""Version"": ""1.0.0"",
  ""Settings"": {
    ""Mount"": ""Simulator"",
    ""Port"": ""COM1"",
    ""BaudRate"": 9600,
    // ... (full common template as shown above)
  }
}";
        }
        
        private string CreateGermanPolarOverrides()
        {
            // Return the GermanPolar override template as shown above
            return @"{
  ""Description"": ""German Equatorial mount-specific settings"",
  ""AlignmentMode"": ""GermanPolar"",
  ""Overrides"": {
    // ... (full override template as shown above)
  }
}";
        }
        
        private string CreatePolarOverrides()
        {
            // Return the Polar override template
            // Similar structure to GermanPolar
        }
        
        private string CreateAltAzOverrides()
        {
            // Return the AltAz override template
            // Similar structure to GermanPolar
        }
        
        private string GetOverrideFileName(AlignmentMode mode)
        {
            return mode switch
            {
                AlignmentMode.GermanPolar => "germanpolar-overrides.json",
                AlignmentMode.Polar => "polar-overrides.json",
                AlignmentMode.AltAz => "altaz-overrides.json",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
        
        private SkySettings DeepClone(SkySettings source)
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<SkySettings>(json)!;
        }
        
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
            
            return Convert.ChangeType(value, targetType);
        }
        
        private string GetCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var infoVersionAttr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;
            
            var version = infoVersionAttr?.InformationalVersion 
                ?? assembly.GetName().Version?.ToString() 
                ?? "1.0.0";
            
            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }
            
            return version;
        }
    }
    
    // Template models
    
    internal class SettingsTemplate
    {
        public string? Description { get; set; }
        public string? Version { get; set; }
        public SkySettings? Settings { get; set; }
    }
    
    internal class OverrideTemplate
    {
        public string? Description { get; set; }
        public string? AlignmentMode { get; set; }
        public Dictionary<string, object>? Overrides { get; set; }
    }
}
```

---

### **2. Updated Profile Service**

```csharp
public class SettingsProfileService : ISettingsProfileService
{
    private readonly string _profilesPath;
    private readonly ILogger<SettingsProfileService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ISettingsTemplateService _templateService;  // NEW
    private static readonly SemaphoreSlim _fileLock = new(1, 1);
    
    public SettingsProfileService(
        IConfiguration configuration,
        ILogger<SettingsProfileService> logger,
        ISettingsTemplateService templateService)  // NEW
    {
        _configuration = configuration;
        _logger = logger;
        _templateService = templateService;  // NEW
        
        // Setup profiles directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var version = GetCurrentVersion();
        _profilesPath = Path.Combine(appData, "GreenSwampAlpaca", version, "profiles");
        
        Directory.CreateDirectory(_profilesPath);
        
        // Initialize default profiles if not exist
        InitializeDefaultProfiles();
    }
    
    public async Task<SettingsProfile> CreateProfileAsync(
        string name, 
        AlignmentMode mode, 
        string? copyFromProfile = null)
    {
        // Validate name
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name cannot be empty", nameof(name));
        
        if (await ProfileExistsAsync(name))
            throw new InvalidOperationException($"Profile '{name}' already exists");
        
        // Create profile
        SettingsProfile profile;
        
        if (!string.IsNullOrEmpty(copyFromProfile))
        {
            // Clone existing profile
            var source = await GetProfileAsync(copyFromProfile);
            profile = source.Clone();
            profile.Name = name;
            profile.AlignmentMode = mode;
        }
        else
        {
            // Create from alignment mode defaults
            profile = CreateDefaultProfile(name, mode);
        }
        
        // Save profile
        await SaveProfileAsync(profile);
        
        _logger.LogInformation("Created settings profile: {Name} (Mode: {Mode})", name, mode);
        
        return profile;
    }
    
    public async Task<SettingsProfile> GetProfileAsync(string name)
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
            
            return profile;
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task<IEnumerable<SettingsProfile>> GetAllProfilesAsync()
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
        
        return profiles;
    }
    
    public async Task UpdateProfileAsync(SettingsProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        
        // Validate before save
        var validation = await ValidateProfileAsync(profile);
        if (!validation.IsValid)
            throw new ValidationException($"Profile validation failed: {validation.ErrorMessage}");
        
        // Update metadata
        profile.LastModified = DateTime.UtcNow;
        
        // Save
        await SaveProfileAsync(profile);
        
        _logger.LogInformation("Updated settings profile: {Name}", profile.Name);
    }
    
    public async Task DeleteProfileAsync(string name)
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
            _logger.LogInformation("Deleted settings profile: {Name}", name);
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task<SettingsProfile> GetActiveProfileAsync()
    {
        var name = await GetActiveProfileNameAsync();
        return await GetProfileAsync(name);
    }
    
    public async Task SetActiveProfileAsync(string name)
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
    
    public async Task<SettingsProfile> GetDefaultProfileAsync(AlignmentMode mode)
    {
        var defaultName = GetDefaultProfileName(mode);
        return await GetProfileAsync(defaultName);
    }
    
    public async Task<ValidationResult> ValidateProfileAsync(SettingsProfile profile)
    {
        var errors = new List<string>();
        
        // Validate name
        if (string.IsNullOrWhiteSpace(profile.Name))
            errors.Add("Profile name is required");
        
        // Validate alignment mode
        if (!Enum.IsDefined(typeof(AlignmentMode), profile.AlignmentMode))
            errors.Add($"Invalid alignment mode: {profile.AlignmentMode}");
        
        // Validate coordinates
        if (profile.Settings.Latitude < -90 || profile.Settings.Latitude > 90)
            errors.Add($"Invalid latitude: {profile.Settings.Latitude}");
        
        if (profile.Settings.Longitude < -180 || profile.Settings.Longitude > 180)
            errors.Add($"Invalid longitude: {profile.Settings.Longitude}");
        
        // Validate mount type
        if (string.IsNullOrWhiteSpace(profile.Settings.Mount))
            errors.Add("Mount type is required");
        
        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(string.Join("; ", errors));
    }
    
    public async Task<string> ExportProfileAsync(string name, string destinationPath)
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
    
    public async Task<SettingsProfile> ImportProfileAsync(string sourcePath)
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
        }
        
        // Save imported profile
        await SaveProfileAsync(profile);
        
        _logger.LogInformation("Imported profile '{Name}' from {Path}", profile.Name, sourcePath);
        
        return profile;
    }
    
    // Private helper methods
    
    private void InitializeDefaultProfiles()
    {
        // Create default profiles for each alignment mode
        foreach (AlignmentMode mode in Enum.GetValues(typeof(AlignmentMode)))
        {
            var defaultName = GetDefaultProfileName(mode);
            var filePath = GetProfilePath(defaultName);
            
            if (!File.Exists(filePath))
            {
                var profile = CreateDefaultProfile(defaultName, mode);
                SaveProfileAsync(profile).Wait();
                
                _logger.LogInformation("Created default profile: {Name}", defaultName);
            }
        }
    }
    
    private SettingsProfile CreateDefaultProfile(string name, AlignmentMode mode)
    {
        // NEW: Load settings from templates instead of hard-coding
        var settings = _templateService.LoadTemplateAsync(mode).Result;
        
        return new SettingsProfile
        {
            Name = name,
            DisplayName = GetDefaultDisplayName(mode),
            Description = settings.Description ?? $"Default settings for {mode} mounts",
            AlignmentMode = mode,
            Settings = settings,
            IsReadOnly = IsDefaultProfile(name),
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
        }
        finally
        {
            _fileLock.Release();
        }
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
    
    private async Task<bool> ProfileExistsAsync(string name)
    {
        var filePath = GetProfilePath(name);
        return File.Exists(filePath);
    }
    
    private string GetProfilePath(string name)
    {
        var fileName = $"{name}.json";
        return Path.Combine(_profilesPath, fileName);
    }
    
    private bool IsDefaultProfile(string name)
    {
        return name.StartsWith("default-", StringComparison.OrdinalIgnoreCase);
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
}
```

---

## Template System Benefits

### **1. No Hard-Coded Defaults** ?
- All defaults in JSON templates
- Easy to modify without recompiling
- Version-controlled templates

### **2. Flexible Configuration** ?
- Add new alignment modes by adding template file
- Modify existing modes by editing template
- Common settings maintained in one place

### **3. Maintainability** ?
- Clear separation: common vs. mode-specific
- Easy to see what differs between modes
- Changes don't require C# code updates

### **4. User Customization** ?
- Users can modify templates (advanced)
- Copy templates to create custom defaults
- Export/import template sets

---

## Implementation Plan

### **Phase 1: Template System** (Week 1)

| Task | Effort | Priority |
|------|--------|----------|
| Create `ISettingsTemplateService` interface | 2 hours | ?? HIGH |
| Implement `SettingsTemplateService` | 8 hours | ?? HIGH |
| Create common.json template | 2 hours | ?? HIGH |
| Create mode-specific override templates | 3 hours | ?? HIGH |
| Implement template merge logic | 4 hours | ?? HIGH |
| Unit tests for template service | 4 hours | ?? HIGH |
| **Total** | **23 hours** | |

---

### **Phase 2: Profile Service** (Week 1-2)

| Task | Effort | Priority |
|------|--------|----------|
| Update `SettingsProfileService` to use templates | 3 hours | ?? HIGH |
| Create `SettingsProfile` model | 1 hour | ?? HIGH |
| Implement CRUD operations | 5 hours | ?? HIGH |
| Add validation logic | 2 hours | ?? MEDIUM |
| Unit tests for profile service | 4 hours | ?? HIGH |
| **Total** | **15 hours** | |

---

### **Phase 3: UI Components** (Week 2)

| Task | Effort | Priority |
|------|--------|----------|
| Create `SettingsProfiles.razor` page | 6 hours | ?? HIGH |
| Create `ProfileEditor.razor` page | 8 hours | ?? HIGH |
| Add profile selection to telescope creation | 4 hours | ?? HIGH |
| Implement import/export dialogs | 4 hours | ?? MEDIUM |
| Add confirmation dialogs | 2 hours | ?? MEDIUM |
| UI testing | 4 hours | ?? HIGH |
| **Total** | **28 hours** | |

---

### **Phase 4: Integration** (Week 3)

| Task | Effort | Priority |
|------|--------|----------|
| Update `Program.cs` for profile-based init | 3 hours | ?? HIGH |
| Migrate existing `appsettings.user.json` to profile | 4 hours | ?? HIGH |
| Update DI configuration | 2 hours | ?? HIGH |
| Integration testing | 6 hours | ?? HIGH |
| Documentation | 3 hours | ?? MEDIUM |
| **Total** | **18 hours** | |

---

### **Total Estimated Effort: 84 hours (~10.5 working days)**

---

## Migration Strategy

### **Migrating Existing Settings**

```csharp
public class SettingsMigrationService
{
    private readonly ISettingsProfileService _profileService;
    private readonly IVersionedSettingsService _legacyService;
    
    public async Task MigrateFromLegacyAsync()
    {
        // Check if legacy file exists
        var legacyPath = _legacyService.UserSettingsPath;
        if (!File.Exists(legacyPath))
            return; // Nothing to migrate
        
        // Load legacy settings
        var legacySettings = _legacyService.GetSettings();
        
        // Determine alignment mode
        var mode = Enum.Parse<AlignmentMode>(legacySettings.AlignmentMode);
        
        // Create profile from legacy settings
        var profileName = "migrated-settings";
        var profile = await _profileService.CreateProfileAsync(profileName, mode);
        
        // Copy all settings
        profile.Settings = legacySettings;
        profile.Description = "Migrated from legacy appsettings.user.json";
        
        // Save
        await _profileService.UpdateProfileAsync(profile);
        
        // Set as active
        await _profileService.SetActiveProfileAsync(profileName);
        
        // Rename legacy file (don't delete, keep as backup)
        var backupPath = legacyPath + ".bak";
        File.Move(legacyPath, backupPath);
        
        Logger.LogInformation("Migrated legacy settings to profile: {Name}", profileName);
    }
}
```

---

## Testing Strategy

### **Unit Tests**

```csharp
[TestClass]
public class SettingsProfileServiceTests
{
    [TestMethod]
    public async Task CreateProfile_WithValidData_CreatesProfile()
    {
        // Arrange
        var service = CreateService();
        
        // Act
        var profile = await service.CreateProfileAsync("test-mount", AlignmentMode.GermanPolar);
        
        // Assert
        Assert.IsNotNull(profile);
        Assert.AreEqual("test-mount", profile.Name);
        Assert.AreEqual(AlignmentMode.GermanPolar, profile.AlignmentMode);
        Assert.AreEqual(0.0, profile.Settings.HomeAxisX);
        Assert.AreEqual(90.0, profile.Settings.HomeAxisY);
    }
    
    [TestMethod]
    public async Task CreateProfile_WithCopyFrom_ClonesSettings()
    {
        // Arrange
        var service = CreateService();
        await service.CreateProfileAsync("source", AlignmentMode.Polar);
        
        // Act
        var cloned = await service.CreateProfileAsync("cloned", AlignmentMode.Polar, "source");
        
        // Assert
        Assert.AreEqual("cloned", cloned.Name);
        // Verify settings copied
    }
    
    [TestMethod]
    public async Task DeleteProfile_DefaultProfile_ThrowsException()
    {
        // Arrange
        var service = CreateService();
        
        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.DeleteProfileAsync("default-germanpolar"));
    }
}
```

---

## Next Steps

### **Immediate Actions** (Day 1)

1. ? Review this analysis document
2. ? Create `ISettingsProfileService` interface
3. ? Create `SettingsProfile` model
4. ? Create `profiles/` directory structure

### **Short Term** (Week 1)

5. ? Implement `SettingsProfileService`
6. ? Generate default profiles for each alignment mode
7. ? Unit test profile CRUD operations

### **Medium Term** (Weeks 2-3)

8. ? Create Blazor UI pages
9. ? Update `Program.cs` for profile-based initialization
10. ? Migrate existing settings
11. ? Integration testing

---

## Conclusion

### **Benefits of Profile Management**

1. ? **Multi-Mount Support** - Easily switch between mount configurations
2. ? **Quick Setup** - Default profiles for each alignment mode
3. ? **Easy Backup** - Export/import profiles
4. ? **No Code Changes** - Telescope/Mount code remains unchanged
5. ? **User-Friendly** - Intuitive UI for profile management

### **Effort Summary**

- **Backend**: 23 hours
- **Profile Service**: 15 hours
- **UI**: 28 hours
- **Integration**: 18 hours
- **Total**: 84 hours (~10.5 working days)

### **Risk Assessment**

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Migration fails for existing users | Low | High | Keep legacy file as backup |
| Profile corruption | Low | Medium | Validation before save, export for backup |
| Performance degradation | Very Low | Low | File I/O is fast, profiles are small |

---

## References

- **Current Settings System**: `GreenSwamp.Alpaca.Settings/Services/VersionedSettingsService.cs`
- **Settings Model**: `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs`
- **AlignmentMode Usage**: `GreenSwamp.Alpaca.MountControl/Axes.cs`
- **Related**: [Multi-Telescope Architecture Analysis](Multi-Telescope-Architecture-Analysis.md)

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-XX  
**Status**: Planning / Awaiting Approval  
**Author**: AI Architecture Assistant
