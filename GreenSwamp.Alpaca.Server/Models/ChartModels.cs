/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace GreenSwamp.Alpaca.Server.Models
{
    /// <summary>
    /// A single timestamped data point for chart series.
    /// Timestamp is UTC milliseconds since epoch for ApexCharts time axis compatibility.
    /// </summary>
    public record ChartPointDto(long TimestampMs, double Value);

    /// <summary>
    /// A single pulse data point with axis and rejection metadata.
    /// Axis 0 = RA, Axis 1 = Dec.
    /// </summary>
    public record PulsePointDto(long TimestampMs, double Duration, double Rate, int Axis, bool Rejected);

    /// <summary>
    /// Identifies which chart type a connection wants to receive data for.
    /// </summary>
    public enum ChartType
    {
        RaDec,
        Pulse
    }

    /// <summary>
    /// Scale options for the RA/Dec position chart.
    /// </summary>
    public enum RaDecScale
    {
        Steps,
        Degrees,
        ArcSeconds
    }

    /// <summary>
    /// Scale options for the pulse guide chart.
    /// </summary>
    public enum PulseScale
    {
        Milliseconds,
        ArcSeconds,
        Steps
    }

    /// <summary>
    /// Carries a snapshot of a historical data batch sent to new chart window connections.
    /// </summary>
    public record HistoricalDataDto(
        ChartType ChartType,
        IReadOnlyList<ChartPointDto> AxisOnePoints,
        IReadOnlyList<ChartPointDto> AxisTwoPoints);

    /// <summary>
    /// Historical pulse data snapshot sent to newly connected pulse chart windows.
    /// </summary>
    public record HistoricalPulseDto(
        IReadOnlyList<PulsePointDto> RaPoints,
        IReadOnlyList<PulsePointDto> DecPoints);
}
