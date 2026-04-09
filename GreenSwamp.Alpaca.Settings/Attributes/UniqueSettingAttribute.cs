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

namespace GreenSwamp.Alpaca.Settings.Attributes
{
    /// <summary>
    /// Marks a SkySettings property as unique — its value is replaced with the
    /// new alignment mode's default when the alignment mode changes (Behaviour B2).
    /// Unique properties include axis limits, park positions, home positions,
    /// hour angle limits, polar mode, and other mode-sensitive values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class UniqueSettingAttribute : Attribute
    {
    }
}
