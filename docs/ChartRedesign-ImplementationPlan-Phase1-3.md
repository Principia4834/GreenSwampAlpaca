# Chart Redesign — Implementation Plan: Phases 1–3

**Author:** GitHub Copilot  
**Prepared for:** Andy  
**Created:** 2026-07-14 14:17  
**Last updated:** 2026-07-14 14:17  
**Status:** Ready for review — no code changes started  
**Source of truth:** `docs\ChartRedesign-Requirements.md`

---

## How to use this document

Each phase is a self-contained vertical slice that leaves the codebase in a **buildable, runnable** state.  
Each step is atomic: one concern, one file or file group, one build verification.  
Steps are numbered within phases; the cross-phase dependency order is Phase 1 → Phase 2 → Phase 3.

---

## Phase 1 — Settings & Infrastructure Foundations

**Goal:** Extend the settings model and service interface with all new chart fields; register everything; prove the settings round-trip before any UI or JS work starts.  
**Risk level:** Low — additive model changes only; no existing logic is altered.  
**Build gates:** Build must pass green at the end of every step.

---

### Step 1.1 — Extend `ChartSettings` model

**File:** `GreenSwamp.Alpaca.Settings\Models\ChartSettings.cs`

Add the following new properties to `ChartSettings`. All are backward-compatible (new fields with defaults):

| New property | Type | Default | Notes |
|---|---|---|---|
| `DisplayMode` | `string` | `"Realtime"` | `"Realtime"` or `"Historical"` |
| `RealtimeWindowSeconds` | `int` | `30` | Valid values: 10, 30, 120 |
| `RaDecMaxPoints` | `int` | `5000` | User-selectable: 5000 / 10000 / 20000 |

**Notes:**
- The existing `MaxPoints` property is **kept unchanged** (Pulse chart uses it as a fixed 5 000-per-series cap; no Pulse UI selector is required per OQ-02).
- `DisplayMode` persists the last-used mode across window reopen.
- `RealtimeWindowSeconds` persists the last-used realtime window duration.
- `RaDecMaxPoints` persists the RA/Dec buffer size selection.
- XML doc comments required; follow the style of existing properties.

**Verification:** `dotnet build` (or Visual Studio Build) — 0 errors.

---

### Step 1.2 — Extend `IVersionedSettingsService` (no-op interface change)

**File:** `GreenSwamp.Alpaca.Settings\Services\IVersionedSettingsService.cs`

The `GetChartSettings()`, `SaveChartSettingsAsync()`, and `ChartSettingsChanged` members **already exist** in this interface (confirmed in context). No changes needed to the interface.

**Action:** Read-only verification — confirm `ChartSettingsPath` property also exists. If absent, add:

```csharp
/// <summary>Gets the path to chart.settings.user.json for the current version.</summary>
string ChartSettingsPath { get; }
```

**Verification:** Confirm the concrete `VersionedSettingsService` implementation compiles with the interface. Build — 0 errors.

---

### Step 1.3 — Verify settings service implementation handles new fields

**File:** `GreenSwamp.Alpaca.Settings\Services\VersionedSettingsService.cs` (or equivalent concrete class)

- Locate the `GetChartSettings()` and `SaveChartSettingsAsync()` implementations.
- Confirm they use `System.Text.Json` deserialization with default-on-missing-property behaviour (i.e., a missing `DisplayMode` key in the JSON file produces the default `"Realtime"` value). This is the standard .NET behaviour for `JsonSerializer.Deserialize<T>` — no change needed.
- If the implementation uses a custom `JsonSerializerOptions` with `PropertyNameCaseInsensitive = false` or `DefaultIgnoreCondition` that would suppress defaults, add the new fields to the defaults initialiser explicitly.

**Verification:** No code change expected; confirm by reading. Build — 0 errors.

---

### Step 1.4 — Add a settings-round-trip smoke test (optional but recommended)

**File:** New test method in the existing chart-settings test class (if one exists) or a new `ChartSettingsTests.cs` in the test project.

Test: Deserialise a JSON string that **omits** the three new fields → assert defaults are correct.  
Test: Serialise a `ChartSettings` with non-default values → deserialise → assert round-trip fidelity.

**Note:** Skip this step if there is no existing settings test project. Record the decision.

---

### Phase 1 Deliverables

- `ChartSettings.cs` — 3 new properties, documented, defaults set.
- Settings service compiles cleanly with new fields.
- Build: green, 0 errors.
- No UI, no JS, no chart pages modified.

---

## Phase 2 — JavaScript Buffer Module (`gsChartBuffer.js`)

**Goal:** Author the new browser-local JS buffer module that is the core of the Option A revised architecture. Wire it into `_Layout.cshtml`. No Blazor component changes yet.  
**Risk level:** Medium — new JS module; no C# code impacted; unit-testable in browser console.  
**Build gates:** Build green after `_Layout.cshtml` change. Manual smoke test in browser console.

---

### Step 2.1 — Create `gsChartBuffer.js`

**File:** `GreenSwamp.Alpaca.Server\wwwroot\js\gsChartBuffer.js` (new file)

This module exposes a **single global object** `window.gsChartBuffer` with the following API.  
It is intentionally not an ES module (no `export`) so it can be loaded via a `<script>` tag in `_Layout.cshtml` and called by name from C# `InvokeVoidAsync` — matching the pattern already established by `chartWindowInterop.js`.

#### 2.1.1 Internal state

```javascript
window.gsChartBuffer = (() => {
	// Per-chart-type state bags keyed by chartType string ("radec" | "pulse")
	const _state = {};

	function _getOrCreate(chartType) {
		if (!_state[chartType]) {
			_state[chartType] = {
				series: {},          // seriesKey -> [{x: epochMs, y: number}]
				maxPoints: 5000,     // per-series cap; set by init()
				chartId: null,       // ApexCharts chart DOM id; set by init()
				intervalId: null,    // setInterval handle
				realtimeActive: false,
				windowMs: 30000      // realtime scroll window; set by init()
			};
		}
		return _state[chartType];
	}
	// ... (public API below)
})();
```

#### 2.1.2 `init(chartType, chartId, seriesKeys, maxPoints, windowMs)`

Called once from `OnAfterRenderAsync` of the Blazor component.

- Creates the state bag for `chartType`.
- Sets `chartId`, `maxPoints`, `windowMs`.
- Initialises each `seriesKey` in `seriesKeys` to an empty array.
- Does **not** start the timer (timer is started separately via `setRealtimeActive`).

#### 2.1.3 `add(chartType, seriesKey, x, y)`

Called by the C# SignalR handler via `InvokeVoidAsync("gsChartBuffer.add", ...)`.

- Pushes `{ x, y }` onto `_state[chartType].series[seriesKey]`.
- If the series length exceeds `maxPoints`, removes the oldest element (`shift()`).

**Design note:** `x` is a Unix epoch millisecond timestamp (long, serialised as JS `number`); `y` is a `double`. Both are JSON-serialisable without special handling.

#### 2.1.4 `setRealtimeActive(chartType, active)`

Called by C# to start/stop the JS timer.

- If `active == true` and no interval is running: start `setInterval(_tick, 1000)`.
- If `active == false`: `clearInterval` and null the handle.
- Store the active state.

#### 2.1.5 `_tick(chartType)` (internal)

Called by `setInterval` every 1 000 ms.

```javascript
function _tick(chartType) {
	const s = _state[chartType];
	if (!s || !s.realtimeActive || !s.chartId) return;

	const nowMs = Date.now();
	const cutMs = nowMs - s.windowMs;

	// Build the windowed series array for ApexCharts updateSeries
	const seriesData = Object.entries(s.series).map(([key, pts]) => ({
		name: key,
		data: pts.filter(p => p.x >= cutMs)
	}));

	// Native ApexCharts exec — zero JSInterop, pure JS
	if (window.ApexCharts) {
		ApexCharts.exec(s.chartId, 'updateSeries', seriesData);
	}
}
```

**Design note:** `ApexCharts.exec(chartId, 'updateSeries', data)` is the correct native API for driving series updates from outside the Blazor wrapper. The chart `id` must match the `Id` property set on `<ApexChart>` in the Blazor markup.

#### 2.1.6 `getAll(chartType)`

Returns the full series data for all series in `_state[chartType].series` as an array of `{ name, data }` objects. Used by the Historical refresh path in C#:

```javascript
getAll(chartType) {
	const s = _state[chartType];
	if (!s) return [];
	return Object.entries(s.series).map(([key, pts]) => ({ name: key, data: [...pts] }));
}
```

#### 2.1.7 `getPointCount(chartType, seriesKey)`

Returns the current buffer size for a given series. Used by C# to display a buffer-fill indicator in the toolbar (nice-to-have; implement as a stub returning `_state[chartType]?.series[seriesKey]?.length ?? 0`).

#### 2.1.8 `exportCsv(chartType, filename)`

Builds a CSV from the full buffer (not the rendered chart) and triggers a browser download.

```javascript
exportCsv(chartType, filename) {
	const s = _state[chartType];
	if (!s) return;
	const rows = ['Timestamp,Series,Value'];
	for (const [key, pts] of Object.entries(s.series)) {
		for (const p of pts) {
			rows.push(`${new Date(p.x).toISOString()},${key},${p.y}`);
		}
	}
	const blob = new Blob([rows.join('\n')], { type: 'text/csv' });
	const url = URL.createObjectURL(blob);
	const a = document.createElement('a');
	a.href = url; a.download = filename;
	document.body.appendChild(a); a.click();
	document.body.removeChild(a);
	URL.revokeObjectURL(url);
}
```

#### 2.1.9 `clear(chartType)`

Stops the timer and resets all series arrays to empty. Called from C# `DisposeAsync`.

```javascript
clear(chartType) {
	const s = _state[chartType];
	if (!s) return;
	if (s.intervalId) { clearInterval(s.intervalId); s.intervalId = null; }
	s.realtimeActive = false;
	for (const key of Object.keys(s.series)) s.series[key] = [];
}
```

#### 2.1.10 `updateSettings(chartType, maxPoints, windowMs)`

Called by C# when the user changes RA/Dec MaxPoints or realtime window duration without a full page reload. Updates the in-memory caps immediately.

---

### Step 2.2 — Register `gsChartBuffer.js` in `_Layout.cshtml`

**File:** `GreenSwamp.Alpaca.Server\Pages\_Layout.cshtml`

Add the script tag **after** `chartWindowInterop.js` (ApexCharts must already be loaded by the Blazor-ApexCharts bundle at this point):

```html
<script src="js/gsChartBuffer.js" asp-append-version="true"></script>
```

The final script block order becomes:
1. `MudBlazor.min.js`
2. `blazor.server.js`
3. `profileUtils.js`
4. `floatingWindow.js`
5. `chartWindowInterop.js`
6. `gsChartBuffer.js`  ← new

**Important:** `gsChartBuffer.js` must load **after** `blazor.server.js` because Blazor must be initialised before `_tick` can be called (though the timer itself is started from `OnAfterRenderAsync`, not at script load time).

**Verification:**
- `dotnet build` — 0 errors.
- Launch app in browser → open DevTools Console → type `window.gsChartBuffer` → should return the object (not `undefined`).

---

### Step 2.3 — Manual smoke test of the JS buffer API

**No code change.** In the browser DevTools Console on any page (after Step 2.2 is deployed):

```javascript
// Smoke test sequence
gsChartBuffer.init('test', 'myChart', ['s1', 's2'], 100, 10000);
gsChartBuffer.add('test', 's1', Date.now(), 42.5);
gsChartBuffer.add('test', 's1', Date.now(), 43.1);
gsChartBuffer.getAll('test');   // should return [{name:'s1', data:[...]}, {name:'s2', data:[]}]
gsChartBuffer.clear('test');
gsChartBuffer.getAll('test');   // should return [{name:'s1', data:[]}, ...]
```

Record the results before proceeding to Phase 3.

---

### Phase 2 Deliverables

- `wwwroot\js\gsChartBuffer.js` — complete buffer module, all 9 public methods.
- `_Layout.cshtml` — new script tag added in correct position.
- Build: green, 0 errors.
- Console smoke test passing.
- No Blazor component code changed yet.

---

## Phase 3 — RA/Dec Chart Component Redesign

**Goal:** Replace `RaDecChart.razor` and `RaDecChart.razor.cs` with the new implementation that uses the JS buffer, the new display-mode toolbar, the rebuilt options builder, and the new dispose pattern. The Pulse chart is **not** touched in Phase 3 (it is Phase 4).  
**Risk level:** High — replaces a functioning page; existing users of the old page are affected.  
**Mitigation:** Keep the old files side-by-side with a new route suffix during development, then swap routes only when the new implementation passes manual testing.  
**Build gates:** Build green after every sub-step.

---

### Step 3.1 — Read and document the full existing `RaDecChart.razor.cs`

**Action:** Read `RaDecChart.razor.cs` lines 1–291 in full to capture:
- All injected services and fields
- The `OnInitializedAsync` / `OnAfterRenderAsync` lifecycle sequence
- The SignalR hub connection setup
- The `BuildChartOptions()` content
- The `FlushChartUpdate` / timer disposal pattern
- All toolbar event handlers

This is a read-only step — no edits. The goal is to produce the exact field-for-field mapping between old and new before writing a single line of replacement code.

---

### Step 3.2 — Design the new field layout for `RaDecChart.razor.cs`

**Action (planning, no code yet):** Define the new private field set:

```csharp
// Services
[Inject] private IVersionedSettingsService SettingsService { get; set; } = null!;
[Inject] private IHubConnectionBuilder HubConnectionBuilder { get; set; } = null!;
[Inject] private IJSRuntime JS { get; set; } = null!;

// Chart component reference
private ApexChart<ChartPointDto>? _chart;
private ApexChartOptions<ChartPointDto> _chartOptions = new();

// Settings (loaded in OnInitializedAsync)
private ChartSettings _settings = new();

// SignalR
private HubConnection? _hubConnection;

// State
private string _chartId = "radec-chart";          // must match @id in markup
private bool _disposed;
```

**Removed from prototype:**
- `List<ChartPointDto> _axis1Points` / `_axis2Points` — replaced by JS buffer
- `System.Threading.Timer _timer` — replaced by JS setInterval in gsChartBuffer
- `bool _hasNewData` — no longer needed

---

### Step 3.3 — Rewrite `RaDecChart.razor.cs` code-behind

**File:** `GreenSwamp.Alpaca.Server\Pages\Charts\RaDecChart.razor.cs`

Implement the following methods in order. **Edit incrementally — build after each logical group.**

#### Group A: Lifecycle

**`OnInitializedAsync`:**
1. Load `_settings = SettingsService.GetChartSettings()`.
2. Call `BuildChartOptions()` (sets `_chartOptions` for the initial mode).
3. Build the `HubConnection` to `/charthub`.
4. Register SignalR handlers:
   - `OnAxis1Point(ChartPointDto pt)` → `await JS.InvokeVoidAsync("gsChartBuffer.add", "radec", "Axis 1", pt.Timestamp.ToUnixTimeMilliseconds(), ConvertValue(pt.Value, "axis1"))`.
   - `OnAxis2Point(ChartPointDto pt)` → same pattern for `"Axis 2"`.
   - `OnHistory(IEnumerable<ChartPointDto> axis1, IEnumerable<ChartPointDto> axis2)` → iterate and call `gsChartBuffer.add` for each historical point (no display update yet — the chart will render via `RefreshHistoricalAsync` after history is loaded).
5. Start the hub connection.
6. Call `JoinRaDecGroupAsync(DeviceNumber)`.
7. Call `RequestHistoricalDataAsync(DeviceNumber)`.

**`OnAfterRenderAsync(bool firstRender)`:**
- Only on `firstRender`:
  1. Call `await JS.InvokeVoidAsync("gsChartBuffer.init", "radec", _chartId, new[] { "Axis 1", "Axis 2" }, _settings.RaDecMaxPoints, _settings.RealtimeWindowSeconds * 1000)`.
  2. If `_settings.DisplayMode == "Realtime"`, call `await JS.InvokeVoidAsync("gsChartBuffer.setRealtimeActive", "radec", true)`.
  3. If `_settings.DisplayMode == "Historical"`, call `await RefreshHistoricalAsync()`.

**`DisposeAsync`:**
1. Guard against double-dispose with `_disposed`.
2. `await JS.InvokeVoidAsync("gsChartBuffer.clear", "radec")` — stops timer, clears buffer. Wrap in try/catch JSDisconnectedException (circuit may already be gone).
3. `await _hubConnection!.StopAsync()`.
4. `await _hubConnection.DisposeAsync()`.

#### Group B: Options builder

**`BuildChartOptions()`:**

Replaces the prototype's single `BuildChartOptions()` with a mode-aware version:

```csharp
private void BuildChartOptions()
{
	var yTitle = _settings.RaDecScale switch
	{
		"Degrees"    => "Degrees",
		"ArcSeconds" => "Arc-seconds",
		_            => "Steps"
	};

	if (_settings.DisplayMode == "Realtime")
		BuildRealtimeOptions(yTitle);
	else
		BuildHistoricalOptions(yTitle);
}

private void BuildRealtimeOptions(string yTitle) { /* per Section 6.1 of requirements */ }
private void BuildHistoricalOptions(string yTitle) { /* per Section 6.2 of requirements */ }
```

The exact option skeletons are in `ChartRedesign-Requirements.md` §6.1 and §6.2.

Realtime tick amounts:
- 10 s window → `TickAmount = 10`
- 30 s window → `TickAmount = 6`
- 120 s window → `TickAmount = 12`

#### Group C: Mode switch

**`OnModeChangedAsync(string? newMode)`:**
- Implement exactly per §6.3 of the requirements document.
- Pause JS timer → `UpdateOptionsAsync` → either `RefreshHistoricalAsync` or resume JS timer.
- Persist settings.

**`OnWindowChangedAsync(int seconds)`:**
- Update `_settings.RealtimeWindowSeconds`.
- Call `await JS.InvokeVoidAsync("gsChartBuffer.updateSettings", "radec", _settings.RaDecMaxPoints, seconds * 1000)`.
- Rebuild options and `UpdateOptionsAsync`.
- Persist settings.

**`OnMaxPointsChangedAsync(int newMax)`:**
- Update `_settings.RaDecMaxPoints`.
- Call `await JS.InvokeVoidAsync("gsChartBuffer.updateSettings", "radec", newMax, _settings.RealtimeWindowSeconds * 1000)`.
- Persist settings.
- **Note:** Changing max points does not clear the existing buffer; new points will cap at the new limit; old points beyond the new limit will drain naturally.

#### Group D: Scale and series visibility

**`OnScaleChangedAsync(string scale)`:**
- Set `_settings.RaDecScale = scale`.
- Persist settings.
- Rebuild options and `UpdateOptionsAsync`.
- If in Historical mode: call `RefreshHistoricalAsync()` (y-values need reconverting — see conversion note below).

**`OnAxis1VisibilityChanged(bool show)` / `OnAxis2VisibilityChanged(bool show)`:**
- Set `_settings.ShowAxis1` / `ShowAxis2`.
- Persist settings.
- Call `_chart.ToggleSeriesAsync("Axis 1")` or `"Axis 2"`.

**Y-value conversion note:**  
The `gsChartBuffer.add` call stores the converted Y value (Steps/Degrees/ArcSeconds) at the point of ingestion. When the scale changes, the stored values are in the old unit. Two strategies:

- **Strategy A (simpler):** Store raw Steps always in the buffer; convert on `getAll()` read via a JS-side converter parameter. This avoids re-buffering.
- **Strategy B (current prototype approach):** Clear buffer and re-request historical data from the server when scale changes.

**Recommendation:** Use Strategy B for Phase 3 (matches prototype behaviour, lower JS complexity). Strategy A can be a Phase 5 optimisation if needed. When scale changes: call `gsChartBuffer.clear("radec")` (resets but does not re-init), then re-request history from the hub, then rebuild options. This means the buffer fills from scratch on scale change — acceptable for an astronomy tool.

#### Group E: Export

**`ExportPngAsync()`:**
```csharp
private async Task ExportPngAsync()
{
	var uri = await _chart!.GetDataUriAsync();
	await JS.InvokeVoidAsync("chartWindowInterop.downloadDataUri", uri, $"radec-{DateTime.Now:yyyyMMdd-HHmmss}.png");
}
```

**`ExportCsvAsync()`:**
```csharp
private async Task ExportCsvAsync()
{
	var filename = $"radec-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
	await JS.InvokeVoidAsync("gsChartBuffer.exportCsv", "radec", filename);
}
```

**CSV scope:** Full buffer (all points held in JS memory), not just the visible window. Per OQ-04.

#### Group F: Historical refresh helper

**`RefreshHistoricalAsync()`:**
```csharp
private async Task RefreshHistoricalAsync()
{
	// Read entire JS buffer back into C# as JSON
	var allSeries = await JS.InvokeAsync<JsonElement>("gsChartBuffer.getAll", "radec");
	// Parse and call _chart.UpdateSeriesAsync with full data
	// (exact mapping: allSeries[i].name -> series name, allSeries[i].data -> List<T>)
}
```

**Note on `UpdateSeriesAsync` vs direct JS exec in Historical mode:**  
Historical mode uses the Blazor wrapper's `UpdateSeriesAsync` (not `ApexCharts.exec`) to avoid the state-divergence risk noted in the requirements. The JS timer is paused in Historical mode, so there is no race.

---

### Step 3.4 — Rewrite `RaDecChart.razor` markup

**File:** `GreenSwamp.Alpaca.Server\Pages\Charts\RaDecChart.razor`

#### Toolbar layout (MudBlazor)

```
MudToolBar
  ├── MudToggleGroup (Realtime | Historical)         — mode selector
  ├── [if Realtime] MudSelect<int> (10s / 30s / 120s) — window duration
  ├── [if Realtime] MudSelect<int> (5k / 10k / 20k)   — max points (RA/Dec only)
  ├── MudSelect<string> (Steps / Degrees / Arc-seconds) — Y scale
  ├── MudSwitch   — Show Axis 1
  ├── MudSwitch   — Show Axis 2
  ├── [if Historical] MudIconButton (Refresh)         — manual refresh
  └── MudMenu (Export)
		├── Export PNG
		└── Export CSV
```

**MudToggleGroup details** (confirmed via MudBlazor MCP):
```razor
<MudToggleGroup T="string" Value="_settings.DisplayMode"
				ValueChanged="OnModeChangedAsync"
				SelectionMode="SelectionMode.SingleSelection"
				Size="Size.Small" Color="Color.Primary">
	<MudToggleItem Value="@("Realtime")">Realtime</MudToggleItem>
	<MudToggleItem Value="@("Historical")">Historical</MudToggleItem>
</MudToggleGroup>
```

**Conditional toolbar sections** use `@if (_settings.DisplayMode == "Realtime")`.

#### Chart component

```razor
<ApexChart TItem="ChartPointDto"
		   @ref="_chart"
		   Options="_chartOptions"
		   Id="@_chartId"
		   Height="@("100%")">
	<ApexPointSeries TItem="ChartPointDto"
					 Name="Axis 1"
					 Items="@(Array.Empty<ChartPointDto>())"
					 SeriesType="SeriesType.Line"
					 XValue="@(p => p.Timestamp.ToUnixTimeMilliseconds())"
					 YValue="@(p => (decimal)ConvertValue(p.Value, "axis1"))"
					 Color="#b71c1c" />
	<ApexPointSeries TItem="ChartPointDto"
					 Name="Axis 2"
					 Items="@(Array.Empty<ChartPointDto>())"
					 SeriesType="SeriesType.Line"
					 XValue="@(p => p.Timestamp.ToUnixTimeMilliseconds())"
					 YValue="@(p => (decimal)ConvertValue(p.Value, "axis2"))"
					 Color="#2196f3" />
</ApexChart>
```

**Important:** Series `Items` are bound to empty arrays because realtime data is pushed via `ApexCharts.exec` from JS. The wrapper's series model is only used in Historical mode via `UpdateSeriesAsync`. This intentional decoupling is the core of Option A revised.

**`Id` on `<ApexChart>` must exactly match `_chartId` in code-behind** (`"radec-chart"`) so `ApexCharts.exec("radec-chart", ...)` in the JS timer finds the correct instance.

---

### Step 3.5 — Add Y-value conversion helper

**File:** `RaDecChart.razor.cs`

```csharp
private double ConvertValue(double rawSteps, string axis)
{
	return _settings.RaDecScale switch
	{
		"Degrees"    => rawSteps * 360.0 / StepsPerRevolution(axis),
		"ArcSeconds" => rawSteps * 360.0 / StepsPerRevolution(axis) * 3600.0,
		_            => rawSteps
	};
}

private double StepsPerRevolution(string axis)
{
	// Read from device settings for the current DeviceNumber
	var dev = SettingsService.GetDeviceSettings(DeviceNumber);
	return axis == "axis1"
		? dev?.Axis1StepsPerRevolution ?? 9_024_000.0
		: dev?.Axis2StepsPerRevolution ?? 9_024_000.0;
}
```

**Note:** The exact property names for `StepsPerRevolution` on `SkySettings` need to be confirmed against the actual model before coding. If they differ, update accordingly.

---

### Step 3.6 — End-to-end manual test plan for Phase 3

Perform these checks before declaring Phase 3 done:

| Test | Expected result |
|---|---|
| Open `/charts/radec/0` in a new window | Page loads; toolbar visible; Realtime mode selected |
| Mount data flowing | Chart scrolls in realtime; x-axis shows last 30 s |
| Switch to Historical | Timer pauses; full buffer renders; zoom toolbar appears |
| Refresh (Historical) | Chart re-renders with latest buffered data |
| Switch back to Realtime | Timer resumes; chart scrolls again |
| Change window to 10 s | Chart re-renders with 10 s window; x-axis shows 10 ticks |
| Change Y scale | Chart re-renders with new Y values; settings persisted |
| Toggle Axis 1 off | Axis 1 series hidden |
| Export PNG | PNG file downloads |
| Export CSV | CSV downloads with all buffered points |
| Close window | DevTools: no JS errors on dispose |
| Reopen window | Previous mode/window/scale restored from settings |

---

### Step 3.7 — Route swap and prototype cleanup

**Only after Step 3.6 tests pass:**

- Remove the temporary test route suffix (if one was used).
- Confirm the old prototype code is fully replaced (not left as dead code).
- Build — 0 errors.
- Commit: `feat: replace RaDecChart prototype with Option A revised (JS buffer + JS timer)`

---

### Phase 3 Deliverables

- `RaDecChart.razor.cs` — fully rewritten; no C# timer; JS buffer via interop.
- `RaDecChart.razor` — toolbar with mode toggle, window/maxpoints selectors (conditional), scale select, visibility switches, export menu.
- All manual tests from Step 3.6 passing.
- Build: green, 0 errors.
- Pulse chart untouched (Phase 4).

---

## Cross-phase dependency summary

```
Phase 1 (Settings model) ──────────────────────────────► must complete first
	│
	▼
Phase 2 (gsChartBuffer.js + _Layout registration) ─────► must complete before Phase 3
	│
	▼
Phase 3 (RaDecChart redesign) ──────────────────────────► uses Phase 1 settings + Phase 2 buffer
	│
	▼
Phase 4 (PulseChart redesign) — not in scope of this document
```

---

## Key risks and mitigations

| Risk | Mitigation |
|---|---|
| `ApexCharts.exec` finds wrong instance (multiple chart windows open) | Each chart page uses a unique `_chartId`; `gsChartBuffer` state is keyed by `chartType` string |
| Blazor wrapper state diverges from native exec | Historical mode always uses `UpdateSeriesAsync` (wrapper); realtime uses exec (JS); no mixing during the same mode |
| JSDisconnectedException on dispose | Wrap all `InvokeVoidAsync` in dispose with `try/catch JSDisconnectedException` |
| Scale change loses historical data | Accept Strategy B (clear + re-request); document as known limitation; add progress indicator |
| `setInterval` leaks if page is navigated away without proper dispose | `clear()` in `DisposeAsync` stops all intervals; guard with `_disposed` flag |
| Y-value conversion: wrong `StepsPerRevolution` property name | Confirm property names against `SkySettings` model before Step 3.5 |

---

## Files to be created / modified

| File | Action | Phase |
|---|---|---|
| `GreenSwamp.Alpaca.Settings\Models\ChartSettings.cs` | Modify — add 3 new properties | 1 |
| `GreenSwamp.Alpaca.Settings\Services\IVersionedSettingsService.cs` | Read / possibly add `ChartSettingsPath` | 1 |
| `GreenSwamp.Alpaca.Server\wwwroot\js\gsChartBuffer.js` | Create — new buffer module | 2 |
| `GreenSwamp.Alpaca.Server\Pages\_Layout.cshtml` | Modify — add script tag | 2 |
| `GreenSwamp.Alpaca.Server\Pages\Charts\RaDecChart.razor.cs` | Rewrite — full replacement | 3 |
| `GreenSwamp.Alpaca.Server\Pages\Charts\RaDecChart.razor` | Rewrite — full replacement | 3 |

**Not modified in Phases 1–3:**
- `PulseChart.razor` / `PulseChart.razor.cs` (Phase 4)
- `ChartHub.cs` (no changes required)
- `ChartSettings` persisted JSON schema (backward-compatible additions only)
- `chartWindowInterop.js` (PNG export still uses its `downloadDataUri` helper)

---

*End of Plan — Phases 1–3*  
*Next document: Phase 4 plan (PulseChart redesign) — to be created after Phase 3 is complete.*
