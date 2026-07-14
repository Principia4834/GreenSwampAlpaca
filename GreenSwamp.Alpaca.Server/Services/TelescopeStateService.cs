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
    /// On each 200 ms tick the service snapshots every registered device into an
    /// internal cache and then fires <see cref="StateChanged"/>. Subscribers call
    /// <see cref="GetCurrentState"/> to read the pre-built snapshot — no mount
    /// properties are read by the caller.
    /// </summary>
    public class TelescopeStateService : IDisposable
    {
        private readonly Timer _updateTimer;

        // Keyed by device number. Replaced atomically each tick; never mutated in place.
        private Dictionary<int, TelescopeStateModel> _cache = new();

        /// <summary>
        /// Fired every 200 ms after the internal snapshot cache has been refreshed.
        /// </summary>
        public event EventHandler? StateChanged;

        public TelescopeStateService()
        {
            // Infinite period: the next tick is only scheduled after the current
            // callback finishes (see finally block in OnTimerTick). This prevents
            // System.Threading.Timer re-entrancy when subscribers take >= 200 ms.
            _updateTimer = new Timer(OnTimerTick, null,
                TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);
        }

        private void OnTimerTick(object? state)
        {
            try
            {
                // Build a fresh snapshot for every registered device first, then notify
                // subscribers. This ensures GetCurrentState() is always a cheap cache read
                // regardless of how many subscribers call it.
                var instances = MountRegistry.GetAllInstances();
                var next = new Dictionary<int, TelescopeStateModel>(instances.Count);
                foreach (var dn in instances.Keys)
                    next[dn] = BuildSnapshot(dn);

                _cache = next;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                // Reschedule only after the callback completes, guaranteeing no overlap.
                _updateTimer.Change(TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Returns the most recent cached snapshot for the given device number.
        /// Returns an empty model when the device is not registered.
        /// This is always a dictionary lookup — no mount properties are read.
        /// </summary>
        public TelescopeStateModel GetCurrentState(int deviceNumber = 0) =>
            _cache.TryGetValue(deviceNumber, out var cached) ? cached : new TelescopeStateModel();

        /// <summary>
        /// Reads all mount properties and builds a new snapshot.
        /// Called once per device per tick, inside OnTimerTick.
        /// </summary>
        private static TelescopeStateModel BuildSnapshot(int deviceNumber)
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
                    LimitsOn = mount.LimitsOn,
                    LimitWarningActive = mount.LimitWarningActive,
                    LimitWarningMessage = mount.LimitWarningMessage,
                    LimitWarningSequence = mount.LimitWarningSequence,
                    AtPark = mount.AtPark,
                    AtHome = mount.AtHome,
                    IsMountRunning = mount.IsMountRunning,
                    ComPort = mount.Settings.Port ?? string.Empty,
                    ConnectedClientCount = mount.ConnectedClientCount,
                    HasEverBeenConnected = mount.HasEverBeenConnected,
                    ParkSelectedName = mount.ParkSelected?.Name,
                    ParkPositionNames = mount.Settings.ParkPositions?.Select(p => p.Name).ToList() ?? new List<string>(),
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
                    LastUpdate = DateTime.UtcNow,
                    ControllerVoltage = mount.ControllerVoltage,
                    LowVoltageEvent = mount.LowVoltageEvent,
                    EnableVoice = mount.EnableVoice,
                    VoiceActive = mount.Settings.VoiceActive,
                    VoiceName = mount.Settings.VoiceName,
                    VoiceVolume = mount.Settings.VoiceVolume,
                    IsAutoHomeRunning = mount.IsAutoHomeRunning,
                    AutoHomeProgressBar = mount.AutoHomeProgressBar,
                    IsGermanPolarMode = mount.Settings.AlignmentMode == ASCOM.Common.DeviceInterfaces.AlignmentMode.GermanPolar,
                    FlipOnNextGoto = mount.FlipOnNextGoto,
                    AutoHomeAxisX = mount.Settings.AutoHomeAxisX,
                    AutoHomeAxisY = mount.Settings.AutoHomeAxisY,
                    StepsPerRevolution = mount.StepsPerRevolution,
                    StepsWormPerRevolution = mount.StepsWormPerRevolution,
                    StepsTimeFreq = mount.StepsTimeFreq,
                    TrackingOffsetRate = mount.TrackingOffsetRate,
                    CanPPec = mount.CanPPec,
                    CanHomeSensor = mount.CanHomeSensor,
                    CanPolarLed = mount.CanPolarLed,
                    CanAdvancedCmdSupport = mount.CanAdvancedCmdSupport,
                    MountName = mount.MountName,
                    MountVersion = mount.MountVersion,
                    Capabilities = mount.Capabilities
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
