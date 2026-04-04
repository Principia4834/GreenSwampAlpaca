 # Settings Architecture Redesign — Requirements & Design

**2026-04-03 12:39**
**Status:** Section 18 added — high-level design for alignment mode and observatory settings (pending CSV values from Andy)
**Scope:** Settings layer redesign before any Blazor UI changes  
**Branch target:** `master` (GreenSwampAlpaca)

---

## 1. Background & Motivation

The current `appsettings.user.json` bundles two distinct concerns into a single JSON file:

```json
{
  "AlpacaDevices": [ /* ASCOM discovery metadata */ ],
  "Devices":       [ /* Full per-device operational settings, ~100 fields each */ ]
}
```

This coupling requires `DeviceSynchronizationService` (110+ lines) whose sole purpose is to maintain 1-to-1 consistency between these two arrays. It also forces a global `SemaphoreSlim(1,1)` that serialises all device saves, and an API shim (`GetSettings()` → always device 0) that prevents true multi-device settings management.

The UI planning exercise confirmed that `MountSettings.razor` cannot be made per-device without an explicit `GetDeviceSettings(int deviceNumber)` API. Building the UI against the current interface would require immediate re-work.

---

## 2. Decisions (Andy, 2026-04-03)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Alpaca discovery metadata → `appsettings.alpaca.user.json` | Clear purpose; server-wide metadata separated from device config |
| D2 | Per-device settings → `device-nn.settings.json` (nn = 00–99) | Zero-padded filenames are sortable; hard 100-device limit eliminates defensive complexity |
| D3 | No backward compatibility with old single-file format | Existing users are developers; clean redesign reduces implementation scope by ~200 lines |
| D4 | Complete settings redesign **before** any Blazor UI changes | Prevents building UI against `GetSettings()` shim; correct API available from UI-day-1 |
| D5 | `observatory.settings.json` is **version-specific** (inside `{version}\`) | Enables complete settings reproduction by file copy for a specific version; consistent with all other user files (Q5, 2026-04-03) |

---

## 3. File Layout

### 3.1 Runtime user files (per app version)

```
%AppData%\GreenSwampAlpaca\{version}\
│
├── appsettings.user.json           ← Server-wide settings (monitor, auth, logging)
│                                      NOT per-device. Retained from current design.
│
├── observatory.settings.json       ← Observatory physical properties (latitude, longitude,
│                                      elevation, UTC offset). Created at first run from app
│                                      defaults if absent; intended for user editing at setup.
│                                      Applies to all new devices. See Section 17.
│
├── appsettings.alpaca.user.json    ← AlpacaDevices array (ASCOM discovery metadata only)
│                                      Small; one entry per registered device.
│
├── device-00.settings.json         ← Full SkySettings for device number 0
├── device-01.settings.json         ← Full SkySettings for device number 1
├── device-02.settings.json         ← Full SkySettings for device number 2
│   ...
└── device-99.settings.json         ← Full SkySettings for device number 99 (max)
```

### 3.2 File naming rule

`device-{n:D2}.settings.json` where `n` is the ASCOM device number (0–99).

| Device number | Filename |
|:---:|---|
| 0 | `device-00.settings.json` |
| 9 | `device-09.settings.json` |
| 10 | `device-10.settings.json` |
| 99 | `device-99.settings.json` |

### 3.3 Checked-in server defaults (unchanged structure)

`GreenSwamp.Alpaca.Server\appsettings.json` retains both `AlpacaDevices` and `Devices` arrays. This file is the **factory default source** used on first-run to generate the per-device user files. It is never modified at runtime.

---

## 4. File Format Specifications

### 4.1 `appsettings.alpaca.user.json`

Contains the `AlpacaDevices` array only. Maximum 100 entries.

```json
{
  "AlpacaDevices": [
    {
      "DeviceNumber": 0,
      "DeviceName": "Simulator (GEM)",
      "DeviceType": "Telescope",
      "UniqueId": "87654321-4321-4321-4321-cba987654321"
    },
    {
      "DeviceNumber": 1,
      "DeviceName": "SkyWatcher EQ6-R",
      "DeviceType": "Telescope",
      "UniqueId": "12345678-1234-1234-1234-abc123456789"
    }
  ]
}
```

**Validation rules:**
- `DeviceNumber`: integer, 0–99, unique within the array
- `DeviceName`: non-empty string
- `DeviceType`: must be `"Telescope"` (current scope)
- `UniqueId`: valid GUID string
- Array length: 0–100

### 4.2 `device-nn.settings.json`

Contains a **single flat `SkySettings` object** (no wrapping array). The `DeviceNumber` field inside the file must match the `nn` in the filename; a mismatch is a validation error.

```json
{
  "DeviceNumber": 0,
  "DeviceName": "Simulator (GEM)",
  "Enabled": true,
  "Mount": "Simulator",
  "Port": "COM1",
  "BaudRate": 9600,
  "Latitude": 51.21135,
  "Longitude": -1.459816,
  "Elevation": 10.0,
  "AlignmentMode": "GermanPolar",
  "... all remaining SkySettings fields ..."
}
```

**Validation rules:**
- `DeviceNumber` in file content must equal `nn` from filename
- Required fields: `Mount`, `Port`, `AlignmentMode` (existing `[Required]` attributes on `SkySettings`)
- File must be valid JSON

### 4.3 `appsettings.user.json`

Retains server-wide settings. The `Devices` and `AlpacaDevices` keys are **removed** from this file in the new design. Contains only:

```json
{
  "MonitorSettings": { ... },
  "ServerSettings": { ... }
}
```

---

## 5. Interface Redesign: `IVersionedSettingsService`

### 5.1 New interface

```csharp
public interface IVersionedSettingsService
{
    // ── Metadata ─────────────────────────────────────────────────────────────
    string CurrentVersion { get; }
    string UserSettingsPath { get; }          // appsettings.user.json path
    string AlpacaSettingsPath { get; }        // appsettings.alpaca.user.json path
    string GetDeviceSettingsPath(int deviceNumber);  // device-nn.settings.json path

    // ── Server-wide settings (appsettings.user.json) ─────────────────────────
    MonitorSettings GetMonitorSettings();
    Task SaveMonitorSettingsAsync(MonitorSettings settings);

    // ── Alpaca discovery (appsettings.alpaca.user.json) ──────────────────────
    List<AlpacaDevice> GetAlpacaDevices();
    Task SaveAlpacaDevicesAsync(List<AlpacaDevice> devices);
    Task AddAlpacaDeviceAsync(AlpacaDevice device);
    Task RemoveAlpacaDeviceAsync(int deviceNumber);

    // ── Per-device settings (device-nn.settings.json) ────────────────────────
    SkySettings? GetDeviceSettings(int deviceNumber);
    List<SkySettings> GetAllDeviceSettings();
    Task SaveDeviceSettingsAsync(int deviceNumber, SkySettings settings);
    Task DeleteDeviceSettingsAsync(int deviceNumber);
    bool DeviceSettingsExist(int deviceNumber);

    // ── Validation ────────────────────────────────────────────────────────────
    ValidationResult ValidateDeviceSettings(int deviceNumber);
    ValidationResult ValidateAlpacaDevices();

    // ── Events ────────────────────────────────────────────────────────────────
    event EventHandler<SkySettings>? DeviceSettingsChanged;
    event EventHandler<MonitorSettings>? MonitorSettingsChanged;
}
```

### 5.2 Removed from interface (no replacement)

| Removed method | Reason |
|---|---|
| `SkySettings GetSettings()` | Device-0 shim — replaced by `GetDeviceSettings(0)` |
| `List<SkySettings> GetAllDevices()` | Replaced by `GetAllDeviceSettings()` |
| `List<SkySettings> GetAllDevices(out ValidationResult)` | Validation now per-device |
| `Task SaveSettingsAsync(SkySettings)` | Ambiguous — replaced by `SaveDeviceSettingsAsync(int, SkySettings)` |
| `Task<bool> MigrateFromPreviousVersionAsync()` | No backward compat (D3) |
| `string[] AvailableVersions` | Migration removed |

---

## 6. Implementation: `VersionedSettingsService` Changes

### 6.1 File locking strategy

Replace single global `SemaphoreSlim(1,1)` with:

```csharp
private static readonly SemaphoreSlim _alpacaFileLock = new(1, 1);
private static readonly SemaphoreSlim _monitorFileLock = new(1, 1);
private static readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceFileLocks = new();

private SemaphoreSlim GetDeviceLock(int deviceNumber)
    => _deviceFileLocks.GetOrAdd(deviceNumber, _ => new SemaphoreSlim(1, 1));
```

This enables concurrent saves to different device files while still preventing concurrent writes to the same file.

### 6.2 Path helpers

```csharp
public string GetDeviceSettingsPath(int deviceNumber)
{
    if (deviceNumber < 0 || deviceNumber > 99)
        throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Device number must be 0–99");
    return Path.Combine(_currentVersionPath, $"device-{deviceNumber:D2}.settings.json");
}

public string AlpacaSettingsPath
    => Path.Combine(_currentVersionPath, "appsettings.alpaca.user.json");
```

### 6.3 First-run initialisation (replaces backward-compat migration)

On startup, if no user files exist for the current version, initialise from `appsettings.json` factory defaults:

```
GetAllDeviceSettings() called
  → no device-nn.settings.json files found
  → read IConfiguration["Devices"] array (appsettings.json)
  → write device-{n:D2}.settings.json for each entry
  → read IConfiguration["AlpacaDevices"] array
  → write appsettings.alpaca.user.json
```

No migration from prior version format (D3).

### 6.4 `GetAllDeviceSettings()` implementation

```
1. Enumerate device-*.settings.json files in _currentVersionPath
2. Parse nn from filename
3. Deserialize each as SkySettings
4. Validate DeviceNumber in content matches nn in filename
5. Return sorted list (ascending DeviceNumber)
6. If none found → run first-run initialisation → retry once
```

### 6.5 `SaveDeviceSettingsAsync(int deviceNumber, SkySettings settings)` implementation

```
1. Validate deviceNumber 0–99
2. Acquire GetDeviceLock(deviceNumber)
3. Ensure settings.DeviceNumber == deviceNumber
4. Serialize to JSON (indented)
5. Write to GetDeviceSettingsPath(deviceNumber) (atomic: write temp → rename)
6. Release lock
7. Raise DeviceSettingsChanged event
```

---

## 7. Removed Services

### 7.1 `DeviceSynchronizationService` — DELETED

**Files to delete:**
- `GreenSwamp.Alpaca.Settings\Services\DeviceSynchronizationService.cs`
- `GreenSwamp.Alpaca.Settings\Services\IDeviceSynchronizationService.cs`

**Reason:** The sync service existed solely to maintain consistency between `AlpacaDevices` and `Devices` in a single file. With separate files, there is no synchronisation needed — each file is independently authoritative for its concern.

### 7.2 DI registration cleanup

In `SettingsServiceCollectionExtensions.cs`:

```csharp
// Remove:
services.AddSingleton<IDeviceSynchronizationService, DeviceSynchronizationService>();

// Keep:
services.AddSingleton<IVersionedSettingsService, VersionedSettingsService>();
```

`VersionedSettingsService` constructor no longer takes `IDeviceSynchronizationService`.

---

## 8. Cascade Changes by Project

### 8.1 `GreenSwamp.Alpaca.Settings` (primary changes)

| File | Change |
|------|--------|
| `Services\IVersionedSettingsService.cs` | Full redesign — Section 5.1 |
| `Services\VersionedSettingsService.cs` | Full redesign — Section 6 |
| `Services\DeviceSynchronizationService.cs` | **DELETE** |
| `Services\IDeviceSynchronizationService.cs` | **DELETE** |
| `Extensions\SettingsServiceCollectionExtensions.cs` | Remove `IDeviceSynchronizationService` registration |
| `Templates\appsettings.json` | **DELETE** — flat `SkySettings` object; replaced by first-run init from server `appsettings.json` |

### 8.2 `GreenSwamp.Alpaca.Server` (call-site updates)

| File | Change |
|------|--------|
| `Program.cs` | Replace `GetAllDevices()` call with `GetAllDeviceSettings()` at startup device loading |
| `Services\UnifiedDeviceRegistry.cs` | `RegisterDevice()` — call `AddAlpacaDeviceAsync()` + `SaveDeviceSettingsAsync()` separately; remove sync service references |
| `Controllers\SetupDevicesController.cs` | `GetDevices()` stays (reads from `MountInstanceRegistry`); Add/Remove device endpoints call new `IVersionedSettingsService` methods |
| `Pages\MountSettings.razor` | Replace `@inject IVersionedSettingsService` usage: `GetSettings()` → `GetDeviceSettings(DeviceNumber)`, `SaveSettingsAsync()` → `SaveDeviceSettingsAsync(DeviceNumber, ...)`. Add `[Parameter] int DeviceNumber = 0` |
| `Pages\DeviceManager.razor` | Update Add/Remove actions to call new service methods |

### 8.3 `GreenSwamp.Alpaca.MountControl` (no structural changes)

`SkySettingsInstance` is populated from `SkySettings` DTO at startup — this mechanism is unchanged. The source of that DTO changes (now from `GetDeviceSettings(n)` instead of `GetAllDevices()[n]`), but `SkySettingsInstance` itself is unaffected.

### 8.4 `GreenSwamp.Alpaca.MountControl.Tests` (no changes)

Integration test (currently skipped/deferred) is unaffected by settings file format.

---

## 9. Startup Flow (Revised)

```
Program.cs startup
│
├── Build IConfiguration from appsettings.json + appsettings.{env}.json
│
├── Register IVersionedSettingsService (singleton)
│
├── VersionedSettingsService constructor:
│   ├── Determine CurrentVersion (assembly informational version)
│   ├── Create %AppData%\GreenSwampAlpaca\{version}\ directory
│   └── (No migration — D3)
│
├── Load AlpacaDevices for ASCOM DeviceManager registration:
│   ├── GetAlpacaDevices()
│   │   ├── Read appsettings.alpaca.user.json
│   │   └── If not found → first-run init from IConfiguration["AlpacaDevices"]
│   └── For each AlpacaDevice: DeviceManager.LoadTelescope(...)
│
└── Load per-device settings for MountInstanceRegistry:
    ├── GetAllDeviceSettings()
    │   ├── Enumerate device-nn.settings.json files
    │   └── If none found → first-run init from IConfiguration["Devices"]
    └── For each SkySettings (Enabled == true):
        ├── Create SkySettingsInstance(settings, settingsService)
        └── MountInstanceRegistry.CreateInstance(deviceNumber, settingsInstance, deviceName)
```

---

## 10. Save Flow (Revised)

### Settings save from UI (MountSettings.razor)

```
User edits settings → clicks Save
│
├── Blazor EditForm OnValidSubmit
├── Call settingsService.SaveDeviceSettingsAsync(deviceNumber, editedSettings)
│   ├── Acquire per-device file lock
│   ├── Serialize SkySettings → JSON
│   ├── Atomic write (temp file → rename)
│   └── Raise DeviceSettingsChanged event
│
└── MountInstance subscribes to DeviceSettingsChanged
    └── Apply changed settings to live SkySettingsInstance fields
```

### Device registration (add new device)

```
POST /setup/devices (AddDeviceRequest)
│
├── Validate deviceNumber 0–99, not already registered
├── Create SkySettings from request (or copy defaults)
├── settingsService.SaveDeviceSettingsAsync(deviceNumber, settings)
├── settingsService.AddAlpacaDeviceAsync(alpacaDevice)
├── MountInstanceRegistry.CreateInstance(deviceNumber, ...)
└── DeviceManager.LoadTelescope(deviceNumber, ...)
```

### Device removal

```
DELETE /setup/devices/{deviceNumber}
│
├── Validate not a reserved slot (if applicable)
├── MountInstanceRegistry.RemoveInstance(deviceNumber)  → calls Disconnect()
├── DeviceManager.UnloadTelescope(deviceNumber)
├── settingsService.RemoveAlpacaDeviceAsync(deviceNumber)
└── settingsService.DeleteDeviceSettingsAsync(deviceNumber)
    └── Deletes device-nn.settings.json
```

---

## 11. Constraints & Limits

| Constraint | Value | Enforcement |
|---|:---:|---|
| Max device number | 99 | `ArgumentOutOfRangeException` in `GetDeviceSettingsPath()` |
| Max devices | 100 | Validated in `AddAlpacaDeviceAsync()` before write |
| File lock timeout | 5 seconds | `SemaphoreSlim.WaitAsync(TimeSpan)` |
| JSON write strategy | Atomic (temp → rename) | Prevents partial-write corruption |
| File encoding | UTF-8, no BOM | `JsonSerializerOptions` + `File.WriteAllTextAsync` with `Encoding.UTF8` |
| JSON format | Indented | Human-readable for offline editing |

---

## 12. Validation Approach

### Device settings validation (`ValidateDeviceSettings(int deviceNumber)`)

| Check | Error code | Severity |
|---|---|---|
| File not found | `DEVICE_FILE_NOT_FOUND` | Info (triggers first-run) |
| File not readable | `DEVICE_FILE_ACCESS_DENIED` | Error |
| Invalid JSON | `DEVICE_FILE_PARSE_ERROR` | Error |
| DeviceNumber in content ≠ nn in filename | `DEVICE_NUMBER_MISMATCH` | Error |
| Missing required fields | `DEVICE_MISSING_REQUIRED` | Error |
| DeviceNumber out of range | `DEVICE_NUMBER_OUT_OF_RANGE` | Error |

### Alpaca devices validation (`ValidateAlpacaDevices()`)

| Check | Error code | Severity |
|---|---|---|
| File not found | `ALPACA_FILE_NOT_FOUND` | Info (triggers first-run) |
| Duplicate DeviceNumber | `ALPACA_DUPLICATE_DEVICE_NUMBER` | Error |
| DeviceNumber 0–99 violation | `ALPACA_DEVICE_NUMBER_OUT_OF_RANGE` | Error |
| Array > 100 entries | `ALPACA_MAX_DEVICES_EXCEEDED` | Error |
| No corresponding device-nn file | `ALPACA_ORPHANED_ENTRY` | Warning |

---

## 13. Implementation Phases

| Phase | Description | Files changed | Risk |
|-------|-------------|---|:---:|
| **S1** | New `IVersionedSettingsService` interface | `IVersionedSettingsService.cs` | 🟢 |
| **S2** | New `VersionedSettingsService` implementation | `VersionedSettingsService.cs` | 🔴 High |
| **S3** | Delete `DeviceSynchronizationService` + `IDeviceSynchronizationService` | 2 files deleted | 🟡 |
| **S4** | Update `SettingsServiceCollectionExtensions` | 1 file | 🟢 |
| **S5** | Update `Program.cs` startup — call `GetAllDeviceSettings()` | `Program.cs` | 🟡 |
| **S6** | Update `UnifiedDeviceRegistry` + `SetupDevicesController` | 2 files | 🟡 |
| **S7** | Delete `Templates\appsettings.json` | 1 file | 🟢 |
| **S8** | **Build verification** — all projects green | — | gate |
| **S9** | Update `MountSettings.razor` — per-device parameter + new service calls | 1 file | 🟡 |
| **S10** | Update `DeviceManager.razor` — new add/remove calls | 1 file | 🟢 |
| **S11** | Final build + smoke test (simulator, two devices) | — | gate |

**Build gate rule:** `run_build` must succeed before proceeding to next phase. If S2 breaks the build, stop and revert S2 before attempting S3.

**Note:** Phases SA–SF (alignment mode and observatory settings) will be inserted before S8 once Andy's CSV property definitions are received. See Section 18.7 for the full phase list.

---

## 14. Testing Approach

### Unit testable (no mount hardware required)

- `VersionedSettingsService.GetDeviceSettings(0)` → reads `device-00.settings.json`
- `VersionedSettingsService.SaveDeviceSettingsAsync(0, settings)` → file written atomically
- `VersionedSettingsService.GetAllDeviceSettings()` → returns all valid files, skips invalid
- `VersionedSettingsService.AddAlpacaDeviceAsync(...)` → entry appears in `appsettings.alpaca.user.json`
- `VersionedSettingsService.RemoveAlpacaDeviceAsync(0)` → entry removed from `appsettings.alpaca.user.json`
- `ValidateDeviceSettings(n)` with mismatch DeviceNumber in content → error returned
- `GetAllDeviceSettings()` with no files → first-run init → returns default device

### Smoke test (manual, simulator)

1. Delete all user files for current version
2. Start application → first-run init should create `appsettings.alpaca.user.json` + `device-00.settings.json`
3. Verify device 0 loads in DeviceManager page
4. Connect device 0 → simulator should start
5. Change a setting in MountSettings → save → restart → verify persisted
6. Add a second device via DeviceManager → verify `device-01.settings.json` created + entry in `appsettings.alpaca.user.json`
7. Remove device 1 → verify file deleted + entry removed

---

## 15. Out of Scope

| Item | Disposition |
|------|-------------|
| Backward compat with old single-file format | Explicitly excluded (D3) |
| Version-to-version settings migration | Excluded — no migration service |
| Settings encryption | Not required (no secrets in device settings) |
| Remote settings management (API) | Future phase — not this redesign |
| Settings for non-Telescope device types | Future phase — `DeviceType` is always `"Telescope"` currently |
| Per-device monitor/logging settings | Monitor settings remain server-wide in `appsettings.user.json` |

---

## 16. Resolved Questions & Open Questions

| # | Question | Resolution (Andy, 2026-04-03) |
|---|----------|---|
| Q1 | Should `Enabled = false` devices be written to disk at all, or only registered/enabled ones? | ✅ **Write all.** `Enabled` flag controls only whether `MountInstanceRegistry.CreateInstance()` is called. |
| Q2 | If `device-nn.settings.json` exists but has no corresponding entry in `appsettings.alpaca.user.json` — is this a warning or auto-repair? | ✅ **Warning only for initial implementation.** Add a code comment marking the location for future auto-repair logic. Do not auto-add to the Alpaca array. |
| Q3 | Should `DeleteDeviceSettingsAsync` hard-delete or soft-delete (rename to `.bak`)? | ✅ **Hard-delete for initial implementation.** Add a code comment at the delete call site noting the option to rename to `.bak` for future soft-delete support. |
| Q4 | `appsettings.json` (server defaults) — should the `Devices` array remain or be replaced with a per-device template file? | ✅ **Resolved as new requirements.** This question revealed a broader need for alignment-mode-aware device creation and mode-change behaviour. See **Section 17** for the full requirements. Implementation design deferred pending Andy's property list. |
| Q5 | Should `observatory.settings.json` be **version-specific** (inside `{version}\`) or **version-agnostic** (in the parent `GreenSwampAlpaca\` directory)? | ✅ **Version-specific** (inside `{version}\`, same directory as all other user files). Rationale: developers must be able to reproduce a user's exact settings environment for a specific version by manually copying all files in the versioned directory. See **D5**. |

---

## 17. Alignment Mode Settings Requirements

*Added 2026-04-03 in response to Q4. Design is deferred — Andy to provide property list and per-mode default values before design phase begins.*

---

### 17.1 Background

`AlignmentMode` (`GermanPolar`, `Polar`, `AltAz`) is a fundamental property of each telescope device. The appropriate default values for a significant subset of `SkySettings` properties differ between alignment modes. This creates two distinct operational requirements: creating a new device file for a given mode, and changing the mode on an existing device without losing user-configured values.

Additionally, the physical observatory properties (latitude, longitude, elevation, UTC offset) are stable values that apply to all devices at a given installation. They require their own user-editable file (`observatory.settings.json`) so that the user sets them once and they flow into each new device file automatically — rather than being re-entered device by device.

---

### 17.2 Property Classification

All `SkySettings` properties fall into exactly one of two categories:

#### Common properties

Properties whose values are shared across all alignment modes and must be **preserved** when the alignment mode changes. This category includes two sub-groups:

**Observatory properties** — user-configurable values that describe the physical installation:
- Latitude
- Longitude
- Elevation
- UTC offset

Observatory properties are **not stored exclusively in device files**. They have their own dedicated file, `observatory.settings.json` (see Section 17.4). When a new device file is created, the current values from `observatory.settings.json` are copied into it as a snapshot. After that point, the device file's copy is independent — changes to `observatory.settings.json` do NOT automatically propagate to existing device files in the initial implementation (see Section 17.4 for detail).

During an alignment mode change, the observatory property values already in the device file MUST be preserved unchanged.

**Capability configuration properties** — typically named `Can...` (e.g., `CanPark`, `CanTrackingRates`, `CanPulseGuide`):
- Values are app-defined for each supported mount/hardware combination
- Do not vary with alignment mode
- Must be preserved during a mode change

#### Unique properties

Properties whose appropriate values differ between alignment modes (e.g., axis limits, park positions, slew speed limits, flip angle, hour angle limit, tracking rates):
- The same property names exist in `SkySettings` for all modes
- Default values are mode-specific
- On new device creation: populated from app defaults for the specified mode
- On mode change: **replaced** with app defaults for the new mode (the old mode's values are discarded)

**Pending:** Andy to provide the definitive list of common and unique properties, together with the specific default values for each unique property per mode (GermanPolar, Polar, AltAz).

---

### 17.3 Required Behaviours

#### Behaviour B1: Create new device file for a specified alignment mode

When a new device (`device-nn.settings.json`) is created and an `AlignmentMode` is specified:

1. Unique properties MUST be populated from app defaults for the specified `AlignmentMode`
2. Common capability (`Can...`) properties MUST be populated from app defaults
3. Observatory properties MUST be copied from the **current `observatory.settings.json`** (which contains either app defaults or user-defined values depending on whether the user has completed setup)
4. `DeviceNumber` MUST be set to `nn`; `AlignmentMode` MUST be set to the specified mode
5. The resulting file MUST be a valid `device-nn.settings.json` as specified in Section 4.2

#### Behaviour B2: Change alignment mode on an existing device

When the `AlignmentMode` of an existing device is changed (e.g., from `GermanPolar` to `AltAz`):

1. The existing `device-nn.settings.json` MUST be read in full before any write
2. Unique properties for the **new** mode MUST be sourced from app defaults for the new mode and written to the file
3. All common properties (capability properties AND observatory properties including user-defined values) MUST be preserved exactly as they are in the existing file — not replaced by defaults
4. `AlignmentMode` in the file MUST be updated to the new mode
5. All other `SkySettings` fields not in either category MUST be preserved unchanged
6. The operation MUST be atomic (consistent with Section 11 write strategy)

#### Behaviour B3: App defaults are mode-specific and independently specifiable

1. The app MUST define a distinct set of default values for unique properties for each of: `GermanPolar`, `Polar`, `AltAz`
2. These defaults MUST be specifiable without modifying compiled code (source TBD — pending design phase)
3. Defaults for one mode MUST NOT be derived from or share storage with defaults for another mode

#### Behaviour B4: Create and manage `observatory.settings.json`

1. On first run, if `observatory.settings.json` does not exist, it MUST be created and populated with app-level defaults for observatory properties
2. The file MUST be human-readable and directly editable by the user (JSON, indented format)
3. The file is the **single authoritative source** for observatory properties at device-creation time
4. Changes to `observatory.settings.json` MUST NOT automatically update existing `device-nn.settings.json` files — the device file's snapshot is independent after creation
5. A code comment MUST be placed at the relevant write location identifying where future "push observatory settings to all registered devices" functionality would be added
6. The app MUST provide a UI path for the user to edit observatory settings and save them back to `observatory.settings.json` (UI implementation deferred to UI phase)

---

### 17.4 `observatory.settings.json` — File Specification

#### Purpose

A dedicated, user-editable JSON file that holds the physical properties of the telescope installation. It is the **single source of truth** for observatory properties at the time a new device file is created.

#### File location

`%AppData%\GreenSwampAlpaca\{version}\observatory.settings.json`

> **Open question Q5** (see Section 16): Should this file be version-specific (versioned directory, as above) or version-agnostic (parent `GreenSwampAlpaca\` directory)? Observatory properties do not change between app versions; a version-agnostic location would avoid re-entry on upgrade, but conflicts with D3 (no migration). Decision deferred to Andy.

#### File format

```json
{
  "Latitude":  51.21135,
  "Longitude": -1.459816,
  "Elevation": 10.0,
  "UtcOffset": 0.0
}
```

*(Property names and any additional fields TBD — pending Andy's CSV property definitions.)*

#### Lifecycle

| Event | Action |
|-------|--------|
| First run — file does not exist | Create `observatory.settings.json` from app-level defaults |
| User completes observatory setup in UI | Save updated values to `observatory.settings.json` |
| New device created | Copy current values from `observatory.settings.json` into new `device-nn.settings.json` |
| User updates `observatory.settings.json` | Existing `device-nn.settings.json` files are **not updated** (v1 — code comment added at save location) |
| Alignment mode changed on existing device | Observatory values already in `device-nn.settings.json` are preserved from that file — `observatory.settings.json` is not consulted |

#### Code comment requirement

At the point in the save implementation where `observatory.settings.json` is written, a comment MUST be placed:

```
// TODO: Future feature — optionally push updated observatory settings to all registered
// device-nn.settings.json files. This would iterate GetAllDeviceSettings(), update
// observatory properties in each, and call SaveDeviceSettingsAsync(). Deferred to v2;
// requires explicit user confirmation in UI to avoid accidental overwrites.
```

---

### 17.5 Scope Boundaries

| Item | In scope | Notes |
|------|:---:|------|
| Creating a device file with a specified mode | ✅ | Behaviour B1 |
| Changing mode on an existing device | ✅ | Behaviour B2 |
| Per-mode default values for unique properties | ✅ | Source TBD — pending CSV from Andy |
| `observatory.settings.json` create/populate/save | ✅ | Behaviour B4 |
| UI for editing observatory settings | ✅ | Required; deferred to UI phase |
| UI flow for mode selection at device creation | ✅ | Required; deferred to UI phase |
| UI flow for mode change on existing device | ✅ | Required; deferred to UI phase |
| Push observatory settings changes to existing device files | ❌ | v1: code comment only; future feature |
| Migration of old per-mode data from previous format | ❌ | No compat (D3) |

---

### 17.6 Impact on Preceding Sections

| Section | Impact |
|---------|--------|
| **Section 3.1** (file layout) | `observatory.settings.json` added to the runtime user files tree. |
| **Section 4.2** (`device-nn.settings.json` format) | Format (flat `SkySettings` object) is unchanged. Observatory properties within the file are now explicitly a snapshot from `observatory.settings.json` at creation time — a creation-time concern, not a format concern. |
| **Section 4 — new 4.4** | A new sub-section is required to specify the `observatory.settings.json` format (see Section 17.4 above). This will be written once Andy's CSV property definitions are received. |
| **Section 5.1** (`IVersionedSettingsService` interface) | Additional methods required: read/save `observatory.settings.json`; create device for mode (B1); change alignment mode (B2). All interface updates deferred until Andy provides property list. |
| **Section 6** (`VersionedSettingsService` implementation) | New implementation logic for B1, B2, and B4. Deferred pending property list. |
| **Section 8.2** (`MountSettings.razor` + `DeviceManager.razor`) | UI must allow observatory settings editing (B4), mode selection at device creation (B1), and mode change on existing device (B2). Deferred to UI phases. |
| **Section 9** (startup flow) | `observatory.settings.json` first-run check added to startup sequence alongside existing first-run logic. |
| **Section 13** (implementation phases) | New phases for observatory settings file management and alignment mode handling will be inserted. Phases S1–S8 can proceed without them. |

---

### 17.7 Blocking Dependency

**Implementation of Section 17 behaviours is blocked on Andy providing:**

1. The definitive list of **common properties** (capability `Can...` properties + observatory property names)
2. The definitive list of **unique properties** (mode-sensitive property names)
3. The specific **default values** for each unique property for each of: `GermanPolar`, `Polar`, `AltAz`
4. The **observatory property definitions** — field names, types, and app-level default values

**Andy will provide items 1–4 as CSV files.**

At design time, the agent will use the CSV files to:
- Formally classify each `SkySettings` property as common or unique
- Specify the per-mode default values for unique properties (which become template JSON content embedded in the app)
- Specify the `observatory.settings.json` field set and app defaults

✅ **Confirmed:** CSV files can be used directly as design-time input. The agent will read each CSV to produce the property classification table, the per-mode default value specifications, and the `observatory.settings.json` format definition (Section 4.4). No additional tooling is required.

No design or code changes for Section 17 will be made until the CSV files are received.

---

## 18. Alignment Mode & Observatory Settings — High-Level Design

*Added 2026-04-03. Pending final property values from Andy's CSV files. Architecture and approach decisions are captured here; specific property names and values in `SkySettings.cs` and `appsettings.json` will be populated from the CSV.*

---

### 18.1 Property Classification via C# Attributes

Two lightweight attributes, added to the `GreenSwamp.Alpaca.Settings` project, decorate each `SkySettings` property to drive both the mode-change merge and new-device creation algorithms. No separate list file or configuration is required — the attributes are the authoritative classification.

```
[CommonSetting]   — property is preserved during an alignment mode change
[UniqueSetting]   — property is replaced from the new mode's template during a mode change
```

Example expected usage (property names illustrative — definitive list pending CSV):

| Property | Category | Attribute | Rationale |
|---|---|---|---|
| `Latitude` | Observatory (Common) | `[CommonSetting]` | User-defined; must survive mode change |
| `CanPark` | Capability (Common) | `[CommonSetting]` | Hardware capability; independent of mode |
| `HourAngleLimit` | Unique | `[UniqueSetting]` | Mechanically meaningless outside GermanPolar |
| `AxisLimitX` | Unique | `[UniqueSetting]` | Physical axis range differs per mount mode |

All `SkySettings` properties that fall into neither category (e.g., `DeviceNumber`, `DeviceName`, `Enabled`, `AlignmentMode`) are handled explicitly by the algorithms rather than by attribute.

---

### 18.2 New Model: `ObservatorySettings`

A new POCO class in `GreenSwamp.Alpaca.Settings\Models\ObservatorySettings.cs` representing the content of `observatory.settings.json`. Property names and any additional fields are TBD pending Andy's CSV.

```
ObservatorySettings
  Latitude   : double
  Longitude  : double
  Elevation  : double
  UtcOffset  : double
  [additional fields TBD from CSV]
```

---

### 18.3 App-Deployed JSON: New Sections in `appsettings.json`

No new files are introduced for templates. The existing `GreenSwamp.Alpaca.Server\appsettings.json` gains two new top-level sections. `VersionedSettingsService` reads them via the already-injected `IConfiguration` — the same mechanism used for the existing `Devices` and `AlpacaDevices` first-run arrays.

#### `"DeviceTemplates"` section

One complete `SkySettings`-equivalent object per alignment mode. Each object contains app-level defaults for **all** `SkySettings` fields for that mode. Unique-property values differ between modes; common-property values are the same across all three (they serve as the fallback for common properties when creating a device before the user has set observatory values).

The property values are populated from Andy's CSV files at design time.

```json
"DeviceTemplates": {
  "GermanPolar": {
    "AlignmentMode": "GermanPolar",
    "Latitude":       51.21135,     // [CommonSetting] observatory — overridden by observatory.settings.json
    "CanPark":        true,         // [CommonSetting] capability  — app default
    "HourAngleLimit": 92.0,         // [UniqueSetting]             — GermanPolar value
    "AxisLimitX":     185.0,        // [UniqueSetting]             — GermanPolar value
    "... all remaining SkySettings fields with GermanPolar defaults ..."
  },
  "Polar": {
    "AlignmentMode":  "Polar",
    "Latitude":       51.21135,     // [CommonSetting] same across all modes
    "CanPark":        true,
    "HourAngleLimit": 0.0,          // [UniqueSetting] — not applicable to Polar
    "AxisLimitX":     200.0,        // [UniqueSetting] — Polar value
    "..."
  },
  "AltAz": {
    "AlignmentMode":  "AltAz",
    "..."
  }
}
```

#### `"ObservatoryDefaults"` section

Source for `observatory.settings.json` on first run. Property names and values TBD from CSV.

```json
"ObservatoryDefaults": {
  "Latitude":  51.21135,
  "Longitude": -1.459816,
  "Elevation": 10.0,
  "UtcOffset": 0.0
}
```

---

### 18.4 Interface Additions (supplement to Section 5.1)

Four new methods added to `IVersionedSettingsService`. These are added after phases S1–S8 (see Section 18.7).

```
// ── Observatory settings (observatory.settings.json) ─────────────────────
ObservatorySettings GetObservatorySettings();
Task SaveObservatorySettingsAsync(ObservatorySettings settings);

// ── Mode-aware device creation (Behaviour B1) ─────────────────────────────
Task CreateDeviceForModeAsync(int deviceNumber, string deviceName, AlignmentMode mode);

// ── Mode change on existing device (Behaviour B2) ─────────────────────────
Task ChangeAlignmentModeAsync(int deviceNumber, AlignmentMode newMode);
```

---

### 18.5 Key Algorithms

#### B1 — Create new device for a specified alignment mode

```
CreateDeviceForModeAsync(deviceNumber, deviceName, mode):
  1. template    ← bind IConfiguration["DeviceTemplates:{mode}"] → SkySettings
  2. observatory ← GetObservatorySettings()
  3. For each property P on ObservatorySettings:
       set matching property P on template from observatory
       (explicit property copy — no reflection surprises)
  4. template.DeviceNumber  = deviceNumber
     template.DeviceName    = deviceName
     template.AlignmentMode = mode
     template.Enabled       = true
  5. await SaveDeviceSettingsAsync(deviceNumber, template)
```

#### B2 — Change alignment mode on an existing device

```
ChangeAlignmentModeAsync(deviceNumber, newMode):
  1. current  ← GetDeviceSettings(deviceNumber)
  2. template ← bind IConfiguration["DeviceTemplates:{newMode}"] → SkySettings
  3. merged   ← new SkySettings()
  4. For each property P on typeof(SkySettings).GetProperties():
       if P has [UniqueSetting]:  merged.P = template.P   // new mode default
       else:                      merged.P = current.P    // preserve existing value
  5. merged.AlignmentMode = newMode
  6. await SaveDeviceSettingsAsync(deviceNumber, merged)  // atomic write
```

#### B4 — Observatory settings first-run and save

```
GetObservatorySettings():
  1. If observatory.settings.json exists: deserialize and return
  2. Else: defaults ← bind IConfiguration["ObservatoryDefaults"] → ObservatorySettings
           await SaveObservatorySettingsAsync(defaults)   // first-run write
           return defaults

SaveObservatorySettingsAsync(settings):
  1. Acquire _observatoryFileLock
  2. Serialize settings → indented JSON
  3. Atomic write to observatory.settings.json (temp → rename)
  4. Release lock
  5. Raise ObservatorySettingsChanged event
  // TODO: Future — optionally push updated observatory values to all registered
  // device-nn.settings.json files (see Section 17.4 code comment requirement).
```

---

### 18.6 New and Changed Files Summary

#### New files — `GreenSwamp.Alpaca.Settings` project

| File | Purpose |
|------|---------|
| `Attributes\CommonSettingAttribute.cs` | Marks common `SkySettings` properties |
| `Attributes\UniqueSettingAttribute.cs` | Marks unique `SkySettings` properties |
| `Models\ObservatorySettings.cs` | POCO for `observatory.settings.json` |

#### Changed files — `GreenSwamp.Alpaca.Settings` project

| File | Change |
|------|--------|
| `Models\SkySettings.cs` | Add `[CommonSetting]` or `[UniqueSetting]` to each property — **pending CSV** |
| `Services\IVersionedSettingsService.cs` | Add 4 new methods (Section 18.4) |
| `Services\VersionedSettingsService.cs` | Implement B1, B2, B4; add `_observatoryFileLock` (`SemaphoreSlim`) |

#### Changed files — `GreenSwamp.Alpaca.Server` project

| File | Change |
|------|--------|
| `appsettings.json` | Add `"DeviceTemplates"` and `"ObservatoryDefaults"` sections — **pending CSV values** |

No changes to `GreenSwamp.Alpaca.MountControl`, `ASCOM.Alpaca.Razor`, or test projects for Section 17/18 work.

---

### 18.7 Implementation Phases (supplement to Section 13)

These phases are inserted before S8 (build verification gate). Phases SA and SC are unblocked and can start after S2; SB, SD, and SF are blocked on Andy's CSV.

| Phase | Description | Files changed | Risk | Blocked on |
|-------|-------------|---|:---:|---|
| **SA** | New attribute files + `ObservatorySettings` model | 3 new files | 🟢 | Nothing — start after S2 |
| **SB** | Annotate `SkySettings.cs` with `[CommonSetting]`/`[UniqueSetting]` | `SkySettings.cs` | 🟡 | Andy's CSV |
| **SC** | Add 4 new methods to `IVersionedSettingsService` (interface only) | `IVersionedSettingsService.cs` | 🟢 | SA |
| **SD** | Implement B1, B2, B4 in `VersionedSettingsService` | `VersionedSettingsService.cs` | 🔴 High | SB + SC |
| **SF** | Populate `"DeviceTemplates"` + `"ObservatoryDefaults"` in `appsettings.json` | `appsettings.json` | 🟡 | Andy's CSV |
| **S8** | **Build verification gate** | — | gate | SA–SF complete |

**Revised full phase order:** S1 → S2 → S3 → S4 → S5 → S6 → S7 → SA → SB → SC → SD → SF → S8 → S9 → S10 → S11

---

*Document created: 2026-04-03 11:49 | Last updated: 2026-04-03 12:39*
*Author: GitHub Copilot (GreenSwamp Alpaca workspace)*  
*Next action: Andy reviews Section 18 design → provides CSV property definitions → SkySettings.cs annotated + appsettings.json templates populated → approve S1 implementation*
