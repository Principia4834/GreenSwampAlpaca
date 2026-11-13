using Microsoft.AspNetCore.Localization;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class BrowserCultureService(IJSRuntime js) : IAsyncDisposable
{
    private static readonly string JsVersion = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;

    private async ValueTask<IJSObjectReference> GetModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", $"./js/culturePreferences.js?v={JsVersion}");

    public async Task<bool> HasCulturePreferenceAsync()
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("hasCulturePreference", CookieRequestCultureProvider.DefaultCookieName);
    }

    public async Task<BrowserCulturePreference?> GetCulturePreferenceAsync()
    {
        var module = await GetModuleAsync();
        var values = await module.InvokeAsync<string[]?>("getCulturePreference", CookieRequestCultureProvider.DefaultCookieName);
        return values is { Length: 2 }
            ? new BrowserCulturePreference(values[0], values[1])
            : null;
    }

    public async Task MarkInitializationAttemptedAsync()
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("markInitializationAttempted");
    }

    public sealed record BrowserCulturePreference(string RegionalFormat, string DisplayLanguage);

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
                // The browser releases its module reference when the circuit ends.
            }
        }
    }
}