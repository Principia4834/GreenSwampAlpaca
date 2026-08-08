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
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Models;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Event arguments for a mount notification (voice announcement or limit warning).
    /// </summary>
    public sealed class MountNotificationEventArgs : EventArgs
    {
        public int DeviceNumber { get; init; }
        public string Text { get; init; } = string.Empty;
        /// <summary>True when voice is both enabled and active for this device.</summary>
        public bool IsVoiceEnabled { get; init; }
        public string VoiceName { get; init; } = string.Empty;
        public int VoiceVolume { get; init; }
        /// <summary>True when this notification should be shown as a snackbar warning.</summary>
        public bool IsLimitWarning { get; init; }
        /// <summary>True when this notification should be shown as an error snackbar for a connection failure.</summary>
        public bool IsConnectionError { get; init; }
    }

    /// <summary>
    /// Singleton service that detects telescope state transitions and raises
    /// <see cref="NotificationRequested"/> so any subscribed UI component can
    /// speak or display alerts — regardless of the current page.
    /// </summary>
    public sealed class MountNotificationService : IDisposable
    {
        private readonly TelescopeStateService _stateService;

        private readonly record struct DeviceNotificationState(
            bool AtPark,
            bool Slewing,
            bool AtHome,
            bool IsConnected,
            bool Tracking,
            long LimitWarningSequence,
            PointingState SideOfPier,
            string? LastConnectionError);

        private readonly Dictionary<int, DeviceNotificationState> _lastState = [];

        /// <summary>
        /// Raised on a background thread whenever a noteworthy state transition occurs.
        /// Subscribers must marshal to the UI thread (e.g. via <c>InvokeAsync</c>).
        /// </summary>
        public event EventHandler<MountNotificationEventArgs>? NotificationRequested;

        public MountNotificationService(TelescopeStateService stateService)
        {
            ArgumentNullException.ThrowIfNull(stateService);
            _stateService = stateService;
            _stateService.StateChanged += OnStateChanged;
        }

        private static bool IsUiClientConnected(int deviceNumber) =>
            MountRegistry.GetInstance(deviceNumber)?.IsClientConnected(GreenSwamp.Alpaca.MountControl.Mount.UiInternalClientId) ?? false;

        private void OnStateChanged(object? sender, EventArgs e)
        {
            foreach (var dn in MountRegistry.GetAllInstances().Keys)
            {
                var state = _stateService.GetCurrentState(dn);
                var isUiClinetConnected = IsUiClientConnected(dn);
                var connectionError = MountRegistry.GetInstance(dn)?.LastConnectionError;

                if (!_lastState.TryGetValue(dn, out var last))
                {
                    // Prime state on first tick — no events fired.
                    _lastState[dn] = new DeviceNotificationState(
                        state.AtPark, state.Slewing, state.AtHome,
                        state.ConnectedClientCount > 0, state.Tracking, state.LimitWarningSequence, state.SideOfPier, connectionError);
                    continue;
                }

                var connectedUp = !last.IsConnected && state.ConnectedClientCount > 0;
                var connectedDn = last.IsConnected && state.ConnectedClientCount == 0;

                var atParkUp = !last.AtPark && state.AtPark;
                var atParkDn = last.AtPark && !state.AtPark;

                var slewingUp = !last.Slewing && state.Slewing;
                var slewingDn = last.Slewing && !state.Slewing;

                var trackingUp = !last.Tracking && state.Tracking;
                var trackingDn = last.Tracking && !state.Tracking;

                var atHomeUp = !last.AtHome && state.AtHome;
                var atHomeDn = last.AtHome && !state.AtHome;

                var pierSideChange = state.SideOfPier != last.SideOfPier;

                // Silence when autohomne is in progress
                var voiceEnabled = state.VoiceActive && !state.IsAutoHomeRunning;

                CheckAndNotify(atParkUp,
                    "Parked", dn, voiceEnabled, state);
                CheckAndNotify(atParkDn,
                    "Unparked", dn, voiceEnabled, state);

                if (!state.Slewing)
                {
                    CheckAndNotify(trackingUp,
                        "Tracking on", dn, voiceEnabled, state);
                    CheckAndNotify(trackingDn,
                        "Tracking off", dn, voiceEnabled, state);
                }

                CheckAndNotify(slewingUp,
                    "Started Slewing", dn, voiceEnabled, state);
                CheckAndNotify(slewingDn && !state.AtPark && !state.AtHome,
                    "Finished Slewing", dn, voiceEnabled, state);

                CheckAndNotify(atHomeUp,
                    "At Home", dn, voiceEnabled, state);

                CheckAndNotify(pierSideChange,
                    $"Pier side {state.SideOfPier}", dn, voiceEnabled, state);

                CheckAndNotify(connectedDn,
                    "Mount Disconnected", dn, voiceEnabled, state);
                CheckAndNotify(connectedUp,
                    "Mount Connected", dn, voiceEnabled, state);

                // Condition: Transition from not Slewing to Slewing (i.e., started slewing)
                //if(slewingUp)
                //{
                //    switch (state.SlewState)
                //    {
                //        case SlewType.SlewAltAz:
                //        case SlewType.SlewRaDec:
                //            CheckAndNotify(true, "Slewing to coordinates", dn, voiceEnabled, state);
                //            break;
                //        case SlewType.SlewHome:
                //            CheckAndNotify(true, "Slewing to home", dn, voiceEnabled, state);
                //            break;
                //        case SlewType.SlewPark:
                //            CheckAndNotify(true, "Slewing to park", dn, voiceEnabled, state);
                //            break;
                //        default:
                //            CheckAndNotify(true, "Started Slewing", dn, voiceEnabled, state);
                //            break;
                //    }
                //}

                if (state.LimitWarningSequence > last.LimitWarningSequence
                    && !string.IsNullOrWhiteSpace(state.LimitWarningMessage))
                {
                    NotificationRequested?.Invoke(this, new MountNotificationEventArgs
                    {
                        DeviceNumber = dn,
                        Text = state.LimitWarningMessage,
                        IsVoiceEnabled = false,
                        VoiceName = state.VoiceName,
                        VoiceVolume = state.VoiceVolume,
                        IsLimitWarning = true
                    });
                }

                // Fire once when a new connection error message appears.
                if (!string.IsNullOrEmpty(connectionError) && connectionError != last.LastConnectionError)
                {
                    NotificationRequested?.Invoke(this, new MountNotificationEventArgs
                    {
                        DeviceNumber = dn,
                        Text = $"Connection failed: {connectionError}",
                        IsVoiceEnabled = false,
                        IsConnectionError = true,
                        VoiceName = state.VoiceName,
                        VoiceVolume = state.VoiceVolume,
                    });
                }

                _lastState[dn] = new DeviceNotificationState(
                    state.AtPark, state.Slewing, state.AtHome,
                    state.ConnectedClientCount > 0, state.Tracking, state.LimitWarningSequence, state.SideOfPier, connectionError);
            }


        }

        private void CheckAndNotify(
            bool condition, string text, int deviceNumber,
            bool voiceEnabled, TelescopeStateModel state)
        {
            if (!condition) return;

            NotificationRequested?.Invoke(this, new MountNotificationEventArgs
            {
                DeviceNumber = deviceNumber,
                Text = text,
                IsVoiceEnabled = voiceEnabled,
                VoiceName = state.VoiceName,
                VoiceVolume = state.VoiceVolume,
                IsLimitWarning = false
            });
        }

        public void Dispose() => _stateService.StateChanged -= OnStateChanged;
    }
}
