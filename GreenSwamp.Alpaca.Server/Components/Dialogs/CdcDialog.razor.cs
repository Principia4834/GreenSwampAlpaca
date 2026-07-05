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
using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components.Dialogs
{
    /// <summary>
    /// Code-behind for <see cref="CdcDialog"/>: connects to a Carte du Ciel server and
    /// supports bidirectional transfer of observatory location data.
    /// <list type="bullet">
    ///   <item><term>Copy From CdC</term><description>Reads lat/lon/alt from CdC and returns it to the caller.</description></item>
    ///   <item><term>Copy To CdC</term><description>Pushes the caller's current lat/lon/alt to CdC inline (dialog stays open).</description></item>
    /// </list>
    /// </summary>
    public partial class CdcDialog : IDisposable
    {
        [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

        /// <summary>Network address and port of the CdC server.</summary>
        [Parameter] public CdcConnectionParams ConnectionParams { get; set; } = default!;

        /// <summary>Current local latitude (decimal degrees) — used by Copy To CdC.</summary>
        [Parameter] public double LocalLatitude { get; set; }

        /// <summary>Current local longitude (decimal degrees) — used by Copy To CdC.</summary>
        [Parameter] public double LocalLongitude { get; set; }

        /// <summary>Current local altitude (metres) — used by Copy To CdC.</summary>
        [Parameter] public double LocalAltitude { get; set; }

        private readonly CancellationTokenSource _cts = new();
        private DialogState _state = DialogState.Idle;
        private CdcLocationResult? _cdcResult;
        private string? _error;
        private string? _sendError;

        /// <summary>Connects to the CdC server and reads the current observatory location.</summary>
        private async Task TestConnectionAsync()
        {
            _state = DialogState.Connecting;
            _error = null;
            _cdcResult = null;
            _sendError = null;
            StateHasChanged();

            try
            {
                using var server = new CdcServer(ConnectionParams.Address, ConnectionParams.Port);
                _cdcResult = await server.GetObsAsync(_cts.Token);
                _state = DialogState.Ready;
            }
            catch (OperationCanceledException)
            {
                _state = DialogState.Idle;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _state = DialogState.Error;
            }
        }

        /// <summary>Returns the CdC location to the caller and closes the dialog.</summary>
        private void CopyFromCdc() => MudDialog.Close(DialogResult.Ok(_cdcResult));

        /// <summary>Pushes the current local location to the CdC server. The dialog remains open.</summary>
        private async Task CopyToCdcAsync()
        {
            _state = DialogState.Sending;
            _sendError = null;
            StateHasChanged();

            try
            {
                using var server = new CdcServer(ConnectionParams.Address, ConnectionParams.Port);
                await server.SetObsAsync(LocalLatitude, LocalLongitude, LocalAltitude, _cts.Token);
                _state = DialogState.Sent;
            }
            catch (OperationCanceledException)
            {
                _state = DialogState.Ready;
            }
            catch (Exception ex)
            {
                _sendError = ex.Message;
                _state = DialogState.SendError;
            }
        }

        private void Cancel()
        {
            _cts.Cancel();
            MudDialog.Cancel();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
