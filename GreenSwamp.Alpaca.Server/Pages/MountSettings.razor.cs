using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Pages
{
    public partial class MountSettings
    {
        [Inject]
        NavigationManager NavManager { get; set; } = default!;

        [Parameter] public int DeviceNumber { get; set; }
        private int ActiveDeviceTabIndex { get; set; }
        private List<AlpacaDevice> _alpacaDevices = [];
        private Dictionary<int, GreenSwamp.Alpaca.Settings.Models.SkySettings> _deviceSettings = new();

        protected override void OnInitialized()
        {
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            _deviceSettings = SettingsService.GetAllDeviceSettings()
                .ToDictionary(d => d.DeviceNumber);
        }

        protected override void OnParametersSet()
        {
            _alpacaDevices = SettingsService.GetAlpacaDevices();
            var keys = GetConfiguredDeviceNumbers();
            var idx = keys.IndexOf(DeviceNumber);
            ActiveDeviceTabIndex = idx >= 0 ? idx : 0;
        }

        private void OnDeviceTabChanged(int index)
        {
            var keys = GetConfiguredDeviceNumbers();
            if (index >= 0 && index < keys.Count)
                NavManager.NavigateTo($"/mount-settings/{keys[index]}");
        }

        private List<int> GetConfiguredDeviceNumbers() =>
            _alpacaDevices
                .Select(d => d.DeviceNumber)
                .OrderBy(d => d)
                .ToList();

        private async Task OpenExportDialog()
        {
            var parameters = new DialogParameters();
            var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };

            await DialogService.ShowAsync<SettingsExportDialog>("", parameters, options);
        }

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

                var activeIndex = ActiveDeviceTabIndex;
                if (activeIndex < 0 || activeIndex >= deviceNumbers.Count) activeIndex = 0;

                var activeDeviceNumber = deviceNumbers[activeIndex];
                var mountType = _deviceSettings.GetValueOrDefault(activeDeviceNumber)?.Mount;

                return string.Equals(mountType, "Simulator", StringComparison.OrdinalIgnoreCase);
            }
        }

        void EditDevice(int deviceNumber)
        {
            NavManager.NavigateTo($"/settings-explorer?device={deviceNumber}&group=Mount%20Configuration");
        }

    }
}
