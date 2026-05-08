# Telescope State Page — UI Redesign Specification

**Document version:** 2026-05-06 17:00
**Author:** GitHub Copilot
**Status:** Draft — for offline review

---

## 1. Overview

The current Telescope State page renders all fields in a single vertical stack of full-width cards.
The redesign introduces a compact, four-quadrant multi-column layout that reduces scrolling, removes
redundant label prefixes, and replaces plain `true/false` text with coloured icon indicators.

---

## 2. Information Architecture

Andy's five defined groups map to the following renamed display panels:

| # | Andy's name                          | Panel title in UI             | Data fields |
|---|--------------------------------------|-------------------------------|-------------|
| 1 | Mount position information           | **Position**                  | RA, Dec, Alt, Az, Local Hour Angle, Side of Pier |
| 2 | Mount status information             | **Status**                    | Mount Running, Slewing, Tracking, At Park, At Home, Pulse Guiding |
| 3 | Supplemental mount position info     | **Axis Positions**            | Actual Axis X/Y, App Axis X/Y, Axis 1 Steps, Axis 2 Steps |
| 4 | Supplemental status information      | **Tracking & Guiding**        | Tracking Rate, Pulse Guide RA, Pulse Guide Dec |
| 5 | Date time information                | **Date / Time**               | Local Date |

Fields **removed** from the current page: UTC Date, Loop Counter, Timer Overruns, Last Update, Target Position.

---

## 3. Page-Level Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Telescope Device State                                  [page heading]  │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  MudTabs — one tab per device (Device 0, Device 1 …)            │   │
│  │  ┌────────────────────────────────────────────────────────────┐  │   │
│  │  │  MudGrid  Spacing="3"  (outer grid, full width)            │  │   │
│  │  │                                                            │  │   │
│  │  │  Row 1:  [Position  xs=12 md=6]  [Status     xs=12 md=6]  │  │   │
│  │  │  Row 2:  [Axis Pos  xs=12 md=6]  [Track/Gde  xs=12 md=6]  │  │   │
│  │  │  Row 3:  [Date/Time xs=12 md=4]  (spans 4 cols, left)     │  │   │
│  │  │                                                            │  │   │
│  │  └────────────────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

All five panels are `MudPaper` with `Elevation="0"` and `Outlined="false"` — no box borders,
consistent with the GS dark theme's flat card style used elsewhere in the app.
Each panel has a `MudText Typo="Typo.overline"` group heading in `--gs-text-secondary` colour
and `Dense` data rows below it.

---

## 4. Panel Designs

### 4.1 Panel 1 — Position

**MudGrid item:** `xs="12" md="6"`

Uses a `MudSimpleTable` with `Dense="true"` and `Elevation="0"` — no outer borders.
Two columns: **Label** (fixed narrow width, `Typo.caption`, secondary colour) and
**Value** (`Typo.body2`, monospace font via `gs-font-mono` class for alignment).

| Label | Format | Example |
|-------|--------|---------|
| RA    | `FormatHMS()` | `+05h 34m 32.10s` |
| Dec   | `FormatDMS()` | `+22° 00′ 52.0″` |
| Alt   | `FormatDegrees()` | `42.3851°` |
| Az    | `FormatDegrees()` | `187.2210°` |
| LHA   | `FormatHMS()` | `−00h 12m 05.00s` |
| Pier  | Plain text (`PierEast` / `PierWest` / `Unknown`) | `PierEast` |

**Label cleanup:** Short labels used — **RA**, **Dec**, **Alt**, **Az**, **LHA**, **Pier**.

---

### 4.2 Panel 2 — Status

**MudGrid item:** `xs="12" md="6"`

Each status row is a horizontal `MudStack Row="true"` containing:
- A `MudIcon` sized `Size.Small` — ✔ (`Icons.Material.Filled.CheckCircle`, `Color.Success`)
  or ✘ (`Icons.Material.Filled.Cancel`, `Color.Default` / muted)
- A `MudText Typo="Typo.body2"` label

This replaces the current `true` / `false` text output with a glanceable visual indicator.

| Field          | True colour  | False colour  | Notes |
|----------------|-------------|--------------|-------|
| Mount Running  | `Color.Success` | `Color.Default` | Primary health indicator |
| Slewing        | `Color.Warning` | `Color.Default` | Yellow when active — slew in progress |
| Tracking       | `Color.Success` | `Color.Default` | |
| At Park        | `Color.Info`    | `Color.Default` | Blue — parked state, not an error |
| At Home        | `Color.Info`    | `Color.Default` | |
| Pulse Guiding  | `Color.Primary` | `Color.Default` | Combined RA + Dec: true if either is active |

---

### 4.3 Panel 3 — Axis Positions

**MudGrid item:** `xs="12" md="6"`

Same `MudSimpleTable Dense` pattern as Panel 1.

| Label          | Format              | Notes |
|----------------|---------------------|-------|
| Actual X       | `FormatDegrees()`   | Rename from "Actual Axis X" |
| Actual Y       | `FormatDegrees()`   | Rename from "Actual Axis Y" |
| App X          | `FormatDegrees()`   | Rename from "App Axis X"    |
| App Y          | `FormatDegrees()`   | Rename from "App Axis Y"    |
| Axis 1 Steps   | `N0` integer format | Keep label as-is            |
| Axis 2 Steps   | `N0` integer format | Keep label as-is            |

**Label cleanup:** Removes the word "Axis" from the degree-formatted rows to avoid repetition
since it appears in the panel heading.

---

### 4.4 Panel 4 — Tracking & Guiding

**MudGrid item:** `xs="12" md="6"`

Mixed row types: one text value + two icon indicators.

| Field           | Component  | Detail |
|-----------------|-----------|--------|
| Tracking Rate   | `MudText Typo.body2` | Render `DriveRate` enum as plain text: `Sidereal`, `Lunar`, etc. |
| Pulse Guide RA  | `MudIcon` tick/cross | `Color.Primary` when active |
| Pulse Guide Dec | `MudIcon` tick/cross | `Color.Primary` when active |

Rows follow same `MudStack Row="true"` pattern as Panel 2.

---

### 4.5 Panel 5 — Date / Time

**MudGrid item:** `xs="12" md="4"`

Single row. Local Date displayed using the current culture's locale-formatted string
(`s.LocalDate.ToString("f", CultureInfo.CurrentCulture)` — long date + short time, e.g. *Tuesday, 6 May 2026 17:00*).
Milliseconds omitted — the state refresh rate makes sub-second precision meaningless visually.

| Label      | Format                                        |
|------------|----------------------------------------------|
| Local Date | `ToString("f", CultureInfo.CurrentCulture)`  |

---

## 5. Tab Labels

Tab text uses the **device name from settings** retrieved via
`IVersionedSettingsService.GetAlpacaDevices()`, matched by `DeviceNumber`.
Fallback: `"Device {n}"` if the name is empty or the device is not found in the list.

```csharp
private string TabLabel(int deviceNumber)
{
	var device = _alpacaDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
	return string.IsNullOrWhiteSpace(device?.DeviceName)
		? $"Device {deviceNumber}"
		: device.DeviceName;
}
```

---

## 6. Boolean Icon Pattern — Reusable Helper

To avoid repeating icon markup, a small private render-fragment helper produces the icon
for any `bool` field:

```razor
@* In @code block *@
private RenderFragment BoolIcon(bool value, Color trueColor) => __builder =>
{
	<MudIcon Icon="@(value ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.Cancel)"
			 Color="@(value ? trueColor : Color.Default)"
			 Size="Size.Small" />
};
```

Usage in markup:

```razor
<MudStack Row="true" AlignItems="AlignItems.Center" Spacing="1">
	@BoolIcon(s.Tracking, Color.Success)
	<MudText Typo="Typo.body2">Tracking</MudText>
</MudStack>
```

---

## 7. Typography & Spacing Conventions

| Element          | MudBlazor component / prop         |
|------------------|------------------------------------|
| Panel heading    | `MudText Typo="Typo.overline"` + `Class="gs-panel-heading mb-2"` |
| Table label col  | `Typo.caption`, `Color.Secondary`, min-width ~90 px |
| Table value col  | `Typo.body2`, `Class="gs-font-mono"` |
| Icon+label row   | `MudStack Row="true" AlignItems="AlignItems.Center" Spacing="1"` |
| Panel padding    | `Class="pa-3"` on each `MudPaper` |
| Outer grid gap   | `MudGrid Spacing="3"` |

---

## 8. CSS Additions Required

One new utility class needed in `site.css`:

```css
/* Telescope State — panel group heading */
.gs-panel-heading {
	color:          var(--gs-text-secondary);
	letter-spacing: 0.08em;
	font-size:      0.70rem;
	text-transform: uppercase;
}
```

The `gs-font-mono` class is already available via `--gs-font-mono` in the design system.

---

## 9. Decisions Confirmed (2026-05-06)

| Q | Decision |
|---|---------|
| SideOfPier | **Include** in Panel 1 as the last row |
| Slewing colour | **Yellow** (`Color.Warning`) when active |
| Local Date format | **Locale string** — `ToString("f", CultureInfo.CurrentCulture)` |
| Target Position | **Dropped** — not required |
| Tab labels | **Device name from settings** via `GetAlpacaDevices()`, fallback `"Device {n}"` |

---

## 10. Out of Scope

- No changes to `TelescopeStateModel.cs` or `TelescopeStateService.cs`
- No changes to formatting helper methods (`FormatHMS`, `FormatDMS`, `FormatDegrees`)
- No changes to the tab routing logic (`/telescope-state/{DeviceNumber:int}`)
- The `SettingsHealthStatus.razor` component is unaffected