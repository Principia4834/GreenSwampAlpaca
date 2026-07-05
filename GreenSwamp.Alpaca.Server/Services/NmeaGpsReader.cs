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
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Reads a single position fix from a GPS receiver over a serial port, parsing
    /// NMEA GGA (position + altitude) and RMC (position + UTC date/time) sentences.
    /// Ported from the legacy GSS GpsHardware implementation, with ASCOM/monitor-log
    /// dependencies removed and an async, cancellable API.
    /// </summary>
    /// <remarks>
    /// GGA is preferred for latitude/longitude/altitude since it carries altitude;
    /// RMC is preferred for the reported UTC timestamp since it carries a full date.
    /// Whichever sentence(s) arrive first with valid data are used - the reader does
    /// not require both to be present.
    /// </remarks>
    /// <seealso href="https://gpsd.gitlab.io/gpsd/NMEA.html">NMEA reference</seealso>
    public sealed class NmeaGpsReader(GpsConnectionParams connectionParams)
    {
        private readonly record struct GgaFix(double Latitude, double Longitude, double Altitude, DateTime? GpsUtc, DateTime PcUtc);
        private readonly record struct RmcFix(double Latitude, double Longitude, DateTime? GpsUtc, DateTime PcUtc);

        /// <summary>
        /// Opens the configured serial port and waits for a valid GGA and/or RMC sentence,
        /// up to <see cref="GpsConnectionParams.TimeoutS"/>. Fails fast with an
        /// <see cref="InvalidOperationException"/> if the configured port is not present
        /// on the system, without attempting to open it.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the read.</param>
        /// <returns>The parsed <see cref="GpsFixResult"/>.</returns>
        /// <exception cref="InvalidOperationException">The configured port does not exist.</exception>
        /// <exception cref="TimeoutException">No valid GGA/RMC sentence was received within the timeout.</exception>
        public async Task<GpsFixResult> TryReadAsync(CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionParams.Port);

            var availablePorts = SerialPortDiscovery.GetPortNames();
            if (!availablePorts.Contains(connectionParams.Port, StringComparer.OrdinalIgnoreCase))
            {
                var known = availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "none";
                throw new InvalidOperationException($"GPS port '{connectionParams.Port}' was not found. Available ports: {known}.");
            }

            return await Task.Run(() => ReadFix(cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        private GpsFixResult ReadFix(CancellationToken cancellationToken)
        {
            using var serial = new GsSerialPort(
                connectionParams.Port,
                connectionParams.BaudRate,
                TimeSpan.FromSeconds(connectionParams.TimeoutS),
                ParseHandshake(connectionParams.Handshake),
                ParseParity(connectionParams.Parity),
                ParseStopBits(connectionParams.StopBits),
                connectionParams.DataBits,
                SerialOptions.None);

            serial.Open();

            GgaFix? gga = null;
            RmcFix? rmc = null;

            var stopwatch = Stopwatch.StartNew();
            var budgetMs = connectionParams.TimeoutS * 1000;

            while (stopwatch.Elapsed.TotalMilliseconds < budgetMs && (gga is null || rmc is null))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remainingMs = budgetMs - stopwatch.Elapsed.TotalMilliseconds;
                if (remainingMs <= 0) break;
                serial.ReadTimeout = (int)Math.Max(1, remainingMs);

                var pcUtcNow = DateTime.UtcNow;
                string line;
                try
                {
                    line = serial.ReadLine();
                }
                catch (TimeoutException)
                {
                    break;
                }

                if (string.IsNullOrEmpty(line) || line.Length < 6) continue;

                var fields = line.Split(',');
                if (fields[0].Length < 6) continue;
                var code = fields[0].Substring(3, 3);

                switch (code)
                {
                    case "GGA" when gga is null && fields.Length == 15 && ValidateCheckSum(line):
                        if (TryParseGga(fields, pcUtcNow, out var ggaFix)) gga = ggaFix;
                        break;
                    case "RMC" when rmc is null && fields.Length == 13 && ValidateCheckSum(line):
                        if (TryParseRmc(fields, pcUtcNow, out var rmcFix)) rmc = rmcFix;
                        break;
                }
            }

            if (gga is null && rmc is null)
                throw new TimeoutException($"No valid GPS fix (GGA/RMC) received on '{connectionParams.Port}' within {connectionParams.TimeoutS} s.");

            double latitude, longitude, altitude;
            DateTime? gpsUtc;
            DateTime pcUtc;

            if (rmc is { } rmcValue)
            {
                latitude = gga?.Latitude ?? rmcValue.Latitude;
                longitude = gga?.Longitude ?? rmcValue.Longitude;
                altitude = gga?.Altitude ?? 0.0;
                gpsUtc = rmcValue.GpsUtc ?? gga?.GpsUtc;
                pcUtc = rmcValue.PcUtc;
            }
            else
            {
                var ggaValue = gga!.Value;
                latitude = ggaValue.Latitude;
                longitude = ggaValue.Longitude;
                altitude = ggaValue.Altitude;
                gpsUtc = ggaValue.GpsUtc;
                pcUtc = ggaValue.PcUtc;
            }

            var timeDiff = gpsUtc.HasValue ? gpsUtc.Value - pcUtc : (TimeSpan?)null;
            return new GpsFixResult(latitude, longitude, altitude, gpsUtc, pcUtc, timeDiff);
        }

        /// <summary>Parses a GGA sentence: $--GGA,hhmmss.ss,llll.ll,a,yyyyy.yy,a,x,xx,x.x,x.x,M,x.x,M,x.x,xxxx*hh</summary>
        private static bool TryParseGga(IReadOnlyList<string> fields, DateTime pcUtcNow, out GgaFix fix)
        {
            fix = default;
            try
            {
                var lat = fields[2];
                var ns = fields[3];
                var lon = fields[4];
                var ew = fields[5];
                if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(lon) || string.IsNullOrEmpty(ew))
                    return false;

                var latitude = NmeaToDecimal(lat, ns);
                var longitude = NmeaToDecimal(lon, ew);
                if (Math.Abs(latitude) <= 0.0 && Math.Abs(longitude) <= 0.0) return false;

                double.TryParse(fields[9], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var altitude);

                var timestamp = TryParseNmeaDateTime(null, fields[1], pcUtcNow, out var ts) ? ts : (DateTime?)null;

                fix = new GgaFix(latitude, longitude, altitude, timestamp, pcUtcNow);
                return true;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException)
            {
                return false;
            }
        }

        /// <summary>Parses an RMC sentence: $--RMC,hhmmss.ss,A,llll.ll,a,yyyyy.yy,a,x.x,x.x,xxxx,x.x,a,m*hh</summary>
        private static bool TryParseRmc(IReadOnlyList<string> fields, DateTime pcUtcNow, out RmcFix fix)
        {
            fix = default;
            try
            {
                var lat = fields[3];
                var ns = fields[4];
                var lon = fields[5];
                var ew = fields[6];
                if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(lon) || string.IsNullOrEmpty(ew))
                    return false;

                var latitude = NmeaToDecimal(lat, ns);
                var longitude = NmeaToDecimal(lon, ew);
                if (Math.Abs(latitude) <= 0.0 && Math.Abs(longitude) <= 0.0) return false;

                var timestamp = TryParseNmeaDateTime(fields[9], fields[1], pcUtcNow, out var ts) ? ts : (DateTime?)null;

                fix = new RmcFix(latitude, longitude, timestamp, pcUtcNow);
                return true;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Converts NMEA ddmm.mmmm (or dddmm.mmmm) plus a hemisphere letter to decimal degrees.
        /// </summary>
        private static double NmeaToDecimal(string num, string dir)
        {
            const NumberStyles style = NumberStyles.AllowDecimalPoint;
            if (!double.TryParse(num, style, CultureInfo.InvariantCulture, out var raw))
                return 0.0;

            var hemisphere = dir.Equals("S", StringComparison.OrdinalIgnoreCase) || dir.Equals("W", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
            var degrees = Math.Truncate(raw / 100);
            var minutes = raw - degrees * 100;
            return Math.Round((degrees + minutes / 60) * hemisphere, 5);
        }

        /// <summary>
        /// Combines an optional NMEA date (ddMMyy, from RMC) with a required NMEA time
        /// (hhmmss.ff.., from GGA/RMC) into a UTC <see cref="DateTime"/>. When
        /// <paramref name="date"/> is null (GGA has no date field), today's UTC date is used.
        /// </summary>
        private static bool TryParseNmeaDateTime(string? date, string time, DateTime pcUtcNow, out DateTime result)
        {
            result = default;
            if (string.IsNullOrEmpty(time)) return false;

            var utcTime = time.Contains('.') ? time : time + ".00";
            var utcParts = utcTime.Split('.');
            if (utcParts.Length != 2) return false;

            var timeFormat = utcParts[1].Length switch
            {
                1 => @"hhmmss\.f",
                2 => @"hhmmss\.ff",
                3 => @"hhmmss\.fff",
                4 => @"hhmmss\.ffff",
                _ => string.Empty
            };
            if (timeFormat.Length == 0) return false;

            var datePart = pcUtcNow.Date;
            if (!string.IsNullOrEmpty(date) &&
                DateTime.TryParseExact(date, "ddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                datePart = parsedDate;
            }

            if (!TimeSpan.TryParseExact(utcTime, timeFormat, CultureInfo.InvariantCulture, TimeSpanStyles.None, out var timePart))
                return false;

            result = datePart + timePart;
            return true;
        }

        /// <summary>Validates the trailing NMEA XOR checksum between '$' and '*'.</summary>
        private static bool ValidateCheckSum(string receivedData)
        {
            var checkChar = GetTextBetween(receivedData, "*", "\r");
            if (string.IsNullOrEmpty(checkChar)) return false;

            var strToCheck = GetTextBetween(receivedData, "$", "*");
            var checkSum = 0;
            foreach (var ch in strToCheck)
            {
                checkSum ^= Convert.ToByte(ch);
            }

            return string.Equals(checkChar, checkSum.ToString("X2"), StringComparison.Ordinal);
        }

        private static string GetTextBetween(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0) return string.Empty;
            startIndex += start.Length;
            var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
            return endIndex < 0 ? string.Empty : source.Substring(startIndex, endIndex - startIndex);
        }

        private static Handshake ParseHandshake(string value) =>
            Enum.TryParse<Handshake>(value, ignoreCase: true, out var result) ? result : Handshake.None;

        private static Parity ParseParity(string value) =>
            Enum.TryParse<Parity>(value, ignoreCase: true, out var result) ? result : Parity.None;

        private static StopBits ParseStopBits(string value) =>
            Enum.TryParse<StopBits>(value, ignoreCase: true, out var result) ? result : StopBits.One;
    }
}
