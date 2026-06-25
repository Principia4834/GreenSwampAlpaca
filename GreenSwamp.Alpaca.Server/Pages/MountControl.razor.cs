using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Pages
{
    public partial class MountControl
    {
        [Parameter] public int DeviceNumber { get; set; }

        [Inject] private NavigationManager NavManager { get; set; } = default!;

        private int _activeDeviceTabIndex;
        private List<AlpacaDevice> _alpacaDevices = [];
        private Dictionary<int, GreenSwamp.Alpaca.Settings.Models.SkySettings> _deviceSettings = new();
        private enum CoordMode { RaDec, AltAz }
        private CoordMode _coordMode = CoordMode.RaDec;
        private bool EnableShutdown { get; set; } = false;
        private bool AllowShutdown => !EnableShutdown;

        private const long UiClientId = GreenSwamp.Alpaca.MountControl.Mount.UiInternalClientId;

        protected override void OnInitialized()
        {
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            _deviceSettings = SettingsService.GetAllDeviceSettings()
                .ToDictionary(d => d.DeviceNumber);

            StateService.StateChanged += OnStateChanged;
            SettingsService.DeviceSettingsChanged += OnDeviceSettingsChanged;
        }

        protected override void OnParametersSet()
        {
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            var keys = GetConfiguredDeviceNumbers();
            var idx = keys.IndexOf(DeviceNumber);
            _activeDeviceTabIndex = idx >= 0 ? idx : 0;
        }

        private void OnStateChanged(object? sender, EventArgs e) =>
            InvokeAsync(StateHasChanged);

        private void OnDeviceSettingsChanged(object? sender, GreenSwamp.Alpaca.Settings.Models.SkySettings updated)
        {
            _deviceSettings[updated.DeviceNumber] = updated;
            InvokeAsync(StateHasChanged);
        }

        void Shutdown()
        {
            try
            {
                Program.Lifetime?.StopApplication();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Shutdown failed: {ex.Message}", Severity.Error);
            }
        }

        public void Dispose()
        {
            StateService.StateChanged -= OnStateChanged;
            SettingsService.DeviceSettingsChanged -= OnDeviceSettingsChanged;
        }

        private void OnDeviceTabChanged(int index)
        {
            var keys = GetConfiguredDeviceNumbers();
            if (index >= 0 && index < keys.Count)
                NavManager.NavigateTo($"/mount-control/{keys[index]}");
        }

        /// <summary>Returns true when the UI's internal client is registered as connected.</summary>
        private bool IsUiClientConnected(int dn) =>
            MountRegistry.GetInstance(dn)?.IsClientConnected(UiClientId) ?? false;

        private async Task OnConnectToggleAsync(int dn)
        {
            try
            {
                var telescope = DeviceManager.GetTelescope((uint)dn);
                var connect = !IsUiClientConnected(dn);
                await Task.Run(() => telescope.Connected = connect);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Connect/Disconnect failed: {ex.Message}", Severity.Error);
            }
        }

        /// <summary>Sets VoiceActive for the given device and persists the change to JSON.</summary>
        private async Task OnVoiceActiveSetAsync(int dn, bool value)
        {
            if (!_deviceSettings.TryGetValue(dn, out var settings)) return;
            settings.VoiceActive = value;
            try
            {
                await SettingsService.SaveDeviceSettingsAsync(dn, settings);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to save voice setting: {ex.Message}", Severity.Error);
            }
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

        private static string FormatHMS(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return "N/A";
            var sign = hours < 0 ? "-" : "+";
            hours = Math.Abs(hours);
            var h = (int)hours;
            var m = (int)((hours - h) * 60);
            var s = ((hours - h) * 60 - m) * 60;
            return $"{sign}{h:00}h {m:00}m {s:00.00}s";
        }

        private static string FormatDMS(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
            var sign = degrees < 0 ? "-" : "+";
            degrees = Math.Abs(degrees);
            var d = (int)degrees;
            var m = (int)((degrees - d) * 60);
            var s = ((degrees - d) * 60 - m) * 60;
            return $"{sign}{d:00}\u00b0 {m:00}\u2032 {s:00.00}\u2033";
        }
    }
}
