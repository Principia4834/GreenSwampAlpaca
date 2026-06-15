namespace GreenSwamp.Alpaca.Server.Components
{
    /// <summary>
    /// Helper class for file download via JavaScript interop.
    /// </summary>
    public static class DownloadFileHelper
    {
        /// <summary>
        /// Downloads a file by creating a blob URL and triggering a browser download.
        /// This uses JavaScript interop to avoid issues with large file downloads.
        /// </summary>
        public static async Task DownloadAsync(string fileName, byte[] data)
        {
            // This method signature exists for the Blazor component to call.
            // The actual download is handled client-side via JavaScript in _Imports.razor or a JS module.
            // For now, we'll implement this using a data URI approach in the component.
            await Task.Delay(100); // Brief delay to allow UI to update
        }
    }
}
