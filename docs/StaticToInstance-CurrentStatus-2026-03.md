# Static-to-Instance Migration Current Status Report
## GreenSwamp Alpaca SkyServer / MountControl

**Prepared:** March 2026 (post-SkyWatcher GermanPolar confidence test)
**Update history:**
- 2026-04-05 08:05 Markdown history block reformatted to bullet list for correct Preview rendering
- 2026-04-05 08:02 Encoding repaired (UTF-8 BOM), emoji symbols restored, CRLF normalized
- April 2026, 08:25 UTC full re-verification against `master`
- April 2026, 14:30 UTC `SkyServer.TelescopeAPI.cs` full region-by-region review; CTS delegation confirmed; new concerns G3 identified
- 2026-04-03 09:00 timestamp format standardised per copilot-instructions.md
- 2026-04-03 09:23 Phase G complete (X1, X2, G1, G2, G3); all builds green
- 2026-04-04 09:20 Two-device smoke test failure diagnosed; Phase H items identified; "all blockers cleared" conclusion overturned; Phase E elevated to critical
- 2026-04-04 08:58 UTC Phase H complete (H1-H5); all builds green; two-device simulator isolation confirmed
- 2026-04-04 19:48 Encoding fixed (Windows-1252 mojibake UTF-8 without BOM)
- 2026-04-04 19:53 Phase I complete; all builds green; ConformU device-1 rate blockers resolved
- 2026-04-04 20:58 Group 3 low-risk items re-assessed; Phase J identified; J1/J2/J4/J5/J7 elevated to critical; J3 elevated to critical; J6 deferred to Phase E
- 2026-04-04 20:12 Duplicate sections removed; Phase J task table added to Section 5; LF line endings normalised
- 2026-04-04 22:49 Phase J complete (J1/J2/J4/J5/J7 resolved); git-restore I/R regressions re-applied; all builds green
- 2026-04-05 09:06 Phase K complete; SlewController and SlewOperation fully instance-aware; all builds green
- 2026-04-05 10:48 Phase L complete; Step 9 bridge region fully migrated per-instance; EmergencyStop fixed; commit 487c08e; all builds green
**Branch:** `master`
**Build baseline:** Green Simulator and SkyWatcher GermanPolar pass confidence tests
**Assessment method:** Direct code review of all five key files + queue subsystem + full read of all 14 regions of `SkyServer.TelescopeAPI.cs` + two-device smoke test failure diagnosis (2026-04-04)

---

## 1. Executive Summary

The migration is **functionally complete for single-telescope operation** and is estimated
at approximately **95 %** complete overall. Every piece of per-device state now lives on
`MountInstance`; the static `SkyServer.*` surface is a pure thin delegation layer to
`MountInstanceRegistry.GetInstance(0)`. The ASCOM driver (`Telescope.cs`) routes entirely
through `GetInstance(_deviceNumber)`.

**⚠️ Assessment revised 2026-04-04 two-device smoke test failed.** Connecting to device 1
returned device 0 state. Code tracing identified four new Phase H blockers; the previous
"all hard blockers are cleared" conclusion was incorrect.

Remaining work by category:

| Category | Items | Blocking multi-telescope? |
|---|---|---|
| **Phase H Simulator static contamination** ✅ | `Controllers._ctsMount` → instance field; `MountStop()` and `OnUpdateServerEvent()` per-instance; `ConnectAlignmentModel()` is empty stub (no-op) | ✅ Resolved |
| **Phase I Static method instance-awareness** ✅ | `SetSlewRates`, `SetGuideRates`, `CalcCustomTrackingOffset`, `SetTracking` accept `MountInstance? instance`; `Defaults()`/`MountConnect()` pass `this`; `SkyPulseGoto`, `SimGoTo`, `SkyGoTo`, `PulseGuideAltAz` read/write instance fields directly | ✅ Resolved |
| **Phase J Group 3 re-assessment** | J1/J2/J4/J5/J7 resolved ✅; J3 (logging labels) 🟠 still pending; J6 deferred to Phase E 🟡 | ✅ Mostly resolved — J3 logging labels remaining |
| **Phase K SlewController/SlewOperation instance-aware** ✅ | `SlewController`/`SlewOperation` state/dispatch via `operation.MountInstance`; `InstanceApplyTracking`, `InstanceApplyTrackingDirect`, `InstanceSetTrackingMode`, `InstanceCompletePark` helpers added | ✅ Resolved |
| **Phase L Step 9 bridge region fully migrated** ✅ | `MountInstance.cs`; 12 ConformU-failing bridge methods + `EmergencyStop` now operate on this device's hardware queue, settings, and CTS tokens; root cause of device-1 failures eliminated | ✅ Resolved |
| **Phase E Blazor per-device UI notifications** | `TelescopeStateService` reads all state from `SkyServer.*` (device 0 only); `StaticPropertyChanged` fires globally | 🔴 Yes confirmed primary cause of smoke test UI symptom |
| **Phase F Option C Phase 3** | Per-device serial config in `Devices[]` array; Blazor multi-device UI not started | 🟡 UI / config only |

**Minor residuals (cleanup only):**
- `//lock (_goToAsyncLock)` commented reference in `SkyServer.TelescopeAPI.cs` line 787 field is gone, comment can be removed
- `SkySettingsBridge.cs` still exists on disk but is excluded from compilation via `<Compile Remove>` in the csproj; should be deleted

**New concerns identified April 2026 (TelescopeAPI review):**

| ID | Item | File | Blocking multi-telescope? |
|---|---|---|---|
| **G1** ✅ | `RateDecOrg` / `RateRaOrg` converted to delegating computed properties | `SkyServer.TelescopeAPI.cs:538,572` | ✅ Resolved Phase G |
| **G2** ✅ | `MountInstance? instance` parameter threaded through `SlewAxes→SlewMount→GoToAsync` | `SkyServer.TelescopeAPI.cs:763`, `SkyServer.Core.cs` | ✅ Resolved Phase G |
| **G3** ✅ | `PulseGuideAltAz()` moved to `MountInstance` instance method | `MountInstance.cs`, `SkyServer.TelescopeAPI.cs` | ✅ Resolved Phase G |

**New concerns identified 2026-04-04 (two-device smoke test failure):**

| ID | Item | File | Blocking multi-telescope? |
|---|---|---|---|
| **H1** 🔴 | `Controllers._ctsMount` is `private static` both simulator instances share one CTS; `Stop()` is also `private static`; stopping either device cancels the other | `GreenSwamp.Alpaca.Simulator\Controllers.cs:28` | 🔴 Yes stops both simulators on single disconnect |
| **H2** 🟠 | `OnUpdateServerEvent()` calls static `SkyServer.CheckSlewState()` and `SkyServer.CheckAxisLimits()` route to device 0 only; device 1's slew state and axis limits are never updated | `MountInstance.cs:1723–1724` | 🟠 Yes slew/limit detection broken for device 1 |
| **H3** 🟠 | `MountStop()` calls static `SkyServer.Tracking = false` and `SkyServer.CancelAllAsync()` disconnecting device 1 disables device 0's tracking and cancels device 0's async ops | `MountInstance.cs:1675–1676` | 🟠 Yes disconnect of device 1 disrupts device 0 |
| **H4** 🟡 | `SkyServer.ConnectAlignmentModel()` called for each device in `MountStart()` unclear if per-instance or device-0-only | `MountInstance.cs:1602` | 🟡 Needs verification |
| **H5** 🟡 | `Mount.Simulator.Settings.AutoHomeAxisX/Y` is a `public static class` last-writer-wins when two simulators initialise | `GreenSwamp.Alpaca.Simulator\Settings.cs:9` | 🟡 AutoHome only low risk |

**New concerns identified 2026-04-04 (Group 3 re-assessment — previously low risk, now elevated):**

| ID | Item | File | Blocking multi-telescope? |
|---|---|---|---|
| **J1** ✅ | `PulseGuide()` static method reads `SkyServer.SouthernHemisphere` (→ `_defaultInstance.Settings.Latitude < 0`); Dec direction inversion always uses device 0's latitude; device 1 at a different hemisphere gets wrong Dec guide direction | `SkyServer.TelescopeAPI.cs` | ✅ Resolved Phase J |
| **J2** ✅ | `AxesContext.FromSettings(settings)` sets `SouthernHemisphere = SkyServer.SouthernHemisphere` (comment: "Still from SkyServer for now") — always device 0; also `SideOfPier = SkyServer.SideOfPier` — device 0 only; coordinate conversion pipeline uses wrong hemisphere for device 1 | `AxesContext.cs:130,131` | ✅ Resolved Phase J |
| **J3** 🟠 | All `MonitorEntry` records in `MountInstance.cs` use `Device = MonitorDevice.Server` with no device-number identifier in `Message`; log entries from device 0 and device 1 are indistinguishable in the log file | `MountInstance.cs` (all MonitorLog calls) | 🟠 Not functionally blocking, but makes two-device troubleshooting impossible |
| **J4** ✅ | `_altAzTrackingTimer` is `private static`; `StartAltAzTrackingTimer()` is `private static`; `AltAzTrackingTimerEvent` is `internal static` and always calls `SetTracking()` on `_defaultInstance`; timer `Period` comes from `_settings!.AltAzTrackingUpdateInterval` (device 0 only); two AltAz mounts share one timer at device 0's interval — device 1's tracking never updated | `SkyServer.TelescopeAPI.cs` | ✅ Resolved Phase J |
| **J5** ✅ | `SkyServer.LimitStatus` is `public static LimitStatusType` (a value-type struct); `MountInstance.CheckAxisLimits()` reads `SkyServer.LimitStatus` for the AltAz case; axis limit *settings* (`HourAngleLimit`, `AxisLimitX` etc.) are correctly per-instance in `_settings` ✅ but limit *state* is a shared static — last-writer-wins for two AltAz devices | `SkyServer.cs:951`, `MountInstance.cs` | ✅ Resolved Phase J |
| **J6** 🟡 | UI-only residuals: `AxesContext.FromSettings` reading `SkyServer.SideOfPier` for display; `AutoHomeProgressBar` and similar UI-state statics always show device 0 — no motor control impact | `SkyServer.cs`, `AxesContext.cs` | 🟡 No — display only; deferred to Phase E |
| **J7** ✅ | `MountInstance.CheckAxisLimits()` limit-enforcement calls `SkyServer.StopAxes()` and `SkyServer.GoToPark()` — both are static and route to `_defaultInstance`; when device 1 hits a physical limit, device 0's motors are stopped / device 0 parks; each device is a physically distinct mount with separate hardware and power supply | `MountInstance.cs` (limit action code) | ✅ Resolved Phase J |

---

## 2. What Is Confirmed Complete (code-verified, April 2026 re-check)

| Area | File(s) | Notes |
|---|---|---|
| All per-device state fields on `MountInstance` | `MountInstance.cs` | Every backing field confirmed instance-owned |
| `_defaultInstance` computed property (`=> MountInstanceRegistry.GetInstance(0)`) | `SkyServer.Core.cs` | Bridge B0 fully removed |
| `SkyServer.*` properties all delegate to `_defaultInstance` | All three `SkyServer.*.cs` partials | Zero independent static backing values |
| `SkyQueueImplementation` owned by `MountInstance` | `MountInstance.cs` | `SkyQueueInstance` field; started in `MountStart()` |
| `MountQueueImplementation` owned by `MountInstance` | `MountInstance.cs` | `MountQueueInstance` field; started in `MountStart()` |
| `ReceiveSteps()` per-instance with full pipeline | `MountInstance.cs` | Steps → coordinates → event signal → UI notify |
| `_mountPositionUpdatedEvent` per-instance | `MountInstance.cs` | `SkyPrecisionGoto` and `SkyPulseGoto` use it |
| `OnUpdateServerEvent()` instance method | `MountInstance.cs` | Per-device timer lock |
| `UpdateServerEvent` delegates to `_defaultInstance` | `SkyServer.Core.cs` | Single-telescope path correct |
| `ISerialPort` per-instance (`_serial`, `_connectStates`) | `MountInstance.cs` | `OpenSerial()` / `CloseSerial()` on instance |
| `SkySystem.cs` deleted | | Zero references remain |
| `SkySettings` static facade deleted | | All 35 references migrated |
| `SkySettingsInstance` fully instance-based | `SkySettingsInstance.cs` | All 134 settings properties |
| `Telescope.cs` routes via `GetInstance(_deviceNumber)` | `Telescope.cs` | Every property/method uses `GetInstance()` |
| Instance-aware `SkyTasks(MountTaskName, MountInstance)` | `SkyServer.Core.cs` | Complete; all commands use `q = instance.SkyQueueInstance` |
| Instance-aware `SimTasks(MountTaskName, MountInstance)` | `SkyServer.Core.cs` | Complete; all commands use `q = instance.MountQueueInstance` |
| Instance-aware `AxesStopValidate(MountInstance)` | `SkyServer.Core.cs` | Complete; uses per-instance queue refs |
| `MountConnect()` reads capabilities from instance fields directly | `MountInstance.cs` | No `SkyServer.CanPPec` etc. read-back |
| PEC system fully migrated | `MountInstance.Pec.cs` | Fully instance-based |
| Cancellation tokens per-instance | `MountInstance.cs` | `_ctsGoTo`, `_ctsPulseGuide*`, `_ctsHcPulseGuide` |
| `SlewController` per-instance | `MountInstance.cs` | `_slewController` field |
| HC anti-backlash fields per-instance | `MountInstance.cs` | `_hcPrevMoveRa/Dec`, `_hcPrevMovesDec` |
| Tracking offset rate per-instance | `MountInstance.cs` | `_trackingOffsetRate` field |
| `_skyTrackingOffset` per-instance | `MountInstance.cs` | `int[2]` array |
| **A1 Legacy `SkyTasks(MountTaskName)` deleted** ✅ | `SkyServer.Core.cs` | Only instance-aware overload remains; confirmed by direct search |
| **A2 Legacy `AxesStopValidate()` deleted** ✅ | `SkyServer.Core.cs` | Only `AxesStopValidate(MountInstance)` remains; "Phase A2 verified" commit |
| **B1 `SkyQueue.RegisterInstance()` removed** ✅ | `MountInstance.cs` | Zero calls remain in the codebase |
| **B2/B3 Dead `PropertyChangedSkyQueue`/`MountQueue` handlers deleted** ✅ | `SkyServer.Core.cs` | Static facades gone; handlers had nothing to subscribe to |
| **B4/B5 `[Obsolete]` shortcut constructors removed** ✅ | `SkyCommands.cs` | All base class ctors require `(long id, ICommandQueue<> queue)`; zero `[Obsolete]` attributes |
| **B6 Static `SkyQueue` class deleted** ✅ | `SkyQueue.cs` | File now contains only `SkyQueueImplementation` (109 lines) |
| **B7 Static `MountQueue` class deleted** ✅ | `MountQueue.cs` | File now contains only `MountQueueImplementation` (63 lines) |
| **C1/C2 Pulse-guide callbacks write to `this` fields** ✅ | `MountInstance.cs` | Both Simulator (line 1595–1596) and SkyWatcher (line 1632–1633) branches fixed |
| **D1 `_stepsTimeFreq` field added to `MountInstance`** ✅ | `MountInstance.cs` | `internal long[] _stepsTimeFreq = { 0, 0 };` at line 63 |
| **D2/D3 `StepsTimeFreq` delegates; instance `SkyTasks` writes instance field** ✅ | `SkyServer.cs`, `SkyServer.Core.cs` | `get => _defaultInstance?._stepsTimeFreq ?? new long[] { 0, 0 }` |
| **D4 `_mountRunning` static field removed** ✅ | `SkyServer.cs` | `IsMountRunning` is `get => _defaultInstance?.IsMountRunning ?? false` |
| **D5/D6 `SnapPort1/2` delegate to `_defaultInstance`** ✅ | `SkyServer.cs` | `get => _defaultInstance?._snapPort1 ?? false` etc. |
| **D7 `_goToAsyncLock` field removed** ✅ | `SkyServer.TelescopeAPI.cs` | Field declaration gone; only `//lock (_goToAsyncLock)` comment remains (cleanup item) |
| **D8 `LastDecDirection` delegates to `_defaultInstance._lastDecDirection`** ✅ | `SkyServer.cs` | `internal GuideDirection _lastDecDirection` on `MountInstance` at line 106 |
| **3.11 `SkySettingsBridge.cs` excluded from compilation** ✅ | `.csproj` | `<Compile Remove="SkySettingsBridge.cs" />` confirmed; file still exists on disk (delete when ready) |
| **CTS tokens all four delegate to `_defaultInstance`** ✅ | `SkyServer.Core.cs:131–150` | `_ctsGoTo`, `_ctsPulseGuideRa`, `_ctsPulseGuideDec`, `_ctsHcPulseGuide` all computed delegating properties; comment: "Phase 5.3 delegate to default instance to prevent cross-device cancellation" |
| **`_slewController` computed delegating property** ✅ | `SkyServer.TelescopeAPI.cs:~1549` | `get => _defaultInstance?._slewController` |
| **`SlewAsync` / `SlewSync` delegate to instance** ✅ | `SkyServer.TelescopeAPI.cs:~1568–1590` | Both methods call `_defaultInstance.SlewAsync/SlewSync` |
| **`_parkSelected` delegating computed property** ✅ | `SkyServer.cs:64–68` | `get => _defaultInstance?._parkSelected; set { if (_defaultInstance != null) _defaultInstance._parkSelected = value; }` |
| **X1 `//lock (_goToAsyncLock)` comment removed** ✅ | `SkyServer.TelescopeAPI.cs` | Dead comment referencing deleted `_goToAsyncLock` field removed from `GoToAsync` |
| **X2 `SkySettingsBridge.cs` deleted** ✅ | `GreenSwamp.Alpaca.MountControl\` | File deleted; `<Compile Remove="SkySettingsBridge.cs" />` removed from `.csproj` |
| **G1 `RateDecOrg` / `RateRaOrg` delegating** ✅ | `SkyServer.TelescopeAPI.cs` | Converted from true static auto-properties to computed delegating properties routing to `_defaultInstance?.RateDecOrg/RateRaOrg`; `MountInstance` backing fields already existed |
| **G2 `MountInstance` param through `SlewAxes→SlewMount→GoToAsync`** ✅ | `SkyServer.TelescopeAPI.cs`, `SkyServer.Core.cs` | `MountInstance? instance = null` optional parameter added; `var effectiveInstance = instance ?? _defaultInstance!` resolves in `SlewMount` and `GoToAsync`; `AutoHome` callers backward-compat via default |
| **G3 `PulseGuideAltAz` moved to `MountInstance`** ✅ | `MountInstance.cs`, `SkyServer.TelescopeAPI.cs` | New `internal void PulseGuideAltAz(...)` in `#region AltAz Pulse Guide`; uses `this.SkyPredictor`, `_monitorPulse` backing field, inlined `SiderealRate = 15.0410671786691`; `StopAltAzTrackingTimer` made `internal static`; 4 call sites updated |
| **I1 `SetSlewRates` instance-aware** ✅ | `SkyServer.Core.cs`, `MountInstance.cs` | `SetSlewRates(double maxRate, MountInstance? instance = null)`; `var inst = instance ?? _defaultInstance`; all `_slewSpeedOne…Eight` writes use `inst.*`; `Defaults()` and `MountConnect()` pass `this` |
| **I2 `SetGuideRates` instance-aware** ✅ | `SkyServer.TelescopeAPI.cs`, `MountInstance.cs` | `SetGuideRates(MountInstance? instance = null)`; `inst.GuideRateRa/Dec`; `Defaults()` passes `this` |
| **I3 `CalcCustomTrackingOffset` instance-aware** ✅ | `SkyServer.Core.cs`, `MountInstance.cs` | `CalcCustomTrackingOffset(MountInstance? instance = null)`; reads `inst._stepsTimeFreq[n]`/`inst._stepsPerRevolution[n]`; writes `inst._trackingOffsetRate.X/Y` directly; `MountConnect()` passes `this` |
| **I4 `Defaults()` — `_slewSettleTime` direct write** ✅ | `MountInstance.cs` | `_slewSettleTime = 0` replaces `SkyServer.SlewSettleTime = 0` |
| **I5 `Defaults()` — `_parkSelected` direct write** ✅ | `MountInstance.cs` | `_parkSelected = found` replaces `SkyServer.ParkSelected = found` |
| **R1 `SkyPulseGoto` — `_slewState` direct read** ✅ | `MountInstance.cs` | Both spin-wait loop checks use `_slewState == SlewType.SlewNone` (×2); no longer reads device-0 static |
| **R2 `SimGoTo`/`SkyGoTo` — `_slewSettleTime` direct read** ✅ | `MountInstance.cs` | Both goto methods read `_slewSettleTime` directly; `TimeSpan.FromSeconds(_slewSettleTime)` |
| **R3 `PulseGuideAltAz` — `SetTracking(this)`** ✅ | `MountInstance.cs` | `SkyServer.SetTracking(this)` passes instance; no longer routes SetTracking to device 0 |
| **R4 `PulseGuideAltAz` — `_isPulseGuidingRa/Dec` direct access** ✅ | `MountInstance.cs` | Three sites: axis-0 guard (`_isPulseGuidingDec`), axis-1 guard (`_isPulseGuidingRa`), reset at end (`_isPulseGuidingRa = false; _isPulseGuidingDec = false`) |
| **R5 `PulseGuideAltAz` — `this.UpdateSteps()`** ✅ | `MountInstance.cs` | `this.UpdateSteps()` replaces `SkyServer.UpdateSteps()` |
| **J1 `PulseGuide()` — per-instance `SouthernHemisphere`** ✅ | `SkyServer.TelescopeAPI.cs` | Reads `inst._settings.Latitude < 0` for the active device; no longer uses `SkyServer.SouthernHemisphere` (device 0) |
| **J2 `AxesContext.FromSettings` — per-instance hemisphere + SideOfPier** ✅ | `AxesContext.cs` | `SouthernHemisphere = settings.Latitude < 0`; optional `PointingState? sideOfPier` param; all `MountInstance` callers pass `SideOfPier` |
| **J4 AltAz tracking timer per-instance** ✅ | `MountInstance.cs` | `_altAzTrackingTimer` instance field; `StartAltAzTrackingTimer()` / `StopAltAzTrackingTimer()` instance methods; `AltAzTrackingTimerTick` instance handler; `_altAzTrackingLock` Int32 per-instance |
| **J5 `_limitStatus` per-instance** ✅ | `MountInstance.cs` | `internal SkyServer.LimitStatusType _limitStatus` on `MountInstance`; `CheckAxisLimits()` reads/writes `_limitStatus` directly (not static `SkyServer.LimitStatus`) |
| **J7 `InstanceStopAxes` / `InstanceGoToPark` per-instance** ✅ | `MountInstance.cs` | `CheckAxisLimits()` calls `InstanceStopAxes()` and `InstanceGoToPark()` which operate on `this` device's queues only; no longer calls `SkyServer.StopAxes()` / `SkyServer.GoToPark()` |
| **K1 `InstanceApplyTracking/Direct/SetTrackingMode/CompletePark` helpers** ✅ | `MountInstance.cs` | `InstanceSetTrackingMode()`, `InstanceApplyTracking(bool)`, `InstanceApplyTrackingDirect(bool, TrackingMode)`, `InstanceCompletePark()` added before AltAz timer region |
| **K2 `SlewController`/`SlewOperation` fully instance-aware** ✅ | `SlewController.cs` | All state transitions and hardware dispatch use `operation.MountInstance`; `ForceStopAxesAsync(MountInstance?)` added; tracking via instance helpers; no static `SkyServer` routing in slew execution path |
| **L1 `InstanceCancelAllAsync` helper** ✅ | `MountInstance.cs` | Per-instance cancel of `_ctsGoTo`, `_ctsPulseGuideRa/Dec`, `_ctsHcPulseGuide` with 2 s spin-wait; mirrors `SkyServer.CancelAllAsync` |
| **L2 `InstanceSetRateMoveSlewState` helper** ✅ | `MountInstance.cs` | Per-instance slew-state update from `_rateMoveAxes.X/Y`; sets `_moveAxisActive`, `_isSlewing`, `_slewState`; mirrors `SkyServer.SetRateMoveSlewState` |
| **L3 `InstanceActionRateRaDec` helper** ✅ | `MountInstance.cs` | Per-instance RA/Dec rate action; updates `SkyPredictor` and calls `SkyServer.SetTracking(this)`; mirrors `SkyServer.ActionRateRaDec` |
| **L4 `RateMovePrimaryAxis` setter migrated** ✅ | `MountInstance.cs` | Dispatches to `this.SkyQueueInstance`/`MountQueueInstance` Axis1; no longer routes via `SkyServer._defaultInstance` |
| **L5 `RateMoveSecondaryAxis` setter migrated** ✅ | `MountInstance.cs` | Dispatches to `this.SkyQueueInstance`/`MountQueueInstance` Axis2; Simulator correctly negates Y |
| **L6 `ParkSelected` get/set migrated** ✅ | `MountInstance.cs` | Reads/writes `_parkSelected` using this device's `_settings.ParkName/ParkAxes/ParkPositions` |
| **L7 `ApplyTracking` migrated** ✅ | `MountInstance.cs` | Delegates to Phase K `InstanceApplyTracking(bool)` |
| **L8 `SetSideOfPier` migrated** ✅ | `MountInstance.cs` | Per-instance pier flip via `_flipOnNextGoto`, `SlewRaDecAsync`/`SlewAltAzAsync`, `SkyServer.IsWithinFlipLimits` |
| **L9 `SetRateDec` / `SetRateRa` migrated** ✅ | `MountInstance.cs` | Sets `RateDec`/`RateRa` on this instance; calls `InstanceActionRateRaDec()` |
| **L10 `AbortSlewAsync` migrated** ✅ | `MountInstance.cs` | Cancels `_slewController`, `InstanceCancelAllAsync`, stops axes, restores tracking — all on `this`; no device-0 contamination |
| **L11 `CanMoveAxis` migrated** ✅ | `MountInstance.cs` | Reads `_settings.NumMoveAxis` from this device's settings |
| **L12 `DetermineSideOfPier` migrated** ✅ | `MountInstance.cs` | Uses `this.SideOfPier` and `AxesContext.FromSettings(_settings)` |
| **L13 `GoToHome` migrated** ✅ | `MountInstance.cs` | Uses `_homeAxes`, `AtHome`, `_slewState`; dispatches via `this.SlewAsync` |
| **L14 `GoToParkAsync` migrated** ✅ | `MountInstance.cs` | Uses `this.ParkSelected`, updates `_settings.ParkAxes/ParkName`; dispatches via `this.SlewAsync` |
| **L15 `EmergencyStop` fixed** ✅ | `MountInstance.cs` | Was `SkyServer.AbortSlewAsync(speak: false)` → `this.AbortSlewAsync(speak: false)` |

---

## 3. Remaining Work

All 🔴 Critical and 🟠 High items from the March 2026 assessment have been resolved.
The sections below are retained for historical context with updated status markers.

### 3.1 ✅ RESOLVED Legacy `SkyTasks(MountTaskName)` Deleted

**Verified April 2026.** Searching `SkyServer.Core.cs` for the no-parameter overload yields
zero results. Only the instance-aware `SkyTasks(MountTaskName taskName, MountInstance instance)`
exists. The misleading "removed" comment is also gone.

---

### 3.2 ✅ RESOLVED Legacy `AxesStopValidate()` (No Instance Parameter) Deleted

**Verified April 2026.** The commit "Phase A2 verified build clean" records this.
Only `AxesStopValidate(MountInstance instance)` exists in `SkyServer.Core.cs`.

---

### 3.3 ✅ RESOLVED `SkyQueue.RegisterInstance()` Removed

**Verified April 2026.** Zero occurrences of `RegisterInstance` in `MountInstance.cs`
or anywhere in the codebase. The static `SkyQueue` facade class has been deleted entirely
(see 3.5 / B6).

---

### 3.4 ✅ RESOLVED Pulse-Guide Callbacks Write to Instance Fields

**Verified April 2026.** Both branches in `MountInstance.MountStart()` now use:
```csharp
v => { _isPulseGuidingRa = v; },
v => { _isPulseGuidingDec = v; }
```
Simulator branch: lines 1595–1596. SkyWatcher branch: lines 1632–1633.

---

### 3.5 ✅ RESOLVED `PropertyChangedSkyQueue` / `PropertyChangedMountQueue` Deleted

**Verified April 2026.** Static facades (`SkyQueue`, `MountQueue`) are deleted; these
handlers have no event to subscribe to and are gone.

---

### 3.6 ✅ RESOLVED `StepsTimeFreq` Moved to Per-Instance Field

**Verified April 2026.**
- `MountInstance` has `internal long[] _stepsTimeFreq = { 0, 0 };` at line 63.
- `SkyServer.StepsTimeFreq` delegates: `get => _defaultInstance?._stepsTimeFreq ?? new long[] { 0, 0 }`.
- Instance-aware `SkyTasks(StepTimeFreq, instance)` writes to `instance._stepsTimeFreq`.

---

### 3.7 ✅ RESOLVED `_mountRunning` Static Field Removed

**Verified April 2026.** `SkyServer.IsMountRunning` is now:
```csharp
get => _defaultInstance?.IsMountRunning ?? false;
```
No independent `_mountRunning` static field exists.

---

### 3.8 ✅ RESOLVED `SnapPort1` / `SnapPort2` Delegate to Instance

**Verified April 2026.** Both delegate to `_defaultInstance._snapPort1/2`; backing fields
exist on `MountInstance` at lines 104–105.

---

### 3.9 ✅ RESOLVED `_goToAsyncLock` Field Removed

**Verified April 2026.** The field declaration is gone from `SkyServer.TelescopeAPI.cs`.
One residual `//lock (_goToAsyncLock)` comment remained at line 787 **removed (X1, 2026-04-03)** ✅

---

### 3.10 ✅ RESOLVED `LastDecDirection` Moved to Instance

**Verified April 2026.** `MountInstance` has `internal GuideDirection _lastDecDirection` at
line 106. `SkyServer.LastDecDirection` delegates get/set to `_defaultInstance._lastDecDirection`.

---

### 3.11 ✅ RESOLVED `SkySettingsBridge.cs` Deleted

**Verified April 2026.** `<Compile Remove="SkySettingsBridge.cs" />` was in the `.csproj`.
**Resolved (X2, 2026-04-03):** File deleted from disk; `<Compile Remove>` entry removed from `.csproj`. ✅

---

### 3.12 🟡 MEDIUM B5: Blazor Per-Device UI Notifications Not Implemented

**Status: elevated to 🔴 HIGH by 2026-04-04 smoke test. Confirmed as the primary cause of the Blazor UI showing device 0 for all devices.**

`TelescopeStateService` is the Blazor singleton that feeds all UI state. Its `UpdateState()` method reads 20+ properties exclusively from `SkyServer.*` all of which route to `_defaultInstance = MountInstanceRegistry.GetInstance(0)`. Every Blazor page shows device 0's coordinates, tracking state, slew state, and limits regardless of which device number the user is viewing.

**File:** `GreenSwamp.Alpaca.Server\Services\TelescopeStateService.cs`

```csharp
// UpdateState() ALL reads are device 0 only:
Altitude = SkyServer.Altitude,              // → _defaultInstance.Altitude
RightAscension = SkyServer.RightAscension,  // → _defaultInstance.RightAscension
Declination = SkyServer.Declination,        // → _defaultInstance.Declination
IsSlewing = SkyServer.IsSlewing,            // → _defaultInstance.IsSlewing
// ... 16+ more
```

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs`

```csharp
public static event PropertyChangedEventHandler StaticPropertyChanged;
```

All Blazor UI components subscribe to this single static event. Every `SkyServer.*`
property setter fires it. For multiple telescopes, every state change on any device
causes all Blazor components to refresh regardless of which device they are bound to.

`MountInstance` has no `PropertyChanged` / `InstancePropertyChanged` event of its own.

No per-device notification mechanism exists. Implementing it requires:
- Each `MountInstance` to have its own `PropertyChanged` event
- `TelescopeStateService` to become device-number-aware (or split to one service per device)
- Blazor components to bind to the specific `MountInstance` event for their device number
- The static `NotifyStepsChanged()` helper to be replaced or supplemented with
  per-device routing

---

### 3.13 🟡 MEDIUM Option C Phase 3: Config & UI (Partially Started)

**Status: F1 partially complete; F2/F3 not started.**

Per-device serial config fields (`Port`, `BaudRate`, `DataBits`, `Handshake`,
`ReadTimeout`, `DTREnable`, `RTSEnable`) already exist in the `Devices[]` array in
`appsettings.json` not inside `AlpacaDevices[]` as originally envisioned, but the
data is present per device. The Blazor connection status panel still needs per-device
controls (F2/F3).

---

### 3.14 ✅ RESOLVED G1: `RateDecOrg` / `RateRaOrg` Now Delegating Computed Properties

**Resolved 2026-04-03 (Phase G).** `MountInstance` backing fields (`_rateDecOrg`, `_rateRaOrg`) and instance properties already existed. Converted `SkyServer.TelescopeAPI.cs` properties to computed delegating form routing to `_defaultInstance?.RateDecOrg/RateRaOrg`. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` lines 538, 572

```csharp
public static double RateDecOrg { get; set; }   // ⚠️ true static auto-property
public static double RateRaOrg { get; set; }    // ⚠️ true static auto-property
```

These store the "original" tracking rate (before sign-correction direction is applied)
used by `ActionRateRaDec()`. For a second telescope, setting `RateDecOrg` on device 1
would silently overwrite the stored value for device 0.

**Impact:** Low these are momentary "previous value" helpers, not persistent state.
Written and read within the same `RateDec`/`RateRa` setter call sequence. A second
device running simultaneously could corrupt the direction logic for offset tracking
rates (`RaRateOrg` / `DecRateOrg`), but only during active guiding with non-zero
offset rates on both devices simultaneously.

**Fix applied (Phase G, 2026-04-03):** `MountInstance` backing fields already existed; converted `SkyServer.TelescopeAPI.cs` properties to computed delegating form. ✅

---

### 3.15 ✅ RESOLVED G2: `MountInstance` Parameter Threaded Through `SlewAxes→SlewMount→GoToAsync`

**Resolved 2026-04-03 (Phase G).** `MountInstance? instance = null` optional parameter added to `SlewAxes`, `SlewMount`, and `GoToAsync`. `var effectiveInstance = instance ?? _defaultInstance!` used throughout `GoToAsync`. `AutoHome` callers backward-compatible via default. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` line 763

```csharp
private static void GoToAsync(double[] target, SlewType slewState, EventWaitHandle goToStarted, bool tracking = false)
```

**Call chain:** `SlewAxes()` → `SlewMount()` → `GoToAsync` this is the **handpad
slew path** used by the Blazor HC panel. `GoToAsync` uses `_defaultInstance!` throughout
(lines 795, 835, 843, 985, 996) and would always operate on device 0 regardless of
which device the user intends to move.

**Fix applied:** `SlewAxes()`, `SlewMount()`, and `GoToAsync()` all now accept an optional `MountInstance? instance` parameter; `GoToAsync` uses `effectiveInstance` throughout.

**Note:** All primary ASCOM slew paths
already been migrated to `SlewController` (`_defaultInstance.SlewAsync/SlewSync`) and
are not affected. Only the axis-coordinate (handpad) slew path is impacted.

---

### 3.16 ✅ RESOLVED G3: `PulseGuideAltAz()` Moved to `MountInstance` Instance Method

**Resolved 2026-04-03 (Phase G).** New `internal void PulseGuideAltAz(...)` added to `MountInstance` in `#region AltAz Pulse Guide`. Uses `this.SkyPredictor`, `_monitorPulse` backing field (inaccessible via `SkyServer.MonitorPulse` `private get`), inlined `SiderealRate = 15.0410671786691`. `StopAltAzTrackingTimer()` made `internal static`. Four call sites in `SkyServer.TelescopeAPI.cs` updated to `_defaultInstance!.PulseGuideAltAz(...)`. Static method deleted. ✅

**Identified April 2026 (TelescopeAPI review).**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs` ~line 1877 (method deleted)

```csharp
private static void PulseGuideAltAz(int axis, double guideRate, int duration,
    Action<CancellationToken> pulseGoTo, CancellationToken token)
{
    Task.Run(() => {
        // ...
        _defaultInstance.SkyPredictor.Set(...);   // ⚠️ hardcoded to device 0
    });
}
```

ed for
`_isPulseGuidingRa/Dec`. For AltAz mounts only: calling `PulseGuide()` on device 1
would modify the `SkyPredictor` state of device 0.

**Impact:** AltAz mode only. GermanPolar/Polar modes route pulse-guide commands
directly to per-instance queue refs and are not affected.

**Fix applied (Phase G, 2026-04-03):** `PulseGuideAltAz` moved to `MountInstance` instance method; `this.SkyPredictor` used; static method deleted; four call sites updated. ✅

---

### 3.17 ✅ RESOLVED H1: `Controllers._ctsMount` Static Field (Simulator Cross-Device Contamination)

**Identified 2026-04-04 (two-device smoke test analysis). Resolved 2026-04-04 (Phase H).**

**File:** `GreenSwamp.Alpaca.Simulator\Controllers.cs`

`_ctsMount` made an instance field; initialised in the `Controllers` constructor. `Stop()` converted from `private static` to a non-static method. Two simulator instances now each own their own `CancellationTokenSource`. ✅

---

### 3.18 ✅ RESOLVED H2: Static `CheckSlewState()` / `CheckAxisLimits()` Called from Device 1's Timer

**Identified 2026-04-04 (two-device smoke test analysis). Resolved 2026-04-04 (Phase H).**

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

`CheckSlewState()` and `CheckAxisLimits()` added as private instance methods on `MountInstance`. `OnUpdateServerEvent()` call sites updated to call `this.CheckSlewState()` / `this.CheckAxisLimits()`. Device 1's slew state and axis limits are now updated by its own timer loop. ✅

---

### 3.19 ✅ RESOLVED H3: `MountStop()` Static Calls Affect Device 0 When Called from Device 1

**Identified 2026-04-04 (two-device smoke test analysis). Resolved 2026-04-04 (Phase H).**

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs`

`SkyServer.Tracking = false` replaced with `_trackingMode = TrackingMode.Off; _tracking = false;`.
`SkyServer.CancelAllAsync()` replaced with four per-instance CTS cancellations: `_ctsGoTo?.Cancel(); _ctsPulseGuideRa?.Cancel(); _ctsPulseGuideDec?.Cancel(); _ctsHcPulseGuide?.Cancel();`. ✅

---

### 3.20 ✅ RESOLVED H4: `SkyServer.ConnectAlignmentModel()` — Device-Awareness Verified

**Identified 2026-04-04 (two-device smoke test analysis). Resolved 2026-04-04 (Phase H).**

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs` line 1602

`ConnectAlignmentModel()` body is `// ToDo: Remove if not needed` — empty stub, no-op. No fix required. ✅

---

### 3.21 ✅ RESOLVED H5: `Mount.Simulator.Settings` Static Class — Last-Writer-Wins

**Identified 2026-04-04 (two-device smoke test analysis). Resolved 2026-04-04 (Phase H).**

**File:** `GreenSwamp.Alpaca.Simulator\Controllers.cs`

`AutoHomeAxisX` and `AutoHomeAxisY` added as instance fields on `Controllers`. Values captured from `Settings.*` in the constructor before the next device initialises and can overwrite them. `HomeSensorReset()` updated to use instance fields. ✅

---

### 3.22 ✅ RESOLVED Phase I: Static Methods Made Instance-Aware

**Identified 2026-04-04 (ConformU device-1 compliance failure). Resolved 2026-04-04 (Phase I).**

Four static helper methods operated exclusively on `_defaultInstance`, causing device 1 to be initialised with zero slew speeds, zero guide rates, and uninitialised tracking-offset state. Several `MountInstance` methods read static `SkyServer.*` properties instead of their own backing fields.

**Initialization fixes (I1–I5):**
- `SetSlewRates(double maxRate, MountInstance? instance = null)` — `Defaults()` and `MountConnect()` pass `this`
- `SetGuideRates(MountInstance? instance = null)` — `Defaults()` passes `this`
- `CalcCustomTrackingOffset(MountInstance? instance = null)` — `MountConnect()` passes `this`
- `Defaults()`: `_slewSettleTime = 0` (was `SkyServer.SlewSettleTime = 0`)
- `Defaults()`: `_parkSelected = found` (was `SkyServer.ParkSelected = found`)

**Runtime fixes (R1–R5):**
- `SkyPulseGoto`: both spin-wait loop checks use `_slewState` directly (×2)
- `SimGoTo` + `SkyGoTo`: read `_slewSettleTime` directly
- `PulseGuideAltAz`: `SkyServer.SetTracking(this)` routes to correct device
- `PulseGuideAltAz`: `_isPulseGuidingDec`/`_isPulseGuidingRa` read/written directly (×3)
- `PulseGuideAltAz`: `this.UpdateSteps()` replaces `SkyServer.UpdateSteps()`

All builds green. ✅

---

### 3.23 🔴 J1: `SouthernHemisphere` in Static `PulseGuide()` — Device 0 Only

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🔴.**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

The static `PulseGuide()` method reads `SkyServer.SouthernHemisphere` to determine Dec direction inversion for Polar and GermanPolar alignment modes:

```csharp
if (!SouthernHemisphere) decGuideRate = decGuideRate > 0 ? -Math.Abs(decGuideRate) : Math.Abs(decGuideRate);
```

`SkyServer.SouthernHemisphere` delegates to `_defaultInstance.Settings.Latitude < 0` — always device 0. If device 1 is configured at a different latitude that places it in the opposite hemisphere (e.g., device 0 is northern, device 1 is southern for test purposes), the Dec direction inversion for device 1's pulse guides will be wrong — guiding north when it should guide south.

**Impact:** Polar and GermanPolar pulse-guide commands deliver incorrect Dec corrections for device 1 when at a different hemisphere than device 0.

**Fix required:** Read `instance._settings.Latitude < 0` (or the per-instance `_southernHemisphere` field if one exists) within `PulseGuide()` for the active device.

---

### 3.24 🔴 J2: `AxesContext.FromSettings()` Reads Static `SouthernHemisphere` and `SideOfPier`

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🔴.**

**File:** `GreenSwamp.Alpaca.MountControl\AxesContext.cs:130–131`

`AxesContext.FromSettings(SkySettingsInstance settings)` receives the correct per-device settings but then reads two values from static `SkyServer.*`:

```csharp
SouthernHemisphere = SkyServer.SouthernHemisphere, // Still from SkyServer for now
SideOfPier = SkyServer.SideOfPier,
```

Both delegate to `_defaultInstance` (device 0). `SouthernHemisphere` is used in the coordinate conversion pipeline in `Axes.cs` for hemisphere inversion — a motor-control concern. When device 1 calls `AxesContext.FromSettings(device1Settings)`, it receives device 0's hemisphere flag and side-of-pier position, causing all coordinate conversions for device 1 to be computed against device 0's geometry.

**Impact:** All axis coordinate conversions for device 1 use device 0's hemisphere and pier state. Slew targets and tracking calculations will be wrong for a device 1 in a different hemisphere.

**Fix required:** `FromSettings()` should compute `SouthernHemisphere = settings.Latitude < 0` directly from the passed-in settings. `SideOfPier` should be passed in from the calling `MountInstance` context, not read from the static surface.

---

### 3.25 🟠 J3: Log Messages Do Not Identify the Device

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🟠.**

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs` (all `MonitorLog.LogToMonitor` call sites)

All `MonitorEntry` records emitted from `MountInstance.cs` use `Device = MonitorDevice.Server` with no device number or device name in the `Message` field. In a two-device session, log entries from device 0 and device 1 are completely indistinguishable. The `Method` field identifies the C# method but not which physical mount instance generated the entry.

**Impact:** Two-device troubleshooting is impossible from logs alone. When device 1 fails to slew or pulse-guide correctly, it cannot be determined from the log which device's entry is which.

**Fix required:** Prepend the device identifier (e.g., `DeviceNumber` or `_instanceName`) to each `Message` string in `MountInstance.cs` log entries. Example: `Message = $"[Dev{DeviceNumber}] {msg}"`.

---

### 3.26 🔴 J4: AltAz Tracking Timer Is Static — Shared Across All Devices

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🔴.**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.TelescopeAPI.cs`

Three declarations are `static` and share a single timer for all AltAz devices:

```csharp
private static MediaTimer? _altAzTrackingTimer;          // shared field
private static void StartAltAzTrackingTimer() { ... }    // starts shared timer
internal static void AltAzTrackingTimerEvent(object sender, EventArgs e)
{
    if (_altAzTrackingTimer?.IsRunning == true)
    {
        SetTracking();   // always calls _defaultInstance path
    }
}
```

The timer `Period` is read from `_settings!.AltAzTrackingUpdateInterval` (device 0's settings). `SetTracking()` routes to `_defaultInstance`. For two AltAz devices: (a) there is only one timer, (b) its interval is that of device 0, and (c) it only updates device 0's tracking rates. Device 1's AltAz tracking is never driven.

When G3 moved `PulseGuideAltAz` to `MountInstance`, `StopAltAzTrackingTimer()` was made `internal static` for access from `MountInstance`, but the underlying timer remains shared.

**Impact:** AltAz device 1 receives no tracking rate updates. Any AltAz telescope on device 1 will not track the sky.

**Fix required:** Move `_altAzTrackingTimer`, `StartAltAzTrackingTimer()`, and the timer event handler to `MountInstance` as instance members. Each `MountInstance` for an AltAz mount owns and drives its own timer, using `this._settings.AltAzTrackingUpdateInterval`.

---

### 3.27 🔴 J5: `SkyServer.LimitStatus` Is a Shared Static Struct

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🔴.**

**File:** `GreenSwamp.Alpaca.MountControl\SkyServer.cs:951`

```csharp
public static LimitStatusType LimitStatus = new LimitStatusType();
```

`LimitStatus` is a `public static` value-type struct. The axis limit *settings* (`HourAngleLimit`, `AxisLimitX`, `AxisUpperLimitY`, etc.) are correctly stored per-instance in `SkySettingsInstance` and read via `_settings` in `MountInstance.CheckAxisLimits()`. However, the limit *state* — which axes have exceeded their limits — is stored in this single shared struct.

For the AltAz case, `MountInstance.CheckAxisLimits()` reads `SkyServer.LimitStatus.AtLowerLimitAxisX` etc. to determine `meridianLimit` and `horizonLimit`. When device 0 and device 1 are both AltAz mounts with different physical axis extents, their limit states will overwrite each other in `LimitStatus`.

**Impact:** AltAz device 1's limit state contaminates device 0's limit state and vice versa. A limit hit on device 1 could incorrectly trigger limit enforcement on device 0.

**Fix required:** Add `_limitStatus` as an instance field on `MountInstance`. `CheckAxisLimits()` writes to `this._limitStatus`. For the AltAz read-back, the hardware callback that populates `LimitStatus` must be routed to the correct instance.

---

### 3.28 🟡 J6: UI-Only Residuals — Deferred to Phase E

**Identified 2026-04-04 (Group 3 re-assessment). Confirmed low risk. No change to priority.**

Several static reads on `SkyServer.*` that are consumed only by the Blazor UI (not by motor-control logic) remain routing to device 0. These include `SkyServer.SideOfPier` in `AxesContext.FromSettings` for display coordinate reads, `AutoHomeProgressBar`, and similar UI-only state statics that always show device 0's value.

**Impact:** Device 0's state is displayed for all devices in the Blazor UI — this is the same root cause as Phase E (`TelescopeStateService`). These residuals do not affect motor commands.

**Fix:** Deferred to Phase E. Once `TelescopeStateService` is made device-number-aware and Blazor components bind to per-device `MountInstance.InstancePropertyChanged`, these UI-only reads will be corrected as part of that work.

---

### 3.29 🔴 J7: Limit Enforcement Actions Command the Wrong Physical Mount

**Identified 2026-04-04 (Group 3 re-assessment). Previously classified low risk. Elevated to 🔴 — safety concern.**

**File:** `GreenSwamp.Alpaca.MountControl\MountInstance.cs` (limit action code in `CheckAxisLimits()`)

When `MountInstance.CheckAxisLimits()` detects a limit breach it calls:

```csharp
if (found == null) SkyServer.StopAxes(); else { _parkSelected = found; SkyServer.GoToPark(); }
```

`SkyServer.StopAxes()` and `SkyServer.GoToPark()` are static methods that route to `_defaultInstance` (device 0). When device 1 hits a physical axis limit, the limit enforcement stops device 0's motors and/or commands device 0 to park — leaving device 1's motors running and device 1 potentially driving past its hardware stop.

Each device is a physically distinct mount with its own hardware, motors, gear train, and power supply. Stopping the wrong device has no effect on the mount that is actually in danger.

**Impact:** Safety critical. A device 1 limit breach silently leaves device 1 unprotected and disrupts device 0 without cause.

**Fix required:** `CheckAxisLimits()` in `MountInstance` must call instance-level `StopAxes()` and `GoToPark()` equivalents (or accept a `MountInstance` reference) so that limit enforcement acts on the correct physical hardware.

---

## 4. Data-Flow Verification (Single Telescope — Current Operation)

The following paths have been code-verified as correct for single-telescope (April 2026):

```
Hardware steps
  → SkyWatcher executor (_stepsCallback)
  → MountInstance.ReceiveSteps(steps)            ✅ per-instance
  → _steps[0,1] set
  → SetSteps() — coordinate conversion          ✅ per-instance settings
  → _mountPositionUpdatedEvent.Set()            ✅ per-instance event
  → SkyServer.NotifyStepsChanged()              ✅ fires global Blazor event (Phase E caveat)

Hardware pulse-guide Ra/Dec
  → _pulseGuideRaCallback / _pulseGuideDecCallback
  → v => { _isPulseGuidingRa = v; }             ✅ writes directly to this instance (C1/C2 fixed)
  → v => { _isPulseGuidingDec = v; }            ✅ writes directly to this instance

ASCOM GoTo — primary path (SlewController, confirmed April 2026)
  → Telescope.cs GetInstance(_deviceNumber)     ✅ per-device routing
  → SkyServer.SlewRaDecAsync(ra, dec)           (static; uses SlewController)
  → SlewAsync(target, SlewType.SlewRaDec)       ✅ delegates to _defaultInstance.SlewAsync
  → MountInstance.SlewAsync(target, slewType)   ✅ per-instance SlewController

ASCOM GoTo — handpad path (GoToAsync/SlewMount, G2 resolved 2026-04-03)
  → SlewAxes(primary, secondary, slewState, instance)  ✅ MountInstance? param threaded (G2)
  → SlewMount(Vector, slewState, ..., instance)         ✅ effectiveInstance = instance ?? _defaultInstance!
  → GoToAsync(target, slewState, handle, instance)      ✅ uses effectiveInstance throughout

CTS cancellation (confirmed April 2026)
  → CancelAllAsync()                            ✅ _ctsGoTo/_ctsPulseGuide* are delegating
  → SkyServer.Core.cs:131-150                   ✅ get/set route to _defaultInstance backing fields

Mount position event wait (SkyPrecisionGoto)
  → _mountPositionUpdatedEvent.Reset/Wait       ✅ per-instance event

SetSlewRates / SetGuideRates / CalcCustomTrackingOffset (Phase I, 2026-04-04)
  → Defaults()/MountConnect() call with this    ✅ all rate fields written to correct instance
  → device 1 SlewSpeedEight/GuideRateRa/Dec    ✅ non-zero after connect (ConformU fix)
```

---

## 5. Static Code Still Requiring Removal — Prioritised Task List

### ✅ Phase A — Legacy Bridge Methods — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **A1** | `SkyServer.Core.cs` | Delete `SkyTasks(MountTaskName)` legacy overload | ✅ Done |
| **A2** | `SkyServer.Core.cs` | Delete `AxesStopValidate()` no-parameter overload | ✅ Done |
| **A3** | `SkyServer.Core.cs` | Delete misleading "removed" comment above A1 body | ✅ Done |

### ✅ Phase B — Remove `SkyQueue` / `MountQueue` Static Facades — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **B1** | `MountInstance.cs` | Remove `SkyQueue.RegisterInstance(sqImpl)` call | ✅ Done |
| **B2** | `SkyServer.Core.cs` | Delete `PropertyChangedSkyQueue` handler | ✅ Done |
| **B3** | `SkyServer.Core.cs` | Delete `PropertyChangedMountQueue` handler | ✅ Done |
| **B4** | `SkyCommands.cs` | Remove `[Obsolete]` shortcut ctors from all command classes | ✅ Done |
| **B5** | `SkyCommands.cs` | Remove `SkyCommandBase(long id)` etc. base class shortcut ctors | ✅ Done |
| **B6** | `SkyQueue.cs` | Delete `public static class SkyQueue` entirely | ✅ Done — file is 109 lines, `SkyQueueImplementation` only |
| **B7** | `MountQueue.cs` | Delete `public static class MountQueue` entirely | ✅ Done — file is 63 lines, `MountQueueImplementation` only |

### ✅ Phase C — Pulse-Guide Callback Routing — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **C1** | `MountInstance.cs` | Simulator `SetupCallbacks` pulse-guide lambdas write to `this` fields | ✅ Done (lines 1595–1596) |
| **C2** | `MountInstance.cs` | SkyWatcher `SetupCallbacks` pulse-guide lambdas write to `this` fields | ✅ Done (lines 1632–1633) |

### ✅ Phase D — Fix Remaining True Static Fields — COMPLETE

| Task | File | Description | Status |
|---|---|---|---|
| **D1** | `MountInstance.cs` | Add `internal long[] _stepsTimeFreq = { 0, 0 };` field | ✅ Done (line 63) |
| **D2** | `SkyServer.cs` | `StepsTimeFreq` delegates to `_defaultInstance._stepsTimeFreq` | ✅ Done |
| **D3** | `SkyServer.Core.cs` | Instance-aware `SkyTasks(StepTimeFreq, instance)` writes `instance._stepsTimeFreq` | ✅ Done |
| **D4** | `SkyServer.cs` | Remove `private static bool _mountRunning` | ✅ Done — `IsMountRunning` is `_defaultInstance?.IsMountRunning ?? false` |
| **D5** | `MountInstance.cs` | Add `internal bool _snapPort1`, `internal bool _snapPort2` fields | ✅ Done (lines 104–105) |
| **D6** | `SkyServer.cs` | `SnapPort1`/`SnapPort2` delegate to `_defaultInstance` | ✅ Done |
| **D7** | `SkyServer.TelescopeAPI.cs` | Remove `_goToAsyncLock` field | ✅ Done — field gone; residual `//lock (_goToAsyncLock)` comment at line 787 is cleanup |
| **D8** | `SkyServer.cs` | Move `LastDecDirection` to `MountInstance` as `_lastDecDirection` | ✅ Done |

### ✅ Phase G — `SkyServer.TelescopeAPI.cs` Residuals — COMPLETE (2026-04-03)

| Task | File | Description | Priority |
|---|---|---|---|
| **G1** | `SkyServer.TelescopeAPI.cs:538,572` | Move `RateDecOrg` / `RateRaOrg` to `MountInstance` backing fields; bridge via delegating properties | ✅ Done |
| **G2** | `SkyServer.TelescopeAPI.cs:763` / `SkyServer.Core.cs` | Add `MountInstance` parameter to `SlewAxes()` → `SlewMount()` → `GoToAsync()` chain (handpad slew path) | ✅ Done |
| **G3** | `SkyServer.TelescopeAPI.cs:~1877` | Move `PulseGuideAltAz()` to an instance method on `MountInstance`; call via `_defaultInstance!.PulseGuideAltAz(...)` | ✅ Done |

### ✅ Phase H — Simulator Static Contamination + Update Loop Static Calls — COMPLETE (2026-04-04)

| Task | File | Description | Status |
|---|---|---|---|
| **H1** ✅ | `GreenSwamp.Alpaca.Simulator\Controllers.cs:28` | `_ctsMount` made instance field; `Stop()` made non-static; constructor initialises per-instance CTS | ✅ Done |
| **H2** ✅ | `MountInstance.cs` | `CheckSlewState()` and `CheckAxisLimits()` added as private instance methods; `OnUpdateServerEvent()` call sites updated | ✅ Done |
| **H3** ✅ | `MountInstance.cs:1675–1676` | `SkyServer.Tracking = false` → `_trackingMode = TrackingMode.Off; _tracking = false;`; `SkyServer.CancelAllAsync()` → four per-instance CTS cancellations | ✅ Done |
| **H4** ✅ | `MountInstance.cs:1602` | `ConnectAlignmentModel()` is an empty stub — no fix required | ✅ No-op |
| **H5** ✅ | `GreenSwamp.Alpaca.Simulator\Controllers.cs` | `AutoHomeAxisX` and `AutoHomeAxisY` added as instance fields; values captured in constructor from `Settings.*` before next device can overwrite; `HomeSensorReset()` updated to use instance fields | ✅ Done |

### ✅ Phase I — Static Method Instance-Awareness — COMPLETE (2026-04-04)

Identified via ConformU device-1 compliance failure: `SetSlewRates`, `SetGuideRates`, `CalcCustomTrackingOffset`, and `SetTracking` all operated on `_defaultInstance` only; device 1 was never initialised with correct rates. `SkyPulseGoto`, `SimGoTo`, `SkyGoTo`, and `PulseGuideAltAz` read/wrote static `SkyServer.*` properties instead of `this` instance fields.

| Task | File | Description | Status |
|---|---|---|---|
| **I1** | `SkyServer.Core.cs`, `MountInstance.cs` | `SetSlewRates(double maxRate, MountInstance? instance = null)`; `var inst = instance ?? _defaultInstance`; all `_slewSpeedOne…Eight` writes use `inst.*`; `Defaults()` and `MountConnect()` pass `this` | ✅ Done |
| **I2** | `SkyServer.TelescopeAPI.cs`, `MountInstance.cs` | `SetGuideRates(MountInstance? instance = null)`; `inst.GuideRateRa/Dec`; `Defaults()` passes `this` | ✅ Done |
| **I3** | `SkyServer.Core.cs`, `MountInstance.cs` | `CalcCustomTrackingOffset(MountInstance? instance = null)`; reads `inst._stepsTimeFreq[n]`/`inst._stepsPerRevolution[n]`; writes `inst._trackingOffsetRate.X/Y` directly; `MountConnect()` passes `this` | ✅ Done |
| **I4** | `MountInstance.cs` | `Defaults()`: `_slewSettleTime = 0` (was `SkyServer.SlewSettleTime = 0`) | ✅ Done |
| **I5** | `MountInstance.cs` | `Defaults()`: `_parkSelected = found` (was `SkyServer.ParkSelected = found`) | ✅ Done |
| **R1** | `MountInstance.cs` | `SkyPulseGoto`: both spin-wait loop checks use `_slewState` from `this` (not device 0) ✅ | ✅ Done |
| **R2** | `MountInstance.cs` | `SimGoTo` + `SkyGoTo`: both read `_slewSettleTime` from `this` ✅ | ✅ Done |
| **R3** | `MountInstance.cs` | `PulseGuideAltAz`: `SkyServer.SetTracking(this)` — routes SetTracking to correct device | ✅ Done |
| **R4** | `MountInstance.cs` | `PulseGuideAltAz`: axis-0/1 guards and end-of-pulse reset use `_isPulseGuidingDec`/`_isPulseGuidingRa` directly (×3) | ✅ Done |
| **R5** | `MountInstance.cs` | `PulseGuideAltAz`: `this.UpdateSteps()` replaces `SkyServer.UpdateSteps()` | ✅ Done |

---

### ✅ Phase J — Group 3 Re-Assessment Items — MOSTLY COMPLETE (J3/J6 Remaining)

| Task | File | Description | Priority |
|---|---|---|---|
| **J1** | `SkyServer.TelescopeAPI.cs` | `PulseGuide()` reads `SkyServer.SouthernHemisphere` (device 0); read `instance._settings.Latitude < 0` for the active device | ✅ Done |
| **J2** | `AxesContext.cs:130-131` | `FromSettings()` reads static `SkyServer.SouthernHemisphere` and `SkyServer.SideOfPier`; compute `SouthernHemisphere = settings.Latitude < 0` from passed-in settings; pass `SideOfPier` from `MountInstance` | ✅ Done |
| **J3** | `MountInstance.cs` | All `MonitorEntry` records have no device identifier; prepend `[Dev{DeviceNumber}]` to `Message` at all `MonitorLog.LogToMonitor` call sites | 🟠 High |
| **J4** | `SkyServer.TelescopeAPI.cs` | `_altAzTrackingTimer`, `StartAltAzTrackingTimer()`, `AltAzTrackingTimerEvent` are static/shared; move to `MountInstance` as instance members | ✅ Done |
| **J5** | `SkyServer.cs:951` | `LimitStatus` is a shared static struct; add `_limitStatus` instance field to `MountInstance`; route AltAz hardware callbacks per-instance | ✅ Done |
| **J6** | `SkyServer.cs`, `AxesContext.cs` | UI-only reads of `SkyServer.SideOfPier` and `AutoHomeProgressBar` always show device 0 | 🟡 Deferred to Phase E |
| **J7** | `MountInstance.cs` | `CheckAxisLimits()` calls `SkyServer.StopAxes()` and `SkyServer.GoToPark()` (device 0); replace with instance-level equivalents | ✅ Done |

### ✅ Phase K — SlewController / SlewOperation Instance-Aware — COMPLETE (2026-04-05)

`SlewController` and its inner `SlewOperation` class were the last significant execution-path components still reading/writing state via static `SkyServer.*` properties, causing all GoTo and slew operations to be silently bound to device 0. Phase K introduces per-instance helper methods and rewires all state transitions and hardware dispatch through `operation.MountInstance`.

| Task | File | Description | Status |
|---|---|---|---|
| **K1** | `MountInstance.cs` | Add `InstanceSetTrackingMode()`, `InstanceApplyTracking(bool)`, `InstanceApplyTrackingDirect(bool, TrackingMode)`, `InstanceCompletePark()` helper methods | ✅ Done |
| **K2** | `SlewController.cs` | `SlewOperation.Prepare()`: reads initial RA/Dec/tracking from `MountInstance`; disables tracking via `InstanceApplyTracking(false)` | ✅ Done |
| **K3** | `SlewController.cs` | `SlewOperation.ExecuteMovementAsync()`: reads `MountInstance.Settings`; dispatches to `MountInstance.SimGoTo`/`SkyGoTo` | ✅ Done |
| **K4** | `SlewController.cs` | `SlewOperation.MarkComplete()`: sets `MountInstance._slewState`; restores tracking via `InstanceApplyTracking` | ✅ Done |
| **K5** | `SlewController.cs` | `SlewOperation.HandleCancellation()`: resets `_rateMoveAxes`, `_moveAxisActive`, `_slewState`; restores tracking — all on instance | ✅ Done |
| **K6** | `SlewController.cs` | `SlewOperation.HandleError()`: sets `_mountError`, `_slewState`; disables tracking — all on instance | ✅ Done |
| **K7** | `SlewController.cs` | `SlewOperation.CompleteRaDecSlewAsync()`: reads `Settings`, target RA/Dec, `_stepsPerRevolution` from instance; applies tracking via `InstanceApplyTrackingDirect` | ✅ Done |
| **K8** | `SlewController.cs` | `SlewOperation.CompleteAsync()`: SlewPark path uses `MountInstance.InstanceCompletePark()` | ✅ Done |
| **K9** | `SlewController.cs` | `ForceStopAxesAsync(MountInstance?)`: optional instance param; uses `instance.InstanceStopAxes()` with safe fallback to `SkyServer.StopAxes()` | ✅ Done |

---

### ✅ Phase L — Step 9 Bridge Region Migration — COMPLETE (2026-04-05)

The `#region Telescope API Bridge Methods (Step 8)` in `MountInstance.cs` was the root cause of all ConformU device-1 failures. Every action method in the region delegated back to `SkyServer` static methods that hardcode `_defaultInstance` (device 0). `DeclinationRate`, `RightAscensionRate`, `MoveAxis`, `Park`, `FindHome`, and `PulseGuide` guard checks all silently operated on device 0's hardware queue and CTS tokens. Phase L (Step 9) replaces all 12 failing bridge methods and adds 3 per-instance helpers. `EmergencyStop` is also fixed.

| Task | File | Description | Status |
|---|---|---|---|
| **L1** | `MountInstance.cs` | Add `InstanceCancelAllAsync()`: per-instance CTS cancel (`_ctsGoTo`, `_ctsPulseGuideRa/Dec`, `_ctsHcPulseGuide`) with 2 s spin-wait | ✅ Done |
| **L2** | `MountInstance.cs` | Add `InstanceSetRateMoveSlewState()`: per-instance `_moveAxisActive`, `_isSlewing`, `_slewState` from `_rateMoveAxes.X/Y` | ✅ Done |
| **L3** | `MountInstance.cs` | Add `InstanceActionRateRaDec()`: per-instance predictor update + `SkyServer.SetTracking(this)` | ✅ Done |
| **L4** | `MountInstance.cs` | `RateMovePrimaryAxis` setter: dispatch to `this.SkyQueueInstance`/`MountQueueInstance` Axis1 | ✅ Done |
| **L5** | `MountInstance.cs` | `RateMoveSecondaryAxis` setter: dispatch to `this.SkyQueueInstance`/`MountQueueInstance` Axis2; Simulator correctly negates Y | ✅ Done |
| **L6** | `MountInstance.cs` | `ParkSelected` get/set: read/write `_parkSelected` using this device's `_settings.ParkName/ParkAxes/ParkPositions` | ✅ Done |
| **L7** | `MountInstance.cs` | `ApplyTracking(bool)`: delegate to Phase K `InstanceApplyTracking(bool)` | ✅ Done |
| **L8** | `MountInstance.cs` | `SetSideOfPier(PointingState)`: per-instance pier flip via `_flipOnNextGoto`, `SlewRaDecAsync`/`SlewAltAzAsync`, `SkyServer.IsWithinFlipLimits` | ✅ Done |
| **L9** | `MountInstance.cs` | `SetRateDec(double)` / `SetRateRa(double)`: set on this instance; call `InstanceActionRateRaDec()` | ✅ Done |
| **L10** | `MountInstance.cs` | `AbortSlewAsync(bool)`: cancel `_slewController`, `InstanceCancelAllAsync`, stop axes, restore tracking — all on `this` | ✅ Done |
| **L11** | `MountInstance.cs` | `CanMoveAxis(TelescopeAxis)`: reads `_settings.NumMoveAxis` from this device | ✅ Done |
| **L12** | `MountInstance.cs` | `DetermineSideOfPier(double, double)`: uses `this.SideOfPier` and `AxesContext.FromSettings(_settings)` | ✅ Done |
| **L13** | `MountInstance.cs` | `GoToHome()`: uses `_homeAxes`, `AtHome`, `_slewState`; dispatches via `this.SlewAsync` | ✅ Done |
| **L14** | `MountInstance.cs` | `GoToParkAsync()`: uses `this.ParkSelected`, updates `_settings.ParkAxes/ParkName`; dispatches via `this.SlewAsync` | ✅ Done |
| **L15** | `MountInstance.cs` | `EmergencyStop()`: was `SkyServer.AbortSlewAsync(speak: false)` → `this.AbortSlewAsync(speak: false)` | ✅ Done |