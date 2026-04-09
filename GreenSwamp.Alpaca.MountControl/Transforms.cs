/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

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

using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;

namespace GreenSwamp.Alpaca.Server.MountControl
{
    public static class Transforms
    {
        #region Transform

        private static readonly Transform xForm = new Transform();

        /// <summary>
        /// Convert RA and DEC from "from type" to "to type" using Transform
        /// </summary>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// <param name="latitude"></param>
        /// <param name="longitude"></param>
        /// <param name="elevation"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// /// <param name="log"></param>
        /// <returns></returns>
        public static Vector ConvertRaDec(double rightAscension, double declination, double latitude, double longitude, double elevation, string from, string to, bool log = false)
        {
            xForm.SiteElevation = elevation;
            xForm.SiteLatitude = latitude;
            xForm.SiteLongitude = longitude;
            xForm.Refraction = MountRegistry.GetInstance(0)?.Settings?.Refraction ?? default;
            switch (from.ToLower())
            {
                case "j2000":
                    xForm.SetJ2000(rightAscension, declination);
                    break;
                case "topocentric":
                    xForm.SetTopocentric(rightAscension, declination);
                    break;
                case "apparent":
                    xForm.SetApparent(rightAscension, declination);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var r = new Vector(0, 0);

            switch (to.ToLower())
            {
                case "j2000":
                    r.X = xForm.RAJ2000;
                    r.Y = xForm.DecJ2000;
                    break;
                case "topocentric":
                    r.X = xForm.RATopocentric;
                    r.Y = xForm.DECTopocentric;
                    break;
                case "apparent":
                    r.X = xForm.RAApparent;
                    r.Y = xForm.DECApparent;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (log)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"lat:{latitude}|long:{longitude}|Ref:{MountRegistry.GetInstance(0)?.Settings?.Refraction ?? default}|Ele:{elevation}|ra/dec:{rightAscension},{declination}|{r.X},{r.Y}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return r;
        }

        /// <summary>
        /// Converts RA and DEC to the required EquatorialCoordinateType
        /// Used for all RA and DEC coordinates coming into the system 
        /// </summary>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// /// <param name="log"></param>
        /// <returns></returns>
        public static Vector CoordTypeToInternal(double rightAscension, double declination, bool log = false, SkySettings? settings = null)
        {
            var s = settings ?? MountRegistry.GetInstance(0)?.Settings;
            //internal is already to-po so return it
            if (s?.EquatorialCoordinateType == EquatorialCoordinateType.Topocentric) return new Vector(rightAscension, declination);

            xForm.SiteElevation = s?.Elevation ?? default;
            xForm.SiteLatitude = s?.Latitude ?? default;
            xForm.SiteLongitude = s?.Longitude ?? default;
            xForm.Refraction = s?.Refraction ?? default;
            xForm.SiteTemperature = s?.Temperature ?? default;
            switch (s?.EquatorialCoordinateType ?? default)
            {
                case EquatorialCoordinateType.J2000:
                    xForm.SetJ2000(rightAscension, declination);
                    break;
                case EquatorialCoordinateType.Topocentric:
                    return new Vector(rightAscension, declination);
                case EquatorialCoordinateType.Other:
                    xForm.SetApparent(rightAscension, declination);
                    break;
                case EquatorialCoordinateType.J2050:
                    xForm.SetJ2000(rightAscension, declination);
                    break;
                case EquatorialCoordinateType.B1950:
                    xForm.SetJ2000(rightAscension, declination);
                    break;
                default:
                    xForm.SetTopocentric(rightAscension, declination);
                    break;
            }
            if (log)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"lat:{s?.Latitude ?? default}|long:{s?.Longitude ?? default}|Ref:{s?.Refraction ?? default}|Ele:{s?.Elevation ?? default}|ra/dec:{rightAscension},{declination}|{xForm.RATopocentric},{xForm.DECTopocentric}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }

            return new Vector(xForm.RATopocentric, xForm.DECTopocentric);
        }

        /// <summary>
        /// Converts internal stored coords to the stored EquatorialCoordinateType
        /// Used for all RA and DEC coordinates going out of the system  
        /// </summary>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// /// <param name="log"></param>
        /// <returns></returns>
        public static Vector InternalToCoordType(double rightAscension, double declination, bool log = false, SkySettings? settings = null)
        {
            var s = settings ?? MountRegistry.GetInstance(0)?.Settings;
            var radec = new Vector();
            //internal is already to-po so return it
            if (s?.EquatorialCoordinateType == EquatorialCoordinateType.Topocentric) return new Vector(rightAscension, declination);

            xForm.SiteElevation = s?.Elevation ?? default;
            xForm.SiteLatitude = s?.Latitude ?? default;
            xForm.SiteLongitude = s?.Longitude ?? default;
            xForm.Refraction = s?.Refraction ?? default;
            xForm.SiteTemperature = s?.Temperature ?? default;
            xForm.SetTopocentric(rightAscension, declination);
            switch (s?.EquatorialCoordinateType ?? default)
            {
                case EquatorialCoordinateType.J2000:
                    radec.X = xForm.RAJ2000;
                    radec.Y = xForm.DecJ2000;
                    break;
                case EquatorialCoordinateType.Topocentric:
                    radec.X = rightAscension;
                    radec.Y = declination;
                    break;
                case EquatorialCoordinateType.Other:
                    radec.X = xForm.RAApparent;
                    radec.Y = xForm.DECApparent;
                    break;
                case EquatorialCoordinateType.J2050:
                    radec.X = xForm.RAJ2000;
                    radec.Y = xForm.DecJ2000;
                    break;
                case EquatorialCoordinateType.B1950:
                    radec.X = xForm.RAJ2000;
                    radec.Y = xForm.DecJ2000;
                    break;
                default:
                    radec.X = rightAscension;
                    radec.Y = declination;
                    break;
            }
            if (log)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Environment.CurrentManagedThreadId,
                    Message = $"lat:{s?.Latitude ?? default}|long:{s?.Longitude ?? default}|Ref:{s?.Refraction ?? default}|Ele:{s?.Elevation ?? default}|ra/dec:{rightAscension},{declination}/{radec.X},{radec.Y}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            return radec;
        }

        #endregion

    }
}
