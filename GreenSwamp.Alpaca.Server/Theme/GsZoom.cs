using System.Globalization;

namespace GreenSwamp.Alpaca.Server.Theme;

/// <summary>
/// Single source of truth for the UI zoom setting. Zoom scales the root font-size,
/// which every rem-based size in <see cref="GsTheme"/> follows.
/// </summary>
public static class GsZoom
{
    public const double DefaultScale = 1.0;
    public const double MinScale = 0.75;
    public const double MaxScale = 1.5;
    public const int MinZoomPercent = 75;
    public const int MaxZoomPercent = 150;
    public const int StepPercent = 5;

    /// <summary>Root font-size at 100% zoom. Keep in sync with the :root fallback in site.css.</summary>
    public const double BaseFontSizePx = 16.0;

    /// <summary>Zoom percentages offered in the UI, in <see cref="StepPercent"/> steps.</summary>
    public static IReadOnlyList<int> ZoomPercentOptions { get; } =
        Enumerable.Range(0, ((MaxZoomPercent - MinZoomPercent) / StepPercent) + 1)
            .Select(index => MinZoomPercent + (index * StepPercent))
            .ToArray();

    public static double NormalizeScale(double fontScale)
        => Math.Clamp(fontScale, MinScale, MaxScale);

    /// <summary>Clamps to the supported range and snaps to the nearest step.</summary>
    public static int NormalizePercent(int percent)
    {
        var clamped = Math.Clamp(percent, MinZoomPercent, MaxZoomPercent);
        var snapped = (int)Math.Round((clamped - MinZoomPercent) / (double)StepPercent) * StepPercent + MinZoomPercent;

        return Math.Clamp(snapped, MinZoomPercent, MaxZoomPercent);
    }

    public static double GetRootFontSizePx(double fontScale)
        => BaseFontSizePx * NormalizeScale(fontScale);

    /// <summary>Builds the root font-size rule. Culture-invariant so the CSS stays valid in comma-decimal cultures.</summary>
    public static string BuildRootStyle(double fontScale)
        => string.Create(
            CultureInfo.InvariantCulture,
            $":root {{ font-size: {GetRootFontSizePx(fontScale):0.##}px; }}");
}
