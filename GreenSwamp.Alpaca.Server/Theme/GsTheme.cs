using System.Collections.Concurrent;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// GreenSwamp Alpaca — MudBlazor theme.
/// Typography and layout live here; the palette is derived by <see cref="GsThemeBuilder"/>
/// from a single Theme Colour and the background surfaces.
/// </summary>
public static class GsTheme
{
    // Apex cannot read CSS variables in every option, so chart chrome uses literal values from the same surfaces as the palette.
    public static string ChartBackground(GsThemeDefinition theme) => theme.Surfaces.Surface;
    public static string ChartText(GsThemeDefinition theme) => $"rgba({theme.Surfaces.TextRgb},0.87)";
    public static string ChartGrid(GsThemeDefinition theme) => $"rgba({theme.Surfaces.TextRgb},0.12)";

    private static readonly ConcurrentDictionary<string, MudTheme> Cache = new();

    /// <summary>Default theme (Dark Green). Used until the stored preference has been read.</summary>
    public static MudTheme Instance => Get(GsThemeCatalog.Default);

    /// <summary>Returns the cached MudTheme for a theme definition.</summary>
    public static MudTheme Get(GsThemeDefinition theme) => Cache.GetOrAdd(theme.Id, _ => Create(theme));

    private static MudTheme Create(GsThemeDefinition theme) => new()
    {
        PaletteLight = theme.IsDark ? new PaletteLight() : GsThemeBuilder.BuildLightPalette(theme),
        PaletteDark = theme.IsDark ? GsThemeBuilder.BuildDarkPalette(theme) : new PaletteDark(),

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Roboto", "Helvetica Neue", "Arial", "sans-serif"],
                FontSize = "0.875rem",
                FontWeight = "400",
                LineHeight = "1.43",
                LetterSpacing = "0.01em",
            },
            H1 = new H1Typography { FontSize = "1.75rem", FontWeight = "300", LineHeight = "1.2" },
            H2 = new H2Typography { FontSize = "1.5rem", FontWeight = "400", LineHeight = "1.3" },
            H3 = new H3Typography { FontSize = "1.25rem", FontWeight = "500", LineHeight = "1.3" },
            H4 = new H4Typography { FontSize = "1.125rem", FontWeight = "500", LineHeight = "1.4" },
            H5 = new H5Typography { FontSize = "1rem", FontWeight = "500", LineHeight = "1.4" },
            H6 = new H6Typography { FontSize = "0.9375rem", FontWeight = "500", LineHeight = "1.4" },
            Subtitle1 = new Subtitle1Typography { FontSize = "1rem", FontWeight = "500", LineHeight = "1.4" },
            Subtitle2 = new Subtitle2Typography { FontSize = "0.875rem", FontWeight = "500", LineHeight = "1.4" },
            Body1 = new Body1Typography { FontSize = "0.875rem", LineHeight = "1.5" },
            Body2 = new Body2Typography { FontSize = "0.8125rem", LineHeight = "1.43" },
            Button = new ButtonTypography
            {
                FontSize = "0.8125rem",
                FontWeight = "500",
                TextTransform = "uppercase",
                LetterSpacing = "0.06em",
            },
            Caption = new CaptionTypography { FontSize = "0.75rem", LineHeight = "1.4" },
            Overline = new OverlineTypography
            {
                FontSize = "0.75rem",
                FontWeight = "500",
                LineHeight = "2.66",
                LetterSpacing = "0.08333em",
                TextTransform = "uppercase",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft  = "15.625rem",
            DrawerWidthRight = "15.625rem",
            AppbarHeight     = "3.5rem",
        }
    };
}
