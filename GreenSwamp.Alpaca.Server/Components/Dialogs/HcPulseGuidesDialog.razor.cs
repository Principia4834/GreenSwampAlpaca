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
//using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Settings.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components.Dialogs
{
    public partial class HcPulseGuidesDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = default!;

        /// <summary>Device number used to load/save settings.</summary>
        [Parameter]
        public int DeviceNumber { get; set; }

        /// <summary>Upper bound for the Rate column; sourced from SkySettings.MaximumSlewRate.</summary>
        [Parameter] public double MaxRate { get; set; }

        private GreenSwamp.Alpaca.MountControl.Mount? _mount;
        private List<SkySettings.HcPulseGuide> _working = [];
        private bool _flipEw;
        private bool _flipNs;
        private bool _antiRa;
        private bool _antiDec;
        private bool _isDirty;

        protected override void OnInitialized()
        {
            _mount = Alpaca.MountControl.MountRegistry.GetInstance(DeviceNumber);

            if (_mount?.Settings != null)
            {
                _flipEw = _mount.Settings.HcFlipEw;
                _flipNs = _mount.Settings.HcFlipNs;
                _antiRa = _mount.Settings.HcAntiRa;
                _antiDec = _mount.Settings.HcAntiDec;
                if (_mount.Settings.HcPulseGuides is { Count: > 0 } guides)
                {
                    // Deep copy so edits don't mutate the live settings until Save is confirmed.
                    _working = [.. guides
                        .Select(g => new SkySettings.HcPulseGuide
                        {
                            Speed = g.Speed,
                            Duration = g.Duration,
                            Interval = g.Interval,
                            Rate = g.Rate
                        })];
                }
            }
        }

        private void OnEditorChanged() => _isDirty = true;

        private enum HcOption
        {
            FlipEw,
            FlipNs,
            AntiRa,
            AntiDec
        }

        private async Task OnHandControllerOptionChanged(HcOption option)
        {
            var persisted = SettingsService.GetDeviceSettings(DeviceNumber);

            switch (option)
            {
                case HcOption.FlipEw:
                    _mount?.Settings.HcFlipEw = _flipEw;
                    persisted?.HcFlipEW = _flipEw;
                    break;

                case HcOption.FlipNs:
                    _mount?.Settings.HcFlipNs = _flipNs;
                    persisted?.HcFlipNS = _flipNs;
                    break;

                case HcOption.AntiRa:
                    _mount?.Settings.HcAntiRa = _antiRa;
                    persisted?.HcAntiRa = _antiRa;
                    break;

                case HcOption.AntiDec:
                    _mount?.Settings.HcAntiDec = _antiDec;
                    persisted?.HcAntiDec = _antiDec;
                    break;
            }

            if (persisted != null)
                await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, persisted);
        }

        private async Task SaveAsync()
        {
            var settings = SettingsService.GetDeviceSettings(DeviceNumber);
            if (settings == null)
            {
                MudDialog.Cancel();
                return;
            }

            settings.HcPulseGuides = _working;
            settings.HcFlipEW = _flipEw;
            settings.HcFlipNS = _flipNs;
            settings.HcAntiRa = _antiRa;
            settings.HcAntiDec = _antiDec;
            await SettingsService.SaveDeviceSettingsAsync(DeviceNumber, settings);

            // Also update the live mount instance if it is running.
            // The mount uses a structurally identical but separate HcPulseGuide type, so project across.
            var mount = GreenSwamp.Alpaca.MountControl.MountRegistry.GetInstance(DeviceNumber);
            if (mount?.Settings != null)
            {
                mount.Settings.HcFlipEw = _flipEw;
                mount.Settings.HcFlipNs = _flipNs;
                mount.Settings.HcAntiRa = _antiRa;
                mount.Settings.HcAntiDec = _antiDec;
                mount.Settings.HcPulseGuides = [.. _working
                    .Select(g => new GreenSwamp.Alpaca.MountControl.Pulses.HcPulseGuide
                    {
                        Speed = g.Speed,
                        Duration = g.Duration,
                        Interval = g.Interval,
                        Rate = g.Rate
                    })];
            }

            MudDialog.Close(DialogResult.Ok(true));
        }

        private void Cancel() => MudDialog.Cancel();
    }
}