using GreenSwamp.Alpaca.Server.Theme;

namespace GreenSwamp.Alpaca.Server.Services;

/// <summary>
/// Holds the active theme for one browser circuit and persists the choice in browser local storage.
/// </summary>
public interface IThemeService
{
    /// <summary>The active theme. Dark Green until <see cref="InitializeAsync"/> has read the stored preference.</summary>
    GsThemeDefinition Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes. May be raised on any thread; marshal to the renderer before use.</summary>
    event Action? Changed;

    /// <summary>Brightness percentage of the active theme; 100 when the theme cannot be dimmed.</summary>
    int NightLevel { get; }

    /// <summary>Zoom percentage chosen in this browser, or null to use the server default.</summary>
    int? ZoomPercent { get; }

    /// <summary>Raised after <see cref="NightLevel"/> or <see cref="ZoomPercent"/> changes. Does not imply a theme change.</summary>
    event Action? DisplayChanged;

    /// <summary>Reads the stored preference once. Call from <c>OnAfterRenderAsync(firstRender)</c>; storage is unavailable during prerendering.</summary>
    Task InitializeAsync();

    /// <summary>Applies a theme immediately, then stores it. Returns false when the id is unknown or storage failed.</summary>
    Task<bool> SetThemeAsync(string themeId);

    /// <summary>Applies a brightness level at once and stores it shortly afterwards. Returns false when the active theme cannot be dimmed.</summary>
    Task<bool> SetNightLevelAsync(int level);

    /// <summary>Applies a zoom for this browser, or null for the server default, then stores it. Returns false when storage failed.</summary>
    Task<bool> SetZoomPercentAsync(int? percent);

    /// <summary>Returns to the default theme and removes the stored preference.</summary>
    Task<bool> ResetAsync();
}
