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
using GreenSwamp.Alpaca.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Text.RegularExpressions;

namespace GreenSwamp.Alpaca.Server.Pages.Charts
{
    /// <summary>
    /// Partial class for PulseChart: SignalR lifecycle, data management,
    /// rolling-window SubList maintenance, and the 1-second flush timer.
    /// Toolbar handlers and chart-options builder live in PulseChart.razor.cs.
    /// </summary>
    public partial class PulseChart
    {
        private string ChartId { get; set; } = null;

        // -- Lifecycle ----------------------------------------------------------

        protected override Task OnInitializedAsync()
        {
            _settings    = SettingsService.GetChartSettings();
            _displayMode = "Realtime";
            ChartId = string.IsNullOrEmpty(Label) ? "Pulse" : Regex.Replace(Label, @"[^\w]+", string.Empty);
            ChartId += $"_{DeviceNumber.ToString()}_{DateTime.Now.ToString("yyyy-MM-dd")}";

            BuildChartOptions();

            _raSubList     = new SubList<PulsePointDto>(_raData,     0);
            _raRejSubList  = new SubList<PulsePointDto>(_raRejData,  0);
            _decSubList    = new SubList<PulsePointDto>(_decData,    0);
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
                    // Re-request history so the chart is fully populated after a reconnect
                    // (the server may have discarded in-flight buffered points during the gap).
                    await _hub!.InvokeAsync("RequestHistoricalDataAsync", "pulse", DeviceNumber);
                    RequestChartUpdate();
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested || _disposed) { }
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
            _hubState       = _hub.State;
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

            // Cap — remove oldest if at limit (SubList start index recomputed below)
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
            SetSubListWindow(_raData,     _raSubList!,     latestMs);
            SetSubListWindow(_raRejData,  _raRejSubList!,  latestMs);
            SetSubListWindow(_decData,    _decSubList!,    latestMs);
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
            if (_raData.Count     > 0) max = Math.Max(max, _raData[^1].TimestampMs);
            if (_raRejData.Count  > 0) max = Math.Max(max, _raRejData[^1].TimestampMs);
            if (_decData.Count    > 0) max = Math.Max(max, _decData[^1].TimestampMs);
            if (_decRejData.Count > 0) max = Math.Max(max, _decRejData[^1].TimestampMs);
            return max;
        }

        // -- Chart update (1-second flush timer) --------------------------------

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
            if (maxMs == long.MinValue) return;
            if (maxMs <= minMs) maxMs = minMs + 1;

            try
            {
                await _chart.ZoomXAsync((decimal)minMs, (decimal)maxMs);
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or JSDisconnectedException) { }
        }

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
