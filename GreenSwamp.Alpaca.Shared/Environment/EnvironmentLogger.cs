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

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Shared.EnvironmentLog
{
    /// <summary>
    /// Orchestrates writing the environment log to disk.
    /// All sections are collected synchronously inside a <see cref="Task.Run"/> call
    /// that is bounded by a cancellation timeout so it never blocks application startup.
    /// </summary>
    public static class EnvironmentLogger
    {
        /// <summary>
        /// Write the full environment log asynchronously with timeout protection.
        /// </summary>
        /// <param name="logFilePath">Absolute path of the file to create.</param>
        /// <param name="timeoutSeconds">Maximum seconds before the operation is cancelled.</param>
        public static async Task LogEnvironmentAsync(string logFilePath, int timeoutSeconds = 10)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

            try
            {
                EnsureDirectory(logFilePath);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                await Task.Run(() => WriteLog(logFilePath, cts.Token), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AppendMarker(logFilePath, "[TIMEOUT] Environment logging timed out");
            }
            catch (Exception ex)
            {
                AppendMarker(logFilePath, $"[ERROR] Environment logging failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove old log files from <paramref name="directory"/>, retaining only the
        /// <paramref name="keepCount"/> most recently created files that match
        /// <paramref name="pattern"/>.
        /// </summary>
        public static void CleanupOldLogs(string directory, string pattern, int keepCount = 3)
        {
            try
            {
                if (!Directory.Exists(directory)) return;

                var toDelete = Directory.GetFiles(directory, pattern)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(keepCount);

                foreach (var file in toDelete)
                {
                    try { file.Delete(); }
                    catch { /* ignore individual deletion failures */ }
                }
            }
            catch { /* non-critical */ }
        }

        // -- Internal ---------------------------------------------------------

        private static void WriteLog(string logFilePath, CancellationToken ct)
        {
            using var writer = new StreamWriter(logFilePath, append: false);

            writer.WriteLine("=== GREENSWAMP ALPACA ENVIRONMENT LOG ===");
            writer.WriteLine($"Generated (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"Generated (UTC):   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();

            RunSection(writer, ct, EnvironmentInfo.LogApplicationInfo);
            RunSection(writer, ct, EnvironmentInfo.LogOperatingSystemInfo);
            RunSection(writer, ct, EnvironmentInfo.LogRuntimeInfo);
            RunSection(writer, ct, EnvironmentInfo.LogProcessInfo);
            RunSection(writer, ct, PlatformEnvironmentInfo.LogAdminInfo);
            RunSection(writer, ct, PlatformEnvironmentInfo.LogSystemInfo);
            RunSection(writer, ct, PlatformEnvironmentInfo.LogCpuInfo);
            RunSection(writer, ct, PlatformEnvironmentInfo.LogMemoryInfo);
            RunSection(writer, ct, PlatformEnvironmentInfo.LogVideoInfo);
            RunSection(writer, ct, EnvironmentInfo.LogCultureInfo);
            RunSection(writer, ct, EnvironmentInfo.LogPathInfo);
            RunSection(writer, ct, EnvironmentInfo.LogDriveInfo);
            RunSection(writer, ct, EnvironmentInfo.LogNetworkInfo);

            writer.WriteLine("=== END LOG ===");
        }

        private static void RunSection(StreamWriter writer, CancellationToken ct, Action<StreamWriter> section)
        {
            if (ct.IsCancellationRequested)
            {
                writer.WriteLine("[CANCELLED] Remaining sections skipped");
                ct.ThrowIfCancellationRequested();
            }

            section(writer);
            writer.Flush();
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        private static void AppendMarker(string logFilePath, string message)
        {
            try
            {
                File.AppendAllText(logFilePath,
                    $"{System.Environment.NewLine}{message}{System.Environment.NewLine}");
            }
            catch { /* best-effort */ }
        }
    }
}
