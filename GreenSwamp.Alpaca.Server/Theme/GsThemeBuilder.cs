using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// Background surfaces and neutral text for a theme. Only the dark preset exists today.
/// </summary>
public sealed record GsSurfaces(
    bool IsDark,
    string Background,
    string Surface,
    string BackgroundGray,
    string Appbar,
    string TextRgb)
{
    public static readonly GsSurfaces Dark = new(
        IsDark: true,
        Background: "#121212",
        Surface: "#1e1e1e",
        BackgroundGray: "#2a2a2a",
        Appbar: "#0d1117",
        TextRgb: "255,255,255");
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

    private const double ControlSaturationFactor = 0.65;
    private const double ControlContrast = 3.0;
    private const double AccentContrast = 4.5;
    private const double StatusContrast = 3.0;
    private const double LightnessStep = 0.005;

    public static PaletteDark BuildDarkPalette(string themeColour)
    {
        var palette = new PaletteDark();
        Fill(palette, themeColour, GsSurfaces.Dark);
        return palette;
    }

    private static void Fill(Palette p, string themeColour, GsSurfaces s)
    {
        var surfaces = new[]
        {
            Rgb.Parse(s.Background), Rgb.Parse(s.Surface), Rgb.Parse(s.BackgroundGray), Rgb.Parse(s.Appbar)
        };

        var seed = Rgb.Parse(themeColour);
        var (hue, saturation, _) = seed.ToHsl();

        var control = LeastProminent(hue, saturation * ControlSaturationFactor, surfaces, ControlContrast, s.IsDark);
        var accent = EnsureContrast(seed, surfaces, AccentContrast, s.IsDark);
        var neutral = Blend(Rgb.ParseRgbList(s.TextRgb), Rgb.Parse(s.Surface), 0.60);

        // -- Surfaces -----------------------------------------------------
        p.Background = s.Background;
        p.Surface = s.Surface;
        p.BackgroundGray = s.BackgroundGray;
        p.AppbarBackground = s.Appbar;
        p.DrawerBackground = s.Appbar;

        // -- Control (Primary) --------------------------------------------
        p.Primary = control.ToHex();
        p.PrimaryLighten = control.Shift(0.12).ToHex();
        p.PrimaryDarken = control.Shift(-0.08).ToHex();
        p.PrimaryContrastText = ContrastText(control);

        // -- Accent (Tertiary) --------------------------------------------
        p.Tertiary = accent.ToHex();
        p.TertiaryLighten = accent.Shift(0.15).ToHex();
        p.TertiaryDarken = accent.Shift(-0.12).ToHex();
        p.TertiaryContrastText = ContrastText(accent);

        // -- Neutral (Secondary) ------------------------------------------
        p.Secondary = neutral.ToHex();
        p.SecondaryLighten = neutral.Shift(0.10).ToHex();
        p.SecondaryDarken = neutral.Shift(-0.10).ToHex();
        p.SecondaryContrastText = ContrastText(neutral);

        // -- Text ---------------------------------------------------------
        p.TextPrimary = $"rgba({s.TextRgb},0.87)";
        p.TextSecondary = $"rgba({s.TextRgb},0.60)";
        p.TextDisabled = $"rgba({s.TextRgb},0.38)";
        p.DrawerText = $"rgba({s.TextRgb},0.87)";
        p.DrawerIcon = $"rgba({s.TextRgb},0.60)";
        p.AppbarText = $"rgba({s.TextRgb},0.87)";

        // -- Lines / Dividers ---------------------------------------------
        p.Divider = $"rgba({s.TextRgb},0.12)";
        p.DividerLight = $"rgba({s.TextRgb},0.06)";
        p.TableLines = $"rgba({s.TextRgb},0.12)";
        p.LinesDefault = $"rgba({s.TextRgb},0.12)";
        p.LinesInputs = $"rgba({s.TextRgb},0.30)";

        // -- Overlays -----------------------------------------------------
        p.OverlayDark = "rgba(0,0,0,0.5)";
        p.OverlayLight = "rgba(0,0,0,0.3)";

        // -- Status (not derived from the Theme Colour) -------------------
        p.Success = EnsureContrast(Rgb.Parse("#66bb6a"), surfaces, StatusContrast, s.IsDark).ToHex();
        p.Warning = EnsureContrast(Rgb.Parse("#ffa726"), surfaces, StatusContrast, s.IsDark).ToHex();
        p.Error = EnsureContrast(Rgb.Parse("#f44336"), surfaces, StatusContrast, s.IsDark).ToHex();
        p.Info = EnsureContrast(Rgb.Parse("#42a5f5"), surfaces, StatusContrast, s.IsDark).ToHex();

        // -- Action states ------------------------------------------------
        p.ActionDefault = $"rgba({s.TextRgb},0.60)";
        p.ActionDisabled = $"rgba({s.TextRgb},0.26)";
        p.ActionDisabledBackground = $"rgba({s.TextRgb},0.12)";
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
