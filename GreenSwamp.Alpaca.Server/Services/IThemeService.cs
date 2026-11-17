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

    /// <summary>Reads the stored preference once. Call from <c>OnAfterRenderAsync(firstRender)</c>; storage is unavailable during prerendering.</summary>
    Task InitializeAsync();

    /// <summary>Applies a theme immediately, then stores it. Returns false when the id is unknown or storage failed.</summary>
    Task<bool> SetThemeAsync(string themeId);

    /// <summary>Returns to the default theme and removes the stored preference.</summary>
    Task<bool> ResetAsync();
}
