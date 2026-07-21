using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Newtonsoft.Json.Linq;

namespace GreenSwamp.Alpaca.Server.Components.ButtonBar
{
    public partial class MountButtonBar
    {
        [Parameter, EditorRequired] public TelescopeStateModel State { get; set; } = new();
        [Parameter, EditorRequired] public int DeviceNumber { get; set; }
        [Parameter] public bool IsConnected { get; set; }

        // New layout/visibility toggles
        [Parameter] public bool Vertical { get; set; } = false;
        [Parameter] public bool ShowPrimaryButtons { get; set; } = true;
        [Parameter] public bool ShowStopButton { get; set; } = true;

        private GreenSwamp.Alpaca.MountControl.Mount? Mount => MountRegistry.GetInstance(DeviceNumber);

        private static readonly DialogOptions _confirmOptions = new()
        
        {
            MaxWidth = MaxWidth.ExtraSmall,
            CloseOnEscapeKey = true
        };

        private static readonly DialogOptions _configOptions = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseOnEscapeKey = true,
            BackdropClick = false
        };

        // -- Park --------------------------------------------------------------
        private async Task OnParkClickAsync()
        {
            if (State.AtPark)
            {
                var confirmed = await ConfirmAsync("Unpark the mount?", "Unpark");
                if (!confirmed) return;
                await ExecuteAsync(
                    () => DeviceManager.GetTelescope((uint)DeviceNumber).Unpark(),
                    "Unpark failed");
            }
            else
            {
                var confirmed = await ConfirmAsync(
                    string.IsNullOrEmpty(State.ParkSelectedName)
                        ? "Park the mount?"
                        : $"Park the mount at '{State.ParkSelectedName}'?",
                    "Park");
                if (!confirmed) return;
                await ExecuteAsync(
                    () => DeviceManager.GetTelescope((uint)DeviceNumber).Park(),
                    "Park failed");
            }
        }

        // -- Home --------------------------------------------------------------
        private async Task OnHomeClickAsync()
        {
            var confirmed = await ConfirmAsync("Slew to the home position?", "Go to Home");
            if (!confirmed) return;
            await ExecuteAsync(
                () => DeviceManager.GetTelescope((uint)DeviceNumber).FindHome(),
                "Home failed");
        }

        // -- Tracking ----------------------------------------------------------
        private async Task OnTrackingClickAsync()
        {
            await ExecuteAsync(() =>
            {
                var telescope = DeviceManager.GetTelescope((uint)DeviceNumber);
                telescope.Tracking = !telescope.Tracking;
            }, "Tracking toggle failed");
        }

        // -- ReSync ------------------------------------------------------------
        private async Task OnReSyncClickAsync()
        {
            var parameters = new DialogParameters<ReSyncDialog>
        {
            { x => x.ParkPositionNames, State.ParkPositionNames }
        };

            var dialog = await DialogService.ShowAsync<ReSyncDialog>("ReSync Axes", parameters, _configOptions);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            if (result.Data is not ReSyncDialog.ReSyncRequest request) return;

            var mount = Mount;
            if (mount == null) { Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error); return; }

            await ExecuteAsync(() =>
            {
                if (request.Mode == ReSyncDialog.ReSyncMode.Home)
                {
                    mount.ReSync();
                }
                else
                {
                    var position = mount.Settings.ParkPositions?.Find(p => p.Name == request.ParkPositionName);
                    mount.ReSync(position);
                }
            }, "ReSync failed");
        }

        // -- Flip SOP ----------------------------------------------------------
        private async Task OnFlipSopClickAsync()
        {
            var parameters = new DialogParameters<FlipSopDialog>
        {
            { x => x.FlipOnNextGoto, State.FlipOnNextGoto }
        };

            var dialog = await DialogService.ShowAsync<FlipSopDialog>("Flip Side of Pier", parameters, _configOptions);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            if (result.Data is not FlipSopDialog.FlipSopResult flipResult) return;

            var mount = Mount;
            if (mount == null) { Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error); return; }

            mount.FlipOnNextGoto = flipResult.FlipOnNextGoto;

            if (flipResult.DoFlip)
            {
                var oppositeSide = State.SideOfPier == ASCOM.Common.DeviceInterfaces.PointingState.Normal
                    ? ASCOM.Common.DeviceInterfaces.PointingState.ThroughThePole
                    : ASCOM.Common.DeviceInterfaces.PointingState.Normal;

                try
                {
                    await Task.Run(() => mount.SetSideOfPier(oppositeSide));
                }
                catch (InvalidOperationException ex)
                {
                    Snackbar.Add(ex.Message, Severity.Warning);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Flip SOP failed: {ex.Message}", Severity.Error);
                }
            }
        }

        // -- AutoHome ----------------------------------------------------------
        private async Task OnAutoHomeClickAsync()
        {
            var mount = Mount;
            if (mount == null) { Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error); return; }

            var parameters = new DialogParameters<AutoHomeDialog>
        {
            { x => x.IsGermanPolarMode, State.IsGermanPolarMode },
            { x => x.InitialAxisX,      State.AutoHomeAxisX },
            { x => x.InitialAxisY,      State.AutoHomeAxisY },
            { x => x.DeviceNumber,      DeviceNumber }
        };

            await DialogService.ShowAsync<AutoHomeDialog>("AutoHome", parameters, _configOptions);
        }

        private const string HomeSearchIcon = "<path d=\"M0 0h24v24H0z\" fill=\"none\"/>" +
                            "<defs><mask id=\"homeSearchCut\">" +
                                "<rect x=\"0\" y=\"0\" width=\"24\" height=\"24\" fill=\"white\"/>" +
                                "<path d=\"M9.5 4.825a4.675 4.675 0 1 1 0 9.35a4.675 4.675 0 0 1 0-9.35M12.805 12.805l4.675 4.675\" fill=\"none\" stroke=\"black\" stroke-width=\"3.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>" +
                            "</mask></defs>" +
                            "<path d=\"M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z\" mask=\"url(#homeSearchCut)\"/>" +
                            "<path d=\"M9.5 4.825a4.675 4.675 0 1 1 0 9.35a4.675 4.675 0 0 1 0-9.35M12.805 12.805l4.675 4.675\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";

        // -- Limits ------------------------------------------------------------
        private async Task OnLimitsClickAsync()
        {
            var mount = Mount;
            if (mount == null) { Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error); return; }

            await ExecuteAsync(() => mount.LimitsOn = !mount.LimitsOn, "Limits toggle failed");
        }

        private const string LimitsOnIcon = "<path d=\"M0 0h24v24H0z\" fill=\"none\"/>" +
            "<path d=\"M12 21 0 9q2.4-2.45 5.5-3.725t6.5-1.275q3.425 0 6.525 1.275T24 9l-2.525 2.525q-.55-.25-1.125-.375t-1.2-.15l1.95-1.95q-1.95-1.475-4.2625-2.2625T12 6q-2.525 0-4.8375.7875T2.9 9.05l5.8 5.8q1.05-.625 2.45-.8125t2.55.1625q-.35.625-.525 1.3875t-.175 1.4375q0 .65.125 1.2625t.4 1.1875l-1.525 1.525ZM17 21q-.425 0-.7125-.2875T16 20v-3q0-.425.2875-.7125T17 16v-1q0-.825.5875-1.4125T19 13q.825 0 1.4125.5875T21 15v1q.425 0 .7125.2875T22 17v3q0 .425-.2875.7125T21 21h-4Zm1-5h2v-1q0-.425-.2875-.7125T19 14q-.425 0-.7125.2875T18 15v1Z\"/>";

        // -- Voice Enable ------------------------------------------------------

        /// <summary>Sets VoiceActive for the given device and persists the change to JSON.</summary>
        private async Task OnVoiceActiveSetAsync()
        {
            Mount.Settings.VoiceActive = !Mount.Settings.VoiceActive;
        }



        // -- Stop --------------------------------------------------------------
        private async Task OnStopClickAsync()
        {
            var mount = Mount;
            if (mount == null) { Snackbar.Add($"Mount device {DeviceNumber} not found", Severity.Error); return; }

            await ExecuteAsync(() => mount.EmergencyStopAll(), "Stop failed");
        }

        // -- Helpers -----------------------------------------------------------
        private async Task<bool> ConfirmAsync(string message, string confirmText)
        {
            var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, message },
            { x => x.ConfirmText, confirmText }
        };
            var dialog = await DialogService.ShowAsync<ConfirmDialog>(string.Empty, parameters, _confirmOptions);
            var result = await dialog.Result;
            return result is { Canceled: false };
        }

        private async Task ExecuteAsync(Action action, string errorPrefix)
        {
            try
            {
                await Task.Run(action);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"{errorPrefix}: {ex.Message}", Severity.Error);
            }
        }
    }
}
