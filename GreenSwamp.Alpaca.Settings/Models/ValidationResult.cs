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
    /// Result of settings validation
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Indicates if validation passed
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// Error message if validation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Create a successful validation result
        /// </summary>
        public static ValidationResult Success() => new() { IsValid = true };
        
        /// <summary>
        /// Create a failed validation result with error message
        /// </summary>
        public static ValidationResult Failure(string message) => new() 
        { 
            IsValid = false, 
            ErrorMessage = message 
        };
    }
}
