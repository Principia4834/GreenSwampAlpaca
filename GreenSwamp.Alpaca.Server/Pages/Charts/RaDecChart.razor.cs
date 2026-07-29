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
        private bool _disposed;


        // _chartKey is bumped to force <ApexChart> recreation when options change
        // (mode / scale / window). Matches the @key pattern used by PulseChart.
        private string _chartKey = "radec-init";

        private ApexChart<RaDecChartData>? _chart;
        private ApexChartOptions<RaDecChartData> _chartOptions = new();
        private const string MudDefaultAxisLabelColor = "var(--mud-palette-text-primary)";

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

            RequestFullSeriesRefresh(animate: false); // inserted
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

            RequestFullSeriesRefresh(animate: false); // inserted
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

            ActiveViews.Remove(_viewSessionId);
            if (_viewHeartbeat is not null) _viewHeartbeat.Dispose();
            
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
