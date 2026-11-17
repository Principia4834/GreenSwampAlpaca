namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// Status colours (Success, Warning, Error, Info) before the contrast guard is applied.
/// </summary>
public sealed record GsStatusColours(string Success, string Warning, string Error, string Info)
{
    public static readonly GsStatusColours Standard = new("#66bb6a", "#ffa726", "#f44336", "#42a5f5");

    // Night theme: red family only. States are told apart by brightness and glyph, never by hue.
    public static readonly GsStatusColours Red = new("#ff0000", "#e00000", "#ff0000", "#cc0000");
}

/// <summary>
/// One selectable theme: a Theme Colour, its surfaces and the knobs the palette builder needs.
/// </summary>
/// <param name="Id">Stable identifier persisted in browser storage.</param>
/// <param name="NameKey">Localisation key for the display name.</param>
/// <param name="ThemeColour">Seed colour from which Primary (control) and Tertiary (accent) are derived.</param>
/// <param name="Surfaces">Background surfaces and text colours.</param>
/// <param name="ControlSaturation">Fraction of the seed saturation kept for the control colour.</param>
/// <param name="ControlContrast">Minimum contrast of the control colour against every surface.</param>
/// <param name="Monochrome">True for single-hue themes: contrast text is black and no palette slot may leave the hue.</param>
/// <param name="Status">Status colours; null uses <see cref="GsStatusColours.Standard"/>.</param>
public sealed record GsThemeDefinition(
    string Id,
    string NameKey,
    string ThemeColour,
    GsSurfaces Surfaces,
    double ControlSaturation = 0.65,
    double ControlContrast = 3.0,
    bool Monochrome = false,
    GsStatusColours? Status = null)
{
    public bool IsDark => Surfaces.IsDark;
}

/// <summary>
/// The themes offered to the user. The first entry is the default for a browser with no stored preference.
/// </summary>
public static class GsThemeCatalog
{
    public static readonly GsThemeDefinition DarkGreen = new(
        Id: "dark-green",
        NameKey: "Theme_DarkGreen",
        ThemeColour: GsThemeBuilder.GreenThemeColour,
        Surfaces: GsSurfaces.Dark);

    public static readonly GsThemeDefinition LightGreen = new(
        Id: "light-green",
        NameKey: "Theme_LightGreen",
        ThemeColour: GsThemeBuilder.GreenThemeColour,
        Surfaces: GsSurfaces.Light);

    // Astronomical red-light theme. Pure red hue throughout; control colour reaches 4.5:1 so black text on it passes.
    public static readonly GsThemeDefinition NightRed = new(
        Id: "night-red",
        NameKey: "Theme_NightRed",
        ThemeColour: "#ff0000",
        Surfaces: GsSurfaces.NightRed,
        ControlSaturation: 1.0,
        ControlContrast: 4.6,
        Monochrome: true,
        Status: GsStatusColours.Red);

    public static IReadOnlyList<GsThemeDefinition> All { get; } = [DarkGreen, LightGreen, NightRed];

    public static GsThemeDefinition Default => DarkGreen;

    /// <summary>Returns the theme with the given id, or the default when the id is missing or unknown.</summary>
    public static GsThemeDefinition Resolve(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal)) ?? Default;
}
