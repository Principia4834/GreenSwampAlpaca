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
    /// Root object for modelsets.settings.json. Contains the active model set name
    /// and the full list of named model sets available.
    /// </summary>
    public class ModelSetCollection
    {
        public string ActiveModelSet { get; set; } = "Prototype";
        public List<ModelSetEntry> ModelSets { get; set; } = [];
    }

    /// <summary>
    /// A single named model set — maps the five required (plus optional counterpart)
    /// component asset paths relative to wwwroot/models/, and stores the user's
    /// persisted camera state for this set.
    /// </summary>
    public class ModelSetEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ModelFiles Models { get; set; } = new();
        public CameraState? Camera { get; set; }
    }

    /// <summary>
    /// Asset file paths for a model set, relative to wwwroot/models/.
    /// A null path causes the 3D viewer to render a cylinder placeholder for that stage.
    /// OtaSecondary and CounterWeight are mutually exclusive optional Stage 6 elements.
    /// </summary>
    public class ModelFiles
    {
        public string? Support { get; set; }
        public string? Structural { get; set; }
        public string? PrimaryAxis { get; set; }
        public string? SecondaryAxis { get; set; }
        public string? Ota { get; set; }
        public string? OtaSecondary { get; set; }
        public string? CounterWeight { get; set; }
    }

    /// <summary>
    /// Persisted Babylon.js ArcRotateCamera state for a model set.
    /// Saved explicitly by the user via the Save View action.
    /// Null means the viewer uses the prototype defaults on next init.
    /// </summary>
    public class CameraState
    {
        public double Alpha  { get; set; }
        public double Beta   { get; set; }
        public double Radius { get; set; }
        public CameraTarget Target { get; set; } = new();
    }

    /// <summary>Camera look-at target point in world space.</summary>
    public class CameraTarget
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
