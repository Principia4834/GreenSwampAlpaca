# Monitor Settings Configuration Guide

## Overview

The `MonitorSettings` section in `appsettings.json` controls what gets logged and displayed in the monitor window. This replaces the old hardcoded configuration.

---

## Configuration Location

### Application-Wide Defaults
**File:** `GreenSwamp.Alpaca.Server/appsettings.json`
```json
{
  "MonitorSettings": {
    // ... settings here ...
  }
}
```

### User-Specific Overrides
**File:** `%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json`
```json
{
  "MonitorSettings": {
    // ... user overrides ...
  }
}
```

---

## Settings Reference

### MonitorDevice Filters (Controls which devices are logged)

| Setting | Default | Description |
|---------|---------|-------------|
| `ServerDevice` | `true` | Server-related entries (core server operations) |
| `Telescope` | `true` | Telescope/ASCOM API entries |
| `Ui` | `false` | UI/View entries (disabled by default for performance) |

**Recommendation:** Keep `ServerDevice` and `Telescope` enabled. Enable `Ui` only when debugging UI issues.

---

### MonitorCategory Filters (Controls which categories are logged)

| Setting | Default | Description |
|---------|---------|-------------|
| `Other` | `false` | Support/shared project entries |
| `Driver` | `true` | Mount driver data (Simulator, SkyWatcher) |
| `Interface` | `true` | Interface-level operations |
| `Server` | `true` | Core server processes |
| `Mount` | `true` | Mount commands and operations |
| `Alignment` | `false` | Alignment model operations |

**Recommendation:** Enable categories relevant to your debugging needs. `Mount` is essential for telescope operations.

---

### MonitorType Filters (Controls which log levels are captured)

| Setting | Default | Description |
|---------|---------|-------------|
| `Information` | `true` | Informational messages (also in GSSessionLog) |
| `Data` | `false` | Detailed data entries (high volume) |
| `Warning` | `true` | Warning messages (also in GSSessionLog) |
| `Error` | `true` | Error messages (also in GSErrorLog and GSSessionLog) |
| `Debug` | `false` | Debug/troubleshooting entries (very high volume) |

**Recommendation:** Keep `Information`, `Warning`, and `Error` enabled. Enable `Data` or `Debug` only for specific troubleshooting.

---

### Logging Options (Controls file output)

| Setting | Default | Description |
|---------|---------|-------------|
| `LogMonitor` | `true` | Write to `GSMonitorLog*.txt` file |
| `LogSession` | `true` | Write to `GSSessionLog*.txt` file |
| `LogCharting` | `false` | Write charting data to file |
| `StartMonitor` | `true` | **REQUIRED:** Enable monitor system and file logging |

**⚠️ IMPORTANT:** `StartMonitor` must be `true` for `LogMonitor` to work!

---

### Miscellaneous Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Language` | `"en-US"` | UI language code |
| `LogPath` | `"0"` | Custom log path (`"0"` = default: `%USERPROFILE%\Documents\GSServer`) |
| `Version` | `"1.0.0"` | Settings version for migration tracking |

---

## Log File Locations

By default, logs are written to:
```
C:\Users\{YourName}\Documents\GSServer\
```

### Log Files Generated

| File | Content | Controlled By |
|------|---------|---------------|
| `GSSessionLog*.txt` | Information, Warning, Error entries | `LogSession` |
| `GSMonitorLog*.txt` | All enabled monitor entries | `LogMonitor` + `StartMonitor` |
| `GSErrorLog*.txt` | Error entries only | Always enabled |
| `GSChartingLog*.txt` | Charting data | `LogCharting` |

---

## Configuration Examples

### Example 1: Development Configuration (Maximum Logging)
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
    "LogCharting": true,
    "StartMonitor": true,
    "Language": "en-US",
    "LogPath": "0",
    "Version": "1.0.0"
  }
}
```

**Use Case:** Full debugging with all logging enabled. Will generate large log files.

---

### Example 2: Production Configuration (Minimal Logging)
```json
{
  "MonitorSettings": {
    "ServerDevice": true,
    "Telescope": true,
    "Ui": false,
    "Other": false,
    "Driver": false,
    "Interface": false,
    "Server": true,
    "Mount": true,
    "Alignment": false,
    "Information": true,
    "Data": false,
    "Warning": true,
    "Error": true,
    "Debug": false,
    "LogMonitor": false,
    "LogSession": true,
    "LogCharting": false,
    "StartMonitor": true,
    "Language": "en-US",
    "LogPath": "0",
    "Version": "1.0.0"
  }
}
```

**Use Case:** Production use with only essential logging. Reduced disk I/O and file sizes.

---

### Example 3: Troubleshooting Mount Issues
```json
{
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
    "Data": true,
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

**Use Case:** Debugging mount operations with detailed command logging.

---

### Example 4: Profile Loading Diagnostics
```json
{
  "MonitorSettings": {
    "ServerDevice": true,
    "Telescope": false,
    "Ui": false,
    "Other": false,
    "Driver": false,
    "Interface": false,
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

**Use Case:** Focused on settings/profile loading (Server + Mount categories).

---

## Changing Settings at Runtime

### Method 1: Edit appsettings.user.json (Recommended)
1. Stop the application
2. Edit `%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json`
3. Modify the `MonitorSettings` section
4. Restart the application

### Method 2: Via UI (If implemented)
- Navigate to Settings → Monitor Settings
- Toggle checkboxes for desired filters
- Click "Save" to persist changes

---

## Troubleshooting

### Issue: No logs appearing in GSMonitorLog file

**Solution:**
1. Check `StartMonitor = true`
2. Check `LogMonitor = true`
3. Verify at least one Device, Category, and Type filter is enabled
4. Check file permissions on log directory

---

### Issue: Too many log entries (large files)

**Solution:**
1. Disable `Data` and `Debug` types
2. Disable unnecessary categories (e.g., `Driver`, `Alignment`)
3. Set `LogMonitor = false` (keeps session log only)

---

### Issue: Missing specific log entries

**Solution:**
1. Check the entry's Device is enabled (e.g., `ServerDevice`)
2. Check the entry's Category is enabled (e.g., `Mount`)
3. Check the entry's Type is enabled (e.g., `Information`)
4. All three must be enabled for an entry to be logged

---

## Performance Considerations

### High-Volume Settings (May impact performance)
- `Data` type: Logs every command sent to mount
- `Debug` type: Logs detailed internal state
- `Ui` device: Logs every UI interaction
- `LogCharting = true`: Writes charting data continuously

**Recommendation:** Enable these only when actively debugging specific issues.

---

### Low-Impact Settings (Always safe)
- `Information` type
- `Warning` type
- `Error` type
- `Mount` category
- `Server` category
- `LogSession = true`

---

## Default Settings Summary

The default configuration is optimized for:
✅ Profile loading diagnostics
✅ Mount operation monitoring
✅ Server initialization tracking
✅ Error detection
✅ Reasonable file sizes

**Default Mode:** Development/Debugging with essential logging enabled

---

## Migration from Hardcoded Settings

### Before (Hardcoded in Monitor.cs)
```csharp
// Old hardcoded approach
DevicesToMonitor(MonitorDevice.Server, true);
Settings.LogMonitor = true;
Settings.StartMonitor = true;
```

### After (JSON Configuration)
```json
{
  "MonitorSettings": {
    "ServerDevice": true,
    "LogMonitor": true,
    "StartMonitor": true
  }
}
```

**Benefits:**
- ✅ No code changes needed to adjust logging
- ✅ User-specific overrides via appsettings.user.json
- ✅ Profile-specific configurations possible
- ✅ Easy to share configurations between installations

---

## Integration with Profiles

Monitor settings can be included in profile templates:

**File:** `GreenSwamp.Alpaca.Settings/Templates/germanpolar-overrides.json`
```json
{
  "SkySettings": {
    "AlignmentMode": "GermanPolar"
  },
  "MonitorSettings": {
    "Mount": true,
    "Alignment": true,
    "Data": true
  }
}
```

This allows profile-specific logging configurations (e.g., enable alignment logging only for polar profiles).

---

## Summary

- ✅ **All logging now controlled via JSON** - No more hardcoded settings
- ✅ **User-friendly defaults** - Works out of the box for development
- ✅ **Production-ready** - Easy to minimize logging for production
- ✅ **Flexible** - Granular control over what gets logged
- ✅ **Persistent** - Settings saved in appsettings.user.json
- ✅ **Profile-aware** - Can be customized per profile

---

**Last Updated:** 2026-01-11  
**Version:** 1.0.0
