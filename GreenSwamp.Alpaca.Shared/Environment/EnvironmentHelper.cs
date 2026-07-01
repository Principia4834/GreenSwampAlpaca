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
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Shared.EnvironmentLog
{
    /// <summary>
    /// Convenience façade for environment logging.
    /// Resolves the log file path via <see cref="SettingsPathResolver"/> and
    /// delegates to <see cref="EnvironmentLogger"/>.
    /// </summary>
    public static class EnvironmentHelper
    {
        private const int KeepCount = 3;
        private const int TimeoutSeconds = 10;
        private const int SettingsZipTimeoutSeconds = 15;

        /// <summary>
        /// Write the environment log to the standard log location asynchronously.
        /// Old logs beyond the keep count are pruned after writing.
        /// </summary>
        /// <returns>
        /// The path of the created log file, or <c>null</c> if logging failed.
        /// </returns>
        public static async Task<string?> LogToDefaultLocationAsync()
        {
            try
            {
                var logPath = GetDefaultLogPath();
                await EnvironmentLogger.LogEnvironmentAsync(logPath, TimeoutSeconds).ConfigureAwait(false);
                CleanupLogs(logPath);
                return logPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the full path for the next environment log file.
        /// Format: <c>&lt;LogsRoot&gt;\GreenSwampEnv_v{version}_yyyy-MM-dd_HHmmss.log</c>
        /// </summary>
        public static string GetDefaultLogPath()
        {
            var logsRoot = SettingsPathResolver.GetLogsRoot();
            var version = SettingsPathResolver.GetAssemblyVersion();
            var fileName = $"GreenSwampEnv_v{version}_{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
            return Path.Combine(logsRoot, fileName);
        }

        /// <summary>
        /// Returns the directory where environment logs are stored.
        /// </summary>
        public static string GetLogDirectory() => SettingsPathResolver.GetLogsRoot();

        /// <summary>
        /// Zips all JSON files from the versioned AppData settings folder to the
        /// standard log location. Up to three files are retained; older ones are pruned.
        /// </summary>
        /// <returns>
        /// The path of the created zip file, or <c>null</c> if no JSON files were
        /// found or the operation failed.
        /// </returns>
        public static async Task<string?> BackupSettingsToLogsAsync()
        {
            try
            {
                var version = SettingsPathResolver.GetAssemblyVersion();
                var appDataPath = SettingsPathResolver.GetVersionedPath(version);
                if (!Directory.Exists(appDataPath))
                    return null;

                var jsonFiles = Directory.GetFiles(appDataPath, "*.json", SearchOption.TopDirectoryOnly);
                if (jsonFiles.Length == 0)
                    return null;

                var logsRoot = SettingsPathResolver.GetLogsRoot();
                Directory.CreateDirectory(logsRoot);

                var zipPath = Path.Combine(logsRoot, $"Settings_v{version}_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SettingsZipTimeoutSeconds));
                await Task.Run(() => CreateSettingsZip(zipPath, jsonFiles, cts.Token), cts.Token).ConfigureAwait(false);

                EnvironmentLogger.CleanupOldLogs(logsRoot, $"Settings_v{version}_*.zip", KeepCount);
                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        // ── Private ──────────────────────────────────────────────────────────

        private static void CleanupLogs(string logPath)
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
            {
                var version = SettingsPathResolver.GetAssemblyVersion();
                EnvironmentLogger.CleanupOldLogs(dir, $"GreenSwampEnv_v{version}_*.log", KeepCount);
            }
        }

        private static void CreateSettingsZip(string zipPath, string[] jsonFiles, CancellationToken ct)
        {
            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

            foreach (var jsonFile in jsonFiles)
            {
                ct.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(Path.GetFileName(jsonFile), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var source = new FileStream(jsonFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                source.CopyTo(entryStream);
            }
        }
    }
}
