## :bust_in_silhouette:  (19/07/2026 17:46)
I have implemented a chart feature which uses SignalR to transport data and uses MudBlazor and ApexCharts to create and render the charts. This has high bandwidth traffic between the server app and the browser which I suspect is due to teh provision of htnl and svg constructs to render the chart. I want to explore migrating the Blazor approach to a native javascript. This is the native codebase for ApexCharts so the conop osto use SignalR to send data to a locally hosted  Javascript buffer which apex charts then consumes and renders directly in the browser. I have a prototype javascript chart buffer file which may be useful. Assess the feasibility and blockers to this approach. Give me a short summary report on screen. You have access to the ApexCharts MCP and Microsoft Learn - use them
## :speech_balloon: GitHub Copilot

1. Read gsChartBuffer.js, lines 1-400 :white_check_mark:
1. Read RaDecChart.razor.cs, lines 155-503 :white_check_mark:
1. Ran Microsoft Docs Search :white_check_mark:
1. Ran apexcharts_get_reference :white_check_mark:
1. Ran Microsoft Docs Fetch :white_check_mark:
1. Read gsChartBuffer.js, lines 129-235 :white_check_mark:
1. Read ChartDataService.cs, lines 126-236 :white_check_mark:
Andy, feasibility is **good**. The existing server-side SignalR pipeline is already close to what a native browser-rendered ApexCharts implementation needs.

## Short feasibility report

### Viable approach

Move chart rendering/data buffering out of Blazor and into browser JavaScript:

- Keep `ChartDataService` and `ChartHub`.
- Use the SignalR **JavaScript client** directly in the browser.
- Browser JS connects to `/charthub`, calls `JoinRaDecGroupAsync(DeviceNumber)`, receives `ReceiveAxisPoint`, buffers `{ x, y }` points locally, and calls native ApexCharts `updateSeries`.
- Blazor can remain as a light shell for navigation/settings, or the chart window can become mostly static HTML/JS.

This should reduce the suspected high bandwidth caused by Blazor Server/Apex wrapper updates, because the server would send only compact data messages, not chart UI diffs or wrapper-driven update traffic.

### Existing code supports this well

Evidence from current code:

- `ChartDataService` already broadcasts compact point DTOs:

  `ReceiveAxisPoint` sends:
```plaintext
[ { TimestampMs, Value }, { TimestampMs, Value } ]
```

- `ChartHub` already has group membership methods:
  - `JoinRaDecGroupAsync`
  - `LeaveRaDecGroupAsync`
  - `RequestHistoricalDataAsync`

- `gsChartBuffer.js` already has useful browser-side primitives:
  - local per-series buffers
  - rolling window slicing
  - `ApexCharts.exec(chartId, "updateSeries", seriesData)`
  - CSV export
  - timer-driven chart refresh

ApexCharts expects datetime line data as `{ x: epochMs, y: value }`, which matches the planned browser buffer shape.

## Main blockers / migration tasks

### 1. Current prototype still depends on Blazor-to-JSInterop

`gsChartBuffer.js` comments describe this path:

> C# SignalR handlers write each incoming point via `gsChartBuffer.add()`

That still costs one JS interop call per point. For the intended architecture, the JavaScript file should own the SignalR connection directly:
```plaintext
SignalR JS client -> local JS buffer -> native ApexCharts
```

Not:
```plaintext
SignalR .NET client -> Blazor component -> JSInterop -> JS buffer
```

### 2. Reconnection must rejoin groups

Microsoft SignalR JS reconnect creates a new server connection ID after reconnect. Since `ChartHub` tracks group membership per connection ID, the JS client must re-run:

- `JoinRaDecGroupAsync(DeviceNumber)`
- optionally `RequestHistoricalDataAsync("radec", DeviceNumber)`

inside `onreconnected`.

### 3. Avoid duplicate hub connections

During migration, do not keep both:

- the existing Blazor `.NET HubConnection` in `RaDecChart.razor.cs`
- a new browser `signalR.HubConnection`

subscribed to the same chart.

That would double subscriber counts and may duplicate traffic.

### 4. Scaling logic must move or be clarified

Current Blazor code applies:

- `ScaleValue(...)`
- `RescaleRaDecChartData()`
- settings-driven scale changes: `Steps`, `Degrees`, `ArcSeconds`

If JS consumes raw `ChartPointDto.Value`, then one of these must happen:

- move scale conversion into JS, or
- send already-scaled values from the server, or
- add scale metadata/calibration data to the browser.

This is likely the biggest functional migration detail.

### 5. Toolbar/settings ownership

MudBlazor currently controls:

- rolling window
- scale
- axis visibility
- logging state
- reconnect status

If the chart goes native JS, these controls need a boundary:

- keep MudBlazor toolbar and call JS only on setting changes, or
- migrate the chart window UI to HTML/JS fully.

A hybrid is feasible, but the chart itself should not use `ApexChart<T>` for realtime updates.

### 6. Historical load shape needs JS handler

`RequestHistoricalDataAsync("radec", DeviceNumber)` currently sends `HistoricalDataDto`. The JS client needs a `ReceiveRaDecHistory` handler that maps:
```plaintext
AxisOnePoints -> Axis 1 [{ x, y }]
AxisTwoPoints -> Axis 2 [{ x, y }]
```

then calls `updateSeries`.

### 7. Still measure network after migration

This will remove Blazor chart-render/update traffic, but not the raw data stream. If telescope state events are very frequent, server-to-browser SignalR point traffic may still be high.

Possible later optimizations:

- server-side batching
- client-side `updateSeries` at fixed cadence only
- SignalR MessagePack protocol
- dropping intermediate points when browser is behind
- downsampling for display while retaining full CSV/log data

## Recommendation

Proceed with a prototype migration for **RA/Dec only**:

1. Create a native JS chart module that owns:
   - SignalR connection
   - group join/leave
   - reconnect/rejoin
   - history load
   - local buffer
   - ApexCharts render/update

2. Change `RaDecChart.razor` into a lightweight host:
   - `<div id="radec-chart"></div>`
   - load/start JS after first render
   - dispose JS on page close

3. Disable the existing `.NET HubConnection` path for that page during the test.

Expected result: **material bandwidth reduction** if current traffic is dominated by Blazor Server/Apex wrapper UI diffs. The main implementation blockers are reconnection/group rejoin, scale conversion, and avoiding dual SignalR subscriptions.
