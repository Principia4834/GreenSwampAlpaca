# Device Management User Guide

**GreenSwamp Alpaca Multi-Device Telescope Control**  
**Version:** 1.0.0 (Phase 3 Baseline)  
**Date:** 2026-03-25

---

## Overview

GreenSwamp Alpaca supports multiple telescope devices with independent configurations. This guide explains how to add, edit, and remove devices using the settings file.

**Current Implementation:** Manual editing of `appsettings.user.json` (full UI coming in future release)

---

## 1. Understanding Device Configuration

### Settings File Location

Your device configurations are stored in:

```
%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json
```

**Example:** `C:\Users\YourName\AppData\Roaming\GreenSwampAlpaca\1.0.0\appsettings.user.json`

### Two Synchronized Arrays

The settings file contains **two arrays that must stay in sync**:

1. **`Devices`** array - Complete device configurations (all 113 properties)
2. **`AlpacaDevices`** array - Discovery metadata (4 properties)

**⚠️ CRITICAL:** Device numbers must match between arrays. The DeviceSynchronizationService validates this at startup.

---

## 2. Default Configuration

On first launch, the application creates a default configuration:

```json
{
  "Version": "1.0.0",
  "CreatedDate": "2026-03-25T12:00:00Z",
  "LastModified": "2026-03-25T12:00:00Z",

  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    }
  ],

  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "Enabled": true,
      "Mount": "Simulator",
      "AlignmentMode": "GermanPolar",
      // ...110 more properties...
    }
  ]
}
```

---

## 3. Adding a New Device

### Step 1: Copy Default Configuration

1. Open `appsettings.user.json` in a text editor (Visual Studio Code recommended)
2. Copy the entire default `Devices[0]` object
3. Paste it at the end of the `Devices` array (before the closing `]`)
4. Add a comma after the previous device object

### Step 2: Update Device Number and Name

Change these **required** properties:

```json
{
  "DeviceNumber": 1,  // Must be unique (0, 1, 2, 3, ...)
  "DeviceName": "My Mount (AltAz)",  // Descriptive name
  "Enabled": true,  // Set to false to disable without deleting
```

### Step 3: Update Mount-Specific Settings

**For AltAz Mount:**
```json
  "AlignmentMode": "AltAz",
  "Mount": "Simulator",  // or "SkyWatcher"
  "Port": "COM3",
  "BaudRate": 115200,
```

**For German Polar Mount:**
```json
  "AlignmentMode": "GermanPolar",
  "Mount": "SkyWatcher",
  "Port": "COM1",
  "BaudRate": 9600,
```

**For Polar Mount:**
```json
  "AlignmentMode": "Polar",
  "Mount": "SkyWatcher",
  "Port": "COM2",
  "BaudRate": 9600,
```

### Step 4: Update Location Settings

```json
  "Latitude": 51.21135,    // Your observatory latitude
  "Longitude": -1.459816,  // Your observatory longitude
  "Elevation": 10.0,       // Meters above sea level
  "UTCOffset": "00:00:00", // Time zone offset
```

### Step 5: Add AlpacaDevices Entry

Add a matching entry to the `AlpacaDevices` array:

```json
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    },
    {
      "DeviceNumber": 1,  // Must match Devices[1].DeviceNumber
      "DeviceName": "My Mount (AltAz)",  // Must match Devices[1].DeviceName
      "DeviceType": "Telescope",  // Always "Telescope"
      "UniqueId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"  // Generate new GUID
    }
  ],
```

**Generate UniqueId:** Use [Online GUID Generator](https://www.guidgenerator.com/) or PowerShell:
```powershell
[guid]::NewGuid().ToString()
```

### Step 6: Save and Restart

1. Save `appsettings.user.json`
2. **Restart the application** (required - changes take effect at startup)
3. Navigate to `/device-manager` to verify new device appears

---

## 4. Editing an Existing Device

### Step 1: Locate Device

Find the device in the `Devices` array by its `DeviceNumber`:

```json
  "Devices": [
    {
      "DeviceNumber": 1,
      "DeviceName": "My Mount (AltAz)",
      // ...
    }
  ]
```

### Step 2: Modify Properties

Edit any property (except `DeviceNumber` - changing this creates a new device):

```json
  "Port": "COM5",           // Changed from COM3
  "BaudRate": 115200,       // Changed from 9600
  "Latitude": 40.7128,      // Changed location
  "Longitude": -74.0060,
  "Enabled": false,         // Disable without deleting
```

### Step 3: Synchronize DeviceName

If you changed `DeviceName` in `Devices`, update it in `AlpacaDevices` too:

```json
  "AlpacaDevices": [
    {
      "DeviceNumber": 1,
      "DeviceName": "Updated Name",  // Must match
      "DeviceType": "Telescope",
      "UniqueId": "..."
    }
  ]
```

### Step 4: Save and Restart

1. Save `appsettings.user.json`
2. **Restart the application**
3. Changes take effect on next connection

---

## 5. Removing a Device

### Step 1: Remove from Both Arrays

1. Delete the device object from `Devices` array (entire `{...}` block)
2. Delete the matching entry from `AlpacaDevices` array (by `DeviceNumber`)

**Before:**
```json
  "Devices": [
    { "DeviceNumber": 0, ... },
    { "DeviceNumber": 1, ... },  // DELETE THIS
    { "DeviceNumber": 2, ... }
  ],

  "AlpacaDevices": [
    { "DeviceNumber": 0, ... },
    { "DeviceNumber": 1, ... },  // DELETE THIS
    { "DeviceNumber": 2, ... }
  ]
```

**After:**
```json
  "Devices": [
    { "DeviceNumber": 0, ... },
    { "DeviceNumber": 2, ... }
  ],

  "AlpacaDevices": [
    { "DeviceNumber": 0, ... },
    { "DeviceNumber": 2, ... }
  ]
```

### Step 2: Fix JSON Syntax

- Remove trailing comma after last array item
- Ensure matching braces/brackets
- Validate JSON with online tool if unsure

### Step 3: Save and Restart

1. Save `appsettings.user.json`
2. **Restart the application**
3. Device will no longer appear in `/device-manager`

---

## 6. Cloning a Device

To create a new device based on an existing one:

### Step 1: Copy Existing Device

1. Copy the entire device object from `Devices` array
2. Paste it at the end of the array

### Step 2: Update Key Properties

```json
  {
    "DeviceNumber": 3,  // NEW unique number
    "DeviceName": "Clone of Device 1",  // NEW name
    "Enabled": true,
    // ... all other properties copied ...
  }
```

### Step 3: Update Mount-Specific Settings

Change properties that differ:
- `Port` (different COM port)
- `Latitude/Longitude` (different observatory)
- `AlignmentMode` (different mount type)

### Step 4: Add AlpacaDevices Entry

```json
  {
    "DeviceNumber": 3,  // Must match
    "DeviceName": "Clone of Device 1",  // Must match
    "DeviceType": "Telescope",
    "UniqueId": "NEW-GUID-HERE"  // Generate new GUID
  }
```

### Step 5: Save and Restart

---

## 7. Disabling a Device Temporarily

To disable a device without deleting it:

### Option 1: Set Enabled to False

```json
  {
    "DeviceNumber": 1,
    "DeviceName": "My Mount",
    "Enabled": false,  // Device won't load at startup
    // ... other properties unchanged ...
  }
```

- Device remains in configuration
- Won't appear in MountInstanceRegistry
- Can re-enable by setting `Enabled: true` and restarting

### Option 2: Keep AlpacaDevices Entry

You can leave the `AlpacaDevices` entry even when device is disabled. The synchronization service will validate but won't fail on disabled devices.

---

## 8. Validation and Error Handling

### Automatic Validation at Startup

The **DeviceSynchronizationService** validates:

1. ✅ Every `Devices` entry has matching `AlpacaDevices` entry (by `DeviceNumber`)
2. ✅ Every `AlpacaDevices` entry has matching `Devices` entry
3. ✅ `DeviceName` matches between arrays
4. ✅ No duplicate `DeviceNumber` values

### If Validation Fails

**Error Message:**
```
AlpacaDevices/Devices arrays out of sync in 'path\to\appsettings.user.json'.
Please check the console log for details or delete this file to regenerate.
```

**Console Output:**
```
❌ [DeviceSynchronizationService] AlpacaDevices/Devices arrays out of sync.
Only in Devices: [1, 3], Only in Alpaca: [2]
```

**Resolution:**
1. Check console log for specific device numbers
2. Add missing entries or remove extra ones
3. Ensure `DeviceNumber` and `DeviceName` match
4. Restart application

### Common Errors

**❌ JSON Syntax Error:**
```
Unexpected token at line 45, column 10
```
- **Fix:** Use JSON validator (e.g., jsonlint.com)
- Check for missing commas, brackets, quotes

**❌ Duplicate DeviceNumber:**
```
Device number 1 already exists
```
- **Fix:** Each device must have unique `DeviceNumber`

**❌ Missing DeviceName:**
```
Settings file has an empty 'Devices' array
```
- **Fix:** Ensure at least one device exists in array

---

## 9. Best Practices

### 1. Backup Before Editing

```powershell
Copy-Item "$env:APPDATA\GreenSwampAlpaca\1.0.0\appsettings.user.json" `
          "$env:APPDATA\GreenSwampAlpaca\1.0.0\appsettings.user.json.backup"
```

### 2. Use Version Control

Consider keeping your settings file in Git:

```powershell
cd $env:APPDATA\GreenSwampAlpaca
git init
git add 1.0.0\appsettings.user.json
git commit -m "Initial telescope configuration"
```

### 3. Document Your Devices

Add comments (if your JSON editor supports them) or use meaningful `DeviceName` values:

```json
  "DeviceName": "Pier East - Imaging Scope (GEM)",
  "DeviceName": "Pier West - Visual Scope (AltAz)",
```

### 4. Test with Simulator First

Before configuring a physical mount:
1. Test with `"Mount": "Simulator"` first
2. Verify connection in `/device-manager`
3. Then switch to `"Mount": "SkyWatcher"` and update port settings

### 5. Keep Default Device

Keep Device 0 as a working simulator:
```json
  {
    "DeviceNumber": 0,
    "DeviceName": "Simulator (GEM)",
    "Enabled": true,
    "Mount": "Simulator"
  }
```

This ensures you always have a testable device.

---

## 10. Property Reference

### Required Properties (Must Set)

| Property | Type | Example | Description |
|----------|------|---------|-------------|
| `DeviceNumber` | int | `0` | Unique device identifier |
| `DeviceName` | string | `"My Mount"` | Display name |
| `Enabled` | bool | `true` | Load at startup? |
| `Mount` | string | `"Simulator"` | `"Simulator"` or `"SkyWatcher"` |
| `AlignmentMode` | string | `"GermanPolar"` | `"AltAz"`, `"Polar"`, or `"GermanPolar"` |
| `Port` | string | `"COM1"` | Serial port |
| `BaudRate` | int | `9600` | Serial baud rate |

### Location Properties

| Property | Type | Range | Description |
|----------|------|-------|-------------|
| `Latitude` | double | -90 to 90 | Degrees north (positive) or south (negative) |
| `Longitude` | double | -180 to 180 | Degrees east (positive) or west (negative) |
| `Elevation` | double | -500 to 9000 | Meters above sea level |
| `UTCOffset` | string | `"00:00:00"` | Time zone offset (hh:mm:ss) |

### Optics Properties

| Property | Type | Example | Description |
|----------|------|---------|-------------|
| `ApertureDiameter` | double | `0.2` | Meters (200mm) |
| `ApertureArea` | double | `0.0314` | Square meters (π × r²) |
| `FocalLength` | double | `1.26` | Meters (1260mm) |

### Capability Properties (Simulator)

For `"Mount": "Simulator"`, set all `Can*` properties to `true`:

```json
  "CanAlignMode": true,
  "CanAltAz": true,
  "CanDoesRefraction": true,
  "CanEquatorial": true,
  "CanFindHome": true,
  "CanLatLongElev": true,
  "CanOptics": true,
  "CanPark": true,
  "CanPierSide": true,
  "CanPulseGuide": true,
  "CanSetEquRates": true,
  "CanSetGuideRates": true,
  "CanSetPark": true,
  "CanSetPierSide": true,
  "CanSetTracking": true,
  "CanSiderealTime": true,
  "CanSlew": true,
  "CanSlewAltAz": true,
  "CanSlewAltAzAsync": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanSyncAltAz": true,
  "CanUnpark": true,
  "CanTrackingRates": true,
  "CanSetDeclinationRate": true,
  "CanSetRightAscensionRate": true
```

For physical mounts, set based on actual hardware capabilities.

### Complete Property List

See `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs` for all 113 properties with descriptions and data types.

---

## 11. Troubleshooting

### Application Won't Start

**Symptom:** Application crashes at startup with settings error

**Solution:**
1. Delete `appsettings.user.json`
2. Restart application (creates new default file)
3. Re-add your devices one at a time

### Device Not Appearing in List

**Check:**
1. ✅ `Enabled: true` in `Devices` array
2. ✅ Entry exists in both `Devices` and `AlpacaDevices` arrays
3. ✅ `DeviceNumber` matches between arrays
4. ✅ JSON syntax is valid (no trailing commas, etc.)
5. ✅ Application was restarted after editing

### Device Won't Connect

**Check:**
1. ✅ Correct `Port` (e.g., `"COM1"`)
2. ✅ Correct `BaudRate` (9600 for most SkyWatcher mounts)
3. ✅ Mount hardware is powered on
4. ✅ No other application is using the serial port
5. ✅ `Mount` property matches hardware (`"SkyWatcher"` not `"Simulator"`)

### Validation Error at Startup

**Symptom:** Console shows "AlpacaDevices/Devices arrays out of sync"

**Solution:**
1. Check console log for specific device numbers mentioned
2. Compare `Devices` and `AlpacaDevices` arrays
3. Ensure every device number appears in both arrays
4. Ensure `DeviceName` matches between arrays
5. Fix mismatches and restart

---

## 12. Future Enhancements

**Planned for Future Release:**

- ✨ **Web UI for device management** - Add/edit/delete devices via DeviceManager page
- ✨ **Template-based creation** - Create devices from AltAz/GermanPolar/Polar templates
- ✨ **Clone device** - Duplicate existing device with one click
- ✨ **Validation in UI** - Real-time validation before saving
- ✨ **No restart required** - Hot-reload device configuration changes

**Current Status:** Manual editing workflow is fully functional and validated. UI implementation deferred to dedicated development session.

---

## 13. Example Configurations

### Example 1: Single Simulator (Default)

```json
{
  "Version": "1.0.0",
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "Enabled": true,
      "Mount": "Simulator",
      "AlignmentMode": "GermanPolar",
      "Latitude": 51.21135,
      "Longitude": -1.459816,
      "Elevation": 10.0
      // ...110 more properties...
    }
  ]
}
```

### Example 2: Two Physical Mounts

```json
{
  "Version": "1.0.0",
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Imaging Mount (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789abc"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Visual Mount (AltAz)",
      "DeviceType": "Telescope",
      "UniqueId": "87654321-4321-4321-4321-cba987654321"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Imaging Mount (GEM)",
      "Enabled": true,
      "Mount": "SkyWatcher",
      "AlignmentMode": "GermanPolar",
      "Port": "COM1",
      "BaudRate": 9600,
      "Latitude": 34.0522,
      "Longitude": -118.2437,
      "Elevation": 100.0
      // ...
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Visual Mount (AltAz)",
      "Enabled": true,
      "Mount": "SkyWatcher",
      "AlignmentMode": "AltAz",
      "Port": "COM3",
      "BaudRate": 115200,
      "Latitude": 34.0522,
      "Longitude": -118.2437,
      "Elevation": 100.0
      // ...
    }
  ]
}
```

### Example 3: Simulator + Physical Mount

```json
{
  "Version": "1.0.0",
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (Testing)",
      "DeviceType": "Telescope",
      "UniqueId": "sim-00000000-0000-0000-0000-000000000000"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "SkyWatcher EQ6-R Pro",
      "DeviceType": "Telescope",
      "UniqueId": "eq6r-11111111-1111-1111-1111-111111111111"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (Testing)",
      "Enabled": true,
      "Mount": "Simulator",
      "AlignmentMode": "GermanPolar"
      // ...
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "SkyWatcher EQ6-R Pro",
      "Enabled": true,
      "Mount": "SkyWatcher",
      "AlignmentMode": "GermanPolar",
      "Port": "COM1",
      "BaudRate": 9600
      // ...
    }
  ]
}
```

---

## 14. Getting Help

**Documentation:**
- Requirements: `docs/Settings-System-Requirements.md`
- High-Level Design: `docs/Settings-System-HighLevel-Design.md`
- This Guide: `docs/Device-Management-User-Guide.md`

**Code References:**
- SkySettings Model: `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs`
- Validation Service: `GreenSwamp.Alpaca.Settings/Services/DeviceSynchronizationService.cs`
- UI Page: `GreenSwamp.Alpaca.Server/Pages/DeviceManager.razor`

**Community:**
- GitHub Issues: [Report Bug or Request Feature](https://github.com/Principia4834/GreenSwampAlpaca/issues)
- Discussions: [Ask Questions](https://github.com/Principia4834/GreenSwampAlpaca/discussions)

---

## 15. Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-03-25 | Initial release - Phase 1 (Core Services) + Phase 2 (File Structure) complete |

---

**End of User Guide**
