using GreenSwamp.Alpaca.Server.Theme;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class ThemeService(IBrowserSettingsService browserSettings) : IThemeService
{
    public const string StorageKey = "gs.theme.v1";

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
        Apply(GsThemeCatalog.Resolve(preference.ThemeId));
    }

    public async Task<bool> SetThemeAsync(string themeId)
    {
        var theme = GsThemeCatalog.All.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.Ordinal));
        if (theme is null)
        {
            return false;
        }

        Apply(theme);
        return await browserSettings.SetAsync(StorageKey, new ThemePreference { ThemeId = theme.Id });
    }

    public async Task<bool> ResetAsync()
    {
        Apply(GsThemeCatalog.Default);
        return await browserSettings.RemoveAsync(StorageKey);
    }

    private void Apply(GsThemeDefinition theme)
    {
        if (ReferenceEquals(theme, Current))
        {
            return;
        }

        Current = theme;
        Changed?.Invoke();
    }
}
