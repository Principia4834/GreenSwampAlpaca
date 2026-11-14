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
using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Server.Models
{
    /// <summary>Telescope model families. The values match GSServer's Model3DType.</summary>
    public enum Model3DType
    {
        Default = 0,
        Reflector = 1,
        Refractor = 2,
        SchmidtCassegrain = 3,
        RitcheyChretien = 4,
        RitcheyChretienTruss = 5,
        DualTelescope = 6
    }

    /// <summary>
    /// The transform applied to the 3D model: rotation about Z (X), Y (Y) and X (Z), in degrees,
    /// about the centre (0, YCentre, 525) in the model's own units.
    /// </summary>
    public readonly record struct Model3DPose(double X, double Y, double Z, double YCentre);

    /// <summary>
    /// Model files and model transform for the telescope 3D view. Ported from GSServer
    /// (GS.Shared/Model3D.cs and GS.Server/Model3D/Model3DVM.cs). The model is posed from the
    /// raw mount axis positions (<see cref="TelescopeStateModel.ActualAxisX"/> / ActualAxisY), as
    /// GSServer did, so the side of pier is implicit in the axis positions and is not needed.
    /// The .obj files are served from wwwroot/model3d/models.
    /// </summary>
    public static class Model3D
    {
        public const string ModelFolder = "model3d/models/";

        /// <summary>The .obj files in wwwroot/model3d/models (the same set as GSServer).</summary>
        private static readonly HashSet<string> ModelFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Default.obj", "DefaultAltAz.obj",
            "Reflector.obj", "ReflectorAltAz.obj",
            "Refractor.obj", "RefractorAltAz.obj",
            "SchmidtCassegrain.obj", "SchmidtCassegrainAltAz.obj",
            "RitcheyChretien.obj", "RitcheyChretienAltAz.obj",
            "RitcheyChretienTruss.obj", "RitcheyChretienTrussAltAz.obj",
            "DualTelescopeAltAz.obj", "DualTelescopePolarLeft.obj", "DualTelescopePolarRight.obj"
        };

        /// <summary>File-name suffix GSServer adds for each alignment mode.</summary>
        public static string GetSuffix(AlignmentMode mode, PolarMode polarMode) => mode switch
        {
            AlignmentMode.AltAz => "AltAz",
            AlignmentMode.Polar => polarMode == PolarMode.Left ? "PolarLeft" : "PolarRight",
            _ => string.Empty
        };

        /// <summary>
        /// The model file for a model type on a mount. GSServer only has some combinations (for example
        /// Dual Telescope has no German polar file, and polar mounts only have Dual Telescope), so a missing
        /// combination falls back to the Default model for that mount, then Dual Telescope, then Default.obj.
        /// </summary>
        public static string GetModelFile(Model3DType modelType, AlignmentMode mode, PolarMode polarMode)
        {
            var suffix = GetSuffix(mode, polarMode);
            foreach (var candidate in new[]
                     {
                         $"{modelType}{suffix}.obj",
                         $"{Model3DType.Default}{suffix}.obj",
                         $"{Model3DType.DualTelescope}{suffix}.obj"
                     })
            {
                if (ModelFiles.Contains(candidate)) return candidate;
            }
            return "Default.obj";
        }

        /// <summary>The model URL relative to the application base.</summary>
        public static string GetModelUrl(Model3DType modelType, AlignmentMode mode, PolarMode polarMode)
            => ModelFolder + GetModelFile(modelType, mode, polarMode);

        /// <summary>
        /// GSServer's AxisModelOffsets default for each alignment mode (X = Dec axis, Y = RA axis, Z = tilt).
        /// German polar and Polar {90, -90, 0}; Alt-Az {0, 90, 0}.
        /// </summary>
        public static (double X, double Y, double Z) GetAxisModelOffsets(AlignmentMode mode) => mode switch
        {
            AlignmentMode.AltAz => (0.0, 90.0, 0.0),
            _ => (90.0, -90.0, 0.0)
        };

        /// <summary>GSServer's ZAxis: 90 for Alt-Az, otherwise the latitude (absolute, rounded to 2 places).</summary>
        public static double GetZAxis(AlignmentMode mode, double latitude)
            => mode == AlignmentMode.AltAz ? 90.0 : Math.Round(Math.Abs(latitude), 2);

        /// <summary>GSServer's YAxisCentre: the German polar pivot (default -102), otherwise 0.</summary>
        public static double GetYAxisCentre(AlignmentMode mode)
            => mode == AlignmentMode.GermanPolar ? -102.0 : 0.0;

        /// <summary>
        /// Port of GSServer's Model3D.RotateModel: the model rotations (YAxis, XAxis) for the raw mount axis
        /// positions ax / ay. The sign and offset differ by mount type, alignment mode and polar mode.
        /// </summary>
        public static (double YAxis, double XAxis) RotateModel(
            MountType mount, double ax, double ay, bool southernHemisphere, AlignmentMode mode, PolarMode polarMode)
        {
            var yAxis = Math.Round(ax, 3);
            double xAxis;
            switch (mode)
            {
                case AlignmentMode.Polar:
                    xAxis = mount == MountType.SkyWatcher && polarMode == PolarMode.Left
                        ? Math.Round(ay - 180, 3)
                        : Math.Round(ay * -1.0, 3);
                    break;
                case AlignmentMode.GermanPolar:
                    xAxis = mount == MountType.SkyWatcher || southernHemisphere
                        ? Math.Round(ay - 180, 3)
                        : Math.Round(ay * -1.0, 3);
                    break;
                default: // Alt-Az
                    xAxis = Math.Round(ay * -1.0, 3);
                    break;
            }
            return (yAxis, xAxis);
        }

        /// <summary>
        /// The transform GSServer applies to the model (XAxisOffset, YAxisOffset, ZAxisOffset, YAxisCentre)
        /// for the raw mount axis positions ax / ay.
        /// </summary>
        public static Model3DPose GetPose(
            AlignmentMode mode, MountType mount, PolarMode polarMode, double latitude, double ax, double ay)
        {
            var (yAxis, xAxis) = RotateModel(mount, ax, ay, latitude < 0, mode, polarMode);
            var offsets = GetAxisModelOffsets(mode);
            return new Model3DPose(
                xAxis + offsets.X,                      // XAxisOffset = XAxis + offset.X
                yAxis + offsets.Y,                      // YAxisOffset = YAxis + offset.Y
                offsets.Z - GetZAxis(mode, latitude),   // ZAxisOffset = offset.Z - ZAxis
                GetYAxisCentre(mode));
        }

        /// <summary>GSServer shows the southern compass only for equatorial mounts in the south.</summary>
        public static bool UseNorthCompass(AlignmentMode mode, double latitude)
            => latitude >= 0 || mode == AlignmentMode.AltAz;
    }
}
