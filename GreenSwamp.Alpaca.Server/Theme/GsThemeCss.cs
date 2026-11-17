using System.Text;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// Head content that makes the stored theme visible before the Blazor circuit is connected.
/// Both strings are generated from <see cref="GsThemeCatalog"/> so the catalog stays the single source of truth.
/// </summary>
public static class GsThemeCss
{
    /// <summary>Local storage key that <c>ThemeService</c> writes; the boot script reads the same key.</summary>
    public const string StorageKey = "gs.theme.v1";

    /// <summary>Set on the root element once the real theme is on screen.</summary>
    public const string ReadyClass = "gs-theme-ready";

    /// <summary>Root element attribute that selects the active theme.</summary>
    public const string ThemeAttribute = "data-gs-theme";

    /// <summary>
    /// Inline script for the head. Reads the stored theme id, sets the root attribute and exposes
    /// <c>window.gsTheme</c> for live changes. A non-default theme keeps the body hidden until
    /// <c>gsTheme.ready()</c> runs, or for three seconds at most.
    /// </summary>
    public static string BootScript { get; } = BuildBootScript();

    /// <summary>
    /// Inline style for the head: page background, text colour and native colour scheme for each theme,
    /// plus the rule that hides the body until the theme is ready.
    /// </summary>
    public static string Critical { get; } = BuildCritical();

    private static string BuildBootScript()
    {
        var ids = string.Join(",", GsThemeCatalog.All.Select(t => $"\"{t.Id}\""));
        return $$"""
            (function () {
                var ids = [{{ids}}], def = "{{GsThemeCatalog.Default.Id}}", root = document.documentElement, id = def;
                try {
                    var raw = window.localStorage.getItem("{{StorageKey}}");
                    if (raw) {
                        var o = JSON.parse(raw), v = o && (o.ThemeId || o.themeId);
                        if (ids.indexOf(v) >= 0) id = v;
                    }
                } catch (e) { }
                root.setAttribute("{{ThemeAttribute}}", id);
                if (id === def) root.classList.add("{{ReadyClass}}");
                else setTimeout(function () { root.classList.add("{{ReadyClass}}"); }, 3000);
                window.gsTheme = {
                    apply: function (v) { if (ids.indexOf(v) >= 0) root.setAttribute("{{ThemeAttribute}}", v); },
                    ready: function () {
                        requestAnimationFrame(function () {
                            requestAnimationFrame(function () { root.classList.add("{{ReadyClass}}"); });
                        });
                    }
                };
            })();
            """;
    }

    private static string BuildCritical()
    {
        var css = new StringBuilder();
        css.Append($"html:not(.{ReadyClass}) body{{visibility:hidden}}\n");

        foreach (var theme in GsThemeCatalog.All)
        {
            var s = theme.Surfaces;
            var selector = $"html[{ThemeAttribute}=\"{theme.Id}\"]";
            css.Append($"{selector}{{color-scheme:{(theme.IsDark ? "dark" : "light")};background-color:{s.Background};color:{s.TextPrimary}");

            if (theme.Monochrome)
            {
                // Hero photo, brand mark and hero text follow the single hue.
                css.Append($";--gs-brand-green:rgb({s.TextRgb});--gs-brand-green-rgb:{s.TextRgb};--gs-hero-text-rgb:{s.TextRgb}");
            }

            css.Append("}\n");

            if (theme.Monochrome)
            {
                // MudBlazor's default scrollbar thumb is a fixed light grey; keep it, the selection and the track in the hue.
                css.Append($"{selector} ::-webkit-scrollbar-thumb{{background-color:{s.TextSecondary}}}\n");
                css.Append($"{selector} ::-webkit-scrollbar-track{{background-color:{s.Background}}}\n");
                css.Append($"{selector} ::selection{{background-color:rgb({s.TextRgb});color:{s.Background}}}\n");
            }
        }

        return css.ToString();
    }
}
