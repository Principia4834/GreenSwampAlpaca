using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services
{
    public sealed class BrowserTtsService(IJSRuntime js) : IAsyncDisposable
    {
        private IJSObjectReference? _module;

        private async ValueTask<IJSObjectReference> GetModuleAsync()
            => _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import", "./js/tts.js");

        public async Task SpeakAsync(string text, float rate = 0.8f, float volume = 1f)
        {
            try
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("speak", text, rate, volume);
            }
            catch (TaskCanceledException)
            {
                // Circuit disconnected.
            }
        }

        public async Task StopAsync()
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("stop");
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is not null)
            {
                try
                {
                    await _module.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                    // Circuit already disconnected; JS resources are released by the browser.
                }
            }
        }
    }
}