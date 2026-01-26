/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)
   
   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Encapsulates a pulse guide operation with all necessary parameters.
    /// Calculates guide rates and backlash compensation based on direction and settings.
    /// </summary>
    internal class PulseGuideOperation
    {
        public GuideDirection Direction { get; }
        public int Duration { get; }
        public double AltRate { get; }
        public Axis Axis { get; }

        public PulseGuideOperation(
            GuideDirection direction,
            int duration,
            double altRate,
            Axis axis)
        {
            Direction = direction;
            Duration = duration;
            AltRate = altRate;
            Axis = axis;
        }

        /// <summary>
        /// Calculate guide rate based on direction and settings.
        /// Applies hemisphere and pier side corrections.
        /// </summary>
        public double GuideRate
        {
            get
            {
                var useAltRate = Math.Abs(AltRate) > 0;
                var settings = SkyServer.Settings;
                if (settings == null)
                {
                    throw new InvalidOperationException("SkyServer settings not initialized");
                }

                double rate;

                switch (Direction)
                {
                    case GuideDirection.North:
                    case GuideDirection.South:
                        rate = useAltRate ? AltRate : Math.Abs(SkyServer.GuideRateDec);
                        rate = ApplyDecDirectionAndMode(rate, settings);
                        break;

                    case GuideDirection.East:
                    case GuideDirection.West:
                        rate = useAltRate ? AltRate : Math.Abs(SkyServer.GuideRateRa);
                        rate = ApplyRaDirectionAndMode(rate, settings);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(Direction), Direction, null);
                }

                return rate;
            }
        }

        /// <summary>
        /// Calculate backlash compensation steps if direction changed.
        /// Only applies to SkyWatcher Dec axis.
        /// </summary>
        /// <param name="lastDirectionRa">Last RA direction (from controller)</param>
        /// <param name="lastDirectionDec">Last Dec direction (from controller)</param>
        /// <param name="newLastDirectionRa">OUT: Updated RA direction</param>
        /// <param name="newLastDirectionDec">OUT: Updated Dec direction</param>
        public int CalculateBacklashSteps(
            GuideDirection? lastDirectionRa,
            GuideDirection? lastDirectionDec,
            out GuideDirection? newLastDirectionRa,
            out GuideDirection? newLastDirectionDec)
        {
            var settings = SkyServer.Settings;
            if (settings == null || settings.Mount != MountType.SkyWatcher)
            {
                newLastDirectionRa = Direction;
                newLastDirectionDec = Direction;
                return 0;
            }

            int backlash = 0;

            if (Axis == Axis.Axis1) // RA
            {
                if (lastDirectionRa.HasValue && lastDirectionRa.Value != Direction)
                {
                    // Direction changed - RA typically has no backlash for pulse guiding
                    backlash = 0;
                }

                newLastDirectionRa = Direction;
                newLastDirectionDec = lastDirectionDec;
            }
            else // Dec
            {
                if (lastDirectionDec.HasValue && lastDirectionDec.Value != Direction)
                {
                    // Direction changed - apply Dec backlash
                    backlash = settings.DecBacklash;
                }

                newLastDirectionRa = lastDirectionRa;
                newLastDirectionDec = Direction;
            }

            return backlash;
        }

        private double ApplyDecDirectionAndMode(double rate, SkySettingsInstance settings)
        {
            switch (settings.AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    if (Direction == GuideDirection.South)
                    {
                        rate = -rate;
                    }

                    break;

                case AlignmentMode.Polar:
                    if (SkyServer.SideOfPier == PointingState.Normal)
                    {
                        if (Direction == GuideDirection.North)
                        {
                            rate = -rate;
                        }
                    }
                    else
                    {
                        if (Direction == GuideDirection.South)
                        {
                            rate = -rate;
                        }
                    }

                    if (settings.PolarMode == PolarMode.Left)
                    {
                        rate = -rate;
                    }

                    break;

                case AlignmentMode.GermanPolar:
                    if (SkyServer.SideOfPier == PointingState.Normal)
                    {
                        if (Direction == GuideDirection.North)
                        {
                            rate = -rate;
                        }
                    }
                    else
                    {
                        if (Direction == GuideDirection.South)
                        {
                            rate = -rate;
                        }
                    }

                    break;
            }

            return rate;
        }

        private double ApplyRaDirectionAndMode(double rate, SkySettingsInstance settings)
        {
            if (settings.AlignmentMode != AlignmentMode.AltAz)
            {
                if (SkyServer.SouthernHemisphere)
                {
                    if (Direction == GuideDirection.West)
                    {
                        rate = -rate;
                    }
                }
                else
                {
                    if (Direction == GuideDirection.East)
                    {
                        rate = -rate;
                    }
                }
            }
            else
            {
                if (Direction == GuideDirection.East)
                {
                    rate = -rate;
                }
            }

            return rate;
        }
    }
}