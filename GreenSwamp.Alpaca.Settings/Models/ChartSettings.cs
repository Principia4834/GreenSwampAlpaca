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

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// User-configurable preferences for the RA/Dec and Pulse chart windows.
    /// Persisted to <c>chart.settings.user.json</c> in the versioned settings folder.
    /// </summary>
    public class ChartSettings
    {
        // -- RA/Dec chart --------------------------------------------------------------

        /// <summary>
        /// Scale used on the RA/Dec chart Y-axis.
        /// Valid values: "Steps", "Degrees", "ArcSeconds".
        /// </summary>
        public string RaDecScale { get; set; } = "Steps";

        /// <summary>Show the Axis-1 (RA) series on the RA/Dec chart.</summary>
        public bool ShowAxis1 { get; set; } = true;

        /// <summary>Show the Axis-2 (Dec) series on the RA/Dec chart.</summary>
        public bool ShowAxis2 { get; set; } = true;

        // -- Pulse chart ---------------------------------------------------------------

        /// <summary>
        /// Scale used on the Pulse chart Y-axis.
        /// Valid values: "Milliseconds", "ArcSeconds", "Steps".
        /// </summary>
        public string PulseScale { get; set; } = "Milliseconds";

        /// <summary>Show accepted RA pulse series.</summary>
        public bool ShowRaPulse { get; set; } = true;

        /// <summary>Show rejected RA pulse series.</summary>
        public bool ShowRaRejected { get; set; } = true;

        /// <summary>Show accepted Dec pulse series.</summary>
        public bool ShowDecPulse { get; set; } = true;

        /// <summary>Show rejected Dec pulse series.</summary>
        public bool ShowDecRejected { get; set; } = true;

        // -- Shared --------------------------------------------------------------------

        /// <summary>
        /// Maximum number of data points retained per series before the oldest are dropped.
        /// Default 5000 matches the legacy GSServer chart buffer.
        /// </summary>
        public int MaxPoints { get; set; } = 5000;

        /// <summary>Automatically start disk logging when a chart window opens.</summary>
        public bool AutoStartLogging { get; set; } = false;
    }
}
