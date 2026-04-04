# Settings CSV Files — Progress Report

**2026-04-03 18:33** → updated **2026-04-04 08:49**
**Status:** Phases SA, SB, SC, SF, SD complete — attributes, `SkySettings.cs` annotation, interface additions, `DeviceTemplates` + `ObservatoryDefaults` in `appsettings.json`, and B1/B2/B4 implementations done. Build verified ✅.
**Scope:** Review of Andy's four CSV files against `SkySettings.cs` and existing override templates — Phase 2 post-decision verification; MonitorSettings design from `monitor.json`
**Source files reviewed:** `T:\source\repos\SettingsExport\` — `SkyTelescope.csv`, `GermanPolarUnique.csv`, `PolarUnique.csv`, `AltAzUnique.csv`, `monitor.json`

---

## 1. Overall Assessment

### Phase 3 CSV Verification (fresh read — 2026-04-03 18:57)

| File | Lines | Phase 2 status | Phase 3 status |
|------|-------|----------------|----------------|
| `SkyTelescope.csv` | 98 | 🟡 `LimitPark` + `LimitTracking` still present | ✅ Both removed — all blockers cleared |
| `GermanPolarUnique.csv` | 25 | 🟡 `NoSyncPastMeridian` scope `User` | ✅ Scope corrected to `Application` |
| `PolarUnique.csv` | 25 | 🟡 `NoSyncPastMeridian` scope `User` | ✅ Scope corrected to `Application` |
| `AltAzUnique.csv` | 25 | 🟡 `NoSyncPastMeridian` scope `User` | ✅ Scope corrected to `Application` |

### Override template cross-check (new in Phase 2)

| Template | vs. CSV | Gap |
|----------|---------|-----|
| `germanpolar-overrides.json` | 23 properties in Overrides block | ❌ Missing `AxisTrackingLimit` |
| `polar-overrides.json` | 23 properties in Overrides block | ❌ Missing `AxisTrackingLimit` |
| `altaz-overrides.json` | 23 properties in Overrides block | ❌ Missing `AxisTrackingLimit` |
| `settings-override.schema.json` | 23 unique properties defined | ❌ Missing `AxisTrackingLimit` |

All decisions from Phase 1 and Phase 2 have been fully applied. **All CSV blockers for Phase SB are now cleared.** The only remaining gap is G1 (override templates + schema missing `AxisTrackingLimit`) — this is a code task, not a CSV task.

---

## 2. Phase 1 Decisions — Resolution Status

| Item | Description | Status | Action taken |
|------|-------------|--------|--------------|
| C1 | `AxisTrackingLimit` missing from GermanPolar and AltAz Unique CSVs | ✅ Resolved | Added to all three Unique CSVs, value `0.0`, scope `Application` |
| C2 | `AxisModelOffsets` missing from model | ✅ Resolved | Removed from solution and all CSVs — decision to remove entirely |
| C3 | `DisconnectOnPark`, `GlobalStopOn`, `TrackAfterUnpark` in CSV but not in model | ✅ Resolved | Removed from `SkyTelescope.csv` — WPF-era properties, not applicable to Blazor |
| C4 | `CanSetPierSide` in both `SkyTelescope.csv` and all Unique CSVs | ✅ Resolved | Removed from `SkyTelescope.csv`; remains only in Unique CSVs |
| D1 | `AltAzTrackingUpdateInterval` — Unique or Common? | ✅ Resolved | Common (`[CommonSetting]`) — repurposed as system-wide; scope `Configuration`, value `2500` |
| D2 | `NoSyncPastMeridian` — Unique or Common? | ✅ Resolved | Unique (`[UniqueSetting]`) — added to all three Unique CSVs, value `false` |
| D3 | `ParkName` — Unique or Common? | ✅ Resolved | Unique (`[UniqueSetting]`) — added to all three Unique CSVs, value `"Default"` |
| D4 | `LimitPark`, `LimitTracking`, `PolarMode` — remove from `SkyTelescope.csv` | ✅ Resolved | All three removed from `SkyTelescope.csv`; remain correctly in all three Unique CSVs |

---

## 3. Phase 3 — All CSV Decisions Resolved ✅

| Item | Description | Status |
|------|-------------|--------|
| B1 | `LimitPark` + `LimitTracking` removed from `SkyTelescope.csv` | ✅ Confirmed removed (line count 100 → 98) |
| G2 | `NoSyncPastMeridian` scope corrected to `Application` in all three Unique CSVs | ✅ Confirmed — all 24 properties in each Unique CSV now use `Application` scope |
| G3 | `BaudRate` type confirmed as `System.Int32`, value `9600` | ✅ Confirmed — `SkySettings.BaudRate` stays `int`, no code changes required |

**Phase SB is now unblocked from a CSV perspective.**

---

## 4. Secondary Gaps — Non-blocking for Phase SB, Required Before Runtime

### G1 ✅ `AxisTrackingLimit` added to all three override templates and schema

`AxisTrackingLimit: 0.0` added to `germanpolar-overrides.json`, `polar-overrides.json`, and `altaz-overrides.json` (each now has 24 Overrides properties). Property definition added to `settings-override.schema.json`. Build verified ✅.

### G2 ✅ `NoSyncPastMeridian` scope — resolved

All three Unique CSVs now show `NoSyncPastMeridian|System.Boolean|FALSE|Application`. All 24 properties in each Unique CSV consistently use `Application` scope. ✅

### G3 ✅ `BaudRate` type — resolved as `int32`

`SkySettings.BaudRate` stays as `public int BaudRate { get; set; }`. No code changes required.

- CSV corrected to `BaudRate|System.Int32|9600|User`
- `SkySettingsInstance.cs` load line `_baudRate = (SerialSpeed)settings.BaudRate;` is correct — direct cast is valid and lossless because `SerialSpeed` enum members have integer values matching actual baud rates (`ps9600 = 9600`, etc.)
- JSON settings file will store `"BaudRate": 9600` (numeric)

---

## 5. What Is Confirmed Complete

The following is correct and ready — no further changes needed:

- **Scope classification in `SkyTelescope.csv`**: The `Observatory` / `User` / `Configuration` classification is correct and maps directly to `[CommonSetting]` for all three groups.
- **`AltAzTrackingUpdateInterval`**: Correctly placed in `SkyTelescope.csv` as `Configuration` scope, value `2500`. Will be `[CommonSetting]`. ✅
- **All `Can...` properties except `CanSetPierSide`**: Correctly in `SkyTelescope.csv` with `Configuration` scope → `[CommonSetting]`. ✅
- **`CanSetPierSide`**: Correctly removed from `SkyTelescope.csv`; present in all three Unique CSVs with correct mode-specific values (TRUE/TRUE/FALSE). ✅
- **`NoSyncPastMeridian`**: Correctly in all three Unique CSVs with value `false` and scope `Application`. Also correctly in all three override templates and schema. ✅
- **`LimitPark` and `LimitTracking`**: Correctly removed from `SkyTelescope.csv`; present in all three Unique CSVs with `Application` scope and `FALSE` default. ✅
- **`ParkName`**: Correctly in all three Unique CSVs with value `"Default"`; also correctly in all three override templates and schema. ✅
- **`PolarMode`**: Correctly removed from `SkyTelescope.csv`; remains in all three Unique CSVs and templates with correct per-mode values. ✅
- **`AxisTrackingLimit`**: Correctly in all three Unique CSVs, value `0.0`. Templates/schema gap noted in G1 but CSVs are correct. ✅
- **`AxisModelOffsets`**: Correctly removed from all CSVs and from the solution. ✅
- **`DisconnectOnPark`, `GlobalStopOn`, `TrackAfterUnpark`**: Correctly removed from `SkyTelescope.csv`. ✅
- **Observatory properties** (`Latitude`, `Longitude`, `Elevation`, `UTCOffset`): Correct `Observatory` scope in `SkyTelescope.csv`. ✅
- **Override template structure**: All three templates are internally consistent and match their respective Unique CSVs for all 24 properties. `AxisTrackingLimit: 0.0` added (G1). ✅
- **Out-of-scope CSVs** (`Alignment.csv`, `Server.csv`, `Gamepad.csv`, `Focuser.csv`, `Profile.csv`): Correctly excluded. ✅
- **`MonitorSettings.cs` defaults**: `Warning=true`, `Error=true`, `LogMonitor=false` corrected to match `monitor.json` (M1–M3). Build verified. ✅
- **`MonitorSettings` in `Templates\appsettings.json`**: 20-property section added at root level (M4). ✅
- **`MonitorSettings` in `Server\appsettings.json`**: 20-property section added at root level (M5). ✅

---

## 6. Phase Readiness Summary

| Phase | Can start now? | Blocked by |
|-------|:-:|---|
| **SA** — New attribute files + `ObservatorySettings` model | ✅ Done | 3 new files created; build verified |
| **SC** — Add 4 new methods to interface (interface only) | ✅ Done | Methods added to interface; stubs in `VersionedSettingsService`; build verified |
| **SB** — Annotate `SkySettings.cs` | ✅ Done | 97 `[CommonSetting]` + 24 `[UniqueSetting]` applied; build verified |
| **SD** — Implement B1, B2, B4 in `VersionedSettingsService` | ✅ Done | B1 (CreateDeviceForModeAsync), B2 (ChangeAlignmentModeAsync), B4 (GetObservatorySettings + SaveObservatorySettingsAsync) implemented; build verified |
| **SF** — Populate `DeviceTemplates` + `ObservatoryDefaults` in `appsettings.json` | ✅ Done | 3 mode templates (121 props each) + ObservatoryDefaults added; build verified |
| **Monitor/other settings** | ✅ Design complete | `monitor.json` analysed — 3 model default fixes + 2 `appsettings.json` gaps (M1–M5 code tasks, see Section 9) |

---

## 7. Andy's Action List

| Priority | Action | Where |
|----------|--------|-------|
| ✅ Done — G1 | `AxisTrackingLimit: 0.0` added to all three override templates and schema | `GreenSwamp.Alpaca.Settings\Templates\` |
| ✅ Done — M1–M3 | `Warning=true`, `Error=true`, `LogMonitor=false` defaults corrected | `GreenSwamp.Alpaca.Settings\Models\MonitorSettings.cs` |
| ✅ Done — M4 | `MonitorSettings` section (20 props) added | `GreenSwamp.Alpaca.Settings\Templates\appsettings.json` |
| ✅ Done — M5 | `MonitorSettings` section (20 props) added | `GreenSwamp.Alpaca.Server\appsettings.json` |
| ✅ Done — SA | New `Attributes\CommonSettingAttribute.cs`, `Attributes\UniqueSettingAttribute.cs`, `Models\ObservatorySettings.cs` | `GreenSwamp.Alpaca.Settings\` |
| ✅ Done — SB | Annotated all 121 `SkySettings.cs` properties (97 common, 24 unique) | `GreenSwamp.Alpaca.Settings\Models\SkySettings.cs` |
| ✅ Done — SC | 4 new methods added to `IVersionedSettingsService`; stubs in `VersionedSettingsService` | `GreenSwamp.Alpaca.Settings\Services\` |
| ✅ Done — SD | Implement B1 (CreateDeviceForMode), B2 (ChangeAlignmentMode), B4 (GetObservatory/Save) | `GreenSwamp.Alpaca.Settings\Services\VersionedSettingsService.cs` |

---

## 8. Property Classification Summary (Phase 2 state)

### Confirmed `[CommonSetting]` — Observatory sub-group
`Latitude`, `Longitude`, `Elevation`, `UTCOffset`

### Confirmed `[CommonSetting]` — Configuration/Capability sub-group
All `Can...` properties **except** `CanSetPierSide`
`SiderealRate`, `LunarRate`, `SolarRate`, `KingRate`, `DisplayInterval`, `AltAzTrackingUpdateInterval`

### Confirmed `[UniqueSetting]`
`AlignmentMode`, `AtPark`, `AutoHomeAxisX`, `AutoHomeAxisY`, `AxisHzTrackingLimit`,
`AxisLimitX`, `AxisLowerLimitY`, `AxisTrackingLimit`, `AxisUpperLimitY`, `CanSetPierSide`,
`HomeAxisX`, `HomeAxisY`, `HourAngleLimit`, `HzLimitPark`, `HzLimitTracking`,
`LimitPark`, `LimitTracking`, `NoSyncPastMeridian`, `ParkAxes`, `ParkHzLimitName`,
`ParkLimitName`, `ParkName`, `ParkPositions`, `PolarMode`

### Confirmed `[CommonSetting]` — User sub-group (everything else in `SkyTelescope.csv`)
All remaining User-scope properties in `SkyTelescope.csv` not listed above.
Notable members: `AllowAdvancedCommandSet`, `AlternatingPPEC`, `ApertureArea`, `ApertureDiameter`,
`AutoTrack`, `BaudRate` (`int` — resolved G3),
`DataBits`, `DecBacklash`, `DecPulseToGoTo`, `DisableKeysOnGoTo`, `DTREnable`, `EncodersOn`,
`EquatorialCoordinateType`, `EyepieceFS`, `FocalLength`, `FullCurrent`, `GotoPrecision`,
`GpsBaudRate`, `GpsPort`, `GuideRateOffsetX/Y`, `HandShake`, `HcAntiDec/Ra`, `HcFlipEW/NS`,
`HCMode`, `HcPulseGuides`, `HcSpeed`, `InstrumentDescription`, `InstrumentName`,
`MaximumSlewRate`, `MinPulseDec/Ra`, `Mount`, `NumMoveAxis`, `Pec360File`, `PecMode`,
`PecOffSet`, `PecOn`, `PecWormFile`, `PolarLedLevel`, `Port`, `PpecOn`, `RaBacklash`,
`RATrackingOffset`, `ReadTimeout`, `Refraction`, `RTSEnable`, `St4Guiderate`,
`SyncLimit`, `SyncLimitOn`, `Temperature`, `TraceLogger`, `TrackingRate`

---

---

## 9. MonitorSettings Design — Analysis Complete

**Source:** `T:\source\repos\SettingsExport\monitor.json` (received 2026-04-03 19:11)

### Architecture placement

MonitorSettings is **server-wide** — not per-device — and is completely independent of `SkySettings.cs`. The `[CommonSetting]`/`[UniqueSetting]` attribute system does **not** apply to it. It lives under a `"MonitorSettings"` key in:

- **App-wide defaults:** `GreenSwamp.Alpaca.Server\appsettings.json` — root level
- **User overrides:** `%AppData%\GreenSwampAlpaca\{version}\appsettings.user.json` — root level

Placement confirmed by `GreenSwamp.Alpaca.Settings\docs\MONITOR_SETTINGS_GUIDE.md`.

### Model status

`GreenSwamp.Alpaca.Settings\Models\MonitorSettings.cs` — **exists and is complete.** All 21 properties in 4 groups match `monitor.json` exactly by name. No new properties are required.

### Property groups (21 total)

| Group | Properties | Count |
|-------|------------|-------|
| MonitorDevice filters | `ServerDevice`, `Telescope`, `Ui` | 3 |
| MonitorCategory filters | `Other`, `Driver`, `Interface`, `Server`, `Mount`, `Alignment` | 6 |
| MonitorType filters | `Information`, `Data`, `Warning`, `Error`, `Debug` | 5 |
| Logging options | `LogMonitor`, `LogSession`, `LogCharting`, `StartMonitor` | 4 |
| Miscellaneous | `Language`, `LogPath`, `Version` | 3 |

### Default value cross-check — `monitor.json` vs `MonitorSettings.cs`

| Property | `monitor.json` (Andy's intent) | `MonitorSettings.cs` default | Match? |
|----------|-------------------------------|------------------------------|--------|
| `ServerDevice` | `true` | `true` | ✅ |
| `Telescope` | `true` | `true` | ✅ |
| `Ui` | `false` | `false` | ✅ |
| `Other` | `false` | `false` | ✅ |
| `Driver` | `true` | `true` | ✅ |
| `Interface` | `true` | `true` | ✅ |
| `Server` | `true` | `true` | ✅ |
| `Mount` | `true` | `true` | ✅ |
| `Alignment` | `false` | `false` | ✅ |
| `Information` | `true` | `true` | ✅ |
| `Data` | `false` | `false` | ✅ |
| `Warning` | **`true`** | **`false`** | ❌ |
| `Error` | **`true`** | **`false`** | ❌ |
| `Debug` | `false` | `false` | ✅ |
| `LogMonitor` | **`false`** | **`true`** | ❌ |
| `LogSession` | `true` | `true` | ✅ |
| `LogCharting` | `false` | `false` | ✅ |
| `StartMonitor` | `true` | `true` | ✅ |
| `Language` | `"en-US"` | `"en-US"` | ✅ |
| `LogPath` | `""` | `""` | ✅ |
| `Version` | `"0"` | `"0"` | ✅ — exclude from persistence (legacy marker) |

**3 default mismatches require correction (M1–M3):**

- `Warning`: correct to `= true` — warnings should be on by default
- `Error`: correct to `= true` — errors should be on by default
- `LogMonitor`: correct to `= false` — monitor file logging is opt-in, not default

**`Version` note:** The `"0"` value is a legacy migration marker — exclude it from `appsettings.json` population. It is not a meaningful user-facing setting.

### `appsettings.json` gaps

| File | Status |
|------|--------|
| `GreenSwamp.Alpaca.Settings\Templates\appsettings.json` | ❌ `MonitorSettings` section absent — needs adding (M4) |
| `GreenSwamp.Alpaca.Server\appsettings.json` | ❌ `MonitorSettings` section absent — needs adding (M5) |

Both files need a `"MonitorSettings"` block added at root level using Andy's `monitor.json` values, excluding `Version`.

### Code tasks for MonitorSettings

| ID | Action | File | Priority |
|----|--------|------|----------|
| M1 | Correct `Warning` default to `= true` | `GreenSwamp.Alpaca.Settings\Models\MonitorSettings.cs` | ✅ Done |
| M2 | Correct `Error` default to `= true` | `GreenSwamp.Alpaca.Settings\Models\MonitorSettings.cs` | ✅ Done |
| M3 | Correct `LogMonitor` default to `= false` | `GreenSwamp.Alpaca.Settings\Models\MonitorSettings.cs` | ✅ Done |
| M4 | Add `MonitorSettings` section (20 properties, no `Version`) | `GreenSwamp.Alpaca.Settings\Templates\appsettings.json` | ✅ Done |
| M5 | Add `MonitorSettings` section (20 properties, no `Version`) | `GreenSwamp.Alpaca.Server\appsettings.json` | ✅ Done |

---

*Document created: 2026-04-03 15:48 | Last updated: 2026-04-03 22:29*
*Author: GitHub Copilot (GreenSwamp Alpaca workspace)*
*Next actions: Phases S1–S8 (per-device file split, DeviceSynchronizationService removal, full interface redesign) — see `SettingsArchitecture-Redesign-Requirements.md`.*
