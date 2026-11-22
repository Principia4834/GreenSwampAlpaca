using GreenSwamp.Alpaca.Server.Theme;
using Microsoft.JSInterop;

namespace GreenSwamp.Alpaca.Server.Services;

public sealed class ThemeService(IBrowserSettingsService browserSettings, IJSRuntime js) : IThemeService, IDisposable
{
    public const string StorageKey = GsThemeCss.StorageKey;

    // Slider moves are applied at once but written to storage only after they settle.
    private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(400);

    private bool _initialized;
    private bool _systemDark;
    private int? _nightLevel;
    private CancellationTokenSource? _persistCts;
    private DotNetObjectReference<ThemeService>? _systemObserver;

    public GsThemeDefinition Current { get; private set; } = GsThemeCatalog.Default;

    public string Mode { get; private set; } = GsThemeCatalog.Dark;

    public string Colour => GsThemeCatalog.ColourOf(Current);

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
        var savedTheme = GsThemeCatalog.Resolve(preference.ThemeId);
        Mode = preference.Mode is GsThemeCatalog.Light or GsThemeCatalog.Dark or GsThemeCatalog.System
            ? preference.Mode : GsThemeCatalog.Dark;
        _nightLevel = preference.NightLevel;
        ZoomPercent = preference.ZoomPercent is { } zoom ? GsZoom.NormalizePercent(zoom) : null;
        _systemObserver = DotNetObjectReference.Create(this);
        _systemDark = await js.InvokeAsync<bool>("gsTheme.watchSystem", _systemObserver);
        await ApplyAsync(GsThemeCatalog.ForColour(GsThemeCatalog.ColourOf(savedTheme), IsDark()));
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

        Mode = theme.IsDark ? GsThemeCatalog.Dark : GsThemeCatalog.Light;
        await ApplyAsync(theme);
        DisplayChanged?.Invoke();
        return await PersistAsync();
    }

    public async Task<bool> SetModeAsync(string mode)
    {
        if (mode is not (GsThemeCatalog.Light or GsThemeCatalog.Dark or GsThemeCatalog.System))
        {
            return false;
        }

        Mode = mode;
        await ApplyAsync(GsThemeCatalog.ForColour(Colour, IsDark()));
        DisplayChanged?.Invoke();
        return await PersistAsync();
    }

    public async Task<bool> SetColourAsync(string colour)
    {
        if (!GsThemeCatalog.Colours.ContainsKey(colour))
        {
            return false;
        }

        await ApplyAsync(GsThemeCatalog.ForColour(colour, IsDark()));
        return await PersistAsync();
    }

    [JSInvokable]
    public async Task SystemDarkChanged(bool isDark)
    {
        _systemDark = isDark;
        if (Mode == GsThemeCatalog.System)
        {
            await ApplyAsync(GsThemeCatalog.ForColour(Colour, isDark));
        }
    }

    private bool IsDark() => Mode == GsThemeCatalog.Dark || (Mode == GsThemeCatalog.System && _systemDark);

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
        Mode = GsThemeCatalog.Dark;
        await ApplyAsync(GsThemeCatalog.Default);
        DisplayChanged?.Invoke();
        await CallBrowserAsync("gsTheme.setZoom", ZoomPercent);
        return await browserSettings.RemoveAsync(StorageKey);
    }

    public void Dispose()
    {
        CancelPendingPersist();
        if (_systemObserver is not null)
        {
            _ = CallBrowserAsync("gsTheme.unwatchSystem");
            _systemObserver.Dispose();
        }
    }

    private Task<bool> PersistAsync()
    {
        CancelPendingPersist();
        return browserSettings.SetAsync(StorageKey, new ThemePreference
        {
            ThemeId = Current.Id,
            Mode = Mode,
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
