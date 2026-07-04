using System.Globalization;
using System.Web;
using Microsoft.AspNetCore.Components;
using GreenSwamp.Alpaca.Server.Helpers;

namespace GreenSwamp.Alpaca.Server.Components;

/// <summary>Controls what unit labels and readout the dial displays.</summary>
public enum DialDisplayUnits
{
    /// <summary>0–360° display. Cardinal labels: 0°, 90°, 180°, 270°.</summary>
    Degrees,
    /// <summary>0–24 h display. Cardinal labels: 0h, 6h, 12h, 18h. Angle parameter is still in degrees.</summary>
    Hours,
}

public partial class AxisDial
{
    /// <summary>Selects degree or hour display. Defaults to <see cref="DialDisplayUnits.Degrees"/>.</summary>
    [Parameter] public DialDisplayUnits DisplayUnits { get; set; } = DialDisplayUnits.Degrees;

    // Cardinal labels change based on the selected display mode.
    private (double Deg, string Lbl)[] CardinalLabels => DisplayUnits switch
    {
        DialDisplayUnits.Hours => [(0, "0h"), (90, "6h"), (180, "12h"), (270, "18h")],
        _                      => [(0, "0°"), (90, "90°"), (180, "180°"), (270, "270°")],
    };

    // Readout beneath the dial: degrees show the raw angle; hours convert back to hours.
    private string ReadoutText => DisplayUnits switch
    {
        DialDisplayUnits.Hours => CoordinateFormatter.FormatHMS(Angle / 15.0),
        _                      => CoordinateFormatter.FormatDMS(Angle),
    };

    // Blazor's Razor parser treats <text> as a directive keyword, so SVG text
    // elements with attributes must be emitted as raw markup from a code-behind method.
    private static MarkupString SvgText(double x, double y, string content,
        string fontSize = "12", string fontWeight = "normal",
        string fontFamily = "sans-serif", string fill = "currentColor")
    {
        var html = "<text"
                 + $" x=\"{F(x)}\" y=\"{F(y)}\""
                 + " text-anchor=\"middle\" dominant-baseline=\"middle\""
                 + $" font-size=\"{fontSize}\" font-weight=\"{fontWeight}\" font-family=\"{fontFamily}\""
                 + $" fill=\"{fill}\">"
                 + HttpUtility.HtmlEncode(content)
                 + "</" + "text>";
        return new MarkupString(html);
    }

    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
