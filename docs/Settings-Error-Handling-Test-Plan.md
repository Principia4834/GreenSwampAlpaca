# Settings Error Handling Test Plan
**Feature**: Settings Validation and Automatic Repair System  
**Version**: 1.0.0  
**Date**: 2025-01-26  
**Status**: Ready for Testing

---

## Executive Summary

This test plan validates the new Settings Error Handling System that ensures the GreenSwamp Alpaca application:
- **Never advertises broken devices** to ASCOM clients
- **Validates settings automatically** at startup and on-demand
- **Provides clear error reporting** via web UI
- **Enables automatic repair** of common configuration issues
- **Creates backups** before making changes
- **Continues running** even with invalid settings (graceful degradation)

---

## Test Environment Setup

### Prerequisites
- ✅ .NET 8 SDK installed
- ✅ Visual Studio 2026 or VS Code with C# extension
- ✅ GreenSwamp Alpaca solution built successfully
- ✅ Access to `%AppData%\GreenSwampAlpaca\{version}\` folder

### Settings File Location
- **Path**: `%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json`
- **Backup Location**: Same folder with `.backup.{timestamp}` suffix
- **To find your version**: Check application startup logs or Settings Health page

### Quick Setup Script
```powershell
# PowerShell script to prepare test environment
$appData = $env:APPDATA
$settingsRoot = "$appData\GreenSwampAlpaca"

# Find the version folder (most recent)
$versionFolder = Get-ChildItem $settingsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1

Write-Host "Settings folder: $($versionFolder.FullName)"
Write-Host "Settings file: $($versionFolder.FullName)\appsettings.user.json"

# Create test data folder
$testDataFolder = "$($versionFolder.FullName)\TestData"
New-Item -ItemType Directory -Force -Path $testDataFolder
Write-Host "Test data folder created: $testDataFolder"
```

---

## Test Data Files

Copy these JSON files into `%AppData%\GreenSwampAlpaca\{version}\TestData\` for testing.

### Test File 1: Valid Settings
**Filename**: `valid-settings.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM3",
      "BaudRate": 9600,
      "DataBits": 8,
      "Handshake": "None",
      "ReadTimeout": 5000,
      "DTREnable": false,
      "RTSEnable": false,
      "Latitude": 45.0,
      "Longitude": -122.0,
      "Elevation": 100,
      "UTCOffset": "00:00:00",
      "AutoTrack": true,
      "AlignmentMode": "GermanPolar",
      "EquatorialCoordinateType": "J2000",
      "AtPark": false,
      "ApertureDiameter": 0.2,
      "ApertureArea": 0.0314,
      "FocalLength": 1.0,
      "Refraction": true,
      "Temperature": 15.0,
      "CustomGearing": false,
      "CustomRa360Steps": 0,
      "CustomRaWormTeeth": 0,
      "CustomDec360Steps": 0,
      "CustomDecWormTeeth": 0,
      "CustomRaTrackingOffset": 0,
      "CustomDecTrackingOffset": 0,
      "RaBacklash": 0,
      "DecBacklash": 0,
      "HcPulseGuides": [],
      "MinPulseRa": 0,
      "MinPulseDec": 0,
      "DecPulseToGoTo": false,
      "St4Guiderate": 50,
      "GuideRateOffsetX": 0.0,
      "GuideRateOffsetY": 0.0,
      "TrackingRate": "Sidereal",
      "SiderealRate": 15.041,
      "LunarRate": 14.685,
      "SolarRate": 15.0,
      "KingRate": 15.037,
      "RATrackingOffset": 0,
      "CustomTrackingRate": 0.0,
      "CustomTrackingOffsetRate": 0.0,
      "CustomTrackingOffsetDec": 0.0,
      "CustomTrackingRateRa": 0.0,
      "DeclinationFlip": false,
      "PPECOn": false,
      "PPECFile": "",
      "UseDecPulse": false,
      "GuideRateDec": 0.0,
      "GuideRateRa": 0.0,
      "Precision": "Precision1",
      "CanAxisRates": true,
      "CanDestinationSideOfPier": true,
      "CanDoesRefraction": true,
      "CanAlignMode": true,
      "CanMoveAxis": true,
      "CanAltAz": true,
      "CanDateTime": true,
      "CanDeclinationRate": true,
      "CanDualAxisPulseGuide": true,
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
      "CanSetPierSide": false,
      "CanSetTracking": true,
      "CanSiderealTime": true,
      "CanSlew": true,
      "CanSlewAltAz": true,
      "CanSlewAltAzAsync": true,
      "CanSlewAsync": true,
      "CanSync": true,
      "CanSyncAltAz": true,
      "CanUnpark": true,
      "CanTrackingRates": true
    }
  ],
  "Version": "1.0.0",
  "CreatedDate": "2025-01-26T00:00:00Z"
}
```

---

### Test File 2: Missing Devices Array
**Filename**: `missing-devices-array.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Version": "1.0.0",
  "CreatedDate": "2025-01-26T00:00:00Z"
}
```

**Expected Error**: `MISSING_DEVICES_ARRAY`  
**Auto-repairable**: Yes

---

### Test File 3: Invalid AlignmentMode
**Filename**: `invalid-alignment-mode.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM3",
      "BaudRate": 9600,
      "DataBits": 8,
      "Handshake": "None",
      "ReadTimeout": 5000,
      "DTREnable": false,
      "RTSEnable": false,
      "Latitude": 45.0,
      "Longitude": -122.0,
      "Elevation": 100,
      "UTCOffset": "00:00:00",
      "AutoTrack": true,
      "AlignmentMode": "InvalidMode",
      "EquatorialCoordinateType": "J2000",
      "AtPark": false,
      "ApertureDiameter": 0.2,
      "ApertureArea": 0.0314,
      "FocalLength": 1.0,
      "Refraction": true,
      "Temperature": 15.0,
      "CustomGearing": false,
      "RaBacklash": 0,
      "DecBacklash": 0,
      "HcPulseGuides": [],
      "MinPulseRa": 0,
      "MinPulseDec": 0,
      "TrackingRate": "Sidereal",
      "SiderealRate": 15.041
    }
  ],
  "Version": "1.0.0"
}
```

**Expected Error**: `INVALID_ALIGNMENT_MODE`  
**Device**: 0  
**Auto-repairable**: Yes (resets to GermanPolar)

---

### Test File 4: Missing DeviceName
**Filename**: `missing-device-name.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM3",
      "BaudRate": 9600,
      "DataBits": 8,
      "Handshake": "None",
      "ReadTimeout": 5000,
      "DTREnable": false,
      "RTSEnable": false,
      "Latitude": 45.0,
      "Longitude": -122.0,
      "Elevation": 100,
      "UTCOffset": "00:00:00",
      "AutoTrack": true,
      "AlignmentMode": "GermanPolar",
      "EquatorialCoordinateType": "J2000",
      "AtPark": false
    }
  ],
  "Version": "1.0.0"
}
```

**Expected Error**: `MISSING_DEVICE_NAME`  
**Device**: 0  
**Auto-repairable**: Yes (generates "Telescope 0")

---

### Test File 5: Array Synchronization Error
**Filename**: `array-sync-error.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM3",
      "BaudRate": 9600,
      "AlignmentMode": "GermanPolar",
      "Latitude": 45.0,
      "Longitude": -122.0
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Second Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM4",
      "BaudRate": 9600,
      "AlignmentMode": "GermanPolar",
      "Latitude": 45.0,
      "Longitude": -122.0
    }
  ],
  "Version": "1.0.0"
}
```

**Expected Error**: `ARRAY_COUNT_MISMATCH`  
**Auto-repairable**: Yes (regenerates AlpacaDevices from Devices)

---

### Test File 6: Duplicate DeviceNumber
**Filename**: `duplicate-device-number.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    },
    {
      "DeviceNumber": 0,
      "DeviceName": "Duplicate Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "87654321-4321-4321-4321-210987654321"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM3",
      "BaudRate": 9600,
      "AlignmentMode": "GermanPolar",
      "Latitude": 45.0,
      "Longitude": -122.0
    },
    {
      "DeviceNumber": 0,
      "DeviceName": "Duplicate Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM4",
      "BaudRate": 9600,
      "AlignmentMode": "GermanPolar",
      "Latitude": 45.0,
      "Longitude": -122.0
    }
  ],
  "Version": "1.0.0"
}
```

**Expected Error**: `DUPLICATE_DEVICE_NUMBER`  
**Device**: 0  
**Auto-repairable**: No (manual resolution required)  
**Quarantine**: Second device with duplicate number excluded from device list

---

### Test File 7: Corrupt JSON
**Filename**: `corrupt-json.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope"
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
```

**Expected Error**: `FILE_PARSE_ERROR`  
**Auto-repairable**: No (delete and restart)

---

### Test File 8: Multiple Errors
**Filename**: `multiple-errors.json`

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-123456789012"
    }
  ],
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "",
      "Enabled": true,
      "Mount": "",
      "Port": "COM3",
      "BaudRate": 9600,
      "AlignmentMode": "BadMode",
      "Latitude": 45.0,
      "Longitude": -122.0
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Second Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM4",
      "BaudRate": 9600,
      "AlignmentMode": "GermanPolar",
      "Latitude": 45.0,
      "Longitude": -122.0
    }
  ],
  "Version": "1.0.0"
}
```

**Expected Errors**:
- `MISSING_DEVICE_NAME` (Device 0) - Auto-repairable
- `MISSING_MOUNT_TYPE` (Device 0) - Auto-repairable
- `INVALID_ALIGNMENT_MODE` (Device 0) - Auto-repairable
- `ARRAY_COUNT_MISMATCH` - Auto-repairable

---

## Test Execution Guide

### How to Use Test Files

```powershell
# PowerShell script to switch between test files
$settingsFolder = "$env:APPDATA\GreenSwampAlpaca\{version}"  # Replace {version} with your version
$testDataFolder = "$settingsFolder\TestData"

# Function to backup current settings
function Backup-Settings {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    Copy-Item "$settingsFolder\appsettings.user.json" "$settingsFolder\appsettings.user.json.manual-backup-$timestamp"
    Write-Host "Backup created: appsettings.user.json.manual-backup-$timestamp"
}

# Function to load test file
function Load-TestFile($testFileName) {
    Backup-Settings
    Copy-Item "$testDataFolder\$testFileName" "$settingsFolder\appsettings.user.json" -Force
    Write-Host "Loaded test file: $testFileName"
    Write-Host "Restart the application to test"
}

# Examples:
# Load-TestFile "valid-settings.json"
# Load-TestFile "invalid-alignment-mode.json"
# Load-TestFile "multiple-errors.json"
```

---

## Test Scenarios

### Phase 1: Startup Validation (Step 9)

#### Test 1.1: Valid Settings Startup
**Objective**: Verify startup validation with valid settings

**Steps**:
1. Load `valid-settings.json` as `appsettings.user.json`
2. Start application
3. Check console/logs

**Expected Results**:
- ✅ Console log: "Validating settings at startup..."
- ✅ Console log: "Settings validation: All settings are valid"
- ✅ Application starts successfully
- ✅ Device 0 loaded and advertised

**Pass/Fail**: ___________

---

#### Test 1.2: Invalid Settings Startup
**Objective**: Verify startup validation with invalid settings

**Steps**:
1. Load `invalid-alignment-mode.json` as `appsettings.user.json`
2. Start application
3. Check console/logs

**Expected Results**:
- ✅ Console log: "Settings validation failed with 1 error(s):"
- ✅ Console log: "  INVALID_ALIGNMENT_MODE [Device 0]: AlignmentMode 'InvalidMode' is not valid..."
- ✅ Console log: "    Resolution: Use 'Repair Settings' to reset to default (GermanPolar)."
- ✅ Console log: "Invalid devices will be quarantined and not advertised to ASCOM clients"
- ✅ Console log: "Visit /settings-health in the web UI to view details and repair settings"
- ✅ Application continues running (does NOT crash)
- ✅ Device 0 is quarantined (NOT in Device Manager)

**Pass/Fail**: ___________

---

#### Test 1.3: Missing Devices Array Startup
**Objective**: Verify startup with structural error

**Steps**:
1. Load `missing-devices-array.json` as `appsettings.user.json`
2. Start application
3. Check console/logs

**Expected Results**:
- ✅ Console log: "Settings validation failed with 1 error(s):"
- ✅ Console log: "  MISSING_DEVICES_ARRAY: Settings file missing 'Devices' array."
- ✅ Console log: "    Resolution: Use 'Repair Settings' button to regenerate structure."
- ✅ Application starts with default device
- ✅ Settings Health page shows error and repair button

**Pass/Fail**: ___________

---

### Phase 2: Settings Health Page UI

#### Test 2.1: Valid Settings Display
**Objective**: Verify green status display

**Steps**:
1. Load `valid-settings.json`
2. Start application
3. Navigate to `/settings-health`

**Expected Results**:
- ✅ Card border: Green
- ✅ Card header: Green background, white text
- ✅ Icon: oi-check (checkmark)
- ✅ Alert: "All settings are valid."
- ✅ Settings file path displayed
- ✅ No error/warning sections shown
- ✅ No repair button
- ✅ "Re-validate" and "Open Settings Folder" buttons enabled

**Pass/Fail**: ___________

---

#### Test 2.2: Error Display
**Objective**: Verify error rendering

**Steps**:
1. Load `invalid-alignment-mode.json`
2. Start application
3. Navigate to `/settings-health`

**Expected Results**:
- ✅ Card border: Red
- ✅ Card header: Red background, white text
- ✅ Icon: oi-warning
- ✅ Alert: "Settings validation failed. 1 error(s) found."
- ✅ Errors section shows:
  - Badge: "INVALID_ALIGNMENT_MODE" (red)
  - Badge: "Device 0" (secondary)
  - Badge: "Auto-repairable" (info)
  - Message: Full error text
  - Resolution: Detailed steps
- ✅ "Repair Settings Automatically" button visible

**Pass/Fail**: ___________

---

#### Test 2.3: Multiple Errors Display
**Objective**: Verify multiple error rendering

**Steps**:
1. Load `multiple-errors.json`
2. Navigate to `/settings-health`

**Expected Results**:
- ✅ Card border: Red
- ✅ Alert: "Settings validation failed. 4 error(s) found."
- ✅ All 4 errors listed with badges
- ✅ Each error shows device number (if applicable)
- ✅ Auto-repairable badges shown
- ✅ Errors section scrollable if long

**Pass/Fail**: ___________

---

### Phase 3: Repair Functionality

#### Test 3.1: Repair Missing Devices Array
**Objective**: Verify automatic array creation

**Steps**:
1. Load `missing-devices-array.json`
2. Navigate to `/settings-health`
3. Note error: MISSING_DEVICES_ARRAY
4. Click "Repair Settings Automatically"
5. Wait for completion

**Expected Results**:
- ✅ Button shows spinner: "Repairing..."
- ✅ Button disabled during repair
- ✅ Alert: "Repair Successful"
- ✅ Actions list: "Created Devices array with 1 default device"
- ✅ Devices repaired: 1
- ✅ Backup path shown: `appsettings.user.json.backup.{timestamp}`
- ✅ Backup file exists in settings folder
- ✅ Status auto-refreshes → green border
- ✅ Open settings file → Devices array present
- ✅ Device 0 appears in Device Manager

**Pass/Fail**: ___________

---

#### Test 3.2: Repair Invalid AlignmentMode
**Objective**: Verify property value fix

**Steps**:
1. Load `invalid-alignment-mode.json`
2. Navigate to `/settings-health`
3. Click "Repair Settings Automatically"

**Expected Results**:
- ✅ Alert: "Repair Successful"
- ✅ Actions: "Reset AlignmentMode for device 0 to GermanPolar"
- ✅ Devices repaired: 1
- ✅ Backup created
- ✅ Status refreshes → green
- ✅ Open `appsettings.user.json`:
  ```json
  "AlignmentMode": "GermanPolar"
  ```

**Pass/Fail**: ___________

---

#### Test 3.3: Repair Multiple Errors
**Objective**: Verify batch repair

**Steps**:
1. Load `multiple-errors.json`
2. Navigate to `/settings-health`
3. Click "Repair Settings Automatically"

**Expected Results**:
- ✅ Alert: "Repair Successful"
- ✅ Actions list shows 4+ items:
  - Set DeviceName for device 0
  - Set Mount for device 0
  - Reset AlignmentMode for device 0
  - Regenerated AlpacaDevices array (2 entries)
- ✅ Devices repaired: 1
- ✅ Status → green
- ✅ Both devices valid and shown in Device Manager

**Pass/Fail**: ___________

---

#### Test 3.4: Repair with Remaining Errors
**Objective**: Verify partial repair handling

**Steps**:
1. Load `duplicate-device-number.json`
2. Navigate to `/settings-health`
3. Click "Repair Settings Automatically"

**Expected Results**:
- ✅ Alert: "Repair Successful" (even with remaining errors)
- ✅ Actions list: (any auto-repairable fixes)
- ✅ "Remaining Issues (1)" section shown:
  - "DUPLICATE_DEVICE_NUMBER: DeviceNumber 0 appears multiple times..."
- ✅ Card border remains red
- ✅ Error section still shows DUPLICATE_DEVICE_NUMBER
- ✅ Only one device with DeviceNumber 0 in Device Manager

**Pass/Fail**: ___________

---

#### Test 3.5: Backup Verification
**Objective**: Verify backup file creation

**Steps**:
1. Load any test file with errors
2. Navigate to `/settings-health`
3. Click "Repair Settings Automatically"
4. Note backup path from success message
5. Navigate to settings folder

**Expected Results**:
- ✅ Backup file exists with timestamp in name
- ✅ Backup file contains pre-repair settings (original error still present)
- ✅ Current `appsettings.user.json` contains repaired settings

**Pass/Fail**: ___________

---

### Phase 4: Integration Tests

#### Test 4.1: Device Manager Quarantine
**Objective**: Verify invalid devices not shown

**Steps**:
1. Load `invalid-alignment-mode.json`
2. Start application
3. Navigate to `/device-manager`

**Expected Results**:
- ✅ Device list empty (Device 0 quarantined)
- ✅ OR shows only valid devices if multiple exist
- ✅ Navigate to `/settings-health`
- ✅ Shows error for Device 0
- ✅ Repair settings
- ✅ Return to `/device-manager`
- ✅ Device 0 now appears

**Pass/Fail**: ___________

---

#### Test 4.2: Navigation Menu Integration
**Objective**: Verify Settings Health link

**Steps**:
1. Start application
2. Open navigation menu

**Expected Results**:
- ✅ "Settings Health" link visible
- ✅ Icon: oi-heart (heart icon)
- ✅ Positioned between "Mount Settings" and "Monitor Settings"
- ✅ NO "Settings Profiles" link
- ✅ Click → navigates to `/settings-health`

**Pass/Fail**: ___________

---

#### Test 4.3: Re-validate Button
**Objective**: Verify manual re-validation

**Steps**:
1. Load `invalid-alignment-mode.json`
2. Navigate to `/settings-health`
3. Note error displayed
4. Manually fix `appsettings.user.json` (set `AlignmentMode: "GermanPolar"`)
5. Click "Re-validate" button

**Expected Results**:
- ✅ Button disabled during validation
- ✅ Spinner shown briefly
- ✅ Status refreshes automatically
- ✅ Card border changes red → green
- ✅ Error section disappears
- ✅ Success alert shown

**Pass/Fail**: ___________

---

#### Test 4.4: Open Settings Folder
**Objective**: Verify folder opener

**Steps**:
1. Navigate to `/settings-health`
2. Click "Open Settings Folder" button

**Expected Results**:
- ✅ Windows Explorer opens
- ✅ Folder: `%AppData%\GreenSwampAlpaca\{version}\`
- ✅ `appsettings.user.json` visible
- ✅ Any backup files visible
- ✅ Console log: "Opened settings folder: {path}"

**Pass/Fail**: ___________

---

### Phase 5: Edge Cases

#### Test 5.1: Empty Settings File
**Objective**: Verify zero-byte file handling

**Steps**:
1. Create empty `appsettings.user.json` (0 bytes)
2. Start application
3. Navigate to `/settings-health`

**Expected Results**:
- ✅ Error: FILE_PARSE_ERROR
- ✅ Error message: "Settings file contains invalid JSON"
- ✅ Resolution: "Delete {path} and restart..."
- ✅ Application continues running
- ✅ Default device loaded

**Pass/Fail**: ___________

---

#### Test 5.2: File Access Denied
**Objective**: Verify permission error handling

**Steps**:
1. Copy `valid-settings.json` to `appsettings.user.json`
2. Right-click → Properties → Security → Deny Read
3. Start application
4. Restore permissions after test

**Expected Results**:
- ✅ Console log: FILE_ACCESS_DENIED error
- ✅ Application starts with default device
- ✅ Settings Health shows error
- ✅ Resolution suggests checking permissions

**Pass/Fail**: ___________

---

#### Test 5.3: Concurrent Repair
**Objective**: Verify file locking

**Steps**:
1. Load test file with errors
2. Open `/settings-health` in two browser tabs
3. Click "Repair Settings" in both simultaneously

**Expected Results**:
- ✅ One repair completes
- ✅ Second repair waits or shows message
- ✅ No file corruption
- ✅ Both tabs eventually show correct status

**Pass/Fail**: ___________

---

### Phase 6: Performance

#### Test 6.1: Validation Speed
**Objective**: Measure validation performance

| Scenario | Expected | Actual | Pass/Fail |
|----------|----------|--------|-----------|
| 1 device, valid | < 100ms | ____ | ____ |
| 10 devices, valid | < 200ms | ____ | ____ |
| 1 device, errors | < 150ms | ____ | ____ |

---

#### Test 6.2: Repair Speed
**Objective**: Measure repair performance

| Scenario | Expected | Actual | Pass/Fail |
|----------|----------|--------|-----------|
| Fix 1 property | < 500ms | ____ | ____ |
| Regenerate array | < 1000ms | ____ | ____ |
| Fix multiple errors | < 2000ms | ____ | ____ |

---

## Error Code Reference

| Error Code | Severity | Auto-Repair | Description |
|-----------|----------|-------------|-------------|
| FILE_NOT_FOUND | Info | Yes | Settings file missing (creates default) |
| FILE_ACCESS_DENIED | Error | No | Cannot read file (permission issue) |
| FILE_LOCKED | Warning | No | File in use by another process |
| FILE_PARSE_ERROR | Error | No | Invalid JSON syntax |
| MISSING_DEVICES_ARRAY | Error | Yes | Required "Devices" array missing |
| MISSING_ALPACA_ARRAY | Error | Yes | Required "AlpacaDevices" array missing |
| INVALID_ARRAY_TYPE | Error | Yes | Array has wrong data type |
| EMPTY_DEVICES_ARRAY | Warning | Yes | Devices array is empty |
| ARRAY_COUNT_MISMATCH | Error | Yes | Devices and AlpacaDevices counts differ |
| DEVICE_NOT_IN_ALPACA | Error | Yes | Device missing from AlpacaDevices |
| ALPACA_NOT_IN_DEVICES | Error | Yes | AlpacaDevice orphaned |
| DEVICE_NAME_MISMATCH | Warning | Yes | DeviceName differs between arrays |
| MISSING_UNIQUE_ID | Warning | Yes | UniqueId missing in AlpacaDevices |
| DUPLICATE_DEVICE_NUMBER | Error | No | Same DeviceNumber appears twice |
| NEGATIVE_DEVICE_NUMBER | Error | No | DeviceNumber < 0 |
| MISSING_DEVICE_NAME | Error | Yes | DeviceName is null/empty |
| INVALID_ALIGNMENT_MODE | Error | Yes | AlignmentMode not AltAz/GermanPolar/Polar |
| MISSING_MOUNT_TYPE | Error | Yes | Mount property is null/empty |
| VALIDATION_EXCEPTION | Error | No | Unexpected exception during validation |

---

## Test Summary Report Template

```
═══════════════════════════════════════════════════════════
  SETTINGS ERROR HANDLING TEST REPORT
═══════════════════════════════════════════════════════════

Test Date: _____________________
Tester: ________________________
Build Version: _________________
Settings Version: ______________

───────────────────────────────────────────────────────────
TEST STATISTICS
───────────────────────────────────────────────────────────
Total Tests Executed:       ____
Tests Passed:              ____
Tests Failed:              ____
Tests Skipped:             ____
Pass Rate:                 ____%

───────────────────────────────────────────────────────────
PHASE RESULTS
───────────────────────────────────────────────────────────
Phase 1 (Startup):         [ PASS / FAIL ]  (___ / ___ tests)
Phase 2 (UI):              [ PASS / FAIL ]  (___ / ___ tests)
Phase 3 (Repair):          [ PASS / FAIL ]  (___ / ___ tests)
Phase 4 (Integration):     [ PASS / FAIL ]  (___ / ___ tests)
Phase 5 (Edge Cases):      [ PASS / FAIL ]  (___ / ___ tests)
Phase 6 (Performance):     [ PASS / FAIL ]  (___ / ___ tests)

───────────────────────────────────────────────────────────
CRITICAL ISSUES (Blockers)
───────────────────────────────────────────────────────────
1. _____________________________________________________

2. _____________________________________________________

3. _____________________________________________________

───────────────────────────────────────────────────────────
HIGH PRIORITY ISSUES
───────────────────────────────────────────────────────────
1. _____________________________________________________

2. _____________________________________________________

───────────────────────────────────────────────────────────
MEDIUM/LOW PRIORITY ISSUES
───────────────────────────────────────────────────────────
1. _____________________________________________________

2. _____________________________________________________

───────────────────────────────────────────────────────────
RECOMMENDATIONS
───────────────────────────────────────────────────────────
_____________________________________________________________

_____________________________________________________________

_____________________________________________________________

───────────────────────────────────────────────────────────
SIGN-OFF
───────────────────────────────────────────────────────────
Tested By: ________________    Date: ___________

Approved By: ______________    Date: ___________

Release Status: [ APPROVED / NEEDS WORK / BLOCKED ]
═══════════════════════════════════════════════════════════
```

---

## Appendix A: PowerShell Test Helpers

### Complete Test Helper Script

```powershell
# Settings Error Handling Test Helper Script
# Version: 1.0.0

# ═══════════════════════════════════════════════════════════
# CONFIGURATION
# ═══════════════════════════════════════════════════════════

$appData = $env:APPDATA
$settingsRoot = "$appData\GreenSwampAlpaca"

# Auto-detect version folder (most recent)
$versionFolder = Get-ChildItem $settingsRoot -Directory | 
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+' } |
    Sort-Object Name -Descending | 
    Select-Object -First 1

if (-not $versionFolder) {
    Write-Host "ERROR: No version folder found in $settingsRoot" -ForegroundColor Red
    exit 1
}

$settingsFolder = $versionFolder.FullName
$testDataFolder = "$settingsFolder\TestData"
$settingsFile = "$settingsFolder\appsettings.user.json"

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Settings Error Handling Test Helper" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Settings Version: $($versionFolder.Name)" -ForegroundColor Yellow
Write-Host "Settings Folder: $settingsFolder" -ForegroundColor Gray
Write-Host "Test Data Folder: $testDataFolder" -ForegroundColor Gray
Write-Host ""

# ═══════════════════════════════════════════════════════════
# FUNCTIONS
# ═══════════════════════════════════════════════════════════

function Backup-Settings {
    if (Test-Path $settingsFile) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupFile = "$settingsFile.manual-backup-$timestamp"
        Copy-Item $settingsFile $backupFile
        Write-Host "✓ Backup created: $(Split-Path $backupFile -Leaf)" -ForegroundColor Green
        return $backupFile
    }
    else {
        Write-Host "! No settings file to backup" -ForegroundColor Yellow
        return $null
    }
}

function Load-TestFile {
    param(
        [Parameter(Mandatory=$true)]
        [string]$TestFileName
    )
    
    $testFilePath = "$testDataFolder\$TestFileName"
    
    if (-not (Test-Path $testFilePath)) {
        Write-Host "✗ Test file not found: $TestFileName" -ForegroundColor Red
        return $false
    }
    
    Backup-Settings
    Copy-Item $testFilePath $settingsFile -Force
    Write-Host "✓ Loaded test file: $TestFileName" -ForegroundColor Green
    Write-Host "  Restart the application to test" -ForegroundColor Cyan
    return $true
}

function Restore-Backup {
    param(
        [Parameter(Mandatory=$false)]
        [string]$BackupFileName
    )
    
    if ($BackupFileName) {
        $backupPath = "$settingsFolder\$BackupFileName"
    }
    else {
        # Find most recent backup
        $backups = Get-ChildItem "$settingsFolder\appsettings.user.json.manual-backup-*" |
            Sort-Object LastWriteTime -Descending
        
        if (-not $backups) {
            Write-Host "✗ No backup files found" -ForegroundColor Red
            return $false
        }
        
        $backupPath = $backups[0].FullName
    }
    
    if (Test-Path $backupPath) {
        Copy-Item $backupPath $settingsFile -Force
        Write-Host "✓ Restored backup: $(Split-Path $backupPath -Leaf)" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "✗ Backup file not found: $backupPath" -ForegroundColor Red
        return $false
    }
}

function Show-TestFiles {
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Available Test Files" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    
    if (-not (Test-Path $testDataFolder)) {
        Write-Host "! Test data folder not found. Creating..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Force -Path $testDataFolder | Out-Null
        Write-Host "  Copy test JSON files to: $testDataFolder" -ForegroundColor Gray
        return
    }
    
    $testFiles = Get-ChildItem $testDataFolder -Filter "*.json"
    
    if (-not $testFiles) {
        Write-Host "! No test files found in $testDataFolder" -ForegroundColor Yellow
        return
    }
    
    $i = 1
    foreach ($file in $testFiles) {
        Write-Host "$i. $($file.Name)" -ForegroundColor White
        $i++
    }
    Write-Host ""
}

function Show-Backups {
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Available Backups" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    
    $backups = Get-ChildItem "$settingsFolder\appsettings.user.json.*backup*" |
        Sort-Object LastWriteTime -Descending
    
    if (-not $backups) {
        Write-Host "! No backup files found" -ForegroundColor Yellow
        return
    }
    
    $i = 1
    foreach ($backup in $backups) {
        $age = (Get-Date) - $backup.LastWriteTime
        $ageStr = if ($age.TotalHours -lt 1) {
            "$([math]::Round($age.TotalMinutes)) minutes ago"
        } elseif ($age.TotalDays -lt 1) {
            "$([math]::Round($age.TotalHours)) hours ago"
        } else {
            "$([math]::Round($age.TotalDays)) days ago"
        }
        
        Write-Host "$i. $($backup.Name)" -ForegroundColor White
        Write-Host "   Created: $ageStr" -ForegroundColor Gray
        $i++
    }
    Write-Host ""
}

function Open-SettingsFolder {
    Start-Process $settingsFolder
    Write-Host "✓ Opened settings folder in Explorer" -ForegroundColor Green
}

function Show-CurrentSettings {
    if (-not (Test-Path $settingsFile)) {
        Write-Host "✗ Settings file not found" -ForegroundColor Red
        return
    }
    
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Current Settings Summary" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    
    $json = Get-Content $settingsFile -Raw | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "Version: $($json.Version)" -ForegroundColor White
    Write-Host "Devices Count: $($json.Devices.Count)" -ForegroundColor White
    Write-Host "AlpacaDevices Count: $($json.AlpacaDevices.Count)" -ForegroundColor White
    
    if ($json.Devices.Count -gt 0) {
        Write-Host ""
        Write-Host "Devices:" -ForegroundColor Yellow
        foreach ($device in $json.Devices) {
            $status = if ($device.Enabled) { "Enabled" } else { "Disabled" }
            Write-Host "  #$($device.DeviceNumber): $($device.DeviceName) - $status" -ForegroundColor Gray
        }
    }
    Write-Host ""
}

# ═══════════════════════════════════════════════════════════
# MENU
# ═══════════════════════════════════════════════════════════

function Show-Menu {
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Test Helper Menu" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Show Test Files" -ForegroundColor White
    Write-Host "2. Load Test File" -ForegroundColor White
    Write-Host "3. Show Backups" -ForegroundColor White
    Write-Host "4. Restore Backup" -ForegroundColor White
    Write-Host "5. Show Current Settings" -ForegroundColor White
    Write-Host "6. Open Settings Folder" -ForegroundColor White
    Write-Host "Q. Quit" -ForegroundColor White
    Write-Host ""
}

# ═══════════════════════════════════════════════════════════
# MAIN LOOP
# ═══════════════════════════════════════════════════════════

while ($true) {
    Show-Menu
    $choice = Read-Host "Enter choice"
    
    switch ($choice.ToUpper()) {
        "1" { Show-TestFiles }
        "2" {
            Show-TestFiles
            $fileName = Read-Host "Enter test file name"
            if ($fileName) {
                Load-TestFile $fileName
            }
        }
        "3" { Show-Backups }
        "4" {
            Show-Backups
            $backupName = Read-Host "Enter backup name (or press Enter for most recent)"
            Restore-Backup $backupName
        }
        "5" { Show-CurrentSettings }
        "6" { Open-SettingsFolder }
        "Q" { 
            Write-Host "Goodbye!" -ForegroundColor Cyan
            exit 0
        }
        default {
            Write-Host "Invalid choice. Try again." -ForegroundColor Red
        }
    }
    
    Write-Host ""
    Read-Host "Press Enter to continue"
    Clear-Host
}
```

Save this as `TestHelper.ps1` and run:
```powershell
.\TestHelper.ps1
```

---

## Appendix B: Quick Reference

### Common Tasks

| Task | Command |
|------|---------|
| Find settings folder | `echo %AppData%\GreenSwampAlpaca` |
| Open settings folder | `explorer %AppData%\GreenSwampAlpaca` |
| Backup settings | Copy file with timestamp suffix |
| Delete settings | Delete `appsettings.user.json`, restart app |
| View logs | Check console output or ASCOM logs |

### URL Quick Links

| Page | URL |
|------|-----|
| Settings Health | `http://localhost:31426/settings-health` |
| Device Manager | `http://localhost:31426/device-manager` |
| Server Settings | `http://localhost:31426/setup` |
| Mount Settings | `http://localhost:31426/mountsettings` |

*(Replace port 31426 with your configured port)*

---

## Questions or Issues?

If you encounter any issues during testing:

1. **Check Console Logs**: Look for validation errors and quarantine messages
2. **Check Settings Health Page**: `/settings-health` shows detailed errors
3. **Backup Before Testing**: Always backup before loading test files
4. **Use Test Helper Script**: Automates common tasks
5. **Report Issues**: Document error codes, steps to reproduce, and expected vs actual behavior

---

**Test Plan Version**: 1.0.0  
**Last Updated**: 2025-01-26  
**Author**: GitHub Copilot  
**Status**: Ready for Execution
