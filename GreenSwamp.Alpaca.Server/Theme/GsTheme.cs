using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// GreenSwamp Alpaca — MudBlazor theme.
/// Single source of truth for all GS design tokens; replaces the Bootstrap
/// variable bridge that previously lived in site.css.
/// </summary>
public static class GsTheme
{
    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            // -- Surfaces -------------------------------------------------
            Background       = "#121212",          // --gs-bg-app
            Surface          = "#1e1e1e",          // --gs-bg-paper
            BackgroundGray   = "#2a2a2a",          // --gs-bg-elevated
            AppbarBackground = "#0d1117",          // --gs-bg-sidebar
            DrawerBackground = "#0d1117",

            // -- Accent / Primary -----------------------------------------
            Primary          = "#4caf50",          // --gs-accent-500
            PrimaryLighten   = "#81c784",          // --gs-accent-300
            PrimaryDarken    = "#388e3c",          // --gs-accent-700

            // -- Text -----------------------------------------------------
            TextPrimary      = "rgba(255,255,255,0.87)",
            TextSecondary    = "rgba(255,255,255,0.60)",
            TextDisabled     = "rgba(255,255,255,0.38)",
            DrawerText       = "rgba(255,255,255,0.87)",
            DrawerIcon       = "rgba(255,255,255,0.60)",
            AppbarText       = "rgba(255,255,255,0.87)",

            // -- Lines / Dividers -----------------------------------------
            Divider          = "rgba(255,255,255,0.12)",
            DividerLight     = "rgba(255,255,255,0.06)",
            TableLines       = "rgba(255,255,255,0.12)",
            LinesDefault     = "rgba(255,255,255,0.12)",
            LinesInputs      = "rgba(255,255,255,0.30)",

            // -- Overlays -------------------------------------------------
            OverlayDark      = "rgba(0,0,0,0.5)",
            OverlayLight     = "rgba(0,0,0,0.3)",

            // -- Status ---------------------------------------------------
            Success          = "#66bb6a",          // --gs-success
            Warning          = "#ffa726",          // --gs-warning
            Error            = "#f44336",          // --gs-error
            Info             = "#42a5f5",          // --gs-info

            // -- Action states --------------------------------------------
            ActionDefault    = "rgba(255,255,255,0.60)",
            ActionDisabled   = "rgba(255,255,255,0.26)",
            ActionDisabledBackground = "rgba(255,255,255,0.12)",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Roboto", "Helvetica Neue", "Arial", "sans-serif"],
                FontSize   = "0.875rem",
                FontWeight = "400",
                LineHeight = "1.43",
                LetterSpacing = "0.01em",
            },
            H1 = new H1Typography { FontSize = "1.5rem",    FontWeight = "300", LineHeight = "1.2" },
            H2 = new H2Typography { FontSize = "1.125rem",  FontWeight = "400", LineHeight = "1.3" },
            H3 = new H3Typography { FontSize = "1rem",      FontWeight = "500", LineHeight = "1.3" },
            H4 = new H4Typography { FontSize = "0.9375rem", FontWeight = "500", LineHeight = "1.4" },
            H5 = new H5Typography { FontSize = "0.875rem",  FontWeight = "500", LineHeight = "1.4" },
            H6 = new H6Typography { FontSize = "0.8125rem", FontWeight = "500", LineHeight = "1.4" },
            Body1 = new Body1Typography { FontSize = "0.875rem",  LineHeight = "1.5" },
            Body2 = new Body2Typography { FontSize = "0.8125rem", LineHeight = "1.43" },
            Button = new ButtonTypography
            {
                FontSize      = "0.8125rem",
                FontWeight    = "500",
                TextTransform = "uppercase",
                LetterSpacing = "0.06em",
            },
            Caption = new CaptionTypography { FontSize = "0.75rem", LineHeight = "1.4" },
        },

        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft  = "250px",
            DrawerWidthRight = "250px",
            AppbarHeight     = "3.5rem",
        }
    };
}
