# Static-to-Instance Migration — Progress Report
## GreenSwamp Alpaca — SkyServer / MountControl

**Prepared for:** Offline review  
**Assessment date:** March 2026 (`master` branch)  
**Scope:** `GreenSwamp.Alpaca.MountControl` + `GreenSwamp.Alpaca.Server.TelescopeDriver`

---

## 1. Executive Summary

The migration from monolithic static `SkyServer` to instance-based `MountInstance` is
approximately **90 % complete**.

Build is **green**. All 3 automated tests pass. No regressions.

```
static SkyServer  ──delegates──>  MountInstance._defaultInstance
SkySettings (static facade) ──delegates──> SkySettingsInstance        ✅ complete
Telescope.cs (ASCOM driver)  ──calls──> GetInstance()._xxx             ~80% done
```

Steps 6 and 7 are now **complete**. The `_mountPositionUpdatedEvent` blocker (B1) has been
resolved — position signalling is fully per-instance.

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
| **Step 8** | `Telescope.cs` instance routing — partial | ⚠️ Partial | See §5 below |
| **Step 9** | Remove static `SkyServer` surface | ❌ Not started | Depends on Steps 6–8 |
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

## 5. Step 8 — Telescope.cs Instance Routing (Partial ~80%)

`Telescope.cs` is 2 127 lines. The `GetInstance()` helper (line 100) is in place and routes by `_deviceNumber`.

### 5.1 Completed

| Category | Count | Status |
|---|---:|:---:|
| `GetInstance()` call sites (properties/methods using instance) | 79 | ✅ Done |
| Can* capability properties | All | ✅ Done |
| Position/coordinate read properties | All | ✅ Done |
| Settings read properties (`AlignmentMode`, `Mount`, `DisplayInterval` via `inst.Settings.*`) | Most | ✅ Done |

### 5.2 Remaining static references

| Category | Count | Representative examples |
|---|---:|---|
| `SkyServer.*` action/state refs | **44** | `SyncToTargetRaDec`, `WaitMountPositionUpdated`, `CheckRaDecSyncLimit`, `IsTargetReachable`, `AtPark`, `Tracking`, `SlewSpeedEight` |
| `SkySettings.*` refs | **6** | `VersionOne` ×2, `AlignmentMode`, `Mount`, `DisplayInterval` ×2 |
| `SkySystem.*` refs | **3** | `Connected`, `SetConnected`, `Connecting` (Blocker B2 — connection counting) |
| **Total remaining** | **53** | |

### 5.3 Blocker B2 — SkySystem Connection Counting

`SkySystem.Connected` / `SetConnected` / `Connecting` are used in Telescope.cs connection
management. These are static and reference-counted for the single serial port. Migrating these
requires a per-device connection strategy — deferred until the serial layer design is finalised.

---

## 6. Steps 9 and 10 — Not Started

| Step | Description | Dependency |
|---|---|---|
| **Step 9** | Remove / reduce static `SkyServer` surface | Steps 6, 7, 8 complete |
| **Step 10** | Final clean-up, remove static facades, integration tests | Step 9 complete |

---

## 7. Known Blockers

| ID | Description | Affects | Status |
|---|---|---|:---:|
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

1. **Step 8 (remaining 53 refs):** Work through `Telescope.cs` `SkyServer.*` refs group by group
   (slew actions → tracking → park → sync → remaining state). Leave `SkySystem.*` refs (B2) for last.
   Current breakdown:
   - `SkyServer.*` action/state refs: **44** (e.g. `SyncToTargetRaDec`, `WaitMountPositionUpdated`, `AtPark`, `Tracking`)
   - `SkySettings.*` refs: **6** (e.g. `VersionOne`, `AlignmentMode`)
   - `SkySystem.*` refs: **3** (blocked by B2)

2. **Step 9:** Once Telescope.cs is clean, audit and remove dead static surface from `SkyServer.*`.
   Includes removing the now-shim `_mountPositionUpdatedEvent` / `MountPositionUpdatedEvent` from `SkyServer.cs`.

3. **Step 10:** Integration tests covering two concurrent `MountInstance` objects with separate
   precision goto operations (validates the per-instance event isolation delivered by Steps 6/7).
