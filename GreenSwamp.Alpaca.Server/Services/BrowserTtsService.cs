using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Wraps the browser Web Speech API for text-to-speech via JS interop.
    /// </summary>
    public sealed class BrowserTtsService(IJSRuntime js) : IAsyncDisposable
    {
        private IJSObjectReference? _module;

        private async ValueTask<IJSObjectReference> GetModuleAsync()
            => _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import", "./js/tts.js");

        /// <summary>
        /// Speaks <paramref name="text"/> using the specified voice and volume.
        /// If <paramref name="voiceName"/> is empty, the browser default voice is used.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        /// <param name="voiceName">Exact Web Speech API voice name, or empty string for default.</param>
        /// <param name="volumePct">Volume as a percentage (0–100).</param>
        /// <param name="rate">Speech rate (0.1–10).</param>
        public async Task SpeakAsync(string text, string voiceName = "", int volumePct = 100, float rate = 0.8f)
        {
            try
            {
                var module = await GetModuleAsync();
                await module.InvokeVoidAsync("speak", text, voiceName, volumePct, rate);
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

        /// <summary>
        /// Returns the list of browser voices, optionally filtered by BCP-47 language tag (e.g. "en-US").
        /// Returns an empty list when the Speech API is unavailable.
        /// </summary>
        public async Task<List<BrowserVoice>> GetVoicesAsync(string languageTag = "")
        {
            try
            {
                var module = await GetModuleAsync();
                var json = await module.InvokeAsync<string>("getVoices", languageTag);
                return System.Text.Json.JsonSerializer.Deserialize<List<BrowserVoice>>(json)
                    ?? [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
        }

        /// <summary>Returns the browser's preferred BCP-47 language tag (e.g. "en-US").</summary>
        public async Task<string> GetLanguageAsync()
        {
            try
            {
                var module = await GetModuleAsync();
                return await module.InvokeAsync<string>("getLanguage") ?? string.Empty;
            }
            catch (TaskCanceledException)
            {
                return string.Empty;
            }
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

    /// <summary>Represents a single Web Speech API voice entry.</summary>
    public sealed record BrowserVoice(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")]  string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("lang")]  string Lang,
        [property: System.Text.Json.Serialization.JsonPropertyName("default")] bool Default);
}
