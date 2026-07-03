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

using GreenSwamp.Alpaca.Server.Components;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Globalization;

namespace GreenSwamp.Alpaca.Server.Pages
{
    public partial class MountStatus
    {
        [Parameter]
        public int DeviceNumber { get; set; }

        private int ActiveTabIndex { get; set; }
        private List<AlpacaDevice> _alpacaDevices = [];
        private Dictionary<int, GreenSwamp.Alpaca.Settings.Models.SkySettings> _deviceSettings = new();

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
            base.OnParametersSet();
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

        //private void OpenHelloWorldWindow()
        //{
        //    FloatingWindowManager.Open("Mount Status Demo", "Hello World!");
        //}

        public void Dispose()
        {
            StateService.StateChanged -= OnStateChanged;
            SettingsService.DeviceSettingsChanged -= OnDeviceSettingsChanged;
        }

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

        private string BoolToSupported(bool value)
        {
            return value ? "Supported" : "Not supported";
        }

        private string StepsToArcSecs(long steps)
        {
            return Math.Round(steps / 360.0 / 3600, 2).ToString(CultureInfo.CurrentCulture);
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