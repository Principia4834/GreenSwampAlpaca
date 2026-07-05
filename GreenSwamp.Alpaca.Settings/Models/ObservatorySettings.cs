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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// A single named observatory site with its physical properties.
    /// </summary>
    public class ObservatoryInfo
    {
        /// <summary>Stable GUID used as a dictionary key and tree-node identity.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>User-defined display name shown in the Settings Explorer tree.</summary>
        [Required, MaxLength(100)]
        public string Name { get; set; } = "Default Observatory";

        [Range(-90, 90)]
        public double Latitude { get; set; } = 51.476852;

        [Range(-180, 180)]
        public double Longitude { get; set; } = 0.0;

        [Range(-500, 9000)]
        public double Elevation { get; set; } = 10.0;

        public TimeSpan UTCOffset { get; set; } = TimeSpan.Zero;

        /// <summary>Serial port name for a GPS receiver at this site (e.g. COM4 or /dev/ttyUSB0). Empty when not configured.</summary>
        public string GpsPort { get; set; } = string.Empty;

        /// <summary>Baud rate for the GPS receiver serial port.</summary>
        public int GpsBaudRate { get; set; } = 9600;

        /// <summary>Parity for the GPS receiver serial port (None, Odd, Even, Mark, Space).</summary>
        public string GpsParity { get; set; } = "None";

        /// <summary>Stop bits for the GPS receiver serial port (One, Two, OnePointFive, None).</summary>
        public string GpsStopBits { get; set; } = "One";

        /// <summary>Data bits for the GPS receiver serial port (5–8).</summary>
        public int GpsDataBits { get; set; } = 8;

        /// <summary>Read timeout in milliseconds for the GPS receiver serial port.</summary>
        public int GpsTimeout { get; set; } = 2000;

        /// <summary>Handshake for the GPS receiver serial port (None, XOnXOff, RequestToSend, RequestToSendXOnXOff).</summary>
        public string GpsHandshake { get; set; } = "None";

        /// <summary>IP address of the CDC server for this observatory site. Defaults to localhost (127.0.0.1).</summary>
        public string CdcAddress { get; set; } = "127.0.0.1";

        /// <summary>Port number of the CDC server for this observatory site. Defaults to 3292.</summary>
        public int CdcPort { get; set; } = 3292;
    }

    /// <summary>
    /// Collection of observatory sites — content of observatory.settings.json.
    /// Replaces the previous single-observatory model (no backwards compatibility).
    /// </summary>
    public class ObservatorySettings
    {
        public List<ObservatoryInfo> Observatories { get; set; } = [];
    }
}
