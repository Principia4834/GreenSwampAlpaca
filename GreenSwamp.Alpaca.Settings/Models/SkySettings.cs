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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Mount settings - all defaults come from appsettings.json
    /// </summary>
    public class SkySettings
    {
        [Required]
        public string Mount { get; set; } = null!;
        
        [Required]
        public string Port { get; set; } = null!;
        
        public int BaudRate { get; set; }
        
        [Range(-90, 90)]
        public double Latitude { get; set; }
        
        [Range(-180, 180)]
        public double Longitude { get; set; }
        
        [Range(-500, 9000)]
        public double Elevation { get; set; }
        
        public bool AutoTrack { get; set; }
        
        public string AlignmentMode { get; set; } = null!;
        
        public string EquatorialCoordinateType { get; set; } = null!;
        
        public bool Refraction { get; set; }
        
        public double Temperature { get; set; }
        
        public double Pressure { get; set; }
        
        // Custom gearing
        public bool CustomGearing { get; set; }
        public int CustomRa360Steps { get; set; }
        public int CustomRaWormTeeth { get; set; }
        public int CustomDec360Steps { get; set; }
        public int CustomDecWormTeeth { get; set; }
        
        // Backlash
        public int RaBacklash { get; set; }
        public int DecBacklash { get; set; }
        
        // Pulse guide settings
        public int MinPulseDurationRa { get; set; }
        public int MinPulseDurationDec { get; set; }
        public bool DecPulseGoTo { get; set; }
        
        // Advanced settings
        public bool UseAdvancedCommands { get; set; }
        public bool MonitorPulse { get; set; }
        public bool AlternatingPPec { get; set; }
        
        // Rate settings
        public double CustomRateRa { get; set; }
        public double CustomRateDec { get; set; }
        
        // Tracking
        public string TrackingMode { get; set; } = null!;
        public double TrackingRate { get; set; }
        
        // Limits
        public bool UseLimits { get; set; }
        public double AltitudeLimit { get; set; }
        public double HorizonLimit { get; set; }
    }
}
