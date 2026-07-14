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

using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Shared;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>
    /// Writes chart data points to disk in the legacy GSChartingLog format so the files
    /// are compatible with the same 7-day rotation that <see cref="MonitorQueue"/> manages.
    ///
    /// Log format per line:
    ///   <c>yyyy-MM-dd HH:mm:ss.fff|<chartType>|<axis>|<value></c>   — for RA/Dec points
    ///   <c>yyyy-MM-dd HH:mm:ss.fff|Pulse|<axis>|<duration>|<rate>|<rejected></c>   — for pulse points
    ///
    /// Logging is opt-in per chart window; call <see cref="StartLoggingAsync"/> /
    /// <see cref="StopLoggingAsync"/> from the chart page based on user preference.
    /// </summary>
    public sealed class ChartLoggingService
    {
        private static readonly SemaphoreSlim FileLock = new(1, 1);

        private string? _raDecFilePath;
        private string? _pulseFilePath;

        private bool _raDecActive;
        private bool _pulseActive;

        // -- Lifecycle -----------------------------------------------------------------

        /// <summary>Begins a new RA/Dec logging session, creating a timestamped file.</summary>
        public Task StartRaDecLoggingAsync()
        {
            _raDecFilePath = BuildFilePath("GSChartingLogRaDec");
            _raDecActive = true;
            return WriteLineAsync(_raDecFilePath,
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}|START|RaDec logging session");
        }

        /// <summary>Begins a new pulse logging session, creating a timestamped file.</summary>
        public Task StartPulseLoggingAsync()
        {
            _pulseFilePath = BuildFilePath("GSChartingLogPulse");
            _pulseActive = true;
            return WriteLineAsync(_pulseFilePath,
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}|START|Pulse logging session");
        }

        /// <summary>Marks the RA/Dec log session as stopped.</summary>
        public Task StopRaDecLoggingAsync()
        {
            _raDecActive = false;
            if (_raDecFilePath is null) return Task.CompletedTask;
            return WriteLineAsync(_raDecFilePath,
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}|STOP|RaDec logging session");
        }

        /// <summary>Marks the pulse log session as stopped.</summary>
        public Task StopPulseLoggingAsync()
        {
            _pulseActive = false;
            if (_pulseFilePath is null) return Task.CompletedTask;
            return WriteLineAsync(_pulseFilePath,
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}|STOP|Pulse logging session");
        }

        // -- Write helpers called by chart pages ---------------------------------------

        /// <summary>Appends a single RA/Dec axis point to the active log file.</summary>
        public Task LogRaDecPointAsync(int axis, ChartPointDto point)
        {
            if (!_raDecActive || _raDecFilePath is null) return Task.CompletedTask;

            var dt = DateTimeOffset.FromUnixTimeMilliseconds(point.TimestampMs).UtcDateTime;
            return WriteLineAsync(_raDecFilePath,
                $"{dt:yyyy-MM-dd HH:mm:ss.fff}|RaDec|{axis}|{point.Value}");
        }

        /// <summary>Appends a single pulse point to the active log file.</summary>
        public Task LogPulsePointAsync(PulsePointDto point)
        {
            if (!_pulseActive || _pulseFilePath is null) return Task.CompletedTask;

            var dt = DateTimeOffset.FromUnixTimeMilliseconds(point.TimestampMs).UtcDateTime;
            return WriteLineAsync(_pulseFilePath,
                $"{dt:yyyy-MM-dd HH:mm:ss.fff}|Pulse|{point.Axis}|{point.Duration}|{point.Rate}|{point.Rejected}");
        }

        // -- Internals -----------------------------------------------------------------

        private static string BuildFilePath(string prefix)
        {
            var fileName = $"{prefix}{DateTime.Now:yyyy-MM-dd-HH}.txt";
            return Path.Combine(GsFile.GetLogPath(), fileName);
        }

        private static async Task WriteLineAsync(string filePath, string line)
        {
            try
            {
                await FileLock.WaitAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)
                    ?? throw new InvalidOperationException("Invalid log path"));
                await using var stream = new FileStream(filePath, FileMode.Append,
                    FileAccess.Write, FileShare.Read, 4096, useAsync: true);
                await using var writer = new StreamWriter(stream);
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChartLoggingService] Write failed: {ex.Message}");
            }
            finally
            {
                FileLock.Release();
            }
        }
    }
}
