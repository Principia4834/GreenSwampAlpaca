/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using ApexCharts;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Pages.Charts
{
    public partial class PulseChart
    {
        [Parameter] public int DeviceNumber { get; set; }

        // -- State --------------------------------------------------------------

        private ChartSettings _settings = new();

        // Four backing lists — one per logical series
        private readonly List<PulsePointDto> _raData    = [];
        private readonly List<PulsePointDto> _raRejData = [];
        private readonly List<PulsePointDto> _decData   = [];
        private readonly List<PulsePointDto> _decRejData = [];

        // Rolling-window views over the backing lists
        private SubList<PulsePointDto>? _raSubList;
        private SubList<PulsePointDto>? _raRejSubList;
        private SubList<PulsePointDto>? _decSubList;
        private SubList<PulsePointDto>? _decRejSubList;

        // Paused snapshots used in Historical mode
        private List<PulsePointDto> _pausedRa    = [];
        private List<PulsePointDto> _pausedRaRej = [];
        private List<PulsePointDto> _pausedDec   = [];
        private List<PulsePointDto> _pausedDecRej = [];

        // Chart item sources: SubList in Realtime, snapshot in Historical
        private IEnumerable<PulsePointDto> ChartItemsRa    => IsHistoricalMode ? _pausedRa    : (IEnumerable<PulsePointDto>)(_raSubList    ?? _raData);
        private IEnumerable<PulsePointDto> ChartItemsRaRej => IsHistoricalMode ? _pausedRaRej : (IEnumerable<PulsePointDto>)(_raRejSubList ?? _raRejData);
        private IEnumerable<PulsePointDto> ChartItemsDec   => IsHistoricalMode ? _pausedDec   : (IEnumerable<PulsePointDto>)(_decSubList   ?? _decData);
        private IEnumerable<PulsePointDto> ChartItemsDecRej => IsHistoricalMode ? _pausedDecRej : (IEnumerable<PulsePointDto>)(_decRejSubList ?? _decRejData);

        // Display mode — session-only, not persisted
        private string _displayMode   = "Realtime";
        private bool IsRealtimeMode   => _displayMode == "Realtime";
        private bool IsHistoricalMode => _displayMode == "Historical";

        // Hub
        private HubConnection? _hub;
        private HubConnectionState _hubState = HubConnectionState.Disconnected;
        private bool _hubInitialised;

        // Chart
        private ApexChart<PulsePointDto>? _chart;
        private ApexChartOptions<PulsePointDto> _chartOptions = new();
        private string _chartKey = "pulse-init";
        private string _chartId  = "pulse";
        private const string MudDefaultAxisLabelColor = "var(--mud-palette-text-primary)";

        // Logging
        private bool _loggingActive;
        private bool _loggingBusy;

        // Lifecycle guards
        private bool _ready;
        private bool _disposed;
        private readonly CancellationTokenSource _disposeCts = new();

        // Chart update synchronisation
        private volatile bool _pendingChartUpdate;
        private int _chartUpdateInFlight;
        private System.Threading.Timer? _refreshTimer;

        // Rolling window
        private long PulseRollingWindowMs => Math.Max(1, _settings.PulseWindowSeconds) * 1000L;

        private bool CanAcceptWork()      => !_disposed && !_disposeCts.IsCancellationRequested;
        private bool CanPushChartUpdate() => CanAcceptWork() && _ready && _hubState == HubConnectionState.Connected && _chart is not null;

        /// <summary>Accepted pulse series type derived from the current PulseSeriesType setting.</summary>
        private SeriesType AcceptedSeriesType => _settings.PulseSeriesType switch
        {
            "Line" => SeriesType.Line,
            "Bars" => SeriesType.Bar,
            _      => SeriesType.Scatter  // "Points"
        };

        // -- Lifecycle ----------------------------------------------------------

        protected override Task OnInitializedAsync()
        {
            _settings    = SettingsService.GetChartSettings();
            _displayMode = "Realtime";
            _chartId     = $"pulse_{DeviceNumber}_{DateTime.Now:yyyy-MM-dd}";

            BuildChartOptions();

            _raSubList    = new SubList<PulsePointDto>(_raData,    0);
            _raRejSubList = new SubList<PulsePointDto>(_raRejData, 0);
            _decSubList   = new SubList<PulsePointDto>(_decData,   0);
            _decRejSubList = new SubList<PulsePointDto>(_decRejData, 0);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Hub construction and timer are deferred to first-render to avoid the prerender
        /// double-initialisation that Blazor Server causes when placed in OnInitializedAsync.
        /// </summary>
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            _refreshTimer ??= new System.Threading.Timer(
                _ => FlushChartUpdate(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            if (_hub is not null) return; // safety guard against double-init

            var hubUrl = Nav.ToAbsoluteUri("/charthub");
            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hub.On<PulsePointDto>("ReceivePulsePoint", OnPulsePoint);
            _hub.On<IReadOnlyList<PulsePointDto>, IReadOnlyList<PulsePointDto>>("ReceivePulseHistory", OnHistory);

            _hub.Reconnecting += _ =>
            {
                _hubState = HubConnectionState.Reconnecting;
                return InvokeAsync(StateHasChanged);
            };

            _hub.Reconnected += async _ =>
            {
                if (!CanAcceptWork()) return;
                _hubState = HubConnectionState.Connected;
                try
                {
                    await _hub!.InvokeAsync("JoinPulseGroupAsync", DeviceNumber);
                    RequestChartUpdate();
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _disposed) { }
                await InvokeAsync(StateHasChanged);
            };

            _hub.Closed += _ =>
            {
                _hubState = HubConnectionState.Disconnected;
                return InvokeAsync(StateHasChanged);
            };

            await _hub.StartAsync();
            _hubState     = _hub.State;
            _hubInitialised = true;

            await _hub.InvokeAsync("JoinPulseGroupAsync", DeviceNumber);
            await _hub.InvokeAsync("RequestHistoricalDataAsync", "pulse", DeviceNumber);

            if (_settings.AutoStartLogging)
            {
                await Logger.StartPulseLoggingAsync();
                _loggingActive = true;
            }

            _ready = true;
        }

        // -- SignalR handlers ---------------------------------------------------

        private void OnPulsePoint(PulsePointDto point)
        {
            if (!CanAcceptWork()) return;
            _ = InvokeAsync(async () =>
            {
                if (!CanAcceptWork()) return;
                AddToPulseData(point);
                if (_loggingActive) await Logger.LogPulsePointAsync(point);
                if (IsRealtimeMode) RequestChartUpdate();
            });
        }

        private void OnHistory(IReadOnlyList<PulsePointDto> ra, IReadOnlyList<PulsePointDto> dec)
        {
            if (!CanAcceptWork()) return;
            _ = InvokeAsync(async () =>
            {
                if (!CanAcceptWork()) return;

                _raData.Clear(); _raRejData.Clear();
                _decData.Clear(); _decRejData.Clear();

                var maxPts = _settings.MaxPoints > 0 ? _settings.MaxPoints : 5000;
                foreach (var p in ra.TakeLast(maxPts))
                {
                    if (p.Rejected) _raRejData.Add(p);
                    else            _raData.Add(p);
                }
                foreach (var p in dec.TakeLast(maxPts))
                {
                    if (p.Rejected) _decRejData.Add(p);
                    else            _decData.Add(p);
                }

                SetAllSubListsToRollingWindow();

                if (_chart is not null)
                {
                    try
                    {
                        await _chart.UpdateSeriesAsync(animate: false);
                        if (IsRealtimeMode) await ApplyRollingWindowViewportAsync();
                    }
                    catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException) { }
                }
                StateHasChanged();
            });
        }

        // -- Data management ----------------------------------------------------

        /// <summary>
        /// Routes an incoming pulse point to the correct backing list, applies the MaxPoints cap,
        /// then recomputes the SubList rolling-window start index from the current timestamp.
        /// </summary>
        private void AddToPulseData(PulsePointDto p)
        {
            var maxPts = _settings.MaxPoints > 0 ? _settings.MaxPoints : 5000;

            List<PulsePointDto> list;
            SubList<PulsePointDto> subList;

            if (p.Axis == 0)
            {
                list    = p.Rejected ? _raRejData    : _raData;
                subList = p.Rejected ? _raRejSubList! : _raSubList!;
            }
            else
            {
                list    = p.Rejected ? _decRejData    : _decData;
                subList = p.Rejected ? _decRejSubList! : _decSubList!;
            }

            // Cap — remove oldest if at limit (no SubList access until after SetStartIndex below)
            if (list.Count >= maxPts) list.RemoveAt(0);
            list.Add(p);

            // Recompute rolling window start from scratch — authoritative regardless of cap trim
            var cutoffMs = p.TimestampMs - PulseRollingWindowMs;
            var idx = list.FindIndex(x => x.TimestampMs >= cutoffMs);
            if (idx < 0) idx = list.Count - 1;
            subList.SetStartIndex(idx);
        }

        /// <summary>Recomputes all four SubList start indices using the latest overall timestamp.</summary>
        private void SetAllSubListsToRollingWindow()
        {
            var latestMs = GetLatestTimestampMs();
            SetSubListWindow(_raData,     _raSubList!,    latestMs);
            SetSubListWindow(_raRejData,  _raRejSubList!, latestMs);
            SetSubListWindow(_decData,    _decSubList!,   latestMs);
            SetSubListWindow(_decRejData, _decRejSubList!, latestMs);
        }

        private void SetSubListWindow(List<PulsePointDto> list, SubList<PulsePointDto> subList, long latestMs)
        {
            if (list.Count == 0 || latestMs == 0) { subList.SetStartIndex(0); return; }
            var cutoffMs = latestMs - PulseRollingWindowMs;
            var idx = list.FindIndex(p => p.TimestampMs >= cutoffMs);
            if (idx < 0) idx = list.Count - 1;
            subList.SetStartIndex(idx);
        }

        private long GetLatestTimestampMs()
        {
            long max = 0;
            if (_raData.Count    > 0) max = Math.Max(max, _raData[^1].TimestampMs);
            if (_raRejData.Count > 0) max = Math.Max(max, _raRejData[^1].TimestampMs);
            if (_decData.Count   > 0) max = Math.Max(max, _decData[^1].TimestampMs);
            if (_decRejData.Count > 0) max = Math.Max(max, _decRejData[^1].TimestampMs);
            return max;
        }

        // -- Chart update -------------------------------------------------------

        private void RequestChartUpdate() => _pendingChartUpdate = true;

        private void FlushChartUpdate()
        {
            if (!_pendingChartUpdate || !CanAcceptWork()) return;
            _ = InvokeAsync(FlushChartUpdateCoreAsync);
        }

        private async Task FlushChartUpdateCoreAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _chartUpdateInFlight, 1) == 1) return;
            try
            {
                while (_pendingChartUpdate && CanPushChartUpdate())
                {
                    _pendingChartUpdate = false;
                    try
                    {
                        await _chart!.UpdateSeriesAsync(animate: false);
                        if (IsRealtimeMode) await ApplyRollingWindowViewportAsync();
                    }
                    catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                await DispatchExceptionAsync(ex);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _chartUpdateInFlight, 0);
                if (_pendingChartUpdate && CanPushChartUpdate()) _ = InvokeAsync(FlushChartUpdateCoreAsync);
            }
        }

        /// <summary>Scrolls the X viewport to keep the latest rolling window visible.</summary>
        private async Task ApplyRollingWindowViewportAsync()
        {
            if (_chart is null || !IsRealtimeMode || !CanPushChartUpdate()) return;
            var latestMs = GetLatestTimestampMs();
            if (latestMs == 0) return;
            var windowStartMs = latestMs - PulseRollingWindowMs;
            try
            {
                await _chart.ZoomXAsync((decimal)windowStartMs, (decimal)latestMs);
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException) { }
        }

        /// <summary>Fits the X viewport to the full extent of the paused Historical snapshot.</summary>
        private async Task ApplyHistoricalViewportAsync()
        {
            if (_chart is null || !IsHistoricalMode || !CanPushChartUpdate()) return;

            long minMs = long.MaxValue, maxMs = long.MinValue;
            foreach (var list in (List<PulsePointDto>[])[_pausedRa, _pausedRaRej, _pausedDec, _pausedDecRej])
            {
                if (list.Count == 0) continue;
                minMs = Math.Min(minMs, list[0].TimestampMs);
                maxMs = Math.Max(maxMs, list[^1].TimestampMs);
            }
            if (maxMs == long.MinValue) return; // all snapshots empty
            if (maxMs <= minMs) maxMs = minMs + 1;

            try
            {
                await _chart.ZoomXAsync((decimal)minMs, (decimal)maxMs);
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException) { }
        }

        // -- Toolbar handlers ---------------------------------------------------

        /// <summary>Changes the rolling window duration. Disabled in Historical mode.</summary>
        private async Task OnWindowChangedAsync(int seconds)
        {
            if (IsHistoricalMode) return;
            _settings.PulseWindowSeconds = seconds;
            await SettingsService.SaveChartSettingsAsync(_settings);
            SetAllSubListsToRollingWindow();
            RebuildChartForCurrentMode();
            RequestChartUpdate();
        }

        /// <summary>Changes the Y-axis scale (Milliseconds / ArcSeconds / Steps).</summary>
        private async Task OnScaleChangedAsync(string scale)
        {
            if (scale == _settings.PulseScale) return;
            _settings.PulseScale = scale;
            await SettingsService.SaveChartSettingsAsync(_settings);
            RebuildChartForCurrentMode();
        }

        /// <summary>Changes the accepted series display type (Bars / Points / Line).</summary>
        private async Task OnSeriesTypeChangedAsync(string seriesType)
        {
            if (seriesType == _settings.PulseSeriesType) return;
            _settings.PulseSeriesType = seriesType;
            await SettingsService.SaveChartSettingsAsync(_settings);
            RebuildChartForCurrentMode();
        }

        /// <summary>Toggles visibility of a named series without recreating the chart.</summary>
        private async Task OnSeriesToggleAsync(string propertyName, bool value)
        {
            switch (propertyName)
            {
                case nameof(ChartSettings.ShowRaPulse):    _settings.ShowRaPulse    = value; break;
                case nameof(ChartSettings.ShowRaRejected): _settings.ShowRaRejected = value; break;
                case nameof(ChartSettings.ShowDecPulse):   _settings.ShowDecPulse   = value; break;
                case nameof(ChartSettings.ShowDecRejected):_settings.ShowDecRejected = value; break;
            }
            await SettingsService.SaveChartSettingsAsync(_settings);
            StateHasChanged();
        }

        private Task TogglePauseResumeAsync() => IsRealtimeMode ? PauseDisplayAsync() : ResumeDisplayAsync();

        private async Task PauseDisplayAsync()
        {
            if (IsHistoricalMode) return;

            _pausedRa    = _raSubList?.ToList()    ?? [];
            _pausedRaRej = _raRejSubList?.ToList() ?? [];
            _pausedDec   = _decSubList?.ToList()   ?? [];
            _pausedDecRej = _decRejSubList?.ToList() ?? [];

            _displayMode        = "Historical";
            _pendingChartUpdate = false;

            RebuildChartForCurrentMode();

            if (_chart is not null)
            {
                try
                {
                    await _chart.UpdateSeriesAsync(animate: false);
                    await ApplyHistoricalViewportAsync();
                }
                catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException) { }
            }
            StateHasChanged();
        }

        private Task ResumeDisplayAsync()
        {
            if (IsRealtimeMode) return Task.CompletedTask;

            _displayMode = "Realtime";
            _pausedRa.Clear(); _pausedRaRej.Clear();
            _pausedDec.Clear(); _pausedDecRej.Clear();

            SetAllSubListsToRollingWindow();
            RebuildChartForCurrentMode();
            RequestChartUpdate();
            StateHasChanged();
            return Task.CompletedTask;
        }

        /// <summary>Starts or stops disk logging of pulse guide points.</summary>
        private async Task ToggleLoggingAsync(bool value)
        {
            if (_loggingBusy || _disposed) return;
            _loggingBusy   = true;
            _loggingActive = value;
            try
            {
                if (_loggingActive) await Logger.StartPulseLoggingAsync();
                else                await Logger.StopPulseLoggingAsync();
            }
            finally { _loggingBusy = false; }
        }

        private async Task ClearChartAsync()
        {
            _raData.Clear(); _raRejData.Clear();
            _decData.Clear(); _decRejData.Clear();

            // Lists are now empty — SetStartIndex(0) is valid on empty SubLists
            _raSubList!.SetStartIndex(0);
            _raRejSubList!.SetStartIndex(0);
            _decSubList!.SetStartIndex(0);
            _decRejSubList!.SetStartIndex(0);

            if (_chart is not null)
                try { await _chart.UpdateSeriesAsync(animate: false); }
                catch (TaskCanceledException) { }
        }

        private async Task ExportPngAsync()
        {
            if (_chart is null) return;
            var imgUri = await _chart.GetDataUriAsync(new DataUriOptions());
            await JS.InvokeVoidAsync("chartWindowInterop.downloadDataUri", imgUri, "pulse-chart.png");
        }

        private async Task ExportCsvAsync()
            => await JS.InvokeVoidAsync("chartWindowInterop.exportChartCsv", "pulse-chart");

        // -- Chart options builder ----------------------------------------------

        /// <summary>
        /// Rebuilds ApexChart options from current settings.
        /// Must be called before bumping _chartKey so the new options are applied on recreation.
        /// </summary>
        private void BuildChartOptions()
        {
            var yTitle = _settings.PulseScale switch
            {
                "ArcSeconds" => "Arc-seconds",
                "Steps"      => "Steps",
                _            => "ms"
            };

            var yFormatter = _settings.PulseScale switch
            {
                "ArcSeconds" => "function(val) { return Number(val).toFixed(1); }",
                _            => "function(val) { return Math.round(Number(val)).toString(); }"
            };

            // Stroke widths per series — order matches razor: RA(0), RA-rej(1), Dec(2), Dec-rej(3).
            // Rejected series (1, 3) are always Scatter → width 0. Accepted: 1 if Line, 0 otherwise.
            // Marker sizes: all 4 if Points mode; only rejected (1, 3) otherwise.
            var isHistorical = IsHistoricalMode;

            _chartOptions = new ApexChartOptions<PulsePointDto>
            {
                Chart = new Chart
                {
                    Id = _chartId,
                    Toolbar = new Toolbar
                    {
                        Show  = isHistorical,
                        Tools = new Tools
                        {
                            Download = isHistorical,
                            Zoom     = isHistorical,
                            Pan      = isHistorical,
                            Reset    = isHistorical
                        }
                    },
                    Zoom = new Zoom
                    {
                        Enabled        = isHistorical,
                        Type           = AxisType.X,
                        AutoScaleYaxis = true
                    },
                    Animations           = new Animations { Enabled = false },
                    ParentHeightOffset   = 0,
                    RedrawOnParentResize = true,
                    RedrawOnWindowResize = true,
                    ForeColor            = MudDefaultAxisLabelColor
                },
                PlotOptions = new PlotOptions
                {
                    // columnWidth is only meaningful in Bars mode but is harmless otherwise
                    Bar = new PlotOptionsBar { ColumnWidth = "1%" }
                },
                Stroke = new Stroke
                {
                    Curve = Curve.Straight,
                    Width = _settings.PulseSeriesType == "Line"
                        ? [1, 0, 1, 0]
                        : [0, 0, 0, 0]
                },
                Markers = new Markers
                {
                    Size = _settings.PulseSeriesType == "Points"
                        ? [4, 4, 4, 4]
                        : [0, 4, 0, 4]
                },
                Xaxis = new XAxis
                {
                    Type = XAxisType.Datetime,
                    Labels = new XAxisLabels
                    {
                        Show                  = true,
                        HideOverlappingLabels = true,
                        Format                = "HH:mm:ss",
                        DatetimeUTC           = false
                    },
                    AxisTicks  = new AxisTicks  { Show = true },
                    AxisBorder = new AxisBorder { Show = true }
                },
                Yaxis =
                [
                    new YAxis
                    {
                        Title  = new AxisTitle   { Text = yTitle },
                        Labels = new YAxisLabels { Formatter = yFormatter },
                        Min    = 0
                    }
                ],
                Legend  = new Legend  { Show = true },
                Grid    = new Grid    { BorderColor = "rgba(255,255,255,0.12)" },
                Tooltip = new Tooltip { Enabled = false }
            };
        }

        /// <summary>Rebuilds options and bumps _chartKey to force ApexChart recreation.</summary>
        private void RebuildChartForCurrentMode()
        {
            BuildChartOptions();
            _chartKey = $"pulse-{_displayMode}-{_settings.PulseScale}-{_settings.PulseSeriesType}-{_settings.PulseWindowSeconds}s";
        }

        // -- Value helpers ------------------------------------------------------

        /// <summary>
        /// Returns the Y-axis value for a pulse point in the user-selected scale.
        /// ArcSeconds = (duration_ms / 1000) × |rate_deg_s| × 3600.
        /// </summary>
        private double GetValue(PulsePointDto p) => _settings.PulseScale switch
        {
            "ArcSeconds" => p.Duration / 1000.0 * Math.Abs(p.Rate) * 3600.0,
            "Steps"      => p.Duration,
            _            => p.Duration  // Milliseconds
        };

        // -- Dispose ------------------------------------------------------------

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            await _disposeCts.CancelAsync();
            _disposeCts.Dispose();

            if (_refreshTimer is not null) await _refreshTimer.DisposeAsync();

            if (_loggingActive)
                try { await Logger.StopPulseLoggingAsync(); } catch { }

            if (_hub is not null)
            {
                try { await _hub.InvokeAsync("LeavePulseGroupAsync", DeviceNumber); } catch { }
                await _hub.DisposeAsync();
            }
        }
    }
}
