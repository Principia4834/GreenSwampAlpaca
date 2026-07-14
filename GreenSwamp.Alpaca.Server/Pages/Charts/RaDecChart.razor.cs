using ApexCharts;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Pages.Charts
{
    public partial class RaDecChart
    {
        [Parameter] public int DeviceNumber { get; set; }

        // -- State --------------------------------------------------------------
        private ChartSettings _settings = new();
        private readonly List<ChartPointDto> _axis1Data = [];
        private readonly List<ChartPointDto> _axis2Data = [];
        private ApexChart<ChartPointDto>? _chart;
        private ApexChartOptions<ChartPointDto> _chartOptions = new();
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

            _hub.On<ChartPointDto>("ReceiveAxis1Point", OnAxis1Point);
            _hub.On<ChartPointDto>("ReceiveAxis2Point", OnAxis2Point);
            _hub.On<IReadOnlyList<ChartPointDto>, IReadOnlyList<ChartPointDto>>("ReceiveRaDecHistory", OnHistory);

            _hub.Reconnecting += _ => { _hubState = HubConnectionState.Reconnecting; InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Reconnected += _ => { _hubState = HubConnectionState.Connected; InvokeAsync(StateHasChanged); return Task.CompletedTask; };
            _hub.Closed += _ => { _hubState = HubConnectionState.Disconnected; InvokeAsync(StateHasChanged); return Task.CompletedTask; };

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
            _refreshTimer = new System.Threading.Timer(_ => FlushChartUpdate(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        // -- SignalR handlers ---------------------------------------------------

        private void OnAxis1Point(ChartPointDto point)
        {
            if (_disposed) return;
            InvokeAsync(async () =>
            {
                TrimBuffer(_axis1Data);
                _axis1Data.Add(point);
                if (_loggingActive) await Logger.LogRaDecPointAsync(1, point);
                if (_settings.ShowAxis1) _pendingChartUpdate = true;
            });
        }

        private void OnAxis2Point(ChartPointDto point)
        {
            if (_disposed) return;
            InvokeAsync(async () =>
            {
                TrimBuffer(_axis2Data);
                _axis2Data.Add(point);
                if (_loggingActive) await Logger.LogRaDecPointAsync(2, point);
                if (_settings.ShowAxis2) _pendingChartUpdate = true;
            });
        }

        private void OnHistory(IReadOnlyList<ChartPointDto> axis1, IReadOnlyList<ChartPointDto> axis2)
        {
            if (_disposed) return;
            InvokeAsync(async () =>
            {
                _axis1Data.Clear();
                _axis1Data.AddRange(axis1.TakeLast(_settings.MaxPoints));
                _axis2Data.Clear();
                _axis2Data.AddRange(axis2.TakeLast(_settings.MaxPoints));
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
            _settings.RaDecScale = scale;
            await SettingsService.SaveChartSettingsAsync(_settings);
            BuildChartOptions();
            // @key="@_settings.RaDecScale" on <ApexChart> causes Blazor to recreate the
            // component with the new options, including the correct y-axis title.
            StateHasChanged();
        }

        private async Task OnSeriesToggleAsync(bool value, int axis)
        {
            if (axis == 1) _settings.ShowAxis1 = value;
            else _settings.ShowAxis2 = value;
            await SettingsService.SaveChartSettingsAsync(_settings);
            StateHasChanged();
        }

        private async Task ToggleLoggingAsync()
        {
            if (_loggingActive)
            {
                await Logger.StopRaDecLoggingAsync();
                _loggingActive = false;
            }
            else
            {
                await Logger.StartRaDecLoggingAsync();
                _loggingActive = true;
            }
        }

        private async Task ClearChartAsync()
        {
            _axis1Data.Clear();
            _axis2Data.Clear();
            if (_chart is not null)
                await _chart.UpdateSeriesAsync(animate: false);
        }

        private async Task ExportPngAsync()
        {
            if (_chart is null) return;
            var imgUri = await _chart.GetDataUriAsync(new DataUriOptions());
            await JS.InvokeVoidAsync("chartWindowInterop.downloadDataUri", imgUri, "radec-chart.png");
        }

        private async Task ExportCsvAsync()
        {
            await JS.InvokeVoidAsync("chartWindowInterop.exportChartCsv", "radec-chart");
        }

        // -- Helpers ------------------------------------------------------------

        private void BuildChartOptions()
        {
            var yTitle = _settings.RaDecScale switch
            {
                "Degrees" => "Degrees",
                "ArcSeconds" => "Arc-seconds",
                _ => "Steps"
            };

            _chartOptions = new ApexChartOptions<ChartPointDto>
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
                    Range = 30_000,
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
                            Formatter = _settings.RaDecScale switch
                            {
                                "Degrees"    => "function(val) { return parseFloat(val).toFixed(3); }",
                                "ArcSeconds" => "function(val) { return parseFloat(val).toFixed(1); }",
                                _            => "function(val) { return Math.round(val).toString(); }"
                            }
                        }
                    }
                ],
                Stroke = new Stroke { Curve = Curve.Smooth, Width = [1, 1] },
                Legend = new Legend { Show = true },
                Grid = new Grid { BorderColor = "rgba(255,255,255,0.12)" }
            };
        }

        /// <summary>
        /// Converts a raw step value from the mount to the user-selected unit.
        /// axisIndex: 0 = RA (Axis 1), 1 = Dec (Axis 2).
        /// </summary>
        private double GetValue(ChartPointDto p, int axisIndex)
        {
            if (_settings.RaDecScale == "Steps") return p.Value;
            var stepsPerRev = StateService.GetCurrentState(DeviceNumber).StepsPerRevolution;
            var spr = stepsPerRev is { Length: > 0 } ? stepsPerRev[Math.Min(axisIndex, stepsPerRev.Length - 1)] : 0L;
            if (spr <= 0) return p.Value;  // guard: mount not yet initialised, show raw steps
            return _settings.RaDecScale switch
            {
                "Degrees" => p.Value * 360.0 / spr,
                "ArcSeconds" => p.Value * 360.0 * 3600.0 / spr,
                _ => p.Value
            };
        }

        private void TrimBuffer(List<ChartPointDto> list)
        {
            var max = _settings.MaxPoints > 0 ? _settings.MaxPoints : 5000;
            if (list.Count >= max) list.RemoveAt(0);
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
