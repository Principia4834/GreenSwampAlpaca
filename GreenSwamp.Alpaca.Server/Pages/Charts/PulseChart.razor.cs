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

        [SupplyParameterFromQuery(Name = "type")]
        public string? AlignmentMode { get; set; }

        [SupplyParameterFromQuery(Name = "label")]
        public string? Label { get; set; }

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
        // SubLists are guaranteed non-null after OnInitializedAsync; ! suppresses nullable warning.
        private IEnumerable<PulsePointDto> ChartItemsRa     => IsHistoricalMode ? _pausedRa     : (IEnumerable<PulsePointDto>)_raSubList!;
        private IEnumerable<PulsePointDto> ChartItemsRaRej  => IsHistoricalMode ? _pausedRaRej  : (IEnumerable<PulsePointDto>)_raRejSubList!;
        private IEnumerable<PulsePointDto> ChartItemsDec    => IsHistoricalMode ? _pausedDec    : (IEnumerable<PulsePointDto>)_decSubList!;
        private IEnumerable<PulsePointDto> ChartItemsDecRej => IsHistoricalMode ? _pausedDecRej : (IEnumerable<PulsePointDto>)_decRejSubList!;

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
                case nameof(ChartSettings.ShowRaPulse): _settings.ShowRaPulse = value; break;
                case nameof(ChartSettings.ShowRaRejected): _settings.ShowRaRejected = value; break;
                case nameof(ChartSettings.ShowDecPulse): _settings.ShowDecPulse = value; break;
                case nameof(ChartSettings.ShowDecRejected): _settings.ShowDecRejected = value; break;
            }
            await SettingsService.SaveChartSettingsAsync(_settings);
            _chartKey = $"pulse-{_displayMode}-{_settings.PulseScale}-{_settings.PulseSeriesType}-{_settings.PulseWindowSeconds}s-ra{_settings.ShowRaPulse}-rarej{_settings.ShowRaRejected}-dec{_settings.ShowDecPulse}-decrej{_settings.ShowDecRejected}";
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

        // -- Chart options builder ----------------------------------------------

        /// <summary>
        /// Rebuilds ApexChart options from current settings.
        /// Must be called before bumping _chartKey so the new options are applied on recreation.
        /// </summary>
        private void BuildChartOptions()
        {
            var yTitle = _settings.PulseScale switch
            {
                "ArcSeconds" => "Arc Seconds",
                "Steps"      => "Steps",
                _            => "mS"
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
                        Show = isHistorical,
                        Tools = new Tools
                        {
                            Download = isHistorical,
                            Zoom = isHistorical,
                            Pan = isHistorical,
                            Reset = isHistorical
                        },
                        Export = new ExportOptions
                        {
                            Csv = new ExportCSV
                            {
                                ColumnDelimiter = "|",
                                HeaderCategory = "Timestamp",
                                HeaderValue = "Value",
                                CategoryFormatter = "function(val) { return new Date(val).toISOString().slice(0,-1); }",
                                ValueFormatter = "function(val) { return Number(val).toFixed(3); }"
                            }
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
                    Bar = new PlotOptionsBar { ColumnWidth = "4px" }
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
                        Labels = new YAxisLabels { Formatter = yFormatter }
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
        /// Returns the signed Y-axis value for a pulse point in the selected scale.
        /// • Milliseconds : duration_ms × sign(rate)
        /// • ArcSeconds   : (duration_ms / 1000) × rate_deg_s × 3600  (sign from rate)
        /// • Steps        : arcSeconds × (StepsPerRevolution[axis] / 360 / 3600)
        ///                  — reads live StepsPerRevolution from StateService, matching
        ///                  the same pattern used by RaDecChart.ScaleValue().
        /// Falls back to signed milliseconds when mount state is unavailable.
        /// </summary>
        private double GetValue(PulsePointDto p)
        {
            var arcSeconds = p.Duration / 1000.0 * p.Rate * 3600.0;

            return _settings.PulseScale switch
            {
                "ArcSeconds" => arcSeconds,
                "Steps" => ArcSecondsToSteps(arcSeconds, p.Axis),
                _ => p.Duration * Math.Sign(p.Rate)  // Milliseconds — signed duration
            };
        }

        /// <summary>
        /// Converts a signed arc-second value to steps using the mount's live
        /// StepsPerRevolution for the given axis — same source as RaDecChart.ScaleValue().
        /// Returns the arc-second value unchanged if the mount state is unavailable.
        /// </summary>
        private double ArcSecondsToSteps(double arcSeconds, int axisIndex)
        {
            var stepsPerRev = StateService.GetCurrentState(DeviceNumber).StepsPerRevolution;
            var spr = stepsPerRev is { Length: > 0 }
                ? stepsPerRev[Math.Min(axisIndex, stepsPerRev.Length - 1)]
                : 0L;
            if (spr <= 0) return arcSeconds; // mount not connected — show arc-seconds
            return arcSeconds * spr / (360.0 * 3600.0);
        }
    }
}
