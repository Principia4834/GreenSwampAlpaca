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

using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Pages
{
    public partial class MountStatus
    {
        [Parameter]
        public int DeviceNumber { get; set; }

        private int ActiveTabIndex { get; set; }
        private List<AlpacaDevice> _alpacaDevices = [];
        private Dictionary<int, GreenSwamp.Alpaca.Settings.Models.SkySettings> _deviceSettings = new();
        private readonly Dictionary<int, long> _lastLimitWarningSequence = new();
        private readonly Dictionary<int, bool> _lastAtParkState = new();
        private readonly Dictionary<int, bool> _lastSlewingState = new();
        private readonly Dictionary<int, bool> _lastHomeState = new();
        private readonly Dictionary<int, bool> _lastConnectedState = new();
        private readonly Dictionary<int, bool> _lastTrackingState = new();

        protected override void OnInitialized()
        {
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            _deviceSettings = SettingsService.GetAllDeviceSettings()
                .ToDictionary(d => d.DeviceNumber);

            foreach (var dn in GetConfiguredDeviceNumbers())
            {
                _lastAtParkState[dn] = StateService.GetCurrentState(dn).AtPark;
                _lastSlewingState[dn] = StateService.GetCurrentState(dn).Slewing;
                _lastHomeState[dn] = StateService.GetCurrentState(dn).AtHome;
                _lastConnectedState[dn] = IsUiClientConnected(dn);
                _lastTrackingState[dn] = StateService.GetCurrentState(dn).Tracking;
            }

            StateService.StateChanged += OnStateChanged;
            SettingsService.DeviceSettingsChanged += OnDeviceSettingsChanged;
        }

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            var keys = GetConfiguredDeviceNumbers();
            var idx = keys.IndexOf(DeviceNumber);
            ActiveTabIndex = idx >= 0 ? idx : 0;
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            foreach (var dn in GetConfiguredDeviceNumbers())
            {
                var state = StateService.GetCurrentState(dn);
                var voiceEnabled = state.EnableVoice && state.VoiceActive;

                var wasAtPark = _lastAtParkState.GetValueOrDefault(dn);
                if (!wasAtPark && state.AtPark)
                {
                    if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Parked", state.VoiceName, state.VoiceVolume));
                }

                var wasSlewing = _lastSlewingState.GetValueOrDefault(dn);
                if (!wasSlewing && state.Slewing)
                {
                    if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Started Slewing", state.VoiceName, state.VoiceVolume));
                }
                else if (wasSlewing && !state.Slewing)
                {
                    if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Finished Slewing", state.VoiceName, state.VoiceVolume));
                }

                var wasHome = _lastHomeState.GetValueOrDefault(dn);
                if (!wasHome && state.AtHome)
                {
                    if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("At Home", state.VoiceName, state.VoiceVolume));
                }

                var wasConnected = _lastConnectedState.GetValueOrDefault(dn);
                if (!wasConnected && IsUiClientConnected(dn))
                {
                    if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Mount Connected", state.VoiceName, state.VoiceVolume));
                }

                // var wasTracking = _lastTrackingState.GetValueOrDefault(dn);
                // if (!wasTracking && state.Tracking)
                // {
                //     if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Tracking On", state.VoiceName, state.VoiceVolume));
                // }
                // else if (wasTracking && !state.Tracking)
                // {
                //     if (voiceEnabled) _ = InvokeAsync(() => Tts.SpeakAsync("Tracking Off", state.VoiceName, state.VoiceVolume));
                // }

                _lastAtParkState[dn] = state.AtPark;
                _lastSlewingState[dn] = state.Slewing;
                _lastHomeState[dn] = state.AtHome;
                _lastConnectedState[dn] = IsUiClientConnected(dn);
                _lastTrackingState[dn] = state.Tracking;

                var seen = _lastLimitWarningSequence.GetValueOrDefault(dn);
                if (state.LimitWarningSequence <= seen) continue;

                _lastLimitWarningSequence[dn] = state.LimitWarningSequence;
                if (!string.IsNullOrWhiteSpace(state.LimitWarningMessage))
                {
                    Snackbar.Add(state.LimitWarningMessage, Severity.Warning);
                }
            }

            InvokeAsync(StateHasChanged);
        }

        private void OnDeviceSettingsChanged(object? sender, GreenSwamp.Alpaca.Settings.Models.SkySettings updated)
        {
            _deviceSettings[updated.DeviceNumber] = updated;
            InvokeAsync(StateHasChanged);
        }

        public void Dispose()
        {
            StateService.StateChanged -= OnStateChanged;
            SettingsService.DeviceSettingsChanged -= OnDeviceSettingsChanged;
        }

        // UI client ID delegates to the canonical constant defined on Mount
        // (ASCOM Alpaca default ClientId; AlpacaRequestContext.ClientId defaults to 0 in the Blazor context)
        private const long UiClientId = GreenSwamp.Alpaca.MountControl.Mount.UiInternalClientId;

        /// <summary>Returns true when the UI's internal client (key 0) is registered as connected.</summary>
        private bool IsUiClientConnected(int dn) =>
            MountRegistry.GetInstance(dn)?.IsClientConnected(UiClientId) ?? false;

        private async Task OpenExportDialog()
        {
            var parameters = new DialogParameters();
            var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };

            await DialogService.ShowAsync<SettingsExportDialog>("", parameters, options);
        }

        private List<int> GetConfiguredDeviceNumbers() =>
            _alpacaDevices
                .Select(d => d.DeviceNumber)
                .OrderBy(d => d)
                .ToList();

        private string TabLabel(int deviceNumber)
        {
            var device = _alpacaDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
            return string.IsNullOrWhiteSpace(device?.DeviceName)
                ? $"Device {deviceNumber}"
                : device.DeviceName;
        }

        private string GetTabClass(int deviceNumber)
        {
            var mountSettings = _deviceSettings.GetValueOrDefault(deviceNumber);
            return mountSettings?.Mount == "Simulator" ? "gs-tab-simulator" : string.Empty;
        }

        private string FormatHMS(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return "N/A";
            var sign = hours < 0 ? "-" : "+";
            hours = Math.Abs(hours);
            var h = (int)hours;
            var m = (int)((hours - h) * 60);
            var s = ((hours - h) * 60 - m) * 60;
            return $"{sign}{h:00}h {m:00}m {s:00.00}s";
        }

        private string FormatDMS(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
            var sign = degrees < 0 ? "-" : "+";
            degrees = Math.Abs(degrees);
            var d = (int)degrees;
            var m = (int)((degrees - d) * 60);
            var s = ((degrees - d) * 60 - m) * 60;
            return $"{sign}{d:00}° {m:00}' {s:00.00}\"";
        }

        private string FormatDegrees(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
            return $"{degrees:F4}°";
        }
    }
}