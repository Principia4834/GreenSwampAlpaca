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

using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Mount initialization: defaults, home/park positions, mount reset, and low-voltage handling.
    /// Manages per-instance mount startup configuration and state initialization.
    /// </summary>
    public partial class Mount
    {
        /// <summary>
        /// Load default settings and slew rates
        /// Migrated from SkyServer.Defaults()
        /// </summary>
        private void Defaults()
        {
            _slewSettleTime = 0;

            // Initialize FactorStep array (already initialized in constructor, but keep for compatibility)
            // _factorStep is already initialized as new double[2]

            // home axes
            _homeAxes = GetHomeAxes(Settings.HomeAxisX, Settings.HomeAxisY);

            // Initialize ParkSelected from ParkName setting
            if (!string.IsNullOrEmpty(Settings.ParkName))
            {
                var found = Settings.ParkPositions.Find(x => x.Name == Settings.ParkName);
                if (found != null)
                {
                    _parkSelected = found;
                    // Also ensure ParkAxes is populated for backward compatibility
                    Settings.ParkAxes = [found.X, found.Y];
                }
            }

            // Set slew rates for all speed levels
            // SetSlewRates expects degrees/second and stores values in degrees
            // (hardware layer converts to radians when needed)
            this.SetSlewRates(Settings.MaxSlewRate);

            // set the guiderates
            _guideRate = new Vector(Settings.GuideRateOffsetY, Settings.GuideRateOffsetX);
            this.SetGuideRates();
        }

        /// <summary>
        /// Reset mount to home position
        /// Migrated from SkyServer.MountReset()
        /// </summary>
        public void MountReset()
        {
            // Set home positions using current settings (already loaded)
            _homeAxes = GetHomeAxes(Settings.HomeAxisX, Settings.HomeAxisY);

            // Set axis positions
            _appAxes = new Vector(_homeAxes.X, _homeAxes.Y);
        }

        // Expose internal state for static facade backward compatibility
        internal Vector HomeAxes => _homeAxes;
        internal Vector AppAxes => _appAxes;

        /// <summary>
        /// Low voltage event handler
        /// </summary>
        private void OnLowVoltageEvent(object sender, EventArgs e)
        {
            _lowVoltageEventState = true;
            LogMount(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Mount,
                Type = MonitorType.Error,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = "Mount detected low voltage: check power supply and wiring"
            });
        }
    }
}
