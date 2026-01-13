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
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    public static class Axes
    {
        /// <summary>
        /// Convert internal mount axis degrees to mount with correct hemisphere using context object
        /// </summary>
        /// <returns></returns>
        public static double[] MountAxis2Mount(AxesContext context)
        {
            var appAxisX = context.GetAppAxisX();
            var appAxisY = context.GetAppAxisY();
            var a = new[] { appAxisX, appAxisY };
            if (context.AlignmentMode == AlignmentMode.GermanPolar)
            {
                if (context.SouthernHemisphere)
                {
                    a[0] = appAxisX + 180;
                    a[1] = 180 - appAxisY;
                }
                else
                {
                    a[0] = appAxisX;
                    a[1] = appAxisY;
                }
            }
            return a;
        }

        /// <summary>
        /// Convert axes positions from Local to Mount using context object
        /// </summary>
        /// <param name="axes">Axes positions to convert</param>
        /// <param name="context">Mount configuration context</param>
        /// <returns>Converted axes positions</returns>
        internal static double[] AxesAppToMount(double[] axes, AxesContext context)
        {
            var a = new[] { axes[0], axes[1] };

            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    break;

                case AlignmentMode.GermanPolar:
                    switch (context.MountType)
                    {
                        case MountType.Simulator:
                            if (context.SouthernHemisphere)
                            {
                                a[0] = 180 - a[0];
                                a[1] = a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = a[1];
                            }
                            break;

                        case MountType.SkyWatcher:
                            if (context.SouthernHemisphere)
                            {
                                a[0] = 180 - a[0];
                                a[1] = a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = 180 - a[1];
                            }
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;

                case AlignmentMode.Polar:
                    switch (context.MountType)
                    {
                        case MountType.Simulator:
                            if (context.SouthernHemisphere)
                            {
                                a[0] = -a[0];
                                a[1] = a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = a[1];
                            }
                            break;

                        case MountType.SkyWatcher:
                            if (context.PolarMode == PolarMode.Left) // ✅ From context
                            {
                                if (context.SouthernHemisphere)
                                {
                                    a[0] = 180 - a[0];
                                    a[1] = a[1];
                                }
                                else
                                {
                                    a[0] = a[0];
                                    a[1] = 180 - a[1];
                                }
                            }
                            else
                            {
                                if (context.SouthernHemisphere)
                                {
                                    a[0] = -a[0];
                                    a[1] = a[1];
                                }
                                else
                                {
                                    a[0] = a[0];
                                    a[1] = a[1];
                                }
                            }
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Debug,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{axes[0]}|{axes[1]}|{a[0]}|{a[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return a;
        }

        // ✅ KEEP OLD OVERLOAD (backward compatibility - delegates to new)
        internal static double[] AxesAppToMount(
            double[] axes,
            AlignmentMode alignmentMode,
            MountType mountType)
        {
            var context = AxesContext.FromStatic();
            return AxesAppToMount(axes, context);
        }

        /// <summary>
        /// Converts axes positions from Mount to App ha/dec or alt/az) 
        /// </summary>
        /// <param name="axes"></param>
        /// <returns></returns>
        internal static double[] AxesMountToApp(double[] axes, AlignmentMode alignmentMode, MountType mountType)

        {
            var a = new[] { axes[0], axes[1] };
            switch (alignmentMode)
            {
                case AlignmentMode.AltAz:
                    break;
                case AlignmentMode.GermanPolar:
                    switch (mountType)
                    {
                        case MountType.Simulator:
                            if (SkyServer.SouthernHemisphere)
                            {
                                a[0] = a[0] * -1.0;
                                a[1] = 180 - a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = a[1];
                            }

                            break;
                        case MountType.SkyWatcher:
                            if (SkyServer.SouthernHemisphere)
                            {
                                a[0] = a[0] * -1.0;
                                a[1] = 180 - a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = 180 - a[1];
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                case AlignmentMode.Polar:
                    switch (mountType)
                    {
                        case MountType.Simulator:
                            if (SkyServer.SouthernHemisphere)
                            {
                                a[0] = a[0] * -1.0;
                                a[1] = a[1];
                            }
                            else
                            {
                                a[0] = a[0];
                                a[1] = a[1];
                            }

                            break;
                        case MountType.SkyWatcher:
                            if (SkyServer.PolarMode == PolarMode.Left)
                            {
                                if (SkyServer.SouthernHemisphere)
                                {
                                    a[0] = a[0] * -1.0;
                                    a[1] = 180 - a[1];
                                }
                                else
                                {
                                    a[0] = a[0];
                                    a[1] = 180 - a[1];
                                }
                            }
                            else
                            {
                                if (SkyServer.SouthernHemisphere)
                                {
                                    a[0] = a[0] * -1.0;
                                    a[1] = a[1];
                                }
                                else
                                {
                                    a[0] = a[0];
                                    a[1] = a[1];
                                }
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                default:
                    break;
            }

            return a;
        }

        /// <summary>
        /// German and polar equatorial mounts have two possible axes positions, given an axis position this returns the other 
        /// Alternate position is 180 degrees from the current position
        /// Alt Az have two possible axes positions, given an axis position this returns the other
        /// Alternate position plus / minus 360 degrees from the current position
        /// </summary>
        /// <param name="alt">position</param>
        /// <returns>other axis position</returns>
        internal static double[] GetAltAxisPosition(double[] alt, AxesContext context)
        {
            var d = new[] { 0.0, 0.0 };
            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    if (alt[0] > 0)
                    {
                        d[0] = alt[0] - 360;
                    }
                    else
                    {
                        d[0] = alt[0] + 360;
                    }
                    d[1] = alt[1];
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    if (alt[0] > 90)
                    {
                        d[0] = alt[0] - 180;
                        d[1] = 180 - alt[1];
                    }
                    else
                    {
                        d[0] = alt[0] + 180;
                        d[1] = 180 - alt[1];
                    }
                    break;
            }
            return d;
        }

        /// <summary>
        /// convert a decimal Az/Alt positions to an axes positions.
        /// </summary>
        /// <param name="azAlt"></param>
        /// <param name="context">Mount configuration context</param>
        /// <returns></returns>
        internal static double[] AzAltToAxesXy(double[] azAlt, AxesContext context)
        {
            var axes = new[] { 0.0, 0.0 };
            var b = new[] { 0.0, 0.0 };
            var alt = new[] { 0.0, 0.0 };
            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    axes[0] = Range.Range180(azAlt[0]); // Azimuth range is -180 to 180
                    axes[1] = azAlt[1];
                    //check for alternative position within hardware limits
                    b = AxesAppToMount(axes, context);
                    alt = SkyServer.GetAlternatePosition(b);
                    if (alt != null) axes = alt;
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
//                    axes = Coordinate.AltAz2RaDec(azAlt[1], azAlt[0], SkySettings.Latitude, lst);
                    axes = Coordinate.AltAz2HaDec(azAlt[1], azAlt[0], context.Latitude);
                    axes = HaDecToAxesXy(axes, context);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Range:{axes[0]}|{axes[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return new[] { axes[0], axes[1] };
        }

        /// <summary>
        /// Conversion of mount axis positions in degrees to Az and Alt
        /// Raw mount axis values must be converted using AxesMountToApp before calling this method.
        /// </summary>
        /// <param name="axes"></param>
        /// <param name="context">Mount configuration context</param>
        /// <returns>AzAlt</returns>
        internal static double[] AxesXyToAzAlt(double[] axes, AxesContext context)
        {
            var altAz = new[] { axes[1], axes[0] };
            var ha = 0.0;
            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    break;
                case AlignmentMode.GermanPolar:
                case AlignmentMode.Polar:
                    if (altAz[0] > 90)
                    {
                        altAz[1] += 180.0;
                        altAz[0] = 180 - altAz[0];
                        altAz = Range.RangeAltAz(altAz);
                    }

                    //southern hemisphere
                    if (context.SouthernHemisphere) altAz[0] = -altAz[0];

                    //axis degrees to ha
                    ha = altAz[1] / 15.0;
                    altAz = Coordinate.HaDec2AltAz(ha, altAz[0], context.Latitude);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            altAz = Range.RangeAltAz(altAz);
            return new[] { altAz[1], altAz[0] };
        }

        /// <summary>
        /// Conversion of mount axis positions in degrees to Ra and Dec
        /// </summary>
        /// <param name="axes"></param>
        /// <param name="context">Mount configuration context</param>
        /// <returns></returns>
        internal static double[] AxesXyToRaDec(IReadOnlyList<double> axes, AxesContext context)
        {
            double[] raDec = new[] { axes[0], axes[1] };
            double lst = context.GetLst(); // ✅ From context
            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    raDec = Coordinate.AltAz2RaDec(axes[1], axes[0], context.Latitude, lst);
                    break;
                case AlignmentMode.GermanPolar:
                case AlignmentMode.Polar:
                    if (raDec[1] > 90)
                    {
                        raDec[0] += 180.0;
                        raDec[1] = 180 - raDec[1];
                        raDec = Range.RangeAz360Alt90(raDec);
                    }

                    raDec[0] = lst - raDec[0] / 15.0;
                    if (context.SouthernHemisphere)
                        raDec[1] = -raDec[1];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            raDec = Range.RangeRaDec(raDec);
            return raDec;
        }

        /// <summary>
        /// Convert RA/Dec position to axes positions using context object
        /// </summary>
        /// <param name="raDec">RA (hours 0-24) and Dec (degrees -90 to 90)</param>
        /// <param name="context">Mount configuration context</param>
        /// <returns>Axes position in mount coordinates</returns>
        internal static double[] RaDecToAxesXy(IReadOnlyList<double> raDec, AxesContext context)
        {
            // RA needs LST for conversion to hour angle
            double lst = context.GetLst();
            return RaDecToAxesXyCore(raDec, useLst: true, lst, context);
        }

        /// <summary>
        /// Convert Hour Angle/Dec position to axes positions using context object
        /// </summary>
        /// <param name="haDec">Hour Angle (hours) and Dec (degrees -90 to 90)</param>
        /// <param name="context">Mount configuration context</param>
        /// <returns>Axes position in mount coordinates</returns>
        internal static double[] HaDecToAxesXy(IReadOnlyList<double> haDec, AxesContext context)
        {
            // Hour Angle is already in mount reference frame, no LST needed
            return RaDecToAxesXyCore(haDec, useLst: false, lst: 0.0, context);
        }

        /// <summary>
        /// Core conversion logic shared by RaDecToAxesXy and HaDecToAxesXy
        /// </summary>
        /// <param name="coordinates">RA/Dec or HA/Dec coordinates</param>
        /// <param name="useLst">True if converting from RA (apply LST offset), false for HA</param>
        /// <param name="lst">Local Sidereal Time (only used if useLst is true)</param>
        /// <param name="context">Mount configuration context</param>
        /// <returns>Axes position in mount coordinates</returns>
        private static double[] RaDecToAxesXyCore(
            IReadOnlyList<double> coordinates,
            bool useLst,
            double lst,
            AxesContext context)
        {
            double[] axes = { coordinates[0], coordinates[1] };
            double[] b;
            double[] alt;

            switch (context.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // Convert to Alt/Az coordinates
                    axes = Coordinate.RaDec2AltAz(axes[0], axes[1], lst, context.Latitude);
                    Array.Reverse(axes); // Swap to [Az, Alt]
                    axes[0] = Range.Range180(axes[0]); // Azimuth range is -180 to 180

                    // Check for alternative position within hardware limits
                    b = AxesAppToMount(axes, context);
                    alt = SkyServer.GetAlternatePosition(b);
                    if (alt != null) axes = alt;
                    return AxesAppToMount(axes, context);

                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    // Convert to mount axes
                    // If useLst is true: convert RA to HA via LST, else coordinates[0] is already HA
                    axes[0] = useLst ? 15.0 * (lst - axes[0]) : 15.0 * axes[0];
                    axes[0] = Range.Range360(axes[0]);

                    // Southern hemisphere dec inversion
                    if (context.SouthernHemisphere)
                        axes[1] = -axes[1];

                    // Adjust axes to be through the pole if needed
                    if (axes[0] > 180.0)
                    {
                        axes[0] += 180;
                        axes[1] = 180 - axes[1];
                    }

                    // Normalize axes ranges
                    axes = Range.RangeAxesXy(axes); // Axes[0] in [0..180), Axes[1] in [-90..90] or [-180..-90] U [90..180]

                    // Check for alternative position within flip angle and hardware limits
                    axes = AxesAppToMount(axes, context);
                    alt = SkyServer.GetAlternatePosition(axes);
                    return (alt is null) ? axes : alt;

                default:
                    throw new ArgumentOutOfRangeException(nameof(context.AlignmentMode),
                        context.AlignmentMode,
                        "Unsupported alignment mode");
            }
        }
        /// <summary>
        /// Determine if a flip is needed to reach the RA/Dec coordinates
        /// </summary>
        /// <remarks>Uses a SideOfPier test at the converted coordinates and compares to current SideOfPier</remarks>
        /// <param name="raDec"></param>
        /// <param name="lst"></param>
        /// <returns></returns>
        internal static bool IsFlipRequired(IReadOnlyList<double> raDec, double lst = double.NaN)
        {
            var axes = new[] { raDec[0], raDec[1] };
            if (double.IsNaN(lst)) lst = SkyServer.SiderealTime;
            switch (SkySettings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return false;
                case AlignmentMode.GermanPolar:
                case AlignmentMode.Polar:
                    axes[0] = (lst - axes[0]) * 15.0;
                    if (SkyServer.SouthernHemisphere) axes[1] = -axes[1];
                    axes[0] = Range.Range360(axes[0]);

                    if (axes[0] > 180.0 || axes[0] < 0)
                    {
                        // adjust the targets to be through the pole
                        axes[0] += 180;
                        axes[1] = 180 - axes[1];
                    }

                    axes = Range.RangeAxesXy(axes);

                    //check if within Flip Angle
                    var b = AxesAppToMount(axes, SkySettings.AlignmentMode, SkySettings.Mount);
                    if (SkyServer.IsWithinFlipLimits(b))
                    {
                        return false;
                    }

                    //similar to SideOfPier property code but this passes conform for both hemispheres
                    PointingState e = PointingState.Unknown;
                    if (SkyServer.SideOfPier == PointingState.Unknown) { return false; }

                    switch (SkySettings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            e = b[0] >= 0.0 ? PointingState.Normal : PointingState.ThroughThePole;
                            break;
                        case AlignmentMode.Polar:
                            e = (b[1] < 90.0000000001 && b[1] > -90.0000000001) ? PointingState.Normal : PointingState.ThroughThePole;
                            break;
                        case AlignmentMode.GermanPolar:
                            switch (SkySettings.Mount)
                            {
                                case MountType.Simulator:
                                    if (b[1] < 90.0000000001 && b[1] > -90.0000000001) { e = PointingState.Normal; }
                                    else { e = PointingState.ThroughThePole; }
                                    break;
                                case MountType.SkyWatcher:
                                    if (SkyServer.SouthernHemisphere)
                                    {
                                        if (b[1] < 90.0 && b[1] > -90.0) { e = PointingState.Normal; }
                                        else { e = PointingState.ThroughThePole; }
                                    }
                                    else
                                    {
                                        if (b[1] < 90.0 && b[1] > -90.0) { e = PointingState.ThroughThePole; }
                                        else { e = PointingState.Normal; }
                                    }

                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            break;
                    }
                    return e != SkyServer.SideOfPier;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

    }
}
