using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// Background surfaces and text colours for a theme. Line and action tokens are alpha tints of <c>TextRgb</c>.
/// </summary>
public sealed record GsSurfaces(
    bool IsDark,
    string Background,
    string Surface,
    string BackgroundGray,
    string Appbar,
    string TextRgb,
    string TextPrimary,
    string TextSecondary,
    string TextDisabled,
    string LinesInputs,
    string? Neutral = null)
{
    public static readonly GsSurfaces Dark = new(
        IsDark: true,
        Background: "#121212",
        Surface: "#1e1e1e",
        BackgroundGray: "#2a2a2a",
        Appbar: "#0d1117",
        TextRgb: "255,255,255",
        TextPrimary: "rgba(255,255,255,0.87)",
        TextSecondary: "rgba(255,255,255,0.60)",
        TextDisabled: "rgba(255,255,255,0.38)",
        LinesInputs: "rgba(255,255,255,0.30)");

    public static readonly GsSurfaces Light = new(
        IsDark: false,
        Background: "#f5f5f5",
        Surface: "#ffffff",
        BackgroundGray: "#eeeeee",
        Appbar: "#ffffff",
        TextRgb: "0,0,0",
        TextPrimary: "rgba(0,0,0,0.87)",
        TextSecondary: "rgba(0,0,0,0.60)",
        TextDisabled: "rgba(0,0,0,0.38)",
        LinesInputs: "rgba(0,0,0,0.38)");

    // Red on black only. Text is explicit because an alpha tint of red on black falls below 3:1 (60% gives 2.6:1).
    // #f20000 is about 4.6:1 and #c80000 about 3.4:1 on the lightest surface (#140000).
    public static readonly GsSurfaces NightRed = new(
        IsDark: true,
        Background: "#000000",
        Surface: "#0a0000",
        BackgroundGray: "#140000",
        Appbar: "#000000",
        TextRgb: "255,0,0",
        TextPrimary: "#f20000",
        TextSecondary: "#c80000",
        TextDisabled: "#7a0000",
        LinesInputs: "rgba(255,0,0,0.80)",
        Neutral: "#c80000");
}

/// <summary>
/// Derives a complete MudBlazor palette from a single Theme Colour and a set of surfaces.
/// <list type="bullet">
/// <item><description>Control (Primary): muted Theme Colour, least prominent tone that still reaches 3:1 on every surface. Outlines, icons, filled controls. Never used for text.</description></item>
/// <item><description>Accent (Tertiary): Theme Colour moved only as far as needed to reach 4.5:1 on every surface. Brand mark, selected tab, clock.</description></item>
/// <item><description>Neutral (Secondary and text tokens): follow the surfaces' text colour.</description></item>
/// <item><description>Status colours keep their hue and are only adjusted if they fall below 3:1.</description></item>
/// </list>
/// </summary>
public static class GsThemeBuilder
{
    public const string GreenThemeColour = "#4caf50";

    private const double AccentContrast = 4.5;
    private const double StatusContrast = 3.0;
    private const double LightnessStep = 0.005;

    public static PaletteDark BuildDarkPalette(GsThemeDefinition theme)
    {
        var palette = new PaletteDark();
        Fill(palette, theme);
        return palette;
    }

    public static PaletteLight BuildLightPalette(GsThemeDefinition theme)
    {
        var palette = new PaletteLight();
        Fill(palette, theme);
        return palette;
    }

    private static void Fill(Palette p, GsThemeDefinition theme)
    {
        var s = theme.Surfaces;
        var surfaces = new[]
        {
            Rgb.Parse(s.Background), Rgb.Parse(s.Surface), Rgb.Parse(s.BackgroundGray), Rgb.Parse(s.Appbar)
        };

        var seed = Rgb.Parse(theme.ThemeColour);
        var (hue, saturation, _) = seed.ToHsl();

        var control = LeastProminent(hue, saturation * theme.ControlSaturation, surfaces, theme.ControlContrast, s.IsDark);
        var accent = EnsureContrast(seed, surfaces, AccentContrast, s.IsDark);
        var neutral = s.Neutral is null
            ? Blend(Rgb.ParseRgbList(s.TextRgb), Rgb.Parse(s.Surface), 0.60)
            : Rgb.Parse(s.Neutral);

        // Single-hue themes: lightening stops at the fully saturated colour so hover shades never turn pink or white.
        Rgb Lighten(Rgb c, double delta)
        {
            if (!theme.Monochrome)
            {
                return c.Shift(delta);
            }

            var (h, sat, l) = c.ToHsl();
            return Rgb.FromHsl(h, sat, Math.Min(l + delta, 0.5));
        }

        string OnFill(Rgb fill) => theme.Monochrome ? "#000000" : ContrastText(fill);

        // -- Surfaces -----------------------------------------------------
        p.Background = s.Background;
        p.Surface = s.Surface;
        p.BackgroundGray = s.BackgroundGray;
        p.AppbarBackground = s.Appbar;
        p.DrawerBackground = s.Appbar;

        // -- Control (Primary) --------------------------------------------
        p.Primary = control.ToHex();
        p.PrimaryLighten = Lighten(control, 0.12).ToHex();
        p.PrimaryDarken = control.Shift(-0.08).ToHex();
        p.PrimaryContrastText = OnFill(control);

        // -- Accent (Tertiary) --------------------------------------------
        p.Tertiary = accent.ToHex();
        p.TertiaryLighten = Lighten(accent, 0.15).ToHex();
        p.TertiaryDarken = accent.Shift(-0.12).ToHex();
        p.TertiaryContrastText = OnFill(accent);

        // -- Neutral (Secondary) ------------------------------------------
        p.Secondary = neutral.ToHex();
        p.SecondaryLighten = Lighten(neutral, 0.10).ToHex();
        p.SecondaryDarken = neutral.Shift(-0.10).ToHex();
        p.SecondaryContrastText = OnFill(neutral);

        // -- Text ---------------------------------------------------------
        p.TextPrimary = s.TextPrimary;
        p.TextSecondary = s.TextSecondary;
        p.TextDisabled = s.TextDisabled;
        p.DrawerText = s.TextPrimary;
        p.DrawerIcon = s.TextSecondary;
        p.AppbarText = s.TextPrimary;

        // -- Lines / Dividers ---------------------------------------------
        p.Divider = $"rgba({s.TextRgb},0.12)";
        p.DividerLight = $"rgba({s.TextRgb},0.06)";
        p.TableLines = $"rgba({s.TextRgb},0.12)";
        p.LinesDefault = $"rgba({s.TextRgb},0.12)";
        p.LinesInputs = s.LinesInputs;

        // -- Overlays -----------------------------------------------------
        p.OverlayDark = "rgba(0,0,0,0.5)";
        p.OverlayLight = "rgba(0,0,0,0.3)";

        // -- Status (not derived from the Theme Colour) -------------------
        var status = theme.Status ?? GsStatusColours.Standard;
        var success = EnsureContrast(Rgb.Parse(status.Success), surfaces, StatusContrast, s.IsDark);
        var warning = EnsureContrast(Rgb.Parse(status.Warning), surfaces, StatusContrast, s.IsDark);
        var error = EnsureContrast(Rgb.Parse(status.Error), surfaces, StatusContrast, s.IsDark);
        var info = EnsureContrast(Rgb.Parse(status.Info), surfaces, StatusContrast, s.IsDark);
        p.Success = success.ToHex();
        p.SuccessLighten = Lighten(success, 0.10).ToHex();
        p.SuccessDarken = success.Shift(-0.08).ToHex();
        p.Warning = warning.ToHex();
        p.WarningLighten = Lighten(warning, 0.10).ToHex();
        p.WarningDarken = warning.Shift(-0.08).ToHex();
        p.Error = error.ToHex();
        p.ErrorLighten = Lighten(error, 0.10).ToHex();
        p.ErrorDarken = error.Shift(-0.08).ToHex();
        p.Info = info.ToHex();
        p.InfoLighten = Lighten(info, 0.10).ToHex();
        p.InfoDarken = info.Shift(-0.08).ToHex();

        // -- Action states ------------------------------------------------
        p.ActionDefault = s.TextSecondary;
        p.ActionDisabled = $"rgba({s.TextRgb},0.26)";
        p.ActionDisabledBackground = $"rgba({s.TextRgb},0.12)";

        if (!theme.Monochrome)
        {
            return;
        }

        // Single-hue theme: replace every remaining MudBlazor default that is not in the hue.
        var gray = Rgb.Parse(s.BackgroundGray);
        p.White = s.TextPrimary;
        p.Black = s.Background;
        p.Dark = s.BackgroundGray;
        p.DarkContrastText = s.TextPrimary;
        p.DarkLighten = Lighten(gray, 0.05).ToHex();
        p.DarkDarken = gray.Shift(-0.02).ToHex();
        p.SuccessContrastText = "#000000";
        p.WarningContrastText = "#000000";
        p.ErrorContrastText = "#000000";
        p.InfoContrastText = "#000000";
        p.GrayDefault = Rgb.FromHsl(hue, 1, 0.40).ToHex();
        p.GrayLight = Rgb.FromHsl(hue, 1, 0.45).ToHex();
        p.GrayLighter = Rgb.FromHsl(hue, 1, 0.50).ToHex();
        p.GrayDark = Rgb.FromHsl(hue, 1, 0.30).ToHex();
        p.GrayDarker = Rgb.FromHsl(hue, 1, 0.20).ToHex();
        p.Skeleton = $"rgba({s.TextRgb},0.12)";
        p.TableStriped = $"rgba({s.TextRgb},0.06)";
        p.TableHover = $"rgba({s.TextRgb},0.10)";
    }

    private static double MinContrast(Rgb c, Rgb[] surfaces)
    {
        var min = double.MaxValue;
        foreach (var surface in surfaces)
        {
            min = Math.Min(min, Rgb.Contrast(c, surface));
        }
        return min;
    }

    /// <summary>Moves lightness away from the surfaces, only if needed, until the target contrast is met.</summary>
    private static Rgb EnsureContrast(Rgb colour, Rgb[] surfaces, double target, bool darkSurfaces)
    {
        var (h, sat, l) = colour.ToHsl();
        var current = colour;
        while (MinContrast(current, surfaces) < target && l > 0 && l < 1)
        {
            l = Math.Clamp(l + (darkSurfaces ? LightnessStep : -LightnessStep), 0, 1);
            current = Rgb.FromHsl(h, sat, l);
        }
        return current;
    }

    /// <summary>Finds the tone closest to the surfaces that still meets the target contrast.</summary>
    private static Rgb LeastProminent(double hue, double saturation, Rgb[] surfaces, double target, bool darkSurfaces)
    {
        var best = darkSurfaces ? 1.0 : 0.0;
        for (var step = 0; step <= 200; step++)
        {
            var l = darkSurfaces ? 1.0 - step * LightnessStep : step * LightnessStep;
            if (MinContrast(Rgb.FromHsl(hue, saturation, l), surfaces) < target)
            {
                break;
            }
            best = l;
        }
        return Rgb.FromHsl(hue, saturation, best);
    }

    private static string ContrastText(Rgb fill) =>
        Rgb.Contrast(fill, Rgb.White) >= Rgb.Contrast(fill, Rgb.Black) ? "#ffffff" : "#000000";

    private static Rgb Blend(Rgb foreground, Rgb background, double alpha) =>
        new(background.R + (foreground.R - background.R) * alpha,
            background.G + (foreground.G - background.G) * alpha,
            background.B + (foreground.B - background.B) * alpha);

    private readonly record struct Rgb(double R, double G, double B)
    {
        public static readonly Rgb White = new(1, 1, 1);
        public static readonly Rgb Black = new(0, 0, 0);

        public static Rgb Parse(string hex)
        {
            var h = hex.TrimStart('#');
            return new Rgb(
                Convert.ToInt32(h[..2], 16) / 255.0,
                Convert.ToInt32(h[2..4], 16) / 255.0,
                Convert.ToInt32(h[4..6], 16) / 255.0);
        }

        public static Rgb ParseRgbList(string list)
        {
            var parts = list.Split(',');
            return new Rgb(
                int.Parse(parts[0].Trim()) / 255.0,
                int.Parse(parts[1].Trim()) / 255.0,
                int.Parse(parts[2].Trim()) / 255.0);
        }

        public string ToHex() =>
            $"#{(int)Math.Round(R * 255):x2}{(int)Math.Round(G * 255):x2}{(int)Math.Round(B * 255):x2}";

        public double Luminance
        {
            get
            {
                static double Linear(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
                return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
            }
        }

        public static double Contrast(Rgb a, Rgb b)
        {
            var la = a.Luminance;
            var lb = b.Luminance;
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public Rgb Shift(double deltaLightness)
        {
            var (h, s, l) = ToHsl();
            return FromHsl(h, s, Math.Clamp(l + deltaLightness, 0, 1));
        }

        public (double H, double S, double L) ToHsl()
        {
            var max = Math.Max(R, Math.Max(G, B));
            var min = Math.Min(R, Math.Min(G, B));
            var l = (max + min) / 2;
            if (max == min)
            {
                return (0, 0, l);
            }

            var d = max - min;
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h;
            if (max == R)
            {
                h = (G - B) / d + (G < B ? 6 : 0);
            }
            else if (max == G)
            {
                h = (B - R) / d + 2;
            }
            else
            {
                h = (R - G) / d + 4;
            }
            return (h / 6, s, l);
        }

        public static Rgb FromHsl(double h, double s, double l)
        {
            if (s == 0)
            {
                return new Rgb(l, l, l);
            }

            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            return new Rgb(Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));
        }

        private static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
