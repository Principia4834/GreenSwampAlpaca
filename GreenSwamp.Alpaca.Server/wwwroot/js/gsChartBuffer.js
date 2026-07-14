// gsChartBuffer.js
// Browser-local data buffer for the RA/Dec and Pulse chart windows.
//
// Architecture: Option A revised (JS timer)
//   - C# SignalR handlers write each incoming point via gsChartBuffer.add() (one JSInterop call per point).
//   - A JS setInterval drives realtime chart updates natively via ApexCharts.exec(), with zero JSInterop.
//   - The Blazor wrapper (ApexChart<T>) is used only for options changes, historical refresh, and PNG export.
//
// Usage (called from C# OnAfterRenderAsync):
//   gsChartBuffer.init('radec', 'radec-chart', ['Axis 1', 'Axis 2'], 5000, 30000)
//   gsChartBuffer.setRealtimeActive('radec', true)
//
// Per-point write (called from C# SignalR handler):
//   gsChartBuffer.add('radec', 'Axis 1', epochMs, yValue)
//
// Historical refresh (called from C# RefreshHistoricalAsync):
//   const allSeries = gsChartBuffer.getAll('radec')   // returns [{name, data:[{x,y}]}]
//
// Cleanup (called from C# DisposeAsync):
//   gsChartBuffer.clear('radec')

window.gsChartBuffer = (() => {

    // -- Internal state ------------------------------------------------------------
    // Keyed by chartType string (e.g. 'radec', 'pulse').
    // Each entry is created by init() and cleared by clear().
    const _state = {};

    function _ensureState(chartType) {
        if (!_state[chartType]) {
            _state[chartType] = {
                seriesKeys: [],      // ordered series names; set by init()
                series: {},          // seriesKey -> [{x: epochMs, y: number}]
                maxPoints: 5000,     // per-series cap; set by init() / updateSettings()
                chartId: null,       // ApexCharts chart.id; set by init()
                intervalId: null,    // setInterval handle
                realtimeActive: false,
                windowMs: 30000      // realtime scroll window ms; set by init() / updateSettings()
            };
        }
        return _state[chartType];
    }

    // -- Realtime tick (internal) --------------------------------------------------
    // Called every 1 000 ms by setInterval when realtimeActive is true.
    // Slices the in-memory buffer to the current rolling window and drives a native
    // ApexCharts.exec updateSeries call — no JSInterop involved.
    function _tick(chartType) {
        const s = _state[chartType];
        if (!s || !s.realtimeActive || !s.chartId) return;

        const nowMs = Date.now();
        const cutMs = nowMs - s.windowMs;

        // Build the windowed series array in the same order as init() received them.
        // ApexCharts updateSeries matches by series index, so order must be stable.
        const seriesData = s.seriesKeys.map(key => ({
            name: key,
            data: s.series[key].filter(p => p.x >= cutMs)
        }));

        // ApexCharts.exec is the native static API for driving updates from outside
        // the Blazor wrapper. This is zero-JSInterop from the server perspective.
        if (window.ApexCharts) {
            ApexCharts.exec(s.chartId, 'updateSeries', seriesData);
        }
    }

    // -- Public API ----------------------------------------------------------------
    return {

        // init(chartType, chartId, seriesKeys, maxPoints, windowMs)
        // Called once from OnAfterRenderAsync (firstRender only).
        // Does NOT start the realtime timer — call setRealtimeActive(chartType, true) separately.
        init: (chartType, chartId, seriesKeys, maxPoints, windowMs) => {
            const s = _ensureState(chartType);
            s.chartId = chartId;
            s.maxPoints = maxPoints;
            s.windowMs = windowMs;
            s.seriesKeys = [...seriesKeys];
            // Initialise each series bucket to an empty array.
            for (const key of seriesKeys) {
                if (!s.series[key]) s.series[key] = [];
            }
        },

        // add(chartType, seriesKey, x, y)
        // Called by the C# SignalR handler for each incoming data point.
        // x: Unix epoch milliseconds (JS number). y: numeric value (double).
        // Enforces the per-series maxPoints cap by dropping the oldest point (FIFO).
        add: (chartType, seriesKey, x, y) => {
            const s = _state[chartType];
            if (!s) return;
            const buf = s.series[seriesKey];
            if (!buf) return;
            buf.push({ x, y });
            if (buf.length > s.maxPoints) buf.shift();
        },

        // setRealtimeActive(chartType, active)
        // Starts or stops the 1-second JS timer that drives native chart updates.
        // Called by C# when switching display modes or on first render.
        setRealtimeActive: (chartType, active) => {
            const s = _state[chartType];
            if (!s) return;
            if (active && !s.intervalId) {
                s.realtimeActive = true;
                s.intervalId = setInterval(() => _tick(chartType), 1000);
            } else if (!active && s.intervalId) {
                clearInterval(s.intervalId);
                s.intervalId = null;
                s.realtimeActive = false;
            }
        },

        // getAll(chartType)
        // Returns the full series buffer as [{name, data:[{x,y}]}].
        // Called by C# RefreshHistoricalAsync to feed UpdateSeriesAsync via the Blazor wrapper.
        getAll: (chartType) => {
            const s = _state[chartType];
            if (!s) return [];
            return s.seriesKeys.map(key => ({
                name: key,
                data: [...s.series[key]]   // shallow copy to avoid mutation during iteration
            }));
        },

        // getPointCount(chartType, seriesKey)
        // Returns the current buffer fill for a given series.
        // Used by C# to display a buffer-fill indicator in the toolbar.
        getPointCount: (chartType, seriesKey) => {
            return _state[chartType]?.series[seriesKey]?.length ?? 0;
        },

        // exportCsv(chartType, filename)
        // Builds a CSV from the FULL buffer (not just the visible realtime window)
        // and triggers a browser file download. Pure JS — no JSInterop required.
        // CSV format: ISO timestamp, series name, value — one row per data point,
        // sorted by timestamp ascending across all series.
        exportCsv: (chartType, filename) => {
            const s = _state[chartType];
            if (!s) return;

            // Collect all points from every series with a series label column.
            const rows = [['Timestamp', 'Series', 'Value']];
            for (const key of s.seriesKeys) {
                for (const p of s.series[key]) {
                    rows.push([new Date(p.x).toISOString(), key, p.y]);
                }
            }

            // Sort by timestamp (column 0) ascending so the CSV reads chronologically.
            rows.sort((a, b) => {
                if (a[0] === 'Timestamp') return -1;  // keep header first
                return a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0;
            });

            const csv = rows.map(r => r.join(',')).join('\n');
            const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        },

        // updateSettings(chartType, maxPoints, windowMs)
        // Called by C# when the user changes RaDecMaxPoints or RealtimeWindowSeconds
        // without a full page reload. Updates in-memory caps immediately.
        // Note: reducing maxPoints does not trim existing buffer entries; the cap
        // is enforced on the next add() call (oldest points drain naturally).
        updateSettings: (chartType, maxPoints, windowMs) => {
            const s = _state[chartType];
            if (!s) return;
            s.maxPoints = maxPoints;
            s.windowMs = windowMs;
        },

        // clearSeries(chartType)
        // Empties all series arrays but keeps the state bag and timer alive.
        // Called by C# when the Y-scale changes and the buffer must be refilled
        // from a fresh server history request.
        clearSeries: (chartType) => {
            const s = _state[chartType];
            if (!s) return;
            for (const key of s.seriesKeys) s.series[key] = [];
        },

        // clear(chartType)
        // Stops the realtime timer and resets the entire state bag to empty buffers.
        // Called by C# DisposeAsync. Safe to call if the state bag does not exist.
        clear: (chartType) => {
            const s = _state[chartType];
            if (!s) return;
            if (s.intervalId) {
                clearInterval(s.intervalId);
                s.intervalId = null;
            }
            s.realtimeActive = false;
            for (const key of s.seriesKeys) s.series[key] = [];
        }
    };
})();
