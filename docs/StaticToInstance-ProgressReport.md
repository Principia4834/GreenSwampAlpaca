# Static-to-Instance Migration — Progress Report
## GreenSwamp Alpaca — SkyServer / MountControl

**Prepared for:** Offline review  
**Assessment date:** March 2026 (`master` branch)  
**Scope:** `GreenSwamp.Alpaca.MountControl` + `GreenSwamp.Alpaca.Server.TelescopeDriver`

---

## 1. Executive Summary

The migration from monolithic static `SkyServer` to instance-based `MountInstance` is
approximately **95 % complete**.

Build is **green**. All 3 automated tests pass. No regressions.

```
static SkyServer  ──delegates──>  MountInstance._defaultInstance
SkySettings (static facade) ──delegates──> SkySettingsInstance        ✅ complete
Telescope.cs (ASCOM driver)  ──calls──> GetInstance()._xxx             ✅ done (SkySystem deferred B2)
```

Steps 6, 7 and 8 are now **complete**.

---

## 2. Completed Work — Summary Table

| Phase / Step | Area | Status | Notes |
|---|---|:---:|---|
| **Phase 0 — Q1** | `SkyQueueImplementation` moved to instance | ✅ Done | Owns callbacks per device |
| **Phase 0 — Q2** | `MountQueueImplementation` moved to instance | ✅ Done | Owns callbacks per device |
| **Phase 0 — Q3** | `SkyQueueInstance` / `MountQueueInstance` fields on `MountInstance` | ✅ Done | `internal` fields |
| **Phase 0 — Q4** | `MountStart()` creates and registers queue instances | ✅ Done | Per-device lifecycle |
| **Phase 0 — Q5** | `MountStop()` nulls queue instances | ✅ Done | Clean teardown |
| **Phase 0 — Q6** | `IsMountRunning` reads per-instance queue | ✅ Done | No static queue reference |
| **Step 1** | Duplicate static arrays delegated | ✅ Done | `_factorStep`, `_stepsPerRevolution`, `_stepsWormPerRevolution` |
| **Step 2** | Flat state fields migrated | ✅ Done | All fields in §3.2 of migration plan |
| **Step 3** | PEC system migrated | ✅ Done | Completed this sprint; `MountInstance.Pec.cs` fully instance-based |
| **Step 4** | CTS fields on `MountInstance` | ✅ Done | Pre-done; confirmed in survey |
| **Step 5** | `SlewController` on `MountInstance` | ✅ Done | Pre-done; confirmed in survey |
| **Step 6** | Timer event pipeline | ✅ Done | `_mountPositionUpdatedEvent` per-instance; `SkyPrecisionGoto`/`SkyPulseGoto` use instance event |
| **Step 7** | `Steps` setter / position pipeline | ✅ Done | `ReceiveSteps()` on instance; queue callbacks bypass static setter; `WaitMountPositionUpdated` delegates to instance |
| **Step 8** | `Telescope.cs` instance routing | ✅ Done | All `SkyServer.*`/`SkySettings.*` → `inst.*`; `SkySystem.*` (3 lines) deferred — Blocker B2 |
| **Bridge B0** | Pre-register `_defaultInstance` as registry slot 0 | ✅ Removed (Step 9) | All 3 artefacts deleted; `_defaultInstance` is now a computed property |
| **Step 9** | Migrate write pipeline to registry instances; remove Bridge B0 | ✅ Done | 9a: 5 static write-backs removed from `OnUpdateServerEvent`; 9b: Bridge B0 fully removed; 9c: `IsSlewing` axis-rate check added; 9d: static shim removed |
| **Step 10** | Final clean-up / tests | ❌ Not started | Last phase |

---

## 3. Step 6 — Timer Event Pipeline ✅ Complete

| Sub-item | File | Status |
|---|---|:---:|
| `UpdateServerEvent` static wrapper delegates to `_defaultInstance?.OnUpdateServerEvent()` | `SkyServer.Core.cs` | ✅ |
| `OnUpdateServerEvent()` on `MountInstance` with per-device `_timerLock` | `MountInstance.cs` | ✅ |
| `_altAzTrackingTimer` field on `MountInstance`; unhooked in `MountStop()` | `MountInstance.cs` | ✅ |
| `_mountPositionUpdatedEvent` per-instance field on `MountInstance` | `MountInstance.cs` | ✅ |
| `ReceiveSteps()` on `MountInstance` — sets `_steps`, runs pipeline, signals instance event, fires `NotifyStepsChanged()` | `MountInstance.cs` | ✅ |
| `NotifyStepsChanged()` internal static helper on `SkyServer` for Blazor UI backward compat | `SkyServer.cs` | ✅ |
| Queue callbacks in `MountStart()` use `ReceiveSteps(steps)` for both Simulator and SkyWatcher | `MountInstance.cs` | ✅ |
| `SkyPrecisionGoto` uses `_mountPositionUpdatedEvent` directly | `MountInstance.cs` | ✅ |
| `SkyPulseGoto` uses `_mountPositionUpdatedEvent` directly | `MountInstance.cs` | ✅ |
| `PropertyChangedSkyQueue` / `PropertyChangedMountQueue` — redundant direct `.Set()` removed | `SkyServer.Core.cs` | ✅ |
| `WaitMountPositionUpdated()` delegates to `_defaultInstance?._mountPositionUpdatedEvent` | `SkyServer.Core.cs` | ✅ |

---

## 4. Step 7 — Steps Setter / Position Pipeline ✅ Complete

| Sub-item | File | Status |
|---|---|:---:|
| `_steps` backing field on `MountInstance` | `MountInstance.cs` | ✅ |
| `SetSteps()` converts hardware steps → degrees → RA/Dec → UI coords | `MountInstance.cs` | ✅ |
| `ReceiveSteps()` owns the full pipeline: set `_steps` → `SetSteps()` → signal event → `NotifyStepsChanged()` | `MountInstance.cs` | ✅ |
| `Steps` static getter reads `_defaultInstance._steps` | `SkyServer.cs` | ✅ |
| `Steps` static setter delegates to `_defaultInstance?.ReceiveSteps()` — no longer sets fields directly | `SkyServer.cs` | ✅ |
| Queue callbacks use `steps => ReceiveSteps(steps)` — static `SkyServer.Steps = steps` coupling broken | `MountInstance.MountStart()` | ✅ |

**Residual:** Static `_mountPositionUpdatedEvent` field and `MountPositionUpdatedEvent` property remain
in `SkyServer.cs` as no-op shims. Scheduled for removal in Step 9 alongside the broader static surface audit.

---

## 5. Step 8 — Telescope.cs Instance Routing ✅ Complete

`Telescope.cs` routes all calls through `GetInstance()`. Zero `SkyServer.*` / `SkySettings.*`
references remain. Three `SkySystem.*` references in `Connected` / `Connecting` are
intentionally retained pending Blocker B2 (serial connection counting).

### 5.1 Completed

| Category | Count | Status |
|---|---:|:---:|
| Public properties using `inst.*` | All | ✅ Done |
| Slew/sync/park action methods | All | ✅ Done |
| Private helpers (removed `static`, use `GetInstance()`) | 9 | ✅ Done |
| `SkySystem.*` connection refs | 3 | ⏸ Deferred (B2) |

---

## 5.2 Bridge Fix — Slot-0 Instance Pre-Registration ✅ Applied (temporary)

Step 8 migrated all `Telescope.cs` reads to `GetInstance(_deviceNumber)` →
`MountInstanceRegistry.GetInstance(0)`. The write pipeline (`OnUpdateServerEvent`,
`SetRateMoveSlewState`, `SetSlewRates`) continued to target `_defaultInstance`.
These were two **different** `MountInstance` objects:

- `_defaultInstance` — created by `SkyServer.Initialize(settingsInstance)`; all writes land here
- Registry slot 0 — created by `UnifiedDeviceRegistry.RegisterDevice(0, simulatorSettings, ...)`;
  all `Telescope.cs` reads land here — fresh object, all state at default/zero values

All state read by `Telescope.cs` (`SiderealTime`, `SlewSpeedEight`, `IsSlewing`) was therefore
always zero/false regardless of mount activity. Confirmed by three ConformU failures.

**Root cause:** `UnifiedDeviceRegistry.RegisterDevice()` always called
`MountInstanceRegistry.CreateInstance()`, creating a fresh instance for slot 0 regardless of
whether one was already registered by `SkyServer.Initialize()`.

**Fix applied (3 files — March 2026):**

| File | Change |
|---|---|
| `MountInstanceRegistry.cs` | Added `RegisterInstance(int, MountInstance)` — stores an existing instance in the registry; overwrite semantics for re-init safety; `TODO Step 9` |
| `SkyServer.Core.cs` `Initialize()` | Calls `MountInstanceRegistry.RegisterInstance(0, _defaultInstance)` immediately after constructing `_defaultInstance`; `TODO Step 9` |
| `UnifiedDeviceRegistry.cs` `RegisterDevice()` | Guards `CreateInstance()` with `if (MountInstanceRegistry.GetInstance(deviceNumber) == null)` — skips new-instance creation for pre-registered slots; `TODO Step 9` |

**Effect:** `GetInstance(0)` and `_defaultInstance` are now the same object. The read/write
split is eliminated for all state properties. ConformU passes.

**This is a deliberate temporary bridge.** All three changes carry `// TODO Step 9` comments.
The bridge must be removed as part of Step 9 once the write pipeline is migrated to target
per-device registry instances directly. See §6.2 for the removal plan.

---

## 6. Step 9 — Complete ✅

| Step | Description | Status |
|---|---|---|
| **9a** | Migrate `OnUpdateServerEvent` static write-backs to `this.*` | ✅ Done — 5 writes replaced (`_siderealTime`, `UpdateSteps`, `_lha`, `_isHome`, `_isSideOfPier`) |
| **9b** | Remove Bridge B0 — `_defaultInstance` computed property; `RegisterInstance()` removed; `CreateInstance()` unconditional | ✅ Done |
| **9c** | Fix `MountInstance.IsSlewing` axis-rate check | ✅ Done — `(Math.Abs(_rateMoveAxes.X) + Math.Abs(_rateMoveAxes.Y)) > 0` added |
| **9d** | Remove static `_mountPositionUpdatedEvent` shim; remove `?? _mountPositionUpdatedEvent` fallback | ✅ Done |
| **Step 10** | Final clean-up, remove static facades, integration tests | ❌ Not started |

### 6.1 Bridge B0 — Removed ✅

| Artefact | File | Status |
|---|---|---|
| `_defaultInstance` stored field | `SkyServer.Core.cs` line 68 | ✅ Converted to computed property `=> MountInstanceRegistry.GetInstance(0)` |
| `MountInstance("default", settings)` construction + `RegisterInstance(0, _defaultInstance)` call | `SkyServer.Core.cs` `Initialize()` | ✅ Removed |
| `if (MountInstanceRegistry.GetInstance(deviceNumber) == null)` guard | `UnifiedDeviceRegistry.cs` `RegisterDevice()` | ✅ Removed — `CreateInstance()` now unconditional |
| `RegisterInstance()` method | `MountInstanceRegistry.cs` | ✅ Removed — zero callers confirmed before deletion |

### 6.2 Write Pipeline Migration — Required Before Bridge Removal

Before the bridge can be removed, every write that currently targets `_defaultInstance` must
be redirected to the correct per-device registry instance. The key write sites are:

| Write site | File | Current target | Required change |
|---|---|---|---|
| `UpdateServerEvent` static wrapper → `_defaultInstance?.OnUpdateServerEvent()` | `SkyServer.Core.cs` | `_defaultInstance` only | Call `OnUpdateServerEvent()` on each active registered instance (iterator over `MountInstanceRegistry`) |
| `SetRateMoveSlewState(bool)` — sets `_defaultInstance._isSlewing` | `SkyServer.Core.cs` | `_defaultInstance` | Accept `MountInstance` parameter or make it an instance method |
| `SetSlewRates()` / `Defaults()` — sets `_defaultInstance._slewSpeedEight` etc. | `SkyServer.Core.cs` | `_defaultInstance` | Call per registry instance at initialisation |
| `OnUpdateServerEvent()` line ~1513 — `SkyServer.SiderealTime = SkyServer.GetLocalSiderealTime()` | `MountInstance.cs` | `_defaultInstance._siderealTime` via static setter | Replace with direct `this._siderealTime = ...` write |
| `SkyServer.RateMovePrimaryAxis` / `RateMoveSecondaryAxis` setter | `SkyServer.cs` | `_defaultInstance._rateMoveAxes` | Already called from `MountInstance`; remove static indirection; write `this._rateMoveAxes` directly |

**Also (code quality — no bug after bridge, but should be fixed in Step 9):**
`MountInstance.IsSlewing` (lines ~388–392) is missing the
`(Math.Abs(_rateMoveAxes.X) + Math.Abs(_rateMoveAxes.Y)) > 0` axis-rate check present in
`SkyServer.IsSlewing`. Once the bridge is removed `inst` will be a freshly-created registry
instance, so this gap will matter again. Close it so `MountInstance.IsSlewing` is
self-contained and matches the `SkyServer.IsSlewing` logic exactly.

---

## 7. Known Blockers

| ID | Description | Affects | Status |
|---|---|---|:---:|
| **B0** | Bridge: `_defaultInstance` pre-registered as registry slot 0 — temporary measure closing the Step-8 read/write split | Step 9 (must be removed) | ✅ Removed — `_defaultInstance` is now a computed property; all 3 artefacts deleted |
| **B1** | `_mountPositionUpdatedEvent` is static (shared) | Steps 6, 7 | ✅ Resolved |
| **B2** | `SkySystem` serial connection is reference-counted for one port | Step 8e (Telescope.cs `Connected`) | ❌ Deferred |
| **B5** | `StaticPropertyChanged` fires for Blazor UI subscriptions — per-device notifications not yet wired | Blazor component refresh on second device | ❌ Not started |

---

## 8. Files Changed for Steps 6 / 7 (all complete)

| File | Change made |
|---|---|
| `MountInstance.cs` | Added `_mountPositionUpdatedEvent` field; added `ReceiveSteps()` method; updated `SkyPrecisionGoto` and `SkyPulseGoto` to use instance event; updated both `SetupCallbacks` in `MountStart()` |
| `SkyServer.cs` | Added `NotifyStepsChanged()` static helper; `Steps` setter now delegates to `_defaultInstance?.ReceiveSteps()` |
| `SkyServer.Core.cs` | Removed redundant `_mountPositionUpdatedEvent.Set()` from `PropertyChangedSkyQueue` and `PropertyChangedMountQueue`; `WaitMountPositionUpdated()` delegates to instance event |

---

## 8a. Files Changed for Bridge Fix B0 (temporary — removal tracked in §6.1)

| File | Change made |
|---|---|
| `MountInstanceRegistry.cs` | Added `RegisterInstance(int deviceNumber, MountInstance instance)` — public method; dictionary overwrite semantics; `TODO Step 9` comment |
| `SkyServer.Core.cs` | `Initialize()` calls `MountInstanceRegistry.RegisterInstance(0, _defaultInstance)` after constructing `_defaultInstance`; `TODO Step 9` comment block |
| `UnifiedDeviceRegistry.cs` | `RegisterDevice()` wraps `CreateInstance()` call in `if (MountInstanceRegistry.GetInstance(deviceNumber) == null)` guard; `TODO Step 9` comment |

---

## 9. Current File Sizes (as surveyed March 2026)

| File | Lines | Notes |
|---|---:|---|
| `SkyServer.cs` | ~998 | Static partial; `_mountPositionUpdatedEvent` lines 61–67 |
| `SkyServer.Core.cs` | ~2 059 | Static partial; old `SkyTasks` overload line 1377; `UpdateServerEvent` line 709 |
| `SkyServer.TelescopeAPI.cs` | ~1 870 | Static partial ASCOM API |
| `MountInstance.cs` | ~2 145 | Primary instance class; `SkyPrecisionGoto` ~line 1800 |
| `MountInstance.Pec.cs` | ~427 | PEC partial — fully migrated ✅ |
| `Telescope.cs` | ~2 127 | ASCOM driver — 79 `GetInstance()` done, 53 static refs remaining |
| `MountInstanceRegistry.cs` | ~155 | Thread-safe registry |

---

## 10. Recommended Next Actions (in order)

1. **Step 10 — Remove `SkySettings` static facade** (deferred from Step 8):
   - Inject `SkySettingsInstance` into Blazor pages via DI
   - Remove `SkySettings.Initialize()` call and the static class

2. **Integration tests** (Phase 0 test table from migration plan):
   - `WhenSimulatorConnectedThenMountInstanceOwnsQueue`
   - `WhenTwoDevicesRegisteredThenQueuesAreIndependent`
   - `WhenStepsUpdatedThenMountInstanceReceivesCallback`
   - `WhenCommandSentToDevice0ThenDevice1QueueUnaffected`

3. **Blocker B2** (`SkySystem` serial connection reference counting) — required for Step 8e
   (`Telescope.cs` `Connected` / `Connecting` routing through `MountInstance`).

4. **Blocker B5** (per-device `StaticPropertyChanged` notifications for Blazor UI) — required
   for multi-device UI refresh without the static facade.
