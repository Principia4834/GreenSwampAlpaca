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
    /// Network connection parameters for a Carte du Ciel (CdC) server.
    /// </summary>
    /// <param name="Address">IP address or hostname of the CdC server (e.g. 127.0.0.1).</param>
    /// <param name="Port">TCP port the CdC server is listening on (default 3292).</param>
    public sealed record CdcConnectionParams(string Address, int Port);

    /// <summary>
    /// Observatory location data returned by a Carte du Ciel server via the GETOBS command.
    /// </summary>
    /// <param name="Latitude">Latitude in decimal degrees (positive = North).</param>
    /// <param name="Longitude">Longitude in decimal degrees (positive = East).</param>
    /// <param name="Altitude">Altitude above sea level in metres.</param>
    public sealed record CdcLocationResult(double Latitude, double Longitude, double Altitude);
}
