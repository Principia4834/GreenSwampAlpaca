using System.Globalization;
using System.Web;
using Microsoft.AspNetCore.Components;

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
        DialDisplayUnits.Hours => FormatHMS(Angle / 15.0),
        _                      => FormatDMS(Angle),
    };

    private string FormatHMS(double hours)
    {
        if (double.IsNaN(hours) || double.IsInfinity(hours)) return "N/A";
        var sign = hours < 0 ? "-" : "+";
        hours = Math.Abs(hours);
        var h = (int)hours;
        var m = (int)((hours - h) * 60);
        var s = ((hours - h) * 60 - m) * 60;
        return $"{sign}{h:00}h {m:00}m {s:00.00}s";
    }

    private string FormatDMS(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
        var sign = degrees < 0 ? "-" : "+";
        degrees = Math.Abs(degrees);
        var d = (int)degrees;
        var m = (int)((degrees - d) * 60);
        var s = ((degrees - d) * 60 - m) * 60;
        return $"{sign}{d:00}° {m:00}' {s:00.00}\"";
    }

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
