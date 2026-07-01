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

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Serial connection parameters for a GPS receiver, sourced from the caller's
    /// working copy of either an observatory site or a telescope device's settings.
    /// </summary>
    /// <param name="Port">Serial port name (e.g. COM4 or /dev/ttyUSB0).</param>
    /// <param name="BaudRate">Baud rate for the GPS receiver serial port.</param>
    /// <param name="Parity">Parity: None, Odd, Even, Mark, Space.</param>
    /// <param name="StopBits">Stop bits: None, One, OnePointFive, Two.</param>
    /// <param name="DataBits">Data bits (5-8).</param>
    /// <param name="TimeoutMs">Read timeout in milliseconds.</param>
    /// <param name="Handshake">Handshake: None, XOnXOff, RequestToSend, RequestToSendXOnXOff.</param>
    public sealed record GpsConnectionParams(
        string Port,
        int BaudRate,
        string Parity,
        string StopBits,
        int DataBits,
        int TimeoutMs,
        string Handshake);

    /// <summary>
    /// Result of a successful GPS fix parsed from NMEA GGA/RMC sentences.
    /// </summary>
    /// <param name="Latitude">Latitude in decimal degrees.</param>
    /// <param name="Longitude">Longitude in decimal degrees.</param>
    /// <param name="Altitude">Altitude in metres above sea level (from GGA; 0 if only RMC was received).</param>
    /// <param name="GpsUtc">UTC date/time reported by the GPS receiver, if available.</param>
    /// <param name="PcUtc">Local machine UTC date/time captured when the fix was read.</param>
    /// <param name="TimeDiff">Difference between <see cref="GpsUtc"/> and <see cref="PcUtc"/>, if <see cref="GpsUtc"/> is available.</param>
    public sealed record GpsFixResult(
        double Latitude,
        double Longitude,
        double Altitude,
        DateTime? GpsUtc,
        DateTime PcUtc,
        TimeSpan? TimeDiff);
}
