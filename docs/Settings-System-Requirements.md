# Settings System Requirements Document

**Project:** GreenSwamp Alpaca - Multi-Device Telescope Control  
**Component:** Settings & Configuration Management  
**Version:** Draft 0.1  
**Date:** 2025-01-XX  
**Status:** In Progress - Iterative Development

---

## 1. Purpose & Scope

This document defines requirements for the settings and configuration management system supporting multiple ASCOM Alpaca telescope devices with version-isolated, mode-specific configuration profiles.

**In Scope:**
- Device configuration storage and retrieval
- Version management and migration
- Template-based device creation
- Multi-mode support (AltAz, German Polar, Polar)
- Multi-mount support (Simulator, SkyWatcher)
- App-wide settings (server and monitor configuration)

**Out of Scope:**
- Runtime device state management
- Mount control algorithms
- ASCOM protocol implementation
- UI implementation (Blazor pages)

**Architecture Constraints:**
- **Logging:** All settings services MUST use existing `MonitorLog.LogToMonitor()` pattern (not `ILogger<T>`)
  - **Startup Timing:** MonitorQueue initializes AFTER VersionedSettingsService constructor runs
  - **Solution:** Use fallback logging pattern: try MonitorLog, catch and fallback to Console.WriteLine for early startup
  - **Safe After:** `MonitorQueue.EnsureInitialized()` called in Program.cs (post-DI-container-build)
- **Settings Flow:** Settings from JSON files populate device instances via `MountInstanceRegistry.CreateInstance(deviceNumber, settings, deviceName)` at application startup

---

## 1.1 Related UI Components

The following Blazor pages interact with the settings system:

| Page | Purpose | Settings Section |
|------|---------|------------------|
| **Server Settings** | Configure app-wide server options | `ServerSettings` section |
| **Monitor Settings** | Configure logging and monitoring | `MonitorSettings` section |
| **Device Management** | Add/remove/configure devices | `Devices` array |
| **Profile/Template Management** | Manage device templates | Templates system |

**Action Required:** Review these pages to extract complete property schemas for `ServerSettings` and `MonitorSettings`.

---

## 2. Functional Requirements

### FR-1: JSON-Based Configuration
**Priority:** CRITICAL  
**Requirement:** All settings SHALL be stored in JSON format.

**Acceptance Criteria:**
- Settings files use `.json` extension
- Valid JSON syntax per RFC 8259
- Human-readable formatting (indented, 2-4 spaces)
- UTF-8 encoding

---

### FR-2: Version Isolation
**Priority:** CRITICAL  
**Requirement:** Settings files SHALL be isolated by application version.

**Acceptance Criteria:**
- Each app version stores settings in separate directory
- Path format: `%AppData%/GreenSwampAlpaca/{version}/appsettings.user.json`
- Version string extracted from `AssemblyInformationalVersionAttribute`
- Build metadata (e.g., `+commitHash`) stripped from version path

---

### FR-3: Timestamp Tracking
**Priority:** HIGH  
**Requirement:** Settings files SHALL record creation and modification timestamps.

**Acceptance Criteria:**
- `CreatedDate` field (ISO 8601 UTC) written on file creation
- `LastModified` field (ISO 8601 UTC) updated on every save
- Timestamps preserved during migration
- Timestamps visible in JSON for troubleshooting

---

### FR-4: Property Migration Support
**Priority:** CRITICAL  
**Requirement:** System SHALL migrate settings when properties are added or removed between versions.

**Acceptance Criteria:**
- Detect previous version settings automatically
- Copy existing property values to new version
- Add new properties with sensible defaults
- Remove obsolete properties without error
- Log migration actions (info level)
- Failed migration does not prevent app startup

---

### FR-5: JSON Schema Validation
**Priority:** MEDIUM  
**Requirement:** Settings files SHALL have JSON schema for IDE validation and IntelliSense.

**Acceptance Criteria:**
- `appsettings.schema.json` file included in project
- Schema referenced via `$schema` property in settings files
- Covers all properties with types and descriptions
- Enum constraints for mode/mount selections
- Required vs optional properties specified

---

### FR-6: App-Wide Settings
**Priority:** CRITICAL  
**Requirement:** Application SHALL maintain device-independent configuration settings.

**Acceptance Criteria:**
- **Server Settings:** Configuration displayed/edited on "Server Settings" Blazor page
- **Monitor Settings:** Configuration displayed/edited on "Monitor Settings" Blazor page
- Settings apply to entire application, not specific devices
- Changes to monitor settings do not require device restart
- Changes to server settings require all devices to be disconnected and a user instruction to restart the app
- Stored separately from device-specific configuration

---

### FR-7: Per-Device Runtime Configuration
**Priority:** CRITICAL  
**Requirement:** Each Alpaca device SHALL have independent runtime configuration indexed by device number.

**Acceptance Criteria:**
- Runtime settings stored in `Devices` array in `appsettings.user.json`
- Each device has `DeviceNumber` property (0-indexed)
- Device number uniquely identifies device configuration
- Device number used for API routing (`/api/v1/telescope/{deviceNumber}/...`)
- Multiple devices can coexist in single settings file

---

### FR-8: Multi-Mode Support
**Priority:** CRITICAL  
**Requirement:** Each device SHALL support AltAz, German Polar, and Polar alignment modes.

**Acceptance Criteria:**
- `AlignmentMode` property with enum values: `AltAz`, `GermanPolar`, `Polar`
- Mode-specific properties present for all modes
- Unused properties retained (not null/omitted) for easy mode switching
- Mode change does not require device recreation

---

### FR-9: Multi-Mount Support
**Priority:** CRITICAL  
**Requirement:** Each device SHALL support Simulator and SkyWatcher mount types.

**Acceptance Criteria:**
- `Mount` property with enum values: `Simulator`, `SkyWatcher`
- Mount-specific properties present for both types
- Unused properties retained for easy mount switching
- Mount change may require reconnection but not device recreation

---

### FR-10: Complete Property Set
**Priority:** HIGH  
**Requirement:** All device properties SHALL be present in settings regardless of current mode/mount.

**Rationale:** Enables easy switching between modes/mounts without data loss.

**Acceptance Criteria:**
- All 142 SkySettings properties present in each device
- Properties for unused features have valid default values
- No `null` values for typed properties (use defaults: `0`, `false`, `""`, etc.)
- Mode/mount switching preserves all existing values

---

### FR-11: Template-Based Device Creation
**Priority:** HIGH  
**Requirement:** System SHALL provide template settings files for each alignment mode.

**Acceptance Criteria:**
- Templates available for: `AltAz`, `GermanPolar`, `Polar`
- Templates contain sensible defaults for common use cases
- Templates include all required properties
- Templates stored in: `GreenSwamp.Alpaca.Settings/Templates/` directory

---

### FR-12: Template Composition
**Priority:** HIGH  
**Requirement:** Templates SHALL be derived from common settings plus mode-specific overrides.

**Acceptance Criteria:**
- `common.json` contains properties shared across all modes
- `{mode}-overrides.json` contains mode-specific values
- Template merge logic: common + overrides = complete template
- Override values take precedence over common values
- `SettingsTemplateService` implements merge logic

---

### FR-13: Device Creation Methods
**Priority:** HIGH  
**Requirement:** New devices SHALL be creatable from templates or by copying existing devices.

**Acceptance Criteria:**
- **From Template:** Select mode → load template → assign device number → save
- **From Existing:** Select device → deep copy → change device number → save
- Both methods preserve all properties
- Device number validation prevents duplicates
- `Enabled` property defaults to `true` for new devices

---

### FR-14: Default Configuration
**Priority:** CRITICAL  
**Requirement:** Application SHALL ship with one complete default device in `appsettings.json`.

**Acceptance Criteria:**
- `Devices[0]` contains **all 142 SkySettings properties** with sensible defaults
- Device 0: German Polar mode, Simulator mount, enabled by default
- All `Can*` capabilities set to `true` (simulator supports all features)
- Optics defaults: 200mm aperture, 1260mm focal length (8" SCT)
- Location defaults: Configurable latitude/longitude  
- Used by `CreateDefaultDevice()` when no `appsettings.user.json` exists
- User can modify Device 0 or add additional devices via UI
- No hardcoded property defaults in C# code (all JSON-configurable per Q4)

---

### FR-15: AlpacaDevices Synchronization
**Priority:** CRITICAL  
**Requirement:** System SHALL maintain 1-to-1 synchronization between AlpacaDevices and Devices arrays.

**Acceptance Criteria:**
- Every entry in `AlpacaDevices` has matching entry in `Devices` (linked by `DeviceNumber`)
- Device creation adds entries to BOTH arrays atomically
- Device deletion removes from BOTH arrays atomically  
- `DeviceNumber` and `DeviceName` kept in sync across arrays
- Validation on startup: fail fast if arrays are out of sync
- `UniqueId` generated as GUID at device creation time (immutable)
- `ProfileName` property REMOVED from `AlpacaDevices` (see Analysis 1)

---

### FR-16: Device Activation
**Priority:** HIGH  
**Requirement:** Newly configured Alpaca devices SHALL require app restart to become active.

**Acceptance Criteria:**
- Device creation via UI saves to `appsettings.user.json`
- New devices do NOT become active until app restart
- UI displays message: "Restart application to activate new device"
- After restart, device appears in `/management/v1/configureddevices`
- Device advertised via Alpaca Discovery API after restart

---

## 3. Non-Functional Requirements

### NFR-1: Performance
- Settings load time: <500ms for file with 10 devices
- Settings save time: <200ms
- Migration time: <1 second per device

### NFR-2: Reliability
- Settings corruption: Fail fast with clear error message guiding user to delete file
- Migration failure: Log error, fall back to defaults
- Concurrent access: File locking prevents corruption

### NFR-3: Usability
- Error messages guide user to resolution (e.g., "Delete file at {path} to regenerate")
- Logging at Info level for normal operations
- Schema enables IntelliSense in VS Code / Visual Studio

### NFR-4: Maintainability
- Version migration logic isolated in single service
- Template composition logic testable independently
- Clear separation: defaults (appsettings.json) vs user overrides (appsettings.user.json)

---

## 4. Data Model

### 4.1 Four Distinct JSON Structures

The settings system uses **four separate data structures** with clear separation of concerns:

#### Structure 1: Setup/Creation Information (Templates)
- **Purpose:** Defines device configuration templates for creating new devices
- **Format:** `GreenSwamp.Alpaca.Settings` object or equivalent structure (see Q1)
- **Location:** `GreenSwamp.Alpaca.Settings/Templates/appsettings.json`
- **Usage:** Read by `SettingsTemplateService` to create new device configurations
- **Example:**
```json
{
  "GreenSwamp.Alpaca.Settings": {
    "AlignmentMode": "GermanPolar",
    "Mount": "Simulator",
    // ...template properties only...
  }
}
```

#### Structure 2: App-Wide Settings
- **Purpose:** Application-level configuration independent of devices
- **Sections:**
  - **GreenSwamp.Alpaca.ServerSettings:** Configuration from "Server Settings" Blazor page (port, host, etc.)
  - **GreenSwamp.Alpaca.MonitorSettings:** Configuration from "Monitor Settings" Blazor page (logging, monitoring)
- **Location:** Root level in `appsettings.json` and `appsettings.user.json`
- **Example:**
```json
{
  "GreenSwamp.Alpaca.ServerSettings": {
    // See "Server Settings" Blazor page
  },
  "GreenSwamp.Alpaca.MonitorSettings": {
    // See "Monitor Settings" Blazor page
    "LogMonitor": false,
    "LogSession": true,
    "StartMonitor": false
  }
}
```

#### Structure 3: Runtime Device Configurations
- **Purpose:** Device-specific runtime settings indexed by device number
- **Format:** `Devices` array
- **Location:** `appsettings.user.json` (user settings), `appsettings.json` (defaults)
- **Usage:** Read by `VersionedSettingsService.GetAllDevices()` at application startup
- **Example:**
```json
{
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "Enabled": true,
      "Mount": "Simulator",
      "AlignmentMode": "GermanPolar",
      // ...all 142 properties...
    }
  ]
}
```

#### Structure 4: Alpaca Device Discovery Metadata
- **Purpose:** ASCOM Alpaca device discovery information for management API
- **Format:** `AlpacaDevices` array indexed by device number
- **Location:** `appsettings.json` (may also be in runtime registry)
- **Usage:** Returned by `/management/v1/configureddevices` endpoint
- **Example:**
```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (AltAz)",
      "DeviceType": "Telescope",
      "ProfileName": "simulator-altaz",
      "UniqueId": "sim-altaz-12345678-1234-1234-1234-123456789abc"
    }
  ]
}
```

### 4.2 Settings File Complete Structure

Complete `appsettings.json` structure combining all concerns:

```json
{
  "$schema": "./appsettings.schema.json",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "AppVersion": "1.0.0",

  // Structure 4: Alpaca device discovery metadata
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "ProfileName": "simulator-gem",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    }
  ],

  // Structure 3: Runtime device configurations
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "Enabled": true,
      "Mount": "Simulator",
      "AlignmentMode": "GermanPolar",
      // ...all 142 properties...
    }
  ],

  // Structure 2: App-wide settings
  "GreenSwamp.Alpaca.ServerSettings": {
    // See "Server Settings" Blazor page
  },
  "GreenSwamp.Alpaca.MonitorSettings": {
    // See "Monitor Settings" Blazor page
    "ServerDevice": true,
    "Telescope": true,
    "LogMonitor": false,
    "LogSession": true
  }
}
```

### 4.3 User Settings File Structure

`appsettings.user.json` in `%AppData%/GreenSwampAlpaca/{version}/`:

```json
{
  "$schema": "./appsettings.schema.json",
  "Version": "1.0.0",
  "CreatedDate": "2025-01-15T12:34:56Z",
  "LastModified": "2025-01-15T14:22:10Z",

  // Structure 3: Runtime device configurations (user's devices)
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "My Custom Mount",
      "Enabled": true,
      // ...all 142 properties...
    }
  ],

  // Structure 2: App-wide settings (user overrides)
  "GreenSwamp.Alpaca.ServerSettings": {
    // User's server settings overrides
  },
  "GreenSwamp.Alpaca.MonitorSettings": {
    // User's monitor settings overrides
  }
}
```

### 4.4 Key Properties

| Property | Type | Values | Purpose |
|----------|------|--------|---------|
| `DeviceNumber` | int | 0-N | Unique device identifier, API routing |
| `DeviceName` | string | User-defined | Display name |
| `Enabled` | bool | true/false | Whether device loads at startup |
| `Mount` | enum | Simulator, SkyWatcher | Hardware type |
| `AlignmentMode` | enum | AltAz, GermanPolar, Polar | Mount configuration mode |

---

## 5. Use Cases

### UC-1: First Launch (No Settings)
1. User starts app for first time
2. System detects no `appsettings.user.json` exists
3. System creates file with single German Polar Simulator device
4. Device 0 loads and is ready for connection

### UC-2: Add Second Device from Template
1. User navigates to device management UI
2. User selects "Add Device" → "From Template"
3. User chooses "Polar" template
4. System loads template, assigns device number 1, saves
5. Device 1 appears in device list

### UC-3: Clone Existing Device
1. User selects device 0 in UI
2. User clicks "Duplicate"
3. System deep copies device 0 → device 2
4. User changes device name "Backup Mount"
5. System saves device 2 to settings

### UC-4: Switch Mount Type
1. User has device 0 configured as Simulator
2. User edits device 0, changes `Mount` to `SkyWatcher`
3. All properties retained (Port, BaudRate already present)
4. User reconnects device to apply new mount

### UC-5: Version Upgrade with Migration
1. User upgrades app from v1.0.0 to v1.1.0
2. System detects `%AppData%/GreenSwampAlpaca/1.0.0/appsettings.user.json`
3. System migrates settings to `1.1.0` directory
4. New property `CustomFeature` added with default value
5. Obsolete property `LegacyOption` removed
6. User settings preserved

---

## 6. Configuration File Responsibilities

The settings system uses **three configuration files** with distinct responsibilities:

### `appsettings.json` (Application Defaults)
- **Location:** `GreenSwamp.Alpaca.Server/appsettings.json`
- **Purpose:** Provides default values for all settings when no user settings exist
- **Contains:**
  - **Structure 2:** ServerSettings, MonitorSettings (app-wide defaults)
  - **Structure 3:** Devices array with single German Polar Simulator device (runtime defaults)
  - **Structure 4:** AlpacaDevices array (discovery metadata)
- **Properties:** All 142 SkySettings properties in default device
- **Read By:** `IConfiguration` → `VersionedSettingsService.CreateDefaultDevice()`
- **User Editable:** No (ships with app)

### `appsettings.user.json` (User Settings)
- **Location:** `%AppData%/GreenSwampAlpaca/{version}/appsettings.user.json`
- **Purpose:** User's device configurations and app-wide overrides
- **Contains:**
  - **Structure 2:** ServerSettings, MonitorSettings (user overrides)
  - **Structure 3:** Devices array with 0-N user devices (runtime configurations)
  - Version, CreatedDate, LastModified metadata
- **Properties:** All 142 SkySettings properties per device
- **Read By:** `VersionedSettingsService.GetAllDevices()`, `GetServerSettings()`, `GetMonitorSettings()`
- **User Editable:** Yes (via UI or direct edit)

### `Templates/appsettings.json` (Device Creation Templates)
- **Location:** `GreenSwamp.Alpaca.Settings/Templates/appsettings.json`
- **Purpose:** Embedded resource for template-based device creation
- **Contains:**
  - **Structure 1:** Setup/creation information (SkySettings object or similar)
  - Minimal property set (30-40) for template composition
- **Format:** **Separate from runtime Devices array** (see Q1 answer)
- **Read By:** `SettingsTemplateService` during device creation workflow
- **User Editable:** No (embedded in assembly)

### Separation of Concerns

| Structure | Purpose | Format | Location(s) |
|-----------|---------|--------|-------------|
| **1. Setup/Creation** | Device templates for creation | `SkySettings` object | Templates/appsettings.json |
| **2. App-Wide Settings** | Server & monitor config | Sections at root level | appsettings.json, appsettings.user.json |
| **3. Runtime Devices** | Active device configurations | `Devices` array | appsettings.json, appsettings.user.json |
| **4. Discovery Metadata** | Alpaca device registry | `AlpacaDevices` array | appsettings.json |

---

## 7. Open Questions

### Q1: Template File Format ✅ ANSWERED
**Question:** Should `Templates/appsettings.json` use `SkySettings` object or `Devices` array?

**Answer:** **Structure 1 (Setup/Creation) should use `SkySettings` object format.**

**Rationale:**
- Templates are for **device creation**, not runtime execution
- `Devices` array is **Structure 3** (runtime configurations) - inappropriate for templates
- Separation of concerns: setup/creation vs runtime
- Naming should reflect purpose (SkySettings = setup configuration)

**Decision:** Templates use `SkySettings` object format:
```json
{
  "SkySettings": {
    "AlignmentMode": "GermanPolar",
    "Mount": "Simulator",
    // ...minimal template properties...
  }
}
```

---

### Q2: AlpacaDevices Section ✅ ANSWERED
**Question:** What is the purpose of `AlpacaDevices` section in `appsettings.json`?

**Answer:** **Structure 4 - Alpaca device discovery metadata with full CRUD lifecycle**

**Purpose:**
- ASCOM Alpaca device discovery information
- Returned by `/management/v1/configureddevices` endpoint (Alpaca Management API)
- Required for ASCOM protocol compliance

**Lifecycle:**
- **Creation:** User creates device via UI (specification TBD)
- **UniqueId Generation:** GUID generated at device creation time
- **Synchronization:** MUST be kept in 1-to-1 sync with `Devices` array
- **Shared Key:** `DeviceNumber` links `AlpacaDevices` entry to corresponding `Devices` entry
- **CRUD Operations:** Full Create, Read, Update, Delete lifecycle supported
- **Activation:** App restart required to enable newly configured Alpaca devices
- **Discovery:** Advertised via Alpaca Discovery API after restart

**Synchronization Rules:**
- When device created → add entry to both `AlpacaDevices` AND `Devices` arrays
- When device deleted → remove from both arrays
- When device updated → sync `DeviceNumber`, `DeviceName` between arrays
- `DeviceNumber` is primary key for linking entries

**Properties:**
```json
{
  "DeviceNumber": 0,          // Shared key with Devices array
  "DeviceName": "My Mount",   // User-friendly name (synced with Devices)
  "DeviceType": "Telescope",  // ASCOM device type (always "Telescope")
  "ProfileName": "???",       // Usage unclear - see Analysis 1
  "UniqueId": "guid-value"    // GUID generated at creation, immutable
}
```

**Decision:** `ProfileName` property removed from structure (see Q2 resolution below).

---

### Q3: ServerSettings Structure
**Question:** What properties belong in `ServerSettings` section?

**Reference:** "Server Settings" Blazor page (currently open in workspace)

**Known Candidates:**
- Server host/port configuration
- API endpoint settings
- Security/authentication settings
- CORS configuration

**Action Required:** Review "Server Settings" Blazor page to extract exact property list.

**Decision Needed:** Complete `ServerSettings` schema definition.

---

### Q4: Property Merge Strategy ✅ ANSWERED
**Question:** How should `CreateDefaultDevice()` populate all 142 properties?

**Answer:** **All properties MUST be configurable from JSON (no hardcoded defaults)**

**Requirement:**
- All 142 SkySettings properties must be configurable via `appsettings.json`
- No property values hardcoded in C# code
- Enables changing defaults without recompilation
- Supports different default configurations (e.g., Northern vs Southern hemisphere)

**Decision:** `Devices[0]` in `appsettings.json` contains all 142 properties with complete defaults (see FR-14).

**Implementation:**
```csharp
private SkySettings CreateDefaultDevice()
{
    var settings = new SkySettings();
    _configuration.GetSection("Devices:0").Bind(settings);  // Complete binding

    // All properties populated from JSON
    return settings;
}
```

---

### Q5: Schema Generation ✅ ANSWERED
**Question:** How should `CreateDefaultDevice()` populate all 142 properties?

**Option A - IConfiguration Binding Only:**
- Bind from `Devices:0` in `appsettings.json`
- Requires `appsettings.json` to have all 142 properties

**Option B - Multi-Source Merge:**
- Bind core properties from `Devices:0`
- Apply capability defaults (`Can*` properties) from code
- Merge optics defaults from separate section

**Decision Needed:** ???

---

### Q5: Schema Generation ✅ ANSWERED
**Question:** Should JSON schema be auto-generated from `SkySettings` model?

**Answer:** **Option A - Manual Schema (for now)**

**Rationale:**
- JSON definition changes expected to be infrequent
- Manual schema provides full control over validation rules
- Auto-generation adds build complexity without clear benefit
- Can revisit if schema maintenance becomes burden

**Decision:**
- Handwrite `appsettings.schema.json`
- Maintain separately from `SkySettings` model
- Document schema update process in developer guide

---


---

## 8. Key Decisions

### Decision 1: ProfileName Property - REMOVED
**Rationale:**
- Not required by ASCOM Alpaca specification
- No operational value (all configuration in Devices array)
- Becomes stale after device modifications
- Unclear semantics for cloned devices
- Simplifies AlpacaDevices/Devices synchronization

**Final AlpacaDevices Structure:**
```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "guid-value"
    }
  ]
}
```  

### Decision 2: Complete Devices[0] with All 142 Properties - ACCEPTED
**Rationale:**
- Simplest architecture (single structure, no merging)
- Device 0 IS the default device (clear semantics)
- Consistent with appsettings.user.json format
- All properties JSON-configurable (no hardcoded defaults)
- File size (~500 lines) acceptable for shipped configuration

**Implementation:**
```json
{
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "Enabled": true,
      // ...all 142 SkySettings properties with complete defaults...
      "CanPark": true,
      "CanSlew": true,
      "ApertureDiameter": 0.2,
      "FocalLength": 1.26
    }
  ]
}
```  

**Default Values (Simulator GermanPolar mode):**
- All `Can*` capabilities = `true` (simulator supports all features)
- Optics: 200mm aperture, 1260mm focal length (8" SCT)
- Location: Configurable latitude/longitude
- Backlash: 0 (simulator has no mechanical backlash)

---

## 9. Next Steps

1. ? **Q1 Answered:** Templates use `SkySettings` object format (Structure 1 - setup/creation)
2. ? **Q2 Answered:** `AlpacaDevices` full CRUD lifecycle, 1-to-1 sync via `DeviceNumber`, `ProfileName` removed
3. ? **Q4 Answered:** All 142 properties JSON-configurable via complete `Devices[0]` in appsettings.json
4. ? **Q5 Answered:** Manual schema (Option A) - infrequent changes expected
5. ? **Decisions Accepted:** ProfileName removal + complete Devices[0] approach finalized
6. ?? **Review Complete:** User has approved both recommendations
7. ? **Extract UI Schemas:** Review "Server Settings" and "Monitor Settings" Blazor pages (Q3)
8. ? **Create High-Level Design:** Architecture document showing four structures and component interactions
9. ? **Design Data Flow:** Diagram showing settings flow from defaults ? templates ? user files ? runtime
10. ? **Implementation Plan:** Break work into steps with verification criteria

---

## 10. Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 0.1 | 2026-03-25 | Copilot | Initial draft from user requirements |
| 0.2 | 2026-03-25 | Copilot | Added Q1/Q2 answers, four-structure architecture, app-wide settings |
| 0.3 | 2026-03-25 | Copilot | Q2/Q4/Q5 answers, technical analyses, FR-15/FR-16 added |
| 0.4 | 2026-03-25 | Copilot | User accepted both recommendations, removed technical analyses, finalized decisions |

---

**Instructions for Next Phase:**
1. Review finalized requirements document
2. Proceed to high-level design document creation
3. Design data flow diagrams
4. Create implementation plan with verification steps

