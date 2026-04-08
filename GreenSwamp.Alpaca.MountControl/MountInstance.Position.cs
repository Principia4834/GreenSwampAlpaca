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
 
// Phase M2: Per-instance position, coordinate and limit methods.
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
 /// <summary>MountInstance partial — position, coordinates and limits. Phase M2.</summary>
 public partial class MountInstance
 {
 #region Position, Coordinates and Limits (Phase M2)

        /// <summary>Get alternate position based on alignment mode — instance version.</summary>
        public double[] GetAlternatePosition(double[] position)
        {
            switch (_settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return GetAlternatePositionAltAz(position);
                case AlignmentMode.Polar:
                    return GetAlternatePositionPolar(position);
                case AlignmentMode.GermanPolar:
                    return GetAlternatePositionGEM(position);
                default:
                    throw new ArgumentOutOfRangeException(nameof(_settings.AlignmentMode), _settings.AlignmentMode, "Unsupported alignment mode for alternate position calculation.");
            }
        }

        /// <summary>Calculates which pair of axis positions is closer to a given position.</summary>
        private static string ChooseClosestPosition(double position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Abs(a[0] - position);
            var val2 = Math.Abs(b[0] - position);
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        /// <summary>Calculates which pair of axis positions is closer to a given position (polar).</summary>
        private static string ChooseClosestPositionPolar(IReadOnlyList<double> position, IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            var val1 = Math.Max(Math.Abs(a[0] - position[0]), Math.Abs(a[1] - position[1]));
            var val2 = Math.Max(Math.Abs(b[0] - position[0]), Math.Abs(b[1] - position[1]));
            if (!(Math.Abs(val1 - val2) > 0)) { return "a"; }
            return val1 < val2 ? "a" : "b";
        }

        /// <summary>GEM: within meridian limits check for closest slew — instance version.</summary>
        private double[] GetAlternatePositionGEM(double[] position)
        {
            if (!IsWithinFlipLimits(position)) { return null; }
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            if (!IsWithinFlipLimits(alt)) { return null; }
            var cl = ChooseClosestPosition(_actualAxisX, position, alt);
            if (_flipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{_actualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                });
            }
            if (cl != "b") { return null; }
            return alt;
        }

        /// <summary>AltAz: within hardware limits get alternate position — instance version.</summary>
        private double[] GetAlternatePositionAltAz(double[] position)
        {
            if (!IsWithinFlipLimits(position)) { return null; }
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            var cl = ChooseClosestPosition(_actualAxisX, position, alt);
            if (_flipOnNextGoto)
            {
                cl = cl == "a" ? "b" : "a";
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"flip|{cl}|{_actualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                });
            }
            if (cl != "b") { return null; }
            return alt;
        }

        /// <summary>Polar: within hardware limits and flip angle get alternate position — instance version.</summary>
        private double[] GetAlternatePositionPolar(double[] position)
        {
            var context = AxesContext.FromSettings(_settings);
            var alt = Axes.GetAltAxisPosition(position, context);
            alt[0] = Range.Range180(alt[0]);
            var altOk = IsTargetWithinLimits(alt);
            var posOk = IsTargetWithinLimits(position);
            if (!altOk) return null;
            if (posOk && altOk)
            {
                var cl = ChooseClosestPositionPolar(new[] { _actualAxisX, _actualAxisY }, position, alt);
                if (_flipOnNextGoto)
                {
                    cl = cl == "a" ? "b" : "a";
                    MonitorLog.LogToMonitor(new MonitorEntry
                    {
                        Datetime = HiResDateTime.UtcNow,
                        Device = MonitorDevice.Server,
                        Category = MonitorCategory.Server,
                        Type = MonitorType.Information,
                        Method = MethodBase.GetCurrentMethod()?.Name,
                        Thread = Thread.CurrentThread.ManagedThreadId,
                        Message = $"flip|{cl}|{_actualAxisX}|{position[0]}|{position[1]}|{alt[0]}|{alt[1]}"
                    });
                }
                if (cl != "b") { return null; }
                return alt;
            }
            return alt;
        }

        /// <summary>Get current local sidereal time — instance version.</summary>
        internal double GetLocalSiderealTime()
            => GetLocalSiderealTime(HiResDateTime.UtcNow);

        /// <summary>Get local sidereal time for specific UTC time — instance version.</summary>
        internal double GetLocalSiderealTime(DateTime utcNow)
        {
            var gsjd = JDate.Ole2Jd(utcNow);
            return Time.Lst(JDate.Epoch2000Days(), gsjd, false, _settings.Longitude);
        }

        /// <summary>Get local sidereal time for an explicit longitude — instance version.</summary>
        internal double GetLocalSiderealTime(double longitude)
        {
            var gsjd = JDate.Ole2Jd(HiResDateTime.UtcNow);
            return Time.Lst(JDate.Epoch2000Days(), gsjd, false, longitude);
        }

        /// <summary>Calculates if axis position is within the defined flip angle — instance version.</summary>
        public bool IsWithinFlipLimits(IReadOnlyList<double> position)
        {
            var absPos0 = Math.Abs(position[0]);
            switch (_settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    return (_settings.AxisLimitX >= absPos0) && (absPos0 >= 360.0 - _settings.AxisLimitX);
                case AlignmentMode.Polar:
                    return (180.0 - _settings.AxisLimitX <= absPos0) && (absPos0 <= _settings.AxisLimitX);
                case AlignmentMode.GermanPolar:
                    return -_settings.HourAngleLimit < absPos0 && absPos0 < _settings.HourAngleLimit ||
                           180 - _settings.HourAngleLimit < absPos0 && absPos0 < 180 + _settings.HourAngleLimit;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>Checks if the target is within the defined limits — instance version.</summary>
        private bool IsTargetWithinLimits(double[] target)
        {
            const double oneArcSec = 1.0 / 3600;
            var axisUpperLimitY = _settings.AxisUpperLimitY;
            var axisLowerLimitY = _settings.AxisLowerLimitY;
            if (_settings.AlignmentMode == AlignmentMode.Polar && _settings.PolarMode == PolarMode.Left)
            {
                axisLowerLimitY = 180 - _settings.AxisUpperLimitY;
                axisUpperLimitY = 180 - _settings.AxisLowerLimitY;
            }
            return (-_settings.AxisLimitX - oneArcSec <= target[0] && target[0] <= _settings.AxisLimitX + oneArcSec) &&
                   (axisLowerLimitY <= target[1] && target[1] <= axisUpperLimitY);
        }

        #endregion
    }
}
