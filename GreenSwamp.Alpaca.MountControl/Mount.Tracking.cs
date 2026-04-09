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

// Per-instance tracking and rate methods.
// Replaces SkyServer.SetTracking(this), SetGuideRates(this), CalcCustomTrackingOffset(this),
// SetSlewRates(rate, this). SkyGetRate() fixes the device-0 AlignmentMode bias.

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Represents a per-device instance of a telescope mount controller, implementing the
    /// <see cref="IMountController"/> interface for both SkyWatcher hardware and the built-in simulator.
    /// <para>
    /// Each <see cref="Mount"/> owns its full lifecycle — serial/UDP connection, hardware command
    /// queues (<see cref="SkyQueue"/> / <see cref="SimQueue"/>), coordinate pipeline,
    /// tracking timers, slew controller, and all associated cancellation tokens — so that multiple
    /// physical devices can operate concurrently without shared state.
    /// </para>
    /// <para>
    /// Key responsibilities:
    /// <list type="bullet">
    ///   <item><description>Connects to and disconnects from mount hardware (COM port or UDP/WiFi).</description></item>
    ///   <item><description>Runs the per-tick update loop (<see cref="OnUpdateServerEvent"/>) that converts
    ///   raw hardware step counts to topocentric RA/Dec, Alt/Az, and app-axis coordinates.</description></item>
    ///   <item><description>Manages sidereal, AltAz, and custom tracking modes via
    ///   <see cref="ApplyTracking"/> and the per-instance AltAz tracking timer.</description></item>
    ///   <item><description>Executes GoTo slews (coarse + precision pass) for RA/Dec, Alt/Az,
    ///   Home, and Park targets through <see cref="SlewAsync"/> / <see cref="SlewSync"/>.</description></item>
    ///   <item><description>Handles pulse guiding (equatorial and AltAz predictor-based) with
    ///   per-instance cancellation tokens to prevent cross-device interference.</description></item>
    ///   <item><description>Enforces meridian and horizon axis limits and reacts with configurable
    ///   stop-tracking or auto-park responses.</description></item>
    ///   <item><description>Exposes ASCOM-compliant properties and bridge methods consumed by
    ///   <c>Telescope.cs</c> and the Blazor UI without routing through the static <c>SkyServer</c> façade.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This is a partial class; additional members are defined in
    /// <c>Mount.Tracking.cs</c> and related sibling files.
    /// </para>
    /// </summary>
    public partial class Mount
    {
        #region Tracking and Rate Methods (Phase M1)

        private const double SiderealRate = 15.0410671786691;

        internal void SetTracking()
        {
            if (!IsMountRunning) return;

            double rateChange = 0;
            Vector rate = default;
            var currentTrackingMode = TrackingMode;
            switch (currentTrackingMode)
            {
                case TrackingMode.Off:
                    break;
                case TrackingMode.AltAz:
                    rateChange = SkyServer.CurrentTrackingRate(this);
                    break;
                case TrackingMode.EqN:
                    rateChange = SkyServer.CurrentTrackingRate(this);
                    break;
                case TrackingMode.EqS:
                    rateChange = -SkyServer.CurrentTrackingRate(this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    switch (Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SkyServer.SetAltAzTrackingRates(AltAzTrackingType.Predictor, this);
                                if (_altAzTrackingTimer?.IsRunning != true) StartAltAzTrackingTimer();
                            }
                            else
                            {
                                if (_altAzTrackingTimer?.IsRunning == true) StopAltAzTrackingTimer();
                                _skyTrackingRate = new Vector(0, 0);
                            }
                            rate = SkyGetRate();
                            {
                                var mq = SimQueue;
                                if (mq == null) return;
                                if (_rateMoveAxes.X == 0.0)
                                    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rate.X);
                                if (_rateMoveAxes.Y == 0.0)
                                    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis2, rate.Y);
                            }
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            {
                                var mq = SimQueue!;
                                if (_rateMoveAxes.X == 0.0)
                                    _ = new CmdAxisTracking(mq.NewId, mq, Axis.Axis1, rateChange);
                                var raRate = currentTrackingMode != TrackingMode.Off
                                    ? SkyServer.GetRaRateDirection(RateRa, Settings) : 0.0;
                                _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis1, raRate);
                                if (_rateMoveAxes.Y == 0.0)
                                {
                                    var decRate = currentTrackingMode != TrackingMode.Off
                                        ? SkyServer.GetDecRateDirection(RateDec, this) : 0.0;
                                    _ = new CmdRaDecRate(mq.NewId, mq, Axis.Axis2, decRate);
                                }
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;

                case MountType.SkyWatcher:
                    switch (Settings.AlignmentMode)
                    {
                        case AlignmentMode.AltAz:
                            if (rateChange != 0)
                            {
                                SkyServer.SetAltAzTrackingRates(AltAzTrackingType.Predictor, this);
                                if (_altAzTrackingTimer?.IsRunning != true) StartAltAzTrackingTimer();
                            }
                            else
                            {
                                if (_altAzTrackingTimer?.IsRunning == true) StopAltAzTrackingTimer();
                                _skyTrackingRate = new Vector(0, 0);
                            }
                            break;
                        case AlignmentMode.Polar:
                        case AlignmentMode.GermanPolar:
                            _skyTrackingRate = new Vector(rateChange, 0);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    rate = SkyGetRate();
                    {
                        var sq = SkyQueue;
                        if (sq == null) return;
                        if (_rateMoveAxes.X == 0.0)
                            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis1, rate.X);
                        if (_rateMoveAxes.Y == 0.0)
                            _ = new SkyAxisSlew(sq.NewId, sq, Axis.Axis2, rate.Y);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (Settings.PecOn) return;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{currentTrackingMode}|{rateChange * 3600}|{_pecBinNow}|{_skyTrackingOffset[0]}|{_skyTrackingOffset[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        private Vector SkyGetRate()
        {
            var change = new Vector();
            change += _skyTrackingRate;
            change += SkyHcRate;
            change.X += _rateMoveAxes.X;
            change.X += Settings.AlignmentMode != AlignmentMode.AltAz ? SkyServer.GetRaRateDirection(RateRa, Settings) : 0;
            change.Y += _rateMoveAxes.Y;
            change.Y += Settings.AlignmentMode != AlignmentMode.AltAz ? SkyServer.GetDecRateDirection(RateDec, this) : 0;
            CheckAxisLimits();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Data,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{change}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            return change;
        }

        internal void SetGuideRates()
        {
            var rate = SkyServer.CurrentTrackingRate(this);
            GuideRateRa = rate * Settings.GuideRateOffsetX;
            GuideRateDec = rate * Settings.GuideRateOffsetY;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{GuideRateRa * 3600}|{GuideRateDec * 3600}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        internal void CalcCustomTrackingOffset()
        {
            _trackingOffsetRate = new Vector(0.0, 0.0);
            if (Settings.Mount != MountType.SkyWatcher) return;
            if (Settings.CustomGearing == false) return;

            var ratioFactor = (double)_stepsTimeFreq[0] / _stepsPerRevolution[0] * 1296000.0;
            var siderealI = ratioFactor / SiderealRate;
            siderealI += Settings.CustomRaTrackingOffset;
            var newRate = ratioFactor / siderealI;
            _trackingOffsetRate.X = SiderealRate - newRate;

            ratioFactor = (double)_stepsTimeFreq[1] / _stepsPerRevolution[1] * 1296000.0;
            siderealI = ratioFactor / SiderealRate;
            siderealI += Settings.CustomDecTrackingOffset;
            newRate = ratioFactor / siderealI;
            _trackingOffsetRate.Y = SiderealRate - newRate;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{_trackingOffsetRate.X}|{_trackingOffsetRate.Y}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        internal void SetSlewRates(double maxRate)
        {
            _slewSpeedOne   = Math.Round(maxRate * 0.0034, 3);
            _slewSpeedTwo   = Math.Round(maxRate * 0.0068, 3);
            _slewSpeedThree = Math.Round(maxRate * 0.047,  3);
            _slewSpeedFour  = Math.Round(maxRate * 0.068,  3);
            _slewSpeedFive  = Math.Round(maxRate * 0.2,    3);
            _slewSpeedSix   = Math.Round(maxRate * 0.4,    3);
            _slewSpeedSeven = Math.Round(maxRate * 0.8,    3);
            _slewSpeedEight = Math.Round(maxRate * 1.0,    3);
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{_slewSpeedOne}|{_slewSpeedTwo}|{_slewSpeedThree}|{_slewSpeedFour}|{_slewSpeedFive}|{_slewSpeedSix}|{_slewSpeedSeven}|{_slewSpeedEight}"
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion
    }
}