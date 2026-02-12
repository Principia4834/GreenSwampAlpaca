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
    /// Service that provides telescope state updates synchronized with SkyServer main loop
    /// Implements observer pattern for real-time UI updates
    /// </summary>
    public class TelescopeStateService : IDisposable
    {
        private readonly Timer _updateTimer;
        private TelescopeStateModel _currentState;
        private readonly object _stateLock = new object();
        
        /// <summary>
        /// Event fired when telescope state is updated (synchronized with SkyServer loop)
        /// </summary>
        public event EventHandler<TelescopeStateModel>? StateChanged;
        
        /// <summary>
        /// Constructor initializes the service and subscribes to SkyServer property changes
        /// </summary>
        public TelescopeStateService()
        {
            _currentState = new TelescopeStateModel();
            
            // Subscribe to SkyServer static property changes
            SkyServer.StaticPropertyChanged += OnSkyServerPropertyChanged;
            
            // Create a backup timer (100ms) in case property change events are missed
            _updateTimer = new Timer(UpdateState, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }
        
        /// <summary>
        /// Handler for SkyServer property changes - updates state and notifies subscribers
        /// </summary>
        private void OnSkyServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update on key property changes that indicate server loop update
            if (e.PropertyName == nameof(SkyServer.LoopCounter) || 
                e.PropertyName == nameof(SkyServer.Steps) ||
                e.PropertyName == null) // null means multiple properties changed
            {
                UpdateState(null);
            }
        }
        
        /// <summary>
        /// Updates the current telescope state from SkyServer
        /// </summary>
        private void UpdateState(object? state)
        {
            try
            {
                var newState = new TelescopeStateModel
                {
                    // Core positioning
                    Altitude = SkyServer.Altitude,
                    Azimuth = SkyServer.Azimuth,
                    Declination = SkyServer.Declination,
                    RightAscension = SkyServer.RightAscension,
                    
                    // Pier side and timing
                    SideOfPier = SkyServer.SideOfPier,
                    LocalHourAngle = SkyServer.Lha,
                    UTCDate = HiResDateTime.UtcNow,
                    LocalDate = DateTime.Now,
                    
                    // Mount state
                    Slewing = SkyServer.IsSlewing,
                    Tracking = SkyServer.Tracking,
                    AtPark = SkyServer.AtPark,
                    AtHome = SkyServer.IsHome,
                    IsMountRunning = SkyServer.IsMountRunning,
                    
                    // Target information
                    TargetRightAscension = SkyServer.TargetRa,
                    TargetDeclination = SkyServer.TargetDec,
                    
                    // Axis positions - mount coordinates (internal) and app coordinates (local)
                    ActualAxisX = SkyServer.ActualAxisX,
                    ActualAxisY = SkyServer.ActualAxisY,
                    AppAxisX = SkyServer.AppAxisX,
                    AppAxisY = SkyServer.AppAxisY,

                    // Axis step positions
                    Axis1Steps = SkyServer.Steps?[0] ?? 0,
                    Axis2Steps = SkyServer.Steps?[1] ?? 0,

                    // Rates and guides
                    TrackingRate = DriveRate.Sidereal,  // Use constant for now
                    IsPulseGuidingRa = SkyServer.IsPulseGuidingRa,
                    IsPulseGuidingDec = SkyServer.IsPulseGuidingDec,
                    
                    // Slew state
                    SlewState = SkyServer.SlewState,
                    
                    // Performance
                    LoopCounter = SkyServer.LoopCounter,
                    TimerOverruns = 0,  // Not publicly accessible
                    
                    LastUpdate = DateTime.UtcNow
                };
                
                lock (_stateLock)
                {
                    _currentState = newState;
                }
                
                // Notify subscribers on thread pool to avoid blocking
                Task.Run(() => StateChanged?.Invoke(this, newState));
            }
            catch (Exception)
            {
                // Silently handle errors to prevent service failure
                // Could log here if needed
            }
        }
        
        /// <summary>
        /// Gets the current telescope state (thread-safe)
        /// </summary>
        public TelescopeStateModel GetCurrentState()
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
        
        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            SkyServer.StaticPropertyChanged -= OnSkyServerPropertyChanged;
            _updateTimer?.Dispose();
        }
    }
}
