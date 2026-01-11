# Monitor Settings Migration Summary

## Changes Made: 2026-01-11

### Overview
Removed hardcoded monitor logging settings and migrated to JSON-based configuration for better flexibility and maintainability.

---

## Files Changed

### 1. `GreenSwamp.Alpaca.Shared/Monitor.cs`
**Change:** Removed hardcoded initialization

**Before:**
```csharp
public static void Load_Settings()
{
    // ... load from Settings.cs ...
    
    // AWW ToDo remove hard coding - Temporary to enable monitor logging for profile debugging
    //MonitorDevice
    DevicesToMonitor(MonitorDevice.Server, true);
    DevicesToMonitor(MonitorDevice.Telescope, true);
    //MonitorCategory
    CategoriesToMonitor(MonitorCategory.Driver, true);
    CategoriesToMonitor(MonitorCategory.Interface, true);
    CategoriesToMonitor(MonitorCategory.Server, true);
    CategoriesToMonitor(MonitorCategory.Mount, true);
    //MonitorType
    TypesToMonitor(MonitorType.Information, true);
    TypesToMonitor(MonitorType.Warning, true);
    TypesToMonitor(MonitorType.Error, true);
    
    // Enable file logging for GSMonitorLog
    Settings.LogMonitor = true;
    Settings.StartMonitor = true;
}
```

**After:**
```csharp
public static void Load_Settings()
{
    //MonitorDevice
    if (Settings.ServerDevice){DevicesToMonitor(MonitorDevice.Server, Settings.ServerDevice);}
    if (Settings.Telescope){DevicesToMonitor(MonitorDevice.Telescope, Settings.Telescope);}
    if (Settings.Ui){DevicesToMonitor(MonitorDevice.Ui, Settings.Ui);}
    //MonitorCategory
    if (Settings.Other){CategoriesToMonitor(MonitorCategory.Other, Settings.Other);}
    if (Settings.Driver){CategoriesToMonitor(MonitorCategory.Driver, Settings.Driver);}
    if (Settings.Interface){CategoriesToMonitor(MonitorCategory.Interface, Settings.Interface);}
    if (Settings.Server){CategoriesToMonitor(MonitorCategory.Server, Settings.Server);}
    if (Settings.Mount){CategoriesToMonitor(MonitorCategory.Mount, Settings.Mount);}
    if (Settings.Alignment){CategoriesToMonitor(MonitorCategory.Alignment, Settings.Alignment);}
    //MonitorType
    if (Settings.Information){TypesToMonitor(MonitorType.Information, Settings.Information);}
    if (Settings.Data){TypesToMonitor(MonitorType.Data, Settings.Data);}
    if (Settings.Warning){TypesToMonitor(MonitorType.Warning, Settings.Warning);}
    if (Settings.Error){TypesToMonitor(MonitorType.Error, Settings.Error);}
    if (Settings.Debug){TypesToMonitor(MonitorType.Debug, Settings.Debug);}

    // Settings are loaded from Settings.cs which reads from JSON configuration
    // All monitor filters are configured via MonitorSettings section in appsettings.json
}
```

**Result:** All hardcoded values removed. Settings now controlled via JSON.

---

### 2. `GreenSwamp.Alpaca.Settings/Models/MonitorSettings.cs`
**Change:** Updated default values to be more development-friendly

**Before:**
```csharp
public bool LogMonitor { get; set; } = false;  // Off by default
public bool StartMonitor { get; set; } = false;  // Off by default
```

**After:**
```csharp
public bool LogMonitor { get; set; } = true;  // On by default for development
public bool StartMonitor { get; set; } = true;  // On by default
```

**Rationale:** Profile loading diagnostics require monitor logging to be enabled. Development-friendly defaults make troubleshooting easier.

---

### 3. `GreenSwamp.Alpaca.Server/appsettings.json`
**Change:** Added new `MonitorSettings` section

**Added Section:**
```json
{
  "SkySettings": {
    // ... existing settings ...
  },
  "MonitorSettings": {
    "ServerDevice": true,
    "Telescope": true,
    "Ui": false,
    "Other": false,
    "Driver": true,
    "Interface": true,
    "Server": true,
    "Mount": true,
    "Alignment": false,
    "Information": true,
    "Data": false,
    "Warning": true,
    "Error": true,
    "Debug": false,
    "LogMonitor": true,
    "LogSession": true,
    "LogCharting": false,
    "StartMonitor": true,
    "Language": "en-US",
    "LogPath": "0",
    "Version": "1.0.0"
  }
}
```

**Result:** Application-wide defaults now defined in configuration file.

---

### 4. **New File:** `GreenSwamp.Alpaca.Settings/docs/MONITOR_SETTINGS_GUIDE.md`
**Purpose:** Comprehensive documentation for monitor settings configuration

**Contents:**
- Configuration reference for all 21 settings
- Usage examples (development, production, troubleshooting)
- Performance considerations
- Troubleshooting guide
- Migration notes from hardcoded approach

---

## Benefits

### ✅ No More Hardcoding
- All logging configuration now in JSON
- No code changes required to adjust logging
- Easy to version control configurations

### ✅ User-Specific Overrides
Users can override defaults in their `appsettings.user.json`:
```json
{
  "MonitorSettings": {
    "Debug": true,
    "Data": true
  }
}
```

### ✅ Profile-Specific Settings
Profiles can include custom monitor configurations:
```json
// germanpolar-overrides.json
{
  "SkySettings": { "AlignmentMode": "GermanPolar" },
  "MonitorSettings": { "Alignment": true }
}
```

### ✅ Better Defaults for Development
- Profile loading visible in logs (ServerDevice + Mount + Information)
- Mount operations logged (Mount + Driver + Information)
- Errors always captured (Error + Warning)
- Performance-friendly (Data and Debug off by default)

### ✅ Production-Ready
Easy to create minimal logging configuration for production:
```json
{
  "MonitorSettings": {
    "LogMonitor": false,
    "Data": false,
    "Debug": false
  }
}
```

---

## Testing Performed

### ✅ Verified Settings Load Correctly
```
GSMonitorLog shows:
- LoadedFromProfile message (ServerDevice + Mount + Information enabled)
- AppliedSettings message (ServerDevice + Mount + Information enabled)
- No hardcoded overrides interfering with JSON settings
```

### ✅ Verified File Logging Works
```
Files created:
- GSSessionLog*.txt (LogSession = true)
- GSMonitorLog*.txt (StartMonitor = true, LogMonitor = true)
```

### ✅ Verified Backward Compatibility
- Existing `appsettings.user.json` files without `MonitorSettings` use defaults
- Application works with or without `MonitorSettings` section (falls back to model defaults)

---

## User Migration Required

### For Existing Installations

**Option 1: Use New Defaults (Recommended)**
- No action required
- Application will use defaults from `MonitorSettings.cs`
- User can customize later if needed

**Option 2: Preserve Custom Settings**
If users had custom hardcoded settings before:

1. Add `MonitorSettings` section to their `appsettings.user.json`:
```json
{
  "SkySettings": { /* existing */ },
  "MonitorSettings": {
    "StartMonitor": true,
    "LogMonitor": true,
    "Mount": true,
    "Server": true,
    "Information": true,
    "Warning": true,
    "Error": true
  }
}
```

2. Restart application

---

## Configuration Examples

### Example 1: Minimal Logging (Production)
```json
{
  "MonitorSettings": {
    "ServerDevice": true,
    "Telescope": true,
    "Server": true,
    "Mount": false,
    "Driver": false,
    "Information": true,
    "Warning": true,
    "Error": true,
    "LogMonitor": false,
    "LogSession": true,
    "StartMonitor": true
  }
}
```
**Result:** Only session log with errors/warnings. No GSMonitorLog file.

---

### Example 2: Debug Everything (Development)
```json
{
  "MonitorSettings": {
    "ServerDevice": true,
    "Telescope": true,
    "Ui": true,
    "Other": true,
    "Driver": true,
    "Interface": true,
    "Server": true,
    "Mount": true,
    "Alignment": true,
    "Information": true,
    "Data": true,
    "Warning": true,
    "Error": true,
    "Debug": true,
    "LogMonitor": true,
    "LogSession": true,
    "StartMonitor": true
  }
}
```
**Result:** Everything logged. Large log files. Use only for troubleshooting.

---

### Example 3: Mount Operations Only
```json
{
  "MonitorSettings": {
    "ServerDevice": false,
    "Telescope": true,
    "Mount": true,
    "Driver": true,
    "Information": true,
    "Warning": true,
    "Error": true,
    "LogMonitor": true,
    "LogSession": true,
    "StartMonitor": true
  }
}
```
**Result:** Focus on mount commands and operations. Good for mount debugging.

---

## Next Steps (Optional)

### Future Enhancement: UI for Monitor Settings

Create a Blazor component for managing monitor settings:

```
GreenSwamp.Alpaca.Server/Pages/MonitorSettings.razor
```

**Features:**
- Checkboxes for each Device/Category/Type filter
- Toggle for LogMonitor/LogSession
- "Save" button to persist to appsettings.user.json
- "Reset to Defaults" button
- Real-time preview of what will be logged

**Implementation Status:** Not implemented (JSON configuration sufficient for now)

---

## Summary

✅ **Hardcoded settings removed** - All configuration via JSON  
✅ **User-friendly defaults** - Works out of the box for development  
✅ **Documented** - Comprehensive guide in MONITOR_SETTINGS_GUIDE.md  
✅ **Tested** - Profile loading logs correctly captured  
✅ **Backward compatible** - Existing installations work without changes  
✅ **Production-ready** - Easy to minimize logging for production  

**Status:** ✅ **COMPLETE - Ready for production use**

---

**Migration Date:** 2026-01-11  
**Version:** 1.0.0  
**Breaking Changes:** None (backward compatible)
