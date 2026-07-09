using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components
{
    public partial class HandControllerPanel
    {
        [Parameter] public int DeviceNumber { get; set; }
        [Parameter] public bool IsEnabled { get; set; }

        private ElementReference _gridElement;
        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<HandControllerPanel>? _dotNetRef;

        private GreenSwamp.Alpaca.MountControl.Mount? _mount;
        private int _speed = 4;
        private HcMode _mode = HcMode.Axes;
        private string _statusMessage = string.Empty;
        private bool _flipEw;
        private bool _flipNs;
        private bool _antiRa;
        private bool _antiDec;
        private bool _oneClickStart;
        private bool _disableKeysOnGoTo;
        private string? _activeOneClickDir;

        // Diagonal direction mappings
        private static readonly Dictionary<string, (SlewDirection RaStop, SlewDirection DecStop)> DiagonalStops = new()
        {
            ["NorthWest"] = (SlewDirection.SlewNoneRa, SlewDirection.SlewNoneDec),
            ["NorthEast"] = (SlewDirection.SlewNoneRa, SlewDirection.SlewNoneDec),
            ["SouthWest"] = (SlewDirection.SlewNoneRa, SlewDirection.SlewNoneDec),
            ["SouthEast"] = (SlewDirection.SlewNoneRa, SlewDirection.SlewNoneDec)
        };

        // Cardinal direction mappings
        private static readonly Dictionary<string, SlewDirection> CardinalStops = new()
        {
            ["North"] = SlewDirection.SlewNoneDec,
            ["South"] = SlewDirection.SlewNoneDec,
            ["East"] = SlewDirection.SlewNoneRa,
            ["West"] = SlewDirection.SlewNoneRa
        };

        // Direction name to SlewDirection enum mapping
        private static readonly Dictionary<string, SlewDirection> DirectionMap = new()
        {
            ["North"] = SlewDirection.SlewNorth,
            ["South"] = SlewDirection.SlewSouth,
            ["East"] = SlewDirection.SlewEast,
            ["West"] = SlewDirection.SlewWest,
            ["NorthEast"] = SlewDirection.SlewNorthEast,
            ["NorthWest"] = SlewDirection.SlewNorthWest,
            ["SouthEast"] = SlewDirection.SlewSouthEast,
            ["SouthWest"] = SlewDirection.SlewSouthWest
        };

        protected override void OnInitialized()
        {
            _mount = MountRegistry.GetInstance(DeviceNumber);

            if (_mount?.Settings != null)
            {
                _speed = (int)_mount.Settings.HcSpeed;
                _mode = _mount.Settings.HcMode;
                _flipEw = _mount.Settings.HcFlipEw;
                _flipNs = _mount.Settings.HcFlipNs;
                _antiRa = _mount.Settings.HcAntiRa;
                _antiDec = _mount.Settings.HcAntiDec;
                _oneClickStart = _mount.Settings.HcOneClickStart;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/handController.js");
                    _dotNetRef = DotNetObjectReference.Create(this);
                    await _jsModule.InvokeVoidAsync("init", _dotNetRef, _gridElement);
                }
                catch (Exception ex)
                {
                    _statusMessage = $"Failed to initialize controller: {ex.Message}";
                    StateHasChanged();
                }
            }
        }

        [JSInvokable]
        public void OnButtonDown(string direction)
        {
            if (!IsEnabled || _mount == null || !DirectionMap.TryGetValue(direction, out var slewDir))
                return;

            try
            {
                var speed = (SlewSpeed)_speed;

                if (_oneClickStart)
                {
                    if (_activeOneClickDir == direction)
                    {
                        StopDirection(direction);
                        _activeOneClickDir = null;
                        _statusMessage = "Stopped";
                    }
                    else
                    {
                        if (_activeOneClickDir != null)
                            StopDirection(_activeOneClickDir);
                        _mount.HcMoves(speed, slewDir);
                        _activeOneClickDir = direction;
                        _statusMessage = $"Moving {direction} at speed {_speed}";
                    }
                }
                else
                {
                    _mount.HcMoves(speed, slewDir);
                    _statusMessage = $"Moving {direction} at speed {_speed}";
                }

                StateHasChanged();
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                StateHasChanged();
            }
        }

        [JSInvokable]
        public void OnButtonUp(string direction)
        {
            if (!IsEnabled || _mount == null)
                return;

            if (_oneClickStart)
                return;

            try
            {
                var speed = (SlewSpeed)_speed;

                if (DiagonalStops.TryGetValue(direction, out var stops))
                {
                    _mount.HcMoves(speed, stops.RaStop);
                    _mount.HcMoves(speed, stops.DecStop);
                    _statusMessage = "Stopped";
                }
                else if (CardinalStops.TryGetValue(direction, out var stop))
                {
                    _mount.HcMoves(speed, stop);
                    _statusMessage = "Stopped";
                }

                StateHasChanged();
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                StateHasChanged();
            }
        }

        [JSInvokable]
        public void OnStopPressed()
        {
            if (!IsEnabled || _mount == null)
                return;

            try
            {
                _mount.AbortSlew(speak: false);
                _activeOneClickDir = null;
                _statusMessage = "Emergency stop";
                StateHasChanged();
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                StateHasChanged();
            }
        }

        private async Task OnSpeedChanged()
        {
            if (_mount != null)
                _mount.Settings.HcSpeed = (SlewSpeed)_speed;

            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);
            if (persisted != null)
            {
                persisted.HcSpeed = ((SlewSpeed)_speed).ToString();
                await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
            }
        }

        private async Task OnModeChanged()
        {
            if (_mount != null)
                _mount.Settings.HcMode = _mode;

            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);
            if (persisted != null)
            {
                persisted.HcMode = _mode.ToString();
                await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
            }
        }

        private async Task OnFlipEwChanged()
        {
            if (_mount != null) _mount.Settings.HcFlipEw = _flipEw;
            await PersistHcFlags();
        }

        private async Task OnFlipNsChanged()
        {
            if (_mount != null) _mount.Settings.HcFlipNs = _flipNs;
            await PersistHcFlags();
        }

        private async Task OnAntiRaChanged()
        {
            if (_mount != null) _mount.Settings.HcAntiRa = _antiRa;
            await PersistHcFlags();
        }

        private async Task OnAntiDecChanged()
        {
            if (_mount != null) _mount.Settings.HcAntiDec = _antiDec;
            await PersistHcFlags();
        }

        private async Task OnOneClickStartChanged()
        {
            if (!_oneClickStart && _activeOneClickDir != null)
            {
                StopDirection(_activeOneClickDir);
                _activeOneClickDir = null;
            }

            if (_mount != null)
                _mount.Settings.HcOneClickStart = _oneClickStart;

            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);
            if (persisted != null)
            {
                persisted.HcOneClickStart = _oneClickStart;
                await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
            }
        }

        private async Task OnDisableKeysOnGoToChanged()
        {
            if (_mount != null)
                _mount.Settings.DisableKeysOnGoTo = _disableKeysOnGoTo;

            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);
            if (persisted != null)
            {
                persisted.DisableKeysOnGoTo = _disableKeysOnGoTo;
                await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
            }
        }

        private void StopDirection(string direction)
        {
            if (_mount == null) return;
            var speed = (SlewSpeed)_speed;
            if (DiagonalStops.TryGetValue(direction, out var stops))
            {
                _mount.HcMoves(speed, stops.RaStop);
                _mount.HcMoves(speed, stops.DecStop);
            }
            else if (CardinalStops.TryGetValue(direction, out var stop))
            {
                _mount.HcMoves(speed, stop);
            }
        }

        private string GetButtonClass(string direction) =>
            _oneClickStart && _activeOneClickDir == direction ? "hc-btn hc-active" : "hc-btn";

        private async Task PersistHcFlags()
        {
            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);
            if (persisted == null) return;
            persisted.HcFlipEW = _flipEw;
            persisted.HcFlipNS = _flipNs;
            persisted.HcAntiRa = _antiRa;
            persisted.HcAntiDec = _antiDec;
            await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
        }

        /// <summary>Opens the HC Pulse Guides editor dialog for this device.</summary>
        private async Task OpenHcPulseGuidesDialogAsync()
        {
            var maxRate = _mount?.Settings?.MaxSlewRate ?? 3.5;

            var parameters = new DialogParameters<HcPulseGuidesDialog>
            {
                { d => d.DeviceNumber, DeviceNumber },
                { d => d.MaxRate,      maxRate      }
            };

            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            await DialogService.ShowAsync<HcPulseGuidesDialog>("HC Pulse Guides", parameters, options);
        }

        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("dispose");
                    await _jsModule.DisposeAsync();
                }
                catch { /* Ignore disposal errors */ }
            }

            _dotNetRef?.Dispose();
        }
    }
}