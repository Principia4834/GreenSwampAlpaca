using GreenSwamp.Alpaca.Server.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components.Dialogs
{
    /// <summary>
    /// Code-behind for <see cref="GpsFixDialog"/>: reads a single position fix over the
    /// supplied serial connection parameters and lets the user review and apply the result.
    /// </summary>
    public partial class GpsFixDialog : IDisposable
    {
        [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

        /// <summary>Serial connection parameters for the GPS receiver to read from.</summary>
        [Parameter] public GpsConnectionParams ConnectionParams { get; set; } = default!;

        private readonly CancellationTokenSource _cts = new();
        private bool _reading = true;
        private string? _error;
        private GpsFixResult? _result;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var reader = new NmeaGpsReader(ConnectionParams);
                _result = await reader.TryReadAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
            finally
            {
                _reading = false;
            }
        }

        private void Apply() => MudDialog.Close(DialogResult.Ok(_result));

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
