namespace GreenSwamp.Alpaca.Server.Services;

/// <summary>
/// Display preferences chosen in this browser, persisted in browser local storage under <c>gs.theme.v1</c>.
/// Only ids and plain numbers are stored so the colours can evolve without invalidating saved preferences.
/// </summary>
public sealed class ThemePreference
{
    public int Version { get; set; } = 1;

    public string? ThemeId { get; set; }

    /// <summary>Brightness percentage for themes that support dimming; null means the theme default.</summary>
    public int? NightLevel { get; set; }

    /// <summary>Zoom percentage for this browser; null means the server default.</summary>
    public int? ZoomPercent { get; set; }
}
