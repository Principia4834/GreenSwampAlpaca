using GreenSwamp.Alpaca.Server.Theme;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class ThemeService(IBrowserSettingsService browserSettings, IJSRuntime js) : IThemeService
{
    public const string StorageKey = GsThemeCss.StorageKey;

    private bool _initialized;

    public GsThemeDefinition Current { get; private set; } = GsThemeCatalog.Default;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var preference = await browserSettings.GetAsync(StorageKey, new ThemePreference());
        await ApplyAsync(GsThemeCatalog.Resolve(preference.ThemeId));
        await CallBrowserAsync("gsTheme.ready");
    }

    public async Task<bool> SetThemeAsync(string themeId)
    {
        var theme = GsThemeCatalog.All.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.Ordinal));
        if (theme is null)
        {
            return false;
        }

        await ApplyAsync(theme);
        return await browserSettings.SetAsync(StorageKey, new ThemePreference { ThemeId = theme.Id });
    }

    public async Task<bool> ResetAsync()
    {
        await ApplyAsync(GsThemeCatalog.Default);
        return await browserSettings.RemoveAsync(StorageKey);
    }

    private async Task ApplyAsync(GsThemeDefinition theme)
    {
        if (ReferenceEquals(theme, Current))
        {
            return;
        }

        Current = theme;
        Changed?.Invoke();
        await CallBrowserAsync("gsTheme.apply", theme.Id);
    }

    // The root-element attribute drives first paint on the next page load; a failed call must not block the theme change.
    private async Task CallBrowserAsync(string identifier, params object[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
        }
    }
}
