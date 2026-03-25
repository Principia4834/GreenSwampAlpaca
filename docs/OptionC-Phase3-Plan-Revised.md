# Option C Phase 3: Multi-Device Configuration & UI
## Settings-First Architecture (No Backward Compatibility)

**Date:** March 25, 2026  
**Status:** Ready to start  
**Prerequisites:** Phase 1 ✅ Complete, Phase 2 ✅ Complete  
**Estimated Duration:** 3-4 hours  
**Risk Level:** Low

---

## 1. Executive Summary

Phase 3 adds **application-layer multi-device support** with a clean settings-first architecture:

- **Each `SkySettings` instance represents one complete device** (all 134 properties)
- **Add 3 properties to `SkySettings`:** `DeviceNumber`, `DeviceName`, `Enabled`
- **JSON structure:** Array of `SkySettings` objects (no nesting)
- **Migration strategy:** Delete old settings files, recreate on first run
- **Blazor UI:** Device Manager page with connect/disconnect controls per device

**Rationale:** `SkySettings` already contains the full telescope configuration (Port, BaudRate, Latitude, Longitude, mount parameters, etc.). Making it device-aware is simpler than creating a separate nested structure.

---

## 2. Architecture: `SkySettings` IS the Device Configuration

### Current Structure

```csharp
public class SkySettings
{
    // Connection (lines 26-38)
    public string Mount { get; set; } = "Simulator";
    public string Port { get; set; } = "COM1";
    public long BaudRate { get; set; } = 9600;
    public Handshake HandShake { get; set; }
    
    // Location (lines 40-50)
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Elevation { get; set; }
    
    // Mount Config (lines 52-56)
    public AlignmentMode AlignmentMode { get; set; }
    public bool AutoTrack { get; set; }
    
    // Optics (lines 59-62)
    public int ApertureDiameter { get; set; }
    public int FocalLength { get; set; }
    
    // ... 125+ more properties (tracking, limits, PEC, encoders, etc.)
}
```

**Problem:** No `DeviceNumber` or `DeviceName` — cannot identify which device this represents.

### Revised Structure (Phase 3)

```csharp
public class SkySettings
{
    // NEW: Device identification (Phase 3)
    public int DeviceNumber { get; set; } = 0;
    public string DeviceName { get; set; } = "Telescope";
    public bool Enabled { get; set; } = true;
    
    // EXISTING: All 134 properties unchanged
    public string Mount { get; set; } = "Simulator";
    public string Port { get; set; } = "COM1";
    public long BaudRate { get; set; } = 9600;
    // ... etc.
}
```

---

## 3. JSON Configuration

### Multi-Device Configuration (Target)

```json
{
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Main Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM1",
      "BaudRate": 9600,
      "Latitude": 51.5074,
      "Longitude": -0.1278,
      "Elevation": 50.0,
      "AlignmentMode": "GermanPolar",
      "ApertureDiameter": 200,
      "FocalLength": 1000,
      "AutoTrack": true,
      "TrackingRate": 1.0,
      "MinPulseRa": 50,
      "MinPulseDec": 50,
      "GotoPrecision": 0.0002,
      "MaxSlewRate": 4.0,
      "HourAngleLimit": 20.0,
      "DecLimitNorth": 90.0,
      "DecLimitSouth": -90.0
      // ... all other 120+ properties
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "Guide Scope",
      "Enabled": true,
      "Mount": "SkyWatcher",
      "Port": "COM3",
      "BaudRate": 115200,
      "Latitude": 51.5074,
      "Longitude": -0.1278,
      "Elevation": 50.0,
      "AlignmentMode": "AltAz",
      "ApertureDiameter": 80,
      "FocalLength": 400,
      "AutoTrack": false,
      "CustomGearing": true,
      "CustomRa360Steps": 9024000,
      "CustomRaWormTeeth": 130
      // ... all other 120+ properties
    }
  ]
}
```

**Key Points:**
- Each device gets **all** 134 properties
- Devices can have completely different configurations (different locations, mounts, settings)
- `Enabled: false` → device not registered at startup
- No nested structures, no override logic, no fallback chains

### Migration: Old Settings Deleted

**Old `appsettings.user.json` (Phase 2):**
```json
{
  "SkySettings": {
    "Mount": "Simulator",
    "Port": "COM1",
    "BaudRate": 9600
    // ... 131 more properties
  }
}
```

**Migration Strategy:**
1. User upgrades to Phase 3 build
2. Application detects old settings format (no `Devices` array)
3. **Delete old `appsettings.user.json`**
4. Create new `appsettings.user.json` with single-device default:
```json
{
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Telescope",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM1",
      "BaudRate": 9600
      // ... all 131 default values from embedded appsettings.json
    }
  ]
}
```

---

## 4. Implementation Tasks

### Task 3.1 — Add 3 Properties to `SkySettings`

**File:** `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs`

**Changes:**
1. Add 3 properties to `SkySettings` class (insert after line 24, before first existing property):
```csharp
// Phase 3: Device identification
public int DeviceNumber { get; set; } = 0;
public string DeviceName { get; set; } = "Telescope";
public bool Enabled { get; set; } = true;
```

2. **No nested `DeviceSettings` class needed** — `SkySettings` IS the device config

**Build:** Verify 0 errors

**Time:** 5 minutes  
**Risk:** None — additive change

---

### Task 3.2 — Update Settings Service to Return List

**File:** `GreenSwamp.Alpaca.Settings/Services/IVersionedSettingsService.cs`

**Changes:**
1. Add method to return all device configurations:
```csharp
/// <summary>
/// Gets all device configurations from the Devices array.
/// Returns empty list if no devices configured.
/// </summary>
List<SkySettings> GetAllDevices();
```

**File:** `GreenSwamp.Alpaca.Settings/Services/VersionedSettingsService.cs`

**Changes:**
2. Implement `GetAllDevices()`:
```csharp
public List<SkySettings> GetAllDevices()
{
    var root = _configuration.Get<SettingsRoot>();
    return root?.Devices ?? new List<SkySettings>();
}

private class SettingsRoot
{
    public List<SkySettings>? Devices { get; set; }
}
```

3. Update migration logic in `LoadSettings()`:
```csharp
// Check if old format (single SkySettings object)
var oldFormat = _configuration.Get<SkySettings>();
if (oldFormat != null && oldFormat.Port != null)
{
    // Delete old file
    if (File.Exists(_userSettingsFilePath))
    {
        File.Delete(_userSettingsFilePath);
    }
    
    // Create new single-device config
    oldFormat.DeviceNumber = 0;
    oldFormat.DeviceName = "Telescope";
    oldFormat.Enabled = true;
    
    var newRoot = new SettingsRoot 
    { 
        Devices = new List<SkySettings> { oldFormat } 
    };
    
    // Save in new format
    var json = JsonSerializer.Serialize(newRoot, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(_userSettingsFilePath, json);
}
```

**Build:** Verify 0 errors

**Time:** 20 minutes  
**Risk:** Low — settings service changes

---

### Task 3.3 — Update `MountInstance` Constructor (No Changes Needed)

**File:** `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Current constructor:**
```csharp
public MountInstance(string id, SkySettingsInstance settings, string? deviceName = null)
{
    _id = id ?? "mount-0";
    _instanceName = id ?? "default";
    _deviceName = deviceName ?? id ?? "Unnamed Device";
    _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    // ...
}
```

**Status:** ✅ **No changes needed** — constructor already receives full `SkySettingsInstance` with all properties.

**Time:** 0 minutes

---

### Task 3.4 — Update Application Startup Loop

**File:** `GreenSwamp.Alpaca.Server/Program.cs`

**Current (Phase 2):**
```csharp
// Register Device 0 (static settings)
var settingsInstance = new SkySettingsInstance(SkySettings.Settings, settingsService);
UnifiedDeviceRegistry.RegisterDevice(0, settingsInstance, "Telescope 0");
```

**Revised (Phase 3):**
```csharp
// Load all device configurations
var devices = settingsService.GetAllDevices();

// Register each enabled device
foreach (var deviceConfig in devices.Where(d => d.Enabled))
{
    var settingsInstance = new SkySettingsInstance(deviceConfig, settingsService);
    UnifiedDeviceRegistry.RegisterDevice(
        deviceConfig.DeviceNumber,
        settingsInstance,
        deviceConfig.DeviceName
    );
    
    Console.WriteLine($"Registered Device {deviceConfig.DeviceNumber}: {deviceConfig.DeviceName} ({deviceConfig.Mount} on {deviceConfig.Port})");
}

if (devices.Count == 0)
{
    Console.WriteLine("WARNING: No devices configured in appsettings.json Devices array");
}
```

**Build:** Verify 0 errors

**Time:** 15 minutes  
**Risk:** Low — straightforward loop change

---

### Task 3.5 — Create Blazor Device Manager UI

**File (NEW):** `GreenSwamp.Alpaca.Server/Pages/DeviceManager.razor`

**Content:**
```razor
@page "/devices"
@using GreenSwamp.Alpaca.Shared
@inject IVersionedSettingsService SettingsService

<PageTitle>Device Manager</PageTitle>

<h3>Connected Devices</h3>

<table class="table table-striped">
    <thead>
        <tr>
            <th>Device #</th>
            <th>Name</th>
            <th>Mount</th>
            <th>Port</th>
            <th>Status</th>
            <th>Actions</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var device in _devices)
        {
            var instance = UnifiedDeviceRegistry.GetMountInstance(device.DeviceNumber);
            var status = instance?.IsConnected == true ? "Connected" : "Disconnected";
            var badgeClass = instance?.IsConnected == true ? "badge bg-success" : "badge bg-secondary";
            
            <tr>
                <td>@device.DeviceNumber</td>
                <td>@device.DeviceName</td>
                <td>@device.Mount</td>
                <td>@device.Port</td>
                <td><span class="@badgeClass">@status</span></td>
                <td>
                    @if (instance?.IsConnected == true)
                    {
                        <button class="btn btn-sm btn-danger" @onclick="() => Disconnect(device.DeviceNumber)">
                            Disconnect
                        </button>
                    }
                    else
                    {
                        <button class="btn btn-sm btn-primary" @onclick="() => Connect(device.DeviceNumber)">
                            Connect
                        </button>
                    }
                </td>
            </tr>
        }
    </tbody>
</table>

@code {
    private List<SkySettings> _devices = new();

    protected override void OnInitialized()
    {
        _devices = SettingsService.GetAllDevices();
    }

    private void Connect(int deviceNumber)
    {
        var instance = UnifiedDeviceRegistry.GetMountInstance(deviceNumber);
        instance?.SetConnected(GetClientId(), true);
        StateHasChanged();
    }

    private void Disconnect(int deviceNumber)
    {
        var instance = UnifiedDeviceRegistry.GetMountInstance(deviceNumber);
        instance?.SetConnected(GetClientId(), false);
        StateHasChanged();
    }

    private long GetClientId()
    {
        // Generate unique client ID for this browser session
        return DateTime.UtcNow.Ticks;
    }
}
```

**File:** `GreenSwamp.Alpaca.Server/Shared/NavMenu.razor`

**Add menu link:**
```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="devices">
        <span class="bi bi-hdd-network-fill" aria-hidden="true"></span> Devices
    </NavLink>
</div>
```

**Build:** Verify 0 errors, run application, verify `/devices` page loads

**Time:** 30 minutes  
**Risk:** Low — new page, no existing code modified

---

### Task 3.6 — Update `appsettings.json` Example

**File:** `GreenSwamp.Alpaca.Server/appsettings.json`

**Replace entire `SkySettings` block with:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*",
  "Devices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator",
      "Enabled": true,
      "Mount": "Simulator",
      "Port": "COM1",
      "BaudRate": 9600,
      "Latitude": 28.5,
      "Longitude": -81.6,
      "Elevation": 30.0,
      "AlignmentMode": "AltAz",
      "ApertureDiameter": 200,
      "FocalLength": 1000,
      "AutoTrack": false
      // ... (include other default properties as needed)
    }
  ]
}
```

**Note:** Users will customize `appsettings.user.json` for multiple devices.

**Time:** 10 minutes  
**Risk:** None — documentation only

---

### Task 3.7 — Integration Testing

**Test Scenarios:**

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **T1: Single device** | 1. Delete `appsettings.user.json`<br>2. Start application<br>3. Check logs | Logs show: "Registered Device 0: Telescope (Simulator on COM1)" |
| **T2: Dual device** | 1. Edit `appsettings.user.json`:<br>`Devices: [{DeviceNumber:0, Port:"COM1"}, {DeviceNumber:1, Port:"COM3"}]`<br>2. Start application<br>3. Check logs | Logs show both devices registered |
| **T3: Device Manager UI** | 1. Navigate to `/devices`<br>2. Click "Connect" for Device 0<br>3. Verify status badge changes | Status badge shows "Connected" (green) |
| **T4: ConformU Device 0** | 1. Run ConformU against Device 0<br>2. Verify all tests pass | ConformU: PASS (all tests) |
| **T5: ConformU Device 1** | 1. Configure Device 1 as SkyWatcher on COM3<br>2. Run ConformU against Device 1<br>3. Verify all tests pass | ConformU: PASS (all tests) |
| **T6: Disconnect timing** | 1. Connect Device 0<br>2. Measure disconnect time | Disconnect < 100ms (no busy-wait) |
| **T7: Disabled device** | 1. Set `Enabled: false` for Device 1<br>2. Start application<br>3. Verify Device 1 not registered | Only Device 0 in logs; Device 1 not created |

**Time:** 1-2 hours  
**Risk:** High (integration issues, timing violations)

---

## 5. Task Summary

| Task | Description | Time | Risk | Status |
|------|-------------|------|------|--------|
| 3.1 | Add 3 properties to `SkySettings` | 5 min | None | ❌ |
| 3.2 | Update settings service for device array | 20 min | Low | ❌ |
| 3.3 | Review `MountInstance` constructor | 0 min | None | ✅ No changes needed |
| 3.4 | Update `Program.cs` startup loop | 15 min | Low | ❌ |
| 3.5 | Create Blazor Device Manager page | 30 min | Low | ❌ |
| 3.6 | Update `appsettings.json` example | 10 min | None | ❌ |
| 3.7 | Integration testing (7 scenarios) | 1-2 hr | High | ❌ |
| **TOTAL** | | **3-4 hr** | | |

---

## 6. Benefits of This Architecture

| Benefit | Explanation |
|---------|-------------|
| **No redundancy** | Each device has one complete `SkySettings` object with all 134 properties |
| **Clear ownership** | `DeviceNumber`/`DeviceName`/`Enabled` are part of the settings themselves |
| **Full flexibility** | Devices can have completely different locations, mounts, tracking settings, etc. |
| **Simpler code** | No nested classes, no inheritance, no override logic, no fallback chains |
| **Clean migration** | Old files deleted and recreated — no complex backward-compat logic |
| **JSON transparency** | Array structure is obvious; each device is a self-contained block |

---

## 7. Post-Phase 3 State

### JSON Structure

```json
{
  "Devices": [
    { "DeviceNumber": 0, "DeviceName": "Main", "Port": "COM1", ... },
    { "DeviceNumber": 1, "DeviceName": "Guide", "Port": "COM3", ... }
  ]
}
```

### Code Flow

1. **Startup:** `Program.cs` calls `settingsService.GetAllDevices()`
2. **Loop:** For each enabled device, create `SkySettingsInstance` and register
3. **Runtime:** Each `MountInstance` has its own complete `SkySettingsInstance`
4. **UI:** Device Manager page queries `UnifiedDeviceRegistry` for status

### Blazor UI

```
╔══════════════════════════════════════════════════════════════╗
║ Device Manager                                               ║
╠════╦═══════════╦═══════════╦══════╦═══════════╦═════════════╣
║ #  ║ Name      ║ Mount     ║ Port ║ Status    ║ Actions     ║
╠════╬═══════════╬═══════════╬══════╬═══════════╬═════════════╣
║ 0  ║ Main      ║ Simulator ║ COM1 ║ Connected ║ [Disconnect]║
║ 1  ║ Guide     ║ SkyWatcher║ COM3 ║ Disconn.  ║ [Connect]   ║
╚════╩═══════════╩═══════════╩══════╩═══════════╩═════════════╝
```

---

## 8. Next Steps After Phase 3

**Phase 4 (Future):** Multi-device Blazor UI enhancements
- Real-time status updates (SignalR)
- Per-device coordinate displays
- Device enable/disable at runtime
- Settings editor with per-device validation

**Phase 5 (Future):** Advanced multi-device features
- Device groups (main + guide)
- Automated focus/guider coordination
- Dithering support
- Multi-device logging/monitoring

---

## 9. References

- **Phase 1 Report:** `docs/StaticToInstance-ProgressReport.md` Section 14
- **Phase 2 Report:** `docs/StaticToInstance-ProgressReport.md` Section 16
- **Migration Plan:** `docs/StaticToInstance-MigrationPlan.md` Phase 0 Q1-Q6
- **Serial Architecture:** `docs/MultiTelescope-SerialArchitecture.md`
- **Option C Plan:** `docs/MultiTelescope-OptionC-ImplementationPlan.md`

---

**END OF PLAN**
