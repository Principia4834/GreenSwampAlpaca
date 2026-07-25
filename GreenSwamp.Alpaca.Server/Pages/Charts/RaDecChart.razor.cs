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

    // RaDecChart: uses the same C# list-buffer + System.Threading.Timer pattern as PulseChart.
    // The wrapper's UpdateSeriesAsync / AppendDataAsync are the only chart-update paths.
    // No raw IJSRuntime calls on the data hot path — those violate the prerender contract.
    public partial class RaDecChart
    {
        [Parameter] public int DeviceNumber { get; set; }

        /// <summary>Represents a data point for the RA/Dec chart.</summary>
        private sealed class RaDecChartData
        {
            /// <summary>Gets the timestamp of the data point in milliseconds.</summary>
            public long TimestampMs { get; init; }

            /// <summary>Gets the raw RA/axis-1 value in steps.</summary>
            public double RawRaSteps { get; init; }

            /// <summary>Gets the raw Dec/axis-2 value in steps.</summary>
            public double RawDecSteps { get; init; }

            /// <summary>Gets the display RA value in the selected scale.</summary>
            public double Ra { get; set; }

            /// <summary>Gets the display Dec value in the selected scale.</summary>
            public double Dec { get; set; }
        }

        // -- State --------------------------------------------------------------
        private ChartSettings _settings = new();
        private HubConnection? _hub;
        private bool _loggingActive;
        private bool _loggingBusy;
        private bool _ready;
        private HubConnectionState _hubState = HubConnectionState.Disconnected;
        private bool _disposed;


        // _chartKey is bumped to force <ApexChart> recreation when options change
        // (mode / scale / window). Matches the @key pattern used by PulseChart.
        private string _chartKey = "radec-init";

        // Coalesces incoming points into one UpdateSeriesAsync call per second,
        // matching the PulseChart FlushChartUpdate pattern.
        private volatile bool _pendingChartUpdate;
        private System.Threading.Timer? _refreshTimer;

        private readonly CancellationTokenSource _disposeCts = new();
        private int _chartUpdateInFlight;
        private volatile bool _forceNonAnimatedRefresh;

        private bool CanAcceptWork()
            => !_disposed && !_disposeCts.IsCancellationRequested;

        private bool CanPushChartUpdate()
            => CanAcceptWork()
               && _ready
               && _hubState == HubConnectionState.Connected
               && _chart is not null;

        private void RequestChartUpdate(bool animate)
        {
            if (!animate)
            {
                _forceNonAnimatedRefresh = true;
            }

            _pendingChartUpdate = true;
        }

        private long RaDecRollingWindowMs => Math.Max(1, _settings.RealtimeWindowSeconds) * 1000L;
        private  List<RaDecChartData> _raDecChartData = [];
        private SubList<RaDecChartData> _raDecChartDataSubList = null; // Will be initialized in OnInitializedAsync
        private ApexChart<RaDecChartData>? _chart;
        private ApexChartOptions<RaDecChartData> _chartOptions = new();
        private const string MudDefaultAxisLabelColor = "var(--mud-palette-text-primary)";

        #region SignalR Handlers
        // -- SignalR handlers ---------------------------------------------------

        /// <summary>       
        /// SignalR handler for incoming axis points. Adds the points to the chart data buffers.
        /// </summary>
        /// <param name="points">The array of incoming axis points.</param>
        private void OnAxisPoints(ChartPointDto[] points)
        {
            if (points.Length < 2 || !CanAcceptWork()) return;

            _ = InvokeAsync(() =>
            {
                if (!CanAcceptWork()) return Task.CompletedTask;

                AddToRaDecChartData(points[0], points[1]);
                RequestChartUpdate(animate: true);

                return Task.CompletedTask;
            });
        }

        //private void OnAxisPoints(ChartPointDto[] points)
        //{
        //    if (_disposed || points.Length < 2) return;

        //    InvokeAsync(async () =>
        //    {
        //        if (_disposed) return;

        //        AddToRaDecChartData(points[0], points[1]);

        //        if (_loggingActive)
        //        {
        //            await Logger.LogRaDecPointAsync(1, points[0]);
        //            await Logger.LogRaDecPointAsync(2, points[1]);
        //        }

        //        await UpdateRaDecChartAsync(animate: true);
        //    });
        //}

        /// <summary>
        /// Adds the given RA and Dec points to the RA/Dec chart data buffers, maintaining the rolling window and history limits.
        /// </summary>
        /// <param name="raPoint">The RA data point.</param>
        /// <param name="decPoint">The Dec data point.</param>
        private void AddToRaDecChartData(ChartPointDto raPoint, ChartPointDto decPoint)
        {
            var timestampMs = raPoint.TimestampMs;
            var rollingCutoffMs = timestampMs - RaDecRollingWindowMs;
            var historyCutoffMs = timestampMs - 1 * 60 * 60 * 1000L; // 1 hour

            // Store the old start index before trimming the history
            var oldStartIndex = _raDecChartData.Count == 0
                ? 0
                : _raDecChartDataSubList.StartIndex;

            // Trim the history to keep only the last 1 hour of data
            var historyTrimCount = _raDecChartData
                .TakeWhile(p => p.TimestampMs < historyCutoffMs)
                .Count();
            if (historyTrimCount > 0) _raDecChartData.RemoveRange(0, historyTrimCount);

            // Adjust the start index of the sublist after trimming the history
            if (_raDecChartData.Count == 0) 
            {
                _raDecChartDataSubList.SetStartIndex(0);
            }
            else
            {
                var adjustedStartIndex = Math.Max(0, oldStartIndex - historyTrimCount);
                adjustedStartIndex = Math.Min(adjustedStartIndex, _raDecChartData.Count - 1);
                _raDecChartDataSubList.SetStartIndex(adjustedStartIndex);
            }

            // Add the new point to the chart data
            _raDecChartData.Add(new RaDecChartData
            {
                TimestampMs = timestampMs,
                RawRaSteps = raPoint.Value,
                RawDecSteps = decPoint.Value,
                Ra = ScaleValue(raPoint.Value, axisIndex: 0),
                Dec = ScaleValue(decPoint.Value, axisIndex: 1)
            });

            // Find the index of the first point that is within the rolling window and update the sublist's start index accordingly
            var rollingStartIndex = _raDecChartData.FindIndex(p => p.TimestampMs >= rollingCutoffMs);
            if (rollingStartIndex < 0) rollingStartIndex = _raDecChartData.Count - 1;

            _raDecChartDataSubList.SetStartIndex(rollingStartIndex);
        }
        
        #endregion

        #region Chart Update
        // -- Lifecycle ----------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            _raDecChartDataSubList = new SubList<RaDecChartData>(_raDecChartData, 0);
            
            _settings = SettingsService.GetChartSettings();
            BuildChartOptions();

            var hubUrl = Nav.ToAbsoluteUri("/charthub");
            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ChartPointDto[]>("ReceiveAxisPoint", OnAxisPoints);
            // _hub.On<HistoricalDataDto>("ReceiveRaDecHistory", OnHistory);

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
                    await _hub!.InvokeAsync("JoinRaDecGroupAsync", DeviceNumber);
                    // Force a clean non-animated repaint after reconnect.
                    _forceNonAnimatedRefresh = true;
                    _pendingChartUpdate = true;
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _disposed)
                {
                }
                catch (Exception ex)
                {
                    await DispatchExceptionAsync(ex);
                }
                await InvokeAsync(StateHasChanged);
            };

            _hub.Closed += _ =>
            {
                _hubState = HubConnectionState.Disconnected;
                return InvokeAsync(StateHasChanged);
            };

            await _hub.StartAsync();
            _hubState = _hub.State;
            await _hub.InvokeAsync("JoinRaDecGroupAsync", DeviceNumber);

            if (_settings.AutoStartLogging)
            {
                await Logger.StartRaDecLoggingAsync();
                _loggingActive = true;
            }

            _ready = true;
        }

        // Timer is started here — OnAfterRender is the first point guaranteed to be
        // post-prerender, matching the official RealTime.razor example pattern exactly.
        protected override void OnAfterRender(bool firstRender)
        {
            if (!firstRender || _refreshTimer != null) return;
            _refreshTimer = new System.Threading.Timer(
                _ => FlushChartUpdate(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        // -- Realtime flush (1-second timer) ------------------------------------

        /// <summary>
        /// Flushes any pending chart updates to the RA/Dec chart if there are updates pending and the 
        /// component can accept work.
        /// </summary>
        private void FlushChartUpdate()
        {
            if (!_pendingChartUpdate || !CanAcceptWork()) return;

            _ = InvokeAsync(FlushChartUpdateCoreAsync);
        }

        /// <summary>
        /// Flushes any pending chart updates to the RA/Dec chart asynchronously. If there are updates pending and 
        /// the component can accept work, it will update the chart series. This method ensures that only one chart 
        /// update is in flight at a time.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task FlushChartUpdateCoreAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _chartUpdateInFlight, 1) == 1) return;

            try
            {
                while (_pendingChartUpdate && CanPushChartUpdate())
                {
                    _pendingChartUpdate = false;
                    var animate = !_forceNonAnimatedRefresh
                                  && _settings.DisplayMode == "Realtime";
                    _forceNonAnimatedRefresh = false;

                    try
                    {
                        await _chart!.UpdateSeriesAsync(animate);
                    }
                    catch (JSDisconnectedException)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _disposed)
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

        /// <summary>
        /// Updates the RA/Dec chart asynchronously, optionally animating the update. If the component cannot accept work, 
        /// the method returns a completed task.
        /// </summary>
        /// <param name="animate">Indicates whether the update should be animated.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private Task UpdateRaDecChartAsync(bool animate)
        {
            if (!CanAcceptWork())
            {
                return Task.CompletedTask;
            }

            RequestChartUpdate(animate);

            if (_chart is null)
            {
                StateHasChanged();
            }

            return Task.CompletedTask;
        }
        /// <summary>
        /// Rescales the RA/Dec chart data points based on the current scale settings.
        /// </summary>
        private void RescaleRaDecChartData()
        {
            foreach (var point in _raDecChartData)
            {
                point.Ra = ScaleValue(point.RawRaSteps, axisIndex: 0);
                point.Dec = ScaleValue(point.RawDecSteps, axisIndex: 1);
            }
        }

        /// <summary>
        /// Trims the RA/Dec chart data to fit within the rolling window.
        /// </summary>
        private void SetRaDecChartDataToRollingWindow()
        {
            if (_raDecChartData.Count == 0) return;

            var latestTimestampMs = _raDecChartData[^1].TimestampMs;
            var cutoffMs = latestTimestampMs - RaDecRollingWindowMs;

            var index = _raDecChartData.FindIndex(p => p.TimestampMs >= cutoffMs);
            if (index < 0) index = _raDecChartData.Count - 1;

            _raDecChartDataSubList.SetStartIndex(index);
        }
        #endregion

        #region Toolbar Handlers
        // -- Toolbar handlers ---------------------------------------------------

        /// <summary>Switches between Realtime and Historical display modes.</summary>
        private async Task OnModeChangedAsync(string? newMode)
        {
            if (newMode is null || newMode == _settings.DisplayMode) return;
            _settings.DisplayMode = newMode;
            await SettingsService.SaveChartSettingsAsync(_settings);
            BuildChartOptions();
            _chartKey = $"radec-{_settings.DisplayMode}-{_settings.RealtimeWindowSeconds}";
            StateHasChanged();
        }

        /// <summary>
        /// Changes the rolling window size (10 / 30 / 120 / 300seconds) for Realtime mode.
        /// </summary>
        /// <param name="seconds">The new rolling window size in seconds.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task OnWindowChangedAsync(int seconds)
        {
            _settings.RealtimeWindowSeconds = seconds;
            await SettingsService.SaveChartSettingsAsync(_settings);

            SetRaDecChartDataToRollingWindow();

            BuildChartOptions();
            _chartKey = $"radec-{_settings.DisplayMode}-{seconds}s";

            await UpdateRaDecChartAsync(animate: false);
        }

        /// <summary>Changes the per-series buffer cap (5,000 / 10,000 / 20,000 points).</summary>
        private async Task OnMaxPointsChangedAsync(int newMax)
        {
            _settings.RaDecMaxPoints = newMax;
            await SettingsService.SaveChartSettingsAsync(_settings);
            // Trim existing buffers to the new cap immediately.
        }

        /// <summary>Changes the Y-axis scale (Steps / Degrees / Arc-seconds).</summary>
        private async Task OnScaleChangedAsync(string scale)
        {
            if (scale == _settings.RaDecScale) return;

            _settings.RaDecScale = scale;
            await SettingsService.SaveChartSettingsAsync(_settings);

            RescaleRaDecChartData();

            BuildChartOptions();
            _chartKey = $"radec-{_settings.RaDecScale}";

            await UpdateRaDecChartAsync(animate: false);
        }

        /// <summary>
        /// Toggles the visibility of a series (Axis 1 or Axis 2) on or off.
        /// </summary>
        /// <param name="value">The new visibility state.</param>
        /// <param name="axis">The axis index (1 or 2).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task OnSeriesToggleAsync(bool value, int axis)
        {
            if (axis == 1)
                _settings.ShowAxis1 = value;
            else
                _settings.ShowAxis2 = value;

            await SettingsService.SaveChartSettingsAsync(_settings);

            _chartKey = $"radec-{_settings.RaDecScale}-{_settings.RealtimeWindowSeconds}s-a1{_settings.ShowAxis1}-a2{_settings.ShowAxis2}";

            StateHasChanged();
        }

        /// <summary>
        /// Toggles Ra/Dec logging on or off. If logging is already in progress, this method does nothing.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ToggleLoggingAsync(bool v)
        {
            if (_loggingBusy || _disposed) return;

            _loggingBusy = true;
            _loggingActive = v;

            try
            {
                if (_loggingActive)
                {
                    await Logger.StopRaDecLoggingAsync();
                }
                else
                {
                    await Logger.StartRaDecLoggingAsync();
                }
            }
            finally
            {
                _loggingBusy = false;
            }
        }

        /// <summary>Manual refresh — re-requests history from the server.</summary>
        private async Task RefreshHistoricalAsync()
        {
            try { await _hub!.InvokeAsync("RequestHistoricalDataAsync", "radec", DeviceNumber); }
            catch (TaskCanceledException) { }
        }

        private async Task ExportPngAsync()
        {
        }

        private async Task ExportCsvAsync()
        {
            var filename = $"radec-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            try { await JS.InvokeVoidAsync("chartWindowInterop.exportChartCsv", filename); }
            catch (TaskCanceledException) { }
        }
        #endregion

        #region Chart options builder
        // -- Chart options builder ----------------------------------------------

        /// <summary>
        /// Builds the chart options based on the current settings.
        /// </summary>
        private void BuildChartOptions()
        {
            var yTitle = _settings.RaDecScale switch
            {
                "Degrees" => "Degrees",
                "ArcSeconds" => "Arc-seconds",
                _ => "Steps"
            };

            // The formatter function for the y-axis labels, based on the selected scale.
            var yFormatter = _settings.RaDecScale switch
            {
                "Degrees" => "function(val) { return Number(val).toFixed(2); }",
                "ArcSeconds" => "function(val) { return Number(val).toFixed(1); }",
                _ => "function(val) { return Math.round(Number(val)).toString(); }"
            };

            _chartOptions = new ApexChartOptions<RaDecChartData>
            {
                Chart = new Chart
                {
                    Toolbar = new Toolbar { Show = false },
                    ParentHeightOffset = 0,
                    RedrawOnParentResize = true,
                    RedrawOnWindowResize = true,
                    ForeColor = MudDefaultAxisLabelColor,
                },

                Title = new Title
                {
                    Text = "RA/Dec Chart",
                    Align = Align.Center
                },

                Tooltip = new Tooltip { Enabled = false },

                Xaxis = new XAxis
                {
                    Type = XAxisType.Datetime,
                    Labels = new XAxisLabels
                    {
                        Show = true,
                        HideOverlappingLabels = true,
                        Format = "HH:mm:ss",
                        DatetimeUTC = false
                    },
                    AxisTicks = new AxisTicks { Show = true },
                    AxisBorder = new AxisBorder { Show = true }
                },
                Yaxis =
                [
                    new YAxis
                {
                    Title = new AxisTitle { Text = yTitle },
                    Labels = new YAxisLabels { Formatter = yFormatter }
                }
                ]
            };
        }

        private void BuildHistoricalOptions(string yTitle, string yFormatter)
        {
        }
        #endregion

        #region Helpers
        // -- Value conversion & buffer helpers ----------------------------------

        /// <summary>Converts a raw point to a new ChartPointDto with the scaled Y value.</summary>
        private ChartPointDto ConvertPoint(ChartPointDto p, int axisIndex)
            => new(p.TimestampMs, ScaleValue(p.Value, axisIndex));

        /// <summary>Scales a raw step value to the user-selected unit.</summary>
        private double ScaleValue(double value, int axisIndex)
        {
            if (_settings.RaDecScale == "Steps") return value;
            var stepsPerRev = StateService.GetCurrentState(DeviceNumber).StepsPerRevolution;
            var spr = stepsPerRev is { Length: > 0 }
                ? stepsPerRev[Math.Min(axisIndex, stepsPerRev.Length - 1)]
                : 0L;
            if (spr <= 0) return value;
            return _settings.RaDecScale switch
            {
                "Degrees"    => value * 360.0 / spr,
                "ArcSeconds" => value * 360.0 * 3600.0 / spr,
                _            => value
            };
        }

        // GetValue kept for the ApexPointSeries YValue lambda in the markup.
        private double GetValue(ChartPointDto p, int axisIndex) => ScaleValue(p.Value, axisIndex);

        private void AddToBuffer(List<ChartPointDto> buffer, ChartPointDto point, int axisIndex)
        {
            var max = _settings.RaDecMaxPoints > 0 ? _settings.RaDecMaxPoints : 5000;
            if (buffer.Count >= max) buffer.RemoveAt(0);
            buffer.Add(ConvertPoint(point, axisIndex));
        }

        private void TrimBuffer(List<ChartPointDto> buffer)
        {
            var max = _settings.RaDecMaxPoints > 0 ? _settings.RaDecMaxPoints : 5000;
            while (buffer.Count > max) buffer.RemoveAt(0);
        }
        #endregion

        // -- Dispose ------------------------------------------------------------

        /// <summary>
        /// Disposes of the resources used by the RaDecChart component, including stopping logging, leaving the SignalR group, 
        /// and disposing of the SignalR hub connection and timer.
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            _disposed = true;

            if (!_disposeCts.IsCancellationRequested) _disposeCts.Cancel();

            if (_refreshTimer is not null) await _refreshTimer.DisposeAsync();

            if (_loggingActive)
            {
                try
                {
                    await Logger.StopRaDecLoggingAsync();
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
                {
                }
                catch
                {
                }
            }

            if (_hub is not null)
            {
                try
                {
                    if (_hub.State != HubConnectionState.Disconnected) await _hub.InvokeAsync("LeaveRaDecGroupAsync", DeviceNumber);
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
                {
                }
                catch
                {
                }

                try
                {
                    await _hub.DisposeAsync();
                }
                catch
                {
                }
            }

            _disposeCts.Dispose();
        }
    }
}
