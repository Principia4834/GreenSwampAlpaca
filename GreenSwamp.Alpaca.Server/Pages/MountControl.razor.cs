using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using GreenSwamp.Alpaca.Server.Helpers;

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

        private const string PulseIcon = "<path d=\"M0 0h24v24H0z\" fill=\"none\"/>" +
            "<path d=\"M8.75025 18.063c-.41775 0-.77925-.186-.864-.5955l-1.17075-5.60475-.771 1.33125c-.15825.2685-.44775.45975-.759.45975L.92175 13.65375c-.48675 0-.882-.39525-.882-.88125 0-.48675.39525-.88275.882-.88275l3.75975 0 1.67625-2.83275c.18525-.31275.54075-.483.90375-.4215.35925.06.645.3345.7185.69075l.70875 3.42525L10.305 3.7845c.07575-.42.441-.71175.8685-.71175.00075 0 .003 0 .00375 0 .42825 0 .7935.297.8655.7185l1.725 10.06575.555-1.40625c.132-.3375.4575-.56175.81975-.56175l7.8255 0c.48675 0 .88125.39525.88125.88275 0 .486-.3945.88125-.88125.88125l-7.22625 0-1.4925 3.7725c-.14475.3675-.5115.59025-.91125.55275-.39225-.04125-.711-.339-.77775-.72825l-1.40775-8.2245L9.618 17.445c-.075.4155-.43425.618-.8565.618C8.75775 18.063 8.754 18.063 8.75025 18.063z\"/>"; protected override void OnInitialized()
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

        private static string FormatHMS(double hours)   => CoordinateFormatter.FormatHMS(hours);
        private static string FormatDMS(double degrees) => CoordinateFormatter.FormatDMS(degrees);
        
        // -- Manage Park Positions (status bar) --------------------------------
        private async Task OpenManageParkPositionsDialogAsync(int deviceNumber)
        {
            var parameters = new DialogParameters
            {
                [nameof(ManageParkPositionsDialog.DeviceNumber)] = deviceNumber
            };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                CloseOnEscapeKey = true,
                CloseButton = true
            };
            await DialogService.ShowAsync<ManageParkPositionsDialog>("", parameters, options);
        }

        // -- Park Position Selection (status bar) ------------------------------
        private void OnParkPositionSelectedInStatusBar(int deviceNumber, string positionName)
        {
            var mount = MountRegistry.GetInstance(deviceNumber);
            if (mount == null) { Snackbar.Add($"Mount device {deviceNumber} not found", Severity.Error); return; }

            var position = mount.Settings.ParkPositions?.Find(p => p.Name == positionName);
            if (position == null) { Snackbar.Add($"Park position '{positionName}' not found", Severity.Warning); return; }

            mount.ParkSelected = position;
            Snackbar.Add($"Park position set to: {positionName}", Severity.Info);
        }
    }
}
