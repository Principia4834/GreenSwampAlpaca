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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Observatory physical properties — content of observatory.settings.json.
    /// This is the single source of truth for observatory properties at device-creation
    /// time (Behaviour B1). Changes here do not automatically propagate to existing
    /// device-nn.settings.json files (v1; see TODO in SaveObservatorySettingsAsync).
    /// </summary>
    public class ObservatorySettings
    {
        [Range(-90, 90)]
        public double Latitude { get; set; } = 51.21135;

        [Range(-180, 180)]
        public double Longitude { get; set; } = -1.459816;

        [Range(-500, 9000)]
        public double Elevation { get; set; } = 10.0;

        public TimeSpan UTCOffset { get; set; } = TimeSpan.Zero;
    }
}
