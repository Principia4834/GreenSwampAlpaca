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

using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.Mvc;

namespace GreenSwamp.Alpaca.Server.Controllers
{
    /// <summary>
    /// Provides backup functionality for settings and documents.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BackupController : ControllerBase
    {
        private readonly ISettingsBackupService _backupService;

        public BackupController(ISettingsBackupService backupService)
        {
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        }

        /// <summary>
        /// Gets information about the backup without actually creating it.
        /// </summary>
        [HttpGet("info")]
        public async Task<IActionResult> GetBackupInfo(CancellationToken cancellationToken)
        {
            try
            {
                var info = await _backupService.GetBackupInfoAsync(cancellationToken);
                return Ok(info);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Generates and downloads a backup ZIP file.
        /// The filename is GreenSwamp_{DateTime}.zip using local system time.
        /// </summary>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadBackup(CancellationToken cancellationToken)
        {
            try
            {
                var backupStream = await _backupService.GenerateBackupZipAsync(
                    progressCallback: null,
                    cancellationToken: cancellationToken);

                var fileName = $"GreenSwamp_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";

                return File(
                    backupStream,
                    "application/zip",
                    fileName,
                    enableRangeProcessing: false);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
            }
        }
    }
}
