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
    /// Result of settings validation with detailed error reporting
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Indicates if validation passed (no errors)
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// List of validation errors
        /// </summary>
        public List<ValidationError> Errors { get; set; } = new();

        /// <summary>
        /// List of validation warnings
        /// </summary>
        public List<ValidationError> Warnings { get; set; } = new();

        /// <summary>
        /// Error message if validation failed (legacy compatibility)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Indicates if there are any errors
        /// </summary>
        public bool HasErrors => Errors.Any();

        /// <summary>
        /// Indicates if there are any warnings
        /// </summary>
        public bool HasWarnings => Warnings.Any();

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

    /// <summary>
    /// Detailed validation error or warning
    /// </summary>
    public class ValidationError
    {
        /// <summary>
        /// Standardized error code (e.g., DUPLICATE_DEVICE_NUMBER)
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Error severity: "error", "warning", "info"
        /// </summary>
        public string Severity { get; set; } = "error";

        /// <summary>
        /// Device number if error is device-specific (null for file-level errors)
        /// </summary>
        public int? DeviceNumber { get; set; }

        /// <summary>
        /// Human-readable error message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Resolution steps to fix the error
        /// </summary>
        public string Resolution { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if this error can be automatically repaired
        /// </summary>
        public bool IsAutoRepairable { get; set; }
    }

    /// <summary>
    /// Result of automatic repair operation
    /// </summary>
    public class RepairResult
    {
        /// <summary>
        /// Indicates if repair was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Summary message about repair operation
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Path to backup file created before repair
        /// </summary>
        public string? BackupPath { get; set; }

        /// <summary>
        /// List of actions performed during repair
        /// </summary>
        public List<string> ActionsPerformed { get; set; } = new();

        /// <summary>
        /// Number of devices repaired
        /// </summary>
        public int DevicesRepaired { get; set; }

        /// <summary>
        /// Validation errors that remain after repair (require manual intervention)
        /// </summary>
        public List<ValidationError> RemainingErrors { get; set; } = new();
    }
}

