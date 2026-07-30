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
    public partial class RaDecChart
    {
        private HubConnectionState _hubState = HubConnectionState.Disconnected;
        private bool _hubInitialised; // true once StartAsync completes; suppresses spurious "Disconnected" on first render        private volatile bool _pendingChartUpdate;
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
        private readonly List<RaDecChartData> _pendingAppendPoints = [];
        private volatile bool _forceFullSeriesRefresh;
        private long RaDecRollingWindowMs => Math.Max(1, _settings.RealtimeWindowSeconds) * 1000L;
        private List<RaDecChartData> _raDecChartData = [];
        private SubList<RaDecChartData> _raDecChartDataSubList = null; // Will be initialized in OnInitializedAsync
        private readonly string _viewSessionId = Guid.NewGuid().ToString("N");
        private PeriodicTimer? _viewHeartbeat;

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
            var chartPoint = new RaDecChartData
            {
                TimestampMs = timestampMs,
                RawRaSteps = raPoint.Value,
                RawDecSteps = decPoint.Value,
                Ra = ScaleValue(raPoint.Value, axisIndex: 0),
                Dec = ScaleValue(decPoint.Value, axisIndex: 1)
            };

            _raDecChartData.Add(chartPoint);
            _pendingAppendPoints.Add(chartPoint);

            // Find the index of the first point that is within the rolling window and update the sublist's start index accordingly
            var rollingStartIndex = _raDecChartData.FindIndex(p => p.TimestampMs >= rollingCutoffMs);
            if (rollingStartIndex < 0) rollingStartIndex = _raDecChartData.Count - 1;

            _raDecChartDataSubList.SetStartIndex(rollingStartIndex);
        }

        #endregion

        #region Chart Update
        // -- Lifecycle ----------------------------------------------------------
        /// <summary>
        /// Called when the component is initialized. This method sets up the axis labels, builds the 
        /// chart options, initializes the RA/Dec chart data sublist, and retrieves the chart settings 
        /// from the SettingsService.
        /// </summary>
        /// <returns></returns>
        protected override async Task OnInitializedAsync()
        {
            _settings = SettingsService.GetChartSettings();   // ← load BEFORE BuildChartOptions
            _axisLabels = AxisLabels(AlignmentMode);
            BuildChartOptions();
            _raDecChartDataSubList = new SubList<RaDecChartData>(_raDecChartData, 0);
        }

        /// <summary>
        /// Called only after the interactive SignalR circuit is established — never during
        /// the static prerender pass. All SignalR hub construction is done here to prevent
        /// the double-initialization that prerendering causes in OnInitializedAsync.
        /// The refresh timer is also started here (replaces the previous sync OnAfterRender).
        /// </summary>
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            // Start the 1-second flush timer (previously in sync OnAfterRender).
            _refreshTimer ??= new System.Threading.Timer(
                _ => FlushChartUpdate(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            if (_hub is not null) return; // already initialised (safety guard)

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
                    _forceNonAnimatedRefresh = true;
                    RequestFullSeriesRefresh(animate: false);
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
            _hubInitialised = true;
            await _hub.InvokeAsync("JoinRaDecGroupAsync", DeviceNumber);

            if (_settings.AutoStartLogging)
            {
                await Logger.StartRaDecLoggingAsync();
                _loggingActive = true;
            }

            _ready = true;

            ActiveViews.Touch(_viewSessionId, DeviceNumber);
            _ = RunViewHeartbeatAsync();
        }

        /// <summary>
        /// Periodically touches ActiveDeviceViewRegistry so that TelescopeStateService
        /// continues polling and pushing axis-point data to the chart hub even when
        /// no MountControl tab is open.
        /// </summary>
        private async Task RunViewHeartbeatAsync()
        {
            _viewHeartbeat = new PeriodicTimer(TimeSpan.FromSeconds(3));
            try
            {
                while (await _viewHeartbeat.WaitForNextTickAsync(_disposeCts.Token))
                {
                    if (!CanAcceptWork()) break;
                    ActiveViews.Touch(_viewSessionId, DeviceNumber);
                }
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
            }
        }
        
        /// <summary>
                 /// Called after the component has been rendered. If this is the first render and the 
                 /// refresh timer has not been initialized, it sets up a timer to flush chart updates 
                 /// every second. OnAfterRender is the first point guaranteed to be post-prerender, 
                 /// matching the official RealTime.razor example pattern exactly.
                 /// </summary>
                 /// <param name="firstRender"></param>
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
                        if (_forceFullSeriesRefresh)
                        {
                            _forceFullSeriesRefresh = false;
                            _pendingAppendPoints.Clear();

                            await _chart!.UpdateSeriesAsync(animate);

                            if (_raDecChartData.Count > 0) await ApplyRollingWindowViewportAsync(_raDecChartData[^1].TimestampMs);

                            continue;
                        }
                        if (_pendingAppendPoints.Count == 0) continue;
                        var batch = _pendingAppendPoints.ToArray();
                        _pendingAppendPoints.Clear();
                        await _chart!.AppendDataBySeriesNameAsync(new Dictionary<string, IEnumerable<RaDecChartData>>
                        {
                            [_axisLabels[0]] = batch,
                            [_axisLabels[1]] = batch
                        });
                        await ApplyRollingWindowViewportAsync(batch[^1].TimestampMs);
                    }
                    catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is JSDisconnectedException)
                    {
                        // Circuit/transport interruption during JS interop.
                        // Expected during disconnect/reconnect or shutdown.
                        // Blazor Server circuit dropped; ignore and let reconnect path recover.
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
        /// Applies a rolling window viewport to the RA/Dec chart based on the latest timestamp. If the 
        /// chart is null or the display mode is not "Realtime", the method returns without making any changes.
        /// </summary>
        /// <param name="latestTimestampMs">The latest timestamp in milliseconds.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ApplyRollingWindowViewportAsync(long latestTimestampMs)
        {
            if (_chart is null || _settings.DisplayMode != "Realtime" || !CanPushChartUpdate()) return;

            var windowStartMs = latestTimestampMs - RaDecRollingWindowMs;
            try
            {
                await _chart.ZoomXAsync((decimal)windowStartMs, (decimal)latestTimestampMs);
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is JSDisconnectedException)
            {
                // Circuit/transport interruption during JS interop.
                // Expected during disconnect/reconnect or shutdown.
                // Blazor Server circuit dropped; ignore and let reconnect path recover.
            }
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

        /// <summary>
        /// Requests a chart update, optionally forcing a non-animated refresh. If the animate parameter
        /// is false, the next chart update will be forced to be non-animated. This method sets the 
        /// _pendingChartUpdate flag to true, indicating that a chart update is needed.
        /// </summary>
        /// <param name="animate">Indicates whether the update should be animated.</param>
        private void RequestChartUpdate(bool animate)
        {
            if (!animate) _forceNonAnimatedRefresh = true;
            _pendingChartUpdate = true;
        }

        /// <summary>
        /// Requests a full series refresh, optionally animating the update. This method sets the 
        /// _forceFullSeriesRefresh flag to true and then calls RequestChartUpdate to schedule the update.
        /// </summary>
        /// <param name="animate">Indicates whether the update should be animated.</param>
        private void RequestFullSeriesRefresh(bool animate)
        {
            _forceFullSeriesRefresh = true;
            RequestChartUpdate(animate);
        }
        #endregion

    }

}