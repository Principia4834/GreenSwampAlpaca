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

        // -- State --------------------------------------------------------------
        private ChartSettings _settings = new();
        private HubConnection? _hub;
        private bool _loggingActive;
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

        // -- Lifecycle ----------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            _settings = SettingsService.GetChartSettings();
            BuildChartOptions();

            var hubUrl = Nav.ToAbsoluteUri("/charthub");
            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ChartPointDto[]>("ReceiveAxisPoint", OnAxisPoints);
            _hub.On<HistoricalDataDto>("ReceiveRaDecHistory", OnHistory);

            _hub.Reconnecting += _ => { _hubState = HubConnectionState.Reconnecting; InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Reconnected  += _ => { _hubState = HubConnectionState.Connected;    InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Closed       += _ => { _hubState = HubConnectionState.Disconnected; InvokeAsync(StateHasChanged); return Task.CompletedTask; };

            await _hub.StartAsync();
            _hubState = _hub.State;
            await _hub.InvokeAsync("JoinRaDecGroupAsync", DeviceNumber);
            await _hub.InvokeAsync("RequestHistoricalDataAsync", "radec", DeviceNumber);

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
        /// Called by the 1-second timer. Calls UpdateSeriesAsync once per tick.
        /// In Realtime mode animate:true is essential — combined with Easing.Linear
        /// and DynamicAnimation.Speed=1000, the wrapper animates from the old series
        /// state to the new one over exactly 1 second, producing smooth left-scroll.
        /// In Historical mode animate:false gives instant redraw after a full load.
        /// </summary>
        private void FlushChartUpdate()
        {
            if (!_pendingChartUpdate || _disposed) return;
            _pendingChartUpdate = false;
            //InvokeAsync(async () =>
            //{
            //    if (_chart is null || _disposed) return;
            //    var animate = _settings.DisplayMode == "Realtime";
            //    try { await _chart.UpdateSeriesAsync(animate); }
            //    catch (TaskCanceledException) { }
            //});
        }

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

        /// <summary>Changes the realtime rolling-window duration (10 / 30 / 120 seconds).</summary>
        private async Task OnWindowChangedAsync(int seconds)
        {
            _settings.RealtimeWindowSeconds = seconds;
            await SettingsService.SaveChartSettingsAsync(_settings);
            BuildChartOptions();
            _chartKey = $"radec-{_settings.DisplayMode}-{seconds}s";
        }

        /// <summary>Changes the per-series buffer cap (5 000 / 10 000 / 20 000 points).</summary>
        private async Task OnMaxPointsChangedAsync(int newMax)
        {
            _settings.RaDecMaxPoints = newMax;
            await SettingsService.SaveChartSettingsAsync(_settings);
            // Trim existing buffers to the new cap immediately.
        }

        /// <summary>Changes the Y-axis scale (Steps / Degrees / Arc-seconds).</summary>
        private async Task OnScaleChangedAsync(string scale)
        {
            _settings.RaDecScale = scale;
            await SettingsService.SaveChartSettingsAsync(_settings);
            // Scale change invalidates buffered values — re-request history to refill in new unit.
            BuildChartOptions();
            _chartKey = $"radec-{_settings.RaDecScale}";
            try { await _hub!.InvokeAsync("RequestHistoricalDataAsync", "radec", DeviceNumber); }
            catch (TaskCanceledException) { }
        }

        private async Task OnSeriesToggleAsync(bool value, int axis)
        {
            if (axis == 1) _settings.ShowAxis1 = value;
            else           _settings.ShowAxis2 = value;
            await SettingsService.SaveChartSettingsAsync(_settings);
        }

        private async Task ToggleLoggingAsync()
        {
            if (_loggingActive) { await Logger.StopRaDecLoggingAsync();  _loggingActive = false; }
            else                { await Logger.StartRaDecLoggingAsync(); _loggingActive = true;  }
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

        // -- Dispose ------------------------------------------------------------

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            if (_refreshTimer is not null) await _refreshTimer.DisposeAsync();
            if (_loggingActive)
            {
                try { await Logger.StopRaDecLoggingAsync(); } catch { }
            }
            if (_hub is not null)
            {
                try { await _hub.InvokeAsync("LeaveRaDecGroupAsync", DeviceNumber); } catch { }
                await _hub.DisposeAsync();
            }
        }
    }
}
