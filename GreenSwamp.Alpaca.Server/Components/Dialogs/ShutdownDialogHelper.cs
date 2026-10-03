using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components.Dialogs
{
    /// <summary>
    /// Helper class for showing a shutdown confirmation dialog and handling the shutdown process.
    /// </summary>
    public static class ShutdownDialogHelper
    {
        /// <summary>
        /// Shows a shutdown confirmation dialog and attempts to shut down the application if confirmed.
        /// </summary>
        /// <param name="dialogService">The dialog service used to show the confirmation dialog.</param>
        /// <param name="snackbar">The snackbar service used to show notifications.</param>
        /// <param name="lifetime">The application lifetime used to stop the application.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public static async Task ShowShutdownDialogAsync(
            IDialogService dialogService,
            ISnackbar snackbar,
            IHostApplicationLifetime? lifetime)
        {
            var contextText = "Are you sure you want to shut down the system?";
            var connectedCount = MountRegistry.ConnectedCount;

            if (connectedCount > 0)
            {
                contextText = connectedCount switch
                {
                    1 => "There is 1 mount connected. " + contextText,
                    _ => $"There are {connectedCount} mounts connected. " + contextText
                };
            }

            var confirmed = await ConfirmAsync(dialogService, contextText, "Shutdown");
            if (!confirmed) return;

            try
            {
                lifetime?.StopApplication();
            }
            catch (Exception ex)
            {
                snackbar.Add($"Shutdown failed: {ex.Message}", Severity.Error);
            }
        }

        /// <summary>
        /// Shows a confirmation dialog with the specified message and confirm button text.
        /// </summary>
        /// <param name="dialogService">The dialog service used to show the confirmation dialog.</param>
        /// <param name="message">The message to display in the confirmation dialog.</param>
        /// <param name="confirmText">The text for the confirm button.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains true if the user confirmed; otherwise, false.</returns>
        private static async Task<bool> ConfirmAsync(
            IDialogService dialogService,
            string message,
            string confirmText)
        {
            var parameters = new DialogParameters<ConfirmDialog>
            {
                { x => x.ContentText, message },
                { x => x.ConfirmText, confirmText }
            };

            var options = new DialogOptions
            {
                CloseButton = false,
                CloseOnEscapeKey = true,
                MaxWidth = MaxWidth.ExtraSmall,
                FullWidth = false
            };

            var dialog = await dialogService.ShowAsync<ConfirmDialog>(string.Empty, parameters, options);
            var result = await dialog.Result;

            return result is { Canceled: false };
        }
    }
}