# Chart Redesign — Requirements Document

**Author:** GitHub Copilot  
**Prepared for:** Andy  
**Created:** 2026-07-14 09:03  
**Last updated:** 2026-07-14 14:01  
**Status:** Requirements complete — all decisions closed

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
| Buffer | C# `List<T>` per series, server-side in the Blazor circuit, capped at `ChartSettings.MaxPoints` |
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

When the mode changes, chart options shall be rebuilt and applied to the existing
chart instance without destroying it (see Section 6.3 for the mode-switch
mechanism).

---

### 3.3 Browser-Local Data Buffer (Req-03)

#### 3.3.1 Buffer location and motivation

Incoming SignalR data points shall be accumulated in a **JavaScript browser-local
buffer** (a JS array managed in the browser's memory), not in a C# server-side
collection.

**Rationale:** The server and the browser may be connected over a low-bandwidth
link. By accumulating data in the browser, the Historical view reads from local
JS memory rather than issuing a new server request for all buffered data. Data
flows from server to browser once per incoming point; there is no back-pressure or
re-fetch when the user switches modes.

#### Chosen architecture — Option A revised (JS timer)

The selected approach is **Option A revised**: the C# SignalR handler writes each
incoming point to the JS buffer via a single JSInterop call, and a JavaScript
`setInterval` drives the realtime chart updates natively — eliminating the C#
1-second timer entirely from the hot path.

**Why a JS timer was chosen over the original C# timer (Option A original):**

The original Option A used a C# `System.Threading.Timer` that fired every second,
called `IJSRuntime.InvokeAsync` to read the current window back from JS, and then
called `_chart.UpdateSeriesAsync()` via the Blazor wrapper. This produced **two
JSInterop round-trips per second**:

```
[Every 1 s — C# timer]
  C# → JS  InvokeVoidAsync("gsChartBuffer.getWindow", windowMs)  [read]
  JS → C#  returns T[] window data
  C# → JS  _chart.UpdateSeriesAsync() → wrapper calls native updateSeries  [write]
```

With the JS timer (Option A revised) the C# timer is removed. The chart update is
driven entirely in JavaScript, reducing JSInterop to **one write per incoming
point** only:

```
[On each SignalR point — C# SignalR handler]
  C# → JS  InvokeVoidAsync("gsChartBuffer.add", chartType, seriesKey, point)  [1 call]

[Every 1 s — JS setInterval]
  JS reads buffer → slices window → ApexCharts.exec(chartId, 'updateSeries', data)
  (pure JS, zero JSInterop)
```

This matches the **WebSocket fire-and-forget pattern** described in the ApexCharts
real-time documentation: the message handler (here, the C# SignalR handler bridged
via one JSInterop write) pushes to the buffer without blocking; the chart consumer
(JS `setInterval`) reads and renders independently.

**Division of responsibility between C# and JS:**

| Concern | Handled by |
|---|---|
| Receiving SignalR points | C# SignalR handler (`_hub.On<T>(...)`) |
| Writing points to JS buffer | C# → JSInterop → `gsChartBuffer.add()` (1 call/point) |
| Realtime chart update (1 Hz) | JS `setInterval` → `ApexCharts.exec(chartId, 'updateSeries', window)` |
| Chart options (zoom, animation, axes) | C# → `_chart.UpdateOptionsAsync()` via Blazor wrapper |
| Historical chart refresh | C# → JSInterop read all → `_chart.UpdateSeriesAsync()` via wrapper |
| Export PNG | C# → `_chart.GetDataUriAsync()` via Blazor wrapper |
| Export CSV | JS → `gsChartBuffer.exportCsv()` directly |
| Cleanup on dispose | C# calls `gsChartBuffer.clear(chartType)` + JS timer stopped |

**Wrapper state divergence — analysis and mitigation:**

When JS calls `ApexCharts.exec(chartId, 'updateSeries', data)` directly, the
Blazon-ApexCharts C# wrapper's internal data model (`ApexPointSeries.Items`) does
not update — only the native chart instance changes. This divergence is contained
because:

1. `UpdateOptionsAsync()` (called on mode switch or scale change) pushes options
   to the native chart via JSInterop regardless of the C# data model state.
2. `UpdateSeriesAsync()` (called in Historical mode) does a full data replace,
   re-syncing the native chart with C# data.
3. `GetDataUriAsync()` (PNG export) reads the native rendered chart, not the C#
   data model — so the exported image is always accurate.
4. The `ApexPointSeries` child components are used only to declare the initial
   series structure (name, type, colour). They are not relied upon for ongoing
   data binding in the realtime hot path.

**Risk:** If `UpdateOptionsAsync` is called from C# while the JS timer is mid-render,
a brief frame inconsistency is possible. This is expected to be imperceptible at
1 Hz and can be mitigated in implementation by pausing the JS timer during options
update if testing reveals visible artefacts.

#### 3.3.2 Buffer size

**RA/Dec chart** — the maximum number of data points stored per axis array shall
be user-selectable from a MudBlazor `MudSelect` in the toolbar:

| Option | Points per axis array | Total objects in browser memory |
|---|---|---|
| 5 000 (default) | 5 000 | ~10 000 `ChartPointDto` |
| 10 000 | 10 000 | ~20 000 `ChartPointDto` |
| 20 000 | 20 000 | ~40 000 `ChartPointDto` |

Each axis buffer (`axis1Buffer`, `axis2Buffer`) is an independent JS array capped
at `RaDecMaxPoints`, trimmed FIFO when full.

> **Note on RA/Dec data model:** The server hub sends Axis 1 (RA) and Axis 2 (Dec)
> values as separate `ChartPointDto` messages on separate SignalR channels
> (`ReceiveAxis1Point`, `ReceiveAxis2Point`). The combined logical record is
> `{timestamp, ra_value, dec_value}`, but storage remains two independent arrays
> each capped at `RaDecMaxPoints`. No change to existing DTOs or hub methods is
> required.

**Pulse chart** — the buffer size is **fixed at 5 000 points per series array**
(not user-configurable):

| Series array | Fixed capacity |
|---|---|
| RA accepted | 5 000 `PulsePointDto` |
| RA rejected | 5 000 `PulsePointDto` |
| Dec accepted | 5 000 `PulsePointDto` |
| Dec rejected | 5 000 `PulsePointDto` |
| **Total** | **20 000 `PulsePointDto`** |

The buffer size selector shall **not** appear in the Pulse chart toolbar.

The RA/Dec buffer size selection shall be persisted to a new `ChartSettings` field
`RaDecMaxPoints`.

#### 3.3.3 Historical data pre-fill

On page load each chart shall call `RequestHistoricalDataAsync` over SignalR to
receive the server-side ring buffer. The history handler shall populate the JS
buffer up to the configured capacity (FIFO, newest points kept). After pre-fill,
live points continue to accumulate. This matches the existing prototype behaviour.

---

### 3.4 Realtime Mode (Req-04)

#### 3.4.1 Scrolling window

The visible time range shall be configurable from a MudBlazor `MudSelect` in the
toolbar, visible **only** when the mode is "Realtime Data":

| Option | Window | `xaxis.range` value |
|---|---|---|
| 10 seconds | 10 s | `10_000` ms |
| 30 seconds (default) | 30 s | `30_000` ms |
| 120 seconds | 2 min | `120_000` ms |

`xaxis.type = 'datetime'` with `xaxis.range` is used. Points outside the visible
window are excluded from the display by ApexCharts but **remain in the JS buffer**.

The selected window duration shall be persisted to a new `ChartSettings` field
`RealtimeWindowSeconds` (default 30).

#### 3.4.2 X-axis tick marks

Tick marks shall be displayed on the x-axis in all modes with the label format
`HH:mm:ss`.

In **Realtime mode** the tick interval is fixed per window, controlled by
`xaxis.tickAmount`:

| Window | Tick interval | `tickAmount` |
|---|---|---|
| 10 seconds | 1 second | `10` |
| 30 seconds | 5 seconds | `6` |
| 120 seconds | 10 seconds | `12` |

In **Historical mode** `tickAmount` is omitted — ApexCharts auto-fits tick density
to the full data span.

In the Blazor-ApexCharts C# options object:

```csharp
Xaxis = new XAxis
{
	Type        = XAxisType.Datetime,
	Range       = realtimeWindowMs,          // Realtime mode only; omit for Historical
	TickAmount  = tickAmountForWindow,       // see table above; omit for Historical
	Labels = new XAxisLabels
	{
		Format                = "HH:mm:ss",
		DatetimeUTC           = true,
		HideOverlappingLabels = false,       // true in Historical (auto-density)
		Style = new AxisLabelStyle { Colors = "rgba(255,255,255,0.87)" }
	}
}
```

#### 3.4.3 Animation

In Realtime mode the chart shall animate using ApexCharts `dynamicAnimation` for
a smooth scrolling effect:

```
chart.animations.enabled               = true
chart.animations.easing                = "linear"
chart.animations.dynamicAnimation.speed = 1000    // matches the 1-second update tick
```

**Why `speed = 1000`:** Per the ApexCharts real-time chart documentation,
`dynamicAnimation.speed` must equal the update interval exactly. If `speed` is
shorter than the interval the animation finishes early and the chart idles (stutter).
If `speed` is longer, animations pile up and the chart falls progressively behind.
Setting both to 1 000 ms ensures each animation completes exactly as the next
`updateSeries` call arrives.

**Why `easing = "linear"`:** Non-linear easings (e.g., `easein`, `easeout`)
accelerate or decelerate within each interval, creating a pulsing motion that looks
unnatural for a continuous data stream. Linear easing maintains constant scroll
velocity.

#### 3.4.4 Chart update strategy — sliding window `updateSeries`

The ApexCharts real-time documentation defines two update mechanisms:

| Mechanism | Recommended for |
|---|---|
| `appendData` fire-and-forget | Data arriving at a fixed regular interval (e.g., exactly 1 point per second) |
| `updateSeries` sliding window | **Data arriving at irregular or higher-than-display rate, batched to a regular tick — our case** |

Because mount data can arrive at an irregular rate potentially above 1 Hz and is
**batched to a 1-second display tick**, the **`updateSeries` sliding window**
approach shall be used. Per the ApexCharts docs: *"For updates arriving faster than
once per second, the `updateSeries` sliding-window approach produces less work per
frame"* — ApexCharts takes an internal fast path that redraws only the series paths,
leaving grid, axes, legend, and tooltip DOM untouched.

**Update sequence on each 1-second tick in Realtime mode (JS timer):**

1. JS `setInterval` fires at 1 000 ms.
2. `gsChartBuffer.getWindow(chartType, windowMs)` slices all series arrays to
   `[now − windowMs, now]`, returning a data object keyed by series name.
3. `ApexCharts.exec(chartId, 'updateSeries', windowData)` is called natively — no
   JSInterop round-trip; this is a direct in-browser JS call.
4. `dynamicAnimation` transitions from the previous frame over exactly 1 000 ms,
   producing smooth continuous scroll.

The JS buffer retains all points up to the configured maximum (not trimmed to the
window). ApexCharts displays only the window via `xaxis.range`.

**C# SignalR handler — fire-and-forget write:**  
The C# handler does the minimum work required: a single fire-and-forget JSInterop
write to the JS buffer. It does not wait for the chart to update:

```csharp
// In the C# SignalR handler — one JSInterop write, no await on chart
private void OnAxis1Point(ChartPointDto point)
{
	if (_disposed) return;
	// Fire-and-forget: write to JS buffer only
	_ = JS.InvokeVoidAsync("gsChartBuffer.add", "radec", "axis1", point);
	// Chart update is handled by the JS setInterval — no C# timer needed
}
```

This mirrors the WebSocket fire-and-forget pattern from the ApexCharts docs:
the event handler pushes to the buffer and returns immediately; the JS timer
drains the buffer and updates the chart independently.

#### 3.4.5 Interactivity — disabled in Realtime mode

Per the ApexCharts documentation: *"A zoom state changes the internal axis range,
and the next `appendData`/`updateSeries` call resets it, which causes a visible
snap."* All interactive controls shall be disabled in Realtime mode:

```
chart.toolbar.show      = false
chart.zoom.enabled      = false
chart.selection.enabled = false
```

#### 3.4.6 Accumulation phase

On first load (or after a clear), the chart accumulates data from zero until the
time window is filled, then begins continuous scrolling. This is handled
automatically by ApexCharts when `xaxis.range` is set — no additional code is
required.

---

### 3.5 Historical Mode (Req-05)

#### 3.5.1 Data displayed

On entering Historical mode the chart shall render **all data currently in the JS
buffer** as a single static (non-scrolling) chart:

- `xaxis.range` shall be absent (ApexCharts auto-fits the x-axis to the full data span)
- `chart.animations.enabled = false` to avoid re-draw lag on large datasets
- `UpdateSeriesAsync(allBufferData, animate: false)` is called once when the mode
  is entered and when the Refresh button is clicked

When new SignalR points arrive while in Historical mode they shall be appended to
the JS buffer **but the chart shall not auto-refresh**. The display remains frozen
until the user explicitly refreshes.

#### 3.5.2 Refresh button

A **Refresh** `MudIconButton` (`Icons.Material.Filled.Refresh`) shall appear in the
toolbar only when the mode is "Historical Data". Clicking it reads the full JS
buffer and calls `UpdateSeriesAsync(allBufferData, animate: false)` to update the
chart with any points accumulated since the last refresh.

#### 3.5.3 Interactivity — enabled in Historical mode

```
chart.toolbar.show                = true
chart.toolbar.autoSelected        = AutoSelected.Zoom
chart.toolbar.tools.zoom          = true
chart.toolbar.tools.zoomin        = true
chart.toolbar.tools.zoomout       = true
chart.toolbar.tools.pan           = true
chart.toolbar.tools.reset         = true
chart.toolbar.tools.download      = false   // export handled by Blazor toolbar
chart.zoom.enabled                = true
chart.selection.enabled           = true
```

---

### 3.6 Export (Req-06)

All charts in both modes shall provide the following export actions via the existing
MudBlazor `MudMenu`:

| Action | Format | Scope | Mechanism |
|---|---|---|---|
| Export PNG | PNG image | Visible chart area | `_chart.GetDataUriAsync` + `chartWindowInterop.downloadDataUri` |
| Export CSV | CSV file | **All populated points in the JS buffer** | New JS function `gsChartBuffer.exportCsv` — see below |

The ApexCharts built-in download button (`toolbar.tools.download`) shall remain
`false` because the Blazor toolbar handles export.

**CSV export detail:**  
The existing `chartWindowInterop.exportChartCsv` reads only from the chart's
currently rendered series. In Realtime mode the chart series contains only the
visible window — the full buffer would not be exported by that function. Therefore
a new JavaScript function `gsChartBuffer.exportCsv(chartType, visibleSeriesNames)`
shall read directly from the JS buffer arrays and generate a CSV download. Only
array elements containing data shall be included (no empty rows for unfilled buffer
slots). The `visibleSeriesNames` parameter allows the function to include only
series the user has switched on via the series toggle switches.

---

### 3.7 Preserved Controls (Req-07)

The following controls from the prototype are retained unchanged:

| Control | Both charts | RA/Dec only | Pulse only |
|---|---|---|---|
| Y-scale `MudSelect` | — | Steps / Degrees / ArcSec | Milliseconds / ArcSec / Steps |
| Series toggle `MudSwitch` × N | — | Axis 1, Axis 2 | RA, RA rej., Dec, Dec rej. |
| Logging toggle `MudIconButton` | ✓ | — | — |
| Clear chart `MudIconButton` | ✓ | — | — |
| Export menu `MudMenu` | ✓ | — | — |
| SignalR status chip | ✓ | — | — |
| `ChartWindowLayout` layout | ✓ | — | — |

---

## 4. New UI Controls Summary

| Control | Chart | Location | Visible when | Persisted to |
|---|---|---|---|---|
| Mode selector (Realtime / Historical) | Both | Toolbar | Always | `ChartSettings.DisplayMode` |
| Buffer size `MudSelect` (5k / 10k / 20k) | RA/Dec only | Toolbar | Always | `ChartSettings.RaDecMaxPoints` |
| Realtime window `MudSelect` (10s / 30s / 120s) | Both | Toolbar | Realtime mode only | `ChartSettings.RealtimeWindowSeconds` |
| Refresh `MudIconButton` | Both | Toolbar | Historical mode only | N/A (action) |

---

## 5. Settings Model Changes (`ChartSettings`)

The following additions are required to `GreenSwamp.Alpaca.Settings.Models.ChartSettings`:

```csharp
// NEW — per-axis buffer size for the RA/Dec chart (user-selectable)
/// <summary>
/// Maximum data points stored per axis buffer in the RA/Dec chart.
/// Valid values: 5000, 10000, 20000.
/// </summary>
public int RaDecMaxPoints { get; set; } = 5000;

// NEW
/// <summary>
/// Display mode selected on chart open.
/// Valid values: "Realtime", "Historical".
/// </summary>
public string DisplayMode { get; set; } = "Realtime";

// NEW
/// <summary>
/// Rolling window duration (seconds) shown in Realtime mode.
/// Valid values: 10, 30, 120.
/// </summary>
public int RealtimeWindowSeconds { get; set; } = 30;
```

**Existing `MaxPoints` field:** Retained for backwards compatibility but superseded
for the new implementation. The Pulse chart buffer is hard-coded to 5 000 per
series array in the JS buffer module.

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
	Theme  = new ApexCharts.Theme { Mode = Mode.Dark },
	Xaxis  = new XAxis
	{
		Type        = XAxisType.Datetime,
		Range       = realtimeWindowMs,       // 10_000 / 30_000 / 120_000
		TickAmount  = tickAmountForWindow,    // 10 / 6 / 12
		Labels = new XAxisLabels
		{
			Format                = "HH:mm:ss",
			DatetimeUTC           = true,
			HideOverlappingLabels = false,
			Style = new AxisLabelStyle { Colors = "rgba(255,255,255,0.87)" }
		}
	},
	Yaxis  = [ new YAxis { Title = new AxisTitle { Text = yTitle }, Labels = new YAxisLabels { Formatter = "..." } } ],
	Stroke = new Stroke { Curve = Curve.Smooth, Width = [...] },
	Grid   = new Grid { BorderColor = "rgba(255,255,255,0.12)" },
	Legend = new Legend { Show = true }
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
		Toolbar = new Toolbar
		{
			Show         = true,
			AutoSelected = AutoSelected.Zoom,
			Tools = new Tools
			{
				Zoom     = true,
				Zoomin   = true,
				Zoomout  = true,
				Pan      = true,
				Reset    = true,
				Download = false
			}
		},
		Zoom = new Zoom { Enabled = true }
	},
	Theme  = new ApexCharts.Theme { Mode = Mode.Dark },
	Xaxis  = new XAxis
	{
		Type   = XAxisType.Datetime,
		// Range omitted — auto-fits to all data
		// TickAmount omitted — ApexCharts auto-density
		Labels = new XAxisLabels
		{
			Format                = "HH:mm:ss",
			DatetimeUTC           = true,
			HideOverlappingLabels = true,
			Style = new AxisLabelStyle { Colors = "rgba(255,255,255,0.87)" }
		}
	},
	Yaxis  = [ new YAxis { Title = new AxisTitle { Text = yTitle }, Labels = new YAxisLabels { Formatter = "..." } } ],
	Stroke = new Stroke { Curve = Curve.Smooth, Width = [...] },
	Grid   = new Grid { BorderColor = "rgba(255,255,255,0.12)" },
	Legend = new Legend { Show = true }
}
```

### 6.3 Mode switch mechanism

First attempt: call `UpdateOptionsAsync` on the existing chart instance to avoid a
destroy/recreate flicker. If testing reveals internal state is not fully reset
(e.g., zoom range or animation state persists incorrectly after the switch), fall
back to a `@key` change on `<ApexChart>` to force a clean rebuild.

```csharp
private async Task OnModeChangedAsync(string newMode)
{
	_settings.DisplayMode = newMode;
	BuildChartOptions();   // rebuilds _chartOptions for the new mode
	await SettingsService.SaveChartSettingsAsync(_settings);

	// Pause the JS realtime timer while options are being applied to prevent
	// a race between native updateSeries and the incoming UpdateOptionsAsync.
	await JS.InvokeVoidAsync("gsChartBuffer.setRealtimeActive", "radec", false);

	if (_chart is not null)
		await _chart.UpdateOptionsAsync(_chartOptions, redraw: true, animate: false);

	if (newMode == "Historical")
		await RefreshHistoricalAsync();   // reads full JS buffer → UpdateSeriesAsync
	else
		await JS.InvokeVoidAsync("gsChartBuffer.setRealtimeActive", "radec", true);

	StateHasChanged();
}
```

Note: `gsChartBuffer.setRealtimeActive` is a JS function that starts/stops the
`setInterval` for the given chart type. The JS timer is started on page load and
stopped on dispose.

### 6.4 Combo chart — Pulse mixed Line + Scatter

The Pulse chart uses two `SeriesType.Line` and two `SeriesType.Scatter` series.
The `<ApexChart>` component-level `Options.Chart.Type` shall remain `ChartType.Line`
(dominant type). Per-series type is declared on each `<ApexPointSeries SeriesType>`.

---

## 7. Architecture Overview

```
Browser (JS memory)
┌──────────────────────────────────────────────────────────────────────┐
│  gsChartBuffer.js                                                    │
│  ┌─────────────┬─────────────┬─────────────┬──────────────────────┐ │
│  │ axis1Buffer │ axis2Buffer │  raBuffer   │ raRejBuf decBuf      │ │
│  │  (≤MaxPts)  │  (≤MaxPts)  │  (≤5 000)   │ decRejBuf (≤5 000 ea)│ │
│  │  RA/Dec     │  RA/Dec     │    Pulse    │       Pulse          │ │
│  └─────────────┴─────────────┴─────────────┴──────────────────────┘ │
│                                                                      │
│  Realtime mode                   │  Historical mode                 │
│  ┌───────────────────────────┐   │  ┌────────────────────────────┐  │
│  │ xaxis.range = N ms        │   │  │ xaxis.range = auto         │  │
│  │ tickAmount = 10 / 6 / 12  │   │  │ tickAmount = auto           │  │
│  │ zoom / toolbar disabled   │   │  │ zoom / toolbar enabled     │  │
│  │ animation: linear 1 000ms │   │  │ animation disabled         │  │
│  │ 1-sec timer:              │   │  │ chart frozen;              │  │
│  │   getWindow(windowMs) →   │   │  │ Refresh button:            │  │
│  │   UpdateSeriesAsync(data) │   │  │   getAll() →               │  │
│  └───────────────────────────┘   │  │   UpdateSeriesAsync(data)  │  │
│                                  │  └────────────────────────────┘  │
  ApexChart<T> (Blazor-ApexCharts wrapper)                            │
│  ← UpdateOptionsAsync from C# (mode switch, scale change)            │
│  ← UpdateSeriesAsync from C# (Historical refresh only)               │
│  ← ApexCharts.exec('updateSeries') from JS setInterval (Realtime)    │
│                                                                      │
│  gsChartBuffer.setRealtimeActive(type, bool) — JS timer on/off       │
│  gsChartBuffer.exportCsv()  — reads full buffer, not chart series     │
└─
   ↑ data points arrive per SignalR message (one point per message)
   ↑ written to JS buffer via JSInterop (Option A) or JS SignalR client (Option B)
┌──────────────────────────────────────────────────────────────────────┐
│  Blazor Server circuit                                               │
│  RaDecChart.razor.cs / PulseChart.razor.cs                           │
│  ChartHub.cs  ←  ChartDataService  ←  Mount hardware                │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 8. Open Questions

All open questions are now closed. See Section 9.

---

## 9. Closed Decisions

| ID | Decision |
|---|---|
| OQ-01 | Buffer location: **JavaScript browser-local** — minimise data on low-bandwidth links; no server-side C# buffer for chart data |
| OQ-02 | MaxPoints: RA/Dec **5k / 10k / 20k user-selectable per axis array**; Pulse **fixed 5 000 per series array** × 4 series = 20 000 total (not configurable) |
| OQ-03 | Display refresh: **1 Hz** (1-second timer). Source data may arrive at higher irregular rates. Chart update uses **`updateSeries` sliding window** per tick — the efficient path per ApexCharts docs when source rate exceeds display rate |
| OQ-04 | Pause button: **deferred** |
| OQ-05 | Live point-count badge: **deferred** |
| OQ-06 | CSV export: **all populated points in the JS buffer** for all visible series; unfilled slots excluded. Requires `gsChartBuffer.exportCsv()` — the existing `chartWindowInterop.exportChartCsv` only sees the rendered chart window and cannot export the full buffer in Realtime mode |
| OQ-07 | Mode switch: **`UpdateOptionsAsync` first**, fallback to `@key` rebuild if state reset is incomplete |
| OQ-08 | Historical Refresh: **replace chart data with full buffer contents** |
| OQ-A | JS buffer architecture sub-pattern: **Option A revised** — C# SignalR handler writes each incoming point to the JS buffer via one `InvokeVoidAsync` call; a JS `setInterval(1000)` reads the buffer and calls `ApexCharts.exec(chartId, 'updateSeries', windowData)` natively (zero JSInterop on the realtime hot path); C# Blazor wrapper is used only for `UpdateOptionsAsync` (mode/scale switch), `UpdateSeriesAsync` (Historical refresh), `GetDataUriAsync` (PNG export), and dispose cleanup. The JS timer is paused during options updates to prevent race conditions. |
| NR-01 | X-axis tick format: `HH:mm:ss`. Tick intervals: 1 s (10 s window, `tickAmount=10`), 5 s (30 s window, `tickAmount=6`), 10 s (120 s window, `tickAmount=12`). Historical mode: auto-density |

---

## 10. Out of Scope

The following items are explicitly **not** in scope for this redesign:

- Changes to `ChartHub.cs` (server-side hub logic, ring buffer, history replay)
- Changes to `ChartDataService` or data producer code
- Changes to `ChartLoggingService`
- Changes to `ChartWindowLayout.razor`
- New chart types beyond RA/Dec and Pulse
- Multiple chart instances in the same window
- Cross-window data sharing
- Pause button (deferred)
- Live buffer point-count badge (deferred)

---

## 11. Implementation Phase Outline (Tentative)

Implementation proceeds in the following phases once OQ-A is resolved:

| Phase | Scope | Key deliverables |
|---|---|---|
| 1 | **Settings** | Add `RaDecMaxPoints`, `DisplayMode`, `RealtimeWindowSeconds` to `ChartSettings`; update persistence |
| 2 | **JS buffer module** | Create `gsChartBuffer.js` — buffer arrays per series, add/trim/getWindow/getAll/clear/exportCsv functions; register as Blazor JS interop module |
| 3 | **Chart options builder** | New `BuildChartOptions(mode, windowSecs)` for both charts — Realtime options with correct `tickAmount`; Historical options with auto-density; retain existing Y-axis formatters and series stroke/marker config |
| 4 | **Realtime update path** | C# SignalR handler writes to JS buffer via `InvokeVoidAsync("gsChartBuffer.add", ...)` (one call per point); JS `setInterval(1000)` in `gsChartBuffer.js` reads buffer window and calls `ApexCharts.exec(chartId, 'updateSeries', windowData)` natively; `setRealtimeActive(chartType, bool)` exported to start/stop the timer |
| 5 | **Historical path and Refresh** | Mode-switch logic (`UpdateOptionsAsync` → `RefreshHistoricalAsync`); Refresh button handler |
| 6 | **Toolbar UI** | Mode selector, RA/Dec buffer size selector, Realtime window selector (both charts), Historical Refresh button |
| 7 | **CSV export** | `gsChartBuffer.exportCsv()` reads full buffer; wire to Blazor export menu; remove dependence on `chartWindowInterop.exportChartCsv` for buffer export |
| 8 | **Cleanup and lifecycle** | `DisposeAsync` stops timer, closes hub, calls `gsChartBuffer.clear(chartType)` to release JS memory; prevents `appendData`/`updateSeries` calls after dispose |
| 9 | **Testing** | Realtime scroll and animation correctness; mode switch (both directions); export in both modes; low-bandwidth simulation; memory profile with 40 000 points; window close cleanup |

---

*End of document*
