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

using ASCOM.Common.DeviceInterfaces;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Encapsulates mount configuration and state needed for axes coordinate conversions.
    /// This context object reduces parameter passing and makes method signatures cleaner.
    /// </summary>
    /// <remarks>
    /// Using a readonly struct ensures immutability and reduces heap allocations.
    /// Create context once and reuse across multiple Axes method calls.
    /// </remarks>
    public readonly struct AxesContext
    {
        #region Core Properties (Required)

        /// <summary>
        /// Alignment mode: GermanPolar, Polar, or AltAz
        /// </summary>
        public AlignmentMode AlignmentMode { get; init; }

        /// <summary>
        /// Mount hardware type: Simulator or SkyWatcher
        /// </summary>
        public MountType MountType { get; init; }

        /// <summary>
        /// Observatory latitude in degrees (positive north, negative south)
        /// </summary>
        public double Latitude { get; init; }

        /// <summary>
        /// True if observatory is in southern hemisphere
        /// </summary>
        public bool SouthernHemisphere { get; init; }

        #endregion

        #region Extended Properties (Optional)

        /// <summary>
        /// Polar mode for fork mounts: Left or Right
        /// Only used for Polar alignment mode with SkyWatcher mounts
        /// </summary>
        public PolarMode PolarMode { get; init; }

        /// <summary>
        /// Local Sidereal Time in hours (0-24)
        /// If null, will be fetched from SkyServer when needed
        /// </summary>
        public double? LocalSiderealTime { get; init; }

        /// <summary>
        /// Current side of pier state: Normal, ThroughThePole, or Unknown
        /// Used for flip calculations
        /// </summary>
        public PointingState? SideOfPier { get; init; }

        /// <summary>
        /// Current application axis X position in degrees
        /// Used by MountAxis2Mount conversion
        /// </summary>
        public double? AppAxisX { get; init; }

        /// <summary>
        /// Current application axis Y position in degrees
        /// Used by MountAxis2Mount conversion
        /// </summary>
        public double? AppAxisY { get; init; }

        /// <summary>
        /// Axis limit X in degrees (for flip angle calculations)
        /// Used by AltAz and Polar alignment modes
        /// </summary>
        public double? AxisLimitX { get; init; }

        /// <summary>
        /// Hour angle limit in degrees (for GEM flip calculations)
        /// Only used for GermanPolar alignment mode
        /// </summary>
        public double? HourAngleLimit { get; init; }

        /// <summary>
        /// Function to check if position is within flip limits
        /// Optional - allows custom flip limit logic
        /// If null, uses default inline logic based on alignment mode
        /// </summary>
        public Func<double[], bool>? IsWithinFlipLimitsFunc { get; init; }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Create context from SkySettingsInstance (preferred for instance-based code)
        /// </summary>
        /// <param name="settings">Settings instance containing mount configuration</param>
        /// <returns>Populated AxesContext</returns>
        public static AxesContext FromSettings(SkySettingsInstance settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return new AxesContext
            {
                AlignmentMode = settings.AlignmentMode,
                MountType = settings.Mount,
                Latitude = settings.Latitude,
                SouthernHemisphere = SkyServer.SouthernHemisphere, // Still from SkyServer for now
                PolarMode = settings.PolarMode,
                LocalSiderealTime = null, // Lazy load when needed
                SideOfPier = null,
                AppAxisX = null,
                AppAxisY = null,
                AxisLimitX = settings.AxisLimitX,
                HourAngleLimit = settings.HourAngleLimit,
                IsWithinFlipLimitsFunc = null // Use default logic
            };
        }

        /// <summary>
        /// Create context from static SkySettings/SkyServer (backward compatibility)
        /// </summary>
        /// <returns>Populated AxesContext using static values</returns>
        public static AxesContext FromStatic()
        {
            return new AxesContext
            {
                AlignmentMode = SkySettings.AlignmentMode,
                MountType = SkySettings.Mount,
                Latitude = SkySettings.Latitude,
                SouthernHemisphere = SkyServer.SouthernHemisphere,
                PolarMode = SkyServer.PolarMode,
                LocalSiderealTime = SkyServer.SiderealTime,
                SideOfPier = SkyServer.SideOfPier,
                AppAxisX = SkyServer.AppAxisX,
                AppAxisY = SkyServer.AppAxisY,
                AxisLimitX = SkySettings.AxisLimitX,
                HourAngleLimit = SkySettings.HourAngleLimit,
                IsWithinFlipLimitsFunc = SkyServer.IsWithinFlipLimits // Delegate to static method
            };
        }

        /// <summary>
        /// Create context with explicit values (for testing or special cases)
        /// </summary>
        /// <param name="alignmentMode">Mount alignment mode</param>
        /// <param name="mountType">Mount hardware type</param>
        /// <param name="latitude">Observatory latitude in degrees</param>
        /// <param name="southernHemisphere">True if in southern hemisphere</param>
        /// <param name="polarMode">Polar mode for fork mounts</param>
        public AxesContext(
            AlignmentMode alignmentMode,
            MountType mountType,
            double latitude,
            bool southernHemisphere,
            PolarMode polarMode = PolarMode.Left)
        {
            AlignmentMode = alignmentMode;
            MountType = mountType;
            Latitude = latitude;
            SouthernHemisphere = southernHemisphere;
            PolarMode = polarMode;
            LocalSiderealTime = null;
            SideOfPier = null;
            AppAxisX = null;
            AppAxisY = null;
            AxisLimitX = null;
            HourAngleLimit = null;
            IsWithinFlipLimitsFunc = null;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get Local Sidereal Time, fetching from SkyServer if not set in context
        /// </summary>
        /// <returns>LST in hours (0-24)</returns>
        public double GetLst()
        {
            return LocalSiderealTime ?? SkyServer.SiderealTime;
        }

        /// <summary>
        /// Get application axis X position, fetching from SkyServer if not set
        /// </summary>
        /// <returns>App axis X in degrees</returns>
        public double GetAppAxisX()
        {
            return AppAxisX ?? SkyServer.AppAxisX;
        }

        /// <summary>
        /// Get application axis Y position, fetching from SkyServer if not set
        /// </summary>
        /// <returns>App axis Y in degrees</returns>
        public double GetAppAxisY()
        {
            return AppAxisY ?? SkyServer.AppAxisY;
        }

        /// <summary>
        /// Check if position is within flip limits
        /// </summary>
        /// <param name="position">Axes position to check [X, Y]</param>
        /// <returns>True if within flip limits, false otherwise</returns>
        /// <remarks>
        /// Uses delegate function if provided, otherwise uses default inline logic
        /// based on alignment mode and configured limits.
        /// </remarks>
        public bool IsWithinFlipLimits(double[] position)
        {
            // Use delegate if provided (backward compatibility with static method)
            if (IsWithinFlipLimitsFunc != null)
                return IsWithinFlipLimitsFunc(position);

            // Default inline logic (standalone use without static dependencies)
            var absPos0 = Math.Abs(position[0]);

            switch (AlignmentMode)
            {
                case AlignmentMode.AltAz:
                    // AltAz: check if position is within axis limit on both sides
                    var limit = AxisLimitX ?? 180.0; // Default to 180° if not set
                    return (limit >= absPos0) && (absPos0 >= 360.0 - limit);

                case AlignmentMode.Polar:
                    // Polar: check if position is outside the flip zone
                    limit = AxisLimitX ?? 180.0;
                    return (180.0 - limit <= absPos0) && (absPos0 <= limit);

                case AlignmentMode.GermanPolar:
                    // GermanPolar: check hour angle limit on both sides of meridian
                    var haLimit = HourAngleLimit ?? 15.0; // Default to 15° if not set
                    return -haLimit < absPos0 && absPos0 < haLimit ||
                           180 - haLimit < absPos0 && absPos0 < 180 + haLimit;

                default:
                    throw new ArgumentOutOfRangeException(nameof(AlignmentMode),
                        AlignmentMode,
                        "Unsupported alignment mode");
            }
        }

        /// <summary>
        /// Create new context with updated SideOfPier state
        /// </summary>
        /// <param name="sideOfPier">New side of pier state</param>
        /// <returns>New AxesContext with updated value</returns>
        public AxesContext WithSideOfPier(PointingState sideOfPier)
        {
            return this with { SideOfPier = sideOfPier };
        }

        /// <summary>
        /// Create new context with updated Local Sidereal Time
        /// </summary>
        /// <param name="lst">New LST in hours</param>
        /// <returns>New AxesContext with updated value</returns>
        public AxesContext WithLst(double lst)
        {
            return this with { LocalSiderealTime = lst };
        }

        /// <summary>
        /// Create new context with updated application axis positions
        /// </summary>
        /// <param name="appAxisX">App axis X in degrees</param>
        /// <param name="appAxisY">App axis Y in degrees</param>
        /// <returns>New AxesContext with updated values</returns>
        public AxesContext WithAppAxes(double appAxisX, double appAxisY)
        {
            return this with { AppAxisX = appAxisX, AppAxisY = appAxisY };
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validate that required properties are set to reasonable values
        /// </summary>
        /// <returns>True if context appears valid</returns>
        public bool IsValid()
        {
            return Latitude >= -90.0 && Latitude <= 90.0 &&
                   Enum.IsDefined(typeof(AlignmentMode), AlignmentMode) &&
                   Enum.IsDefined(typeof(MountType), MountType);
        }

        #endregion

        #region ToString Override

        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            return $"AxesContext: {AlignmentMode}, {MountType}, Lat={Latitude:F2}°, " +
                   $"SH={SouthernHemisphere}, PolarMode={PolarMode}";
        }

        #endregion
    }
}