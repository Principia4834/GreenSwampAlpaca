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
    public partial class PulseChart
    {
        [Parameter] public int DeviceNumber { get; set; }

        // -- State --------------------------------------------------------------
        private ChartSettings _settings = new();
        private readonly List<PulsePointDto> _raData = [];
        private readonly List<PulsePointDto> _raRejData = [];
        private readonly List<PulsePointDto> _decData = [];
        private readonly List<PulsePointDto> _decRejData = [];
        private ApexChart<PulsePointDto>? _chart;
        private ApexChartOptions<PulsePointDto> _chartOptions = new();
        private HubConnection? _hub;
        private bool _loggingActive;
        private bool _ready;
        private HubConnectionState _hubState = HubConnectionState.Disconnected;
        private bool _disposed;
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

            _hub.On<PulsePointDto>("ReceivePulsePoint", OnPulsePoint);
            _hub.On<IReadOnlyList<PulsePointDto>, IReadOnlyList<PulsePointDto>>("ReceivePulseHistory", OnHistory);

            _hub.Reconnecting += _ => { _hubState = HubConnectionState.Reconnecting; InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Reconnected += _ => { _hubState = HubConnectionState.Connected; InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Closed += _ => { _hubState = HubConnectionState.Disconnected; InvokeAsync(StateHasChanged); return Task.CompletedTask; };

            await _hub.StartAsync();
            _hubState = _hub.State;
            await _hub.InvokeAsync("JoinPulseGroupAsync", DeviceNumber);
            await _hub.InvokeAsync("RequestHistoricalDataAsync", "pulse", DeviceNumber);

            if (_settings.AutoStartLogging)
            {
                await Logger.StartPulseLoggingAsync();
                _loggingActive = true;
            }

            _ready = true;
            _refreshTimer = new System.Threading.Timer(_ => FlushChartUpdate(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        // -- SignalR handlers ---------------------------------------------------

        private void OnPulsePoint(PulsePointDto point)
        {
            if (_disposed) return;
            InvokeAsync(async () =>
            {
                AddToBuffer(point);
                if (_loggingActive) await Logger.LogPulsePointAsync(point);
                _pendingChartUpdate = true;
            });
        }

        private void OnHistory(IReadOnlyList<PulsePointDto> ra, IReadOnlyList<PulsePointDto> dec)
        {
            if (_disposed) return;
            InvokeAsync(async () =>
            {
                _raData.Clear();
                _raRejData.Clear();
                _decData.Clear();
                _decRejData.Clear();

                var maxPts = _settings.MaxPoints > 0 ? _settings.MaxPoints : 5000;
                foreach (var p in ra.TakeLast(maxPts))
                {
                    if (p.Rejected) _raRejData.Add(p);
                    else _raData.Add(p);
                }
                foreach (var p in dec.TakeLast(maxPts))
                {
                    if (p.Rejected) _decRejData.Add(p);
                    else _decData.Add(p);
                }

                if (_chart is not null)
                {
                    try { await _chart.UpdateSeriesAsync(animate: false); }
                    catch (TaskCanceledException) { }
                }
                StateHasChanged();
            });
        }

        // -- Toolbar handlers ---------------------------------------------------

        private async Task OnScaleChangedAsync(string scale)
        {
            _settings.PulseScale = scale;
            await SettingsService.SaveChartSettingsAsync(_settings);
            BuildChartOptions();
            // @key="@_settings.PulseScale" on <ApexChart> causes Blazor to recreate the
            // component with the new options, including the correct y-axis title.
            StateHasChanged();
        }

        private async Task OnSeriesToggleAsync(string propertyName, bool value)
        {
            switch (propertyName)
            {
                case nameof(ChartSettings.ShowRaPulse): _settings.ShowRaPulse = value; break;
                case nameof(ChartSettings.ShowRaRejected): _settings.ShowRaRejected = value; break;
                case nameof(ChartSettings.ShowDecPulse): _settings.ShowDecPulse = value; break;
                case nameof(ChartSettings.ShowDecRejected): _settings.ShowDecRejected = value; break;
            }
            await SettingsService.SaveChartSettingsAsync(_settings);
            StateHasChanged();
        }

        private async Task ToggleLoggingAsync()
        {
            if (_loggingActive)
            {
                await Logger.StopPulseLoggingAsync();
                _loggingActive = false;
            }
            else
            {
                await Logger.StartPulseLoggingAsync();
                _loggingActive = true;
            }
        }

        private async Task ClearChartAsync()
        {
            _raData.Clear();
            _raRejData.Clear();
            _decData.Clear();
            _decRejData.Clear();
            if (_chart is not null)
                await _chart.UpdateSeriesAsync(animate: false);
        }

        private async Task ExportPngAsync()
        {
            if (_chart is null) return;
            var imgUri = await _chart.GetDataUriAsync(new DataUriOptions());
            await JS.InvokeVoidAsync("chartWindowInterop.downloadDataUri", imgUri, "pulse-chart.png");
        }

        private async Task ExportCsvAsync()
        {
            await JS.InvokeVoidAsync("chartWindowInterop.exportChartCsv", "pulse-chart");
        }

        // -- Helpers ------------------------------------------------------------

        /// <summary>
        /// Returns the Y value for a pulse point in the user-selected unit.
        /// Rate is in degrees/sec; Duration is in milliseconds.
        /// ArcSeconds = duration_s × |rate| × 3600 (arc-seconds of correction applied).
        /// </summary>
        private double GetValue(PulsePointDto p) => _settings.PulseScale switch
        {
            "ArcSeconds" => p.Duration / 1000.0 * Math.Abs(p.Rate) * 3600.0,
            "Steps" => p.Duration,
            _ => p.Duration  // Milliseconds
        };

        private void BuildChartOptions()
        {
            var yTitle = _settings.PulseScale switch
            {
                "ArcSeconds" => "Arc-seconds/sec",
                "Steps" => "Steps",
                _ => "ms"
            };

            _chartOptions = new ApexChartOptions<PulsePointDto>
            {
                Chart = new Chart
                {
                    Background = "#1e1e1e",
                    ForeColor = "rgba(255,255,255,0.87)",
                    Animations = new Animations { Enabled = false },
                    Toolbar = new Toolbar
                    {
                        Show = true,
                        AutoSelected = AutoSelected.Zoom,
                        Tools = new Tools
                        {
                            Zoom = true,
                            Zoomin = true,
                            Zoomout = true,
                            Pan = true,
                            Reset = true,
                            Download = false  // export handled by the Blazor toolbar MudMenu
                        }
                    },
                    Zoom = new Zoom { Enabled = true }
                },
                Theme = new ApexCharts.Theme { Mode = Mode.Dark },
                Xaxis = new XAxis
                {
                    Type = XAxisType.Datetime,
                    Labels = new XAxisLabels
                    {
                        DatetimeUTC = true,
                        Show = true,
                        HideOverlappingLabels = false,
                        Format = "HH:mm:ss",
                        Style = new AxisLabelStyle { Colors = "rgba(255,255,255,0.87)" }
                    }
                },
                Yaxis =
                [
                    new YAxis
                    {
                        Title = new AxisTitle { Text = yTitle },
                        Labels = new YAxisLabels
                        {
                            Formatter = _settings.PulseScale switch
                            {
                                "ArcSeconds" => "function(val) { return parseFloat(val).toFixed(1); }",
                                _            => "function(val) { return Math.round(val).toString(); }"
                            }
                        }
                    }
                ],
                Stroke = new Stroke { Curve = Curve.Smooth, Width = [1, 0, 1, 0] },
                Markers = new Markers { Size = [0, 4, 0, 4] },
                Legend = new Legend { Show = true },
                Grid = new Grid { BorderColor = "rgba(255,255,255,0.12)" }
            };
        }

        private void AddToBuffer(PulsePointDto p)
        {
            var max = _settings.MaxPoints > 0 ? _settings.MaxPoints : 5000;
            if (p.Axis == 0)
            {
                var list = p.Rejected ? _raRejData : _raData;
                if (list.Count >= max) list.RemoveAt(0);
                list.Add(p);
            }
            else
            {
                var list = p.Rejected ? _decRejData : _decData;
                if (list.Count >= max) list.RemoveAt(0);
                list.Add(p);
            }
        }

        /// <summary>Flushed by the 1-second timer; calls UpdateSeriesAsync once per tick if data arrived.</summary>
        private void FlushChartUpdate()
        {
            if (!_pendingChartUpdate || _disposed) return;
            _pendingChartUpdate = false;
            InvokeAsync(async () =>
            {
                if (_chart is null || _disposed) return;
                try
                {
                    await _chart.UpdateSeriesAsync(animate: false);
                }
                catch (TaskCanceledException) { }
            });
        }

        // -- Dispose ------------------------------------------------------------

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            if (_refreshTimer is not null) await _refreshTimer.DisposeAsync();
            if (_loggingActive)
            {
                try { await Logger.StopPulseLoggingAsync(); } catch { }
            }
            if (_hub is not null)
            {
                try { await _hub.InvokeAsync("LeavePulseGroupAsync", DeviceNumber); } catch { }
                await _hub.DisposeAsync();
            }
        }
    }
}