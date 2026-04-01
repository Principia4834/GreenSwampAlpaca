# Code Flow Analysis: `MapSlewTargetToAxes` — SlewRaDec / GermanPolar / SkyWatcher / Northern Hemisphere

**Prepared for offline review**  
**Date:** 2026-04-01  
**Subject file:** `GreenSwamp.Alpaca.MountControl/MountInstance.cs` (line 1055)  
**Scope:** Restricted to `slewType = SlewRaDec`, `alignmentMode = GermanPolar`, `mountType = SkyWatcher`, Northern Hemisphere only.

---

## 1. Entry Point

```csharp
// MountInstance.cs ~line 1055
public double[] MapSlewTargetToAxes(double[] target, SlewType slewType)
{
    var context = AxesContext.FromSettings(_settings);

    switch (slewType)
    {
        case SlewType.SlewRaDec:
            target = Axes.RaDecToAxesXy(target, context);
            // NOTE: GetSyncedAxes call is commented out here
            break;
        ...
    }
    return target;
}
```

**Input contract:** `target[0]` = RA in decimal hours (0–24), `target[1]` = Dec in decimal degrees (−90 to +90).  
**Output:** Mount-native axis position in degrees, ready to pass to `SkyAxisGoToTarget`.

---

## 2. Step 1 — Context Construction: `AxesContext.FromSettings(_settings)`

### Source: `AxesContext.cs` lines 115–134

```csharp
public static AxesContext FromSettings(SkySettingsInstance settings)
{
    return new AxesContext
    {
        AlignmentMode     = settings.AlignmentMode,          // GermanPolar (confirmed by scope)
        MountType         = settings.Mount,                  // SkyWatcher (confirmed by scope)
        Latitude          = settings.Latitude,               // e.g. +51.5 for London
        SouthernHemisphere = SkyServer.SouthernHemisphere,  // ⚠ reads static, not from settings
        PolarMode         = settings.PolarMode,              // Not used in GermanPolar path
        LocalSiderealTime = null,                            // Lazy — fetched later via SkyServer.SiderealTime
        SideOfPier        = SkyServer.SideOfPier,           // ⚠ reads static, from _defaultInstance
        AppAxisX          = null,                            // Lazy — fetched later via SkyServer.AppAxisX
        AppAxisY          = null,                            // Lazy — fetched later via SkyServer.AppAxisY
        AxisLimitX        = settings.AxisLimitX,
        HourAngleLimit    = settings.HourAngleLimit,
        IsWithinFlipLimitsFunc = null                        // Uses inline AxesContext.IsWithinFlipLimits
    };
}
```

### Settings Dependencies and Ambiguities

| Context Field | Source | Risk |
|---|---|---|
| `AlignmentMode` | `_settings.AlignmentMode` (per-instance) | ✅ Instance-correct |
| `MountType` | `_settings.Mount` (per-instance) | ✅ Instance-correct |
| `Latitude` | `_settings.Latitude` (per-instance) | ✅ Instance-correct |
| `SouthernHemisphere` | `SkyServer.SouthernHemisphere` → `_settings!.Latitude < 0` on `_defaultInstance` | ⚠ **Static singleton** — pulls from slot-0 instance, not `this` instance |
| `LocalSiderealTime` | `null` — lazy via `SkyServer.SiderealTime` at use time | ⚠ **Static singleton** — sees slot-0 time |
| `SideOfPier` | `SkyServer.SideOfPier` — computed from `_defaultInstance._appAxes.Y` | ⚠ **Static singleton** — sees slot-0 axis state |
| `AxisLimitX` | `_settings.AxisLimitX` (per-instance) | ✅ Instance-correct |
| `HourAngleLimit` | `_settings.HourAngleLimit` (per-instance) | ✅ Instance-correct |

> **Key concern (single-device):** For a single-device deployment the default instance is `this` instance, so all static reads are effectively correct. The ambiguity only becomes a real bug in a future multi-device scenario.

### SkyServer.SideOfPier (static getter, GermanPolar / Northern Hemisphere)

```csharp
// SkyServer.TelescopeAPI.cs line 215–226
case AlignmentMode.GermanPolar:
    if (!SouthernHemisphere)
        sideOfPier = (AppAxisY < 90.0000000001 && AppAxisY > -90.0000000001)
            ? PointingState.Normal        // Dec axis within ±90° → pointing east (pre-meridian)
            : PointingState.ThroughThePole; // outside → pointing west (post-meridian, through pole)
```

`SkyServer.AppAxisY` delegates to `_defaultInstance._appAxes.Y`, which is the **app-space** Y axis (Dec axis), updated in `MountInstance.SetSteps()` → `AxesMountToApp()`.

For GermanPolar + SkyWatcher + Northern hemisphere, `AxesMountToApp` transforms the raw Y axis:

```csharp
// Axes.cs line 200–210
case MountType.SkyWatcher (Northern):
    a[1] = 180 - a[1];   // e.g. raw 90° → app 90°, raw 0° → app 180°, raw 170° → app 10°
```

Therefore app-space Y of 0–90° corresponds to the telescope pointing west of meridian (through-the-pole side), while 90–270° covers east of meridian.

**At startup / before first position update:** `_appAxes.Y` is initialized from `_homeAxes.Y` via `MountReset()` → `GetHomeAxes()`. If this hasn't been called yet, `_appAxes.Y` defaults to `0.0` (struct default), giving `SideOfPier = Normal`. This is a **potential initialization ambiguity** — see Section 7.

---

## 3. Step 2 — `Axes.RaDecToAxesXy(target, context)`

### Source: `Axes.cs` lines 471–476

```csharp
internal static double[] RaDecToAxesXy(IReadOnlyList<double> raDec, AxesContext context)
{
    double lst = context.GetLst();           // → SkyServer.SiderealTime (static)
    return RaDecToAxesXyCore(raDec, useLst: true, lst, context);
}
```

`context.GetLst()` returns `LocalSiderealTime ?? SkyServer.SiderealTime`. Since `LocalSiderealTime` is null in the context (lazy), it reads `SkyServer.SiderealTime`, which delegates to `_defaultInstance._siderealTime`, updated each timer tick in `OnUpdateServerEvent`.

**Potential issue:** If called before the first timer tick fires (e.g., immediately after `MountStart()`), `_siderealTime` could be `0.0`. `GetLocalSiderealTime()` is called at the start of each `OnUpdateServerEvent` and stored in the instance field. A race exists between `MountConnect()` completion and the first tick.

---

## 4. Step 3 — `RaDecToAxesXyCore` (GermanPolar branch)

### Source: `Axes.cs` lines 520–658 (Polar/GermanPolar case)

The full transformation pipeline for our scope conditions:

### 4.1 Convert RA to Hour Angle (degrees)

```csharp
// useLst = true
axes[0] = 15.0 * (lst - axes[0]);   // HA in degrees: (LST - RA) × 15
axes[0] = Range.Range360(axes[0]);  // Normalise to [0, 360)
```

- **Input:** `axes[0]` = target RA in hours (e.g., 6.0h)  
- **LST example:** 8.0h → `(8.0 - 6.0) × 15 = 30°` (positive, east of meridian)  
- **LST example:** 4.0h → `(4.0 - 6.0) × 15 = -30° → 330°` (post Range360, west of meridian)  
- `axes[1]` = target Dec (unchanged at this stage, e.g., +45°)

### 4.2 Southern hemisphere Dec inversion (skipped — Northern hemisphere)

```csharp
if (context.SouthernHemisphere)
    axes[1] = -axes[1];   // NOT executed for Northern hemisphere
```

### 4.3 Through-the-pole test

```csharp
if (axes[0] > 180.0)
{
    axes[0] += 180;         // e.g. 330 → 510
    axes[1] = 180 - axes[1]; // e.g. 45 → 135
}
```

This mirrors the axis into the through-the-pole (western) pointing state.

- **HA < 180° (east of meridian / pre-meridian):** No adjustment. Mount will be pointing Normal (scope east of pier, counterweight west).  
- **HA > 180° (west of meridian / post-meridian):** Adjustment applied. Mount will be in ThroughThePole state (scope west, counterweight east).

After this step, `axes[0]` can be > 360°, which is handled next.

### 4.4 Normalisation: `Range.RangeAxesXy(axes)`

```csharp
axes = Range.RangeAxesXy(axes);
// axes[0] = Range360(axes[0])   → [0, 360)
// axes[1] = Range270(axes[1])   → [-90, 270)
```

After normalisation, `axes[0]` is always in [0, 360) and `axes[1]` in [−90, 270).

**App-space interpretation after normalisation (Northern / SkyWatcher GEM):**
- `axes[1]` in [−90, 90] → scope pointing Normal (pre-meridian / east of pier)
- `axes[1]` in (90, 270) → scope pointing ThroughThePole (post-meridian / west of pier)

### 4.5 Convert App axes to Mount axes: `AxesAppToMount(axes, context)`

```csharp
// Axes.cs line 82–93 — SkyWatcher, GermanPolar, Northern hemisphere:
a[0] = a[0];          // RA axis unchanged
a[1] = 180 - a[1];   // Dec axis flip: app 90° → mount 90°, app 0° → mount 180°, app 135° → mount 45°
```

**Effect:** The Dec axis is mirrored around 90°. This is the key hardware coordinate transform for a SkyWatcher GEM in the Northern hemisphere.

The mount-native Y axis values returned to the caller are therefore:
- Pre-meridian target: `axes[1]` ∈ [−90, 90] in app space → `180 − axes[1]` ∈ [90, 270] in mount space
- Post-meridian (TTP) target: `axes[1]` ∈ (90, 270) in app space → `180 − axes[1]` ∈ (−90, 90) in mount space

### 4.6 Alternate Position Selection: `SkyServer.GetAlternatePosition(axes)`

```csharp
// skipAlternatePosition = false for SlewRaDec (default)
alt = SkyServer.GetAlternatePosition(axes);
var finalAxes = (alt is null) ? axes : alt;
```

This calls `GetAlternatePositionGEM(position)`:

```csharp
private static double[] GetAlternatePositionGEM(double[] position)
{
    if (!IsWithinFlipLimits(position)) { return null; }   // Primary not within limit → no alternative needed

    var context = AxesContext.FromSettings(_settings);     // ⚠ Second static context read
    var alt = Axes.GetAltAxisPosition(position, context);  // Compute the 180° flip alternative

    if (!IsWithinFlipLimits(alt)) { return null; }        // Alternative not within limit → no swap

    var cl = ChooseClosestPosition(ActualAxisX, position, alt); // ⚠ reads static ActualAxisX

    if (FlipOnNextGoto)                                    // ⚠ reads static FlipOnNextGoto
    {
        cl = cl == "a" ? "b" : "a";                       // Force the far position (forced pier flip)
    }

    if (cl != "b") { return null; }   // Primary is already closer → use primary
    return alt;                        // Alternate is closer → use alternate (natural pier flip)
}
```

#### `IsWithinFlipLimits(position)` — GermanPolar

```csharp
// SkyServer.TelescopeAPI.cs line 3005–3007
case AlignmentMode.GermanPolar:
    return -_settings!.HourAngleLimit < absPos0 && absPos0 < _settings!.HourAngleLimit ||
           180 - _settings!.HourAngleLimit < absPos0 && absPos0 < 180 + _settings!.HourAngleLimit;
```

`absPos0 = Math.Abs(position[0])` — i.e., the absolute value of the X axis (RA axis) in **mount** space.  
Returns `true` if the target is near the meridian (±HourAngleLimit degrees) or near the 180° through-pole point (180 ± HourAngleLimit degrees).

**HourAngleLimit source:** `_settings!` (static) → reads from `_defaultInstance.Settings.HourAngleLimit`. For a single device this is consistent with the `this._settings.HourAngleLimit` used in context construction.

#### `GetAltAxisPosition` — GermanPolar

```csharp
// Axes.cs line 307–317
case AlignmentMode.GermanPolar:
    if (alt[0] > 90)
    {
        d[0] = alt[0] - 180;
        d[1] = 180 - alt[1];
    }
    else
    {
        d[0] = alt[0] + 180;
        d[1] = 180 - alt[1];
    }
```

This produces the 180° counterpart (the other pier side position). **Note:** this operates on mount-native axis values, not app-space values.

#### `ChooseClosestPosition` — uses `SkyServer.ActualAxisX`

```csharp
private static string ChooseClosestPosition(double position, ...)
```

`ActualAxisX` reads `_defaultInstance._actualAxisX` — the current RA hardware axis position in degrees, updated in `SetSteps()`.

**Single-instance risk:** If `this` is not the default instance, the current RA position used to choose the closest slew direction belongs to a different device. For the current single-device architecture this is benign.

#### `FlipOnNextGoto` — reads static `_defaultInstance._flipOnNextGoto`

Set by the user via the UI or automatically in `SkyServer.SideOfPier` setter when a flip is requested. This flag overrides the natural closest-position selection and forces the alternate (flipped) position. After the GoTo completes it is expected to be cleared.

---

## 5. Side of Pier: Full Flow for SlewRaDec / GermanPolar / Northern

### 5.1 How SideOfPier enters the flow

`AxesContext.FromSettings` captures `SkyServer.SideOfPier` into `context.SideOfPier` at context construction time. In `RaDecToAxesXyCore` this field is **not directly used** to choose the target side. Instead, the through-the-pole branch (step 4.3) and `GetAlternatePositionGEM` (step 4.6) together determine which side the target will be on.

`context.SideOfPier` is used explicitly only in `Axes.IsFlipRequired()`, which is called by `SkyServer.DetermineSideOfPier()` — a separate API not involved in this flow.

### 5.2 How the target side is determined (implicit)

The through-the-pole branch (`if axes[0] > 180.0`) partitions targets:

| HA (degrees after Range360) | Through-pole branch taken? | App axes[1] after branch | Physical pointing |
|---|---|---|---|
| 0–180 (pre-meridian, east) | No | Dec in [−90, 90] | Normal (scope east) |
| 180–360 (post-meridian, west) | Yes | Dec in [90, 270] | ThroughThePole (scope west) |

After `AxesAppToMount` the mount Y-axis command values are:

| App axes[1] range | Mount axes[1] = 180 − app | Physical side |
|---|---|---|
| −90 to 90 | 90 to 270 | Normal for Northern SkyWatcher GEM |
| 90 to 270 | −90 to 90 | ThroughThePole for Northern SkyWatcher GEM |

> **Inversion:** `CalculateSideOfPier` (in `Axes.cs` line 766–780) confirms this: for GermanPolar + SkyWatcher + Northern, `isWithinDecRange` (mount Y ∈ (−90, 90)) means `ThroughThePole`, and outside that range means `Normal`. This is consistent with the physical reality of a GEM counterweight-down configuration.

### 5.3 Forced flip path

If `FlipOnNextGoto = true`, `GetAlternatePositionGEM` inverts the closest-position choice, causing `MapSlewTargetToAxes` to return the alternate (flipped) axis position for the same RA/Dec target. After the goto this flag is **not automatically cleared** by `MapSlewTargetToAxes` — it must be cleared by the caller or by the slew completion logic.

**Ambiguity:** `FlipOnNextGoto` is a static property reading from `_defaultInstance`. If a non-default instance calls `MapSlewTargetToAxes`, the flip flag is shared, not per-instance. This is a multi-device isolation gap.

---

## 6. Full Dependency Map (Northern / SkyWatcher GEM / SlewRaDec)

```
MapSlewTargetToAxes(target, SlewRaDec)
│
├─ AxesContext.FromSettings(_settings)
│   ├─ _settings.AlignmentMode          → per-instance ✅
│   ├─ _settings.Mount                  → per-instance ✅
│   ├─ _settings.Latitude               → per-instance ✅
│   ├─ _settings.AxisLimitX             → per-instance ✅
│   ├─ _settings.HourAngleLimit         → per-instance ✅
│   ├─ SkyServer.SouthernHemisphere     → static _defaultInstance ⚠
│   └─ SkyServer.SideOfPier             → static _defaultInstance._appAxes.Y ⚠
│
└─ Axes.RaDecToAxesXy(target, context)
    │
    ├─ context.GetLst()
    │   └─ SkyServer.SiderealTime       → static _defaultInstance._siderealTime ⚠
    │
    └─ RaDecToAxesXyCore(...)
        │
        ├─ HA degrees = 15 × (LST − RA)
        ├─ Range360
        ├─ Through-pole branch (HA > 180)
        ├─ RangeAxesXy (Range360 × Range270)
        ├─ AxesAppToMount(axes, context)
        │   └─ Northern SkyWatcher GEM: a[1] = 180 − a[1]
        │
        └─ SkyServer.GetAlternatePosition(axes)       ⚠ entirely static
            │
            ├─ IsWithinFlipLimits(position)
            │   └─ _settings!.HourAngleLimit          → static _defaultInstance ⚠
            │
            ├─ AxesContext.FromSettings(_settings)     → second static context ⚠
            ├─ Axes.GetAltAxisPosition(position, ctx)
            ├─ ChooseClosestPosition(ActualAxisX, ...) → static _defaultInstance._actualAxisX ⚠
            └─ FlipOnNextGoto                          → static _defaultInstance._flipOnNextGoto ⚠
```

---

## 7. Uninitialized / Default-Value Risks

### 7.1 `_siderealTime` = 0.0 at startup

**When it occurs:** Between `MountStart()` → `MountConnect()` completing and the first `OnUpdateServerEvent` firing (up to `DisplayInterval` ms, typically 200ms).

**Effect:** LST = 0.0h → HA is calculated as `15.0 × (0.0 − RA)` which is wrong. Any slew triggered in this window gets incorrect axis targets.

**How to check:** `SkyServer.GetLocalSiderealTime()` is called only within `OnUpdateServerEvent`. There is no pre-population of `_siderealTime` before the first tick.

### 7.2 `_appAxes` = (0, 0) before `MountReset()` or first `SetSteps()`

**When it occurs:** Immediately after `MountInstance` constructor, before `MountStart()` → `Defaults()` → `MountReset()` runs.

**Effect:** `SkyServer.SideOfPier` calculates `Normal` for Y = 0.0 (which is within `±90.0000000001`). This default is plausible but may be wrong if the mount has been left in a through-the-pole pointing state from a previous session.

**At-park case:** `MountConnect()` explicitly sets axis positions from `GetDefaultPositions_Internal()` for a parked mount, so the park position is used as the initial axis state. `SideOfPier` will then reflect the park position correctly once `SetSteps()` propagates the first position update.

### 7.3 `HourAngleLimit` = 0.0 if not configured

**Default:** `SkySettingsInstance._hourAngleLimit` is a `double` field; its C# default is `0.0`.

**Effect:** `IsWithinFlipLimits` for GermanPolar:
```csharp
return -0.0 < absPos0 && absPos0 < 0.0 ||   // → always false
       180.0 < absPos0 && absPos0 < 180.0;   // → always false
```
`GetAlternatePositionGEM` always returns `null` — no alternative position is ever considered. The mount will always slew to the primary computed position and will never automatically flip. If the mount is past the meridian, tracking will hit the physical limit.

**How to check:** Verify that `HourAngleLimit` is loaded from JSON and has a sensible value (e.g., 9° = 0.6h = typical SkyWatcher factory limit) before the first slew.

### 7.4 `_actualAxisX` = 0.0 before first hardware update

**When it occurs:** `ChooseClosestPosition(ActualAxisX, ...)` is called inside `GetAlternatePositionGEM`. If `_actualAxisX` is 0.0 (startup default), the distance comparison between the primary and alternate positions uses the wrong current position. This can cause the wrong pier side to be selected for the first slew after connection.

**Mitigation:** `MountConnect()` waits for `rawPositions` from `GetRawDegrees()` before returning, and `SetSteps()` propagates the initial hardware position, so `_actualAxisX` should be populated before any user-initiated slew. The race only exists if a slew is triggered before the first timer tick after `MountConnect()`.

---

## 8. Commented-Out Code — Sync Alignment Not Applied

```csharp
case SlewType.SlewRaDec:
    target = Axes.RaDecToAxesXy(target, context);
    // Convert to synced axes
    // target = SkyServer.GetSyncedAxes(target);   ← COMMENTED OUT
    break;
```

`GetSyncedAxes` applies the alignment model correction (star alignment sync points) to the raw coordinate transform. With this commented out, any sync points set by the user are **not applied** to slew targets. The mount will slew to the unsynced celestial position.

**Impact:** For a freshly connected, unsynced mount this is correct behaviour. For a mount that has been synced to a reference star, the slew will not benefit from the sync correction. This is most likely an intentional temporary state during active migration.

---

## 9. `CalculateSideOfPier` vs `SkyServer.SideOfPier` — Semantic Difference

Two different SideOfPier calculations exist and they differ for GermanPolar/SkyWatcher/Northern:

| Method | Inputs | Meaning |
|---|---|---|
| `SkyServer.SideOfPier` (getter) | `AppAxisY` (current position, app-space) | **Current** mount side, used for status/display |
| `CalculateSideOfPier(mountAxes, context)` | mount-native axes | **Target** side after a slew to those axes |

The `SkyServer.SideOfPier` setter triggers a flip goto when called, making it a **command** as well as state. It reads `ActualAxisX` and `FlipOnNextGoto` from the static default instance.

For the flow under analysis (`MapSlewTargetToAxes`), the current `SideOfPier` captured in the context at step 1 is not used within `RaDecToAxesXyCore` — it is structurally captured but only consumed by `IsFlipRequired()` (a different code path). The pier-side decision in `MapSlewTargetToAxes` is implicit via the through-the-pole branch logic and `GetAlternatePositionGEM`.

---

## 10. Summary of Issues and Recommendations

| # | Issue | Severity | Location |
|---|---|---|---|
| 1 | `SouthernHemisphere`, `SiderealTime`, `SideOfPier`, `ActualAxisX`, `FlipOnNextGoto` all read from static `_defaultInstance` | **Multi-device blocker** / benign single-device | `AxesContext.FromSettings`, `GetAlternatePositionGEM` |
| 2 | `_siderealTime = 0.0` at startup — incorrect LST for any slew in the first timer interval | **Functional bug** | `RaDecToAxesXy` → `GetLst()` |
| 3 | `HourAngleLimit = 0.0` if unconfigured — disables all alternate position and flip logic | **Configuration risk** | `IsWithinFlipLimits` |
| 4 | `_actualAxisX = 0.0` before first `SetSteps()` — wrong closest-position comparison | **Startup race** (narrow window) | `GetAlternatePositionGEM` → `ChooseClosestPosition` |
| 5 | `GetSyncedAxes` is commented out — alignment model sync not applied to slew targets | **Feature gap** | `MapSlewTargetToAxes` case SlewRaDec |
| 6 | `GetAlternatePositionGEM` builds a second `AxesContext.FromSettings(_settings)` using static `_settings` | **Redundant static read** — inconsistent with instance context already constructed | `SkyServer.TelescopeAPI.cs` line 2230 |
| 7 | `FlipOnNextGoto` is not cleared by `MapSlewTargetToAxes` or `RaDecToAxesXyCore` | **Caller responsibility** — must be cleared after each forced-flip goto | `GetAlternatePositionGEM` |
| 8 | `AxesContext.IsWithinFlipLimits` (used by `IsFlipRequired`) has different arithmetic than `SkyServer.IsWithinFlipLimits` for the GermanPolar case | **Logical inconsistency** — could give different answers for the same inputs | `AxesContext.cs` line 230 vs `SkyServer.TelescopeAPI.cs` line 3006 |

### Issue 8 Detail — `IsWithinFlipLimits` discrepancy

`SkyServer.IsWithinFlipLimits` (GermanPolar):
```csharp
return -_settings!.HourAngleLimit < absPos0 && absPos0 < _settings!.HourAngleLimit ||
       180 - _settings!.HourAngleLimit < absPos0 && absPos0 < 180 + _settings!.HourAngleLimit;
```

`AxesContext.IsWithinFlipLimits` (GermanPolar):
```csharp
var haLimit = HourAngleLimit ?? 15.0;
return -haLimit < absPos0 && absPos0 < haLimit ||
       180 - haLimit < absPos0 && absPos0 < 180 + haLimit;
```

The logic is identical but the defaults differ: static version uses the actual configured value; instance version falls back to `15.0°` if `HourAngleLimit` is null. In practice `AxesContext.HourAngleLimit` is always set from settings (not null), so behaviour is equivalent. However `AxesContext.IsWithinFlipLimits` is only called from `Axes.IsFlipRequired()`, which is only called from `DetermineSideOfPier()`. `GetAlternatePositionGEM` calls the **static** `SkyServer.IsWithinFlipLimits` — so within `MapSlewTargetToAxes` only the static version is exercised.

---

## 11. Coordinate System Reference (Northern Hemisphere SkyWatcher GEM)

```
App Space (HaDec-like)          Mount Space (hardware commands)
───────────────────────         ──────────────────────────────
X = RA axis (0–360°)    ──▶     X = RA axis (unchanged)
Y = Dec axis                    Y = 180 - Dec axis

Y in [-90, 90]  → Normal        Mount Y in [90, 270]
Y in [90, 270]  → ThroughThePole Mount Y in [-90, 90]

SideOfPier (static getter):
  AppAxisY ∈ (-90, 90) → Normal
  AppAxisY outside → ThroughThePole

CalculateSideOfPier (on mount axes):
  MountAxisY ∈ (-90, 90) → ThroughThePole   (INVERTED vs static getter)
  MountAxisY outside → Normal
```

> The `CalculateSideOfPier` inversion is intentional and correct: it operates on mount-native axes where the relationship is inverted by the `180 - Y` transform.

---

*End of analysis*
