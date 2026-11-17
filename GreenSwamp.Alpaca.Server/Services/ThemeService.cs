using GreenSwamp.Alpaca.Server.Theme;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class ThemeService(IBrowserSettingsService browserSettings, IJSRuntime js) : IThemeService, IDisposable
{
    public const string StorageKey = GsThemeCss.StorageKey;

    // Slider moves are applied at once but written to storage only after they settle.
    private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(400);

    private bool _initialized;
    private int? _nightLevel;
    private CancellationTokenSource? _persistCts;

    public GsThemeDefinition Current { get; private set; } = GsThemeCatalog.Default;

    public int NightLevel => Current.Dimming is { } dimming ? dimming.Normalize(_nightLevel ?? dimming.Default) : 100;

    public int? ZoomPercent { get; private set; }

    public event Action? Changed;

    public event Action? DisplayChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var preference = await browserSettings.GetAsync(StorageKey, new ThemePreference());
        _nightLevel = preference.NightLevel;
        ZoomPercent = preference.ZoomPercent is { } zoom ? GsZoom.NormalizePercent(zoom) : null;
        await ApplyAsync(GsThemeCatalog.Resolve(preference.ThemeId));
        DisplayChanged?.Invoke();
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
        return await PersistAsync();
    }

    public async Task<bool> SetNightLevelAsync(int level)
    {
        if (Current.Dimming is not { } dimming)
        {
            return false;
        }

        _nightLevel = dimming.Normalize(level);
        DisplayChanged?.Invoke();
        await CallBrowserAsync("gsTheme.setLevel", NightLevel);
        SchedulePersist();
        return true;
    }

    public async Task<bool> SetZoomPercentAsync(int? percent)
    {
        ZoomPercent = percent is { } value ? GsZoom.NormalizePercent(value) : null;
        DisplayChanged?.Invoke();
        await CallBrowserAsync("gsTheme.setZoom", ZoomPercent);
        return await PersistAsync();
    }

    public async Task<bool> ResetAsync()
    {
        CancelPendingPersist();
        _nightLevel = null;
        ZoomPercent = null;
        await ApplyAsync(GsThemeCatalog.Default);
        DisplayChanged?.Invoke();
        await CallBrowserAsync("gsTheme.setZoom", ZoomPercent);
        return await browserSettings.RemoveAsync(StorageKey);
    }

    public void Dispose() => CancelPendingPersist();

    private Task<bool> PersistAsync()
    {
        CancelPendingPersist();
        return browserSettings.SetAsync(StorageKey, new ThemePreference
        {
            ThemeId = Current.Id,
            NightLevel = _nightLevel,
            ZoomPercent = ZoomPercent
        });
    }

    private void SchedulePersist()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _persistCts, cts)?.Cancel();
        _ = PersistAfterDelayAsync(cts.Token);
    }

    private async Task PersistAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(PersistDelay, token);
            await PersistAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingPersist() => Interlocked.Exchange(ref _persistCts, null)?.Cancel();

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
