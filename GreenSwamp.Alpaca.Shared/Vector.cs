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

namespace GreenSwamp.Alpaca.Shared
{
    /// <summary>
    /// Simple 2D vector for astronomical coordinate use.
    /// </summary>
    public struct Vector(double x, double y)
    {
        public double X { get; set; } = x;
        public double Y { get; set; } = y;

        /// <summary>
        /// Gets the squared length (magnitude) of the vector.
        /// </summary>
        public double LengthSquared => X * X + Y * Y;

        /// <summary>
        /// Gets the Euclidean length (magnitude) of the vector.
        /// </summary>
        public double Length => Math.Sqrt(LengthSquared);

        public static Vector operator -(Vector left, Vector right)
        {
            return new Vector(left.X - right.X, left.Y - right.Y);
        }
        public static Vector operator +(Vector left, Vector right)
        {
            return new Vector(left.X + right.X, left.Y + right.Y);
        }

        /// <summary>
        /// Returns a new vector with each component divided by the given scalar.
        /// </summary>
        private Vector DivideBy(double scalar)
        {
            return new Vector(X / scalar, Y / scalar);
        }

        public static Vector operator /(Vector v, double scalar)
        {
            return v.DivideBy(scalar);
        }
    }
}