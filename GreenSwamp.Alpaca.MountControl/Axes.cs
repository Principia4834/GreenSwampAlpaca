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
        /// Check if position is within flip limits using settings values
        /// </summary>
        private static bool IsWithinFlipLimits(SkySettings settings, double[] position)
        {
            var absPos0 = Math.Abs(position[0]);
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return (settings.AxisLimitX >= absPos0) && (absPos0 >= 360.0 - settings.AxisLimitX);
                case AlignmentMode.Polar:
                    return (180.0 - settings.AxisLimitX <= absPos0) && (absPos0 <= settings.AxisLimitX);
                case AlignmentMode.GermanPolar:
                    return -settings.HourAngleLimit < absPos0 && absPos0 < settings.HourAngleLimit ||
                           180 - settings.HourAngleLimit < absPos0 && absPos0 < 180 + settings.HourAngleLimit;
                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AlignmentMode),
                        settings.AlignmentMode, "Unsupported alignment mode");
            }
        }

        /// <summary>
        /// Convert internal mount axis degrees to mount with correct hemisphere
        /// </summary>
        /// <param name="settings">Mount settings</param>
        /// <param name="appAxisX">Current application axis X position in degrees</param>
        /// <param name="appAxisY">Current application axis Y position in degrees</param>
        /// <returns></returns>
        public static double[] MountAxis2Mount(SkySettings settings, double appAxisX, double appAxisY)
        {
            var a = new[] { appAxisX, appAxisY };
            if (settings.AlignmentMode == AlignmentMode.GermanPolar)
            {
                if (settings.Latitude < 0)
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
        /// Convert axes positions from Local to Mount
        /// </summary>
        /// <param name="axes">Axes positions to convert</param>
        /// <param name="settings">Mount settings</param>
        /// <returns>Converted axes positions</returns>
        internal static double[] AxesAppToMount(double[] axes, SkySettings settings)
        {
            var a = new[] { axes[0], axes[1] };

            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    break;

                case AlignmentMode.GermanPolar:
                    switch (settings.Mount)
                    {
                        case MountType.Simulator:
                            if (settings.Latitude < 0)
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
                            if (settings.Latitude < 0)
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
                    switch (settings.Mount)
                    {
                        case MountType.Simulator:
                            if (settings.Latitude < 0)
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
                            if (settings.PolarMode == PolarMode.Left)
                            {
                                if (settings.Latitude < 0)
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
                                if (settings.Latitude < 0)
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
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{axes[0]}|{axes[1]}|{a[0]}|{a[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return a;
        }

        /// <summary>
        /// Convert axes positions from Mount to App (ha/dec or alt/az)
        /// </summary>
        /// <param name="axes">Axes positions to convert</param>
        /// <param name="settings">Mount settings</param>
        /// <returns>Converted axes positions</returns>
        internal static double[] AxesMountToApp(double[] axes, SkySettings settings)
        {
            var a = new[] { axes[0], axes[1] };

            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // No conversion needed for AltAz
                    break;

                case AlignmentMode.GermanPolar:
                    switch (settings.Mount)
                    {
                        case MountType.Simulator:
                            if (settings.Latitude < 0)
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
                            if (settings.Latitude < 0)
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
                            throw new ArgumentOutOfRangeException(nameof(settings.Mount),
                                settings.Mount,
                                "Unsupported mount type");
                    }
                    break;

                case AlignmentMode.Polar:
                    switch (settings.Mount)
                    {
                        case MountType.Simulator:
                            if (settings.Latitude < 0)
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
                            if (settings.PolarMode == PolarMode.Left)
                            {
                                if (settings.Latitude < 0)
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
                                if (settings.Latitude < 0)
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
                            throw new ArgumentOutOfRangeException(nameof(settings.Mount),
                                settings.Mount,
                                "Unsupported mount type");
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AlignmentMode),
                        settings.AlignmentMode,
                        "Unsupported alignment mode");
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
        /// <param name="settings">Mount settings</param>
        /// <returns>other axis position</returns>
        internal static double[] GetAltAxisPosition(double[] alt, SkySettings settings)
        {
            var d = new[] { 0.0, 0.0 };
            switch (settings.AlignmentMode)
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
        /// <param name="settings">Mount settings</param>
        /// <param name="skipAlternatePosition">Skip alternate position selection (for park/home position loading)</param>
        /// <param name="selectAlternatePosition">Delegate to select alternate position; null skips selection</param>
        /// <returns></returns>
        internal static double[] AzAltToAxesXy(double[] azAlt, SkySettings settings,
            bool skipAlternatePosition = false, Func<double[], double[]?>? selectAlternatePosition = null)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"ENTRY|Input:{azAlt[0]}|{azAlt[1]}|AlignmentMode:{settings.AlignmentMode}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            var axes = new[] { 0.0, 0.0 };
            var b = new[] { 0.0, 0.0 };
            double[] alt;
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    axes[0] = Range.Range180(azAlt[0]); // Azimuth range is -180 to 180
                    axes[1] = azAlt[1];
                    //check for alternative position within hardware limits
                    b = AxesAppToMount(axes, settings);
                    alt = selectAlternatePosition?.Invoke(b);
                    if (alt != null) axes = alt;
                    break;
                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    axes = Coordinate.AltAz2HaDec(azAlt[1], azAlt[0], settings.Latitude);
                    var monitorItem2 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterAltAz2HaDec|HA:{axes[0]}hrs|Dec:{axes[1]}deg|Calling:HaDecToAxesXy|skipAlt:{skipAlternatePosition}"
                    };
                    MonitorLog.LogToMonitor(monitorItem2);
                    axes = HaDecToAxesXy(axes, settings, skipAlternatePosition, selectAlternatePosition);
                    var monitorItem3 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterHaDecToAxesXy|X:{axes[0]}|Y:{axes[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem3);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId, Message = $"Range:{axes[0]}|{axes[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return [axes[0], axes[1]];
        }

        /// <summary>
        /// Conversion of mount axis positions in degrees to Az and Alt
        /// Raw mount axis values must be converted using AxesMountToApp before calling this method.
        /// </summary>
        /// <param name="axes"></param>
        /// <param name="settings">Mount settings</param>
        /// <returns>AzAlt</returns>
        internal static double[] AxesXyToAzAlt(double[] axes, SkySettings settings)
        {
            var altAz = new[] { axes[1], axes[0] };
            var ha = 0.0;
            switch (settings.AlignmentMode)
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
                    if (settings.Latitude < 0) altAz[0] = -altAz[0];

                    //axis degrees to ha
                    ha = altAz[1] / 15.0;
                    altAz = Coordinate.HaDec2AltAz(ha, altAz[0], settings.Latitude);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            altAz = Range.RangeAltAz(altAz);
            return [altAz[1], altAz[0]];
        }

        /// <summary>
        /// Conversion of mount axis positions in degrees to Ra and Dec
        /// </summary>
        /// <param name="axes"></param>
        /// <param name="settings">Mount settings</param>
        /// <param name="lst">Local Sidereal Time in hours; computed from settings if null</param>
        /// <returns></returns>
        internal static double[] AxesXyToRaDec(IReadOnlyList<double> axes, SkySettings settings, double? lst = null)
        {
            double[] raDec = [axes[0], axes[1]];
            double lstVal = lst ?? SkyServer.GetLocalSiderealTime(settings.Longitude);
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    raDec = Coordinate.AltAz2RaDec(axes[1], axes[0], settings.Latitude, lstVal);
                    break;
                case AlignmentMode.GermanPolar:
                case AlignmentMode.Polar:
                    if (raDec[1] > 90)
                    {
                        raDec[0] += 180.0;
                        raDec[1] = 180 - raDec[1];
                        raDec = Range.RangeAz360Alt90(raDec);
                    }

                    raDec[0] = lstVal - raDec[0] / 15.0;
                    if (settings.Latitude < 0)
                        raDec[1] = -raDec[1];
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            raDec = Range.RangeRaDec(raDec);
            return raDec;
        }

        /// <summary>
        /// Convert RA/Dec position to axes positions
        /// </summary>
        /// <param name="raDec">RA (hours 0-24) and Dec (degrees -90 to 90)</param>
        /// <param name="settings">Mount settings</param>
        /// <param name="lst">Local Sidereal Time in hours; computed from settings if null</param>
        /// <param name="selectAlternatePosition">Delegate to select alternate position; null skips selection</param>
        /// <returns>Axes position in mount coordinates</returns>
        internal static double[] RaDecToAxesXy(IReadOnlyList<double> raDec, SkySettings settings, double? lst = null,
            Func<double[], double[]?>? selectAlternatePosition = null)
        {
            double lstVal = lst ?? SkyServer.GetLocalSiderealTime(settings.Longitude);
            return RaDecToAxesXyCore(raDec, useLst: true, lstVal, settings, selectAlternatePosition: selectAlternatePosition);
        }

        /// <summary>
        /// Convert Hour Angle/Dec position to axes positions
        /// </summary>
        /// <param name="haDec">Hour Angle (hours) and Dec (degrees -90 to 90)</param>
        /// <param name="settings">Mount settings</param>
        /// <param name="skipAlternatePosition">Skip alternate position selection (for park/home position loading)</param>
        /// <param name="selectAlternatePosition">Delegate to select alternate position; null skips selection</param>
        /// <returns>Axes position in mount coordinates</returns>
        internal static double[] HaDecToAxesXy(IReadOnlyList<double> haDec, SkySettings settings,
            bool skipAlternatePosition = false, Func<double[], double[]?>? selectAlternatePosition = null)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"ENTRY|HA:{haDec[0]}hrs|Dec:{haDec[1]}deg|Calling:RaDecToAxesXyCore|skipAlt:{skipAlternatePosition}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            // Hour Angle is already in mount reference frame, no LST needed
            var result = RaDecToAxesXyCore(haDec, useLst: false, lst: 0.0, settings, skipAlternatePosition, selectAlternatePosition);

            var monitorItem2 = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"RETURN|X:{result[0]}|Y:{result[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem2);

            return result;
        }

        /// <summary>
        /// Core conversion logic shared by RaDecToAxesXy and HaDecToAxesXy
        /// </summary>
        /// <param name="coordinates">RA/Dec or HA/Dec coordinates</param>
        /// <param name="useLst">True if converting from RA (apply LST offset), false for HA</param>
        /// <param name="lst">Local Sidereal Time (only used if useLst is true)</param>
        /// <param name="settings">Mount settings</param>
        /// <param name="skipAlternatePosition">Skip alternate position selection (for park/home position loading)</param>
        /// <param name="selectAlternatePosition">Delegate to select alternate position; null skips selection</param>
        /// <returns>Axes position in mount coordinates</returns>
        private static double[] RaDecToAxesXyCore(
            IReadOnlyList<double> coordinates,
            bool useLst,
            double lst,
            SkySettings settings,
            bool skipAlternatePosition = false,
            Func<double[], double[]?>? selectAlternatePosition = null)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"ENTRY|Coords:{coordinates[0]}|{coordinates[1]}|useLst:{useLst}|Mode:{settings.AlignmentMode}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            double[] axes = [coordinates[0], coordinates[1]];
            double[] b;
            double[] alt;

            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // Convert to Alt/Az coordinates
                    axes = Coordinate.RaDec2AltAz(axes[0], axes[1], lst, settings.Latitude);
                    Array.Reverse(axes); // Swap to [Az, Alt]
                    axes[0] = Range.Range180(axes[0]); // Azimuth range is -180 to 180

                    // Check for alternative position within hardware limits
                    b = AxesAppToMount(axes, settings);
                    alt = selectAlternatePosition?.Invoke(b);
                    if (alt != null) axes = alt;
                    return AxesAppToMount(axes, settings);

                case AlignmentMode.Polar:
                case AlignmentMode.GermanPolar:
                    var monitorItem2 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"PolarCase|BeforeConversion|X:{axes[0]}|Y:{axes[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem2);

                    // Convert to mount axes
                    // If useLst is true: convert RA to HA via LST, else coordinates[0] is already HA
                    axes[0] = useLst ? 15.0 * (lst - axes[0]) : 15.0 * axes[0];
                    axes[0] = Range.Range360(axes[0]);

                    var monitorItem3 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterHAtoDeg|X:{axes[0]}deg|Y:{axes[1]}deg"
                    };
                    MonitorLog.LogToMonitor(monitorItem3);

                    // Southern hemisphere dec inversion
                    if (settings.Latitude < 0)
                        axes[1] = -axes[1];

                    var monitorItem4 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterHemisphereInv|SH:{settings.Latitude < 0}|X:{axes[0]}|Y:{axes[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem4);

                    // Adjust axes to be through the pole if needed
                    if (axes[0] > 180.0)
                    {
                        axes[0] += 180;
                        axes[1] = 180 - axes[1];
                    }

                    var monitorItem5 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterThroughPole|X:{axes[0]}|Y:{axes[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem5);

                    // Normalize axes ranges
                    axes = Range.RangeAxesXy(axes); // Axes[0] in [0..180), Axes[1] in [-90..90] or [-180..-90] U [90..180]

                    var monitorItem6 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterRangeNormalize|X:{axes[0]}|Y:{axes[1]}|Calling:AxesAppToMount"
                    };
                    MonitorLog.LogToMonitor(monitorItem6);

                    // Convert to mount coordinates
                    axes = AxesAppToMount(axes, settings);

                    var monitorItem7 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"AfterAxesAppToMount|X:{axes[0]}|Y:{axes[1]}|CheckingAlternate|skipAlt:{skipAlternatePosition}"
                    };
                    MonitorLog.LogToMonitor(monitorItem7);

                    // Skip alternate position selection when loading park/home positions
                    alt = skipAlternatePosition ? null : selectAlternatePosition?.Invoke(axes);

                    var finalAxes = (alt is null) ? axes : alt;
                    var monitorItem8 = new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server,
                        Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Environment.CurrentManagedThreadId,
                        Message = $"RETURN|AltPos:{(alt != null)}|Skipped:{skipAlternatePosition}|X:{finalAxes[0]}|Y:{finalAxes[1]}"
                    };
                    MonitorLog.LogToMonitor(monitorItem8);

                    return finalAxes;

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AlignmentMode),
                        settings.AlignmentMode,
                        "Unsupported alignment mode");
            }
        }

        /// <summary>
        /// Determine if a flip is needed to reach the RA/Dec coordinates
        /// </summary>
        /// <param name="raDec">Target RA/Dec coordinates [RA in hours, Dec in degrees]</param>
        /// <param name="settings">Mount settings</param>
        /// <param name="sideOfPier">Current side of pier state</param>
        /// <param name="lst">Local Sidereal Time in hours; computed from settings if null</param>
        /// <returns>True if flip is required to reach target, false otherwise</returns>
        internal static bool IsFlipRequired(
            IReadOnlyList<double> raDec,
            SkySettings settings,
            PointingState sideOfPier,
            double? lst = null)
        {
            var axes = new[] { raDec[0], raDec[1] };
            double lstVal = lst ?? SkyServer.GetLocalSiderealTime(settings.Longitude);

            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // AltAz mounts don't need meridian flips
                    return false;

                case AlignmentMode.GermanPolar:
                case AlignmentMode.Polar:
                    // Convert RA/Dec to mount axes (HA/Dec)
                    axes[0] = (lstVal - axes[0]) * 15.0; // RA to HA in degrees
                    if (settings.Latitude < 0)
                        axes[1] = -axes[1];
                    axes[0] = Range.Range360(axes[0]);

                    // Adjust axes to be through the pole if needed
                    if (axes[0] > 180.0 || axes[0] < 0)
                    {
                        axes[0] += 180;
                        axes[1] = 180 - axes[1];
                    }

                    axes = Range.RangeAxesXy(axes);

                    // Convert to mount coordinates
                    var b = AxesAppToMount(axes, settings);

                    // Check if target is within flip limits (no flip needed)
                    if (IsWithinFlipLimits(settings, b))
                    {
                        return false;
                    }

                    // Check if current SideOfPier is valid
                    if (sideOfPier == PointingState.Unknown)
                    {
                        return false; // Can't determine flip requirement without current state
                    }

                    // Calculate what side of pier the target would be on
                    var targetSideOfPier = CalculateSideOfPier(b, settings);

                    // Flip is required if target side differs from current side
                    return targetSideOfPier != sideOfPier;

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AlignmentMode),
                        settings.AlignmentMode,
                        "Unsupported alignment mode");
            }
        }

        /// <summary>
        /// Calculate side of pier for given mount axes position
        /// </summary>
        /// <param name="axes">Mount axes position [X, Y] in degrees</param>
        /// <param name="settings">Mount settings</param>
        /// <returns>PointingState indicating side of pier</returns>
        private static PointingState CalculateSideOfPier(double[] axes, SkySettings settings)
        {
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return axes[0] >= 0.0
                        ? PointingState.Normal
                        : PointingState.ThroughThePole;

                case AlignmentMode.Polar:
                    return (axes[1] < 90.0000000001 && axes[1] > -90.0000000001)
                        ? PointingState.Normal
                        : PointingState.ThroughThePole;

                case AlignmentMode.GermanPolar:
                    switch (settings.Mount)
                    {
                        case MountType.Simulator:
                            return (axes[1] < 90.0000000001 && axes[1] > -90.0000000001)
                                ? PointingState.Normal
                                : PointingState.ThroughThePole;

                        case MountType.SkyWatcher:
                            bool isWithinDecRange = (axes[1] < 90.0 && axes[1] > -90.0);
                            if (settings.Latitude < 0)
                            {
                                return isWithinDecRange
                                    ? PointingState.Normal
                                    : PointingState.ThroughThePole;
                            }
                            else
                            {
                                return isWithinDecRange
                                    ? PointingState.ThroughThePole
                                    : PointingState.Normal;
                            }

                        default:
                            throw new ArgumentOutOfRangeException(nameof(settings.Mount),
                                settings.Mount,
                                "Unsupported mount type");
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.AlignmentMode),
                        settings.AlignmentMode,
                        "Unsupported alignment mode");
            }
        }
    }
}
