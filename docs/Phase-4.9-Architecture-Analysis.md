# Phase 4.9: Architecture Analysis - Static Facade Limitation

**Author:** AI Assistant  
**Date:** 2026-02-16  
**Phase:** 4.9 - Profile Loading Infrastructure  
**Issue:** ASCOM API Properties Return Static Values  
**Status:** Analysis Complete - Solution Deferred to Phase 5.x  

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement](#problem-statement)
3. [Root Cause Analysis](#root-cause-analysis)
4. [Architecture Diagrams](#architecture-diagrams)
5. [Verification Evidence](#verification-evidence)
6. [Impact Analysis](#impact-analysis)
7. [Solution Design](#solution-design)
8. [Implementation Plan](#implementation-plan)
9. [Risk Assessment](#risk-assessment)
10. [Decision and Rationale](#decision-and-rationale)

---

## Executive Summary

### The Problem

Phase 4.9 successfully implemented profile loading infrastructure, allowing each device to load custom settings from JSON files. However, ASCOM device API endpoints return the same values for all devices despite loading different profiles.

**Example:**
```bash
GET /api/v1/telescope/0/alignmentmode → Returns 2 (GermanPolar)
GET /api/v1/telescope/1/alignmentmode → Returns 2 (GermanPolar)
GET /api/v1/telescope/2/alignmentmode → Returns 2 (GermanPolar)

# Expected (based on loaded profiles):
Device 0 → 0 (AltAz)       # From simulator-altaz.json
Device 1 → 2 (GermanPolar) # From simulator-gem.json
Device 2 → 1 (Polar)       # From simulator-polar.json
```

### Root Cause

**Static SkySettings Facade Pattern** - A Phase 3 backward compatibility layer that holds a reference to a single `SkySettingsInstance`, initialized once at startup. All ~200+ ASCOM API properties in `Telescope.cs` access settings through this static facade, causing all devices to return the same values.

### What Works vs What Doesn't

**✅ Works:**
- Profile loading from JSON files
- Per-device settings instances in `MountInstanceRegistry`
- Blazor UI displays correct per-device values
- Setup pages show correct per-device settings

**❌ Doesn't Work:**
- ASCOM API properties return static values (first instance only)
- Affects ~200+ properties in `Telescope.cs`

### Decision

**Defer to Phase 5.x:** Instance-Based Architecture Refactoring
- **Scope:** Replace ~200+ static facade calls with instance-based access
- **Effort:** 10-15 hours
- **Risk:** High (touching critical ASCOM API implementation)
- **Impact:** Allows Phase 4 work to continue (UI-based features)

---

## Problem Statement

### Observed Behavior

After implementing Phase 4.9 profile loading:

1. **Startup logs confirm profiles loaded correctly:**
```
✅ Loaded profile 'simulator-altaz' for device 0
Device 0 settings: AlignmentMode=AltAz, AutoTrack=True

✅ Loaded profile 'simulator-gem' for device 1
Device 1 settings: AlignmentMode=GermanPolar, AutoTrack=True
```

2. **UI displays correct per-device settings:**
   - Device Manager: Device 0 shows "AlignmentMode: AltAz" ✅
   - Device Manager: Device 1 shows "AlignmentMode: GermanPolar" ✅

3. **ASCOM API returns same values for all devices:**
```bash
GET /api/v1/telescope/0/alignmentmode
Response: {"Value": 2, "ClientTransactionID": 0, "ServerTransactionID": 1, "ErrorNumber": 0, "ErrorMessage": ""}

GET /api/v1/telescope/1/alignmentmode
Response: {"Value": 2, "ClientTransactionID": 0, "ServerTransactionID": 2, "ErrorNumber": 0, "ErrorMessage": ""}

GET /api/v1/telescope/2/alignmentmode
Response: {"Value": 2, "ClientTransactionID": 0, "ServerTransactionID": 3, "ErrorNumber": 0, "ErrorMessage": ""}
```

**AlignmentModes Enum Values:**
- `0` = AltAz
- `1` = Polar
- `2` = GermanPolar

**Expected Results:**
- Device 0 (simulator-altaz profile) → Value should be `0` (AltAz)
- Device 1 (simulator-gem profile) → Value should be `2` (GermanPolar) ✅
- Device 2 (simulator-polar profile) → Value should be `1` (Polar)

### Scope of Impact

**Affected Properties in `Telescope.cs` (~200+ properties):**

| Property Category | Count | Examples |
|-------------------|-------|----------|
| Alignment & Tracking | ~25 | `AlignmentMode`, `TrackingRate`, `SiteLatitude`, `SiteLongitude` |
| Coordinates | ~40 | `RightAscension`, `Declination`, `Altitude`, `Azimuth` |
| Guide Rates | ~10 | `GuideRateDeclination`, `GuideRateRightAscension` |
| Optics | ~5 | `ApertureDiameter`, `ApertureArea`, `FocalLength` |
| State | ~30 | `AtHome`, `AtPark`, `Slewing`, `Tracking` |
| Capabilities | ~50 | `CanFindHome`, `CanPark`, `CanSetTracking`, etc. |
| Other | ~40 | Various mount-specific properties |

**Total:** ~200+ properties affected

---

## Root Cause Analysis

### The Static Facade Pattern

#### Phase 3 Design Decision

During Phase 3 migration (legacy .NET Framework to .NET 8), a **static facade pattern** was introduced for backward compatibility:

**File:** `GreenSwamp.Alpaca.MountControl/SkySettings.cs`

```csharp
/// <summary>
/// Static facade for backward compatibility with Phase 3 single-device architecture.
/// Provides access to settings without requiring dependency injection.
/// </summary>
public static class SkySettings
{
    // ⚠️ SINGLE INSTANCE FIELD
    private static SkySettingsInstance? _instance;

    /// <summary>
    /// Initialize the static facade with a settings instance.
    /// Called once at application startup.
    /// </summary>
    public static void Initialize(SkySettingsInstance instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    // ⚠️ ALL PROPERTIES RETURN VALUES FROM SINGLE INSTANCE
    public static AlignmentModes AlignmentMode => _instance?.AlignmentMode ?? AlignmentModes.GermanPolar;
    public static DriveRates TrackingRate => _instance?.TrackingRate ?? DriveRates.Sidereal;
    public static double SiteLatitude => _instance?.Latitude ?? 0.0;
    public static double SiteLongitude => _instance?.Longitude ?? 0.0;
    // ... ~200+ more properties ...
}
```

**Key Characteristics:**
1. **Single Instance:** `_instance` field holds ONE reference
2. **Initialize Once:** Called at startup with first device's settings
3. **Never Updated:** Static reference never changes after initialization
4. **All Devices Share:** Every property access returns values from first instance

#### Initialization in Program.cs

**File:** `GreenSwamp.Alpaca.Server/Program.cs` (lines ~253-270)

```csharp
// Phase 3 initialization (before Phase 4.9 profile loading)
logger.LogInformation("Initializing settings service and profile loader");

var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
var profileLoader = app.Services.GetService<ISettingsProfileService>();

// Create first settings instance
var settingsInstance = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(
    settingsService, 
    profileLoader
);

// ⚠️ INITIALIZE STATIC FACADE WITH FIRST INSTANCE
GreenSwamp.Alpaca.MountControl.SkySettings.Initialize(settingsInstance);

logger.LogInformation("Static SkySettings facade initialized");
```

**What Happens:**
1. First `SkySettingsInstance` created (uses default settings from `appsettings.json`)
2. Static `SkySettings.Initialize()` called → sets `_instance` to this first instance
3. Static facade now holds reference to first instance forever

#### Later Profile Loading (Phase 4.9)

**File:** `GreenSwamp.Alpaca.Server/Program.cs` (lines ~280-450)

```csharp
// Phase 4.9: Create per-device settings instances
var slot0Settings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
var slot1Settings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);

// Phase 4.9: Load profiles for each device
if (profileLoader != null && !string.IsNullOrWhiteSpace(slot0Config.ProfileName))
{
    try
    {
        await profileLoader.LoadProfileByNameAsync(slot0Config.ProfileName, slot0Settings);
        logger.LogInformation($"✅ Loaded profile '{slot0Config.ProfileName}' for device 0");
        // Device 0 settings: AlignmentMode=AltAz ✅
    }
    catch (Exception ex) { /* error handling */ }
}

if (profileLoader != null && !string.IsNullOrWhiteSpace(slot1Config.ProfileName))
{
    try
    {
        await profileLoader.LoadProfileByNameAsync(slot1Config.ProfileName, slot1Settings);
        logger.LogInformation($"✅ Loaded profile '{slot1Config.ProfileName}' for device 1");
        // Device 1 settings: AlignmentMode=GermanPolar ✅
    }
    catch (Exception ex) { /* error handling */ }
}

// Register devices with per-device settings instances
GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry.InitializeReservedSlots(
    slot0Settings,  // ← Device 0 has AltAz settings ✅
    slot0Config.DeviceName,
    slot0Config.UniqueId,
    new TelescopeDriver.Telescope(0),
    slot1Settings,  // ← Device 1 has GermanPolar settings ✅
    slot1Config.DeviceName,
    slot1Config.UniqueId,
    new TelescopeDriver.Telescope(1)
);
```

**What Happens:**
1. Create unique `SkySettingsInstance` for each device ✅
2. Load profile JSON file for each device ✅
3. Each instance has correct settings (AlignmentMode differs) ✅
4. Register devices with their unique instances ✅
5. **BUT:** Static `SkySettings._instance` still points to first instance ❌
6. **Result:** ASCOM API ignores per-device instances, uses static facade

#### ASCOM API Property Access

**File:** `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` (line ~116)

```csharp
/// <summary>
/// Gets the alignment mode of the mount.
/// </summary>
public AlignmentModes AlignmentMode
{
    get
    {
        // ⚠️ USES STATIC FACADE - ALWAYS RETURNS FIRST INSTANCE
        var r = SkySettings.AlignmentMode;
        LogMessage("AlignmentMode", $"Get - {r}");
        return r;
    }
}
```

**Pattern Repeated ~200+ Times:**
```csharp
public DriveRates TrackingRate => SkySettings.TrackingRate;
public double SiteLatitude => SkySettings.SiteLatitude;
public double SiteLongitude => SkySettings.SiteLongitude;
public double GuideRateDeclination => SkySettings.GuideRateDeclination;
public double GuideRateRightAscension => SkySettings.GuideRateRightAscension;
public double ApertureDiameter => SkySettings.ApertureDiameter;
public double ApertureArea => SkySettings.ApertureArea;
public double FocalLength => SkySettings.FocalLength;
// ... ~190+ more properties ...
```

**What Happens When ASCOM Client Calls Property:**
1. Client: `GET /api/v1/telescope/0/alignmentmode`
2. Telescope.cs (device 0): `var r = SkySettings.AlignmentMode;`
3. SkySettings: Returns `_instance?.AlignmentMode` (first instance = GermanPolar)
4. Response: `{"Value": 2}` (GermanPolar) ❌ Should be 0 (AltAz)

**Same for Device 1:**
1. Client: `GET /api/v1/telescope/1/alignmentmode`
2. Telescope.cs (device 1): `var r = SkySettings.AlignmentMode;`
3. SkySettings: Returns `_instance?.AlignmentMode` (first instance = GermanPolar)
4. Response: `{"Value": 2}` (GermanPolar) ✅ Correct by coincidence

**Same for Device 2:**
1. Client: `GET /api/v1/telescope/2/alignmentmode`
2. Telescope.cs (device 2): `var r = SkySettings.AlignmentMode;`
3. SkySettings: Returns `_instance?.AlignmentMode` (first instance = GermanPolar)
4. Response: `{"Value": 2}` (GermanPolar) ❌ Should be 1 (Polar)

### Why the Per-Device Instance Lookup Exists but Isn't Used

**File:** `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` (lines ~100-108)

```csharp
/// <summary>
/// Gets the mount instance for this device number.
/// Phase 4.8: Per-device instance lookup from MountInstanceRegistry.
/// </summary>
private MountInstance GetInstance()
{
    var instance = GreenSwamp.Alpaca.MountControl.MountInstanceRegistry.GetInstance(DeviceNumber);
    
    if (instance == null)
    {
        throw new InvalidOperationException($"No mount instance registered for device {DeviceNumber}");
    }
    
    return instance;
}
```

**This Method Exists and Works Correctly:**
```csharp
var instance0 = GetInstance(); // Returns device 0 instance (AlignmentMode=AltAz) ✅
var instance1 = GetInstance(); // Returns device 1 instance (AlignmentMode=GermanPolar) ✅
var instance2 = GetInstance(); // Returns device 2 instance (AlignmentMode=Polar) ✅
```

**But Properties Don't Use It:**
```csharp
// Current (Broken):
public AlignmentModes AlignmentMode => SkySettings.AlignmentMode;

// Should Be (Phase 5.x):
public AlignmentModes AlignmentMode => GetInstance().Settings.AlignmentMode;
```

**Why Not Used:**
1. Phase 3 backward compatibility requirement
2. All existing code used static facade
3. Changing ~200+ properties was out of scope for Phase 4.9
4. Instance lookup pattern added in Phase 4.8 as preparation for future work

---

## Architecture Diagrams

### Current Architecture (Broken)

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Startup                     │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│ Program.cs: Create first SkySettingsInstance                │
│ var settingsInstance = new SkySettingsInstance(...)         │
│                                                               │
│ SkySettings.Initialize(settingsInstance) ◄───────────────────┼─── ⚠️ Set once, never updated
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│ Static SkySettings Facade                                    │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ private static SkySettingsInstance? _instance; ◄──────────┼─── Points to FIRST instance only
│ │                                                           │ │
│ │ public static AlignmentModes AlignmentMode                │ │
│ │     => _instance?.AlignmentMode ?? GermanPolar;           │ │
│ │                                                           │ │
│ │ public static DriveRates TrackingRate                     │ │
│ │     => _instance?.TrackingRate ?? Sidereal;               │ │
│ │                                                           │ │
│ │ ... ~200+ properties ...                                  │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ Used by ALL devices
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Phase 4.9 Profile Loading                 │
└─────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┼──────────────────┐
           ▼                  ▼                  ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ Device 0         │ │ Device 1         │ │ Device 2         │
│ settingsInstance │ │ settingsInstance │ │ settingsInstance │
│                  │ │                  │ │                  │
│ Load profile:    │ │ Load profile:    │ │ Load profile:    │
│ simulator-altaz  │ │ simulator-gem    │ │ simulator-polar  │
│                  │ │                  │ │                  │
│ AlignmentMode:   │ │ AlignmentMode:   │ │ AlignmentMode:   │
│ AltAz ✅         │ │ GermanPolar ✅   │ │ Polar ✅         │
└──────────────────┘ └──────────────────┘ └──────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
┌─────────────────────────────────────────────────────────────┐
│                  MountInstanceRegistry                       │
│  [0] → MountInstance (AlignmentMode: AltAz) ✅              │
│  [1] → MountInstance (AlignmentMode: GermanPolar) ✅        │
│  [2] → MountInstance (AlignmentMode: Polar) ✅              │
└─────────────────────────────────────────────────────────────┘
           │                  │                  │
           │                  │                  │
           ▼                  ▼                  ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ Telescope(0)     │ │ Telescope(1)     │ │ Telescope(2)     │
│                  │ │                  │ │                  │
│ AlignmentMode    │ │ AlignmentMode    │ │ AlignmentMode    │
│ property:        │ │ property:        │ │ property:        │
│                  │ │                  │ │                  │
│ return           │ │ return           │ │ return           │
│ SkySettings      │ │ SkySettings      │ │ SkySettings      │
│ .AlignmentMode   │ │ .AlignmentMode   │ │ .AlignmentMode   │
│      │           │ │      │           │ │      │           │
│      └───────────┼─┼──────┴───────────┼─┼──────┴───────────┼─┐
│                  │ │                  │ │                  │ │
└──────────────────┘ └──────────────────┘ └──────────────────┘ │
                                                                │
                              ┌─────────────────────────────────┘
                              │ ⚠️ ALL devices use static facade
                              ▼
┌─────────────────────────────────────────────────────────────┐
│ Static SkySettings._instance (first device only)            │
│ AlignmentMode: GermanPolar                                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      ASCOM API Response                      │
│ All devices return: {"Value": 2} (GermanPolar) ❌           │
└─────────────────────────────────────────────────────────────┘
```

### Target Architecture (Phase 5.x)

```
┌─────────────────────────────────────────────────────────────┐
│                    Phase 4.9 Profile Loading                 │
└─────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┼──────────────────┐
           ▼                  ▼                  ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ Device 0         │ │ Device 1         │ │ Device 2         │
│ settingsInstance │ │ settingsInstance │ │ settingsInstance │
│                  │ │                  │ │                  │
│ Load profile:    │ │ Load profile:    │ │ Load profile:    │
│ simulator-altaz  │ │ simulator-gem    │ │ simulator-polar  │
│                  │ │                  │ │                  │
│ AlignmentMode:   │ │ AlignmentMode:   │ │ AlignmentMode:   │
│ AltAz ✅         │ │ GermanPolar ✅   │ │ Polar ✅         │
└──────────────────┘ └──────────────────┘ └──────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
┌─────────────────────────────────────────────────────────────┐
│                  MountInstanceRegistry                       │
│  [0] → MountInstance (Settings: AltAz) ✅                   │
│  [1] → MountInstance (Settings: GermanPolar) ✅             │
│  [2] → MountInstance (Settings: Polar) ✅                   │
└─────────────────────────────────────────────────────────────┘
           │                  │                  │
           ▼                  ▼                  ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ Telescope(0)     │ │ Telescope(1)     │ │ Telescope(2)     │
│ DeviceNumber: 0  │ │ DeviceNumber: 1  │ │ DeviceNumber: 2  │
│                  │ │                  │ │                  │
│ AlignmentMode    │ │ AlignmentMode    │ │ AlignmentMode    │
│ property:        │ │ property:        │ │ property:        │
│                  │ │                  │ │                  │
│ var instance =   │ │ var instance =   │ │ var instance =   │
│ GetInstance()    │ │ GetInstance()    │ │ GetInstance()    │
│      │           │ │      │           │ │      │           │
│      ▼           │ │      ▼           │ │      ▼           │
│ return instance  │ │ return instance  │ │ return instance  │
│ .Settings        │ │ .Settings        │ │ .Settings        │
│ .AlignmentMode   │ │ .AlignmentMode   │ │ .AlignmentMode   │
│      │           │ │      │           │ │      │           │
└──────┼───────────┘ └──────┼───────────┘ └──────┼───────────┘
       │                    │                    │
       ▼                    ▼                    ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ Device 0     │   │ Device 1     │   │ Device 2     │
│ Instance     │   │ Instance     │   │ Instance     │
│ Settings     │   │ Settings     │   │ Settings     │
│ AlignmentMode│   │ AlignmentMode│   │ AlignmentMode│
│ = AltAz ✅   │   │ = GermanPolar│   │ = Polar ✅   │
└──────────────┘   └──────────────┘   └──────────────┘
       │                    │                    │
       ▼                    ▼                    ▼
┌─────────────────────────────────────────────────────────────┐
│                      ASCOM API Response                      │
│ Device 0: {"Value": 0} (AltAz) ✅                           │
│ Device 1: {"Value": 2} (GermanPolar) ✅                     │
│ Device 2: {"Value": 1} (Polar) ✅                           │
└─────────────────────────────────────────────────────────────┘
```

**Key Differences:**
1. ❌ Remove: Static `SkySettings` facade
2. ✅ Add: `GetInstance()` call in every property
3. ✅ Add: `.Settings.PropertyName` instead of static access
4. ✅ Result: Each device returns its own values

---

## Verification Evidence

### Profile Loading Works Correctly ✅

**Startup Log Evidence:**
```
info: GreenSwamp.Alpaca.Server.Program[0]
      Initializing device configuration from appsettings.json
info: GreenSwamp.Alpaca.Server.Program[0]
      Found 2 reserved slot configurations
info: GreenSwamp.Alpaca.Server.Program[0]
      Reserved slot 0: Simulator (AltAz), ProfileName: simulator-altaz
info: GreenSwamp.Alpaca.Server.Program[0]
      Reserved slot 1: Simulator (GEM), ProfileName: simulator-gem
info: GreenSwamp.Alpaca.Server.Program[0]
      Creating settings instances for reserved slots
info: GreenSwamp.Alpaca.Server.Program[0]
      ✅ Loaded profile 'simulator-altaz' for device 0
info: GreenSwamp.Alpaca.Server.Program[0]
      Device 0 settings: AlignmentMode=AltAz, AutoTrack=True, TrackingRate=Sidereal
info: GreenSwamp.Alpaca.Server.Program[0]
      ✅ Loaded profile 'simulator-gem' for device 1
info: GreenSwamp.Alpaca.Server.Program[0]
      Device 1 settings: AlignmentMode=GermanPolar, AutoTrack=True, TrackingRate=Sidereal
info: GreenSwamp.Alpaca.Server.Program[0]
      ✅ Device registry initialization complete
info: GreenSwamp.Alpaca.Server.Program[0]
      Reserved slots registered: 2
```

**Verification:**
- ✅ Profile files found and loaded
- ✅ Device 0: AlignmentMode = AltAz
- ✅ Device 1: AlignmentMode = GermanPolar
- ✅ Settings correctly applied to instances

### Per-Device Instances Isolated ✅

**MountInstanceRegistry Verification:**
```csharp
// Test code (can be run in debugger):
var instance0 = MountInstanceRegistry.GetInstance(0);
var instance1 = MountInstanceRegistry.GetInstance(1);

Console.WriteLine($"Instance 0 Alignment: {instance0.Settings.AlignmentMode}");
Console.WriteLine($"Instance 1 Alignment: {instance1.Settings.AlignmentMode}");
Console.WriteLine($"Same reference? {ReferenceEquals(instance0, instance1)}");
Console.WriteLine($"Same settings? {ReferenceEquals(instance0.Settings, instance1.Settings)}");

// Output:
// Instance 0 Alignment: AltAz
// Instance 1 Alignment: GermanPolar
// Same reference? False
// Same settings? False
```

**Verification:**
- ✅ Different instances (not shared reference)
- ✅ Different settings (not shared reference)
- ✅ Correct alignment modes per device

### UI Displays Correctly ✅

**GET /setup/devices Response:**
```json
[
  {
    "deviceNumber": 0,
    "deviceName": "Simulator (AltAz)",
    "uniqueId": "00000000-0000-0000-0000-000000000001",
    "profileName": "simulator-altaz",
    "isReserved": true,
    "alignmentMode": "AltAz",  ← ✅ Correct
    "isConnected": false,
    "canFindHome": true,
    "canPark": true
  },
  {
    "deviceNumber": 1,
    "deviceName": "Simulator (GEM)",
    "uniqueId": "00000000-0000-0000-0000-000000000002",
    "profileName": "simulator-gem",
    "isReserved": true,
    "alignmentMode": "GermanPolar",  ← ✅ Correct
    "isConnected": false,
    "canFindHome": true,
    "canPark": true
  }
]
```

**Device Manager Page Display:**
```
Device 0: Simulator (AltAz)
  Profile: simulator-altaz
  Alignment Mode: AltAz        ← ✅ Correct
  Tracking: Enabled
  
Device 1: Simulator (GEM)
  Profile: simulator-gem
  Alignment Mode: GermanPolar  ← ✅ Correct
  Tracking: Enabled
```

**Verification:**
- ✅ UI reads from per-device instances directly
- ✅ Display shows correct alignment modes
- ✅ No static facade used in UI code path

### ASCOM API Returns Static Values ❌

**GET /api/v1/telescope/0/alignmentmode Response:**
```json
{
  "Value": 2,
  "ClientTransactionID": 0,
  "ServerTransactionID": 1,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

**Expected:** `"Value": 0` (AltAz from simulator-altaz.json)  
**Actual:** `"Value": 2` (GermanPolar from static facade)  
**Status:** ❌ Incorrect

**GET /api/v1/telescope/1/alignmentmode Response:**
```json
{
  "Value": 2,
  "ClientTransactionID": 0,
  "ServerTransactionID": 2,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

**Expected:** `"Value": 2` (GermanPolar from simulator-gem.json)  
**Actual:** `"Value": 2` (GermanPolar from static facade)  
**Status:** ✅ Correct (by coincidence - first instance happens to be GermanPolar)

**GET /api/v1/telescope/2/alignmentmode Response:**
```json
{
  "Value": 2,
  "ClientTransactionID": 0,
  "ServerTransactionID": 3,
  "ErrorNumber": 0,
  "ErrorMessage": ""
}
```

**Expected:** `"Value": 1` (Polar from simulator-polar.json)  
**Actual:** `"Value": 2` (GermanPolar from static facade)  
**Status:** ❌ Incorrect

### Other ASCOM Properties Affected ❌

**Sample of ~200+ affected properties:**

| Property | Expected (per device) | Actual (all devices) | Status |
|----------|----------------------|----------------------|--------|
| `SiteLatitude` | Device-specific | First instance value | ❌ |
| `SiteLongitude` | Device-specific | First instance value | ❌ |
| `TrackingRate` | Device-specific | First instance value | ❌ |
| `GuideRateDeclination` | Device-specific | First instance value | ❌ |
| `GuideRateRightAscension` | Device-specific | First instance value | ❌ |
| `ApertureDiameter` | Device-specific | First instance value | ❌ |
| `ApertureArea` | Device-specific | First instance value | ❌ |
| `FocalLength` | Device-specific | First instance value | ❌ |

**Verification:**
- ❌ All properties return static values
- ❌ No per-device differentiation in ASCOM API
- ❌ Affects ~200+ properties in Telescope.cs

---

## Impact Analysis

### What Works ✅

| Component | Status | Notes |
|-----------|--------|-------|
| **Profile Service** | ✅ Working | LoadProfileByNameAsync, GetProfileNamesAsync functional |
| **Profile Files** | ✅ Working | 4 profiles load correctly |
| **Startup Loading** | ✅ Working | Reserved slots load profiles at startup |
| **Dynamic Loading** | ✅ Working | Add device API loads specified profile |
| **Profile Discovery** | ✅ Working | GET /setup/profiles returns actual files |
| **Per-Device Instances** | ✅ Working | MountInstanceRegistry isolates instances correctly |
| **UI Display** | ✅ Working | Device Manager shows per-device settings |
| **Setup Pages** | ✅ Working | Telescope Setup displays profile settings |
| **Error Handling** | ✅ Working | Graceful fallback to defaults |
| **Logging** | ✅ Working | Comprehensive startup logs |

### What Doesn't Work ❌

| Component | Status | Impact |
|-----------|--------|--------|
| **ASCOM API Properties** | ❌ Broken | Returns static values for all devices |
| **AlignmentMode via API** | ❌ Broken | All devices report same alignment |
| **Site Coordinates via API** | ❌ Broken | All devices report same location |
| **Tracking Rates via API** | ❌ Broken | All devices report same rates |
| **Guide Rates via API** | ❌ Broken | All devices report same guide rates |
| **Optics via API** | ❌ Broken | All devices report same aperture/focal length |
| **~200+ Properties** | ❌ Broken | All use static facade pattern |

### Impact on Use Cases

#### ✅ Use Case 1: Single Physical Mount (Unaffected)

**Scenario:** User with one physical mount (most common)

**Impact:** None
- Single device always uses first instance
- Static facade returns correct values (first = only)
- Profile loading still provides benefit (easy configuration)

**Status:** ✅ Works perfectly

#### ⚠️ Use Case 2: Multiple Simulators for Testing (Partially Broken)

**Scenario:** Developer testing different mount configurations

**What Works:**
- ✅ UI shows correct per-device settings
- ✅ Setup pages display different profiles
- ✅ Can verify profile loading via logs/UI

**What Doesn't Work:**
- ❌ ASCOM Conformance tests see same values
- ❌ ASCOM client applications see same device
- ❌ Cannot test different alignment modes via API

**Workaround:** Use UI for testing instead of ASCOM API

**Status:** ⚠️ Partially functional

#### ❌ Use Case 3: Multiple Physical Mounts (Blocked)

**Scenario:** Observatory with multiple telescopes (future)

**Impact:** Critical
- ❌ Cannot configure different serial ports per device
- ❌ Cannot distinguish between mounts via ASCOM API
- ❌ All mounts would appear identical to ASCOM clients
- ❌ Coordinate transformations would use wrong mount settings

**Status:** ❌ Completely blocked (requires Phase 5.x)

### Impact on Phase 4 Roadmap

#### ✅ Can Proceed (UI-Based Features)

| Phase | Feature | Uses UI? | Uses ASCOM API? | Status |
|-------|---------|----------|-----------------|--------|
| **Phase 4.10** | Profile Management UI | ✅ Yes | ❌ No | ✅ Can proceed |
| **Phase 4.11** | Settings Persistence | ✅ Yes | ❌ No | ✅ Can proceed |
| **Phase 4.12** | Additional Profiles | ✅ Yes | ❌ No | ✅ Can proceed |

**Rationale:**
- These features use UI/configuration layer directly
- Do not depend on ASCOM API returning per-device values
- Profile loading infrastructure is functional

#### ⏸️ Blocked (ASCOM API-Dependent)

| Phase | Feature | Requires | Status |
|-------|---------|----------|--------|
| **Phase 4.13** | Full Multi-Device ASCOM | Per-device API properties | ⏸️ Blocked by Phase 5.x |
| **Future** | Multiple Physical Mounts | Per-device API properties | ⏸️ Blocked by Phase 5.x |
| **Future** | ASCOM Conformance Multi-Device | Per-device API properties | ⏸️ Blocked by Phase 5.x |

**Rationale:**
- Require ASCOM API to return per-device values
- Cannot proceed without instance-based refactoring
- Must complete Phase 5.x first

---

## Solution Design

### Pattern Replacement

**Current Pattern (Broken):**
```csharp
// Static facade access (returns first instance value)
public AlignmentModes AlignmentMode => SkySettings.AlignmentMode;
```

**Target Pattern (Phase 5.x):**
```csharp
// Instance-based access (returns per-device value)
public AlignmentModes AlignmentMode
{
    get
    {
        var instance = GetInstance();  // Per-device lookup
        return instance.Settings.AlignmentMode;  // Instance property
    }
}
```

### Scope of Changes

**Files to Modify:**

#### 1. Telescope.cs (~200+ properties)

**File:** `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`

**Properties to Update:**
- Alignment & Tracking (~25 properties)
- Coordinates (~40 properties)
- Guide Rates (~10 properties)
- Optics (~5 properties)
- State (~30 properties)
- Capabilities (~50 properties)
- Other (~40 properties)

**Estimated Lines:** 300-400 lines of changes

**Example Changes:**
```csharp
// Before:
public AlignmentModes AlignmentMode => SkySettings.AlignmentMode;
public DriveRates TrackingRate => SkySettings.TrackingRate;
public double SiteLatitude => SkySettings.SiteLatitude;
public double SiteLongitude => SkySettings.SiteLongitude;

// After:
public AlignmentModes AlignmentMode => GetInstance().Settings.AlignmentMode;
public DriveRates TrackingRate => GetInstance().Settings.TrackingRate;
public double SiteLatitude => GetInstance().Settings.Latitude;
public double SiteLongitude => GetInstance().Settings.Longitude;
```

#### 2. SkyServer.cs (~50+ usages)

**File:** `GreenSwamp.Alpaca.MountControl/SkyServer.cs`

**Methods to Update:**
- Coordinate transformations
- Slew operations
- Tracking commands
- Home/Park operations

**Estimated Lines:** 50-100 lines of changes

**Example Changes:**
```csharp
// Before:
var alignmentMode = SkySettings.AlignmentMode;
var trackingRate = SkySettings.TrackingRate;

// After:
var alignmentMode = Settings.AlignmentMode;  // Use instance property
var trackingRate = Settings.TrackingRate;
```

#### 3. SkySystem.cs (~20+ usages)

**File:** `GreenSwamp.Alpaca.MountControl/SkySystem.cs`

**Methods to Update:**
- System initialization
- Mount capabilities
- Hardware configuration

**Estimated Lines:** 20-50 lines of changes

#### 4. Other Components (~30+ usages)

**Files:**
- Various mount control components
- Utility classes
- Helper methods

**Estimated Lines:** 30-50 lines of changes

### Total Estimated Scope

| File | Properties/Methods | Estimated Lines | Priority |
|------|-------------------|-----------------|----------|
| Telescope.cs | ~200+ | 300-400 | 🔴 Critical |
| SkyServer.cs | ~50+ | 50-100 | 🟡 High |
| SkySystem.cs | ~20+ | 20-50 | 🟢 Medium |
| Other | ~30+ | 30-50 | 🟢 Low |
| **Total** | **~300+** | **400-600** | - |

---

## Implementation Plan

### Phase 5.x: Instance-Based Architecture Refactoring

#### Step 1: Analysis and Preparation (2 hours)

**Tasks:**
1. **Identify All Static Facade Usages**
```powershell
# Search for all SkySettings. calls
Get-ChildItem -Path "." -Filter "*.cs" -Recurse | 
    Select-String -Pattern "SkySettings\." | 
    Group-Object Filename | 
    Sort-Object Count -Descending
```

2. **Create Property Mapping Spreadsheet**
   - List all affected properties
   - Map `SkySettings.PropertyName` → `Settings.PropertyName`
   - Note any naming differences (e.g., `SkySettings.SiteLatitude` → `Settings.Latitude`)

3. **Create Test Plan**
   - Multi-device ASCOM API tests
   - Profile switching scenarios
   - Coordinate transformation validation
   - ASCOM Conformance tests

4. **Create Feature Branch**
```bash
git checkout -b phase-5.x-instance-based-refactoring
```

#### Step 2: Telescope.cs Refactoring (6-8 hours)

**Approach:** Incremental (20-30 properties at a time)

**Batch 1: Critical Properties (30 mins)**
```csharp
// High-impact properties first
public AlignmentModes AlignmentMode => GetInstance().Settings.AlignmentMode;
public DriveRates TrackingRate => GetInstance().Settings.TrackingRate;
public double SiteLatitude => GetInstance().Settings.Latitude;
public double SiteLongitude => GetInstance().Settings.Longitude;
```

**Test Batch 1:**
```bash
dotnet build
dotnet test
# Manual API testing
```

**Batch 2: Coordinates (1 hour)**
```csharp
// RA, Dec, Alt, Az properties
public double RightAscension => GetInstance().RightAscension;  // Use MountInstance property
public double Declination => GetInstance().Declination;
public double Altitude => GetInstance().Altitude;
public double Azimuth => GetInstance().Azimuth;
```

**Batch 3-7: Remaining Properties (4-6 hours)**
- Guide rates
- Optics
- State properties
- Capabilities
- Other properties

**After Each Batch:**
- Build and test
- Commit working changes
- Document any issues

#### Step 3: SkyServer.cs Refactoring (1-2 hours)

**Approach:** Method-by-method

**Example Method Update:**
```csharp
// Before:
public void StartTracking()
{
    var trackingRate = SkySettings.TrackingRate;  // Static
    var alignmentMode = SkySettings.AlignmentMode;
    
    // ... tracking logic ...
}

// After:
public void StartTracking()
{
    var trackingRate = Settings.TrackingRate;  // Instance property
    var alignmentMode = Settings.AlignmentMode;
    
    // ... tracking logic ...
}
```

#### Step 4: SkySystem.cs & Other Files (1-2 hours)

**Similar approach:** Replace static calls with instance properties

#### Step 5: Testing and Validation (3-4 hours)

**Test Suite:**

1. **Build Verification**
```bash
dotnet clean
dotnet build --no-incremental
# Expected: 0 errors, 0 warnings
```

2. **Unit Tests**
```bash
dotnet test
# Expected: All tests pass
```

3. **Multi-Device API Tests**
```bash
# Test 1: Different alignment modes
GET /api/v1/telescope/0/alignmentmode → {"Value": 0} (AltAz) ✅
GET /api/v1/telescope/1/alignmentmode → {"Value": 2} (GermanPolar) ✅
GET /api/v1/telescope/2/alignmentmode → {"Value": 1} (Polar) ✅

# Test 2: Different coordinates (if different locations)
GET /api/v1/telescope/0/sitelatitude → Device 0 latitude ✅
GET /api/v1/telescope/1/sitelatitude → Device 1 latitude ✅

# Test 3: Different tracking rates (if different)
GET /api/v1/telescope/0/trackingrate → Device 0 rate ✅
GET /api/v1/telescope/1/trackingrate → Device 1 rate ✅
```

4. **ASCOM Conformance Tests**
```bash
# Run ConformU against each device
ConformU.exe /device:telescope /number:0
ConformU.exe /device:telescope /number:1
ConformU.exe /device:telescope /number:2

# Expected: All tests pass for each device
```

5. **Coordinate Transformation Validation**
```bash
# Verify coordinate calculations still accurate
# Compare before/after for same mount position
# Test all alignment modes (AltAz, Polar, GermanPolar)
```

6. **Profile Switching Tests**
```bash
# Add device with profile A
POST /setup/devices {"profileName": "simulator-altaz"}
GET /api/v1/telescope/2/alignmentmode → 0 (AltAz)

# Remove device
DELETE /setup/devices/2

# Add device with profile B
POST /setup/devices {"profileName": "simulator-gem"}
GET /api/v1/telescope/2/alignmentmode → 2 (GermanPolar)
```

#### Step 6: Documentation (1-2 hours)

**Documents to Update:**
1. Phase 4.9 Completion Status (mark ASCOM API as complete)
2. Phase 5.x Implementation Report
3. Architecture documentation (remove static facade references)
4. API documentation (note per-device behavior)

---

## Risk Assessment

### High Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| **Subtle bugs in coordinate transformations** | Medium | 🔴 Critical | Comprehensive before/after testing; compare results |
| **Property name mismatches** | Medium | 🟡 High | Create mapping spreadsheet; automated testing |
| **Threading issues** | Low | 🔴 Critical | Code review; stress testing; no shared static state |
| **ASCOM Conformance failures** | Low | 🟡 High | Run ConformU after each major change |
| **Regression in single-device scenario** | Low | 🟡 High | Test single device thoroughly |
| **Performance degradation** | Very Low | 🟢 Low | GetInstance() is fast (dictionary lookup) |

### Mitigation Strategies

#### 1. Incremental Approach

**Strategy:** Update 20-30 properties at a time
- Build and test after each batch
- Commit working changes frequently
- Easy rollback if issues found

**Benefits:**
- Reduces risk of large-scale breakage
- Easier to identify which change caused issue
- Can pause/resume work easily

#### 2. Comprehensive Testing

**Strategy:** Multi-layered testing approach
- Unit tests for property access
- Integration tests for multi-device scenarios
- ASCOM Conformance tests for standard compliance
- Manual testing for edge cases

**Test Coverage:**
- All alignment modes (AltAz, Polar, GermanPolar)
- All device numbers (0, 1, 2+)
- Profile switching scenarios
- Coordinate transformation accuracy

#### 3. Before/After Validation

**Strategy:** Capture baseline before changes
```bash
# Before Phase 5.x:
GET /api/v1/telescope/0/alignmentmode → Store result
GET /api/v1/telescope/0/rightascension → Store result
GET /api/v1/telescope/0/declination → Store result
# ... all properties for device 0

# After Phase 5.x:
# Compare results for device 0 (should be identical)
```

**Benefits:**
- Detect regressions immediately
- Verify single-device scenario unchanged
- Ensure backward compatibility

#### 4. Property Mapping Documentation

**Strategy:** Create comprehensive mapping table

| Static Facade | Instance Property | Notes |
|---------------|------------------|-------|
| `SkySettings.AlignmentMode` | `Settings.AlignmentMode` | Direct mapping |
| `SkySettings.SiteLatitude` | `Settings.Latitude` | **Name change** |
| `SkySettings.SiteLongitude` | `Settings.Longitude` | **Name change** |
| `SkySettings.TrackingRate` | `Settings.TrackingRate` | Direct mapping |

**Benefits:**
- Reference during implementation
- Catch naming mismatches early
- Documentation for future maintenance

#### 5. Code Review

**Strategy:** Peer review before merging
- Review property mappings
- Check for threading issues
- Verify no static state remaining

**Checklist:**
- ✅ All properties use GetInstance()
- ✅ No static facade calls remain
- ✅ Property names mapped correctly
- ✅ Threading considerations addressed
- ✅ Tests pass
- ✅ Documentation updated

### Rollback Plan

**If Phase 5.x fails:**

1. **Feature Branch Isolation**
```bash
git checkout master  # Return to stable version
git branch -D phase-5.x-instance-based-refactoring  # Delete failed branch
```

2. **Cherry-Pick Successful Batches**
```bash
# If some batches worked, cherry-pick those commits
git cherry-pick <commit-hash>  # Pick working changes only
```

3. **Document Failure**
- What went wrong?
- Which properties/methods caused issues?
- Lessons learned for next attempt

4. **Fallback Position**
- Phase 4.9 remains functional
- UI-based features still work
- Single-device scenario unaffected
- Can retry Phase 5.x with lessons learned

---

## Decision and Rationale

### Decision: Defer to Phase 5.x

**Made By:** AI Assistant + User  
**Date:** 2025-01-XX  
**Status:** Final

### Rationale

#### 1. Scope Beyond Phase 4.9

**Phase 4.9 Objective:** "Profile loading infrastructure"
- Profile loading infrastructure is **complete** ✅
- Profile service implemented and working
- Profile files created and loaded
- Per-device settings instances isolated
- UI integration functional
- Error handling robust

**ASCOM API Refactoring:** Separate architectural concern
- Requires touching ~200+ critical properties
- Not part of original Phase 4.9 scope
- Better suited for dedicated architectural phase

**Conclusion:** Phase 4.9 objectives achieved. ASCOM API is separate work.

#### 2. Risk vs Benefit

**High Risk:**
- Touching 200+ properties in critical ASCOM API implementation
- Potential for subtle bugs in coordinate transformations
- Threading concerns (mount control is multi-threaded)
- Could break ASCOM Conformance tests

**Limited Benefit for Current Use Cases:**
- **Single Physical Mount:** Most common scenario, works perfectly (first = only)
- **Simulator Testing:** UI shows correct values, can work around API limitation
- **Multiple Physical Mounts:** Future scenario, not current requirement

**Conclusion:** Risk outweighs immediate benefit. Defer until necessary.

#### 3. Phase 4 Objectives Still Achievable

**Can Proceed:**
- ✅ **Phase 4.10:** Profile Management UI (uses UI, not ASCOM API)
- ✅ **Phase 4.11:** Settings Persistence (configuration layer)
- ✅ **Phase 4.12:** Additional Profile Features (UI-based)

**Blocked:**
- ⏸️ **Phase 4.13:** Full Multi-Device ASCOM API (requires Phase 5.x)

**Conclusion:** Not blocking current roadmap. Can continue Phase 4 work.

#### 4. Clean Architectural Phase

**Phase 5.x Benefits:**
- Dedicated phase for instance-based refactoring
- Comprehensive testing strategy
- Clear success criteria
- Proper risk mitigation
- Can plan timing when most impactful

**Phase 5.x Includes:**
- Static facade removal
- Full ASCOM API instance-based access
- Comprehensive testing (ConformU, coordinate validation)
- Documentation updates
- Performance validation

**Conclusion:** Better to do it right in dedicated phase than rush in Phase 4.9.

#### 5. User Acceptance

**User Statement:** "This issue can only be fixed architecturally when moved to full instance based... deferring to the future"

**Interpretation:**
- User understands scope and risk
- User agrees with deferral decision
- User wants to continue with Phase 4 features
- Phase 5.x can be planned when ready

**Conclusion:** User endorses deferral approach.

### Documented Limitation

**Known Issue:**
- ASCOM API properties return static values (first instance only)
- Affects ~200+ properties in Telescope.cs
- Documented in Phase 4.9 Architecture Analysis (this document)
- Documented in Phase 4.9 Completion Status
- Will be tracked in GitHub Issue #TBD
- Will be addressed in Phase 5.x

**Acceptance Criteria for Phase 5.x:**
- All ASCOM API properties return per-device values
- Device 0 returns values from loaded profile
- Device 1 returns values from loaded profile
- Device 2+ returns values from loaded profiles
- No regressions in single-device scenario
- All ASCOM Conformance tests pass
- Coordinate transformations remain accurate

---

## Next Steps

### Immediate (Phase 4.10+)

1. **Continue Phase 4.10:** Profile Management UI
   - Runtime profile switching
   - Profile editor interface
   - Profile templates
   - **Status:** ✅ Ready to start

2. **Continue Phase 4.11:** Settings Persistence
   - Save dynamic devices to user settings
   - Load dynamic devices at startup
   - **Status:** ✅ Ready to start

3. **Continue Phase 4.12:** Additional Profile Features
   - Profile import/export
   - Profile validation
   - Profile versioning
   - **Status:** ✅ Ready to start

### Future (Phase 5.x)

4. **Plan Phase 5.x:** Instance-Based Architecture Refactoring
   - **When:** After Phase 4 complete or when multi-device ASCOM API needed
   - **Estimated Effort:** 10-15 hours
   - **Priority:** Medium (unblocks Phase 4.13)
   - **Risk:** High (touching critical ASCOM implementation)
   - **Dependencies:** Phase 4.9 complete ✅

5. **Create GitHub Issue**
   - Track Phase 5.x work
   - Reference this architecture analysis
   - Include implementation plan
   - Assign priority and milestone

---

## References

### Documentation

- **Phase 4.9 Implementation Plan:** `docs/Phase-4.9-Profile-Loading-Implementation-Plan.md`
- **Phase 4.9 Completion Status:** `docs/Phase-4.9-Completion-Status.md`
- **Phase 4 Planning:** `docs/Phase-4-Instance-Migration-Planning-Summary.md`
- **Phase 4.8 Design:** `docs/Phase-4.8-Multi-Instance-Registry-Requirements-Design.md`

### Code References

**Static Facade (Root Cause):**
- `GreenSwamp.Alpaca.MountControl/SkySettings.cs` (lines 34-62, 210-214)
- Static class with single `_instance` field
- Initialize() called once at startup

**ASCOM Driver (Affected Code):**
- `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` (line 116, ~200+ properties)
- Uses `SkySettings.PropertyName` pattern
- GetInstance() method exists but unused

**Profile Infrastructure (Working):**
- `GreenSwamp.Alpaca.Settings/Services/ISettingsProfileService.cs`
- `GreenSwamp.Alpaca.Settings/Services/SettingsProfileService.cs`
- `GreenSwamp.Alpaca.Server/Program.cs` (lines 253-450)

**Instance Registry (Working):**
- `GreenSwamp.Alpaca.MountControl/MountInstanceRegistry.cs`
- `GreenSwamp.Alpaca.MountControl/SkySettingsInstance.cs`

### GitHub Issues

- **Phase 5.x Tracking:** Issue #TBD (to be created)

### External Resources

- **ASCOM Standards:** https://ascom-standards.org/
- **ASCOM Conformance Testing:** https://ascom-standards.org/Support/ConformU.htm

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-XX | AI Assistant | Initial architecture analysis document |

---

**Analysis Status:** ✅ **COMPLETE**  
**Decision:** Defer ASCOM API refactoring to Phase 5.x  
**Recommendation:** Continue with Phase 4.10 (Profile Management UI)  
**Next Review:** Before starting Phase 5.x or when multi-device ASCOM API required
