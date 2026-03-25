# Option C Phase 3 — Configuration & UI
## Multi-Device Serial Settings + Blazor Connection Panel

**Date:** March 25, 2026  
**Status:** Ready to begin  
**Prerequisites:** Phase 1 ✅ Complete, Phase 2 ✅ Complete  
**Estimated Duration:** 4-6 hours

---

## 1. Overview

Phase 3 adds multi-device support to the configuration and UI layers:

1. **Configuration:** Support multiple serial port/device configurations in `appsettings.json`
2. **UI:** Blazor connection status panel showing all registered devices
3. **Testing:** Verify dual-device operation with ConformU

---

## 2. Current Architecture

### 2.1 Settings Structure (Single Device)

**Current `appsettings.json`:**
```json
{
  "Mount": "SkyWatcher",
  "Port": "COM3",
  "BaudRate": 9600,
  "AlignmentMode": "GermanPolar",
  "Latitude": 28.5,
  "Longitude": -81.6,
  ...
}
```

**Loaded by:**
- `IVersionedSettingsService` → `SkySettingsInstance`
- One settings object per application
- `MountInstance` receives a reference to this shared settings object

### 2.2 Current Limitations

| Issue | Impact |
|-------|--------|
| Single `Port` field | Cannot configure COM3 for device 0 and COM4 for device 1 |
| No device array | `appsettings.json` has no concept of multiple devices |
| Shared settings object | All `MountInstance` objects read the same `Port`/`BaudRate`/etc. |
| UI shows single status | Blazor pages display only "Connected: true/false" |

---

## 3. Phase 3 Goals

| Goal | Acceptance Criteria |
|------|---------------------|
| **G1: Per-device serial config** | `appsettings.json` has `Devices` array; each entry specifies `Port`, `BaudRate`, etc. |
| **G2: Device settings isolation** | Device 0 uses COM3 @ 9600 baud; Device 1 uses COM4 @ 115200 baud (independent) |
| **G3: Backward compatibility** | Existing single-device `appsettings.json` files continue to work (auto-migrate to Device 0) |
| **G4: Blazor multi-device panel** | UI shows table: Device #, Mount Type, Port, Connection Status, Actions (Connect/Disconnect) |
| **G5: Dual-device testing** | ConformU passes for both Device 0 and Device 1 simultaneously |

---

## 4. Implementation Tasks

### Task 3.1 — Extend Settings Model for Multi-Device

**Files:** `GreenSwamp.Alpaca.Settings/Models/SkySettings.cs`

**Plan:**

1. Add `DeviceSettings` nested class:
   ```csharp
   public class DeviceSettings
   {
       public int DeviceNumber { get; set; }
       public string MountType { get; set; } = "Simulator"; // "Simulator" | "SkyWatcher"
       public string Port { get; set; } = "COM1";
       public long BaudRate { get; set; } = 9600;
       public bool Enabled { get; set; } = true;
       
       // Device-specific overrides (optional):
       public double? Latitude { get; set; }
       public double? Longitude { get; set; }
       public string? AlignmentMode { get; set; }
       // ... other per-device overrides
   }
   ```

2. Add `Devices` array to `SkySettings`:
   ```csharp
   public List<DeviceSettings>? Devices { get; set; }
   ```

3. Keep existing single-device properties as "global defaults":
   - `Mount`, `Port`, `BaudRate`, etc. remain for backward compatibility
   - If `Devices` is null/empty, use the legacy single-device config

**Backward Compatibility Logic:**
```csharp
// In VersionedSettingsService or SkySettingsInstance:
public List<DeviceSettings> GetDeviceConfigurations()
{
    // If multi-device config exists, use it:
    if (Devices != null && Devices.Count > 0)
        return Devices;
    
    // Fallback: create Device 0 from legacy properties
    return new List<DeviceSettings>
    {
        new DeviceSettings
        {
            DeviceNumber = 0,
            MountType = Mount.ToString(),
            Port = Port,
            BaudRate = BaudRate,
            Enabled = true
        }
    };
}
```

**Risk:** Low — additive change; existing properties remain functional.

---

### Task 3.2 — Update `MountInstance` to Use Device-Specific Settings

**Files:** `GreenSwamp.Alpaca.MountControl/MountInstance.cs`

**Current Issue:**
`MountInstance` constructor receives `SkySettingsInstance _settings` — a shared object used by all devices.

**Target:**
Each `MountInstance` should have its own `DeviceSettings` merged with global defaults.

**Plan:**

1. Add `DeviceSettings?` field to `MountInstance`:
   ```csharp
   private readonly DeviceSettings? _deviceSettings;
   ```

2. Update constructor:
   ```csharp
   public MountInstance(string instanceName, SkySettingsInstance globalSettings, DeviceSettings? deviceSettings = null)
   {
       _instanceName = instanceName;
       _settings = globalSettings; // Global defaults
       _deviceSettings = deviceSettings; // Device-specific overrides
   }
   ```

3. Add helper method:
   ```csharp
   private T GetDeviceSetting<T>(Func<DeviceSettings, T?> deviceGetter, Func<SkySettingsInstance, T> globalGetter)
       where T : struct
   {
       return _deviceSettings != null && deviceGetter(_deviceSettings).HasValue
           ? deviceGetter(_deviceSettings)!.Value
           : globalGetter(_settings);
   }
   
   private string GetDeviceSettingString(Func<DeviceSettings, string?> deviceGetter, Func<SkySettingsInstance, string> globalGetter)
   {
       return _deviceSettings != null && !string.IsNullOrEmpty(deviceGetter(_deviceSettings))
           ? deviceGetter(_deviceSettings)!
           : globalGetter(_settings);
   }
   ```

4. Update serial port access in `OpenSerial()`:
   ```csharp
   // Before:
   var port = _settings.Port;
   var baudRate = _settings.BaudRate;
   
   // After:
   var port = GetDeviceSettingString(d => d.Port, s => s.Port);
   var baudRate = GetDeviceSetting(d => d.BaudRate, s => s.BaudRate);
   ```

5. Update mount type access in `MountStart()`:
   ```csharp
   // Before:
   switch (_settings.Mount)
   
   // After:
   var mountType = _deviceSettings?.MountType != null
       ? Enum.Parse<MountType>(_deviceSettings.MountType)
       : _settings.Mount;
   switch (mountType)
   ```

**Risk:** Medium — affects serial port initialization (core connection path).

---

### Task 3.3 — Update `UnifiedDeviceRegistry.RegisterDevice()` to Pass Device Settings

**Files:** `GreenSwamp.Alpaca.Shared/UnifiedDeviceRegistry.cs`

**Current:**
```csharp
public static void RegisterDevice(int deviceNumber, SkySettingsInstance settings, IServiceProvider services)
{
    var instance = MountInstanceRegistry.CreateInstance($"Device{deviceNumber}", settings);
    // ...
}
```

**Target:**
```csharp
public static void RegisterDevice(int deviceNumber, SkySettingsInstance globalSettings, DeviceSettings? deviceSettings, IServiceProvider services)
{
    var instance = MountInstanceRegistry.CreateInstance($"Device{deviceNumber}", globalSettings, deviceSettings);
    // ...
}
```

**Plan:**

1. Add `deviceSettings` parameter to `RegisterDevice()`
2. Update `MountInstanceRegistry.CreateInstance()` to accept and pass `deviceSettings`
3. Update all call sites (typically in `Program.cs` startup configuration)

**Risk:** Low — mechanical parameter threading.

---

### Task 3.4 — Update Startup Configuration in `Program.cs`

**Files:** `GreenSwamp.Alpaca.Server/Program.cs`

**Current:**
```csharp
// Single device registration:
var settingsService = builder.Services.BuildServiceProvider().GetRequiredService<IVersionedSettingsService>();
var settings = settingsService.GetSettings();
UnifiedDeviceRegistry.RegisterDevice(0, settings, builder.Services.BuildServiceProvider());
```

**Target:**
```csharp
// Multi-device registration:
var settingsService = builder.Services.BuildServiceProvider().GetRequiredService<IVersionedSettingsService>();
var globalSettings = settingsService.GetSettings();
var deviceConfigs = globalSettings.GetDeviceConfigurations(); // New helper from Task 3.1

foreach (var deviceConfig in deviceConfigs.Where(d => d.Enabled))
{
    UnifiedDeviceRegistry.RegisterDevice(deviceConfig.DeviceNumber, globalSettings, deviceConfig, builder.Services.BuildServiceProvider());
}
```

**Verification:**
- If `Devices` array exists → registers all enabled devices
- If `Devices` is null → falls back to single Device 0 (backward compat)

**Risk:** Low — additive loop; backward compat maintained.

---

### Task 3.5 — Create Blazor Multi-Device Connection Panel

**Files:** 
- `GreenSwamp.Alpaca.Server/Pages/DeviceManager.razor` (new)
- `GreenSwamp.Alpaca.Server/Pages/DeviceManager.razor.cs` (new code-behind)
- `GreenSwamp.Alpaca.Server/Shared/NavMenu.razor` (add link)

**UI Mockup:**

```
┌─────────────────────────────────────────────────────────────┐
│ GreenSwamp Alpaca — Device Manager                          │
├─────────────────────────────────────────────────────────────┤
│ Device # │ Type       │ Port  │ Connected │ Actions         │
├──────────┼────────────┼───────┼───────────┼─────────────────┤
│    0     │ Simulator  │ N/A   │   Yes ✓   │ [Disconnect]    │
│    1     │ SkyWatcher │ COM3  │   No  ✗   │ [Connect]       │
│    2     │ SkyWatcher │ COM4  │   Yes ✓   │ [Disconnect]    │
└─────────────────────────────────────────────────────────────┘

[Refresh Status]
```

**`DeviceManager.razor`:**
```html
@page "/device-manager"
@using GreenSwamp.Alpaca.MountControl
@using GreenSwamp.Alpaca.Shared
@inject IVersionedSettingsService SettingsService

<h3>Device Manager</h3>

<table class="table table-striped">
    <thead>
        <tr>
            <th>Device #</th>
            <th>Type</th>
            <th>Port</th>
            <th>Connected</th>
            <th>Actions</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var device in Devices)
        {
            <tr>
                <td>@device.DeviceNumber</td>
                <td>@device.MountType</td>
                <td>@device.Port</td>
                <td>
                    @if (device.IsConnected)
                    {
                        <span class="badge bg-success">Yes ✓</span>
                    }
                    else
                    {
                        <span class="badge bg-secondary">No ✗</span>
                    }
                </td>
                <td>
                    @if (device.IsConnected)
                    {
                        <button class="btn btn-sm btn-danger" @onclick="() => Disconnect(device.DeviceNumber)">Disconnect</button>
                    }
                    else
                    {
                        <button class="btn btn-sm btn-success" @onclick="() => Connect(device.DeviceNumber)">Connect</button>
                    }
                </td>
            </tr>
        }
    </tbody>
</table>

<button class="btn btn-primary mt-3" @onclick="RefreshStatus">Refresh Status</button>

@code {
    private List<DeviceStatus> Devices { get; set; } = new();

    protected override void OnInitialized()
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        Devices.Clear();
        var globalSettings = SettingsService.GetSettings();
        var deviceConfigs = globalSettings.GetDeviceConfigurations();

        foreach (var config in deviceConfigs.Where(d => d.Enabled))
        {
            var instance = MountInstanceRegistry.GetInstance(config.DeviceNumber);
            Devices.Add(new DeviceStatus
            {
                DeviceNumber = config.DeviceNumber,
                MountType = config.MountType,
                Port = config.Port,
                IsConnected = instance?.IsConnected ?? false
            });
        }

        StateHasChanged();
    }

    private void Connect(int deviceNumber)
    {
        var instance = MountInstanceRegistry.GetInstance(deviceNumber);
        if (instance != null)
        {
            instance.SetConnected($"UI-Device{deviceNumber}", true);
            RefreshStatus();
        }
    }

    private void Disconnect(int deviceNumber)
    {
        var instance = MountInstanceRegistry.GetInstance(deviceNumber);
        if (instance != null)
        {
            instance.SetConnected($"UI-Device{deviceNumber}", false);
            RefreshStatus();
        }
    }

    private class DeviceStatus
    {
        public int DeviceNumber { get; set; }
        public string MountType { get; set; } = "";
        public string Port { get; set; } = "";
        public bool IsConnected { get; set; }
    }
}
```

**Add to `NavMenu.razor`:**
```html
<div class="nav-item px-3">
    <NavLink class="nav-link" href="device-manager">
        <span class="oi oi-list-rich" aria-hidden="true"></span> Device Manager
    </NavLink>
</div>
```

**Risk:** Low — new UI component; doesn't affect existing pages.

---

### Task 3.6 — Update `appsettings.json` Example for Multi-Device

**Files:** `GreenSwamp.Alpaca.Server/appsettings.json`

**Example Configuration:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  
  "AllowedHosts": "*",
  
  // Global defaults (used if device-specific values not provided):
  "Mount": "Simulator",
  "Port": "COM1",
  "BaudRate": 9600,
  "AlignmentMode": "GermanPolar",
  "Latitude": 28.5,
  "Longitude": -81.6,
  "Elevation": 30,
  
  // Multi-device configuration:
  "Devices": [
    {
      "DeviceNumber": 0,
      "MountType": "Simulator",
      "Port": "N/A",
      "BaudRate": 9600,
      "Enabled": true
    },
    {
      "DeviceNumber": 1,
      "MountType": "SkyWatcher",
      "Port": "COM3",
      "BaudRate": 9600,
      "Enabled": true,
      "AlignmentMode": "GermanPolar",
      "Latitude": 28.5,
      "Longitude": -81.6
    },
    {
      "DeviceNumber": 2,
      "MountType": "SkyWatcher",
      "Port": "COM4",
      "BaudRate": 115200,
      "Enabled": false,
      "AlignmentMode": "AltAz",
      "Latitude": 51.5,
      "Longitude": -0.1
    }
  ]
}
```

**Migration Notes:**
- If `Devices` array is missing → falls back to single device using global `Mount`/`Port`/etc.
- Each device can override global settings (e.g., Device 2 uses different `BaudRate` and `AlignmentMode`)
- `Enabled: false` devices are not registered at startup

**Risk:** None — documentation/example only.

---

### Task 3.7 — Integration Testing with Dual Devices

**Test Scenarios:**

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **Dual Simulator** | Configure Device 0 and Device 1 as Simulator; connect both via UI | Both show "Connected ✓" |
| **Simulator + SkyWatcher** | Device 0 = Simulator, Device 1 = SkyWatcher COM3; connect both | Device 0 connects immediately; Device 1 connects to hardware |
| **ConformU Device 0** | Run ConformU against `http://localhost:5000/api/v1/telescope/0/` | All tests pass (same as Phase 2) |
| **ConformU Device 1** | Run ConformU against `http://localhost:5000/api/v1/telescope/1/` | All tests pass (independent operation) |
| **Disconnect Device 0** | Disconnect Device 0 via UI; verify Device 1 still connected | Device 1 unaffected; disconnect < 100ms |
| **Backward Compat** | Remove `Devices` array from `appsettings.json`; restart app | App falls back to single Device 0 using global settings |

**Risk:** High — end-to-end validation; requires physical hardware for full test.

---

## 5. Execution Order

| Task | Description | Estimated Time | Risk |
|------|-------------|----------------|------|
| 3.1 | Extend settings model | 30 min | Low |
| 3.2 | Update `MountInstance` for device settings | 1 hour | Medium |
| 3.3 | Thread device settings through registry | 30 min | Low |
| 3.4 | Update `Program.cs` startup | 30 min | Low |
| 3.5 | Create Blazor Device Manager UI | 1.5 hours | Low |
| 3.6 | Update `appsettings.json` example | 15 min | None |
| 3.7 | Integration testing | 1-2 hours | High |

**Total:** 4-6 hours

---

## 6. Rollback Plan

If Phase 3 introduces regressions:

1. **Configuration rollback:** Remove `Devices` array from `appsettings.json` → app falls back to single-device mode
2. **Code rollback:** Revert Tasks 3.2-3.4 → `MountInstance` uses shared settings as before
3. **UI rollback:** Remove Device Manager page → no impact on existing Telescope UI

---

## 7. Future Enhancements (Post-Phase 3)

| Enhancement | Description |
|-------------|-------------|
| **Dynamic device registration** | Add/remove devices at runtime without restart |
| **Hot config reload** | Update `appsettings.json` and refresh devices without app restart |
| **Per-device logging** | Separate log files for each device |
| **Device health monitoring** | Show connection uptime, error count, last command timestamp |
| **ASCOM Discovery integration** | Auto-detect available COM ports and suggest configurations |

---

**Ready to proceed? Start with Task 3.1 (extend settings model).**
