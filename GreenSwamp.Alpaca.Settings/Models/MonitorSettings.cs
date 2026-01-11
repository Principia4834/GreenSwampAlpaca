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

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Monitor settings model for JSON serialization
    /// Represents logging and monitoring filter configuration
    /// </summary>
    public class MonitorSettings
    {
        #region MonitorDevice Filters (3 properties)

        /// <summary>
        /// Enable monitoring for server device entries
        /// </summary>
        public bool ServerDevice { get; set; } = true;

        /// <summary>
        /// Enable monitoring for telescope device entries
        /// </summary>
        public bool Telescope { get; set; } = true;

        /// <summary>
        /// Enable monitoring for UI device entries
        /// </summary>
        public bool Ui { get; set; } = false;

        #endregion

        #region MonitorCategory Filters (6 properties)

        /// <summary>
        /// Enable monitoring for 'Other' category entries (support/shared projects)
        /// </summary>
        public bool Other { get; set; } = false;

        /// <summary>
        /// Enable monitoring for 'Driver' category entries (simulator and SkyWatcher data)
        /// </summary>
        public bool Driver { get; set; } = true;

        /// <summary>
        /// Enable monitoring for 'Interface' category entries
        /// </summary>
        public bool Interface { get; set; } = true;

        /// <summary>
        /// Enable monitoring for 'Server' category entries (core server processes)
        /// </summary>
        public bool Server { get; set; } = true;

        /// <summary>
        /// Enable monitoring for 'Mount' category entries (simulator and SkyWatcher commands)
        /// </summary>
        public bool Mount { get; set; } = true;

        /// <summary>
        /// Enable monitoring for 'Alignment' category entries
        /// </summary>
        public bool Alignment { get; set; } = false;

        #endregion

        #region MonitorType Filters (5 properties)

        /// <summary>
        /// Enable monitoring for 'Information' type entries (also written to session log)
        /// </summary>
        public bool Information { get; set; } = true;

        /// <summary>
        /// Enable monitoring for 'Data' type entries (core information)
        /// </summary>
        public bool Data { get; set; } = false;

        /// <summary>
        /// Enable monitoring for 'Warning' type entries (also written to session log)
        /// </summary>
        public bool Warning { get; set; } = false;

        /// <summary>
        /// Enable monitoring for 'Error' type entries (also written to error and session logs)
        /// </summary>
        public bool Error { get; set; } = false;

        /// <summary>
        /// Enable monitoring for 'Debug' type entries (troubleshooting data)
        /// </summary>
        public bool Debug { get; set; } = false;

        #endregion

        #region Logging Options (4 properties)

        /// <summary>
        /// Enable logging monitor entries to file (GSMonitorLog)
        /// Recommended: true for development, false for production
        /// </summary>
        public bool LogMonitor { get; set; } = true;

        /// <summary>
        /// Enable logging session entries to file (Information, Warning, Error types)
        /// Always written to GSSessionLog file
        /// </summary>
        public bool LogSession { get; set; } = true;

        /// <summary>
        /// Enable logging charting data to file
        /// </summary>
        public bool LogCharting { get; set; } = false;

        /// <summary>
        /// Start the monitor window automatically and enable file logging
        /// Must be true for LogMonitor to write files
        /// </summary>
        public bool StartMonitor { get; set; } = true;

        #endregion

        #region Miscellaneous (3 properties)

        /// <summary>
        /// UI language code (e.g., "en-US")
        /// </summary>
        public string Language { get; set; } = "en-US";

        /// <summary>
        /// Custom log file path (0 = use default)
        /// </summary>
        public string LogPath { get; set; } = "0";

        /// <summary>
        /// Settings version identifier for upgrades
        /// </summary>
        public string Version { get; set; } = "0";

        #endregion
    }
}
