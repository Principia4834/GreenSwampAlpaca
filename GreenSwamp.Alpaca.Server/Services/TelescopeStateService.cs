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
        /// Builds a state snapshot directly from the per-instance MountInstance.
        /// Returns an empty model when the device number is not registered.
        /// </summary>
        public TelescopeStateModel GetCurrentState(int deviceNumber = 0)
        {
            try
            {
                var inst = MountInstanceRegistry.GetInstance(deviceNumber);
                if (inst == null) return new TelescopeStateModel();

                return new TelescopeStateModel
                {
                    Altitude = inst.Altitude,
                    Azimuth = inst.Azimuth,
                    Declination = inst.Declination,
                    RightAscension = inst.RightAscension,
                    SideOfPier = inst.SideOfPier,
                    LocalHourAngle = inst.Lha,
                    UTCDate = HiResDateTime.UtcNow,
                    LocalDate = DateTime.Now,
                    Slewing = inst.IsSlewing,
                    Tracking = inst.Tracking,
                    AtPark = inst.AtPark,
                    AtHome = inst.AtHome,
                    IsMountRunning = inst.IsMountRunning,
                    TargetRightAscension = inst.TargetRa,
                    TargetDeclination = inst.TargetDec,
                    ActualAxisX = inst.ActualAxisX,
                    ActualAxisY = inst.ActualAxisY,
                    AppAxisX = inst.AppAxisX,
                    AppAxisY = inst.AppAxisY,
                    Axis1Steps = inst.Steps?[0] ?? 0,
                    Axis2Steps = inst.Steps?[1] ?? 0,
                    TrackingRate = DriveRate.Sidereal,
                    IsPulseGuidingRa = inst.IsPulseGuidingRa,
                    IsPulseGuidingDec = inst.IsPulseGuidingDec,
                    SlewState = inst.SlewState,
                    LoopCounter = inst.LoopCounter,
                    TimerOverruns = inst.TimerOverruns,
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
