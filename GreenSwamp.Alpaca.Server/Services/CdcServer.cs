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
using ASCOM.Tools;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GreenSwamp.Alpaca.Server.Services
{
    public sealed class CdcServer : IDisposable
    {
        private TcpClient _tcpClient;
        private const string _crlf = "\r\n";

        private IPAddress Ip { get; }
        private int Port { get; }
        private bool HasData { get; set; }
        private string Data { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        public CdcServer(string ip, int port)
        {
            IPAddress.TryParse(ip, out var tmpIp);
            Ip = tmpIp;
            Port = port;
        }

        /// <summary>
        /// Connection to CDC server
        /// </summary>
        private void Connect()
        {
            _tcpClient = new TcpClient();
            _tcpClient?.Connect(Ip, Port);
            if (!_tcpClient.Connected)
            {
                throw new InvalidOperationException($"CdC server at '{Ip}:{Port}' was not found.");
            }

        }

        /// <summary>
        /// Closes connection to CDC server
        /// </summary>
        private void Close()
        {
            if (_tcpClient == null) return;
            if (_tcpClient.Connected) _tcpClient.Close();
            _tcpClient.Close();
            _tcpClient = null;
        }

        /// <summary>
        /// Sends and Receives data from CDC server
        /// each call opens and closes any connection
        /// </summary>
        /// <param name="command"></param>
        private void SendCommand(string command)
        {
            if (command == null) return;
            Connect();
            Data = null;
            HasData = false;
            var enc = new ASCIIEncoding();
            var ba = enc.GetBytes(command + _crlf);
            Stream stm = _tcpClient.GetStream();
            stm.WriteTimeout = 3000;
            stm.ReadTimeout = 3000;
            stm.Write(ba, 0, ba.Length);
            var data = new byte[100];
            var byteCount = stm.Read(data, 0, 100);
            if (byteCount > 0)
            {
                Data = Encoding.ASCII.GetString(data);
                HasData = true;
            }
            Close();
        }

        /// <summary>
        /// Gets observatory information from CDC server
        /// </summary>
        /// <returns></returns>
        internal double[] GetObs()
        {
            SendCommand("GETOBS");
            var darray = new double[3];
            if (HasData)
            {
                if (Data.Contains("LAT:"))
                {
                    var lat = Strings.GetTxtBetween(Data, "LAT:", "LON:");
                    if (string.IsNullOrEmpty(lat))
                    {
                        darray[1] = 0;
                    }
                    else
                    {
                        darray[0] = Utilities.DMSToDegrees(lat.Trim());
                    }
                }

                if (Data.Contains("LON:"))
                {
                    var lon = Strings.GetTxtBetween(Data, "LON:", "ALT:");
                    if (string.IsNullOrEmpty(lon))
                    {
                        darray[1] = 0;
                    }
                    else
                    {
                        darray[1] = Utilities.DMSToDegrees(lon.Trim()) * -1;
                    }
                }

                if (!Data.Contains("ALT:")) return darray;
                var alt = Strings.GetTxtBetween(Data, "ALT:", "OBS");
                if (string.IsNullOrEmpty(alt))
                {
                    darray[2] = 0;
                }
                else
                {
                    alt = alt.Replace("M", string.Empty);
                    alt = alt.Replace("m", string.Empty);
                    var parsed = double.TryParse(alt.Trim(), out var tmpalt);
                    if (parsed)
                    {
                        darray[2] = tmpalt;
                    }
                    else
                    {
                        darray[2] = 0;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"CdC server did not return observatory data.");
            }
            return darray;
        }

        /// <summary>
        /// Updates CDC server observatory information
        /// </summary>
        /// <param name="lat"></param>
        /// <param name="lon"></param>
        /// <param name="alt"></param>
        internal void SetObs(double lat, double lon, double alt)
        {
            //LAT:+00d00m00sLON:+000d00m00s ALT:000mOBS:name
            var latplus = lat > 0.0 ? "+" : "-";
            var lonplus = lon > 0.0 ? "-" : "+";
            lat = Math.Abs(lat);
            lon = Math.Abs(lon);
            var latstr = Utilities.DegreesToDMS(lat, "d", "m", "s", 3);
            var lonstr = Utilities.DegreesToDMS(lon, "d", "m", "s", 3);
            var altstr = $"{alt}m";
            const string name = "GSServer";
            var command = $"SETOBS LAT:{latplus}{latstr}LON:{lonplus}{lonstr}ALT:{altstr}OBS:{name}";
            SendCommand(command);
        }

        /// <summary>
        /// Asynchronously retrieves the observatory location from the CdC server.
        /// Wraps the synchronous <see cref="GetObs"/> call on a thread-pool thread.
        /// </summary>
        internal async Task<CdcLocationResult> GetObsAsync(CancellationToken ct = default)
        {
            var data = await Task.Run(() => GetObs(), ct).ConfigureAwait(false);
            return new CdcLocationResult(data[0], data[1], data[2]);
        }

        /// <summary>
        /// Asynchronously pushes an observatory location to the CdC server.
        /// Wraps the synchronous <see cref="SetObs"/> call on a thread-pool thread.
        /// </summary>
        internal async Task SetObsAsync(double lat, double lon, double alt, CancellationToken ct = default)
        {
            await Task.Run(() => SetObs(lat, lon, alt), ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Dispose(true);
            // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            // dispose managed resources
            _tcpClient.Dispose();
            // free native resources
        }
    }
}
