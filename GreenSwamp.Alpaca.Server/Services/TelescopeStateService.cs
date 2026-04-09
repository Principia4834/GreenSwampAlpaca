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

using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.Models;
using ASCOM.Common.DeviceInterfaces;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Service that provides telescope state snapshots for the Blazor UI.
    /// Fires a tick event on a 100 ms timer; callers read per-device state via GetCurrentState(deviceNumber).
    /// </summary>
    public class TelescopeStateService : IDisposable
    {
        private readonly Timer _updateTimer;

        /// <summary>
        /// Fired every 100 ms so Blazor pages can call StateHasChanged().
        /// </summary>
        public event EventHandler? StateChanged;

        public TelescopeStateService()
        {
            _updateTimer = new Timer(OnTimerTick, null,
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }

        private void OnTimerTick(object? state) =>
            StateChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Builds a state snapshot directly from the per-instance Mount.
        /// Returns an empty model when the device number is not registered.
        /// </summary>
        public TelescopeStateModel GetCurrentState(int deviceNumber = 0)
        {
            try
            {
                var mount = MountRegistry.GetInstance(deviceNumber);
                if (mount == null) return new TelescopeStateModel();

                return new TelescopeStateModel
                {
                    Altitude = mount.Altitude,
                    Azimuth = mount.Azimuth,
                    Declination = mount.Declination,
                    RightAscension = mount.RightAscension,
                    SideOfPier = mount.SideOfPier,
                    LocalHourAngle = mount.Lha,
                    UTCDate = HiResDateTime.UtcNow,
                    LocalDate = DateTime.Now,
                    Slewing = mount.IsSlewing,
                    Tracking = mount.Tracking,
                    AtPark = mount.AtPark,
                    AtHome = mount.AtHome,
                    IsMountRunning = mount.IsMountRunning,
                    TargetRightAscension = mount.TargetRa,
                    TargetDeclination = mount.TargetDec,
                    ActualAxisX = mount.ActualAxisX,
                    ActualAxisY = mount.ActualAxisY,
                    AppAxisX = mount.AppAxisX,
                    AppAxisY = mount.AppAxisY,
                    Axis1Steps = mount.Steps?[0] ?? 0,
                    Axis2Steps = mount.Steps?[1] ?? 0,
                    TrackingRate = DriveRate.Sidereal,
                    IsPulseGuidingRa = mount.IsPulseGuidingRa,
                    IsPulseGuidingDec = mount.IsPulseGuidingDec,
                    SlewState = mount.SlewState,
                    LoopCounter = mount.LoopCounter,
                    TimerOverruns = mount.TimerOverruns,
                    LastUpdate = DateTime.UtcNow
                };
            }
            catch (Exception)
            {
                return new TelescopeStateModel();
            }
        }

        public void Dispose() => _updateTimer?.Dispose();
    }
}
