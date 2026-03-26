# Settings System Test Plan

**Project:** GreenSwamp Alpaca - Multi-Device Telescope Control  
**Component:** Settings & Configuration Management (Phase 1 & 2)  
**Version:** 1.0  
**Date:** 2026-03-25  
**Test Type:** Manual Offline Testing  
**Status:** Ready for Execution

---

## 📋 Executive Summary

This test plan covers validation of the new JSON-based settings system with version isolation, multi-device support, and 1-to-1 synchronization between `AlpacaDevices` and `Devices` arrays.

**What's Being Tested:**
- ✅ Core settings services (VersionedSettingsService, DeviceSynchronizationService)
- ✅ JSON file structure and property completeness
- ✅ Version isolation and migration
- ✅ Multi-device configuration
- ✅ 1-to-1 synchronization validation
- ✅ Manual device management workflow

**Out of Scope (Phase 3 - Deferred):**
- ❌ Blazor UI for device management (Add/Edit/Delete buttons)
- ❌ Real-time settings editing without restart
- ❌ Template selection UI

---

## 🎯 Testing Objectives

| Objective | Description | Success Criteria |
|-----------|-------------|------------------|
| **OBJ-1** | Verify default configuration generation | Single device (Device 0) created with all 113 properties |
| **OBJ-2** | Validate version isolation | Settings stored in version-specific directories |
| **OBJ-3** | Test multi-device support | Add/edit/remove devices manually via JSON |
| **OBJ-4** | Confirm 1-to-1 synchronization | AlpacaDevices ↔ Devices validation enforced |
| **OBJ-5** | Test version migration | Settings copied from previous version with property updates |
| **OBJ-6** | Verify error handling | Clear error messages for invalid configurations |
| **OBJ-7** | Validate runtime behavior | Devices loaded and functional with correct settings |

---

## 🛠️ Test Environment Setup

### Prerequisites

1. **Clean Machine State** (for fresh install testing)
   - Delete `%AppData%\GreenSwampAlpaca` folder
   - Or rename to `GreenSwampAlpaca.backup` for safe testing

2. **Tools Required**
   - Text editor: Visual Studio Code (recommended) or Notepad++
   - JSON validator: [jsonlint.com](https://jsonlint.com/) or VS Code built-in
   - GUID generator: [guidgenerator.com](https://www.guidgenerator.com/) or PowerShell

3. **Reference Files**
   - Copy of default `appsettings.json` from project
   - Device Management User Guide (`Device-Management-User-Guide.md`)
   - Property reference from default configuration

4. **Test Data**
   - Prepare test device configurations (AltAz, GermanPolar, Polar)
   - Prepare invalid JSON scenarios
   - Prepare out-of-sync scenarios

### File Locations Reference

```
Application Files:
T:\source\repos\GreenSwampAlpaca\
└── GreenSwamp.Alpaca.Server\
    ├── appsettings.json (shipped defaults)
    └── appsettings.schema.json (validation schema)

User Settings Files:
%AppData%\GreenSwampAlpaca\{version}\
└── appsettings.user.json (user configurations)

Example:
C:\Users\Andy\AppData\Roaming\GreenSwampAlpaca\1.0.0\appsettings.user.json
```

---

## 📝 Test Cases

### Test Suite 1: First Launch & Default Configuration

#### TC-001: Fresh Install - Default Configuration Generation
**Priority:** CRITICAL  
**Objective:** OBJ-1  
**Prerequisites:** Clean environment (no existing `%AppData%\GreenSwampAlpaca` folder)

**Steps:**
1. Delete or rename `%AppData%\GreenSwampAlpaca` folder
2. Launch GreenSwamp.Alpaca.Server application
3. Wait for application startup to complete
4. Navigate to `%AppData%\GreenSwampAlpaca\{version}\` directory
5. Open `appsettings.user.json` in VS Code

**Expected Results:**
- ✅ Settings directory created: `%AppData%\GreenSwampAlpaca\{version}\`
- ✅ `appsettings.user.json` file created
- ✅ File contains valid JSON (no syntax errors)
- ✅ Contains `Version` property matching application version
- ✅ Contains `CreatedDate` timestamp (ISO 8601 UTC format)
- ✅ Contains `LastModified` timestamp (matches CreatedDate on first run)
- ✅ Contains exactly 1 device in `AlpacaDevices` array (Device 0)
- ✅ Contains exactly 1 device in `Devices` array (Device 0)
- ✅ Device 0 has all 113 properties (verify against default template)
- ✅ DeviceNumber = 0 in both arrays
- ✅ DeviceName matches in both arrays: "Simulator (GEM)"
- ✅ UniqueId is valid GUID format

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-002: Default Device Properties Verification
**Priority:** HIGH  
**Objective:** OBJ-1  
**Prerequisites:** TC-001 passed

**Steps:**
1. Open `appsettings.user.json` created in TC-001
2. Verify each property category exists (use checklist below)
3. Compare against shipped `appsettings.json` defaults

**Property Checklist (113 total):**

**Core Properties (8):**
- [ ] DeviceNumber = 0
- [ ] DeviceName = "Simulator (GEM)"
- [ ] Enabled = true
- [ ] Mount = "Simulator"
- [ ] AlignmentMode = "GermanPolar"
- [ ] Port = "COM1"
- [ ] BaudRate = 9600
- [ ] Protocol = "Alpaca"

**Location Properties (4):**
- [ ] Latitude
- [ ] Longitude
- [ ] Elevation
- [ ] UTCOffset

**Tracking Properties (10+):**
- [ ] TrackingRate
- [ ] CustomTrackingRateOffsetRA
- [ ] CustomTrackingRateOffsetDec
- [ ] PulseGuideRateRa
- [ ] PulseGuideRateDec
- [ ] (etc. - verify complete set)

**Home/Park Properties (8+):**
- [ ] HomeAxis1
- [ ] HomeAxis2
- [ ] ParkAxis1
- [ ] ParkAxis2
- [ ] ParkName
- [ ] (etc.)

**Limits Properties (8+):**
- [ ] SouthFlipHa
- [ ] SouthFlipDec
- [ ] NorthFlipHa
- [ ] NorthFlipDec
- [ ] HourAngleLimit
- [ ] (etc.)

**PEC Properties (5+):**
- [ ] PPECOn
- [ ] PPECOnTraining
- [ ] PPECRecordMode
- [ ] (etc.)

**Encoders Properties (4+):**
- [ ] EncoderResolutionAxis1
- [ ] EncoderResolutionAxis2
- [ ] (etc.)

**Advanced Properties (20+):**
- [ ] DecPulseToGoTo
- [ ] MinPulseDec
- [ ] MaxPulseDec
- [ ] (etc.)

**Display Properties (5+):**
- [ ] DisplayUnits
- [ ] DisplayDecimalPlaces
- [ ] (etc.)

**Capabilities Properties (15+):**
- [ ] CanFindHome
- [ ] CanPark
- [ ] CanSetPark
- [ ] CanSetTracking
- [ ] CanSlew
- [ ] CanSync
- [ ] (etc.)

**Expected Results:**
- ✅ All 113 properties present in default configuration
- ✅ No properties have null values (unless null is valid for that type)
- ✅ Enum properties have valid values (e.g., AlignmentMode = "GermanPolar")
- ✅ Numeric properties within valid ranges
- ✅ Boolean properties are true/false (not strings)

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-003: Application Startup with Default Configuration
**Priority:** CRITICAL  
**Objective:** OBJ-7  
**Prerequisites:** TC-001 passed

**Steps:**
1. Launch application (if not already running from TC-001)
2. Navigate to `/device-manager` page in browser
3. Verify Device 0 appears in device list
4. Click "Connect" on Device 0
5. Wait for connection to complete
6. Verify connection status shows "Connected"

**Expected Results:**
- ✅ Application starts without errors
- ✅ Device 0 visible in device list
- ✅ DeviceName displays as "Simulator (GEM)"
- ✅ Enabled status shows as "Yes"
- ✅ AlignmentMode shows as "GermanPolar"
- ✅ Connection succeeds (simulator responds)
- ✅ Console/logs show no errors related to settings loading

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 2: Version Isolation

#### TC-004: Version Directory Structure
**Priority:** CRITICAL  
**Objective:** OBJ-2  
**Prerequisites:** TC-001 passed

**Steps:**
1. Navigate to `%AppData%\GreenSwampAlpaca\` folder
2. Verify version subdirectory exists
3. Check version string format

**Expected Results:**
- ✅ Directory structure: `%AppData%\GreenSwampAlpaca\{version}\`
- ✅ Version matches application assembly version (e.g., "1.0.0")
- ✅ Build metadata stripped (e.g., "1.0.0+abc123" → "1.0.0")
- ✅ No duplicate version directories
- ✅ Settings file exists within version directory

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-005: Version Migration (Upgrade Scenario)
**Priority:** HIGH  
**Objective:** OBJ-5  
**Prerequisites:** TC-004 passed

**Setup:**
1. Stop the application
2. Navigate to `%AppData%\GreenSwampAlpaca\`
3. Copy the `{current_version}` folder to `{old_version}` (e.g., "1.0.0" → "0.9.0")
4. Modify `{old_version}\appsettings.user.json`:
   - Change version property to "0.9.0"
   - Modify Device 0 Port to "COM99" (test value)
   - Add a second device (Device 1) with custom settings
5. Delete the `{current_version}` folder (simulate upgrade)
6. Update application version to test migration (or manually test migration logic)

**Steps:**
1. Launch application with simulated old version settings present
2. Check for `{current_version}` folder creation
3. Open new `appsettings.user.json`
4. Verify settings migrated

**Expected Results:**
- ✅ New version directory created: `{current_version}\`
- ✅ New `appsettings.user.json` created
- ✅ Version property updated to current version
- ✅ CreatedDate preserved from old version
- ✅ LastModified updated to migration timestamp
- ✅ Custom Port value "COM99" preserved for Device 0
- ✅ Device 1 settings preserved
- ✅ New properties added with default values (if any added in new version)
- ✅ Obsolete properties removed (if any removed in new version)
- ✅ Console logs show migration info messages

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 3: Multi-Device Support

#### TC-006: Add Second Device (Manual JSON Edit)
**Priority:** CRITICAL  
**Objective:** OBJ-3  
**Prerequisites:** TC-003 passed

**Steps:**
1. Stop the application
2. Open `appsettings.user.json` in VS Code
3. Copy entire Device 0 object from `Devices` array
4. Paste after Device 0 (add comma after Device 0 closing brace)
5. Modify copied device:
   ```json
   {
     "DeviceNumber": 1,
     "DeviceName": "Test Mount (AltAz)",
     "Enabled": true,
     "Mount": "Simulator",
     "AlignmentMode": "AltAz",
     "Port": "COM2",
     // ...other properties unchanged...
   }
   ```
6. Copy AlpacaDevices[0] entry
7. Add to AlpacaDevices array:
   ```json
   {
     "DeviceNumber": 1,
     "DeviceName": "Test Mount (AltAz)",
     "DeviceType": "Telescope",
     "UniqueId": "[generate new GUID]"
   }
   ```
8. Save file
9. Validate JSON syntax (VS Code should show no errors)
10. Launch application
11. Navigate to `/device-manager`

**Expected Results:**
- ✅ JSON file saves without syntax errors
- ✅ Application starts successfully
- ✅ Two devices appear in device manager list
- ✅ Device 0: "Simulator (GEM)" with GermanPolar mode
- ✅ Device 1: "Test Mount (AltAz)" with AltAz mode
- ✅ Both devices show Enabled = Yes
- ✅ Both devices show correct Port assignments
- ✅ Can connect to both devices independently
- ✅ No errors in console/logs related to device loading

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-007: Add Third Device (Different Mount Type)
**Priority:** MEDIUM  
**Objective:** OBJ-3  
**Prerequisites:** TC-006 passed

**Steps:**
1. Stop application
2. Add Device 2 to `appsettings.user.json`:
   ```json
   {
     "DeviceNumber": 2,
     "DeviceName": "SkyWatcher Mount (Polar)",
     "Enabled": true,
     "Mount": "SkyWatcher",
     "AlignmentMode": "Polar",
     "Port": "COM3",
     "BaudRate": 115200,
     // ...other properties...
   }
   ```
3. Add corresponding AlpacaDevices entry
4. Save and restart application
5. Navigate to `/device-manager`

**Expected Results:**
- ✅ Three devices listed
- ✅ Device 2 shows correct name and mode
- ✅ All three devices independently connectable
- ✅ Each device retains independent configuration

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-008: Edit Existing Device Properties
**Priority:** HIGH  
**Objective:** OBJ-3  
**Prerequisites:** TC-006 passed

**Steps:**
1. Stop application
2. Open `appsettings.user.json`
3. Modify Device 1 properties:
   - Change Port: "COM2" → "COM5"
   - Change Latitude: [original] → 40.7128
   - Change Longitude: [original] → -74.0060
   - Change DeviceName: "Test Mount (AltAz)" → "New York Observatory (AltAz)"
4. Update DeviceName in AlpacaDevices[1] to match
5. Save and restart application
6. Navigate to `/device-manager`
7. Connect to Device 1

**Expected Results:**
- ✅ Device 1 shows updated name: "New York Observatory (AltAz)"
- ✅ Device connects using new Port: COM5 (check logs)
- ✅ Location properties updated (verify in telescope properties page if available)
- ✅ LastModified timestamp updated in JSON file
- ✅ Other devices (0, 2) unaffected

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-009: Disable Device Without Deleting
**Priority:** MEDIUM  
**Objective:** OBJ-3  
**Prerequisites:** TC-006 passed

**Steps:**
1. Stop application
2. Open `appsettings.user.json`
3. Change Device 1: `"Enabled": true` → `"Enabled": false`
4. Save and restart application
5. Navigate to `/device-manager`

**Expected Results:**
- ✅ Device 1 still appears in device list
- ✅ Device 1 shows Enabled = No
- ✅ Device 1 cannot be connected (Connect button disabled or connection fails gracefully)
- ✅ Devices 0 and 2 remain connectable
- ✅ No errors in console related to disabled device

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-010: Remove Device
**Priority:** MEDIUM  
**Objective:** OBJ-3  
**Prerequisites:** TC-006 passed

**Steps:**
1. Stop application
2. Open `appsettings.user.json`
3. Delete Device 1 object from `Devices` array (remove entire object and comma)
4. Delete AlpacaDevices[1] entry
5. Verify DeviceNumbers remain unique (should now be 0, 2)
6. Save and restart application
7. Navigate to `/device-manager`

**Expected Results:**
- ✅ Only two devices listed (Device 0, Device 2)
- ✅ Device 1 no longer appears
- ✅ No gaps in device list display
- ✅ No errors related to missing Device 1
- ✅ Remaining devices function normally

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 4: 1-to-1 Synchronization Validation

#### TC-011: Synchronization - Missing AlpacaDevice Entry
**Priority:** CRITICAL  
**Objective:** OBJ-4  
**Prerequisites:** TC-003 passed

**Setup (Create Invalid State):**
1. Stop application
2. Open `appsettings.user.json`
3. Add Device 3 to `Devices` array:
   ```json
   {
     "DeviceNumber": 3,
     "DeviceName": "Orphan Device",
     "Enabled": true,
     "Mount": "Simulator",
     // ...other properties...
   }
   ```
4. **Do NOT add corresponding AlpacaDevices entry**
5. Save file

**Steps:**
1. Launch application
2. Observe console output / logs

**Expected Results:**
- ✅ Application starts successfully (should auto-fix synchronization)
- ✅ DeviceSynchronizationService logs info message about creating missing AlpacaDevice entry
- ✅ Check `appsettings.user.json` - AlpacaDevices array now contains entry for Device 3
- ✅ UniqueId generated automatically
- ✅ DeviceName matches in both arrays
- ✅ Device 3 appears in device manager and is functional

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-012: Synchronization - Mismatched DeviceNumber
**Priority:** CRITICAL  
**Objective:** OBJ-4  
**Prerequisites:** TC-006 passed

**Setup (Create Invalid State):**
1. Stop application
2. Open `appsettings.user.json`
3. Modify Device 1 in `Devices` array: `"DeviceNumber": 1` → `"DeviceNumber": 5`
4. Leave AlpacaDevices[1] with `"DeviceNumber": 1` (mismatch)
5. Save file

**Steps:**
1. Launch application
2. Observe console output / logs

**Expected Results:**
- ✅ Application detects synchronization error
- ✅ Error message clearly states mismatch: "Device 5 exists in Devices but no matching AlpacaDevice entry"
- ✅ Error message suggests resolution steps
- ✅ Application either:
   - Auto-fixes by creating AlpacaDevice entry for Device 5, OR
   - Logs warning and continues (depending on implementation)
- ✅ Device becomes accessible after synchronization fix

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-013: Synchronization - Mismatched DeviceName
**Priority:** HIGH  
**Objective:** OBJ-4  
**Prerequisites:** TC-006 passed

**Setup (Create Invalid State):**
1. Stop application
2. Open `appsettings.user.json`
3. Change Device 1 DeviceName in `Devices`: "Test Mount (AltAz)"
4. Leave different DeviceName in AlpacaDevices[1]: "Old Name"
5. Save file

**Steps:**
1. Launch application
2. Check device manager display
3. Check ASCOM discovery endpoint: `http://localhost:11111/management/v1/configureddevices`

**Expected Results:**
- ✅ Application detects name mismatch
- ✅ DeviceSynchronizationService logs warning
- ✅ Device name synchronized automatically (Devices name takes precedence)
- ✅ Both arrays show consistent DeviceName after startup
- ✅ Discovery endpoint returns correct name

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-014: Synchronization - Orphaned AlpacaDevice Entry
**Priority:** HIGH  
**Objective:** OBJ-4  
**Prerequisites:** TC-003 passed

**Setup (Create Invalid State):**
1. Stop application
2. Open `appsettings.user.json`
3. Add AlpacaDevice entry without corresponding Devices entry:
   ```json
   {
     "DeviceNumber": 10,
     "DeviceName": "Ghost Device",
     "DeviceType": "Telescope",
     "UniqueId": "[new GUID]"
   }
   ```
4. Do NOT add Device 10 to Devices array
5. Save file

**Steps:**
1. Launch application
2. Check console logs
3. Navigate to `/device-manager`
4. Check ASCOM discovery endpoint

**Expected Results:**
- ✅ Application detects orphaned AlpacaDevice entry
- ✅ Warning logged about entry without matching Device
- ✅ Application removes orphaned entry or marks as invalid
- ✅ Device 10 does NOT appear in device manager
- ✅ Discovery endpoint does NOT return Device 10

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 5: Error Handling & Validation

#### TC-015: Invalid JSON Syntax
**Priority:** CRITICAL  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup (Create Invalid File):**
1. Stop application
2. Open `appsettings.user.json`
3. Introduce syntax error: Remove closing brace from Device 0
4. Save file (VS Code should show error)

**Steps:**
1. Attempt to launch application
2. Observe error messages

**Expected Results:**
- ✅ Application detects JSON parse error
- ✅ Error message clearly indicates "Invalid JSON syntax"
- ✅ Error message includes file path
- ✅ Application either:
   - Falls back to default configuration and logs error, OR
   - Shows user-friendly error dialog with recovery instructions
- ✅ Application does not crash

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-016: Missing Required Property
**Priority:** HIGH  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup (Create Invalid Configuration):**
1. Stop application
2. Open `appsettings.user.json`
3. Remove required property from Device 0: Delete `"DeviceNumber": 0,` line
4. Save file

**Steps:**
1. Launch application
2. Observe console logs

**Expected Results:**
- ✅ Application detects missing required property
- ✅ Error message: "DeviceNumber is required for all devices"
- ✅ Application either:
   - Assigns default DeviceNumber and logs warning, OR
   - Skips device and logs error
- ✅ Application continues to run with valid devices

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-017: Duplicate DeviceNumber
**Priority:** HIGH  
**Objective:** OBJ-6  
**Prerequisites:** TC-006 passed

**Setup (Create Invalid Configuration):**
1. Stop application
2. Open `appsettings.user.json`
3. Change Device 1: `"DeviceNumber": 1` → `"DeviceNumber": 0` (duplicate of Device 0)
4. Save file

**Steps:**
1. Launch application
2. Observe console logs
3. Check device manager

**Expected Results:**
- ✅ Application detects duplicate DeviceNumber
- ✅ Error message: "Duplicate DeviceNumber 0 detected"
- ✅ Application either:
   - Auto-corrects by assigning next available DeviceNumber, OR
   - Disables one of the conflicting devices
- ✅ Error logged with clear resolution instructions
- ✅ Application does not crash

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-018: Invalid Enum Value
**Priority:** MEDIUM  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup (Create Invalid Configuration):**
1. Stop application
2. Open `appsettings.user.json`
3. Change Device 0: `"AlignmentMode": "GermanPolar"` → `"AlignmentMode": "InvalidMode"`
4. Save file

**Steps:**
1. Launch application
2. Observe console logs

**Expected Results:**
- ✅ Application detects invalid enum value
- ✅ Error message: "Invalid AlignmentMode 'InvalidMode'. Valid values: AltAz, GermanPolar, Polar"
- ✅ Application falls back to default value (e.g., "GermanPolar")
- ✅ Warning logged
- ✅ Device loads with default mode

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-019: File Locked / Access Denied
**Priority:** MEDIUM  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup (Create Locked File Scenario):**
1. Launch application
2. Keep application running
3. Open `appsettings.user.json` in text editor
4. Make a change (e.g., change Port to "COM10")
5. Attempt to save while application is running

**Steps:**
1. Save file from text editor
2. Observe save behavior
3. Restart application
4. Verify change applied

**Expected Results:**
- ✅ Text editor either:
   - Saves successfully (file not locked by application), OR
   - Shows "File in use" error (depending on file locking strategy)
- ✅ After restart, changes take effect
- ✅ No file corruption
- ✅ Application logs indicate settings reload

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 6: Runtime Behavior

#### TC-020: Settings Applied to Device Instance
**Priority:** CRITICAL  
**Objective:** OBJ-7  
**Prerequisites:** TC-006 passed (multiple devices configured)

**Steps:**
1. Ensure Device 0 configured with Port "COM1", BaudRate 9600
2. Ensure Device 1 configured with Port "COM2", BaudRate 115200
3. Launch application
4. Navigate to `/device-manager`
5. Connect to Device 0
6. Check console logs for connection details
7. Connect to Device 1
8. Check console logs for Device 1 connection

**Expected Results:**
- ✅ Device 0 connection logs show Port = COM1, BaudRate = 9600
- ✅ Device 1 connection logs show Port = COM2, BaudRate = 115200
- ✅ Each device uses independent settings
- ✅ No cross-contamination between device configurations
- ✅ Location properties applied correctly per device

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-021: Timestamp Updates on Save
**Priority:** LOW  
**Objective:** OBJ-7  
**Prerequisites:** TC-003 passed

**Steps:**
1. Launch application
2. Note current `LastModified` timestamp in `appsettings.user.json`
3. Make a change to settings (manually edit file while app stopped)
4. Restart application
5. Check `LastModified` timestamp

**Expected Results:**
- ✅ LastModified timestamp updated to current UTC time
- ✅ Timestamp in ISO 8601 format (e.g., "2026-03-25T14:30:00Z")
- ✅ CreatedDate remains unchanged from original creation

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-022: Device Enable/Disable Toggle
**Priority:** MEDIUM  
**Objective:** OBJ-7  
**Prerequisites:** TC-006 passed

**Steps:**
1. Configure Device 1 with `Enabled: true`
2. Launch application, verify Device 1 connectable
3. Stop application
4. Change Device 1: `"Enabled": false`
5. Restart application
6. Check device manager
7. Stop application
8. Change Device 1: `"Enabled": true`
9. Restart application

**Expected Results:**
- ✅ When Enabled = true: Device appears and is connectable
- ✅ When Enabled = false: Device appears but cannot connect
- ✅ Toggling Enabled requires restart (expected behavior - Phase 2)
- ✅ No errors when toggling Enabled state

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

### Test Suite 7: Edge Cases & Stress Testing

#### TC-023: Maximum Device Count
**Priority:** LOW  
**Objective:** OBJ-3  
**Prerequisites:** TC-007 passed

**Steps:**
1. Add 10 devices (Device 0-9) to configuration file
2. Ensure all have unique DeviceNumbers and valid configurations
3. Add corresponding AlpacaDevices entries
4. Save and restart application
5. Navigate to device manager

**Expected Results:**
- ✅ All 10 devices listed
- ✅ All devices independently connectable
- ✅ No performance degradation
- ✅ No memory leaks (monitor task manager over 5 minutes)

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-024: Empty Devices Array
**Priority:** MEDIUM  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup:**
1. Stop application
2. Open `appsettings.user.json`
3. Delete all entries from `Devices` array: `"Devices": []`
4. Delete all entries from `AlpacaDevices` array: `"AlpacaDevices": []`
5. Save file

**Steps:**
1. Launch application
2. Navigate to device manager

**Expected Results:**
- ✅ Application starts without crash
- ✅ Device manager shows empty list or "No devices configured" message
- ✅ Console logs indicate no devices available
- ✅ Application remains functional (can add devices manually later)

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-025: Very Long DeviceName
**Priority:** LOW  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup:**
1. Stop application
2. Change Device 0 DeviceName to 500-character string
3. Update corresponding AlpacaDevices entry
4. Save and restart

**Expected Results:**
- ✅ Application handles long name without truncation error
- ✅ UI displays name appropriately (may truncate for display but stores full name)
- ✅ No buffer overflow or exceptions

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

#### TC-026: Special Characters in DeviceName
**Priority:** LOW  
**Objective:** OBJ-6  
**Prerequisites:** TC-003 passed

**Setup:**
1. Change Device 0 DeviceName to include special characters:
   - "Test Mount <GEM> #1 [Primary] & Main 'Scope' 50%"
2. Update AlpacaDevices entry
3. Save and restart

**Expected Results:**
- ✅ DeviceName with special characters accepted
- ✅ JSON serialization handles escaping correctly
- ✅ UI displays special characters properly
- ✅ No XSS vulnerabilities in web UI

**Pass/Fail:** ____________  
**Notes:** ___________________________________________

---

## 📊 Test Execution Summary

**Test Execution Date:** _______________  
**Executed By:** _______________  
**Application Version:** _______________

### Test Results Overview

| Test Suite | Total Tests | Passed | Failed | Skipped | Pass Rate |
|------------|-------------|--------|--------|---------|-----------|
| Suite 1: First Launch & Default | 3 | ___ | ___ | ___ | ___% |
| Suite 2: Version Isolation | 2 | ___ | ___ | ___ | ___% |
| Suite 3: Multi-Device Support | 5 | ___ | ___ | ___ | ___% |
| Suite 4: 1-to-1 Synchronization | 4 | ___ | ___ | ___ | ___% |
| Suite 5: Error Handling | 5 | ___ | ___ | ___ | ___% |
| Suite 6: Runtime Behavior | 3 | ___ | ___ | ___ | ___% |
| Suite 7: Edge Cases | 4 | ___ | ___ | ___ | ___% |
| **TOTAL** | **26** | ___ | ___ | ___ | ___% |

---

## 🐛 Defect Tracking

### Critical Issues

| ID | Test Case | Description | Severity | Status |
|----|-----------|-------------|----------|--------|
| | | | | |

### High Priority Issues

| ID | Test Case | Description | Severity | Status |
|----|-----------|-------------|----------|--------|
| | | | | |

### Medium/Low Priority Issues

| ID | Test Case | Description | Severity | Status |
|----|-----------|-------------|----------|--------|
| | | | | |

---

## ✅ Sign-Off Criteria

**Phase 1 & 2 Testing Complete When:**
- [ ] All 26 test cases executed
- [ ] Pass rate ≥ 95% (25/26 tests passing)
- [ ] Zero critical defects open
- [ ] All high priority defects resolved or documented with workarounds
- [ ] Settings system functional for production use with manual workflow
- [ ] User guide validated against actual behavior

**Sign-Off:**

**Tester:** _______________________ **Date:** _______________  
**Developer:** _______________________ **Date:** _______________  
**Stakeholder:** _______________________ **Date:** _______________

---

## 📚 Reference Documents

- **Settings-System-Requirements.md** - Full requirements specification
- **Settings-System-HighLevel-Design.md** - Architecture documentation
- **Settings-System-Implementation-Status.md** - Implementation progress
- **Device-Management-User-Guide.md** - End-user manual workflow guide
- **appsettings.json** - Default configuration template
- **appsettings.schema.json** - JSON schema for validation

---

## 🔧 Troubleshooting Guide

### Common Issues During Testing

**Issue:** Application won't start after editing JSON  
**Solution:** Validate JSON syntax at [jsonlint.com](https://jsonlint.com/), check for missing commas/braces

**Issue:** Changes not taking effect  
**Solution:** Verify application fully restarted (not just refreshed browser). Check `LastModified` timestamp updated.

**Issue:** Device not appearing in list  
**Solution:** Verify `Enabled: true`, check DeviceNumber unique, verify AlpacaDevices entry exists

**Issue:** Synchronization errors on startup  
**Solution:** Use `ValidateSynchronization()` error messages to identify mismatch. Ensure DeviceNumber and DeviceName match between arrays.

**Issue:** Settings file not found  
**Solution:** Check path matches version exactly. Verify no typos in directory name. Check file permissions in %AppData%.

---

## 📝 Notes & Observations

Use this section for general testing notes, observations, suggestions for improvement, or documentation of unexpected behavior that doesn't constitute a defect.

_____________________________________________________________________________
_____________________________________________________________________________
_____________________________________________________________________________
_____________________________________________________________________________
_____________________________________________________________________________

---

**End of Test Plan**
