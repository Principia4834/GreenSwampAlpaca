namespace GreenSwamp.Alpaca.Server.Services;

/// <summary>
/// The theme chosen in this browser, persisted in browser local storage under <c>gs.theme.v1</c>.
/// Only the id is stored so the colours can evolve without invalidating saved preferences.
/// </summary>
public sealed class ThemePreference
{
    public int Version { get; set; } = 1;

    public string? ThemeId { get; set; }
}
