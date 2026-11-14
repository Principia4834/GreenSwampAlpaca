using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Components.Dialogs;
using Microsoft.Extensions.Localization;
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
        /// <param name="loc">The localizer used for dialog and notification text.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public static async Task ShowShutdownDialogAsync(
            IDialogService dialogService,
            ISnackbar snackbar,
            IHostApplicationLifetime? lifetime,
            IStringLocalizer loc)
        {
            var contextText = loc["Shutdown_Question"].Value;
            var connectedCount = MountRegistry.ConnectedCount;

            if (connectedCount > 0)
            {
                contextText = connectedCount switch
                {
                    1 => loc["Shutdown_OneMount"].Value + " " + contextText,
                    _ => loc["Shutdown_ManyMounts", connectedCount].Value + " " + contextText
                };
            }

            var confirmed = await ConfirmAsync(dialogService, contextText, loc["Nav_Shutdown"].Value, loc["Common_Confirm"].Value);
            if (!confirmed) return;

            try
            {
                lifetime?.StopApplication();
            }
            catch (Exception ex)
            {
                snackbar.Add(loc["Shutdown_Failed", ex.Message].Value, Severity.Error);
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
            string confirmText,
            string title)
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

            var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters, options);
            var result = await dialog.Result;

            return result is { Canceled: false };
        }
    }
}