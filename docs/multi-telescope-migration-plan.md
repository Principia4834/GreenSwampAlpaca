# Multi-Telescope Migration Plan
# Eliminating Static SkyServer -- Device-Neutral Architecture

**Document updated:** 2026-04-08 10:31
**Baseline build status:** [x] SUCCESS -- 0 errors, 0 warnings
**Author:** GitHub Copilot (analysis) / Andy (owner)

---

## 1. Executive Summary

The goal is to allow an arbitrary number of telescopes to be connected and controlled simultaneously
via one Alpaca server, with no device having privileged or special-cased status. The ASCOM REST
layer, the settings system, the Blazor UI, and the device registry already support this. The
blocker is the static `SkyServer` class, which hard-wires everything through
`_defaultInstance = MountInstanceRegistry.GetInstance(0)` -- slot 0 is always "the" telescope.

This plan removes that hard-wiring in eight incremental phases. Each phase leaves the build green
before the next begins. No Telescope.cs or TelescopeController.cs changes are needed -- those are
already fully instance-based.

---

## 2. Current Architecture (What We Have)

### 2.1 What is Already Multi-Device Ready

| Component | Status | Notes |
|-----------|--------|-------|
| `TelescopeController.cs` | [x] Done | Routes `DeviceNumber` -> `DeviceManager.GetTelescope(DeviceNumber)` |
| `Telescope.cs` | [x] Done | `GetInstance()` + `inst.Property` for every ASCOM call. **Zero remaining `SkyServer.xxx` calls.** |
| `MountInstance.cs` | [x] Done | Holds all per-device state (3092 lines) |
| `SkySettingsInstance.cs` | [x] Done | Fully instance-owned, per-device JSON persistence |
| `MountInstanceRegistry` | [x] Done | Thread-safe `Dictionary<int, MountInstance>` |
| `UnifiedDeviceRegistry` | (!) Partial | Device registration works, but has wrongly-enforced "reserved slots" (see Phase 7) |
| Blazor UI (TelescopeState, DeviceManager) | [x] Done | Uses `MountInstanceRegistry.GetAllInstances()` already |
| `SimTasks(taskName, instance)` | [x] Done | Takes explicit `MountInstance` -- fully instance-aware |
| `SkyTasks(taskName, instance)` | [x] Done | Takes explicit `MountInstance` -- fully instance-aware |
| `AxesStopValidate(instance)` | [x] Done | Takes explicit `MountInstance` |
| `GoToAsync(target, state, handle, tracking, instance)` | [x] Done | Has per-instance `effectiveInstance` |
| `PulseGuide(direction, duration, altRate, instance)` | [x] Done | Has per-instance support |
| `SkyServer.StaticPropertyChanged` | [x] No consumers | No Blazor page in the Server project subscribes to it |

### 2.2 The Root Cause

Two computed properties in `SkyServer.Core.cs` lines 69--71 create all device-0 bias:

```csharp
// Line 69  <- THE ROOT CAUSE
private static MountInstance? _defaultInstance => MountInstanceRegistry.GetInstance(0);
// Line 71
private static SkySettingsInstance? _settings => _defaultInstance?.Settings;
```

These are referenced **261 times** across the three SkyServer partial files
(`SkyServer.cs` 281 lines, `SkyServer.Core.cs` 1611 lines, `SkyServer.TelescopeAPI.cs` 3312 lines).
Every property, method, and event that uses `_defaultInstance` or `_settings` is device-0 only.

### 2.3 Inventory of Remaining Static Bias

#### SkyServer.Core.cs (1611 lines)
- `_defaultInstance` / `_settings` root computed properties
- All CTS properties (ctsGoTo, ctsPulseGuideRa, etc.) -- delegating wrappers to `_defaultInstance`
- `HomeAxes`, `AltAzSync` -- delegating wrappers
- `SkyHcRate`, `SkyTrackingRate` -- delegating wrappers
- `_altAzTrackingTimer` -- delegating wrapper
- `SkyTrackingOffset` -- delegating to `_defaultInstance._skyTrackingOffset`
- `SkyGetRate(MountInstance? instance = null)` -- reads `_settings!.AlignmentMode` (device-0)
- `CalcCustomTrackingOffset(MountInstance? instance = null)` -- has instance param but falls back
- `SetSlewRates(double maxRate, MountInstance? instance = null)` -- has instance param but falls back
- `CheckAxisLimits()` -- no instance param, uses `_defaultInstance`
- `GetDefaultPositions_Internal()` -- uses `_settings`, `GetHomeAxes()` (device-0)
- `PropertyChangedSkySettings()` -- wired only to device-0 settings; calls `_defaultInstance.SkyPredictor.Reset()`
- `GetLocalSiderealTime()` (zero-arg overload) -- reads `_settings!.Longitude` (device-0)
- `GetLocalSiderealTime(DateTime)` (one-arg overload) -- reads `_settings!.Longitude` (device-0)
- `LowVoltageEventSet()` -- sets `_defaultInstance._lowVoltageEventState`
- `SkyErrorHandler(Exception)` -- sets `_defaultInstance._mountError`
- `UpdateServerEvent()` -- delegates to `_defaultInstance`
- `WaitMountPositionUpdated()` -- uses `_defaultInstance._mountPositionUpdatedEvent`
- `SetRateMoveSlewState()` -- uses `_defaultInstance` directly
- `ActionRateRaDec()` -- uses `_defaultInstance` and `_settings!` directly
- `UpdateMountLimitStatus(double[])` -- writes to `static LimitStatus` field (not per-device)
- `Initialize()` -- wires settings listener only for device-0
- `ShutdownServer()` -- only stops device-0
- `MountStart()` / `MountStop()` -- delegates to `_defaultInstance`

#### SkyServer.TelescopeAPI.cs (3312 lines)
- ~40 static ASCOM position/state/target/rate properties -- all delegate to `_defaultInstance`
- `Tracking` setter -- reads/writes `_defaultInstance` directly
- `AbortSlew(bool speak, ...)` -- uses `_defaultInstance`, `_settings!.Mount`
- `StopAxes()` -- uses `_defaultInstance`, `_settings!.Mount`
- `AutoHomeAsync(...)` -- uses `_defaultInstance`, `_settings!`
- `GoToPark()` / `GoToParkAsync()` -- use `_settings!.ParkAxes`, `_defaultInstance`
- `GoToHome()` / `GoToHomeX()` -- use `_defaultInstance`
- `SyncToTargetRaDec()` -- uses `_defaultInstance`, `_settings!`
- `SyncToAltAzm(...)` -- uses `_defaultInstance`, `_settings!`
- `CompletePark()` -- uses `_defaultInstance`
- `SetTracking(MountInstance? instance = null)` -- falls back to `_defaultInstance`
- `SetTrackingMode()` -- uses `_defaultInstance`, `_settings!`
- `SetTrackingDirect(...)` -- uses `_defaultInstance`
- `CurrentTrackingRate()` (zero-arg) -- reads `_settings!.TrackingRate`, `_defaultInstance._pecBinNow`
- `SetGuideRates(MountInstance? instance = null)` -- falls back to `_defaultInstance`
- `GetAlternatePositionGEM/AltAz/Polar()` -- reads `_defaultInstance._actualAxisX`
- `DetermineSideOfPier()` -- calls `SideOfPier` (device-0)
- `CheckRaDecSyncLimit()` / `CheckAltAzSyncLimit()` -- reads `_settings!`, `_defaultInstance._appAxes`
- `IsWithinFlipLimits()` / `IsTargetWithinLimits()` -- read `_settings!`
- `CheckAxisLimits()` -- reads `_defaultInstance`
- `GetRaRateDirection()` / `GetDecRateDirection()` -- appear device-independent (check)

#### SkyServer.cs (281 lines)
- `public static LimitStatusType LimitStatus = new LimitStatusType()` -- static mutable field
- `IsMountRunning` -- delegates to `_defaultInstance`
- `HcResetPrevMove()` -- uses static `_hcPrevMoveDec` / `_hcPrevMoveRa` (which delegate to `_defaultInstance`)
- `ReSyncAxes(...)` -- uses `_defaultInstance`, `_settings`
- `GetStoredParkPosition()` -- uses `_settings`
- `Mount` property -- reads `_settings`

#### External callers of static SkyServer (outside SkyServer files)
| File | Call | Status |
|------|------|--------|
| `MountInstance.cs` | `SkyServer.SetTracking(this)` | Needs to become `this.SetTracking()` |
| `MountInstance.cs` | `SkyServer.SimTasks(taskName, this)` | Keep or move |
| `MountInstance.cs` | `SkyServer.SkyTasks(taskName, this)` | Keep or move |
| `MountInstance.cs` | `SkyServer.AxesStopValidate(this)` | Keep or move |
| `MountInstance.Pec.cs` | `SkyServer.SkyTasks(taskName, this)` | Keep or move |
| `MountInstance.Pec.cs` | `SkyServer.SetTracking(this)` | Needs to become `this.SetTracking()` |
| `SkyPredictor.cs` | `SkyServer.CurrentTrackingRate` (default delegate) | Must use `CurrentTrackingRate(inst)` overload |
| `SkySettingsInstance.cs` | `SkyServer.SetSlewRates(value, _owner)` | Move to `_owner.SetSlewRates(value)` |
| `SkySettingsInstance.cs` | `SkyServer.SkyTasks(taskName, _owner)` | Move to `_owner.SkyTasks(taskName)` or keep |
| `SlewController.cs` | `SkyServer.AxesStopValidate(instance)` | Keep or move |
| `SlewController.cs` | `SkyServer.StopAxes()` (fallback) | Must use instance |
| `CommandStrings.cs` | `SkyServer.SimTasks(taskName, instance)` | Keep or move |
| `CommandStrings.cs` | `SkyServer.SkyTasks(taskName, instance)` | Keep or move |
| `Program.cs` | `SkyServer.Initialize()` | Replace with per-device init |

---

## 3. Target Architecture (End State)

### 3.1 What SkyServer Becomes

After the migration, `SkyServer` is reduced to a **pure static utility class** with no device state:

```csharp
public static partial class SkyServer
{
    // Pure math helpers
    internal static double DegToRad(double degree);
    internal static double RadToDeg(double rad);

    // Pure validation (no device state)
    private static bool CheckSkyErrors(ISkyCommand command);

    // Alignment passthrough (currently no-ops, kept for future)
    public static double[] GetSyncedAxes(double[] unsynced);
    private static double[] GetUnsyncedAxes(double[] synced);

    // Instance-aware helpers that stay static (take MountInstance explicitly)
    public static void SimTasks(MountTaskName taskName, MountInstance instance);
    public static void SkyTasks(MountTaskName taskName, MountInstance instance);
    internal static bool AxesStopValidate(MountInstance instance);
    public static void SkyErrorHandler(Exception ex, MountInstance instance);  // + instance param

    // Pure string helpers
    private static string GetAutoHomeResultMessage(AutoHomeResult result, string axisName);
    private static string ChooseClosestPosition(...);
    private static string ChooseClosestPositionPolar(...);

    // Type definitions and constants
    public struct LimitStatusType { ... }   // OR move to MountInstance
    private const double SiderealRate = 15.0410671786691;
    public static event PropertyChangedEventHandler StaticPropertyChanged;  // deprecated, remove last
}
```

### 3.2 What MountInstance Gains

All per-device logic currently in SkyServer moves to `MountInstance`:

```csharp
public partial class MountInstance : IMountController
{
    // ASCOM API operations (currently in SkyServer.TelescopeAPI)
    public void AbortSlew(bool speak);
    public void StopAxes();
    public async void AutoHomeAsync(int degreeLimit = 100, int offSetDec = 0);
    public void SyncToTargetRaDec();
    public void SyncToAltAzm(double targetAzimuth, double targetAltitude);
    public void GoToPark();
    public async Task<SlewResult> GoToParkAsync();
    public void GoToHomeX();
    public async Task<SlewResult> GoToHome();
    public void ReSyncAxes(ParkPosition parkPosition = null, bool saveParkPosition = true);
    public void CompletePark();

    // Tracking management
    internal void SetTracking(MountInstance instance = this);  // no-op when refactored
    public void SetTrackingMode();
    internal void SetTrackingDirect(bool tracking, TrackingMode mode);
    public double CurrentTrackingRate();
    internal void SetGuideRates();
    internal void CalcCustomTrackingOffset();

    // Slew rates
    internal void SetSlewRates(double maxRate);

    // Position & sync helpers
    public ParkPosition GetStoredParkPosition();
    private void SetParkAxis(string name);
    private void SetParkAxis(string name, double x, double y);
    public void UpdateMountLimitStatus(double[] rawPositions);
    public double GetLocalSiderealTime();
    public double GetLocalSiderealTime(DateTime utcNow);

    // Position calculations (GEM / AltAz / Polar flip logic)
    public double[] GetAlternatePosition(double[] position);
    private double[] GetAlternatePositionGEM(double[] position);
    private double[] GetAlternatePositionAltAz(double[] position);
    private double[] GetAlternatePositionPolar(double[] position);
    public PointingState DetermineSideOfPier(double rightAscension, double declination);
    public bool CheckRaDecSyncLimit(double ra, double dec);
    public bool CheckAltAzSyncLimit(double alt, double az);

    // Limit status (per-device)
    public LimitStatusType LimitStatus => _limitStatus;

    // Settings initialization (per-device)
    public void InitializeSettings();  // replaces SkyServer.Initialize()
    public double[] GetDefaultPositions();  // replaces GetDefaultPositions_Internal()
    public void ShutdownDevice();  // replaces ShutdownServer()
}
```

### 3.3 No More device0.xxx Calls

Every caller currently doing:
```csharp
SkyServer.Tracking = false;
SkyServer.AbortSlew(true);
SkyServer.PulseGuide(direction, duration, altRate);
```

Will instead do:
```csharp
var inst = MountInstanceRegistry.GetInstance(deviceNumber);
inst.SetTracking(false);
inst.AbortSlew(true);
inst.PulseGuide(direction, duration, altRate);
```

The `Telescope` driver already does exactly this -- no change needed there.

---

## 4. Migration Phases

### Phase M1 -- Convert `SetTracking`, `SetGuideRates`, `CalcCustomTrackingOffset`, `SkyGetRate` to instance methods

**Target files:** `SkyServer.TelescopeAPI.cs`, `SkyServer.Core.cs`, `MountInstance.cs`

**What changes:**
1. `SetTracking(MountInstance? instance = null)` -> `MountInstance.SetTracking()` (instance method).
   The existing `inst.SetTracking(bool)` method already exists; this merges the hardware-sending
   logic currently in `SkyServer.SetTracking(MountInstance?)` into it.
2. `SetGuideRates(MountInstance? instance = null)` -> `MountInstance.SetGuideRates()`.
3. `CalcCustomTrackingOffset(MountInstance? instance = null)` -> `MountInstance.CalcCustomTrackingOffset()`.
4. `SkyGetRate(MountInstance? instance = null)` -> `MountInstance.SkyGetRate()`.
   Fix the bug: currently reads `_settings!.AlignmentMode` (device-0); must read `this.Settings.AlignmentMode`.
5. `SetSlewRates(double maxRate, MountInstance? instance = null)` -> already has instance param,
   change to `MountInstance.SetSlewRates(double maxRate)` and update `SkySettingsInstance._owner` callback.
6. `CurrentTrackingRate()` (zero-arg static) -> remove; all callers must use the existing
   `CurrentTrackingRate(MountInstance inst)` overload. Update `SkyPredictor` default delegate.
7. `SetTrackingMode()` -> `MountInstance.SetTrackingMode()`.
8. `SetTrackingDirect(bool, TrackingMode)` -> `MountInstance.SetTrackingDirect(bool, TrackingMode)`.
9. `ActionRateRaDec()` -> `MountInstance.ActionRateRaDec()` (already mirrors exist in MountInstance).
10. `SetRateMoveSlewState()` -> `MountInstance.SetRateMoveSlewState()` (already mirrors exist).

**Call site fixes:**
- `MountInstance.cs` has `SkyServer.SetTracking(this)` -> `this.SetTracking()` (7 occurrences).
- `MountInstance.Pec.cs` has `SkyServer.SetTracking(this)` -> `this.SetTracking()` (3 occurrences).
- `SkySettingsInstance.cs` `SkyServer.SetSlewRates(value, _owner)` -> `_owner?.SetSlewRates(value)`.

**Build checkpoint:** [x] Green after each file.

---

### Phase M2 -- Move Core Operations to MountInstance

**Target files:** `SkyServer.TelescopeAPI.cs` -> `MountInstance.cs`

**What moves:**

| Static method (SkyServer) | New instance method (MountInstance) |
|---------------------------|--------------------------------------|
| `AbortSlew(bool speak, EventWaitHandle?)` | `AbortSlew(bool speak, EventWaitHandle? abortSlewStarted = null)` |
| `AbortSlewAsync(bool speak)` | `AbortSlewAsync(bool speak)` |
| `StopAxes()` | `StopAxes()` |
| `AutoHomeAsync(int degreeLimit, int offSetDec)` | `AutoHomeAsync(int degreeLimit = 100, int offSetDec = 0)` |
| `SyncToTargetRaDec()` | `SyncToTargetRaDec()` |
| `SyncToAltAzm(double, double)` | `SyncToAltAzm(double, double)` |
| `GoToPark()` | `GoToPark()` |
| `GoToParkAsync()` | `GoToParkAsync()` |
| `GoToHome()` | `GoToHome()` |
| `GoToHomeX()` | `GoToHomeX()` |
| `ReSyncAxes(ParkPosition?, bool)` | `ReSyncAxes(ParkPosition? = null, bool = true)` |
| `CompletePark()` | `CompletePark()` (already exists in MountInstance) |
| `GetStoredParkPosition()` | `GetStoredParkPosition()` |
| `SetParkAxis(string)` | `SetParkAxis(string)` |
| `SetParkAxis(string, double, double)` | `SetParkAxis(string, double, double)` |
| `DetermineSideOfPier(double, double)` | `DetermineSideOfPier(double, double)` |
| `GetAlternatePosition(double[])` | `GetAlternatePosition(double[])` |
| `CheckRaDecSyncLimit(double, double)` | `CheckRaDecSyncLimit(double, double)` |
| `CheckAltAzSyncLimit(double, double)` | `CheckAltAzSyncLimit(double, double)` |
| `GetDefaultPositions_Internal()` | `GetDefaultPositions()` |
| `WaitMountPositionUpdated()` | `WaitMountPositionUpdated()` (already exists) |
| `MapSlewTargetToAxes(double[], SlewType)` | `MapSlewTargetToAxes(double[], SlewType)` (already exists) |
| `SlewAxes(double, double, SlewType, bool, MountInstance?)` | Becomes `SlewAxes(double, double, SlewType, bool)` |

**Internal methods that also move:**
- `GoToAsync(...)` -> `MountInstance.GoToAsync(...)` (already has `effectiveInstance` variable, trivial)
- `SlewMount(...)` -> `MountInstance.SlewMount(...)`
- `IsWithinFlipLimits(double[])` -> `MountInstance.IsWithinFlipLimits(double[])`
- `IsTargetWithinLimits(double[])` -> `MountInstance.IsTargetWithinLimits(double[])`
- `CheckAxisLimits()` -> `MountInstance.CheckAxisLimits()`
- `GetRaRateDirection(double)` -> `MountInstance.GetRaRateDirection(double)` (check if pure -- if so, stays static)
- `GetDecRateDirection(double)` -> `MountInstance.GetDecRateDirection(double)` (check if pure)
- `ActionRateRaDec()` -> already migrated in Phase M1
- `GetLocalSiderealTime()` -> `MountInstance.GetLocalSiderealTime()` using `this.Settings.Longitude`
- `GetLocalSiderealTime(DateTime)` -> `MountInstance.GetLocalSiderealTime(DateTime)` using `this.Settings.Longitude`
- `GetHomeAxes(double, double)` -> `MountInstance.GetHomeAxes(double, double)` (already exists on instance)

**Call site fixes:**
- `SlewController.cs` line 473: remove `SkyServer.StopAxes()` fallback; always use `instance.StopAxes()`.
- `SlewController.cs` line 311: `SkyServer.AxesStopValidate(operation.MountInstance)` -> keep as-is (still static helper) or change to `operation.MountInstance.AxesStopValidate()`.
- Within MountInstance itself: self-calls become `this.Method()` or just `Method()`.

**Build checkpoint:** [x] Green after each method moved.

---

### Phase M3 -- Delete Static ASCOM Property Facade

**Target file:** `SkyServer.TelescopeAPI.cs`

The ~40 static properties that delegate to `_defaultInstance` are no longer called from anywhere
after Phases M1 and M2 (the `Telescope.cs` driver already uses `inst.Property` directly, and
internal callers have been migrated). These properties can be deleted:

```
RightAscension, Declination, RightAscensionXForm, DeclinationXForm,
Altitude, Azimuth, SiderealTime, Lha, IsSideOfPier, SideOfPier,
Tracking, IsSlewing, AtHome, AtPark, IsPulseGuiding, FlipOnNextGoto, SlewState,
RateDec, RateDecOrg, RateRa, RateRaOrg, TargetDec, TargetRa,
GuideRateDec, GuideRateRa, ParkSelected, MoveAxisActive,
MovePrimaryAxisActive, MoveSecondaryAxisActive, TrackingOffsetRaRate, TrackingOffsetDecRate,
Steps (delegating wrapper)
```

**Process:**
1. Use `find_symbol` -> Find All References for each property before deleting.
2. Delete only if zero external callers remain (all internal callers should already be migrated).
3. Keep `CanMoveAxis(TelescopeAxis)` as a static helper if still used (it reads `_settings.NumMoveAxis` -- fix to take `MountInstance`).

**Build checkpoint:** [x] Green after all deletions.

---

### Phase M4 -- Move Remaining Core State Operations

**Target file:** `SkyServer.Core.cs`

After Phases M1--M3, the following items remain in SkyServer.Core.cs and need migration:

1. **`LowVoltageEventSet(object, EventArgs)`** -- move to `MountInstance.OnLowVoltage()` instance event handler; wired per-device in `MountInstance.MountStart()`.

2. **`UpdateServerEvent(object, EventArgs)`** -- already delegates to `_defaultInstance.OnUpdateServerEvent(...)`. Wire per-device in `MountInstance.MountStart()`.

3. **`PropertyChangedSkySettings(object, PropertyChangedEventArgs)`** -- currently wired only to device-0 settings in `SkyServer.Initialize()`. Move the wiring into `MountInstance.InitializeSettings()` so each device subscribes its own settings changes.

4. **`SkyErrorHandler(Exception ex)`** -> `SkyServer.SkyErrorHandler(Exception ex, MountInstance instance)`. Add `MountInstance instance` parameter. Update all internal callers to pass the instance.

5. **`UpdateMountLimitStatus(double[] rawPositions)`** -> `MountInstance.UpdateMountLimitStatus(double[])`. Uses `_settings` for limit values -> `this.Settings`. Writes to `static LimitStatus` -> `_limitStatus` (already exists in MountInstance).

6. **`ShutdownServer()`** -> `MountInstance.ShutdownDevice()`. Currently only stops device-0.

7. **`Initialize()`** -> Move the single meaningful line (wiring settings listener) into a new `MountInstance.InitializeSettings()` called from `MountInstance.MountStart()` or from `Program.cs` per device. Remove `SkyServer.Initialize()`.

8. **Static constructor `static SkyServer()`** -> Remove if empty after cleanup; or keep just the log entry.

9. **`GetRawDegrees()`, `GetRawSteps()`, `UpdateSteps()`, `ConvertStepsToDegrees()`** -- all already delegate to instance. Verify no remaining callers outside SkyServer, then delete.

**Build checkpoint:** [x] Green after each change.

---

### Phase M5 -- Remove `_defaultInstance` and `_settings` Computed Properties

**Target file:** `SkyServer.Core.cs`

This is the definitive cleanup. After Phases M1--M4, `_defaultInstance` and `_settings` should have
zero remaining references in the SkyServer files. Verify:

```powershell
Select-String -Path "GreenSwamp.Alpaca.MountControl\SkyServer*.cs" -Pattern "_defaultInstance|_settings" | Measure-Object | Select-Object -ExpandProperty Count
# Target: 0
```

Then delete lines 62--71 in `SkyServer.Core.cs`:
```csharp
// DELETE these lines:
private static MediaTimer? _altAzTrackingTimer { get => ...; set => ...; }
private static MountInstance? _defaultInstance => MountInstanceRegistry.GetInstance(0);
private static SkySettingsInstance? _settings => _defaultInstance?.Settings;
```

Also delete all remaining delegating property wrappers for CTS, HC anti-backlash, HomeAxes, AltAzSync,
SkyTrackingRate, SkyHcRate, SkyTrackingOffset. These all delegated to `_defaultInstance`.

**Build checkpoint:** [x] Green.

---

### Phase M6 -- Fix Program.cs Initialization

**Target file:** `GreenSwamp.Alpaca.Server\Program.cs`

**Current (device-0 only):**
```csharp
GreenSwamp.Alpaca.MountControl.SkyServer.Initialize();
```

**Replacement:**
```csharp
// Per-device initialization -- wire settings listeners for each registered device
foreach (var kvp in MountInstanceRegistry.GetAllInstances())
{
    kvp.Value.InitializeSettings();
    Logger.LogInformation($"Device {kvp.Key}: settings initialized");
}
```

`MountInstance.InitializeSettings()` (new method from Phase M4) wires `_settings.PropertyChanged`
to a per-instance handler and performs the same logging as the old `SkyServer.Initialize()`.

**Build checkpoint:** [x] Green.

---

### Phase M7 -- Remove UnifiedDeviceRegistry Reserved Slots

**Target file:** `GreenSwamp.Alpaca.Server\Services\UnifiedDeviceRegistry.cs`

**The problem:**
```csharp
private const int SimulatorSlot = 0;    // wrong assumption
private const int PhysicalMountSlot = 1; // wrong assumption
// ...
if (IsReservedSlot(deviceNumber))
    throw new InvalidOperationException("Cannot delete reserved device slot ...");
```

This enforces that slot 0 is always a Simulator and slot 1 is always a physical mount. This is an
architectural assumption, not a capability. Any device number should be able to run any mount type,
and any device (including slot 0) should be removable and reconfigurable.

**What changes:**
1. Remove `SimulatorSlot`, `PhysicalMountSlot`, `FirstDynamicSlot` constants.
2. Remove `IsReservedSlot(int)` method.
3. Change `RemoveDevice(int)` to allow removing any device number.
4. Change `GetNextAvailableDeviceNumber()` to start from 0 (not 2).
5. Update any callers of `IsReservedSlot` in `SetupDevicesController.cs` or Blazor pages.

**Note:** If there is a UI convention about "default" or "primary" device, this should be expressed
through a settings flag (`IsPrimary: true`) not through a hard-coded slot number.

**Build checkpoint:** [x] Green.

---

### Phase M8 -- Final Cleanup: Delete or Shrink SkyServer Files

**After Phases M1--M7, conduct a final audit:**

```powershell
# Should return 0
Select-String -Path "GreenSwamp.Alpaca.MountControl\SkyServer*.cs" -Pattern "_defaultInstance|_settings" | Measure-Object | Select-Object -ExpandProperty Count

# Check what still lives in SkyServer
Get-Content "GreenSwamp.Alpaca.MountControl\SkyServer.cs" | Measure-Object -Line
Get-Content "GreenSwamp.Alpaca.MountControl\SkyServer.Core.cs" | Measure-Object -Line
Get-Content "GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs" | Measure-Object -Line
```

**What should remain (all genuinely static):**

In `SkyServer.Core.cs` (rename to `SkyServer.Helpers.cs` or merge with the others):
```csharp
public static partial class SkyServer
{
    private const double SiderealRate = 15.0410671786691;
    internal static double DegToRad(double degree);
    internal static double RadToDeg(double rad);
    private static bool CheckSkyErrors(ISkyCommand command);
    public static double[] GetSyncedAxes(double[] unsynced);   // currently passthrough
    private static double[] GetUnsyncedAxes(double[] synced);  // currently passthrough
    private static void ConnectAlignmentModel();                // currently no-op
    private static void OnStaticPropertyChanged(string propertyName);
    public static event PropertyChangedEventHandler StaticPropertyChanged;  // mark [Obsolete]
    public static void SimTasks(MountTaskName taskName, MountInstance instance);
    public static void SkyTasks(MountTaskName taskName, MountInstance instance);
    internal static bool AxesStopValidate(MountInstance instance);
    public static void SkyErrorHandler(Exception ex, MountInstance instance);
}
```

In `SkyServer.TelescopeAPI.cs` (what's left after Phase M3):
```csharp
public static partial class SkyServer
{
    private static string GetAutoHomeResultMessage(AutoHomeResult result, string axisName);
    private static string ChooseClosestPosition(...);
    private static string ChooseClosestPositionPolar(...);
    // GetRaRateDirection, GetDecRateDirection -- confirm if pure (no device state), keep static
}
```

`SkyServer.cs` -- **delete the file** if `LimitStatusType` has been moved to `MountInstance`
(the struct is already referenced there as `internal SkyServer.LimitStatusType _limitStatus`
in `MountInstance.cs`). If moved, update that type reference.

**Build checkpoint:** [x] Green. Run all tests.

---

## 5. SkyPredictor Fix (Companion Change to Phase M1)

**File:** `GreenSwamp.Alpaca.MountControl\SkyPredictor.cs` line 41

**Current (device-0 biased):**
```csharp
_trackingRateProvider = trackingRateProvider ?? SkyServer.CurrentTrackingRate;
```

`SkyServer.CurrentTrackingRate` (zero-arg) reads `_settings` (device-0). This means every device's
predictor uses device-0's tracking rate settings.

**Fix:** `SkyPredictor` must be initialized with the `MountInstance` it belongs to and call the
per-instance overload `SkyServer.CurrentTrackingRate(inst)`:
```csharp
// In MountInstance constructor where SkyPredictor is created:
SkyPredictor = new SkyPredictor(() => SkyServer.CurrentTrackingRate(this));
```
This change is minimal (one constructor argument) and already fully supported by the existing
`CurrentTrackingRate(MountInstance inst)` overload.

---

## 6. Test Coverage Requirements

**Existing tests to update:**
- `GreenSwamp.Alpaca.MountControl.Tests\QueueMigrationTests.cs` -- references SkyServer.Steps callback; update after Steps property migration.

**New tests needed:**
- Integration test: Register 2 devices with different mount types. Issue `Tracking = true` on device 0.
  Assert device 1 tracking state is unchanged.
- Integration test: Issue `AbortSlew` on device 1. Assert device 0 slew continues.
- Integration test: `LimitStatus` is independent per device.
- Unit test: `SkyPredictor` uses per-instance tracking rate, not device-0 rate.
- Unit test: `GetLocalSiderealTime` uses per-instance longitude.

---

## 7. Execution Checklist

### Pre-work
- [ ] Verify baseline build is green: `run_build` -> 0 errors
- [ ] Record line counts: SkyServer.cs=281, SkyServer.Core.cs=1611, SkyServer.TelescopeAPI.cs=3312

### Phase M1 -- Tracking and Rate Methods
- [ ] `SetTracking(MountInstance)` -> `MountInstance.SetTracking()`
- [ ] `SetGuideRates(MountInstance)` -> `MountInstance.SetGuideRates()`
- [ ] `CalcCustomTrackingOffset(MountInstance)` -> `MountInstance.CalcCustomTrackingOffset()`
- [ ] `SkyGetRate(MountInstance)` -> `MountInstance.SkyGetRate()` (fix `_settings!.AlignmentMode` bug)
- [ ] `SetSlewRates(double, MountInstance)` -> `MountInstance.SetSlewRates(double)` + fix SkySettingsInstance callback
- [ ] `CurrentTrackingRate()` zero-arg static -> remove; update SkyPredictor default delegate
- [ ] `SetTrackingMode()` -> `MountInstance.SetTrackingMode()`
- [ ] `SetTrackingDirect(bool, TrackingMode)` -> `MountInstance.SetTrackingDirect(bool, TrackingMode)`
- [ ] Fix MountInstance.cs: all `SkyServer.SetTracking(this)` -> `this.SetTracking()`
- [ ] Fix MountInstance.Pec.cs: all `SkyServer.SetTracking(this)` -> `this.SetTracking()`
- [ ] Build and verify: [x]

### Phase M2 -- Core Operations to MountInstance
- [ ] `AbortSlew` -> `MountInstance.AbortSlew`
- [ ] `StopAxes` -> `MountInstance.StopAxes`
- [ ] `AutoHomeAsync` -> `MountInstance.AutoHomeAsync`
- [ ] `SyncToTargetRaDec` -> `MountInstance.SyncToTargetRaDec`
- [ ] `SyncToAltAzm` -> `MountInstance.SyncToAltAzm`
- [ ] `GoToPark` / `GoToParkAsync` -> `MountInstance`
- [ ] `GoToHome` / `GoToHomeX` -> `MountInstance`
- [ ] `ReSyncAxes` -> `MountInstance`
- [ ] `GetStoredParkPosition` / `SetParkAxis` -> `MountInstance`
- [ ] `GetAlternatePosition` family -> `MountInstance`
- [ ] `DetermineSideOfPier` -> `MountInstance`
- [ ] `CheckRaDecSyncLimit` / `CheckAltAzSyncLimit` -> `MountInstance`
- [ ] `GetDefaultPositions_Internal` -> `MountInstance.GetDefaultPositions`
- [ ] `GoToAsync` / `SlewMount` -> `MountInstance`
- [ ] `IsWithinFlipLimits` / `IsTargetWithinLimits` / `CheckAxisLimits` -> `MountInstance`
- [ ] `GetLocalSiderealTime` overloads -> `MountInstance` using `this.Settings.Longitude`
- [ ] Fix SlewController.cs: remove `SkyServer.StopAxes()` fallback
- [ ] Build and verify: [x]

### Phase M3 -- Delete Static ASCOM Property Facade
- [ ] Verify zero external callers for each property via find_symbol
- [ ] Delete all ~40 delegating static properties from SkyServer.TelescopeAPI.cs
- [ ] Fix `CanMoveAxis(TelescopeAxis)` to take MountInstance
- [ ] Build and verify: [x]

### Phase M4 -- Remaining Core State
- [ ] `LowVoltageEventSet` -> `MountInstance.OnLowVoltage()`, wire per-device
- [ ] `UpdateServerEvent` -> wire per-device
- [ ] `PropertyChangedSkySettings` -> per-device in `MountInstance.InitializeSettings()`
- [ ] `SkyErrorHandler(Exception)` -> add MountInstance parameter
- [ ] `UpdateMountLimitStatus(double[])` -> `MountInstance.UpdateMountLimitStatus()`
- [ ] `ShutdownServer()` -> `MountInstance.ShutdownDevice()`
- [ ] `Initialize()` -> `MountInstance.InitializeSettings()`
- [ ] Delete unused delegating wrappers (GetRawDegrees, GetRawSteps, UpdateSteps, etc.)
- [ ] Build and verify: [x]

### Phase M5 -- Remove Root Cause
- [ ] Verify 0 references to `_defaultInstance` and `_settings` in SkyServer files
- [ ] Delete `_defaultInstance` computed property
- [ ] Delete `_settings` computed property
- [ ] Delete all now-orphaned delegating wrappers (CTS, HC, HomeAxes, etc.)
- [ ] Build and verify: [x]

### Phase M6 -- Program.cs
- [ ] Replace `SkyServer.Initialize()` call with per-device loop
- [ ] Build and verify: [x]

### Phase M7 -- UnifiedDeviceRegistry
- [ ] Remove reserved slot constants
- [ ] Remove `IsReservedSlot()`
- [ ] Allow deletion of any device number
- [ ] Update `GetNextAvailableDeviceNumber()` to start from 0
- [ ] Update SetupDevicesController and Blazor pages if they call `IsReservedSlot`
- [ ] Build and verify: [x]

### Phase M8 -- Final Cleanup
- [ ] Delete or shrink SkyServer partial files
- [ ] Move `LimitStatusType` struct to standalone file or to MountInstance
- [ ] Mark `StaticPropertyChanged` as `[Obsolete]` then remove
- [ ] Run all tests: [x]
- [ ] Final build: [x] 0 errors

---

## 8. Key Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| MountInstance.cs grows too large (currently 3092 lines) | High | Medium | Split into more partial files: `MountInstance.Operations.cs`, `MountInstance.Tracking.cs`, `MountInstance.Sync.cs` |
| Breaking SlewController which calls `SkyServer.StopAxes()` | Medium | High | Fix in Phase M2 before deleting static method |
| PulseGuiding regression (AltAz multi-device) | Low | High | Covered by existing per-instance support; unit test before and after |
| Thread safety in `MountInstance` as more state moves in | Medium | Medium | All new instance methods follow the existing lock pattern (`_timerLock`) |
| `SkyPredictor` tracking rate bug (using device-0 rate for all devices) | High | Medium | Fix in Phase M1 companion change -- single line fix |
| `GetLocalSiderealTime()` using wrong longitude for non-device-0 | High | High | Fix in Phase M2; all `inst.GetLocalSiderealTime()` calls use `inst.Settings.Longitude` |

---

## 9. Files Modified Per Phase (Summary)

| Phase | Files Modified |
|-------|---------------|
| M1 | SkyServer.TelescopeAPI.cs, SkyServer.Core.cs, MountInstance.cs, MountInstance.Pec.cs, SkySettingsInstance.cs, SkyPredictor.cs |
| M2 | SkyServer.TelescopeAPI.cs, SkyServer.Core.cs, MountInstance.cs (new partial file recommended), SlewController.cs |
| M3 | SkyServer.TelescopeAPI.cs (deletions only) |
| M4 | SkyServer.Core.cs, MountInstance.cs |
| M5 | SkyServer.Core.cs (deletions only) |
| M6 | Program.cs |
| M7 | UnifiedDeviceRegistry.cs, SetupDevicesController.cs, DeviceManager.razor |
| M8 | SkyServer.cs (possibly delete), SkyServer.Core.cs (shrink), SkyServer.TelescopeAPI.cs (shrink), MountInstance.cs |

---

## 10. Definition of Done

The migration is complete when:

1. `grep -r "_defaultInstance" GreenSwamp.Alpaca.MountControl/SkyServer*.cs` returns **0 hits**.
2. `grep -r "GetInstance(0)" GreenSwamp.Alpaca.MountControl/SkyServer*.cs` returns **0 hits**.
3. `Telescope.cs` can serve two simultaneous instances of different mount types without any shared mutable state.
4. Blazor UI `TelescopeState.razor` continues to display per-device state correctly.
5. Build is green with 0 errors and 0 new warnings.
6. All existing tests pass.
7. Manual test: Two devices registered; `IsMountRunning` set true on device 0; device 1 remains unconnected. No cross-contamination.

---

*Document updated: 2026-04-08 10:31*
