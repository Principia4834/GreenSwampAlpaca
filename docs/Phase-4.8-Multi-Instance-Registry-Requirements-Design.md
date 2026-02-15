# Phase 4.8 + 4.8.1: Multi-Instance Telescope Registry
## Combined Requirements and Design Document

**Document Version:** 1.0  
**Date:** 2024  
**Project:** GreenSwamp Alpaca Server  
**Author:** System Architect  
**Status:** For Review

---

## Executive Summary

This document defines the requirements and design for implementing a multi-instance telescope registry that enables Green Swamp Alpaca Server to present multiple telescope devices (simulators and/or physical mounts) through a single Alpaca endpoint.

**Phase Scope:**
- **Phase 4.8:** Static configuration - Load devices from appsettings.json at startup
- **Phase 4.8.1:** Basic dynamic API - Enable add/remove devices at runtime via REST API

**Rationale for Combined Implementation:**
Adding Phase 4.8.1 alongside 4.8 adds only 1-2 hours of development time but eliminates 50-100 minutes of wasted restart time during Phase 4.9-4.11 development and unblocks Phase 4.11 UI development.

---

## 1. Requirements

### 1.1 Functional Requirements

#### FR-1: Multi-Instance Registry
**Priority:** CRITICAL  
**Description:** System SHALL maintain a registry of multiple `MountInstance` objects indexed by device number.

**Acceptance Criteria:**
- Registry supports device numbers 0 to 2,147,483,647
- Each device number maps to exactly one `MountInstance`
- Registry is thread-safe for concurrent access
- Registry persists for application lifetime

#### FR-2: Static Device Loading
**Priority:** CRITICAL  
**Description:** System SHALL load device configurations from `appsettings.json` at application startup.

**Acceptance Criteria:**
- Devices defined in configuration are loaded on startup
- Invalid configurations are logged but don't prevent startup
- Each device is assigned unique UID that persists across restarts
- Settings profiles are loaded from separate JSON files

#### FR-3: Alpaca Management API Compliance
**Priority:** CRITICAL  
**Description:** System SHALL implement standard Alpaca Management API endpoints per ASCOM specification.

**Acceptance Criteria:**
- `/management/v1/configureddevices` returns all registered devices
- Each device entry includes: DeviceName, DeviceType, DeviceNumber, UniqueID
- Device list updates immediately when devices are added/removed
- JSON format matches Alpaca specification (case-sensitive keys)

#### FR-4: Device Number Routing
**Priority:** CRITICAL  
**Description:** Telescope API calls SHALL route to correct `MountInstance` based on device number in URL.

**Acceptance Criteria:**
- `/api/v1/telescope/{deviceNumber}/...` routes to correct instance
- Invalid device numbers return HTTP 400 with clear error message
- Routing adds minimal latency (<1ms)
- Thread-safe routing under concurrent requests

#### FR-5: Dynamic Device Addition (Phase 4.8.1)
**Priority:** HIGH  
**Description:** System SHALL support adding new telescope devices at runtime via REST API.

**Acceptance Criteria:**
- `POST /setup/devices` creates and registers new device
- New device immediately appears in `/management/v1/configureddevices`
- ASCOM clients can connect to new device without server restart
- Device number validation prevents duplicates

#### FR-6: Dynamic Device Removal (Phase 4.8.1)
**Priority:** HIGH  
**Description:** System SHALL support removing telescope devices at runtime via REST API.

**Acceptance Criteria:**
- `DELETE /setup/devices/{deviceNumber}` removes device
- Connected devices are gracefully disconnected before removal
- Removed device immediately disappears from management API
- Device number becomes available for reuse

#### FR-7: Profile Management (Phase 4.8.1)
**Priority:** MEDIUM
**Description:** System SHALL provide API to list available settings profiles.

**Acceptance Criteria:**
- `GET /setup/profiles` returns list of available profiles
- Profile list includes profile names and paths
- Profiles are discovered from `profiles/` directory
- Invalid profiles are excluded from list

### 1.2 Non-Functional Requirements

#### NFR-1: Performance
- Registry lookup: <1ms per operation
- Device addition: <100ms total time
- Device removal: <500ms total time (including disconnect)
- No degradation with 100+ devices registered

#### NFR-2: Reliability
- Registry operations are atomic
- Partial failures don't corrupt registry state
- Invalid configurations are rejected with clear errors
- System recovers gracefully from profile load failures

#### NFR-3: Maintainability
- Clear separation between static and dynamic logic
- Shared code for device creation used by both paths
- Comprehensive logging at INFO level for operations
- ERROR level logs for failures with actionable messages

#### NFR-4: ASCOM Standard Compliance
- Device numbers start at 0 per device type
- UIDs are globally unique and persistent
- Management API JSON matches specification exactly
- HTTP status codes follow Alpaca specification

---

## 2. Architecture

### 2.1 Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Alpaca Device Server                      │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │            Standard Alpaca APIs (Read-Only)           │  │
│  ├───────────────────────────────────────────────────────┤  │
│  │  /management/v1/configureddevices  (GET)              │  │
│  │  /management/v1/description        (GET)              │  │
│  │  /api/v1/telescope/{device}/...    (GET/PUT)          │  │
│  └───────────────────────────────────────────────────────┘  │
│                            ↓                                  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │              Telescope Controller                      │  │
│  │  - Routes by device number                            │  │
│  │  - Extracts ClientID, TransactionID                   │  │
│  │  - Returns standard Alpaca JSON                       │  │
│  └───────────────────────────────────────────────────────┘  │
│                            ↓                                  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │          MountInstanceRegistry (NEW - 4.8)            │  │
│  │  ┌──────────────────────────────────────────────┐    │  │
│  │  │  Dictionary<int, MountInstance>              │    │  │
│  │  │  - Thread-safe operations                    │    │  │
│  │  │  - GetInstance(deviceNumber)                 │    │  │
│  │  │  - CreateInstance(...)                       │    │  │
│  │  │  - RemoveInstance(deviceNumber)              │    │  │
│  │  └──────────────────────────────────────────────┘    │  │
│  └───────────────────────────────────────────────────────┘  │
│                            ↓                                  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │              MountInstance Objects                     │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐            │  │
│  │  │Device 0  │  │Device 1  │  │Device 2  │            │  │
│  │  │Sim(AltAz)│  │Sim(GEM)  │  │EQ6-Pro   │            │  │
│  │  └──────────┘  └──────────┘  └──────────┘            │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │       Custom Setup API (NEW - 4.8.1 - Write)          │  │
│  ├───────────────────────────────────────────────────────┤  │
│  │  /setup/devices          (GET, POST)                  │  │
│  │  /setup/devices/{id}     (DELETE)                     │  │
│  │  /setup/profiles         (GET)                        │  │
│  └───────────────────────────────────────────────────────┘  │
│                            ↓                                  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │           DeviceManager (ASCOM.Alpaca.Razor)          │  │
│  │  - Maintains device metadata for discovery            │  │
│  │  - Provides list to /configureddevices endpoint       │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                               │
└─────────────────────────────────────────────────────────────┘

   ┌────────────────────────────────────────────────────┐
   │         Configuration Sources                       │
   ├────────────────────────────────────────────────────┤
   │  appsettings.json     → Static startup devices     │
   │  profiles/*.json      → Device settings templates  │
   │  Runtime API calls    → Dynamic add/remove         │
   └────────────────────────────────────────────────────┘
```

### 2.2 Data Flow

#### 2.2.1 Startup Device Loading (Phase 4.8)

```
┌──────────┐      ┌────────────┐      ┌────────────────┐      ┌──────────────┐
│ Program  │──1──→│appsettings │──2──→│ Settings       │──3──→│ Registry     │
│  .cs     │      │  .json     │      │ Service        │      │              │
└──────────┘      └────────────┘      └────────────────┘      └──────────────┘
                                              │                        │
                                              │4. Load Profile         │
                                              ↓                        │
                                       ┌────────────────┐              │
                                       │profiles/       │              │
                                       │sim-gem.json    │              │
                                       └────────────────┘              │
                                              │                        │
                                              │5. Create Instance      │
                                              └───────────────────────→│
                                                                       │
                                                     6. Register       │
                                                          ↓            │
                                              ┌────────────────────────┘
                                              │
                                              ↓
                                       ┌──────────────┐
                                       │DeviceManager │
                                       │.LoadTelescope│
                                       └──────────────┘
```

**Steps:**
1. `Program.cs` reads `AlpacaDevices` section from `appsettings.json`
2. For each device config, extract `DeviceNumber`, `DeviceName`, `ProfileName`, `UniqueId`
3. Call `IVersionedSettingsService.LoadProfile(profileName)` to load settings
4. Settings service loads JSON from `profiles/{profileName}.json`
5. Call `MountInstanceRegistry.CreateInstance(deviceNumber, settings, name)`
6. Registry creates new `MountInstance` and stores in dictionary
7. Call `DeviceManager.LoadTelescope(deviceNumber, telescope, name, uid)`
8. DeviceManager stores metadata for `/configureddevices` endpoint

#### 2.2.2 Runtime Device Addition (Phase 4.8.1)

```
┌──────────┐     ┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  Client  │─1──→│Admin        │─2──→│ Settings     │─3──→│  Registry   │
│(curl/UI) │     │Controller   │     │ Service      │     │             │
└──────────┘     └─────────────┘     └──────────────┘     └─────────────┘
                        │                    │                     │
                        │4. Validate         │                     │
                        ↓                    ↓                     │
                        ✓              Profile Exists?            │
                        │                    │                     │
                        │←───────────────────┘                    │
                        │                                          │
                        │5. Create Instance                        │
                        └─────────────────────────────────────────→│
                                                                    │
                                      6. Register with DeviceManager│
                                                 ↓                  │
                                      ┌──────────────────────┐     │
                                      │  DeviceManager       │←────┘
                                      │  .LoadTelescope()    │
                                      └──────────────────────┘
                                                 │
                                      7. Return Success
                                                 ↓
┌──────────┐                        ┌─────────────────┐
│  Client  │←───────────────────────│  HTTP 200 OK    │
│          │                        │  {deviceNumber} │
└──────────┘                        └─────────────────┘
```

**Steps:**
1. Client sends `POST /setup/devices` with JSON body
2. `SetupDevicesController` validates request
3. Load profile settings via `LoadProfile()`
4. Validate device number is not in use
5. Create `MountInstance` via registry
6. Register with `DeviceManager`
7. Return success response with device number
8. New device immediately available to ASCOM clients

#### 2.2.3 ASCOM Client Request Routing

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────┐
│ASCOM Client │─1──→│ Telescope    │─2──→│   Registry   │─3──→│ Mount    │
│  (N.I.N.A.) │     │ Controller   │     │              │     │ Instance │
└─────────────┘     └──────────────┘     └──────────────┘     └──────────┘
                           │                     │                   │
     GET /api/v1/          │                     │                   │
     telescope/1/          │                     │                   │
     rightascension        │                     │                   │
                           │                     │                   │
                           │ Extract device=1    │                   │
                           │                     │                   │
                           │ GetInstance(1)      │                   │
                           └────────────────────→│                   │
                                                 │                   │
                                      Found MountInstance(1)?        │
                                                 │                   │
                                                 │ YES               │
                                                 └──────────────────→│
                                                                     │
                                                  Call RightAscension│
                                                                     │
                                             ┌───────────────────────┘
                                             │
                                             │ Return value
                                             ↓
                           ┌──────────────────────────────────┐
                           │  JSON Response                   │
                           │  {                               │
                           │    "Value": 12.345,              │
                           │    "ErrorNumber": 0,             │
                           │    "ErrorMessage": ""            │
                           │  }                               │
                           └──────────────────────────────────┘
```

---

## 3. Component Specifications

### 3.1 MountInstanceRegistry

**File:** `GreenSwamp.Alpaca.MountControl/MountInstanceRegistry.cs`

**Purpose:** Central registry for managing multiple telescope device instances.

#### 3.1.1 Properties

```csharp
private static readonly Dictionary<int, MountInstance> _instances;
private static readonly object _lock = new object();
```

**Thread Safety:** All public methods use `lock(_lock)` for thread safety.

#### 3.1.2 Public Methods

##### CreateInstance()

```csharp
public static void CreateInstance(
    int deviceNumber, 
    SkySettingsInstance settings, 
    string deviceName)
```

**Purpose:** Create and register a new mount instance.

**Preconditions:**
- `deviceNumber` >= 0
- `deviceNumber` not already in use
- `settings` is valid configuration
- `deviceName` is non-empty

**Postconditions:**
- New `MountInstance` created with provided settings
- Instance stored in registry at `deviceNumber`
- Instance name set to `deviceName`

**Thread Safety:** Synchronized via `_lock`

**Exceptions:**
- `ArgumentException` if device number already exists
- `ArgumentNullException` if settings or name is null
- `ArgumentOutOfRangeException` if device number < 0

##### GetInstance()

```csharp
public static MountInstance? GetInstance(int deviceNumber)
```

**Purpose:** Retrieve mount instance by device number.

**Returns:** 
- `MountInstance` if found
- `null` if device number not registered

**Thread Safety:** Synchronized via `_lock`

**Performance:** O(1) dictionary lookup

##### RemoveInstance()

```csharp
public static bool RemoveInstance(int deviceNumber)
```

**Purpose:** Remove mount instance from registry.

**Behavior:**
1. Check if instance exists
2. If connected, call `instance.Disconnect()`
3. Remove from dictionary
4. Return success/failure

**Returns:**
- `true` if removed
- `false` if device number not found

**Thread Safety:** Synchronized via `_lock`

##### GetAllInstances()

```csharp
public static IReadOnlyDictionary<int, MountInstance> GetAllInstances()
```

**Purpose:** Get snapshot of all registered instances.

**Returns:** Read-only dictionary copy (defensive copy)

**Use Cases:**
- Admin UI displaying device list
- Health checks
- Diagnostics

##### IsDeviceNumberAvailable()

```csharp
public static bool IsDeviceNumberAvailable(int deviceNumber)
```

**Purpose:** Check if device number can be used.

**Returns:** `true` if available, `false` if in use

##### GetNextAvailableDeviceNumber()

```csharp
public static int GetNextAvailableDeviceNumber()
```

**Purpose:** Find next unused device number.

**Algorithm:** Sequential search from 0 until gap found

**Returns:** First available device number

---

### 3.2 Configuration Models

**File:** `GreenSwamp.Alpaca.Server/Models/AlpacaDeviceConfig.cs`

#### 3.2.1 AlpacaDeviceConfig

```csharp
public class AlpacaDeviceConfig
{
    public int DeviceNumber { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = "Telescope";
    public string ProfileName { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
}
```

**Purpose:** Configuration model for device definition in appsettings.json

**Validation Rules:**
- `DeviceNumber` >= 0
- `DeviceName` non-empty, max 100 chars
- `DeviceType` must be "Telescope" (future: other types)
- `ProfileName` must match existing profile file
- `UniqueId` must be globally unique GUID format

#### 3.2.2 AddDeviceRequest

```csharp
public class AddDeviceRequest
{
    public int DeviceNumber { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string? UniqueId { get; set; }  // Optional, generated if null
}
```

**Purpose:** Request model for `POST /setup/devices`

**Default Behavior:**
- If `UniqueId` is null, generate new GUID
- If `DeviceNumber` is 0, use `GetNextAvailableDeviceNumber()`

#### 3.2.3 SkySettingsInstance (Extended for Physical Mounts)

**File:** `GreenSwamp.Alpaca.Settings/Models/SkySettingsInstance.cs`

**Purpose:** Settings data model containing telescope configuration including serial communication for physical mounts.

**Key Properties:**

```csharp
public class SkySettingsInstance
{
    // Existing properties (partial list)
    public AlignmentMode AlignmentMode { get; set; }
    public MountType Mount { get; set; }
    public bool CanPark { get; set; }
    public bool CanFindHome { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Elevation { get; set; }

    // NEW: Serial Communication Properties for Physical Mounts
    public string? ComPort { get; set; }              // e.g., "COM3" or "/dev/ttyUSB0"
    public int? BaudRate { get; set; }                // e.g., 9600, 115200
    public string? SerialProtocol { get; set; }       // e.g., "SkyWatcher", "Meade"
    public int ConnectionTimeout { get; set; } = 5000; // Milliseconds
    public bool UseRtsCts { get; set; } = false;      // Hardware flow control
}
```

**Validation Rules:**

| Property | Required For | Validation |
|----------|--------------|------------|
| `ComPort` | Physical mounts (Mount != Simulator) | Must be non-empty string; format: "COMn" (Windows) or "/dev/tty*" (Linux) |
| `BaudRate` | Physical mounts (Mount != Simulator) | Must be positive integer; typical values: 9600, 19200, 38400, 57600, 115200 |
| `SerialProtocol` | Physical mounts (Mount != Simulator) | Must match supported protocols; e.g., "SkyWatcher", "Meade" |
| `ConnectionTimeout` | All devices | Must be > 0; default: 5000ms |
| `UseRtsCts` | Physical mounts | Boolean; default: false |

**Usage Notes:**
- For **Simulator** mounts: `ComPort`, `BaudRate`, and `SerialProtocol` should be `null` or omitted
- For **Physical** mounts: `ComPort` and `BaudRate` are **REQUIRED**
- **ComPort Conflict Detection:** Admin API must verify ComPort is not in use by another device before creating instance
- **Cross-Platform Paths:** Windows uses "COM3", Linux uses "/dev/ttyUSB0" - validation must accept both formats

---

### 3.3 Settings Service Extensions

**File:** `GreenSwamp.Alpaca.Settings/Services/IVersionedSettingsService.cs`

#### 3.3.1 New Interface Methods

```csharp
public interface IVersionedSettingsService
{
    // Existing
    SkySettingsInstance GetSettings();
    void SaveSettings(SkySettingsInstance settings);
    
    // NEW - Phase 4.8
    SkySettingsInstance LoadProfile(string profileName);
    
    // NEW - Phase 4.8.1
    void SaveProfile(string profileName, SkySettingsInstance settings);
    IEnumerable<string> GetAvailableProfiles();
}
```

#### 3.3.2 Profile Storage

**Location:** `%AppData%/GreenSwampAlpaca/{version}/profiles/{profileName}.json`

**Structure:**
```
%AppData%/GreenSwampAlpaca/1.0.0/
├── appsettings.user.json          (Default settings)
└── profiles/
    ├── simulator-altaz.json        (AltAz simulator profile)
    ├── simulator-gem.json          (GEM simulator profile)
    ├── simulator-polar.json        (Polar simulator profile)
    ├── eq6-production.json         (EQ6-Pro physical mount)
    └── custom-test.json            (User-created profile)
```

**Profile JSON Format:**
```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "Simulator",
  "CanPark": true,
  "CanFindHome": true,
  "CanSetTracking": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50,
  "TrackingRate": "Sidereal"
}
```

---

### 3.4 Telescope Controller Updates

**File:** `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`

#### 3.4.1 Current State

```csharp
public class Telescope
{
    // Currently uses static SkyServer instance
    private static SkyServer? _skyServer;
}
```

#### 3.4.2 New State (Phase 4.8)

```csharp
public class Telescope
{
    private readonly int _deviceNumber;
    private readonly ILogger<Telescope> _logger;
    
    public Telescope(int deviceNumber)
    {
        _deviceNumber = deviceNumber;
        // Logger injected via DI
    }
    
    private MountInstance GetInstance()
    {
        var instance = MountInstanceRegistry.GetInstance(_deviceNumber);
        if (instance == null)
        {
            throw new InvalidOperationException(
                $"Device number {_deviceNumber} not found");
        }
        return instance;
    }
}
```

#### 3.4.3 Method Pattern

**Every ASCOM method follows this pattern:**

```csharp
public double RightAscension
{
    get
    {
        var instance = GetInstance();  // Lookup by device number
        return instance.RightAscension;  // Delegate to instance
    }
}
```

**Error Handling:**
- Invalid device number → HTTP 400 (caught by middleware)
- Mount not connected → Alpaca error 0x407 with HTTP 200
- Other errors → Alpaca error codes with HTTP 200

---

### 3.5 Setup API Controller

**File:** `GreenSwamp.Alpaca.Server/Controllers/SetupDevicesController.cs`

**Integration Notes:** This controller integrates with the existing Swagger/OpenAPI configuration in `StartupHelpers.cs`. By using `[ApiExplorerSettings(GroupName = "AlpacaSetup")]`, all endpoints automatically appear in the existing "Alpaca HTML Endpoints - v1" Swagger group alongside other setup/configuration APIs.

#### 3.5.1 Endpoints

##### POST /setup/devices

**Purpose:** Add new telescope device

**Request:**
```json
{
  "deviceNumber": 2,
  "deviceName": "Simulator (Polar)",
  "profileName": "simulator-polar"
}
```

**Response (Success - HTTP 200):**
```json
{
  "deviceNumber": 2,
  "deviceName": "Simulator (Polar)",
  "uniqueId": "sim-polar-11111111-2222-3333-4444-555555555555",
  "message": "Device added successfully"
}
```

**Response (Error - HTTP 400):**
```json
{
  "error": "Device number 2 already exists"
}
```

**Response (Error - HTTP 404):**
```json
{
  "error": "Profile 'simulator-polar' not found"
}
```

**Implementation Logic:**
1. Validate request model
2. Check device number availability
3. Load profile from settings service
4. **Validate physical mount requirements:**
   - If `Mount` is not "Simulator", verify `ComPort` is specified
   - If `Mount` is not "Simulator", verify `BaudRate` is specified and valid
   - If `ComPort` is specified, check it is not already in use by another device
   - Validate `ComPort` format ("COMn" on Windows, "/dev/tty*" on Linux)
5. Create unique ID if not provided
6. Create mount instance via registry
7. Register with DeviceManager
8. Log operation at INFO level
9. Return success response with serial communication details

**Physical Mount Validation Code:**
```csharp
// After loading profile:
var settings = settingsService.LoadProfile(request.ProfileName);

// Validate physical mount requirements
if (settings.Mount != MountType.Simulator)
{
    if (string.IsNullOrEmpty(settings.ComPort))
        return BadRequest("Physical mount requires ComPort");

    if (!settings.BaudRate.HasValue || settings.BaudRate <= 0)
        return BadRequest("Physical mount requires valid BaudRate");

    // Check ComPort conflict
    var instances = MountInstanceRegistry.GetAllInstances();
    foreach (var kvp in instances)
    {
        if (kvp.Value.Settings.ComPort?.Equals(settings.ComPort, 
            StringComparison.OrdinalIgnoreCase) == true)
        {
            return BadRequest($"ComPort {settings.ComPort} is already in use by device {kvp.Key}");
        }
    }
}
```

##### DELETE /setup/devices/{deviceNumber}

**Purpose:** Remove telescope device

**Response (Success - HTTP 200):**
```json
{
  "message": "Device 2 removed successfully"
}
```

**Response (Error - HTTP 404):**
```json
{
  "error": "Device 2 not found"
}
```

**Implementation Logic:**
1. Check device exists
2. Get instance from registry
3. If connected, call `Disconnect()`
4. Remove from registry
5. Remove from DeviceManager
6. Log operation at INFO level
7. Return success response

##### GET /setup/devices

**Purpose:** List all configured devices (with more detail than standard API)

**Response (HTTP 200):**
```json
[
  {
    "deviceNumber": 0,
    "deviceName": "Simulator (AltAz)",
    "connected": false,
    "alignmentMode": "AltAz",
    "mountType": "Simulator",
    "comPort": null,
    "baudRate": null
  },
  {
    "deviceNumber": 1,
    "deviceName": "EQ6-Pro Observatory",
    "connected": true,
    "alignmentMode": "GermanPolar",
    "mountType": "SkyWatcher",
    "comPort": "COM3",
    "baudRate": 9600,
    "serialProtocol": "SkyWatcher"
  },
  {
    "deviceNumber": 2,
    "deviceName": "AZ-GTi Field Mount",
    "connected": false,
    "alignmentMode": "AltAz",
    "mountType": "SkyWatcher",
    "comPort": "/dev/ttyUSB0",
    "baudRate": 9600,
    "serialProtocol": "SkyWatcher"
  }
]
```

##### GET /setup/profiles

**Purpose:** List available settings profiles

**Response (HTTP 200):**
```json
[
  {
    "profileName": "simulator-altaz",
    "path": "profiles/simulator-altaz.json"
  },
  {
    "profileName": "simulator-gem",
    "path": "profiles/simulator-gem.json"
  },
  {
    "profileName": "eq6-production",
    "path": "profiles/eq6-production.json"
  }
]
```

#### 3.5.2 Swagger Documentation

The `SetupDevicesController` is fully integrated with the existing Swagger/OpenAPI infrastructure configured in `ASCOM.Alpaca.Razor/StartupHelpers.cs`. This provides automatic API documentation, interactive testing, and client code generation capabilities.

**Controller Attributes:**

| Attribute | Value | Purpose |
|-----------|-------|---------|
| `[ServiceFilter(typeof(AuthorizationFilter))]` | - | Applies authentication/authorization |
| `[ApiExplorerSettings(GroupName = "AlpacaSetup")]` | "AlpacaSetup" | Groups endpoints in "Alpaca HTML Endpoints - v1" Swagger section |
| `[ApiController]` | - | Enables automatic model validation and API behaviors |
| `[Route("setup")]` | "setup" | Base route for all endpoints |
| `[Produces(MediaTypeNames.Application.Json)]` | JSON | Declares response content type |

**XML Documentation Comments:**

All public methods must include XML comments for auto-generated Swagger descriptions:

```csharp
/// <summary>
/// Adds a new telescope device to the system at runtime.
/// </summary>
/// <param name="request">Device configuration including device number, name, and profile</param>
/// <returns>Device registration confirmation with assigned unique ID</returns>
/// <response code="200">Device successfully added and registered</response>
/// <response code="400">Invalid request or device number conflict</response>
/// <response code="404">Specified profile not found</response>
[HttpPost("devices")]
[ProducesResponseType(typeof(AddDeviceResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public async Task<IActionResult> AddDevice([FromBody] AddDeviceRequest request)
{
    // Implementation
}
```

**Request/Response Model Documentation:**

```csharp
/// <summary>
/// Request model for adding a new telescope device
/// </summary>
public class AddDeviceRequest
{
    /// <summary>
    /// Device number (0-99). Use 0 for auto-assignment.
    /// </summary>
    /// <example>2</example>
    public int DeviceNumber { get; set; }

    /// <summary>
    /// Display name for the device
    /// </summary>
    /// <example>Simulator (Polar)</example>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the settings profile to load (without .json extension)
    /// </summary>
    /// <example>simulator-polar</example>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Optional unique identifier. Generated automatically if not provided.
    /// </summary>
    /// <example>sim-polar-12345678-1234-1234-1234-123456789abc</example>
    public string? UniqueId { get; set; }
}
```

**Swagger UI Access:**

Navigate to: `http://localhost:31426/swagger`

**Swagger Group Selection:**

The Swagger UI displays three groups in a dropdown:
- **Alpaca JSON Endpoints - v1** (Standard ASCOM Alpaca device APIs)
- **Alpaca HTML Endpoints - v1** ← *Device management API appears here*
- **OmniSim Only Endpoints** (Simulator-specific)

**Interactive Testing:**

Each endpoint displays:
- **Parameters:** All request parameters with descriptions and examples
- **Request Body:** JSON schema with sample values
- **Responses:** All possible HTTP status codes with example responses
- **Try it out:** Interactive button to execute requests directly from browser
- **Execute:** Sends real HTTP request to the server
- **Response:** Shows actual server response with headers and body

**OpenAPI Specification Export:**

Download the complete OpenAPI 3.0 specification:
- **URL:** `http://localhost:31426/swagger/AlpacaSetup/swagger.json`
- **Use Cases:** Generate client SDKs, import into Postman, API documentation tools

**Benefits:**

| Benefit | Description |
|---------|-------------|
| **Self-Documenting** | XML comments automatically generate API descriptions |
| **Interactive Testing** | Test APIs directly from browser without external tools |
| **Client Generation** | OpenAPI spec enables automatic client code generation (C#, Python, TypeScript, etc.) |
| **Schema Validation** | Request/response schemas clearly defined and validated |
| **Version Control** | API changes automatically reflected in documentation |
| **Standards Compliance** | OpenAPI 3.0 standard ensures compatibility with ecosystem tools |
| **Zero Configuration** | Leverages existing `StartupHelpers.ConfigureSwagger()` setup |

**Integration Notes:**

- No additional Swagger configuration required in `Program.cs`
- XML documentation file automatically included via existing configuration
- All endpoints automatically discovered via `[ApiController]` attribute
- Consistent with existing ASCOM Alpaca Razor library patterns

---

### 3.6 Startup Configuration

**File:** `GreenSwamp.Alpaca.Server/Program.cs`

#### 3.6.1 Device Loading Section

```csharp
// After app is built
var app = builder.Build();

// Get settings service from DI
var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Load device configuration from appsettings.json
var deviceConfigs = builder.Configuration
    .GetSection("AlpacaDevices")
    .Get<List<AlpacaDeviceConfig>>();

if (deviceConfigs == null || !deviceConfigs.Any())
{
    logger.LogWarning("No devices configured in appsettings.json");
}
else
{
    foreach (var config in deviceConfigs)
    {
        try
        {
            // Load settings profile
            var settings = settingsService.LoadProfile(config.ProfileName);
            
            // Create and register instance
            MountInstanceRegistry.CreateInstance(
                config.DeviceNumber,
                settings,
                config.DeviceName
            );
            
            // Register with ASCOM DeviceManager
            DeviceManager.LoadTelescope(
                config.DeviceNumber,
                new Telescope(config.DeviceNumber),
                config.DeviceName,
                config.UniqueId
            );
            
            logger.LogInformation(
                "Registered device {DeviceNumber}: {DeviceName} using profile {ProfileName}",
                config.DeviceNumber,
                config.DeviceName,
                config.ProfileName
            );
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, 
                "Profile '{ProfileName}' not found for device {DeviceNumber}",
                config.ProfileName,
                config.DeviceNumber
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to register device {DeviceNumber}: {DeviceName}",
                config.DeviceNumber,
                config.DeviceName
            );
        }
    }
}

app.Run();
```

---

## 4. Configuration Specifications

### 4.1 appsettings.json Structure

**File:** `GreenSwamp.Alpaca.Server/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (AltAz)",
      "DeviceType": "Telescope",
      "ProfileName": "simulator-altaz",
      "UniqueId": "sim-altaz-12345678-1234-1234-1234-123456789abc"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "ProfileName": "simulator-gem",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    }
  ],
  "AllowedHosts": "*"
}
```

**Notes:**
- `AlpacaDevices` array can be empty (no default devices)
- Device numbers must be unique
- UniqueIds must be valid GUIDs
- ProfileName must reference existing profile file

### 4.2 Profile JSON Structure

**File:** `%AppData%/GreenSwampAlpaca/{version}/profiles/simulator-gem.json`

```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "Simulator",
  "MountType": 0,
  "CanFindHome": true,
  "CanPark": true,
  "CanPulseGuide": true,
  "CanSetDeclinationRate": true,
  "CanSetGuideRates": true,
  "CanSetPark": true,
  "CanSetPierSide": true,
  "CanSetRightAscensionRate": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAltAz": false,
  "CanSlewAltAzAsync": false,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanSyncAltAz": false,
  "CanUnpark": true,
  "NumberMoveAxis": 2,
  "CanMoveAxis": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0,
  "SiteTimeZone": -5,
  "TrackingRate": "Sidereal"
}
```

**Schema Version:** 1.0  
**Required Fields:** AlignmentMode, Mount, Latitude, Longitude  
**Optional Fields:** All other capabilities (defaults applied if missing)

#### 4.2.1 Simulator Profile Example

**File:** `profiles/simulator-gem.json` (shown above)

- `Mount` = "Simulator"
- No `ComPort`, `BaudRate`, or `SerialProtocol` fields needed
- All capabilities controlled via boolean flags

#### 4.2.2 Physical Mount Profile Example - EQ6-Pro

**File:** `%AppData%/GreenSwampAlpaca/{version}/profiles/eq6-production.json`

```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "SkyWatcher",
  "ComPort": "COM3",
  "BaudRate": 9600,
  "SerialProtocol": "SkyWatcher",
  "ConnectionTimeout": 5000,
  "UseRtsCts": false,
  "CanFindHome": true,
  "CanPark": true,
  "CanPulseGuide": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanUnpark": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0,
  "TrackingRate": "Sidereal"
}
```

**Physical Mount Required Fields:**
- `ComPort`: Serial port path (Windows: "COM3", Linux: "/dev/ttyUSB0")
- `BaudRate`: Communication speed (9600, 19200, 38400, 57600, or 115200)
- `SerialProtocol`: Protocol identifier (e.g., "SkyWatcher", "Meade")

**Physical Mount Optional Fields:**
- `ConnectionTimeout`: Timeout in milliseconds (default: 5000)
- `UseRtsCts`: Hardware flow control (default: false)

#### 4.2.3 Physical Mount Profile Example - AZ-GTi (Linux)

**File:** `%AppData%/GreenSwampAlpaca/{version}/profiles/azgti-field.json`

```json
{
  "AlignmentMode": "AltAz",
  "Mount": "SkyWatcher",
  "ComPort": "/dev/ttyUSB0",
  "BaudRate": 9600,
  "SerialProtocol": "SkyWatcher",
  "ConnectionTimeout": 5000,
  "UseRtsCts": false,
  "CanPark": false,
  "CanFindHome": false,
  "CanPulseGuide": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAltAz": true,
  "CanSlewAltAzAsync": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanSyncAltAz": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0,
  "TrackingRate": "Sidereal"
}
```

**Cross-Platform ComPort Notes:**
- **Windows:** Use "COM3", "COM4", etc.
- **Linux:** Use "/dev/ttyUSB0", "/dev/ttyACM0", etc.
- **macOS:** Use "/dev/tty.usbserial-*"

**Validation Rules:**
- ComPort conflict detection: No two devices can use the same ComPort
- ComPort format validation: Must match platform-specific pattern
- BaudRate validation: Must be a standard rate (9600, 19200, 38400, 57600, 115200)

---

## 5. Implementation Sequence

### 5.1 Phase 4.8 Implementation Order

#### Task 4.8.1: Create MountInstanceRegistry
**Estimated Time:** 30 minutes

**Deliverables:**
- [ ] `MountInstanceRegistry.cs` class file
- [ ] `CreateInstance()` method with validation
- [ ] `GetInstance()` method
- [ ] `RemoveInstance()` method
- [ ] Thread-safe dictionary implementation
- [ ] Unit tests for registry operations

**Verification:**
- Registry can store and retrieve instances by device number
- Thread safety tests pass under concurrent access
- Invalid device numbers rejected with clear errors

#### Task 4.8.2: Extend Settings Service
**Estimated Time:** 30 minutes

**Deliverables:**
- [ ] `LoadProfile()` method
- [ ] Profile directory creation
- [ ] Profile JSON deserialization
- [ ] Profile validation
- [ ] Error handling for missing profiles

**Verification:**
- Can load profile from `profiles/` directory
- Missing profile throws `FileNotFoundException`
- Invalid JSON throws meaningful error

#### Task 4.8.3: Create Configuration Models
**Estimated Time:** 15 minutes

**Deliverables:**
- [ ] `AlpacaDeviceConfig.cs` model
- [ ] Data annotations for validation
- [ ] XML documentation comments

**Verification:**
- Model deserializes from appsettings.json
- Validation attributes work correctly

#### Task 4.8.4: Update Program.cs for Device Loading
**Estimated Time:** 30 minutes

**Deliverables:**
- [ ] Configuration reading logic
- [ ] Device creation loop
- [ ] Error handling for load failures
- [ ] Logging at appropriate levels

**Verification:**
- Devices load from appsettings.json on startup
- Invalid configurations logged but don't crash
- Successfully loaded devices appear in logs

#### Task 4.8.5: Update Telescope Controller
**Estimated Time:** 45 minutes

**Deliverables:**
- [ ] Add `_deviceNumber` field
- [ ] Constructor accepting device number
- [ ] `GetInstance()` helper method
- [ ] Update all ASCOM property/method implementations
- [ ] Error handling for missing devices

**Verification:**
- Can route to correct instance by device number
- Invalid device number returns HTTP 400
- All ASCOM methods delegate to correct instance

#### Task 4.8.6: Update DeviceManager Registration
**Estimated Time:** 20 minutes

**Deliverables:**
- [ ] Register multiple telescopes with DeviceManager
- [ ] Each with unique device number
- [ ] Metadata includes unique ID

**Verification:**
- `/management/v1/configureddevices` returns all devices
- Each device has correct metadata
- Device numbers match URL routing

#### Task 4.8.7: Integration Testing
**Estimated Time:** 30 minutes

**Deliverables:**
- [ ] Test with 2-3 devices in config
- [ ] Connect N.I.N.A. to each device
- [ ] Verify independent operation
- [ ] Test concurrent connections

**Verification:**
- ASCOM clients can connect to all devices
- Each device operates independently
- No cross-contamination between devices

**Total Phase 4.8 Time:** 2.5 - 3 hours

---

### 5.2 Phase 4.8.1 Implementation Order

#### Task 4.8.1.1: Create Setup Controller
**Estimated Time:** 45 minutes

**Deliverables:**
- [ ] `SetupDevicesController.cs` with proper controller attributes
- [ ] POST /setup/devices endpoint
- [ ] DELETE /setup/devices/{id} endpoint
- [ ] GET /setup/devices endpoint
- [ ] GET /setup/profiles endpoint
- [ ] Request/response models with XML documentation
- [ ] Validation logic
- [ ] Error handling
- [ ] Swagger annotations (`[ProducesResponseType]`, XML comments)

**Verification:**
- Endpoints respond with correct HTTP status codes
- Request validation works
- Responses match specification
- Swagger UI displays all endpoints at http://localhost:31426/swagger
- "Alpaca HTML Endpoints - v1" group contains all device management endpoints
- XML documentation comments appear in Swagger descriptions
- "Try it out" functionality works for each endpoint
- OpenAPI spec exports successfully from /swagger/AlpacaSetup/swagger.json

#### Task 4.8.1.2: Implement Add Device Logic
**Estimated Time:** 30 minutes

**Deliverables:**
- [ ] Device number validation
- [ ] Profile loading
- [ ] UID generation if not provided
- [ ] Registry integration
- [ ] DeviceManager registration
- [ ] Success response

**Verification:**
- Can add device via POST
- New device appears in `/configureddevices` immediately
- ASCOM client can connect without restart

#### Task 4.8.1.3: Implement Remove Device Logic
**Estimated Time:** 20 minutes

**Deliverables:**
- [ ] Device existence check
- [ ] Disconnect if connected
- [ ] Registry removal
- [ ] DeviceManager cleanup
- [ ] Success response

**Verification:**
- Can remove device via DELETE
- Device disappears from `/configureddevices` immediately
- Device number becomes available for reuse

#### Task 4.8.1.4: Implement Profile List
**Estimated Time:** 15 minutes

**Deliverables:**
- [ ] Profile directory enumeration
- [ ] JSON file filtering
- [ ] Response formatting

**Verification:**
- Returns list of available profiles
- List updates when profiles added/removed

#### Task 4.8.1.5: Add GetAllInstances() and Helper Methods
**Estimated Time:** 15 minutes

**Deliverables:**
- [ ] `GetAllInstances()` in registry
- [ ] `IsDeviceNumberAvailable()`
- [ ] `GetNextAvailableDeviceNumber()`

**Verification:**
- Helper methods return correct results
- Thread-safe under concurrent access

#### Task 4.8.1.6: Testing with curl/Postman
**Estimated Time:** 25 minutes

**Deliverables:**
- [ ] Test add device
- [ ] Test remove device
- [ ] Test list devices
- [ ] Test list profiles
- [ ] Test error cases
- [ ] Verify ASCOM client compatibility

**Verification:**
- All endpoints work via curl
- ASCOM clients see changes immediately
- No server restart required

**Total Phase 4.8.1 Time:** 2.5 hours

**Combined Phase 4.8 + 4.8.1 Time:** 5-6 hours

---

## 6. Testing Strategy

### 6.1 Unit Tests

#### Test: Registry Thread Safety
```csharp
[Fact]
public void Registry_ConcurrentAccess_ThreadSafe()
{
    // Arrange
    var tasks = new List<Task>();
    
    // Act
    for (int i = 0; i < 100; i++)
    {
        int deviceNum = i;
        tasks.Add(Task.Run(() => 
        {
            var settings = CreateTestSettings();
            MountInstanceRegistry.CreateInstance(deviceNum, settings, $"Device {deviceNum}");
        }));
    }
    
    Task.WaitAll(tasks.ToArray());
    
    // Assert
    for (int i = 0; i < 100; i++)
    {
        Assert.NotNull(MountInstanceRegistry.GetInstance(i));
    }
}
```

#### Test: Profile Loading
```csharp
[Fact]
public void SettingsService_LoadProfile_ValidProfile_ReturnsSettings()
{
    // Arrange
    var service = CreateSettingsService();
    
    // Act
    var settings = service.LoadProfile("simulator-gem");
    
    // Assert
    Assert.NotNull(settings);
    Assert.Equal(AlignmentMode.GermanPolar, settings.AlignmentMode);
}

[Fact]
public void SettingsService_LoadProfile_MissingProfile_ThrowsFileNotFoundException()
{
    // Arrange
    var service = CreateSettingsService();
    
    // Act & Assert
    Assert.Throws<FileNotFoundException>(() => 
        service.LoadProfile("nonexistent"));
}
```

#### Test: Device Routing
```csharp
[Fact]
public void Telescope_GetInstance_ValidDevice_ReturnsCorrectInstance()
{
    // Arrange
    var telescope = new Telescope(deviceNumber: 1);
    var expectedInstance = MountInstanceRegistry.GetInstance(1);
    
    // Act
    var actualInstance = telescope.GetInstance();
    
    // Assert
    Assert.Same(expectedInstance, actualInstance);
}

[Fact]
public void Telescope_GetInstance_InvalidDevice_ThrowsInvalidOperationException()
{
    // Arrange
    var telescope = new Telescope(deviceNumber: 999);
    
    // Act & Assert
    Assert.Throws<InvalidOperationException>(() => 
        telescope.GetInstance());
}
```

### 6.2 Integration Tests

#### Test: Static Device Loading
**Setup:** Configure 2 devices in appsettings.json

**Steps:**
1. Start application
2. Call `/management/v1/configureddevices`
3. Verify 2 devices returned
4. Connect ASCOM client to device 0
5. Connect ASCOM client to device 1
6. Verify independent operation

**Expected Results:**
- Both devices loaded on startup
- Each device has unique UID
- Both devices respond to commands independently

#### Test: Dynamic Device Addition
**Setup:** Start with 1 device configured

**Steps:**
1. Start application
2. Verify 1 device in `/configureddevices`
3. `POST /setup/devices` with device 1 config
4. Call `/configureddevices` again
5. Verify 2 devices now present
6. Connect ASCOM client to new device

**Expected Results:**
- New device added without restart
- Device immediately available to ASCOM clients
- Both devices operate independently

#### Test: Dynamic Device Removal
**Setup:** Start with 2 devices configured

**Steps:**
1. Connect ASCOM client to device 1
2. `DELETE /setup/devices/1`
3. Verify ASCOM client disconnected
4. Verify device 1 removed from `/configureddevices`
5. Verify device 0 still operational

**Expected Results:**
- Device removed without restart
- Connected clients gracefully disconnected
- Other devices unaffected

### 6.3 ASCOM Conformance Tests

**Tool:** ConformU (ASCOM Conformance Checker)

**Test Scenarios:**

#### Scenario 1: Single Device Conformance
1. Configure 1 device
2. Run ConformU against device 0
3. Verify all tests pass

#### Scenario 2: Multi-Device Conformance
1. Configure 3 devices (different profiles)
2. Run ConformU against device 0
3. Run ConformU against device 1
4. Run ConformU against device 2
5. Verify all pass independently

#### Scenario 3: Dynamic Device Conformance
1. Start with 0 devices
2. Add device 0 via API
3. Run ConformU against device 0
4. Add device 1 via API
5. Run ConformU against device 1
6. Verify both pass

### 6.4 Performance Tests

#### Test: Registry Lookup Performance
**Metric:** Lookup time < 1ms

**Method:**
```csharp
[Fact]
public void Registry_Lookup_PerformanceTest()
{
    // Arrange
    for (int i = 0; i < 1000; i++)
    {
        MountInstanceRegistry.CreateInstance(i, CreateSettings(), $"Device {i}");
    }
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < 10000; i++)
    {
        var instance = MountInstanceRegistry.GetInstance(i % 1000);
    }
    stopwatch.Stop();
    
    // Assert
    var avgMs = stopwatch.ElapsedMilliseconds / 10000.0;
    Assert.True(avgMs < 1.0, $"Average lookup time: {avgMs}ms");
}
```

#### Test: Device Addition Performance
**Metric:** Add device < 100ms

**Method:** Time `POST /setup/devices` endpoint response time

#### Test: Concurrent Requests
**Metric:** 100 concurrent requests complete without errors

**Method:**
```csharp
[Fact]
public async Task Telescope_ConcurrentRequests_NoErrors()
{
    // Arrange
    var client = CreateTestClient();
    var tasks = new List<Task>();
    
    // Act
    for (int i = 0; i < 100; i++)
    {
        tasks.Add(client.GetAsync("/api/v1/telescope/0/rightascension"));
    }
    
    var responses = await Task.WhenAll(tasks);
    
    // Assert
    Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
}
```

### 6.5 Manual Test Procedures

#### Manual Test 1: N.I.N.A. Multi-Device
**Duration:** 10 minutes

1. Configure 2 devices in appsettings.json
2. Start GreenSwamp server
3. Open N.I.N.A.
4. Go to Equipment → Telescope
5. Click "Choose Device"
6. Verify 2 Green Swamp devices shown
7. Connect to first device
8. Verify connection successful
9. Disconnect
10. Connect to second device
11. Verify connection successful
12. Test slew command on second device

**Pass Criteria:** Both devices visible and operational

#### Manual Test 2: Dynamic Addition via curl
**Duration:** 5 minutes

1. Start server with 1 device
2. Run: `curl http://localhost:31426/management/v1/configureddevices`
3. Verify 1 device returned
4. Run: `curl -X POST http://localhost:31426/setup/devices -H "Content-Type: application/json" -d '{"deviceNumber":1,"deviceName":"Test Device","profileName":"simulator-gem"}'`
5. Verify HTTP 200 response
6. Run: `curl http://localhost:31426/management/v1/configureddevices`
7. Verify 2 devices returned
8. Open N.I.N.A.
9. Refresh device list
10. Verify new device appears

**Pass Criteria:** Device added and immediately visible

#### Manual Test 3: Concurrent Connections
**Duration:** 10 minutes

1. Configure 3 devices
2. Start server
3. Open N.I.N.A. on Computer A → Connect to device 0
4. Open SharpCap on Computer B → Connect to device 1
5. Open PHD2 on Computer A → Connect to device 2
6. Slew device 0 in N.I.N.A.
7. Monitor position in SharpCap (device 1)
8. Verify devices operate independently

**Pass Criteria:** No cross-contamination, all devices work concurrently

---

## 7. Error Handling

### 7.1 Device Number Validation

#### Error: Duplicate Device Number
**Trigger:** Attempt to create instance with existing device number

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Device number {X} already exists"}`

**Logging:**
```
WARN: Attempt to register duplicate device number {DeviceNumber}
```

#### Error: Invalid Device Number
**Trigger:** Device number < 0

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Device number must be >= 0"}`

**Logging:**
```
WARN: Invalid device number {DeviceNumber} in request
```

### 7.2 Profile Loading Errors

#### Error: Profile Not Found
**Trigger:** Profile file doesn't exist

**Response:**
- HTTP 404 Not Found
- JSON: `{"error": "Profile 'profileName' not found at {path}"}`

**Logging:**
```
ERROR: Profile '{ProfileName}' not found at {Path}
```

**Behavior:**
- Startup: Log error, skip device, continue
- API: Return 404, don't create device

#### Error: Invalid Profile JSON
**Trigger:** Profile JSON is malformed

**Response:**
- HTTP 500 Internal Server Error
- JSON: `{"error": "Failed to parse profile: {details}"}`

**Logging:**
```
ERROR: Failed to parse profile '{ProfileName}': {Exception}
```

#### Error: Missing ComPort for Physical Mount
**Trigger:** Physical mount profile (Mount != "Simulator") without ComPort field

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Physical mount requires ComPort"}`

**Logging:**
```
ERROR: Profile '{ProfileName}' missing required ComPort for physical mount
```

**Behavior:**
- Startup: Log error, skip device, continue
- API: Return 400, don't create device

#### Error: Missing BaudRate for Physical Mount
**Trigger:** Physical mount profile without BaudRate field

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Physical mount requires valid BaudRate"}`

**Logging:**
```
ERROR: Profile '{ProfileName}' missing required BaudRate for physical mount
```

#### Error: ComPort Already in Use
**Trigger:** Attempt to add device with ComPort already assigned to another device

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "ComPort COM3 is already in use by device 1"}`

**Logging:**
```
ERROR: ComPort conflict: {ComPort} already in use by device {DeviceNumber}
```

**Behavior:**
- Check all existing instances before creating new device
- Return clear error indicating which device is using the port

#### Error: Invalid ComPort Format
**Trigger:** ComPort doesn't match platform-specific format

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Invalid ComPort format. Expected 'COMn' (Windows) or '/dev/tty*' (Linux)"}`

**Logging:**
```
ERROR: Invalid ComPort format '{ComPort}' in profile '{ProfileName}'
```

**Valid Formats:**
- Windows: "COM1", "COM2", "COM3", etc.
- Linux: "/dev/ttyUSB0", "/dev/ttyACM0", etc.
- macOS: "/dev/tty.usbserial-*"

#### Error: Invalid BaudRate
**Trigger:** BaudRate is not a standard value

**Response:**
- HTTP 400 Bad Request
- JSON: `{"error": "Invalid BaudRate. Must be one of: 9600, 19200, 38400, 57600, 115200"}`

**Logging:**
```
ERROR: Invalid BaudRate {BaudRate} in profile '{ProfileName}'
```

**Valid Values:** 9600, 19200, 38400, 57600, 115200

---

### 7.3 Runtime Errors

#### Error: Device Not Found
**Trigger:** ASCOM request to unregistered device number

**Response:**
- HTTP 400 Bad Request
- Text: `"Device number {X} not found"`

**Logging:**
```
WARN: Request for non-existent device {DeviceNumber}
```

#### Error: Concurrent Modification
**Trigger:** Thread safety violation

**Response:**
- Retry operation (handled internally by lock)
- If persistent: HTTP 500

**Logging:**
```
ERROR: Concurrent modification error in registry
```

### 7.4 Disconnection Handling

#### Error: Remove Connected Device
**Trigger:** `DELETE /setup/devices/{id}` on connected device

**Behavior:**
1. Call `instance.Disconnect()`
2. Wait up to 5 seconds for graceful disconnect
3. If timeout, force remove
4. Log warning
5. Return success

**Response:**
- HTTP 200 OK
- JSON: `{"message": "Device {X} disconnected and removed", "warning": "Device was connected"}`

**Logging:**
```
WARN: Removed connected device {DeviceNumber} - forcefully disconnected
```

---

## 8. Logging Requirements

### 8.1 Log Levels

#### INFO Level
- Device registered on startup
- Device added via API
- Device removed via API
- Profile loaded successfully

**Examples:**
```
INFO: Registered device 0: 'Simulator (AltAz)' using profile 'simulator-altaz'
INFO: Dynamically added device 1: 'Simulator (GEM)'
INFO: Removed device 1: 'Simulator (GEM)'
```

#### WARN Level
- Profile not found on startup (continue with other devices)
- Duplicate device number attempt
- Invalid request parameters
- Removed connected device

**Examples:**
```
WARN: Profile 'missing-profile' not found for device 2, skipping
WARN: Attempt to register duplicate device number 1
WARN: Removed connected device 1
```

#### ERROR Level
- Profile parse failure
- Unexpected exception during device creation
- Registry corruption
- DeviceManager registration failure

**Examples:**
```
ERROR: Failed to parse profile 'simulator-gem': Invalid JSON at line 5
ERROR: Failed to register device 2: {Exception}
```

### 8.2 Structured Logging

Use structured logging for all operations:

```csharp
_logger.LogInformation(
    "Registered device {DeviceNumber}: {DeviceName} using profile {ProfileName}",
    deviceNumber,
    deviceName,
    profileName
);
```

**Benefits:**
- Searchable logs
- Metrics aggregation
- Correlation with ASCOM client logs

---

## 9. Success Criteria

### 9.1 Phase 4.8 Success Criteria

✅ **Registry Implementation**
- [ ] `MountInstanceRegistry` class exists
- [ ] Thread-safe operations verified
- [ ] All public methods implemented
- [ ] Unit tests pass

✅ **Static Configuration**
- [ ] Devices load from appsettings.json on startup
- [ ] Profile files load correctly
- [ ] Invalid configs logged but don't crash
- [ ] Multiple devices can be configured

✅ **ASCOM Compliance**
- [ ] `/management/v1/configureddevices` returns all devices
- [ ] Each device has unique UID
- [ ] Device metadata matches specification
- [ ] URL routing by device number works

✅ **Multi-Device Operation**
- [ ] 2-3 devices configured and operational
- [ ] ASCOM clients see all devices
- [ ] Each device operates independently
- [ ] No cross-contamination between devices

✅ **Performance**
- [ ] Registry lookup < 1ms
- [ ] No degradation with 10+ devices
- [ ] Concurrent requests handled correctly

✅ **Testing**
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] ConformU tests pass for each device
- [ ] Manual tests pass

### 9.2 Phase 4.8.1 Success Criteria

✅ **Setup API Implementation**
- [ ] `POST /setup/devices` works
- [ ] `DELETE /setup/devices/{id}` works
- [ ] `GET /setup/devices` works
- [ ] `GET /setup/profiles` works
- [ ] All endpoints have error handling

✅ **Swagger Documentation**
- [ ] Swagger UI accessible at http://localhost:31426/swagger
- [ ] All endpoints appear in "Alpaca HTML Endpoints - v1" group
- [ ] XML documentation comments display correctly in Swagger
- [ ] "Try it out" functionality works for all endpoints
- [ ] Request/response examples display correctly
- [ ] OpenAPI spec exports from /swagger/AlpacaSetup/swagger.json

✅ **Dynamic Device Management**
- [ ] Can add device without restart
- [ ] Can remove device without restart
- [ ] Changes visible to ASCOM clients immediately
- [ ] Device numbers can be reused after removal

✅ **Profile Management**
- [ ] Profile list updates when files added/removed
- [ ] Invalid profiles excluded from list
- [ ] Profile loading works via API

✅ **Error Handling**
- [ ] Duplicate device numbers rejected
- [ ] Missing profiles return 404
- [ ] Invalid requests return 400
- [ ] All errors have clear messages
- [ ] Physical mount validation errors clear and specific
- [ ] ComPort conflicts detected and reported
- [ ] Invalid ComPort format rejected
- [ ] Invalid BaudRate rejected

✅ **Physical Mount Support**
- [ ] Can add physical mount device via API
- [ ] ComPort and BaudRate validated before creation
- [ ] ComPort conflict detection works
- [ ] Physical mount profiles load correctly
- [ ] Serial communication settings preserved
- [ ] Cross-platform ComPort paths supported (Windows/Linux)

✅ **Testing**
- [ ] API tests pass (curl/Postman)
- [ ] N.I.N.A. compatibility verified
- [ ] Concurrent operation verified
- [ ] No memory leaks after add/remove cycles

### 9.3 Combined Success Criteria

✅ **Development Workflow**
- [ ] Can test multiple configurations without restart
- [ ] Configuration changes take < 5 seconds (vs. 60s restart)
- [ ] No debugging state lost during config changes

✅ **Production Readiness**
- [ ] Static configuration works for production deployment
- [ ] Dynamic API optional (can be disabled if needed)
- [ ] Comprehensive logging for troubleshooting
- [ ] No breaking changes to existing ASCOM clients

✅ **Documentation**
- [ ] README.md updated with configuration examples
- [ ] API endpoints documented
- [ ] Profile schema documented
- [ ] Troubleshooting guide created

---

## 10. Risk Analysis

### 10.1 Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Registry thread safety issues** | Medium | High | Comprehensive unit tests, stress testing |
| **DeviceManager state corruption** | Low | High | Defensive programming, validate before register |
| **Profile loading failures** | Medium | Medium | Validation, graceful degradation |
| **Memory leaks on add/remove** | Medium | Medium | Proper disposal, memory profiling |
| **Concurrent ASCOM client confusion** | Low | Medium | Clear error messages, device list updates |

### 10.2 Implementation Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Phase 4.8 takes longer than estimated** | Low | Low | Break into smaller tasks, test incrementally |
| **Phase 4.8.1 introduces bugs in 4.8** | Low | Medium | Separate code paths, integration testing |
| **ASCOM.Alpaca.Razor breaking changes** | Low | High | Version pin, test with official examples |
| **Configuration schema changes** | Medium | Medium | Version settings, migration support |

### 10.3 Operational Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **User configures duplicate UIDs** | Medium | Low | UID validation, auto-generation |
| **Profile files manually edited incorrectly** | High | Medium | JSON schema validation, clear errors |
| **Accidental device removal** | Medium | Low | Confirmation required (future UI) |
| **Production config committed to git** | Medium | Medium | .gitignore user settings, clear docs |

---

## 11. Future Enhancements (Out of Scope)

### 11.1 Phase 4.11 Enhancements

**NOT part of Phase 4.8/4.8.1, but enabled by this foundation:**

- [ ] Blazor UI for device management (`/admin` page)
- [ ] Profile upload via web UI
- [ ] Real-time device status display
- [ ] Device configuration wizard
- [ ] Profile import/export

### 11.2 Future Capabilities

**Potential enhancements beyond Phase 4.11:**

- [ ] Hot reload of device settings (without remove/add)
- [ ] Device groups/tags
- [ ] Multi-user access control
- [ ] Device usage statistics
- [ ] Profile templates library
- [ ] Cloud profile storage
- [ ] Device health monitoring
- [ ] Automatic failover between devices

---

## 12. Dependencies

### 12.1 External Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| ASCOM.Alpaca.Razor | Latest | DeviceManager, Management API |
| Microsoft.Extensions.Configuration | .NET 8 | Settings loading |
| Microsoft.Extensions.DependencyInjection | .NET 8 | DI container |
| System.Text.Json | .NET 8 | JSON serialization |

### 12.2 Internal Dependencies

| Component | Phase | Required For |
|-----------|-------|--------------|
| `SkySettingsInstance` | Existing | Profile data model |
| `IVersionedSettingsService` | Existing | Settings persistence |
| `MountInstance` | Existing | Device instance management |
| `Telescope` class | Existing | ASCOM interface implementation |

### 12.3 File System Dependencies

| Path | Purpose | Required |
|------|---------|----------|
| `%AppData%/GreenSwampAlpaca/{version}/` | Settings root | Yes |
| `profiles/` subdirectory | Profile storage | Yes (created if missing) |
| `appsettings.json` | Static device config | No (defaults applied) |
| `appsettings.user.json` | Default settings | Yes (existing) |

---

## 13. Deployment Considerations

### 13.1 First-Time Setup

**For new installations:**

1. Application creates `%AppData%/GreenSwampAlpaca/{version}/profiles/` directory
2. Default profiles copied from installation directory (optional)
3. Empty `appsettings.json` → no devices loaded
4. User configures devices via API or config file

### 13.2 Upgrade from Single-Instance

**For existing installations:**

1. Existing settings preserved as default profile
2. Migration creates device 0 using default profile
3. User can add additional devices via API
4. No breaking changes to existing usage

### 13.3 Production Deployment

**Recommended production configuration:**

1. Define all devices in `appsettings.json`
2. Disable Admin API endpoints (optional security)
3. Use environment-specific config transforms
4. Monitor logs for configuration errors

### 13.4 Development Deployment

**Recommended development configuration:**

1. Start with 1-2 devices in config
2. Enable Admin API endpoints
3. Use API to add/remove test configurations
4. Keep profile files in source control

---

## 14. References

### 14.1 ASCOM Standards

- **Alpaca API Specification:** Section 2 (Device API), Section 4 (Management API)
- **Device Numbers:** Section 2.1.3 - Must start at 0, must be unique per device type
- **Unique IDs:** Section 4.2.3 - Globally unique, persistent
- **HTTP Status Codes:** Section 2.5 - 200 for ASCOM errors, 400 for invalid requests

### 14.2 Internal Documents

- **Phase 4.7 Design:** Multi-Telescope UI (references this registry)
- **Settings Architecture:** IVersionedSettingsService specification
- **Testing Strategy:** ASCOM ConformU test procedures

### 14.3 External Resources

- **ASCOM Standards:** https://ascom-standards.org/
- **Alpaca API Reference:** https://ascom-standards.org/api/
- **ConformU:** https://github.com/ASCOMInitiative/ConformU

---

## 15. Approval and Sign-Off

### 15.1 Document Review

| Reviewer | Role | Date | Status |
|----------|------|------|--------|
| [Name] | Lead Developer | [Date] | ⏳ Pending |
| [Name] | ASCOM Architect | [Date] | ⏳ Pending |
| [Name] | Testing Lead | [Date] | ⏳ Pending |

### 15.2 Implementation Authorization

| Approver | Role | Date | Status |
|----------|------|------|--------|
| [Name] | Technical Lead | [Date] | ⏳ Pending |
| [Name] | Product Owner | [Date] | ⏳ Pending |

### 15.3 Change Log

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2024 | System Architect | Initial draft for review |

---

## Appendix A: Configuration Examples

### A.1 Minimal Configuration (Single Simulator)

**appsettings.json:**
```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Mount Simulator",
      "ProfileName": "simulator-gem",
      "UniqueId": "sim-12345678-1234-1234-1234-123456789abc"
    }
  ]
}
```

### A.2 Multiple Simulators (Testing)

**appsettings.json:**
```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (AltAz)",
      "ProfileName": "simulator-altaz",
      "UniqueId": "sim-altaz-12345678-1234-1234-1234-123456789abc"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Simulator (GEM)",
      "ProfileName": "simulator-gem",
      "UniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321"
    },
    {
      "DeviceNumber": 2,
      "DeviceName": "Simulator (Fork)",
      "ProfileName": "simulator-polar",
      "UniqueId": "sim-polar-11111111-2222-3333-4444-555555555555"
    }
  ]
}
```

### A.3 Mixed Simulator and Physical (Production)

**appsettings.json:**
```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "EQ6-Pro Observatory Mount",
      "ProfileName": "eq6-production",
      "UniqueId": "eq6-prod-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "AZ-GTi Field Mount",
      "ProfileName": "azgti-field",
      "UniqueId": "azgti-field-ffffffff-gggg-hhhh-iiii-jjjjjjjjjjjj"
    },
    {
      "DeviceNumber": 2,
      "DeviceName": "Test Simulator",
      "ProfileName": "simulator-gem",
      "UniqueId": "sim-test-99999999-8888-7777-6666-555555555555"
    }
  ]
}
```

**Required Profiles:**

**File: profiles/eq6-production.json** (Physical Mount - Windows)
```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "SkyWatcher",
  "ComPort": "COM3",
  "BaudRate": 9600,
  "SerialProtocol": "SkyWatcher",
  "ConnectionTimeout": 5000,
  "UseRtsCts": false,
  "CanFindHome": true,
  "CanPark": true,
  "CanPulseGuide": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanUnpark": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0
}
```

**File: profiles/azgti-field.json** (Physical Mount - Linux)
```json
{
  "AlignmentMode": "AltAz",
  "Mount": "SkyWatcher",
  "ComPort": "/dev/ttyUSB0",
  "BaudRate": 9600,
  "SerialProtocol": "SkyWatcher",
  "ConnectionTimeout": 5000,
  "UseRtsCts": false,
  "CanPark": false,
  "CanFindHome": false,
  "CanPulseGuide": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAltAz": true,
  "CanSlewAltAzAsync": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanSyncAltAz": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0
}
```

**File: profiles/simulator-gem.json** (Simulator - No Serial Port)
```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "Simulator",
  "CanFindHome": true,
  "CanPark": true,
  "CanPulseGuide": true,
  "CanSetTracking": true,
  "CanSlew": true,
  "CanSlewAsync": true,
  "CanSync": true,
  "CanUnpark": true,
  "Latitude": 28.5,
  "Longitude": -81.5,
  "Elevation": 50.0
}
```

**Notes:**
- Physical mounts (EQ6-Pro, AZ-GTi) **REQUIRE** ComPort and BaudRate
- Simulators do **NOT** include ComPort or BaudRate
- ComPort format differs by platform (Windows: "COM3", Linux: "/dev/ttyUSB0")
- Each profile file must exist in `%AppData%/GreenSwampAlpaca/{version}/profiles/` directory

---

## Appendix B: curl Examples

### B.1 List All Devices

```bash
curl -X GET http://localhost:31426/setup/devices
```

**Response:**
```json
[
  {
    "deviceNumber": 0,
    "deviceName": "Simulator (AltAz)",
    "connected": false,
    "alignmentMode": "AltAz",
    "mountType": "Simulator"
  }
]
```

### B.2 Add New Device (Simulator)

```bash
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 1,
    "deviceName": "Simulator (GEM)",
    "profileName": "simulator-gem"
  }'
```

**Response:**
```json
{
  "deviceNumber": 1,
  "deviceName": "Simulator (GEM)",
  "uniqueId": "sim-gem-87654321-4321-4321-4321-cba987654321",
  "message": "Device added successfully"
}
```

### B.2a Add New Device (Physical Mount - Windows)

```bash
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 2,
    "deviceName": "EQ6-Pro Observatory",
    "profileName": "eq6-production"
  }'
```

**Response:**
```json
{
  "deviceNumber": 2,
  "deviceName": "EQ6-Pro Observatory",
  "uniqueId": "eq6-prod-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "comPort": "COM3",
  "baudRate": 9600,
  "message": "Physical mount device added successfully"
}
```

**Note:** Profile `eq6-production.json` must include `ComPort` and `BaudRate` fields.

### B.2b Add New Device (Physical Mount - Linux)

```bash
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 3,
    "deviceName": "AZ-GTi Field Mount",
    "profileName": "azgti-field"
  }'
```

**Response:**
```json
{
  "deviceNumber": 3,
  "deviceName": "AZ-GTi Field Mount",
  "uniqueId": "azgti-field-ffffffff-gggg-hhhh-iiii-jjjjjjjjjjjj",
  "comPort": "/dev/ttyUSB0",
  "baudRate": 9600,
  "message": "Physical mount device added successfully"
}
```

**Note:** ComPort format differs between Windows (`COM3`) and Linux (`/dev/ttyUSB0`).

### B.2c Error: ComPort Conflict

```bash
# Attempt to add device with COM3 already in use
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 4,
    "deviceName": "Duplicate Mount",
    "profileName": "eq6-production"
  }'
```

**Response (HTTP 400):**
```json
{
  "error": "ComPort COM3 is already in use by device 2"
}
```

### B.2d Error: Missing ComPort for Physical Mount

**Profile without ComPort:**
```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "SkyWatcher",
  "BaudRate": 9600,
  "Latitude": 28.5,
  "Longitude": -81.5
}
```

**Request:**
```bash
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 5,
    "deviceName": "Broken Mount",
    "profileName": "incomplete-profile"
  }'
```

**Response (HTTP 400):**
```json
{
  "error": "Physical mount requires ComPort"
}
```

### B.3 Remove Device

```bash
curl -X DELETE http://localhost:31426/setup/devices/1
```

**Response:**
```json
{
  "message": "Device 1 removed successfully"
}
```

### B.4 List Available Profiles

```bash
curl -X GET http://localhost:31426/setup/profiles
```

**Response:**
```json
[
  {
    "profileName": "simulator-altaz",
    "path": "profiles/simulator-altaz.json"
  },
  {
    "profileName": "simulator-gem",
    "path": "profiles/simulator-gem.json"
  },
  {
    "profileName": "eq6-production",
    "path": "profiles/eq6-production.json"
  }
]
```

### B.5 Check Configured Devices (Standard Alpaca API)

```bash
curl -X GET http://localhost:31426/management/v1/configureddevices
```

**Response:**
```json
[
  {
    "DeviceName": "Simulator (AltAz)",
    "DeviceType": "Telescope",
    "DeviceNumber": 0,
    "UniqueID": "sim-altaz-12345678-1234-1234-1234-123456789abc"
  },
  {
    "DeviceName": "Simulator (GEM)",
    "DeviceType": "Telescope",
    "DeviceNumber": 1,
    "UniqueID": "sim-gem-87654321-4321-4321-4321-cba987654321"
  }
]
```

---

## Appendix C: Troubleshooting Guide

### Issue: Device Not Appearing in ASCOM Client

**Symptoms:**
- Device configured in appsettings.json
- Server starts without errors
- ASCOM client doesn't see device

**Diagnosis:**
1. Check `/management/v1/configureddevices` endpoint
2. Review server logs for registration errors
3. Verify profile exists and loads correctly

**Resolution:**
- If device not in `/configureddevices`: Check startup logs for errors
- If profile error: Verify profile file exists at correct path
- If UID duplicate: Generate new unique UID

### Issue: "Device Number Already Exists" Error

**Symptoms:**
- API returns 400 error when adding device
- Error message: "Device number X already exists"

**Diagnosis:**
1. Check `/setup/devices` to see existing devices (or use Swagger UI at http://localhost:31426/swagger)
2. Verify device number not in use

**Resolution:**
- Use different device number
- Remove existing device first
- Use `GetNextAvailableDeviceNumber()` (future enhancement)

### Issue: Profile Not Found

**Symptoms:**
- Device configured but won't load
- Error: "Profile 'name' not found"

**Diagnosis:**
1. Check profile file exists: `%AppData%/GreenSwampAlpaca/{version}/profiles/{name}.json`
2. Verify profile name matches exactly (case-sensitive)
3. Check file permissions

**Resolution:**
- Create missing profile file
- Fix profile name in configuration
- Copy from another profile as template

### Issue: Device Connected but Not Responding

**Symptoms:**
- ASCOM client connects successfully
- Commands return errors or timeout

**Diagnosis:**
1. Check device number routing: Verify correct instance returned
2. Check instance state: Connected, not in error state
3. Review mount control logs

**Resolution:**
- Verify device number in client matches registration
- Check instance is properly initialized
- Review SkyServer logs for mount-specific errors

### Issue: ComPort Already in Use

**Symptoms:**
- API returns 400 error when adding physical mount
- Error message: "ComPort COM3 is already in use by device X"

**Diagnosis:**
1. Check `/setup/devices` to see which device is using the port (or use Swagger UI)
2. Verify physical connection: Is mount actually connected to that port?

**Resolution:**
- Remove the other device using that ComPort
- Use a different ComPort if multiple mounts connected
- Disconnect unused mount from serial port
- Check Device Manager (Windows) or `ls /dev/tty*` (Linux) for available ports

### Issue: Physical Mount Requires ComPort Error

**Symptoms:**
- API returns 400 error when adding physical mount
- Error message: "Physical mount requires ComPort"

**Diagnosis:**
1. Check profile JSON file for `ComPort` field
2. Verify `Mount` field is not "Simulator"

**Resolution:**
- Add `ComPort` field to profile JSON: `"ComPort": "COM3"` (Windows) or `"ComPort": "/dev/ttyUSB0"` (Linux)
- Add `BaudRate` field: `"BaudRate": 9600`
- Add `SerialProtocol` field: `"SerialProtocol": "SkyWatcher"`

**Example Fix:**
```json
{
  "AlignmentMode": "GermanPolar",
  "Mount": "SkyWatcher",
  "ComPort": "COM3",
  "BaudRate": 9600,
  "SerialProtocol": "SkyWatcher",
  "Latitude": 28.5,
  "Longitude": -81.5
}
```

### Issue: Invalid ComPort Format

**Symptoms:**
- API returns 400 error
- Error message: "Invalid ComPort format"

**Diagnosis:**
1. Check ComPort value in profile
2. Verify format matches platform

**Resolution:**
- **Windows:** Use "COM1", "COM2", "COM3", etc. (case-insensitive)
- **Linux:** Use "/dev/ttyUSB0", "/dev/ttyACM0", etc.
- **macOS:** Use "/dev/tty.usbserial-*" or "/dev/cu.usbserial-*"

**Common Mistakes:**
- `"ComPort": "3"` ❌ Should be `"COM3"`
- `"ComPort": "com3"` ✅ Valid (case-insensitive)
- `"ComPort": "ttyUSB0"` ❌ Should be `"/dev/ttyUSB0"`

### Issue: Serial Port Access Denied (Linux)

**Symptoms:**
- Physical mount configured correctly
- Connection fails with permission error
- Log shows: "Access denied to /dev/ttyUSB0"

**Diagnosis:**
1. Check user permissions: `ls -l /dev/ttyUSB0`
2. Check group membership: `groups`

**Resolution:**
```bash
# Add user to dialout group (Debian/Ubuntu)
sudo usermod -a -G dialout $USER

# Add user to uucp group (Arch/Fedora)
sudo usermod -a -G uucp $USER

# Log out and log back in for changes to take effect
```

### Issue: Physical Mount Not Responding After Connection

**Symptoms:**
- Mount connects successfully
- Commands timeout or return errors
- ComPort validation passed

**Diagnosis:**
1. Verify BaudRate matches mount: Most SkyWatcher mounts use 9600
2. Check physical cable: USB-to-Serial adapter may be faulty
3. Check mount is powered on
4. Review serial communication logs

**Resolution:**
- Try different BaudRate values: 9600, 19200, 115200
- Test cable with different software (e.g., EQMOD, OnStep)
- Verify USB-to-Serial driver installed (Windows)
- Check `dmesg` for USB connection messages (Linux)

**BaudRate Testing:**
```bash
# Test different baud rates via Setup API
curl -X POST http://localhost:31426/setup/devices \
  -H "Content-Type: application/json" \
  -d '{
    "deviceNumber": 10,
    "deviceName": "Test Mount 9600",
    "profileName": "test-9600"
  }'
```

### Issue: Multiple Physical Mounts - ComPort Detection

**Symptoms:**
- Multiple USB-to-Serial adapters connected
- Don't know which ComPort corresponds to which mount

**Diagnosis (Windows):**
1. Open Device Manager
2. Expand "Ports (COM & LPT)"
3. Note existing COM ports
4. Unplug one mount
5. Refresh - see which port disappeared
6. That port belongs to unplugged mount

**Diagnosis (Linux):**
```bash
# Before unplugging
ls /dev/ttyUSB*

# Unplug one mount
ls /dev/ttyUSB*

# Missing device is the unplugged mount
```

**Resolution:**
- Label physical cables
- Document ComPort assignments
- Use descriptive device names in appsettings.json
- Consider USB port consistency (same port = same ComPort usually)

### Issue: Cross-Platform Profile Compatibility

**Symptoms:**
- Profile works on Windows but fails on Linux (or vice versa)
- ComPort format error

**Diagnosis:**
1. Check ComPort value in profile
2. Verify platform-specific format

**Resolution:**
- Use environment-specific profiles
- Windows profile: `eq6-windows.json` with `"ComPort": "COM3"`
- Linux profile: `eq6-linux.json` with `"ComPort": "/dev/ttyUSB0"`
- OR: Update ComPort when deploying to different platform

**Platform Detection (Future):**
```csharp
// Future enhancement: Auto-detect platform and adjust ComPort format
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // Validate Windows COM format
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    // Validate Linux /dev/tty* format
}
```

---

**END OF DOCUMENT**
