using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Pages
{
    public partial class MountControl
    {
        [Parameter] public int DeviceNumber { get; set; }

        [Inject] private NavigationManager NavManager { get; set; } = default!;

        private int ActiveTabIndex { get; set; }
        private List<AlpacaDevice> _alpacaDevices = [];
        private Dictionary<int, GreenSwamp.Alpaca.Settings.Models.SkySettings> _deviceSettings = new();
        private enum CoordMode { RaDec, AltAz }
        private CoordMode _coordMode = CoordMode.RaDec;
        private bool EnableShutdown { get; set; } = false;
        private bool AllowShutdown => !EnableShutdown;

        private const long UiClientId = GreenSwamp.Alpaca.MountControl.Mount.UiInternalClientId;

        private const string LimitsOnIcon = "<path d=\"M0 0h24v24H0z\" fill=\"none\"/>" +
    "<path d=\"M12 21 0 9q2.4-2.45 5.5-3.725t6.5-1.275q3.425 0 6.525 1.275T24 9l-2.525 2.525q-.55-.25-1.125-.375t-1.2-.15l1.95-1.95q-1.95-1.475-4.2625-2.2625T12 6q-2.525 0-4.8375.7875T2.9 9.05l5.8 5.8q1.05-.625 2.45-.8125t2.55.1625q-.35.625-.525 1.3875t-.175 1.4375q0 .65.125 1.2625t.4 1.1875l-1.525 1.525ZM17 21q-.425 0-.7125-.2875T16 20v-3q0-.425.2875-.7125T17 16v-1q0-.825.5875-1.4125T19 13q.825 0 1.4125.5875T21 15v1q.425 0 .7125.2875T22 17v3q0 .425-.2875.7125T21 21h-4Zm1-5h2v-1q0-.425-.2875-.7125T19 14q-.425 0-.7125.2875T18 15v1Z\"/>";

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
            ActiveTabIndex = idx >= 0 ? idx : 0;
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

        private async Task OpenExportDialog()
        {
            var parameters = new DialogParameters();
            var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };

            await DialogService.ShowAsync<SettingsExportDialog>("", parameters, options);
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

        private bool IsActiveMountSimulator
        {
            get
            {
                var deviceNumbers = GetConfiguredDeviceNumbers();
                if (deviceNumbers.Count == 0) return false;

                var activeIndex = ActiveTabIndex;
                if (activeIndex < 0 || activeIndex >= deviceNumbers.Count) activeIndex = 0;

                var activeDeviceNumber = deviceNumbers[activeIndex];
                var mountType = _deviceSettings.GetValueOrDefault(activeDeviceNumber)?.Mount;

                return string.Equals(mountType, "Simulator", StringComparison.OrdinalIgnoreCase);
            }
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
