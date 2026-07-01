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

using System.IO.Compression;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Generates backups of application settings and user documents as ZIP files.
    /// Collects JSON settings from the versioned appsettings folder and all files
    /// from the user's Documents/GreenSwamp folder recursively.
    /// </summary>
    public interface ISettingsExportService
    {
        /// <summary>Gets the current application version string (e.g. "1.2.3").</summary>
        string CurrentVersion { get; }

        /// <summary>
        /// Generates a ZIP stream containing all settings and user documents.
        /// The stream is created in-memory with maximum compression.
        /// </summary>
        /// <param name="progressCallback">Optional callback to report progress (0-100)</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Memory stream containing the ZIP file</returns>
        Task<MemoryStream> GenerateExportZipAsync(
            Action<int>? progressCallback = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about the export (estimated size and file count).
        /// </summary>
        Task<ExportInfo> GetExportInfoAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Information about the backup contents.
    /// </summary>
    public class ExportInfo
    {
        public int FileCount { get; set; }
        public long EstimatedSizeBytes { get; set; }

        public string EstimatedSizeFormatted =>
            EstimatedSizeBytes switch
            {
                < 1024 => $"{EstimatedSizeBytes} B",
                < 1024 * 1024 => $"{EstimatedSizeBytes / 1024} KB",
                < 1024 * 1024 * 1024 => $"{EstimatedSizeBytes / (1024 * 1024)} MB",
                _ => $"{EstimatedSizeBytes / (1024 * 1024 * 1024)} GB"
            };
    }

    /// <summary>
    /// Implementation of ISettingsExportService.
    /// </summary>
    public class SettingsExportService : ISettingsExportService
    {
        private readonly IVersionedSettingsService _settingsService;

        public SettingsExportService(IVersionedSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public string CurrentVersion => _settingsService.CurrentVersion;

        public async Task<MemoryStream> GenerateExportZipAsync(
            Action<int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var memoryStream = new MemoryStream();

            try
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var compressionLevel = CompressionLevel.Optimal;
                    var filesToExport = new List<(string filePath, string entryPath)>();
                    var totalFiles = 0;
                    var processedFiles = 0;

                    // 1. Add versioned settings JSON files
                    var versionedSettingsPath = Path.GetDirectoryName(_settingsService.MonitorSettingsPath);
                    if (!string.IsNullOrWhiteSpace(versionedSettingsPath) && Directory.Exists(versionedSettingsPath))
                    {
                        var jsonFiles = Directory.GetFiles(versionedSettingsPath, "*.json", SearchOption.TopDirectoryOnly);
                        foreach (var file in jsonFiles)
                        {
                            var fileName = Path.GetFileName(file);
                            filesToExport.Add((file, $"settings/{fileName}"));
                        }
                    }

                    // 2. Add user documents from GreenSwamp folder
                    var documentsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "GreenSwamp");

                    if (Directory.Exists(documentsPath))
                    {
                        var allFiles = Directory.GetFiles(documentsPath, "*", SearchOption.AllDirectories);
                        foreach (var file in allFiles)
                        {
                            var relativePath = Path.GetRelativePath(documentsPath, file);
                            var zipEntryPath = relativePath.Replace('\\', '/');
                            filesToExport.Add((file, $"GreenSwamp/{zipEntryPath}"));
                        }
                    }

                    totalFiles = filesToExport.Count;

                    foreach (var (filePath, entryPath) in filesToExport)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            using var fileStream = new FileStream(
                                filePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read);

                            var entry = archive.CreateEntry(entryPath, compressionLevel);
                            using (var entryStream = entry.Open())
                            {
                                await fileStream.CopyToAsync(entryStream, 65536, cancellationToken);
                            }
                        }
                        catch (IOException)
                        {
                            continue;
                        }

                        processedFiles++;
                        var progress = (int)((processedFiles / (double)totalFiles) * 100);
                        progressCallback?.Invoke(progress);
                    }

                } // ← archive.Dispose() fires HERE: Central Directory written at correct END position

                // Position reset AFTER archive is fully finalized
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch
            {
                memoryStream.Dispose();
                throw;
            }
        }

        public async Task<ExportInfo> GetExportInfoAsync(CancellationToken cancellationToken = default)
        {
            var exportInfo = new ExportInfo
            {
                FileCount = 0,
                EstimatedSizeBytes = 0
            };

            await Task.Run(() =>
            {
                // Count JSON files in versioned settings
                var versionedSettingsPath = Path.GetDirectoryName(_settingsService.MonitorSettingsPath);
                if (!string.IsNullOrWhiteSpace(versionedSettingsPath) && Directory.Exists(versionedSettingsPath))
                {
                    var jsonFiles = Directory.GetFiles(versionedSettingsPath, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var file in jsonFiles)
                    {
                        exportInfo.FileCount++;
                        try
                        {
                            exportInfo.EstimatedSizeBytes += new FileInfo(file).Length;
                        }
                        catch (IOException)
                        {
                            // File might be locked, skip size calculation
                        }
                    }
                }

                // Count files in Documents/GreenSwamp
                var documentsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "GreenSwamp");

                if (Directory.Exists(documentsPath))
                {
                    try
                    {
                        var allFiles = Directory.GetFiles(documentsPath, "*", SearchOption.AllDirectories);
                        foreach (var file in allFiles)
                        {
                            exportInfo.FileCount++;
                            try
                            {
                                exportInfo.EstimatedSizeBytes += new FileInfo(file).Length;
                            }
                            catch (IOException)
                            {
                                // File might be locked, skip size calculation
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Access denied to some folders, continue with what we have
                    }
                }
            }, cancellationToken);

            return exportInfo;
        }
    }
}
