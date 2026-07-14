# Chart Redesign — Requirements Document

**Author:** GitHub Copilot  
**Prepared for:** Andy  
**Created:** 2026-07-14 09:03  
**Last updated:** 2026-07-14 09:03  
**Status:** Draft — awaiting Andy's answers to open questions (Section 8)

---

## 1. Purpose

This document captures the functional and technical requirements for replacing the
prototype RA/Dec and Pulse chart pages in `GreenSwamp.Alpaca.Server` with a
redesigned implementation. It is intended for offline review before implementation
begins.

---

## 2. Background — Prototype Behaviour

The current prototypes (`RaDecChart.razor` / `PulseChart.razor`) share the
following characteristics, preserved as the baseline:

| Aspect | Current prototype |
|---|---|
| Route | `/charts/radec/{DeviceNumber}` and `/charts/pulse/{DeviceNumber}` |
| Layout | `ChartWindowLayout` — bare MudBlazor layout, opens in independent browser window |
| Data transport | SignalR hub at `/charthub` — `JoinRaDecGroupAsync`, `JoinPulseGroupAsync`, `RequestHistoricalDataAsync` |
| Buffer | C# `List<T>` per series, capped at `ChartSettings.MaxPoints` (default 5 000), trimmed FIFO on each new point |
| Chart update | 1-second `System.Threading.Timer` fires `UpdateSeriesAsync(animate:false)` if new data arrived |
| Chart library | `Blazor-ApexCharts` NuGet (`ApexChart<T>` / `ApexPointSeries<T>`) |
| Export | PNG via `chart.GetDataUriAsync`, CSV via `chartWindowInterop.exportChartCsv` |
| Series visibility | MudBlazor `MudSwitch` per series — settings persisted to `ChartSettings` |
| Zoom/pan | Enabled in both prototypes (no separate mode distinction) |
| Theme | Dark, `#1e1e1e` background, `rgba(255,255,255,0.87)` foreground |

### 2.1 RA/Dec Chart — Series Detail

| Series | Type | Colour | Y-value source |
|---|---|---|---|
| Axis 1 (RA) | Line | `#b71c1c` | `ChartPointDto.Value` (Steps/Degrees/ArcSec) |
| Axis 2 (Dec) | Line | `#2196f3` | `ChartPointDto.Value` (Steps/Degrees/ArcSec) |

Y-scale selector: **Steps** / **Degrees** / **Arc-seconds**  
Conversion: `Degrees = Value × 360 / StepsPerRevolution[axis]`; `ArcSeconds = Degrees × 3600`

### 2.2 Pulse Chart — Series Detail

| Series | Type | Colour | Y-value source |
|---|---|---|---|
| RA (accepted) | Line | `#4caf50` | `PulsePointDto` where `Axis==0 && !Rejected` |
| RA rejected | Scatter | `#ff9800` | `PulsePointDto` where `Axis==0 && Rejected` |
| Dec (accepted) | Line | `#2196f3` | `PulsePointDto` where `Axis==1 && !Rejected` |
| Dec rejected | Scatter | `#f44336` | `PulsePointDto` where `Axis==1 && Rejected` |

Y-scale selector: **Milliseconds** / **Arc-seconds** / **Steps**

---

## 3. Functional Requirements

### 3.1 Chart Types (Req-01)

Two chart pages shall be implemented:

| Chart | Route (unchanged) | Data DTO |
|---|---|---|
| RA/Dec Position | `/charts/radec/{DeviceNumber:int}` | `ChartPointDto` |
| Pulse Guide | `/charts/pulse/{DeviceNumber:int}` | `PulsePointDto` |

Both pages retain their existing series, colour scheme, Y-scale selectors, series
visibility toggles and export controls from the prototype.

---

### 3.2 Display Modes (Req-02)

Each chart page shall provide **two mutually exclusive display modes** selected by
the user from a MudBlazor `MudToggleGroup` or `MudSelect` in the toolbar:

| Mode | Label | Description |
|---|---|---|
| Realtime | "Realtime Data" | Continuously scrolling live view of the most recent N seconds |
| Historical | "Historical Data" | Static (non-scrolling) view of the entire local data buffer |

The default mode on page load shall be **Realtime Data**.

When the mode changes the chart options shall be rebuilt and `UpdateOptionsAsync`
called; the chart shall **not** be destroyed and re-created if it can be avoided
(no `@key` key-change on mode switch).

---

### 3.3 Browser-Local Data Buffer (Req-03)

#### 3.3.1 Buffer location

Incoming SignalR points shall be accumulated in a **C# circuit-side buffer**
(`List<T>` or `Queue<T>` per series) within the Blazor component. This is
functionally equivalent to "browser-local" for Blazor Server because the buffer is
per-connection and does not require any additional server-side storage or round-trip.

> **Open question OQ-01** — see Section 8. If Andy requires a true
> JavaScript-side buffer (e.g., to survive a Blazor circuit reconnect) this
> decision must be revisited; the JS-side approach adds JSInterop complexity.

#### 3.3.2 Buffer size

The maximum number of data points retained **per series** shall be user-selectable
from a MudBlazor `MudSelect` in the toolbar:

| Option | Points |
|---|---|
| 5 000 (default) | 5 000 |
| 10 000 | 10 000 |
| 20 000 | 20 000 |
| 40 000 | 40 000 |

When the buffer is full the oldest point shall be discarded (FIFO), matching the
existing prototype behaviour.

The selected buffer size shall be persisted to `ChartSettings.MaxPoints` (existing
field, existing range extended to 40 000).

> **Open question OQ-02** — see Section 8. In the Pulse chart there are four
> logical series (RA accepted, RA rejected, Dec accepted, Dec rejected). Does the
> MaxPoints limit apply per individual series list, or across all RA points
> combined, or across all four lists combined?

#### 3.3.3 Historical data pre-fill

On page load the chart shall call `RequestHistoricalDataAsync` over SignalR to
receive the server-side ring buffer. The historical data handler shall populate the
local circuit buffer up to `MaxPoints`, after which live points continue to append.
This matches the existing prototype behaviour and is preserved unchanged.

---

### 3.4 Realtime Mode (Req-04)

#### 3.4.1 Scrolling window

The visible time range shall be configurable from a MudBlazor `MudSelect` in the
toolbar, visible only when the mode is "Realtime Data":

| Option | Window |
|---|---|
| 10 seconds | `xaxis.range = 10_000` |
| 30 seconds (default) | `xaxis.range = 30_000` |
| 120 seconds | `xaxis.range = 120_000` |

ApexCharts `xaxis.type = 'datetime'` with `xaxis.range` is used. Points older
than the window are automatically discarded from the display by ApexCharts but
**remain in the local buffer**.

The selected window duration shall be persisted to a new `ChartSettings` field
`RealtimeWindowSeconds` (default 30).

#### 3.4.2 Animation

In Realtime mode the chart shall animate using ApexCharts `dynamicAnimation`:

```
chart.animations.enabled = true
chart.animations.easing   = "linear"
chart.animations.dynamicAnimation.speed = <window_ms / display_points>
```

Because SignalR data is event-driven (not fixed-interval polling) the
`dynamicAnimation.speed` shall be set to a **fixed sensible value** (e.g. 1 000 ms)
rather than attempting to match a variable arrival rate.

> **Open question OQ-03** — see Section 8. What is the typical data-point arrival
> rate from the mount in production? This governs whether 1 000 ms is appropriate
> or if a different speed should be used.

#### 3.4.3 Interactivity — disabled in Realtime mode

```
chart.toolbar.show = false        (or tools: { zoom: false, pan: false, ... })
chart.zoom.enabled = false
chart.selection.enabled = false
```

Rationale: a user zoom interaction followed by an `appendData`/`updateSeries` call
resets the axis range, causing a jarring snap.

#### 3.4.4 Accumulation phase

On first load (or after a clear), the visible window shall **accumulate** from
zero until sufficient data fills the time window, then begin the continuous scroll.
This is handled automatically by ApexCharts with the `xaxis.range` configuration
— no extra code is required.

#### 3.4.5 Chart update strategy in Realtime mode

The existing 1-second timer / `_pendingChartUpdate` flag pattern shall be retained
**but adapted**:

- The update call shall use `UpdateSeriesAsync(animate: true)` (not `false`) in
  Realtime mode so that `dynamicAnimation` fires.
- The timer interval shall remain 1 second. If data arrives faster than 1 Hz
  the buffer absorbs the burst and the chart renders the latest state on each
  1-second tick.

> **Open question OQ-04** — see Section 8. Should there be a **Pause** button in
> Realtime mode that freezes the display (stops chart updates) while buffering
> continues? This would allow the user to inspect a moment in time without losing
> data.

---

### 3.5 Historical Mode (Req-05)

#### 3.5.1 Data displayed

The chart shall call `UpdateSeriesAsync(animate: false)` with **all data currently
in the local buffer** rendered as a single static chart. The chart shall not scroll.

`xaxis.range` shall be set to `undefined` / `null` (i.e., auto-fit to all data).

When new SignalR points arrive in Historical mode they shall be appended to the
local buffer but the chart shall **not** auto-refresh. A refresh occurs only when
the user:

- Switches back to Realtime mode, or
- Clicks a **Refresh** button (new addition to the Historical mode toolbar section)

> **Open question OQ-05** — see Section 8. Should Historical mode show a live
> point count badge (e.g. "12 450 / 20 000 pts") to indicate the buffer is still
> filling even though the chart is frozen?

#### 3.5.2 Interactivity — enabled in Historical mode

```
chart.toolbar.show = true
chart.toolbar.tools.zoom    = true
chart.toolbar.tools.zoomin  = true
chart.toolbar.tools.zoomout = true
chart.toolbar.tools.pan     = true
chart.toolbar.tools.reset   = true
chart.toolbar.tools.download = false   // export handled by Blazor toolbar
chart.zoom.enabled    = true
chart.selection.enabled = true
```

Animation shall be disabled (`chart.animations.enabled = false`) to avoid re-draw
lag on large datasets.

---

### 3.6 Export (Req-06)

All charts in both modes shall retain the existing MudBlazor `MudMenu` export
controls:

| Action | Format | Mechanism |
|---|---|---|
| Export PNG | PNG image | `_chart.GetDataUriAsync` + `chartWindowInterop.downloadDataUri` |
| Export CSV | CSV file | `chartWindowInterop.exportChartCsv` |

The ApexCharts built-in download button (`toolbar.tools.download`) shall remain
`false` because the Blazor menu already handles export.

> **Open question OQ-06** — see Section 8. For Historical mode with up to 40 000
> points, should CSV export produce the **entire buffer** (all series, all points)
> or only the **currently visible chart window**?

---

### 3.7 Preserved Controls (Req-07)

The following controls from the prototype are retained unchanged in the new design:

| Control | Both charts | RA/Dec only | Pulse only |
|---|---|---|---|
| Y-scale `MudSelect` | — | Steps/Degrees/ArcSec | Milliseconds/ArcSec/Steps |
| Series toggle `MudSwitch` × N | — | Axis1, Axis2 | RA, RA rej., Dec, Dec rej. |
| Logging toggle `MudIconButton` | ✓ | — | — |
| Clear chart `MudIconButton` | ✓ | — | — |
| Export menu `MudMenu` | ✓ | — | — |
| SignalR status chip | ✓ | — | — |
| `ChartWindowLayout` layout | ✓ | — | — |

---

## 4. New UI Controls Summary

The following controls are **new** relative to the prototype:

| Control | Location | Persisted to |
|---|---|---|
| Mode selector (Realtime / Historical) | Toolbar | `ChartSettings.DisplayMode` (new field) |
| Buffer size `MudSelect` (5k/10k/20k/40k) | Toolbar | `ChartSettings.MaxPoints` (extended) |
| Realtime window `MudSelect` (10s/30s/120s) | Toolbar — Realtime mode only | `ChartSettings.RealtimeWindowSeconds` (new field) |
| Refresh button | Toolbar — Historical mode only | N/A (action) |

---

## 5. Settings Model Changes (`ChartSettings`)

The following additions are required to `GreenSwamp.Alpaca.Settings.Models.ChartSettings`:

```csharp
/// <summary>
/// Display mode selected on chart open.
/// Valid values: "Realtime", "Historical".
/// </summary>
public string DisplayMode { get; set; } = "Realtime";

/// <summary>
/// Rolling window duration (seconds) shown in Realtime mode.
/// Valid values: 10, 30, 120.
/// </summary>
public int RealtimeWindowSeconds { get; set; } = 30;
```

`MaxPoints` remains but its valid range is extended to accept 10 000, 20 000, and
40 000 in addition to the existing 5 000.

---

## 6. ApexCharts Configuration Notes

### 6.1 Realtime mode options skeleton

```csharp
new ApexChartOptions<T>
{
	Chart = new Chart
	{
		Background  = "#1e1e1e",
		ForeColor   = "rgba(255,255,255,0.87)",
		Animations  = new Animations
		{
			Enabled          = true,
			Easing           = Easing.Linear,
			DynamicAnimation = new DynamicAnimation { Speed = 1000 }
		},
		Toolbar = new Toolbar { Show = false },
		Zoom    = new Zoom  { Enabled = false }
	},
	Xaxis = new XAxis
	{
		Type  = XAxisType.Datetime,
		Range = realtimeWindowMs,           // e.g. 30_000
		Labels = new XAxisLabels { Format = "HH:mm:ss", DatetimeUTC = true, ... }
	},
	...
}
```

### 6.2 Historical mode options skeleton

```csharp
new ApexChartOptions<T>
{
	Chart = new Chart
	{
		Background = "#1e1e1e",
		ForeColor  = "rgba(255,255,255,0.87)",
		Animations = new Animations { Enabled = false },
		Toolbar    = new Toolbar
		{
			Show = true,
			Tools = new Tools
			{
				Zoom = true, Zoomin = true, Zoomout = true,
				Pan = true, Reset = true, Download = false
			},
			AutoSelected = AutoSelected.Zoom
		},
		Zoom = new Zoom { Enabled = true }
	},
	Xaxis = new XAxis
	{
		Type  = XAxisType.Datetime,
		// No Range property — auto-fits to data
		Labels = new XAxisLabels { Format = "HH:mm:ss", DatetimeUTC = true, ... }
	},
	...
}
```

### 6.3 Mode switch approach

Options are rebuilt via `BuildChartOptions()` when the mode changes. Because the
Blazor `ApexChart<T>` component does not expose `UpdateOptionsAsync` directly
(it does via `@ref`), the call sequence is:

```csharp
private async Task OnModeChangedAsync(string newMode)
{
	_settings.DisplayMode = newMode;
	BuildChartOptions();
	await SettingsService.SaveChartSettingsAsync(_settings);
	if (_chart is not null)
		await _chart.UpdateOptionsAsync(_chartOptions, redraw: true, animate: false);
	StateHasChanged();
}
```

If `UpdateOptionsAsync` does not fully reset the zoom/animation state (a known
Blazor-ApexCharts edge case), a `@key` change on the chart component is the
fallback — this destroys and recreates it, which is acceptable on deliberate
mode switches.

### 6.4 Combo chart (Pulse) — mixed Line + Scatter

The Pulse chart uses two `SeriesType.Line` and two `SeriesType.Scatter` series
simultaneously. This is supported by the Blazor-ApexCharts wrapper through
per-`ApexPointSeries` `SeriesType`. The chart-level `chart.type` should be set to
`ChartType.Line` (the dominant type).

---

## 7. Architecture Diagram

```
Browser circuit (Blazor Server)
┌─────────────────────────────────────────────────────┐
│  RaDecChart.razor / PulseChart.razor                │
│                                                     │
│  Local buffer (C# List<T> per series)               │
│  ┌─────────────┐  ← SignalR ReceiveXxxPoint         │
│  │ _axis1Data  │                                    │
│  │ _axis2Data  │  capped at MaxPoints (FIFO)        │
│  └─────────────┘                                    │
│                                                     │
│  Display Mode                                       │
│  ┌─────────────────────┬──────────────────────────┐ │
│  │ Realtime            │ Historical               │ │
│  │ xaxis.range = N ms  │ xaxis.range = auto       │ │
│  │ zoom = false        │ zoom = true              │ │
│  │ animation = linear  │ animation = false        │ │
│  │ 1-sec timer update  │ manual Refresh only      │ │
│  └─────────────────────┴──────────────────────────┘ │
│                                                     │
│  ApexChart<T> component  ← UpdateSeriesAsync()      │
│  (Blazor-ApexCharts NuGet)                          │
└─────────────────────────────────────────────────────┘
		   ↑ SignalR /charthub
┌─────────────────────────────────────────────────────┐
│  ChartHub (server)                                  │
│  ChartDataService (ring buffer, history replay)     │
└─────────────────────────────────────────────────────┘
```

---

## 8. Open Questions — Action Required from Andy

The following questions require decisions before implementation begins. Please
review and provide answers.

---

### OQ-01 — Buffer location: C# circuit vs. JavaScript

**Question:**  
The requirement says "browser local buffer". For Blazor Server the component's C#
`List<T>` fields live on the server inside the SignalR circuit (one per connected
window). This is the simplest approach and matches the prototype.

A true JavaScript-side buffer (populated via `IJSRuntime.InvokeAsync`) would
survive a Blazor circuit reconnect but adds considerable JSInterop complexity.

**Options:**

| Option | Pros | Cons |
|---|---|---|
| **A — C# circuit buffer (recommended)** | Simple, type-safe, matches prototype | Lost on circuit disconnect/reconnect |
| B — JavaScript buffer | Survives circuit reconnect | JSInterop round-trips, harder to debug |

**Default recommendation:** Option A — C# circuit buffer.

---

### OQ-02 — MaxPoints scope for Pulse chart

**Question:**  
The Pulse chart has four separate `List<PulsePointDto>` collections:
`_raData`, `_raRejData`, `_decData`, `_decRejData`.

Should `MaxPoints` (e.g. 10 000) mean:
- **A** — 10 000 points **per list** (up to 40 000 total objects in memory)?
- **B** — 10 000 **RA points total** (split between accepted + rejected), and 10 000 **Dec points total**?
- **C** — 10 000 **across all four lists combined**?

**Default recommendation:** Option A — per list, consistent with how the prototype
already manages the lists independently. Option B is a reasonable alternative.

---

### OQ-03 — Realtime animation speed

**Question:**  
ApexCharts `dynamicAnimation.speed` should match the data arrival interval for
smooth scrolling. The docs state: "If speed is longer than the interval,
animations pile up and the chart lags."

What is the typical pulse guide and RA/Dec point arrival rate from the SkyWatcher
mount? (e.g. 1 Hz, 2 Hz, 10 Hz?)

**Options:**

| Option | Notes |
|---|---|
| **A — Fixed 1 000 ms (1 Hz)** | Safe default; works for mounts up to 1 Hz |
| B — Fixed 500 ms (2 Hz) | Suitable if data arrives at ~2 Hz |
| C — User-configurable | Added to settings; more flexible but adds UI complexity |

**Default recommendation:** Option A (1 000 ms) unless mount data rate is known
to be higher.

---

### OQ-04 — Pause button in Realtime mode

**Question:**  
Should Realtime mode include a **Pause** `MudIconButton` that freezes the chart
display (no more `UpdateSeriesAsync` calls) while the local buffer continues to
accumulate live points?

When unpaused, the chart would resume from the **latest** buffer contents.

**Options:**

| Option | Notes |
|---|---|
| A — Yes, include Pause button | Useful for inspecting a transient event |
| **B — No, omit for now** | Reduces scope; user can switch to Historical mode to inspect |

---

### OQ-05 — Historical mode live point count badge

**Question:**  
When in Historical mode (chart frozen), should the toolbar show a live badge
indicating how many points are currently in the buffer vs the maximum?

Example: `MudChip` showing **"12 450 / 20 000 pts"** updating via `StateHasChanged`
on each incoming SignalR point.

**Options:**

| Option | Notes |
|---|---|
| A — Yes, show badge | Helpful for knowing when the buffer is nearly full |
| **B — No, omit** | Reduces complexity; buffer size is implicit |

---

### OQ-06 — CSV export scope in Historical mode

**Question:**  
With up to 40 000 data points in the buffer, the CSV export in Historical mode
could be very large. Should CSV export produce:

**Options:**

| Option | Notes |
|---|---|
| A — Full buffer for all visible series | Complete data; potentially very large file |
| **B — Visible window only** | Consistent with PNG export (exports what is shown) |
| C — User choice via dialog | Most flexible; most complex |

---

### OQ-07 — Mode switch chart rebuild vs. options update

**Question:**  
When the user switches between Realtime and Historical mode, the chart options
change significantly (zoom, animation, xaxis.range). Two implementation strategies:

| Strategy | Mechanism | Risk |
|---|---|---|
| **A — UpdateOptionsAsync** | Call `_chart.UpdateOptionsAsync(newOptions)` | Options may not fully reset internal state (known edge case in Blazor-ApexCharts) |
| B — @key change | Change a `@key` attribute on `<ApexChart>` to force destroy/recreate | Chart flickers briefly; series data is re-bound cleanly |

**Default recommendation:** Attempt Option A first; fall back to Option B if
testing reveals state-reset issues.

---

### OQ-08 — Historical Refresh button behaviour

**Question:**  
In Historical mode, new points arrive in the buffer but the chart is frozen.
When the user clicks **Refresh**, should:

| Option | Notes |
|---|---|
| **A — Replace entire chart data** with current buffer | Clean; matches "snapshot of buffer" intent |
| B — Append only the new points since last refresh | Incremental; less disruption to zoom/pan state |

**Default recommendation:** Option A — replace with full buffer. This is simpler
and ensures the chart always accurately reflects the buffer.

---

## 9. Out of Scope

The following items are explicitly **not** in scope for this redesign:

- Changes to `ChartHub.cs` (server-side hub logic, ring buffer, history replay)
- Changes to `ChartDataService` or data producer code
- Changes to `ChartLoggingService`
- Changes to `ChartWindowLayout.razor`
- New chart types beyond RA/Dec and Pulse
- Multiple chart instances in the same window
- Cross-window data sharing

---

## 10. Implementation Phase Outline (Tentative)

Once open questions are resolved, implementation is suggested in the following
phases:

| Phase | Scope |
|---|---|
| 1 | Extend `ChartSettings` with new fields; update persistence |
| 2 | Implement `BuildChartOptions()` for both Realtime and Historical mode (new option builder, replacing existing single builder) |
| 3 | Add Mode selector, Buffer size selector, Realtime window selector to toolbar for both charts |
| 4 | Wire mode switch to option update and chart refresh |
| 5 | Add Historical Refresh button; adjust SignalR handler to not auto-refresh chart in Historical mode |
| 6 | End-to-end testing — Realtime scroll, mode switch, export in both modes |

---

*End of document*
